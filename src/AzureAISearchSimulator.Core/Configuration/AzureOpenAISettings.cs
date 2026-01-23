namespace AzureAISearchSimulator.Core.Configuration;

/// <summary>
/// Configuration settings for Azure OpenAI integration.
/// </summary>
public class AzureOpenAISettings
{
    public const string SectionName = "AzureOpenAISettings";

    /// <summary>
    /// Azure OpenAI endpoint URL.
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// Azure OpenAI API key.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Deployment name for the embedding model.
    /// </summary>
    public string DeploymentName { get; set; } = "text-embedding-ada-002";

    /// <summary>
    /// Number of dimensions for the embedding model.
    /// </summary>
    public int ModelDimensions { get; set; } = 1536;

    /// <summary>
    /// Timeout for API calls in seconds.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;
}
