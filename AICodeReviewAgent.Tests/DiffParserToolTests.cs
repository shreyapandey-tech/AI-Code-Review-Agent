using AICodeReviewAgent.Tools;

namespace AICodeReviewAgent.Tests;

/// <summary>
/// Unit tests for <see cref="DiffParserTool"/>.
///
/// Tests are intentionally simple and self-contained – no mocks,
/// no external dependencies.  They verify the parser against small
/// inline diff strings so a beginner can read and understand them easily.
/// </summary>
public class DiffParserToolTests
{
    // ─────────────────────────────────────────────────────────────
    //  Helpers – inline diff strings
    // ─────────────────────────────────────────────────────────────

    private const string SimpleDiff = """
        diff --git a/src/Calculator.cs b/src/Calculator.cs
        index a1b2c3d..e4f5g6h 100644
        --- a/src/Calculator.cs
        +++ b/src/Calculator.cs
        @@ -1,5 +1,8 @@
         namespace MyApp
         {
             public class Calculator
             {
        -        public int Add(int a, int b) => a + b;
        +        public int Add(int a, int b)
        +        {
        +            return a + b;
        +        }
             }
         }
        """;

    private const string MultiFileDiff = """
        diff --git a/src/Foo.cs b/src/Foo.cs
        index 000..111 100644
        --- a/src/Foo.cs
        +++ b/src/Foo.cs
        @@ -1,3 +1,4 @@
         using System;
        +using System.Linq;
         
         namespace MyApp { }
        diff --git a/src/Bar.cs b/src/Bar.cs
        index 000..222 100644
        --- a/src/Bar.cs
        +++ b/src/Bar.cs
        @@ -1,2 +1,3 @@
         public class Bar
         {
        +    public void Hello() { }
         }
        """;

    private const string NewFileDiff = """
        diff --git a/src/NewFile.cs b/src/NewFile.cs
        new file mode 100644
        index 0000000..abcdef1
        --- /dev/null
        +++ b/src/NewFile.cs
        @@ -0,0 +1,3 @@
        +public class NewFile
        +{
        +}
        """;

    private const string DeletedFileDiff = """
        diff --git a/src/OldFile.cs b/src/OldFile.cs
        deleted file mode 100644
        index abcdef1..0000000
        --- a/src/OldFile.cs
        +++ /dev/null
        @@ -1,3 +0,0 @@
        -public class OldFile
        -{
        -}
        """;

    // ─────────────────────────────────────────────────────────────
    //  Parse – basic counts
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_EmptyString_ReturnsEmptySummary()
    {
        var result = DiffParserTool.Parse(string.Empty);

        Assert.Equal(0, result.TotalFilesChanged);
        Assert.Equal(0, result.TotalLinesAdded);
        Assert.Equal(0, result.TotalLinesRemoved);
        Assert.Empty(result.ChangedFiles);
    }

    [Fact]
    public void Parse_SimpleDiff_ParsesOneFile()
    {
        var result = DiffParserTool.Parse(SimpleDiff);

        Assert.Equal(1, result.TotalFilesChanged);
    }

    [Fact]
    public void Parse_SimpleDiff_CountsLinesCorrectly()
    {
        var result = DiffParserTool.Parse(SimpleDiff);

        // We removed 1 line and added 4 lines.
        Assert.Equal(4, result.TotalLinesAdded);
        Assert.Equal(1, result.TotalLinesRemoved);
    }

    [Fact]
    public void Parse_SimpleDiff_ExtractsCorrectFilePath()
    {
        var result = DiffParserTool.Parse(SimpleDiff);

        Assert.Contains("src/Calculator.cs", result.FileNames);
    }

    [Fact]
    public void Parse_MultiFileDiff_ParsesBothFiles()
    {
        var result = DiffParserTool.Parse(MultiFileDiff);

        Assert.Equal(2, result.TotalFilesChanged);
        Assert.Contains("src/Foo.cs", result.FileNames);
        Assert.Contains("src/Bar.cs", result.FileNames);
    }

