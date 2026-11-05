using AICodeReviewAgent.Configuration;
using AICodeReviewAgent.Models;
using AICodeReviewAgent.Tools;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace AICodeReviewAgent.Services;

/// <summary>
/// The AI Agent that orchestrates the full code-review workflow:
///
///   1. Parse the diff.
///   2. Build a conversation (system + user messages).
///   3. Send it to the LLM with available tool definitions.
///   4. If the model calls a tool → dispatch to the right C# method → feed result back.
///   5. Repeat until the model stops calling tools (or we hit the safety limit).
///   6. Parse the final answer into structured <see cref="ReviewFinding"/> objects.
///
/// The agent NEVER executes uploaded code.  All tools operate only on the
/// text of the diff.
/// </summary>
public class AgentService
{
    private readonly LlmService _llm;
    private readonly NvidiaAiSettings _settings;

    // JSON options used only for the tool-argument parsing step.
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public AgentService(LlmService llm, IOptions<NvidiaAiSettings> settings)
    {
        _llm      = llm;
        _settings = settings.Value;
    }

    // ─────────────────────────────────────────────────────────────
    //  Public entry point
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Run a full code-review agent session for the given diff.
    /// </summary>
    public async Task<ReviewResponse> RunReviewAsync(
        ReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        // ── Step 1: Parse the raw diff ──────────────────────────────
        var diffSummary = DiffParserTool.Parse(request.DiffContent);

        if (diffSummary.TotalFilesChanged == 0)
        {
            return new ReviewResponse
            {
                Summary  = "No changed files were found in the provided diff.",
                Findings = new List<ReviewFinding>()
            };
        }

        // ── Step 2: Build the initial conversation ──────────────────
        var messages = BuildInitialMessages(request, diffSummary);

        // ── Step 3: Define the tools the model may call ─────────────
        var tools = BuildToolDefinitions();

        // ── Step 4: Agent loop ──────────────────────────────────────
        int toolCallCount = 0;
        string? finalAnswer = null;

        for (int iteration = 0; iteration < _settings.MaxToolCallIterations; iteration++)
        {
            var llmRequest = new ChatCompletionRequest
            {
                Messages    = messages,
                Tools       = tools,
                ToolChoice  = "auto",
                Temperature = 0.1,   // low temp → more deterministic output
                MaxTokens   = 4096
            };

            var response = await _llm.CompleteAsync(llmRequest, cancellationToken);
            var choice   = response.Choices.FirstOrDefault();

            if (choice == null) break;

            var assistantMessage = choice.Message;
            assistantMessage.Role = "assistant";
            messages.Add(assistantMessage); // always keep the assistant turn in history

            // ── Did the model request tool calls? ───────────────────
            if (choice.FinishReason == "tool_calls" && assistantMessage.ToolCalls?.Count > 0)
            {
                foreach (var toolCall in assistantMessage.ToolCalls)
                {
                    toolCallCount++;

                    // Execute the requested tool and get a text result.
                    var toolResult = ExecuteTool(toolCall, diffSummary);

                    // Feed the result back as a "tool" role message.
                    messages.Add(new ChatMessage
                    {
                        Role       = "tool",
                        ToolCallId = toolCall.Id,
                        Name       = toolCall.Function.Name,
                        Content    = toolResult
                    });
                }

                // Continue the loop so the model can process the tool results.
                continue;
            }

            // ── Model produced a final text answer ──────────────────
            finalAnswer = assistantMessage.Content;
            break;
        }

        // If we exhausted iterations without a final answer, ask once more
        // without tools so the model is forced to respond in text.
        if (finalAnswer == null)
        {
            var fallbackRequest = new ChatCompletionRequest
            {
                Messages    = messages,
                ToolChoice  = "none", // disable further tool calls
                Temperature = 0.1,
                MaxTokens   = 4096
            };
            var fallback = await _llm.CompleteAsync(fallbackRequest, cancellationToken);
            finalAnswer  = fallback.Choices.FirstOrDefault()?.Message.Content ?? string.Empty;
        }

        // ── Step 5: Parse the AI's final JSON answer ────────────────
        var findings = ParseFindingsFromAnswer(finalAnswer);

        return new ReviewResponse
        {
            Summary           = BuildSummaryText(diffSummary, findings),
            Findings          = findings,
            ToolCallsExecuted = toolCallCount
        };
    }

    // ─────────────────────────────────────────────────────────────
    //  Message construction
    // ─────────────────────────────────────────────────────────────

