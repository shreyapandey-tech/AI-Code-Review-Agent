namespace AICodeReviewAgent.Configuration;

/// <summary>
/// Strongly-typed settings loaded from appsettings.json (or User Secrets / env-vars).
/// Bound to the "NvidiaAI" section.
/// </summary>
public class NvidiaAiSettings
{
    /// <summary>
    /// Your NVIDIA NIM API key.
    /// Store in User Secrets (key: "NvidiaAI:ApiKey") or the
    /// environment variable "NvidiaAI__ApiKey" – never hard-code it here.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>NVIDIA NIM OpenAI-compatible base URL.</summary>
    public string BaseUrl { get; set; } = "https://api.groq.com/openai/v1/chat/completions";

    /// <summary>Model name to use for the code review (must support function/tool calling).</summary>
    public string ModelName { get; set; } = "openai/gpt-oss-120b";

    /// <summary>
    /// Safety limit: how many tool-call iterations the agent loop is
    /// allowed to run before it's forced to produce a final answer.
    /// Prevents runaway loops and excessive token usage.
    /// </summary>
    public int MaxToolCallIterations { get; set; } = 5;
}

