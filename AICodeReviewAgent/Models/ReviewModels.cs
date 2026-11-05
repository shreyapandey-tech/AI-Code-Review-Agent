namespace AICodeReviewAgent.Models;

// ─────────────────────────────────────────────────────────────
//  Models that the frontend sends to / receives from the API
// ─────────────────────────────────────────────────────────────

/// <summary>
/// The body the frontend POSTs to /api/review.
/// Either DiffContent (pasted text) or the form-upload path will
/// populate this before the controller hands it to the service.
/// </summary>
public class ReviewRequest
{
    /// <summary>Raw Git diff/patch text.</summary>
    public string DiffContent { get; set; } = string.Empty;

    /// <summary>
    /// Optional repository context (e.g. file tree as plain text).
    /// Helps the agent decide whether test files exist.
    /// </summary>
    public string? RepositoryContext { get; set; }
}

// ─────────────────────────────────────────────────────────────
//  Models returned to the client
// ─────────────────────────────────────────────────────────────

/// <summary>
/// Top-level response sent back to the client after the agent finishes.
/// </summary>
public class ReviewResponse
{
    /// <summary>Human-readable headline for the review session.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>All findings the agent discovered.</summary>
    public List<ReviewFinding> Findings { get; set; } = new();

    /// <summary>Total tool calls executed during the agent loop.</summary>
    public int ToolCallsExecuted { get; set; }

    /// <summary>ISO-8601 timestamp of when the review was completed.</summary>
    public string ReviewedAt { get; set; } = DateTime.UtcNow.ToString("o");
}

/// <summary>
/// One specific finding from the AI code review.
/// </summary>
public class ReviewFinding
{
    /// <summary>High | Medium | Low</summary>
    public string Severity { get; set; } = "Low";

    /// <summary>
    /// One of: Bug, Security, CodeQuality, MissingTests, Style, Improvement
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Relative file path where the issue was spotted.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Specific line number in the diff (0 = unknown).</summary>
    public int LineNumber { get; set; }

    /// <summary>Clear explanation of what the issue is.</summary>
    public string Explanation { get; set; } = string.Empty;

    /// <summary>Concrete suggestion for fixing or improving the code.</summary>
    public string SuggestedFix { get; set; } = string.Empty;

    /// <summary>
    /// "Potential" – the AI suspects an issue but cannot confirm it.
    /// "Confirmed" – the issue is definitely present based on the diff alone.
    /// </summary>
    public string Confidence { get; set; } = "Potential";
}

