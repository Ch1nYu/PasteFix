namespace PasteFix.Tests;

/// <summary>
/// The Alt+V path used to log the first 100 characters of whatever was on the
/// clipboard into the permanent log under %APPDATA%. Passwords get copied too,
/// so the log may describe the text — its length — but never contain it
/// (2026-08-16). The opt-in PASTEFIX_TRACE file is exempt: it exists precisely
/// to show content while debugging.
/// </summary>
public class ClipboardSummaryTests
{
    [Fact]
    public void DescribeReportsLengthInsteadOfContent()
    {
        Assert.Equal("len=9", ClipboardSummary.Describe("swordfish"));
    }

    [Fact]
    public void DescribeNeverLeaksContentIntoTheSummary()
    {
        string summary = ClipboardSummary.Describe("hunter2 is my password\nsecond line");
        Assert.DoesNotContain("hunter2", summary);
        Assert.DoesNotContain("password", summary);
    }

    [Fact]
    public void DescribeHandlesEmptyAndMissingText()
    {
        Assert.Equal("len=0", ClipboardSummary.Describe(""));
        Assert.Equal("len=0", ClipboardSummary.Describe(null));
    }

    [Fact]
    public void DescribeForACleanReportsSizesAndCountsOnly()
    {
        string summary = ClipboardSummary.Describe(
            "    echo one\n    echo two",
            "echo one\necho two",
            []);

        Assert.Equal("len=25->17 lines=2->2 containers=none", summary);
    }

    [Fact]
    public void DescribeNamesTheContainersItStripped()
    {
        string summary = ClipboardSummary.Describe("a", "b", [ContainerKind.Fence, ContainerKind.Prompt]);

        Assert.EndsWith("containers=Fence+Prompt", summary);
    }

    [Fact]
    public void DescribeForACleanNeverLeaksContent()
    {
        string summary = ClipboardSummary.Describe(
            "hunter2 is my password",
            "hunter2 is my password",
            []);

        Assert.DoesNotContain("hunter2", summary);
        Assert.DoesNotContain("password", summary);
    }

    [Fact]
    public void DescribeForACleanHandlesMissingText()
    {
        Assert.Equal("len=0->0 lines=0->0 containers=none", ClipboardSummary.Describe(null, null, []));
    }
}
