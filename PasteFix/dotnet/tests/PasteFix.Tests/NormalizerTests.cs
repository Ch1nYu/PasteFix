using System;
using System.IO;

namespace PasteFix.Tests;

/// <summary>
/// Normalizer does exactly two things: remove the indent common to every line
/// (<see cref="Normalizer.Dedent"/>), and join continuation lines into single
/// commands (<see cref="Normalizer.Normalize"/>, which also dedents).
///
/// Everything else the old implementation did — stripping prompts, deleting `#`
/// comments, removing markdown fences, rewriting line endings — was removed on
/// 2026-08-09. See docs/records/PLAN-2026-08-09.md for why each rule was dropped.
/// </summary>
public class NormalizerTests
{
    private static string Example(string name)
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "examples", $"{name}.txt")).TrimEnd();

    // ---------------------------------------------------------------- Dedent

    [Fact]
    public void DedentRemovesIndentCommonToEveryLine()
    {
        Assert.Equal("a\n    b\nc", Normalizer.Dedent("    a\n        b\n    c"));
    }

    [Fact]
    public void DedentKeepsRelativeIndentOfPythonCode()
    {
        string input = "class A:\n    def f(self):\n        return 1";
        Assert.Equal(input, Normalizer.Dedent(input));
    }

    [Fact]
    public void DedentKeepsYamlStructure()
    {
        string input = "services:\n  api:\n    build:\n      context: .";
        Assert.Equal(input, Normalizer.Dedent(input));
    }

    [Fact]
    public void DedentDoesNothingWhenFirstLineHasNoIndent()
    {
        // The common prefix is empty, so nothing is stripped.
        string input = "a\n    b";
        Assert.Equal(input, Normalizer.Dedent(input));
    }

    [Fact]
    public void DedentIgnoresBlankLinesWhenComputingCommonIndent()
    {
        Assert.Equal("a\n\nb", Normalizer.Dedent("    a\n\n    b"));
    }

    [Fact]
    public void DedentTrimsBlankLinesAtBothEnds()
    {
        // A trailing newline makes a terminal execute the last line on paste.
        Assert.Equal("git status", Normalizer.Dedent("\n\ngit status\n\n"));
    }

    [Fact]
    public void DedentStripsCommonTabIndent()
    {
        Assert.Equal("a\n\tb", Normalizer.Dedent("\ta\n\t\tb"));
    }

    [Fact]
    public void DedentStripsOnlyTheCommonPrefixOfMixedWhitespace()
    {
        // "\t " is common to both lines; the second line's extra space stays.
        Assert.Equal("a\n b", Normalizer.Dedent("\t a\n\t  b"));
    }

    [Fact]
    public void DedentDoesNotStripWhenWhitespaceKindsDiffer()
    {
        // Tab and space share no common prefix, so neither line is touched.
        string input = "\ta\n b";
        Assert.Equal(input, Normalizer.Dedent(input));
    }

    [Fact]
    public void DedentDoesNotJoinContinuations()
    {
        // Merging is Normalize's job, not Dedent's — AutoMonitor must stay lossless.
        Assert.Equal("npm install \\\n--save express", Normalizer.Dedent("npm install \\\n--save express"));
    }

    [Fact]
    public void DedentReturnsEmptyForBlankInput()
    {
        Assert.Equal("", Normalizer.Dedent("   \n\n  "));
    }

    // -------------------------------------------------- missed left margin

    // A mouse selection that starts at the first character of line 1 misses the
    // left margin every following line carries. The common prefix is then empty
    // and an ordinary dedent can do nothing. Normalize strips it when line 1 is
    // syntactically finished, because then the indent below has no grammar to
    // justify it. Dedent never does — AutoMonitor runs Dedent.

    [Fact]
    public void MarginIsStrippedWhenFirstLineIsAFinishedStatement()
    {
        // Python rejects the input outright: "unexpected indent" on line 2.
        Assert.Equal("x = 1\ny = 2", Normalizer.Normalize("x = 1\n  y = 2"));
    }

    [Fact]
    public void MarginIsStrippedFromAFlatScript()
    {
        Assert.Equal(
            "$ErrorActionPreference = 'Stop'\nGet-Process\nWrite-Host done",
            Normalizer.Normalize("$ErrorActionPreference = 'Stop'\n  Get-Process\n  Write-Host done"));
    }

    [Fact]
    public void MarginIsKeptAfterAColonBecauseItOpensABlock()
    {
        // The body is uniformly shifted, which is still valid Python. Stripping
        // here would flatten a real class instead.
        string input = "class A:\n      def f(self):\n          return 1";
        Assert.Equal(input, Normalizer.Normalize(input));
    }

    [Fact]
    public void MarginIsKeptInsideAnUnclosedBracket()
    {
        string input = "if (x) {\n      y = 1\n  }";
        Assert.Equal(input, Normalizer.Normalize(input));
    }

    [Fact]
    public void MarginIsKeptInsideAHereString()
    {
        string input = "$t = @\"\n      line1\n      line2\n\"@";
        Assert.Equal(input, Normalizer.Normalize(input));
    }

    [Fact]
    public void MarginIsKeptAfterAYamlKey()
    {
        string input = "services:\n    api:\n      build: .";
        Assert.Equal(input, Normalizer.Normalize(input));
    }

    [Fact]
    public void MarginIsKeptAfterAFullwidthColon()
    {
        // Found by sweeping this repository: a Chinese "用法：" heading followed
        // by an indented command list was the one false repair in 380 samples.
        string input = "用法：\n  . tools\\manual-cases.ps1\n  Show-Cases";
        Assert.Equal(input, Normalizer.Normalize(input));
    }

    [Fact]
    public void MarginRepairDoesNotFireWhenLineOneIsIndented()
    {
        // The selection caught the margin; the ordinary dedent already took it.
        Assert.Equal("if (x) {\n    y = 1\n}", Normalizer.Normalize("  if (x) {\n      y = 1\n  }"));
    }

    [Fact]
    public void MarginRepairRewritesAYamlPlainScalar()
    {
        // Known gap, asserted rather than hidden: `- a` opens nothing, so the
        // indent below looks unjustified, but it belongs to the scalar "a b".
        Assert.Equal("- a\nb", Normalizer.Normalize("- a\n  b"));
    }

    [Fact]
    public void DedentNeverRepairsTheMargin()
    {
        // AutoMonitor's path stays structure-preserving.
        string input = "x = 1\n  y = 2";
        Assert.Equal(input, Normalizer.Dedent(input));
    }

    [Fact]
    public void MarginRepairIsIdempotent()
    {
        string once = Normalizer.Normalize("x = 1\n  y = 2");
        Assert.Equal(once, Normalizer.Normalize(once));
    }

    [Fact]
    public void MarginRepairCombinesWithMerging()
    {
        Assert.Equal(
            "docker run -e FOO=bar image",
            Normalizer.Normalize("docker run \\\n      -e FOO=bar \\\n      image"));
    }

    [Fact]
    public void MarginRepairNeedsMoreThanOneLine()
    {
        Assert.Equal("git status", Normalizer.Normalize("git status"));
    }

    // ------------------------------------------------- line endings preserved

    [Fact]
    public void PreservesLfLineEndings()
    {
        Assert.Equal("a\nb", Normalizer.Dedent("  a\n  b"));
    }

    [Fact]
    public void PreservesCrLfLineEndings()
    {
        Assert.Equal("a\r\nb", Normalizer.Dedent("  a\r\n  b"));
    }

    [Fact]
    public void PreservesCrLfWhenMerging()
    {
        Assert.Equal("npm install --save", Normalizer.Normalize("npm install \\\r\n--save"));
    }

    // ------------------------------------------------------ continuation merge

    [Fact]
    public void JoinsBackslashContinuations()
    {
        Assert.Equal(
            "npm install --save express",
            Normalizer.Normalize("npm install \\\n--save \\\nexpress"));
    }

    [Fact]
    public void JoinsBacktickContinuations()
    {
        Assert.Equal(
            "Get-ChildItem -Path . -Filter *.txt",
            Normalizer.Normalize("Get-ChildItem `\n    -Path . `\n    -Filter *.txt"));
    }

    [Fact]
    public void JoinsDoubleAmpersandContinuations()
    {
        Assert.Equal("git add . && git commit", Normalizer.Normalize("git add . &&\ngit commit"));
    }

    [Fact]
    public void JoinsTrailingPipeContinuations()
    {
        // BUGREPORT: a blank line after `Get-Process |` produced EmptyPipeElement.
        Assert.Equal(
            "Get-Process | Select-Object -First 3",
            Normalizer.Normalize("Get-Process |\n    Select-Object -First 3"));
    }

    [Fact]
    public void SkipsBlankLinesWhileJoiningContinuations()
    {
        // BUGREPORT-2026-08-08: `Get-Process |` followed by a blank line gave
        // "An empty pipe element is not allowed." A blank line after a
        // continuation marker is never meaningful, so it is dropped.
        Assert.Equal(
            "Get-Process | Select-Object -First 3",
            Normalizer.Normalize("Get-Process |\n\nSelect-Object -First 3"));
    }

    [Fact]
    public void KeepsBlankLinesThatAreNotInsideAContinuation()
    {
        string input = "echo one\n\necho two";
        Assert.Equal(input, Normalizer.Normalize(input));
    }

    [Fact]
    public void MergedContinuationDoesNotKeepLeadingIndent()
    {
        Assert.Equal(
            "docker run -e FOO=bar image",
            Normalizer.Normalize("docker run \\\n    -e FOO=bar \\\n    image"));
    }

    [Fact]
    public void MergesFixtureExample()
    {
        Assert.Equal(
            Example("shell-continuation.expected"),
            Normalizer.Normalize(Example("shell-continuation.input")));
    }

    // ------------------------------------------------------------- merge guards

    [Fact]
    public void DoesNotMergeInsideUnclosedBracket()
    {
        // `if (a &&` is code, not a shell continuation: the && sits inside an open paren.
        string input = "if (a &&\n    b) { doThing(); }";
        Assert.Equal(input, Normalizer.Normalize(input));
    }

    [Fact]
    public void DoesNotMergeCMacroBackslashInsideBraces()
    {
        string input = "#define FOO(x) do { \\\n    bar(x); \\\n} while (0)";
        Assert.Equal(input, Normalizer.Normalize(input));
    }

    [Fact]
    public void DoesNotMergeWindowsPathEndingInBackslash()
    {
        // `C:\temp\` is a path, not a bash continuation. A real continuation
        // always has whitespace in front of the marker.
        string input = "cd C:\\temp\\\ndir";
        Assert.Equal(input, Normalizer.Normalize(input));
    }

    [Fact]
    public void DoesNotMergeBackslashAttachedToAToken()
    {
        string input = "echo a\\\nb";
        Assert.Equal(input, Normalizer.Normalize(input));
    }

    [Fact]
    public void DoesNotMergeTrailingBackslashInsideSingleQuotedString()
    {
        // Merging would silently change the string's contents.
        string input = "$s = 'a \\\nb'";
        Assert.Equal(input, Normalizer.Normalize(input));
    }

    [Fact]
    public void DoesNotMergeInsidePowerShellHereString()
    {
        string input = "$t = @\"\nline one \\\nline two\n\"@";
        Assert.Equal(input, Normalizer.Normalize(input));
    }

    [Fact]
    public void DoesNotMergeInsideBashHeredoc()
    {
        string input = "cat <<EOF\nline one \\\nline two\nEOF";
        Assert.Equal(input, Normalizer.Normalize(input));
    }

    [Fact]
    public void ResumesMergingAfterHereStringCloses()
    {
        Assert.Equal(
            "$t = @\"\nbody\n\"@\necho a && echo b",
            Normalizer.Normalize("$t = @\"\nbody\n\"@\necho a &&\necho b"));
    }

    // ------------------------------------------------------ no removed behaviour

    [Fact]
    public void KeepsShellPrompts()
    {
        // Prompt stripping was removed: the user avoids copying prompts in the
        // first place, so the rule had no real scenario backing it.
        Assert.Equal("$ npm install", Normalizer.Normalize("$ npm install"));
    }

    [Fact]
    public void KeepsMarkdownBlockquote()
    {
        Assert.Equal("> quoted line", Normalizer.Normalize("> quoted line"));
    }

    [Fact]
    public void KeepsMarkdownFence()
    {
        string input = "```bash\ngit status\n```";
        Assert.Equal(input, Normalizer.Normalize(input));
    }

    [Fact]
    public void KeepsCommentLines()
    {
        string input = "# install deps\nnpm install";
        Assert.Equal(input, Normalizer.Normalize(input));
    }

    [Fact]
    public void KeepsCommentInsideCode()
    {
        string input = "if (true) {\n    # a comment\n    Write-Host 'hi'\n}";
        Assert.Equal(input, Normalizer.Normalize(input));
    }

    [Fact]
    public void KeepsTrailingWhitespaceOnlyWhereItMatters()
    {
        // Markdown hard line breaks are two trailing spaces; nothing trims them now.
        Assert.Equal("line one  \nline two", Normalizer.Normalize("line one  \nline two"));
    }

    // ------------------------------------------------------------- idempotency

    [Theory]
    [InlineData("npm install \\\n--save express")]
    [InlineData("git add . &&\ngit commit")]
    [InlineData("Get-Process |\n    Select-Object -First 3")]
    [InlineData("class A:\n    def f(self):\n        return 1")]
    [InlineData("if (a &&\n    b) { doThing(); }")]
    [InlineData("$t = @\"\nline one \\\nline two\n\"@")]
    [InlineData("    indented\n        deeper")]
    public void NormalizeIsIdempotent(string input)
    {
        string once = Normalizer.Normalize(input);
        Assert.Equal(once, Normalizer.Normalize(once));
    }

    [Theory]
    [InlineData("    a\n        b")]
    [InlineData("class A:\n    def f(self):\n        return 1")]
    [InlineData("\ta\n\t\tb")]
    public void DedentIsIdempotent(string input)
    {
        string once = Normalizer.Dedent(input);
        Assert.Equal(once, Normalizer.Dedent(once));
    }

