namespace PasteFix.Tests;

/// <summary>
/// A wrapper is only removed when it provably covers the whole copy. Every test
/// here that asserts "unchanged" is guarding the 2026-08-08 failure mode: text
/// that merely looks like it has structure must not be restructured. See
/// docs/records/PLAN-2026-08-09.md — the ban is on guessing which line is a
/// command, and nothing in ContainerStripper guesses.
/// </summary>
public class ContainerStripperTests
{
    [Theory]
    [InlineData("```\necho one\necho two\n```")]
    [InlineData("```powershell\necho one\necho two\n```")]
    [InlineData("~~~bash\necho one\necho two\n~~~")]
    public void FenceAroundTheWholeCopyIsRemoved(string input)
    {
        (string text, IReadOnlyList<ContainerKind> stripped) = ContainerStripper.Strip(input);

        Assert.Equal("echo one\necho two", text);
        Assert.Equal([ContainerKind.Fence], stripped);
    }

    [Fact]
    public void FenceStripLeavesIndentationToTheNormalizer()
    {
        // Stripping a wrapper and reformatting text are separate steps. The
        // stripper removes the frame and nothing else; dedent happens later, in
        // Normalizer, where a fenced block and a plain block get the same
        // treatment. Keeping them apart is what makes each one testable.
        const string input = "```\n    echo one\n    echo two\n```";

        (string text, IReadOnlyList<ContainerKind> stripped) = ContainerStripper.Strip(input);

        Assert.Equal("    echo one\n    echo two", text);
        Assert.Equal([ContainerKind.Fence], stripped);
    }

    [Fact]
    public void FenceKeepsTheOriginalLineEndings()
    {
        const string input = "```\r\necho one\r\necho two\r\n```";

        (string text, IReadOnlyList<ContainerKind> stripped) = ContainerStripper.Strip(input);

        Assert.Equal("echo one\r\necho two", text);
        Assert.Equal([ContainerKind.Fence], stripped);
    }

    [Fact]
    public void UnclosedFenceIsNotAFence()
    {
        const string input = "```\necho one\necho two";

        Assert.Equal((input, Array.Empty<ContainerKind>()), Unwrap(ContainerStripper.Strip(input)));
    }

    [Fact]
    public void FenceThatDoesNotWrapEverythingIsLeftAlone()
    {
        // Two blocks, or a document quoting code. Stripping the outer pair would
        // leave the inner markers stranded mid-text.
        const string input = "```\n```\ncode\n```\n```";

        (string text, IReadOnlyList<ContainerKind> stripped) = ContainerStripper.Strip(input);

        Assert.Equal(input, text);
        Assert.Empty(stripped);
    }

    [Fact]
    public void NestedFenceWithADifferentMarkerLengthIsLeftAlone()
    {
        const string input = "````\n```\ncode\n```\n````";

        (string text, IReadOnlyList<ContainerKind> stripped) = ContainerStripper.Strip(input);

        Assert.Equal(input, text);
        Assert.Empty(stripped);
    }

    [Fact]
    public void TextAfterTheClosingFenceIsNotAFence()
    {
        const string input = "```\necho one\n```\nand that is how you do it";

        (string text, IReadOnlyList<ContainerKind> stripped) = ContainerStripper.Strip(input);

        Assert.Equal(input, text);
        Assert.Empty(stripped);
    }

    [Theory]
    [InlineData("PS C:\\Users\\me> echo one\nPS C:\\Users\\me> echo two")]
    [InlineData("$ echo one\n$ echo two")]
    [InlineData("❯ echo one\n❯ echo two")]
    [InlineData("➜ echo one\n➜ echo two")]
    public void PromptGutterIsRemoved(string input)
    {
        (string text, IReadOnlyList<ContainerKind> stripped) = ContainerStripper.Strip(input);

        Assert.Equal("echo one\necho two", text);
        Assert.Equal([ContainerKind.Prompt], stripped);
    }

    [Fact]
    public void PromptGutterToleratesBlankLinesBetweenCommands()
    {
        const string input = "$ echo one\n\n$ echo two";

        (string text, IReadOnlyList<ContainerKind> stripped) = ContainerStripper.Strip(input);

        Assert.Equal("echo one\n\necho two", text);
        Assert.Equal([ContainerKind.Prompt], stripped);
    }

    [Fact]
    public void MixedPromptStylesAreNotAGutter()
    {
        // The user copied output from two different shells, or a transcript.
        // There is no single gutter to remove.
        const string input = "$ echo one\n❯ echo two";

        (string text, IReadOnlyList<ContainerKind> stripped) = ContainerStripper.Strip(input);

        Assert.Equal(input, text);
        Assert.Empty(stripped);
    }

