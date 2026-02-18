namespace AzureAISearchSimulator.Core.Configuration;

/// <summary>
/// Configuration settings for local ONNX-based embedding models.
/// </summary>
public class LocalEmbeddingSettings
{
    public const string SectionName = "LocalEmbeddingSettings";

    /// <summary>
    /// Directory where ONNX model files and vocabularies are stored.
    /// Each model lives in a subdirectory named after the model (e.g., data/models/all-MiniLM-L6-v2/).
    /// </summary>
    public string ModelsDirectory { get; set; } = "./data/models";

    /// <summary>
    /// Default model name used when no model is specified in the local:// URI.
    /// </summary>
    public string DefaultModel { get; set; } = "all-MiniLM-L6-v2";

    /// <summary>
    /// Maximum number of tokens to pass to the BERT tokenizer.
    /// Input text exceeding this limit will be truncated.
    /// </summary>
    public int MaximumTokens { get; set; } = 512;

    /// <summary>
    /// Whether to L2-normalize the output embedding vectors.
    /// Recommended for cosine similarity search.
    /// </summary>
    public bool NormalizeEmbeddings { get; set; } = true;

    /// <summary>
    /// Pooling strategy for aggregating token-level embeddings into a single sentence vector.
    /// Supported values: "Mean", "Max".
    /// </summary>
    public string PoolingMode { get; set; } = "Mean";

    /// <summary>
    /// Whether to automatically download models from HuggingFace when they are not found locally.
    /// </summary>
    public bool AutoDownloadModels { get; set; } = false;

    /// <summary>
    /// Whether the BERT tokenizer should treat input as case-sensitive.
    /// Most sentence-transformer models use uncased tokenization.
    /// </summary>
    public bool CaseSensitive { get; set; } = false;
}
