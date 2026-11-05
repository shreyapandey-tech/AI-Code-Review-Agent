namespace AICodeReviewAgent.Models;

// ─────────────────────────────────────────────────────────────
//  Models that describe a parsed Git diff
// ─────────────────────────────────────────────────────────────

/// <summary>
/// Represents one file that was changed in the Git diff.
/// </summary>
public class ChangedFile
{
    /// <summary>Relative path of the file inside the repository.</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>What happened to the file: Modified, Added, Deleted, Renamed.</summary>
    public string ChangeType { get; set; } = "Modified";

    /// <summary>All hunks (blocks of changes) belonging to this file.</summary>
    public List<DiffHunk> Hunks { get; set; } = new();

    /// <summary>Total lines added across all hunks.</summary>
    public int LinesAdded { get; set; }

    /// <summary>Total lines removed across all hunks.</summary>
    public int LinesRemoved { get; set; }
}

/// <summary>
/// Represents a single "hunk" – a contiguous block of changed lines
/// prefixed by an @@ header in a Git diff.
/// </summary>
public class DiffHunk
{
    /// <summary>The @@ line that opens this hunk (e.g. "@@ -10,7 +10,9 @@").</summary>
    public string Header { get; set; } = string.Empty;

    /// <summary>First line in the new version of the file covered by this hunk.</summary>
    public int StartLineNew { get; set; }

    /// <summary>All lines inside the hunk (context, added, removed).</summary>
    public List<DiffLine> Lines { get; set; } = new();
}

/// <summary>
/// Represents a single line inside a diff hunk.
/// </summary>
public class DiffLine
{
    /// <summary>'+' for added, '-' for removed, ' ' for unchanged context.</summary>
    public char Type { get; set; }

    /// <summary>The actual source-code text of the line.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>Line number in the new file (0 means the line was deleted).</summary>
    public int LineNumberNew { get; set; }

    /// <summary>Line number in the old file (0 means the line was added).</summary>
    public int LineNumberOld { get; set; }
}

/// <summary>
/// Convenience summary returned by the diff-parsing tool so the agent
/// can quickly understand the scope of the changes without reading every line.
/// </summary>
public class DiffSummary
{
    public int TotalFilesChanged { get; set; }
    public int TotalLinesAdded { get; set; }
    public int TotalLinesRemoved { get; set; }
    public List<string> FileNames { get; set; } = new();
    public List<ChangedFile> ChangedFiles { get; set; } = new();
}

