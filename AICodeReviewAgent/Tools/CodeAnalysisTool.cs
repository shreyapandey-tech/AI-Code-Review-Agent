using AICodeReviewAgent.Models;
using System.Text.RegularExpressions;

namespace AICodeReviewAgent.Tools;

/// <summary>
/// A collection of lightweight static analysis helpers that the AI agent
/// can invoke as "tools" to gather factual evidence before forming its
/// final review.  These methods NEVER execute the uploaded code – they
/// only inspect the text of the diff.
///
/// Each method accepts a <see cref="DiffSummary"/> (the parsed diff) and
/// optional extra parameters, then returns a plain-text report that the
/// agent adds to the conversation as a tool-result message.
/// </summary>
public static class CodeAnalysisTool
{
    // ─────────────────────────────────────────────────────────────
    //  1. Security pattern scan
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Scans every *added* line across all changed files for well-known
    /// security anti-patterns (hard-coded secrets, SQL injection risks,
    /// disabled security flags, etc.).
    /// </summary>
    public static string ScanForSecurityIssues(DiffSummary summary)
    {
        // Each pattern has: (regex, human-readable description, severity)
        var patterns = new (Regex Pattern, string Description, string Severity)[]
        {
            (new Regex(@"password\s*=\s*[""'][^""']+[""']",          RegexOptions.IgnoreCase), "Hard-coded password",                       "High"),
            (new Regex(@"(api_?key|apikey|secret)\s*=\s*[""'][^""']+[""']", RegexOptions.IgnoreCase), "Hard-coded API key or secret",       "High"),
            (new Regex(@"connectionstring\s*=\s*[""'][^""']+[""']",   RegexOptions.IgnoreCase), "Hard-coded connection string",             "High"),
            (new Regex(@"SELECT\s.+\s+FROM\s.+\s*\+",                 RegexOptions.IgnoreCase), "Potential SQL injection (string concat)",   "High"),
            (new Regex(@"\.ExecuteCommand\s*\(",                       RegexOptions.IgnoreCase), "Direct command execution",                 "High"),
            (new Regex(@"AllowAnyOrigin|cors\.AllowAll",               RegexOptions.IgnoreCase), "CORS allows any origin",                   "Medium"),
            (new Regex(@"ValidateAntiForgeryToken\s*=\s*false",        RegexOptions.IgnoreCase), "CSRF protection disabled",                 "Medium"),
            (new Regex(@"\.UseHsts\(\)|\.UseHttpsRedirection\(\)",     RegexOptions.IgnoreCase), "HTTPS enforcement found (positive check)", "Low"),
            (new Regex(@"TODO.*(?:auth|security|permission)",          RegexOptions.IgnoreCase), "TODO related to auth/security",            "Medium"),
            (new Regex(@"catch\s*\(\s*Exception",                      RegexOptions.IgnoreCase), "Broad catch(Exception) may hide errors",   "Low"),
            (new Regex(@"MD5|SHA1(?!_)",                               RegexOptions.IgnoreCase), "Weak hashing algorithm (MD5/SHA1)",        "Medium"),
        };

        var findings = new List<string>();

        foreach (var file in summary.ChangedFiles)
        {
            var addedLines = file.Hunks
                .SelectMany(h => h.Lines)
                .Where(l => l.Type == '+')
                .ToList();

            foreach (var line in addedLines)
            {
                foreach (var (pattern, description, severity) in patterns)
                {
                    if (pattern.IsMatch(line.Content))
                    {
                        findings.Add(
                            $"[{severity}] {file.FilePath} line {line.LineNumberNew}: {description}\n" +
                            $"  Code: {line.Content.Trim()}");
                    }
                }
            }
        }

        if (findings.Count == 0)
            return "No obvious security anti-patterns detected in added lines.";

        return "Security scan results:\n" + string.Join("\n", findings);
    }

    // ─────────────────────────────────────────────────────────────
    //  2. Test-coverage check
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Checks whether the diff includes any test file changes alongside
    /// production code changes.  Returns a plain-English report.
    /// </summary>
    public static string CheckTestCoverage(DiffSummary summary)
    {
        // Heuristic: a file is a "test" file if its path or name contains
        // common test-related keywords.
        var testKeywords = new[] { "test", "spec", "fixture", "mock", "stub", "fake" };

        bool IsTestFile(string path) =>
            testKeywords.Any(k => path.Contains(k, StringComparison.OrdinalIgnoreCase));

        var productionFiles = summary.ChangedFiles
            .Where(f => !IsTestFile(f.FilePath))
            .ToList();

        var testFiles = summary.ChangedFiles
            .Where(f => IsTestFile(f.FilePath))
            .ToList();

        if (productionFiles.Count == 0)
            return "Only test files were changed – no production code coverage concern.";

        var report = new System.Text.StringBuilder();
        report.AppendLine($"Production files changed: {productionFiles.Count}");
        report.AppendLine($"Test files changed:        {testFiles.Count}");
        report.AppendLine();

        if (testFiles.Count == 0)
        {
            report.AppendLine("⚠ No test files were modified alongside production changes.");
            report.AppendLine("Consider adding or updating unit tests for:");
            foreach (var pf in productionFiles)
                report.AppendLine($"  - {pf.FilePath}");
        }
        else
        {
            report.AppendLine("Test files included in this diff:");
            foreach (var tf in testFiles)
                report.AppendLine($"  ✓ {tf.FilePath}");
        }

        return report.ToString();
    }

