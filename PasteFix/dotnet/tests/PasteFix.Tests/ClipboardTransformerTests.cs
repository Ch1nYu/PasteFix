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
}
