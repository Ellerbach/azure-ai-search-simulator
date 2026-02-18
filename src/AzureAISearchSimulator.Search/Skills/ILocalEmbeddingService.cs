using AzureAISearchSimulator.Core.Models;

namespace AzureAISearchSimulator.Search.Skills;

/// <summary>
/// Service for generating text embeddings locally using ONNX models.
/// </summary>
public interface ILocalEmbeddingService : IDisposable
{
    /// <summary>
    /// Generates an embedding for the given enriched document using the skill pipeline.
    /// This is called by AzureOpenAIEmbeddingSkillExecutor when a local:// resourceUri is detected.
    /// </summary>
    /// <param name="modelName">The model name (e.g., "all-MiniLM-L6-v2").</param>
    /// <param name="skill">The skill definition with inputs/outputs.</param>
    /// <param name="document">The enriched document to process.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Skill execution result.</returns>
    Task<SkillExecutionResult> GenerateEmbeddingAsync(
        string modelName,
        Skill skill,
        EnrichedDocument document,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates an embedding vector for a single text string.
    /// </summary>
    /// <param name="modelName">The model name (e.g., "all-MiniLM-L6-v2").</param>
    /// <param name="text">The input text to embed.</param>
    /// <returns>The embedding vector as a float array.</returns>
    float[] GenerateEmbedding(string modelName, string text);

    /// <summary>
    /// Checks whether a model is available (files exist on disk).
    /// </summary>
    /// <param name="modelName">The model name.</param>
    /// <returns>True if the model files exist.</returns>
    bool IsModelAvailable(string modelName);

    /// <summary>
    /// Gets the output dimension count for a loaded model.
    /// </summary>
    /// <param name="modelName">The model name.</param>
    /// <returns>The number of dimensions in the model's output embeddings.</returns>
    int GetModelDimensions(string modelName);

    /// <summary>
    /// Lists all models available in the models directory.
    /// </summary>
    /// <returns>Collection of available model names.</returns>
    IReadOnlyList<string> ListAvailableModels();
}
