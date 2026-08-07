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
            "echo a\necho b",
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
    public void RemovesCommentLines()
    {
        Assert.Equal(
            "npm install",
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
        Assert.Equal("echo a\necho b", Normalizer.Normalize("$ echo a\r\n$ echo b"));
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
}