    [Fact]
    public void ADollarSignInsideALineIsNotAPrompt()
    {
        const string input = "echo $HOME\necho $PATH";

        (string text, IReadOnlyList<ContainerKind> stripped) = ContainerStripper.Strip(input);

        Assert.Equal(input, text);
        Assert.Empty(stripped);
    }

    [Fact]
    public void OnePromptedLineAmongPlainLinesIsNotAGutter()
    {
        const string input = "$ echo one\necho two";

        (string text, IReadOnlyList<ContainerKind> stripped) = ContainerStripper.Strip(input);

        Assert.Equal(input, text);
        Assert.Empty(stripped);
    }

    [Theory]
    [InlineData("1:\techo one\n2:\techo two")]
    [InlineData("1| echo one\n2| echo two")]
    [InlineData("1: echo one\n2: echo two")]
    [InlineData("  1: echo one\n  2: echo two")]
    public void LineNumberGutterIsRemoved(string input)
    {
        (string text, IReadOnlyList<ContainerKind> stripped) = ContainerStripper.Strip(input);

        Assert.Equal("echo one\necho two", text);
        Assert.Equal([ContainerKind.LineNumber], stripped);
    }

    [Fact]
    public void GutterThatStartsAtAnOffsetIsStillAGutter()
    {
        // A partial paste from a scrollback buffer starts wherever it starts.
        const string input = "42: echo one\n43: echo two";

        (string text, IReadOnlyList<ContainerKind> stripped) = ContainerStripper.Strip(input);

        Assert.Equal("echo one\necho two", text);
        Assert.Equal([ContainerKind.LineNumber], stripped);
    }

    [Fact]
    public void NonSequentialLineNumbersAreNotAGutter()
    {
        const string input = "1: echo one\n3: echo two";

        (string text, IReadOnlyList<ContainerKind> stripped) = ContainerStripper.Strip(input);

        Assert.Equal(input, text);
        Assert.Empty(stripped);
    }

    [Fact]
    public void ASingleNumberedLineIsNotAGutter()
    {
        const string input = "1: echo one";

        (string text, IReadOnlyList<ContainerKind> stripped) = ContainerStripper.Strip(input);

        Assert.Equal(input, text);
        Assert.Empty(stripped);
    }

    [Theory]
    [InlineData("1. Run the installer\n2. Restart the shell")]
    [InlineData("1) Run the installer\n2) Restart the shell")]
    public void MarkdownOrderedListIsNotAGutter(string input)
    {
        // The 2026-08-08 failure mode, exactly: a numbered list is prose, and
        // eating its numbering is silent structural damage. A gutter uses a tab,
        // a colon or a pipe; "." and ")" are how markdown writes a list.
        (string text, IReadOnlyList<ContainerKind> stripped) = ContainerStripper.Strip(input);

        Assert.Equal(input, text);
        Assert.Empty(stripped);
    }

    [Fact]
    public void MarkdownListInsideAFenceSurvivesTheFenceStrip()
    {
        const string input = "```\n1. Run the installer\n2. Restart the shell\n```";

        (string text, IReadOnlyList<ContainerKind> stripped) = ContainerStripper.Strip(input);

        Assert.Equal("1. Run the installer\n2. Restart the shell", text);
        Assert.Equal([ContainerKind.Fence], stripped);
    }

    [Fact]
    public void FenceAndPromptGutterAreBothRemoved()
    {
        const string input = "```powershell\nPS C:\\Users\\me> echo one\nPS C:\\Users\\me> echo two\n```";

        (string text, IReadOnlyList<ContainerKind> stripped) = ContainerStripper.Strip(input);

        Assert.Equal("echo one\necho two", text);
        Assert.Equal([ContainerKind.Fence, ContainerKind.Prompt], stripped);
    }

    [Fact]
    public void PlainTextComesBackByteForByte()
    {
        // Indentation, trailing spaces and blank lines included. Strip must not
        // reformat text it decided not to wrap.
        const string input = "class Repo:\n    def pull(self):\n        pass\n\n\n";

        (string text, IReadOnlyList<ContainerKind> stripped) = ContainerStripper.Strip(input);

        Assert.Equal(input, text);
        Assert.Empty(stripped);
    }

    [Fact]
    public void EmptyInputProducesEmptyOutput()
    {
        (string text, IReadOnlyList<ContainerKind> stripped) = ContainerStripper.Strip("");

        Assert.Equal("", text);
        Assert.Empty(stripped);
    }

    private static (string, ContainerKind[]) Unwrap((string Text, IReadOnlyList<ContainerKind> Stripped) result)
        => (result.Text, result.Stripped.ToArray());
}
