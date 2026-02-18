using System.Collections.Concurrent;
using System.Numerics.Tensors;
using AzureAISearchSimulator.Core.Configuration;
using AzureAISearchSimulator.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.Tokenizers;

namespace AzureAISearchSimulator.Search.Skills;

/// <summary>
/// Generates text embeddings locally using BERT-based ONNX models.
/// Thread-safe: InferenceSession is safe for concurrent use; tokenizer state is per-call.
/// </summary>
public sealed class LocalOnnxEmbeddingService : ILocalEmbeddingService
{
    private static readonly RunOptions s_runOptions = new();
    private static readonly string[] s_inputNames = ["input_ids", "attention_mask", "token_type_ids"];

    private readonly LocalEmbeddingSettings _settings;
    private readonly ILogger<LocalOnnxEmbeddingService> _logger;
    private readonly ConcurrentDictionary<string, LoadedModel> _models = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _loadLock = new();
    private bool _disposed;

    public LocalOnnxEmbeddingService(
        IOptions<LocalEmbeddingSettings> settings,
        ILogger<LocalOnnxEmbeddingService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<SkillExecutionResult> GenerateEmbeddingAsync(
        string modelName,
        Skill skill,
        EnrichedDocument document,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modelName))
        {
            modelName = _settings.DefaultModel;
        }

        if (!IsModelAvailable(modelName))
        {
            var modelsDir = Path.GetFullPath(_settings.ModelsDirectory);
            return SkillExecutionResult.Failed(
                $"Local embedding model '{modelName}' not found. " +
                $"Expected files at: {Path.Combine(modelsDir, modelName, "model.onnx")} and vocab.txt. " +
                $"Run scripts/Download-EmbeddingModel.ps1 -ModelName {modelName} to download it.");
        }

        try
        {
            var context = skill.Context ?? "/document";
            var contexts = document.GetMatchingPaths(context).ToList();
            var warnings = new List<string>();

            var textInput = skill.Inputs.FirstOrDefault(i => i.Name == "text");
            if (textInput?.Source == null)
            {
                return SkillExecutionResult.Failed("AzureOpenAIEmbeddingSkill (local mode) requires 'text' input");
            }

            foreach (var ctx in contexts)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var sourcePath = ResolveSourcePath(ctx, textInput.Source);
                var text = document.GetValue<string>(sourcePath);

                if (string.IsNullOrEmpty(text))
                {
                    warnings.Add($"Empty text input at {sourcePath}, skipping local embedding generation");
                    continue;
                }

                // Truncate if excessively long (BERT models have token limits)
                const int maxChars = 30000;
                if (text.Length > maxChars)
                {
                    text = text[..maxChars];
                    warnings.Add($"Text truncated to {maxChars} characters for local embedding generation");
                }

                var embedding = GenerateEmbedding(modelName, text);

                var embeddingOutput = skill.Outputs.FirstOrDefault(o => o.Name == "embedding");
                var targetName = embeddingOutput?.TargetName ?? "embedding";
                var outputPath = $"{ctx}/{targetName}";

                document.SetValue(outputPath, embedding);

                _logger.LogDebug("Generated local embedding with {Dimensions} dimensions using model {Model}",
                    embedding.Length, modelName);
            }

