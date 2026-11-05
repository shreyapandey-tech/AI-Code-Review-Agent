using AICodeReviewAgent.Models;
using System.Text.RegularExpressions;

namespace AICodeReviewAgent.Tools;

/// <summary>
/// Parses a raw Git unified diff into structured <see cref="DiffSummary"/> objects.
///
/// A unified diff looks like this:
///
///   diff --git a/src/Foo.cs b/src/Foo.cs
///   index abc..def 100644
///   --- a/src/Foo.cs
///   +++ b/src/Foo.cs
///   @@ -10,7 +10,9 @@ namespace Example
///    unchanged line
///   -removed line
///   +added line
///
/// Each changed file starts with "diff --git".
/// Each hunk starts with "@@".
/// </summary>
public static class DiffParserTool
{
    // Matches the "diff --git a/path b/path" header.
    private static readonly Regex DiffHeaderRegex =
        new(@"^diff --git a/(.+?) b/(.+)$", RegexOptions.Compiled);

    // Matches the "@@ -oldStart,oldLen +newStart,newLen @@" header.
    private static readonly Regex HunkHeaderRegex =
        new(@"^@@ -(\d+)(?:,\d+)? \+(\d+)(?:,\d+)? @@", RegexOptions.Compiled);

    // Matches file mode / rename metadata lines we can skip.
    private static readonly Regex MetaLineRegex =
        new(@"^(index |new file|deleted file|rename |similarity|old mode|new mode)", RegexOptions.Compiled);

    /// <summary>
    /// Entry point: parse the entire diff text and return a summary.
    /// </summary>
    /// <param name="rawDiff">The full text of the .patch / git diff output.</param>
    public static DiffSummary Parse(string rawDiff)
    {
        if (string.IsNullOrWhiteSpace(rawDiff))
            return new DiffSummary();

        var lines = rawDiff.Replace("\r\n", "\n").Split('\n');
        var changedFiles = new List<ChangedFile>();
        ChangedFile? currentFile = null;
        DiffHunk? currentHunk = null;

        // Track line numbers as we iterate through the hunk.
        int oldLineNumber = 0;
        int newLineNumber = 0;

        foreach (var rawLine in lines)
        {
            // ── New file block ──────────────────────────────────────────
            var diffMatch = DiffHeaderRegex.Match(rawLine);
            if (diffMatch.Success)
            {
                // Save the previous file (if any).
                if (currentFile != null)
                    changedFiles.Add(currentFile);

                // Start a new file entry using the "b/" path (the new path).
                currentFile = new ChangedFile
                {
                    FilePath = diffMatch.Groups[2].Value.Trim()
                };
                currentHunk = null;
                continue;
            }

            if (currentFile == null) continue; // skip leading noise before first diff header

            // ── File metadata lines ─────────────────────────────────────
            if (rawLine.StartsWith("--- "))
            {
                if (rawLine.Contains("/dev/null"))
                    currentFile.ChangeType = "Added";
                continue;
            }
            if (rawLine.StartsWith("+++ "))
            {
                if (rawLine.Contains("/dev/null"))
                    currentFile.ChangeType = "Deleted";
                continue;
            }
            if (MetaLineRegex.IsMatch(rawLine))
            {
                if (rawLine.StartsWith("deleted file")) currentFile.ChangeType = "Deleted";
                if (rawLine.StartsWith("new file"))     currentFile.ChangeType = "Added";
                if (rawLine.StartsWith("rename "))      currentFile.ChangeType = "Renamed";
                continue;
            }

            // ── Hunk header ─────────────────────────────────────────────
            var hunkMatch = HunkHeaderRegex.Match(rawLine);
            if (hunkMatch.Success)
            {
                currentHunk = new DiffHunk
                {
                    Header = rawLine,
                    StartLineNew = int.Parse(hunkMatch.Groups[2].Value)
                };
                currentFile.Hunks.Add(currentHunk);

                // Reset our running line-number counters.
                oldLineNumber = int.Parse(hunkMatch.Groups[1].Value);
                newLineNumber = currentHunk.StartLineNew;
                continue;
            }

            // ── Diff lines inside a hunk ─────────────────────────────────
            if (currentHunk == null) continue; // metadata before the first hunk

            if (rawLine.StartsWith("+") && !rawLine.StartsWith("+++"))
            {
                var diffLine = new DiffLine
                {
                    Type = '+',
                    Content = rawLine[1..], // strip the leading '+'
                    LineNumberNew = newLineNumber,
                    LineNumberOld = 0
                };
                currentHunk.Lines.Add(diffLine);
                currentFile.LinesAdded++;
                newLineNumber++;
            }
            else if (rawLine.StartsWith("-") && !rawLine.StartsWith("---"))
            {
                var diffLine = new DiffLine
                {
                    Type = '-',
                    Content = rawLine[1..], // strip the leading '-'
                    LineNumberNew = 0,
                    LineNumberOld = oldLineNumber
                };
                currentHunk.Lines.Add(diffLine);
                currentFile.LinesRemoved++;
                oldLineNumber++;
            }
            else if (rawLine.StartsWith(" ")) // context line
            {
                currentHunk.Lines.Add(new DiffLine
                {
                    Type = ' ',
                    Content = rawLine[1..],
                    LineNumberNew = newLineNumber,
                    LineNumberOld = oldLineNumber
                });
                oldLineNumber++;
                newLineNumber++;
            }
            // Lines that are entirely empty (end of hunk) are intentionally ignored.
        }

        // Don't forget the last file.
        if (currentFile != null)
            changedFiles.Add(currentFile);

        return BuildSummary(changedFiles);
    }

    // ─────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────

    private static DiffSummary BuildSummary(List<ChangedFile> files)
    {
        return new DiffSummary
        {
            TotalFilesChanged = files.Count,
            TotalLinesAdded   = files.Sum(f => f.LinesAdded),
            TotalLinesRemoved = files.Sum(f => f.LinesRemoved),
            FileNames         = files.Select(f => f.FilePath).ToList(),
            ChangedFiles      = files
        };
    }

    /// <summary>
    /// Returns only the added lines (+) from a specific file inside the parsed diff.
    /// Useful for the agent to focus on what actually changed.
    /// </summary>
    public static List<DiffLine> GetAddedLines(DiffSummary summary, string filePath)
    {
        var file = summary.ChangedFiles
            .FirstOrDefault(f => f.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase));

        return file?.Hunks
            .SelectMany(h => h.Lines)
            .Where(l => l.Type == '+')
            .ToList() ?? new List<DiffLine>();
    }

    /// <summary>
    /// Returns a compact, human-readable snippet of the diff for a given file.
    /// This is what we pass to the AI to keep prompts short.
    /// </summary>
    public static string GetFileDiffSnippet(DiffSummary summary, string filePath, int maxLines = 150)
    {
        var file = summary.ChangedFiles
            .FirstOrDefault(f => f.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase));

        if (file == null) return $"File '{filePath}' not found in diff.";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"File: {file.FilePath} [{file.ChangeType}] (+{file.LinesAdded}/-{file.LinesRemoved})");

        int lineCount = 0;
        foreach (var hunk in file.Hunks)
        {
            sb.AppendLine(hunk.Header);
            foreach (var line in hunk.Lines)
            {
                if (lineCount++ >= maxLines)
                {
                    sb.AppendLine($"... (truncated – {file.LinesAdded + file.LinesRemoved - maxLines} more lines)");
                    return sb.ToString();
                }
                sb.AppendLine($"{line.Type}{line.Content}");
            }
        }

        return sb.ToString();
    }
}