    // ─────────────────────────────────────────────────────────────
    //  3. Code-complexity / style quick-check
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Performs a lightweight code-quality scan on added lines:
    /// long methods, deeply-nested code, magic numbers, dead code
    /// markers, and very long lines.
    /// </summary>
    public static string AnalyzeCodeQuality(DiffSummary summary)
    {
        var findings = new List<string>();

        foreach (var file in summary.ChangedFiles)
        {
            var addedLines = file.Hunks
                .SelectMany(h => h.Lines)
                .Where(l => l.Type == '+')
                .ToList();

            foreach (var line in addedLines)
            {
                var text = line.Content;

                // Very long lines hurt readability.
                if (text.Length > 120)
                    findings.Add($"[Style] {file.FilePath}:{line.LineNumberNew} – Line exceeds 120 characters ({text.Length} chars).");

                // Magic numbers (excluding 0 and 1 which are rarely magic).
                if (Regex.IsMatch(text, @"\b[2-9]\d{2,}\b") && !text.TrimStart().StartsWith("//"))
                    findings.Add($"[Style] {file.FilePath}:{line.LineNumberNew} – Possible magic number. Consider a named constant.");

                // TODO / FIXME / HACK markers in new code.
                if (Regex.IsMatch(text, @"\b(TODO|FIXME|HACK|XXX)\b", RegexOptions.IgnoreCase))
                    findings.Add($"[CodeQuality] {file.FilePath}:{line.LineNumberNew} – Unresolved marker: {text.Trim()}");

                // Empty catch blocks.
                if (Regex.IsMatch(text, @"catch\s*\([^)]*\)\s*\{?\s*\}?") && text.Trim().Length < 25)
                    findings.Add($"[Bug] {file.FilePath}:{line.LineNumberNew} – Possible empty catch block; exceptions may be silently swallowed.");

                // Console.WriteLine / System.out.println left in production code.
                if (Regex.IsMatch(text, @"Console\.(Write|WriteLine)|System\.out\.print", RegexOptions.IgnoreCase))
                    findings.Add($"[Style] {file.FilePath}:{line.LineNumberNew} – Debug output (Console.Write) found; use a logger instead.");

                // Nullable dereference: calling method directly on result of a method that may return null.
                if (Regex.IsMatch(text, @"\?\.\w+\(\)") is false &&
                    Regex.IsMatch(text, @"\w+\(\)\.\w+") &&
                    !text.TrimStart().StartsWith("//"))
                {
                    // Only flag if there is no null-check nearby (very simple heuristic).
                    if (!text.Contains("??") && !text.Contains("?.") && !text.Contains("!= null") && !text.Contains("is not null"))
                        findings.Add($"[Bug] {file.FilePath}:{line.LineNumberNew} – Possible null-dereference; consider null-check or null-conditional operator.");
                }
            }
        }

        if (findings.Count == 0)
            return "No code-quality issues detected in the added lines.";

        return "Code quality findings:\n" + string.Join("\n", findings);
    }

    // ─────────────────────────────────────────────────────────────
    //  4. File-context inspector
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns basic metadata about a specific file in the diff:
    /// change type, hunk count, lines added/removed, and the file extension.
    /// </summary>
    public static string GetFileInfo(DiffSummary summary, string filePath)
    {
        var file = summary.ChangedFiles
            .FirstOrDefault(f => f.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase));

        if (file == null)
            return $"File '{filePath}' was not found in the parsed diff.";

        var ext  = Path.GetExtension(file.FilePath);
        var lang = ext switch
        {
            ".cs"   => "C#",
            ".ts"   => "TypeScript",
            ".js"   => "JavaScript",
            ".py"   => "Python",
            ".java" => "Java",
            ".go"   => "Go",
            ".rb"   => "Ruby",
            ".cpp" or ".cc" or ".cxx" => "C++",
            ".h"    => "C/C++ header",
            ".sql"  => "SQL",
            ".json" => "JSON",
            ".yaml" or ".yml" => "YAML",
            ".md"   => "Markdown",
            _       => "Unknown"
        };

        return $"""
            File:        {file.FilePath}
            Language:    {lang}
            Change type: {file.ChangeType}
            Hunks:       {file.Hunks.Count}
            Lines added: {file.LinesAdded}
            Lines removed: {file.LinesRemoved}
            """;
    }
}

