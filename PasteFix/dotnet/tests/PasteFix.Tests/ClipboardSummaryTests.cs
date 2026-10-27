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
            [],
            ProcessingMode.SmartContinuation);

        Assert.Equal("len=25->17 lines=2->2 containers=none mode=SmartContinuation", summary);
    }

    [Fact]
    public void DescribeNamesTheContainersItStripped()
    {
        string summary = ClipboardSummary.Describe(
            "a",
            "b",
            [ContainerKind.Fence, ContainerKind.Prompt],
            ProcessingMode.SmartContinuation);

        Assert.EndsWith("containers=Fence+Prompt mode=SmartContinuation", summary);
    }

    [Fact]
    public void DescribeForACleanNamesTheModeThatDidTheWork()
    {
        // The mode is what makes the log answer the mode question later: the
        // same four lines collapsing to one is a SingleLine job, staying at
        // four is a SmartContinuation one.
        string summary = ClipboardSummary.Describe(
            "echo one\necho two\necho three\necho four",
            "echo one echo two echo three echo four",
            [],
            ProcessingMode.SingleLine);

        Assert.Equal("len=38->38 lines=4->1 containers=none mode=SingleLine", summary);
    }

    [Fact]
    public void DescribeForASkippedCopyNamesTheModeThatDeclined()
    {
        Assert.Equal(
            "len=17 mode=SmartContinuation",
            ClipboardSummary.Describe("echo one\necho two", ProcessingMode.SmartContinuation));
    }

    [Fact]
    public void DescribeForACleanNeverLeaksContent()
    {
        string summary = ClipboardSummary.Describe(
            "hunter2 is my password",
            "hunter2 is my password",
            [],
            ProcessingMode.SmartContinuation);

        Assert.DoesNotContain("hunter2", summary);
        Assert.DoesNotContain("password", summary);
    }

    [Fact]
    public void DescribeForACleanHandlesMissingText()
    {
        Assert.Equal(
            "len=0->0 lines=0->0 containers=none mode=SmartContinuation",
            ClipboardSummary.Describe(null, null, [], ProcessingMode.SmartContinuation));
    }

    [Fact]
    public void DescribeForACleanAppendsCharacterFixesAndTheSource()
    {
        // The new fields go after mode= so every query written against the
        // older lines still matches.
        string summary = ClipboardSummary.Describe(
            "git\u00A0commit",
            new ClipboardResult("git commit", [], CharactersFixed: 1),
            ProcessingMode.SmartContinuation,
            new CopySource(SourceKind.Code, "bash"));

        Assert.Equal("len=10->10 lines=1->1 containers=none mode=SmartContinuation chars=1 source=code:bash", summary);
    }

    [Fact]
    public void DescribeForACleanNamesTheGuardThatKeptStructure()
    {
        string summary = ClipboardSummary.Describe(
            "a\nb",
            new ClipboardResult("a\nb!", [], StructureGuard: "python"),
            ProcessingMode.SingleLine,
            new CopySource(SourceKind.Code, "python"));

        Assert.Equal("len=3->4 lines=2->2 containers=none mode=SingleLine chars=0 source=code:python guard=python", summary);
    }

    [Fact]
    public void DescribeForASkippedCopyNamesTheSource()
    {
        Assert.Equal(
            "len=17 mode=SmartContinuation source=html",
            ClipboardSummary.Describe("echo one\necho two", ProcessingMode.SmartContinuation, new CopySource(SourceKind.Html, null)));
    }
}