// ------------------------------------------------ leading flag continuation

    [Fact]
    public void JoinsNextLineStartingWithOptionFlags()
    {
        string input = "gh release create v1.2.0 PasteFix/dist/PasteFix.exe --title \"Pastefix v1.2.0\"\n--notes-file docs/release-notes/v1.2.0.md";
        string expected = "gh release create v1.2.0 PasteFix/dist/PasteFix.exe --title \"Pastefix v1.2.0\" --notes-file docs/release-notes/v1.2.0.md";
        Assert.Equal(expected, Normalizer.Normalize(input));
    }

    [Fact]
    public void JoinsNextLineStartingWithShortOption()
    {
        string input = "git commit\n  -m \"my message\"";
        string expected = "git commit -m \"my message\"";
        Assert.Equal(expected, Normalizer.Normalize(input));
    }

    [Fact]
    public void JoinsNextLineStartingWithPipeOrLogicalAnd()
    {
        string input = "Get-Process\n  | Select-Object -First 3";
        string expected = "Get-Process | Select-Object -First 3";
        Assert.Equal(expected, Normalizer.Normalize(input));
    }

    [Fact]
    public void DoesNotJoinYamlListStartingWithDash()
    {
        string input = "items:\n  - apple\n  - banana";
        Assert.Equal(input, Normalizer.Normalize(input));
    }

    [Fact]
    public void DoesNotJoinAfterColonInPython()
    {
        string input = "class A:\n    -1";
        Assert.Equal(input, Normalizer.Normalize(input));
    }
}

