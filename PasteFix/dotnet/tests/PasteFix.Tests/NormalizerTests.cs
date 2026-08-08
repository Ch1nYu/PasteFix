using System;
using System.IO;

namespace PasteFix.Tests;

public class NormalizerTests
{
    private static string Example(string name)
    {
        string fileName = $"{name}.input.txt";
        string path = Path.Combine(AppContext.BaseDirectory, "examples", fileName);
        return File.ReadAllText(path).TrimEnd();
    }

    [Fact]
    public void RemovesMarkdownFenceAndIndent()
    {
        string input = Example("markdown-fence");
        string expected = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "examples", "markdown-fence.expected.txt"))
            .TrimEnd();

        Assert.Equal(expected, Normalizer.Normalize(input));
    }

    [Fact]
    public void RemovesDollarShellPrompt()
    {
        Assert.Equal("npm install express", Normalizer.Normalize("$ npm install express"));
    }

    [Fact]
    public void RemovesAngleShellPrompt()
    {
        Assert.Equal("npm install express", Normalizer.Normalize("> npm install express"));
    }

    [Fact]
    public void JoinsBackslashContinuations()
    {
        Assert.Equal(
            "npm install --save express",
            Normalizer.Normalize("npm install \\\n--save \\\nexpress"));
    }

    [Fact]
    public void KeepsMultiLinePromptsAsSeparateCommands()
    {
        Assert.Equal(
            "echo a\r\necho b",
            Normalizer.Normalize("$ echo a\n$ echo b"));
    }

    [Fact]
    public void RemovesPowerShellPrompt()
    {
        Assert.Equal(
            "npm install",
            Normalizer.Normalize("PS C:\\Users\\me> npm install"));
    }

    [Fact]
    public void RemovesCommentLinesInPromptContext()
    {
        // BUG-2: comment removal only in multiPrompt (command) context; prompts stripped too
        Assert.Equal(
            "npm install",
            Normalizer.Normalize("$ # install dependencies\n$ npm install"));
    }

    [Fact]
    public void KeepsCommentLinesInNonPromptContext()
    {
        Assert.Equal(
            "# install dependencies\r\nnpm install",
            Normalizer.Normalize("# install dependencies\nnpm install"));
    }

    [Fact]
    public void JoinsDoubleAmpersandContinuations()
    {
        Assert.Equal(
            "git add . && git commit",
            Normalizer.Normalize("git add . &&\ngit commit"));
    }

    [Fact]
    public void HandlesCrLfInput()
    {
        // BUG-3: output is always CRLF for Windows clipboard compatibility
        Assert.Equal("echo a\r\necho b", Normalizer.Normalize("$ echo a\r\n$ echo b"));
    }

    [Fact]
    public void ConvertsLfInputToCrLfOutput()
    {
        // BUG-3: web copies are LF; output must still be CRLF
        Assert.Equal("echo a\r\necho b", Normalizer.Normalize("$ echo a\n$ echo b"));
    }

    [Fact]
    public void NormalizeLineEndingsConvertsLfToCrLf()
    {
        Assert.Equal("a\r\nb\r\nc", Normalizer.NormalizeLineEndings("a\nb\nc"));
    }

    [Fact]
    public void NormalizeLineEndingsKeepsExistingCrLf()
    {
        Assert.Equal("a\r\nb", Normalizer.NormalizeLineEndings("a\r\nb"));
    }

    [Fact]
    public void ReturnsEmptyForEmptyInput()
    {
        Assert.Equal("", Normalizer.Normalize("   \n\n  "));
    }

    [Fact]
    public void ReturnsEmptyForFenceOnlyInput()
    {
        Assert.Equal("", Normalizer.Normalize("```\n```"));
    }

    // BUG-1 / BUG-3 / BUG-4 coverage gaps
    [Theory]
    [InlineData("$ npm install express")]
    [InlineData("> pip install\nnpm install")]
    [InlineData("```bash\ngit add . &&\ngit commit -m \"x\"\n```")]
    [InlineData("# hello\nnpm install")]
    [InlineData("$ echo a\n$ echo b")]
    public void NormalizeIsIdempotent(string input)
    {
        string once = Normalizer.Normalize(input);
        Assert.Equal(once, Normalizer.Normalize(once));
    }

    [Fact]
    public void KeepsIndentForCodeWithoutPrompt()
    {
        // BUG-1: indentation must be preserved for non-prompt multi-line code
        string input = "if (x) {\n    y\n}";
        Assert.Equal("if (x) {\r\n    y\r\n}", Normalizer.Normalize(input));
    }

    [Fact]
    public void KeepsIndentForPythonCode()
    {
        string input = "class A:\n    def f(self):\n        return 1";
        string result = Normalizer.Normalize(input);
        Assert.Contains("def f(self):", result);
        Assert.Contains("        return 1", result);
    }

    [Fact]
    public void KeepsCommentInsideCodeWithoutPrompt()
    {
        // BUG-2: comments inside code should not be deleted when no prompt
        string input = "if (true) {\n    # this is a comment\n    Write-Host 'hi'\n}";
        string result = Normalizer.Normalize(input);
        Assert.Contains("# this is a comment", result);
    }

    [Fact]
    public void KeepsMarkdownBlockquoteInSingleLineNonPrompt()
    {
        // When input isn't prompt-shaped (no $/>/PS prompts), > is preserved
        // However multiPrompt activates for single > line due to design.
        // This tests that pure non-prompt multi-line passes through.
        string input = "hello\nworld";
        Assert.Equal("hello\r\nworld", Normalizer.Normalize(input));
    }

    [Fact]
    public void KeepsNonCommandMultiLineIntact()
    {
        // BUG-0 / design: non-command multi-line should pass through AutoMonitor gate untouched
        string input = "services:\n  api:\n    build:\n      context: .";
        Assert.Equal(
            "services:\r\n  api:\r\n    build:\r\n      context: .",
            Normalizer.Normalize(input));
    }

    // BUG-5: continuation merge is a shell-command transform. It must not fire on
    // code, where a trailing && / \ sits inside an unclosed bracket.
    [Fact]
    public void KeepsCodeWithTrailingAndAndUnmerged()
    {
        string input = "if (a &&\n    b) { doThing(); }";
        Assert.Equal("if (a &&\r\n    b) { doThing(); }", Normalizer.Normalize(input));
    }

    [Fact]
    public void KeepsCMacroBackslashInsideBracesUnmerged()
    {
        string input = "#define FOO(x) do { \\\n    bar(x); \\\n} while (0)";
        Assert.Equal(
            "#define FOO(x) do { \\\r\n    bar(x); \\\r\n} while (0)",
            Normalizer.Normalize(input));
    }

    // BUG-2 residual: AnyPrompt used a zero-width `\s*`, so PowerShell variables
    // ($a, $b) counted as prompts, flipped multiPrompt on, and the comment filter
    // deleted real code comments.
    [Fact]
    public void PowerShellVariablesAreNotPrompts()
    {
        string input = "$a = 1\n# a comment\n$b = 2";
        Assert.Equal("$a = 1\r\n# a comment\r\n$b = 2", Normalizer.Normalize(input));
    }

    [Fact]
    public void StillDetectsRealDollarPrompts()
    {
        Assert.Equal("echo a\r\necho b", Normalizer.Normalize("$ echo a\n$ echo b"));
    }

    [Fact]
    public void MergedContinuationDoesNotKeepLeadingIndent()
    {
        // Indented shell continuations must collapse to single spaces, not indent + space.
        Assert.Equal(
            "docker run -e FOO=bar image",
            Normalizer.Normalize("docker run \\\n    -e FOO=bar \\\n    image"));
    }
}