    [Fact]
    public void Parse_MultiFileDiff_SumsLinesAcrossFiles()
    {
        var result = DiffParserTool.Parse(MultiFileDiff);

        // Foo.cs: +1 line;  Bar.cs: +1 line  → total 2 added, 0 removed
        Assert.Equal(2, result.TotalLinesAdded);
        Assert.Equal(0, result.TotalLinesRemoved);
    }

    // ─────────────────────────────────────────────────────────────
    //  Change-type detection
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_NewFileDiff_SetsChangeTypeToAdded()
    {
        var result = DiffParserTool.Parse(NewFileDiff);

        var file = result.ChangedFiles.Single();
        Assert.Equal("Added", file.ChangeType);
    }

    [Fact]
    public void Parse_DeletedFileDiff_SetsChangeTypeToDeleted()
    {
        var result = DiffParserTool.Parse(DeletedFileDiff);

        var file = result.ChangedFiles.Single();
        Assert.Equal("Deleted", file.ChangeType);
    }

    // ─────────────────────────────────────────────────────────────
    //  Line-type classification
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_SimpleDiff_ClassifiesAddedLines()
    {
        var result  = DiffParserTool.Parse(SimpleDiff);
        var file    = result.ChangedFiles.Single();
        var allLines = file.Hunks.SelectMany(h => h.Lines).ToList();

        Assert.Equal(4, allLines.Count(l => l.Type == '+'));
    }

    [Fact]
    public void Parse_SimpleDiff_ClassifiesRemovedLines()
    {
        var result  = DiffParserTool.Parse(SimpleDiff);
        var file    = result.ChangedFiles.Single();
        var allLines = file.Hunks.SelectMany(h => h.Lines).ToList();

        Assert.Equal(1, allLines.Count(l => l.Type == '-'));
    }

    [Fact]
    public void Parse_SimpleDiff_ClassifiesContextLines()
    {
        var result  = DiffParserTool.Parse(SimpleDiff);
        var file    = result.ChangedFiles.Single();
        var allLines = file.Hunks.SelectMany(h => h.Lines).ToList();

        // The diff has 4 unchanged context lines (namespace {, class {, blank, }).
        Assert.True(allLines.Count(l => l.Type == ' ') >= 3);
    }

    // ─────────────────────────────────────────────────────────────
    //  GetAddedLines helper
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void GetAddedLines_ReturnsOnlyAddedLinesForFile()
    {
        var summary = DiffParserTool.Parse(SimpleDiff);
        var added   = DiffParserTool.GetAddedLines(summary, "src/Calculator.cs");

        Assert.All(added, l => Assert.Equal('+', l.Type));
        Assert.Equal(4, added.Count);
    }

    [Fact]
    public void GetAddedLines_NonexistentFile_ReturnsEmptyList()
    {
        var summary = DiffParserTool.Parse(SimpleDiff);
        var added   = DiffParserTool.GetAddedLines(summary, "does/not/exist.cs");

        Assert.Empty(added);
    }

    // ─────────────────────────────────────────────────────────────
    //  GetFileDiffSnippet helper
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void GetFileDiffSnippet_ExistingFile_ContainsFilePath()
    {
        var summary = DiffParserTool.Parse(SimpleDiff);
        var snippet = DiffParserTool.GetFileDiffSnippet(summary, "src/Calculator.cs");

        Assert.Contains("src/Calculator.cs", snippet);
    }

    [Fact]
    public void GetFileDiffSnippet_NonexistentFile_ReturnsNotFoundMessage()
    {
        var summary = DiffParserTool.Parse(SimpleDiff);
        var snippet = DiffParserTool.GetFileDiffSnippet(summary, "missing/file.cs");

        Assert.Contains("not found", snippet, StringComparison.OrdinalIgnoreCase);
    }

    // ─────────────────────────────────────────────────────────────
    //  Hunk-header parsing
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_SimpleDiff_ParsesHunkHeader()
    {
        var result = DiffParserTool.Parse(SimpleDiff);
        var hunk   = result.ChangedFiles.Single().Hunks.Single();

        // The hunk starts at new line 1.
        Assert.Equal(1, hunk.StartLineNew);
        Assert.Contains("@@", hunk.Header);
    }
}

