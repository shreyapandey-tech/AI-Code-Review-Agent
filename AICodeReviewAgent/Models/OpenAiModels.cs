using System.Text.Json.Serialization;

namespace AICodeReviewAgent.Models;

// ─────────────────────────────────────────────────────────────
//  OpenAI-compatible API request / response models
//  (NVIDIA NIM exposes an OpenAI-compatible endpoint, so the
//   same DTOs work for both providers.)
// ─────────────────────────────────────────────────────────────

/// <summary>
/// The JSON body we POST to the chat-completions endpoint.
/// </summary>
public class ChatCompletionRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("messages")]
    public List<ChatMessage> Messages { get; set; } = new();

    /// <summary>
    /// Tool definitions the model may choose to call.
    /// We only send this when tools are available.
    /// </summary>
    [JsonPropertyName("tools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ToolDefinition>? Tools { get; set; }

    /// <summary>
    /// "auto" lets the model decide; "none" disables tool calls.
    /// </summary>
    [JsonPropertyName("tool_choice")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolChoice { get; set; }

    [JsonPropertyName("temperature")]
    public double Temperature { get; set; } = 0.1;

    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; set; } = 4096;
}

/// <summary>
/// One message in the conversation history.
/// </summary>
public class ChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty; // system | user | assistant | tool

    [JsonPropertyName("content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Content { get; set; }

    /// <summary>Present when role == "assistant" and the model requested tool calls.</summary>
    [JsonPropertyName("tool_calls")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ToolCall>? ToolCalls { get; set; }

    /// <summary>Present when role == "tool" (tool result message).</summary>
    [JsonPropertyName("tool_call_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolCallId { get; set; }

    /// <summary>Present when role == "tool" (name of the tool that produced the result).</summary>
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }
}

// ─────────────────────────────────────────────────────────────
//  Tool definition (sent to the model once per request)
// ─────────────────────────────────────────────────────────────

public class ToolDefinition
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    [JsonPropertyName("function")]
    public ToolFunctionDefinition Function { get; set; } = new();
}

public class ToolFunctionDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("parameters")]
    public ToolParameters Parameters { get; set; } = new();
}

public class ToolParameters
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "object";

    [JsonPropertyName("properties")]
    public Dictionary<string, ToolParameterProperty> Properties { get; set; } = new();

    [JsonPropertyName("required")]
    public List<string> Required { get; set; } = new();
}

public class ToolParameterProperty
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "string";

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
}

// ─────────────────────────────────────────────────────────────
//  Tool call (returned by the model inside the assistant message)
// ─────────────────────────────────────────────────────────────

public class ToolCall
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    [JsonPropertyName("function")]
    public ToolCallFunction Function { get; set; } = new();
}

public class ToolCallFunction
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>JSON string of the arguments the model wants to pass.</summary>
    [JsonPropertyName("arguments")]
    public string Arguments { get; set; } = "{}";
}

// ─────────────────────────────────────────────────────────────
//  Chat completion response
// ─────────────────────────────────────────────────────────────

public class ChatCompletionResponse
{
    [JsonPropertyName("choices")]
    public List<ChatChoice> Choices { get; set; } = new();
}

public class ChatChoice
{
    [JsonPropertyName("message")]
    public ChatMessage Message { get; set; } = new();

    /// <summary>"stop" | "tool_calls" | "length"</summary>
    [JsonPropertyName("finish_reason")]
    public string FinishReason { get; set; } = string.Empty;
}

