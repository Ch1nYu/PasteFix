namespace PasteFix.Tests;

public class PowerShellSafeTests
{
    [Fact]
    public void WrapScriptBlockLeavesSingleLineUntouched()
    {
        Assert.Equal("git status", Normalizer.WrapScriptBlock("git status"));
        Assert.Equal("", Normalizer.WrapScriptBlock(""));
    }

    [Fact]
    public void WrapScriptBlockWrapsMultiLineInDotBlock()
    {
        string input = "$x = 1\n$y = 2\nWrite-Host ($x + $y)";
        string expected = ". {\n    $x = 1\n    $y = 2\n    Write-Host ($x + $y)\n}";
        Assert.Equal(expected, Normalizer.WrapScriptBlock(input));
    }

    [Fact]
    public void WrapScriptBlockDoesNotDoubleWrapAlreadyEnclosedBlock()
    {
        string input = ". {\n    $x = 1\n}";
        Assert.Equal(input, Normalizer.WrapScriptBlock(input));

        string braced = "{\n    $x = 1\n}";
        Assert.Equal(braced, Normalizer.WrapScriptBlock(braced));
    }

    [Fact]
    public void WrapScriptBlockPreservesLineEndings()
    {
        string input = "$a = 1\r\n$b = 2";
        string expected = ". {\r\n    $a = 1\r\n    $b = 2\r\n}";
        Assert.Equal(expected, Normalizer.WrapScriptBlock(input));
    }

    [Fact]
    public void WrapScriptBlockHandlesCommentsAndBlankLines()
    {
        string input = "$a = 1\n# comment\n\n$b = 2";
        string expected = ". {\n    $a = 1\n    # comment\n\n    $b = 2\n}";
        Assert.Equal(expected, Normalizer.WrapScriptBlock(input));
    }
}