    private static List<ChatMessage> BuildInitialMessages(
        ReviewRequest request,
        DiffSummary   summary)
    {
        // The system prompt explains the agent's role, output format, and guardrails.
        var systemPrompt = """
            You are an expert AI code reviewer.  Your job is to analyze a Git diff and produce
            structured, actionable feedback.

            ## Rules
            - Use the provided tools to gather evidence BEFORE writing findings.
            - Always call "scan_security_issues", "check_test_coverage", and "analyze_code_quality"
              first, then call "get_file_info" for any file that needs closer inspection.
            - Base your findings ONLY on what is visible in the diff.
              Never invent problems that are not supported by the code.
            - Distinguish between "Confirmed" (issue is definitely present) and
              "Potential" (possible issue, needs context to confirm).
            - For each finding, produce a JSON object with these exact keys:
                severity      : "High" | "Medium" | "Low"
                category      : "Bug" | "Security" | "CodeQuality" | "MissingTests" | "Style" | "Improvement"
                fileName      : relative path string
                lineNumber    : integer (0 if unknown)
                explanation   : clear one-to-two sentence explanation
                suggestedFix  : concrete suggestion or code snippet
                confidence    : "Confirmed" | "Potential"

            ## Output format
            After gathering tool results, respond with ONLY valid JSON – no markdown fences,
            no prose before or after.  Use this structure:

            {
              "findings": [ ... ]
            }

            If there are no findings, return: { "findings": [] }
            """;

        // Compact diff overview for the user message.
        var fileList = string.Join("\n", summary.FileNames.Select(f => $"  - {f}"));
        var diffSnippet = string.Join("\n\n",
            summary.ChangedFiles.Select(f =>
                DiffParserTool.GetFileDiffSnippet(summary, f.FilePath)));

        var userMessage = $"""
            Please review the following Git diff.

            Summary:
            - Files changed: {summary.TotalFilesChanged}
            - Lines added:   {summary.TotalLinesAdded}
            - Lines removed: {summary.TotalLinesRemoved}
            - Files:
            {fileList}

            {(request.RepositoryContext is { Length: > 0 }
                ? $"Repository context:\n{request.RepositoryContext}\n\n"
                : "")}
            Diff content:
            {diffSnippet}
            """;

        return new List<ChatMessage>
        {
            new() { Role = "system", Content = systemPrompt },
            new() { Role = "user",   Content = userMessage  }
        };
    }

    // ─────────────────────────────────────────────────────────────
    //  Tool definitions (sent to the model so it knows what to call)
    // ─────────────────────────────────────────────────────────────

    private static List<ToolDefinition> BuildToolDefinitions() => new()
    {
        new ToolDefinition
        {
            Function = new ToolFunctionDefinition
            {
                Name        = "scan_security_issues",
                Description = "Scan all added lines in the diff for security anti-patterns " +
                              "(hard-coded secrets, SQL injection, weak crypto, etc.).",
                Parameters  = new ToolParameters
                {
                    Properties = new Dictionary<string, ToolParameterProperty>(), // no args needed
                    Required   = new List<string>()
                }
            }
        },
        new ToolDefinition
        {
            Function = new ToolFunctionDefinition
            {
                Name        = "check_test_coverage",
                Description = "Check whether test files were modified alongside production code changes.",
                Parameters  = new ToolParameters
                {
                    Properties = new Dictionary<string, ToolParameterProperty>(),
                    Required   = new List<string>()
                }
            }
        },
        new ToolDefinition
        {
            Function = new ToolFunctionDefinition
            {
                Name        = "analyze_code_quality",
                Description = "Scan added lines for code-quality issues: magic numbers, " +
                              "TODO markers, long lines, debug output, and empty catch blocks.",
                Parameters  = new ToolParameters
                {
                    Properties = new Dictionary<string, ToolParameterProperty>(),
                    Required   = new List<string>()
                }
            }
        },
        new ToolDefinition
        {
            Function = new ToolFunctionDefinition
            {
                Name        = "get_file_info",
                Description = "Get metadata about a specific file in the diff " +
                              "(language, change type, hunk count, lines added/removed).",
                Parameters  = new ToolParameters
                {
                    Properties = new Dictionary<string, ToolParameterProperty>
                    {
                        ["filePath"] = new ToolParameterProperty
                        {
                            Type        = "string",
                            Description = "Relative path of the file to inspect (as it appears in the diff)."
                        }
                    },
                    Required = new List<string> { "filePath" }
                }
            }
        },
        new ToolDefinition
        {
            Function = new ToolFunctionDefinition
            {
                Name        = "get_file_diff_snippet",
                Description = "Get a readable snippet of the diff for a specific file, " +
                              "showing all added/removed lines with their line numbers.",
                Parameters  = new ToolParameters
                {
                    Properties = new Dictionary<string, ToolParameterProperty>
                    {
                        ["filePath"] = new ToolParameterProperty
                        {
                            Type        = "string",
                            Description = "Relative path of the file."
                        }
                    },
                    Required = new List<string> { "filePath" }
                }
            }
        }
    };