            return warnings.Count > 0
                ? SkillExecutionResult.SucceededWithWarnings(warnings.ToArray())
                : SkillExecutionResult.Succeeded();
        }
        catch (OperationCanceledException)
        {
            return SkillExecutionResult.Failed("Local embedding generation was cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating local embedding with model {Model}", modelName);
            return SkillExecutionResult.Failed($"Local embedding error: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public float[] GenerateEmbedding(string modelName, string text)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(modelName))
        {
            modelName = _settings.DefaultModel;
        }

        var model = GetOrLoadModel(modelName);
        int maxTokens = _settings.MaximumTokens;

        // Tokenize using Microsoft.ML.Tokenizers BertTokenizer
        IReadOnlyList<int> tokenIds = model.Tokenizer.EncodeToIds(
            text, maxTokens, addSpecialTokens: true,
            out _, out _);

        int tokenCount = tokenIds.Count;

        if (tokenCount == 0)
        {
            // Return zero vector if tokenization yields nothing
            return new float[model.Dimensions];
        }

        // Convert int tokens to long[] for ONNX Runtime
        long[] inputIds = new long[tokenCount];
        long[] attentionMask = new long[tokenCount];
        long[] tokenTypeIds = new long[tokenCount]; // all zeros for single-sentence

        for (int i = 0; i < tokenCount; i++)
        {
            inputIds[i] = tokenIds[i];
            attentionMask[i] = 1; // all tokens are real (no padding)
        }

        var shape = new long[] { 1, tokenCount };

        using var inputIdsOrt = OrtValue.CreateTensorValueFromMemory(
            OrtMemoryInfo.DefaultInstance, inputIds.AsMemory(), shape);
        using var attMaskOrt = OrtValue.CreateTensorValueFromMemory(
            OrtMemoryInfo.DefaultInstance, attentionMask.AsMemory(), shape);
        using var typeIdsOrt = OrtValue.CreateTensorValueFromMemory(
            OrtMemoryInfo.DefaultInstance, tokenTypeIds.AsMemory(), shape);

        var inputValues = new OrtValue[] { inputIdsOrt, attMaskOrt, typeIdsOrt };

        using var outputs = model.Session.Run(
            s_runOptions, s_inputNames, inputValues, model.Session.OutputNames);

        ReadOnlySpan<float> rawOutput = outputs[0].GetTensorDataAsSpan<float>();
        float[] result = Pool(rawOutput, tokenCount, model.Dimensions);

        if (_settings.NormalizeEmbeddings)
        {
            float norm = TensorPrimitives.Norm(result);
            if (norm > 0)
            {
                TensorPrimitives.Divide(result, norm, result);
            }
        }

        return result;
    }

    /// <inheritdoc />
    public bool IsModelAvailable(string modelName)
    {
        var modelDir = GetModelDirectory(modelName);
        return File.Exists(Path.Combine(modelDir, "model.onnx"))
            && File.Exists(Path.Combine(modelDir, "vocab.txt"));
    }

    /// <inheritdoc />
    public int GetModelDimensions(string modelName)
    {
        var model = GetOrLoadModel(modelName);
        return model.Dimensions;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ListAvailableModels()
    {
        var modelsDir = Path.GetFullPath(_settings.ModelsDirectory);
        if (!Directory.Exists(modelsDir))
        {
            return Array.Empty<string>();
        }

        return Directory.GetDirectories(modelsDir)
            .Where(d =>
                File.Exists(Path.Combine(d, "model.onnx")) &&
                File.Exists(Path.Combine(d, "vocab.txt")))
            .Select(d => Path.GetFileName(d)!)
            .OrderBy(n => n)
            .ToList()
            .AsReadOnly();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var kvp in _models)
        {
            kvp.Value.Session.Dispose();
        }
        _models.Clear();
    }

    // ── Private helpers ──────────────────────────────────────────────────

    private LoadedModel GetOrLoadModel(string modelName)
    {
        if (_models.TryGetValue(modelName, out var cached))
        {
            return cached;
        }

        lock (_loadLock)
        {
            // Double-check after acquiring lock
            if (_models.TryGetValue(modelName, out cached))
            {
                return cached;
            }

            var modelDir = GetModelDirectory(modelName);
            var onnxPath = Path.Combine(modelDir, "model.onnx");
            var vocabPath = Path.Combine(modelDir, "vocab.txt");

            if (!File.Exists(onnxPath))
            {
                throw new FileNotFoundException(
                    $"ONNX model file not found: {onnxPath}. " +
                    $"Run: scripts/Download-EmbeddingModel.ps1 -ModelName {modelName}");
            }

            if (!File.Exists(vocabPath))
            {
                throw new FileNotFoundException(
                    $"Vocabulary file not found: {vocabPath}. " +
                    $"Run: scripts/Download-EmbeddingModel.ps1 -ModelName {modelName}");
            }

            _logger.LogInformation("Loading local ONNX embedding model '{Model}' from {Path}", modelName, modelDir);

            var sessionOptions = new SessionOptions();
            sessionOptions.InterOpNumThreads = 1;
            sessionOptions.IntraOpNumThreads = Environment.ProcessorCount;

            var session = new InferenceSession(onnxPath, sessionOptions);
            int dimensions = session.OutputMetadata.First().Value.Dimensions.Last();

            var tokenizer = BertTokenizer.Create(vocabPath, new BertOptions
            {
                LowerCaseBeforeTokenization = !_settings.CaseSensitive
            });

            var loaded = new LoadedModel(session, tokenizer, dimensions);
            _models.TryAdd(modelName, loaded);

            _logger.LogInformation(
                "Loaded local embedding model '{Model}': {Dimensions} dimensions", modelName, dimensions);

            return loaded;
        }
    }

    private string GetModelDirectory(string modelName)
    {
        return Path.Combine(Path.GetFullPath(_settings.ModelsDirectory), modelName);
    }

    private float[] Pool(ReadOnlySpan<float> modelOutput, int tokenCount, int dimensions)
    {
        int totalElements = modelOutput.Length;
        int embeddings = Math.DivRem(totalElements, dimensions, out int leftover);

        if (leftover != 0)
        {
            throw new InvalidOperationException(
                $"Model output length {totalElements} is not a multiple of {dimensions} dimensions.");
        }

        float[] result = new float[dimensions];

        if (embeddings <= 1)
        {
            modelOutput.Slice(0, dimensions).CopyTo(result);
            return result;
        }

        bool useMean = _settings.PoolingMode.Equals("Mean", StringComparison.OrdinalIgnoreCase);

        if (useMean)
        {
            // Mean pooling: sum all token embeddings, divide by count
            TensorPrimitives.Add(
                modelOutput.Slice(0, dimensions),
                modelOutput.Slice(dimensions, dimensions),
                result);

            for (int pos = dimensions * 2; pos < totalElements; pos += dimensions)
            {
                TensorPrimitives.Add(result, modelOutput.Slice(pos, dimensions), result);
            }

            TensorPrimitives.Divide(result, embeddings, result);
        }
        else
        {
            // Max pooling
            TensorPrimitives.Max(
                modelOutput.Slice(0, dimensions),
                modelOutput.Slice(dimensions, dimensions),
                result);

            for (int pos = dimensions * 2; pos < totalElements; pos += dimensions)
            {
                TensorPrimitives.Max(result, modelOutput.Slice(pos, dimensions), result);
            }
        }

        return result;
    }

    private static string ResolveSourcePath(string context, string source)
    {
        if (source.StartsWith('/'))
        {
            return source;
        }
        return $"{context}/{source}";
    }

    // ── Inner types ──────────────────────────────────────────────────────

    private sealed record LoadedModel(
        InferenceSession Session,
        BertTokenizer Tokenizer,
        int Dimensions);
}
