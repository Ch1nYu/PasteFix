namespace PasteFix.Tests;

public class SingleLineNormalizerTests
{
    [Fact]
    public void FlattensMarkerlessCommandWraps()
    {
        Assert.Equal(
            "winget install Microsoft.PowerToys",
            SingleLineNormalizer.Flatten("winget install\nMicrosoft.PowerToys"));
    }

    [Fact]
    public void FlattensWindowsAndLegacyLineEndings()
    {
        Assert.Equal("one two three", SingleLineNormalizer.Flatten("one\r\ntwo\rthree"));
    }

    [Fact]
    public void IgnoresBlankLinesAndTrimsPhysicalLineEdges()
    {
        Assert.Equal(
            "winget install Microsoft.PowerToys",
            SingleLineNormalizer.Flatten(" \r\n  winget install  \r\n\r\n\tMicrosoft.PowerToys \t\n "));
    }

    [Fact]
    public void RemovesBashContinuationMarkers()
    {
        Assert.Equal(
            "docker run --rm image",
            SingleLineNormalizer.Flatten("docker run \\\n  --rm image"));
    }

    [Fact]
    public void KeepsContinuationLikeCharactersInsideQuotedText()
    {
        Assert.Equal(
            "$s = 'a \\ b'",
            SingleLineNormalizer.Flatten("$s = 'a \\\nb'"));
    }

    [Fact]
    public void RemovesPowerShellContinuationMarkers()
    {
        Assert.Equal(
            "Get-ChildItem -Path .",
            SingleLineNormalizer.Flatten("Get-ChildItem `\n  -Path ."));
    }

    [Fact]
    public void KeepsWindowsPathBackslashes()
    {
        Assert.Equal("cd C:\\temp\\ dir", SingleLineNormalizer.Flatten("cd C:\\temp\\\ndir"));
    }

    [Fact]
    public void KeepsHereStringBackslashesWhileFlatteningItsLines()
    {
        Assert.Equal(
            "$t = @\" line one \\ line two \"@",
            SingleLineNormalizer.Flatten("$t = @\"\nline one \\\nline two\n\"@"));
    }

    [Fact]
    public void KeepsHeredocBackslashesWhileFlatteningItsLines()
    {
        Assert.Equal(
            "cat <<EOF line one \\ line two EOF",
            SingleLineNormalizer.Flatten("cat <<EOF\nline one \\\nline two\nEOF"));
    }

    [Theory]
    [InlineData("echo one &&\necho two", "echo one && echo two")]
    [InlineData("Get-Process |\nSelect-Object Name", "Get-Process | Select-Object Name")]
    [InlineData("test one ||\necho fallback", "test one || echo fallback")]
    public void KeepsShellOperators(string input, string expected)
    {
        Assert.Equal(expected, SingleLineNormalizer.Flatten(input));
    }

    [Fact]
    public void PreservesCharactersAndInternalWhitespace()
    {
        Assert.Equal(
            "tool --label 中文  參數 value",
            SingleLineNormalizer.Flatten("  tool --label 中文  參數\nvalue  "));
    }

    [Fact]
    public void IntentionallyFlattensStructuredText()
    {
        Assert.Equal(
            "class A: def f(self): return 1",
            SingleLineNormalizer.Flatten("class A:\n    def f(self):\n        return 1"));
    }

    [Fact]
    public void IntentionallyFlattensYaml()
    {
        Assert.Equal(
            "services: api: build: .",
            SingleLineNormalizer.Flatten("services:\n  api:\n    build: ."));
    }

    [Fact]
    public void FlattenIsIdempotent()
    {
        const string input = "  winget install\n  Microsoft.PowerToys  ";
        string once = SingleLineNormalizer.Flatten(input);

        Assert.Equal(once, SingleLineNormalizer.Flatten(once));
    }
}