    // ─────────────────────────────────────────────────────────────
    //  Tool dispatcher
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Routes a tool call requested by the model to the correct C# method.
    /// Returns the tool result as a plain-text string.
    /// </summary>
    private static string ExecuteTool(ToolCall toolCall, DiffSummary summary)
    {
        try
        {
            // Parse the JSON arguments the model passed.
            var args = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                toolCall.Function.Arguments, JsonOpts) ?? new Dictionary<string, JsonElement>();

            return toolCall.Function.Name switch
            {
                "scan_security_issues"  => CodeAnalysisTool.ScanForSecurityIssues(summary),
                "check_test_coverage"   => CodeAnalysisTool.CheckTestCoverage(summary),
                "analyze_code_quality"  => CodeAnalysisTool.AnalyzeCodeQuality(summary),
                "get_file_info"         => CodeAnalysisTool.GetFileInfo(summary,
                                              GetStringArg(args, "filePath")),
                "get_file_diff_snippet" => DiffParserTool.GetFileDiffSnippet(summary,
                                              GetStringArg(args, "filePath")),
                _ => $"Unknown tool: {toolCall.Function.Name}"
            };
        }
        catch (Exception ex)
        {
            // Never let a tool failure crash the agent loop.
            return $"Tool '{toolCall.Function.Name}' failed: {ex.Message}";
        }
    }

    private static string GetStringArg(Dictionary<string, JsonElement> args, string key)
    {
        if (args.TryGetValue(key, out var element))
            return element.GetString() ?? string.Empty;
        return string.Empty;
    }

    // ─────────────────────────────────────────────────────────────
    //  Response parsing
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts a list of <see cref="ReviewFinding"/> from the AI's JSON answer.
    /// Falls back gracefully if the model produced malformed JSON.
    /// </summary>
    private static List<ReviewFinding> ParseFindingsFromAnswer(string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer))
            return new List<ReviewFinding>();

        try
        {
            // Strip any accidental markdown fences the model may have added.
            var json = answer.Trim();
            if (json.StartsWith("```"))
            {
                var firstNewline = json.IndexOf('\n');
                var lastFence    = json.LastIndexOf("```");
                if (firstNewline > 0 && lastFence > firstNewline)
                    json = json[(firstNewline + 1)..lastFence].Trim();
            }

            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("findings", out var findingsElement))
                return new List<ReviewFinding>();

            var findings = new List<ReviewFinding>();

            foreach (var item in findingsElement.EnumerateArray())
            {
                findings.Add(new ReviewFinding
                {
                    Severity      = GetJsonString(item, "severity",     "Low"),
                    Category      = GetJsonString(item, "category",     "CodeQuality"),
                    FileName      = GetJsonString(item, "fileName",      string.Empty),
                    LineNumber    = GetJsonInt(item,    "lineNumber",    0),
                    Explanation   = GetJsonString(item, "explanation",   string.Empty),
                    SuggestedFix  = GetJsonString(item, "suggestedFix",  string.Empty),
                    Confidence    = GetJsonString(item, "confidence",    "Potential")
                });
            }

            return findings;
        }
        catch
        {
            // If the model didn't return valid JSON, surface the raw text as a single finding.
            return new List<ReviewFinding>
            {
                new()
                {
                    Severity    = "Low",
                    Category    = "CodeQuality",
                    FileName    = "N/A",
                    Explanation = "The AI model returned a non-JSON response. Raw content: " + answer,
                    SuggestedFix = "Re-run the review; the model may have hit its token limit.",
                    Confidence  = "Potential"
                }
            };
        }
    }

    private static string GetJsonString(JsonElement el, string prop, string fallback)
    {
        if (el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String)
            return v.GetString() ?? fallback;
        return fallback;
    }

    private static int GetJsonInt(JsonElement el, string prop, int fallback)
    {
        if (el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number)
            return v.GetInt32();
        return fallback;
    }

    // ─────────────────────────────────────────────────────────────
    //  Summary text
    // ─────────────────────────────────────────────────────────────

    private static string BuildSummaryText(DiffSummary diff, List<ReviewFinding> findings)
    {
        var high   = findings.Count(f => f.Severity == "High");
        var medium = findings.Count(f => f.Severity == "Medium");
        var low    = findings.Count(f => f.Severity == "Low");

        return $"Review complete. {diff.TotalFilesChanged} file(s) analysed, " +
               $"{diff.TotalLinesAdded} lines added, {diff.TotalLinesRemoved} lines removed. " +
               $"Found {findings.Count} issue(s): {high} High, {medium} Medium, {low} Low.";
    }
}

