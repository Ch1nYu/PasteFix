namespace PasteFix.Tests;

public class ClipboardTransformerTests
{
    [Fact]
    public void SmartModePreservesIndependentCommands()
    {
        const string input = "echo one\necho two";

        Assert.Equal(
            input,
            ClipboardTransformer.Transform(input, ProcessingMode.SmartContinuation).Text);
    }

    [Fact]
    public void SingleLineModeFlattensIndependentCommands()
    {
        Assert.Equal(
            "echo one echo two",
            ClipboardTransformer.Transform("echo one\necho two", ProcessingMode.SingleLine).Text);
    }

    [Fact]
    public void UnknownModeSafelyUsesSmartMode()
    {
        const string input = "echo one\necho two";

        Assert.Equal(input, ClipboardTransformer.Transform(input, (ProcessingMode)999).Text);
    }

    [Fact]
    public void WrapperIsStrippedBeforeTheModeRuns()
    {
        const string input = "```powershell\n    echo one\n    echo two\n```";

        ClipboardResult result = ClipboardTransformer.Transform(input, ProcessingMode.SmartContinuation);

        Assert.Equal("echo one\necho two", result.Text);
        Assert.Equal([ContainerKind.Fence], result.Stripped);
    }

    [Fact]
    public void TextWithoutAWrapperReportsNothingStripped()
    {
        const string input = "echo one\necho two";

        ClipboardResult result = ClipboardTransformer.Transform(input, ProcessingMode.SmartContinuation);

        Assert.Equal(input, result.Text);
        Assert.Empty(result.Stripped);
    }

    [Fact]
    public void FenceIsStrippedBeforeFlattening()
    {
        // The fence marker would otherwise end up inside the single line, since
        // Flatten only drops a trailing backslash or backtick when the scanner
        // agrees the line continues.
        const string input = "```\necho one\necho two\n```";

        ClipboardResult result = ClipboardTransformer.Transform(input, ProcessingMode.SingleLine);

        Assert.Equal("echo one echo two", result.Text);
        Assert.Equal([ContainerKind.Fence], result.Stripped);
    }

    [Fact]
    public void InvisibleCharactersAreFixedBeforeTheWrapperIsLookedFor()
    {
        // A prompt indented with no-break spaces is still a prompt.
        const string input = "\u00A0$ echo one\n\u00A0$ echo two";

        ClipboardResult result = ClipboardTransformer.Transform(input, ProcessingMode.SmartContinuation);

        Assert.Equal("echo one\necho two", result.Text);
        Assert.Equal([ContainerKind.Prompt], result.Stripped);
        Assert.Equal(2, result.CharactersFixed);
    }

    [Fact]
    public void ACopyWithNothingInvisibleReportsNoCharacterFixes()
    {
        Assert.Equal(0, ClipboardTransformer.Transform("echo one", ProcessingMode.SmartContinuation).CharactersFixed);
    }

    [Fact]
    public void SingleLineModeKeepsTheStructureOfACodeBlockInAStructuredLanguage()
    {
        // Force Single Line is deliberately destructive to Python and YAML. When
        // the page itself says the copy is one Python block, flattening it can
        // only break it, so the block goes through Smart Continuation instead.
        const string input = "def f():\n    return 1";

        ClipboardResult result = ClipboardTransformer.Transform(
            input, ProcessingMode.SingleLine, new CopySource(SourceKind.Code, "python"));

        Assert.Equal(input, result.Text);
        Assert.Equal("python", result.StructureGuard);
    }

    [Theory]
    [InlineData(SourceKind.Code, "bash")]
    [InlineData(SourceKind.Code, null)]
    [InlineData(SourceKind.Mixed, null)]
    [InlineData(SourceKind.Html, null)]
    [InlineData(SourceKind.PlainText, null)]
    public void SingleLineModeStillFlattensEverythingElse(SourceKind kind, string? language)
    {
        ClipboardResult result = ClipboardTransformer.Transform(
            "echo one\necho two", ProcessingMode.SingleLine, new CopySource(kind, language));

        Assert.Equal("echo one echo two", result.Text);
        Assert.Null(result.StructureGuard);
    }

    [Fact]
    public void SmartModeNeverReportsAGuard()
    {
        // Smart Continuation already keeps structure; there is nothing to guard.
        ClipboardResult result = ClipboardTransformer.Transform(
            "def f():\n    return 1", ProcessingMode.SmartContinuation, new CopySource(SourceKind.Code, "python"));

        Assert.Null(result.StructureGuard);
    }
}
