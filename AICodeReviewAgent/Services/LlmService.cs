using AICodeReviewAgent.Configuration;
using AICodeReviewAgent.Models;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AICodeReviewAgent.Services;

/// <summary>
/// Thin wrapper around the NVIDIA NIM (OpenAI-compatible) chat-completions
/// endpoint.  Its only job is to serialise a <see cref="ChatCompletionRequest"/>,
/// POST it to the API, and return the parsed <see cref="ChatCompletionResponse"/>.
///
/// Every other concern (agent loop, tool dispatch, review formatting) lives in
/// <see cref="AgentService"/>.
/// </summary>
public class LlmService
{
    private readonly HttpClient _http;
    private readonly NvidiaAiSettings _settings;

    // We share one JsonSerializerOptions instance for efficiency.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition      = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented               = false
    };

    public LlmService(HttpClient http, IOptions<NvidiaAiSettings> settings)
    {
        _http     = http;
        _settings = settings.Value;

        // Set up the base address and auth header; HttpClient is injected via AddHttpClient.
        _http.BaseAddress = new Uri(_settings.BaseUrl.TrimEnd('/') + "/");
        if (!string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _settings.ApiKey);
        }
    }

    /// <summary>
    /// Sends a chat-completion request and returns the model's response.
    /// Throws <see cref="HttpRequestException"/> on non-2xx responses.
    /// </summary>
    public async Task<ChatCompletionResponse> CompleteAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            throw new InvalidOperationException(
                "NVIDIA NIM API key is not configured. Please configure your key using .NET User Secrets:\n" +
                "  dotnet user-secrets set \"NvidiaAI:ApiKey\" \"nvapi-your-key-here\"\n" +
                "or set the environment variable 'NvidiaAI__ApiKey'.");
        }

        // Always stamp the configured model name so callers don't have to.
        request.Model = _settings.ModelName;

        var json    = JsonSerializer.Serialize(request, JsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var httpResponse = await _http.PostAsync("chat/completions", content, cancellationToken);

        if (!httpResponse.IsSuccessStatusCode)
        {
            var errorBody = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"NVIDIA NIM API returned {(int)httpResponse.StatusCode} " +
                $"{httpResponse.StatusCode}: {errorBody}");
        }

        var responseJson = await httpResponse.Content.ReadAsStringAsync(cancellationToken);

        var result = JsonSerializer.Deserialize<ChatCompletionResponse>(responseJson, JsonOptions);
        return result ?? throw new InvalidOperationException("Received empty response from NVIDIA NIM API.");
    }
}

