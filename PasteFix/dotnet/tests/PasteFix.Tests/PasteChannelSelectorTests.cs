namespace PasteFix.Tests;

/// <summary>
/// Which physical mechanism delivers the paste, decided by the foreground
/// window's class name. The two built-in rows are measured facts: WezTerm and
/// Windows Terminal host input in child surfaces, so a WM_PASTE sent to the
/// top-level HWND is silently dropped (measured 2026-08-09) — keystrokes are
/// the only thing those windows obey. Everything else is policy: unknown
/// windows get Ctrl+Shift+V (decided 2026-08-16) so that a paste which cannot
/// happen is at least visible, and the whole table is overridable from
/// settings.json so a new terminal never needs a code change.
/// </summary>
public class PasteChannelSelectorTests
{
    private static PasteChannel Select(string? windowClass, Dictionary<string, PasteChannel>? overrides = null)
        => PasteChannelSelector.Select(windowClass, overrides);

    [Fact]
    public void WezTermPastesWithCtrlShiftV()
    {
        Assert.Equal(PasteChannel.CtrlShiftV, Select("org.wezfurlong.wezterm"));
    }

    [Fact]
    public void WindowsTerminalPastesWithCtrlV()
    {
        Assert.Equal(PasteChannel.CtrlV, Select("CASCADIA_HOSTING_WINDOW_CLASS"));
    }

    [Fact]
    public void UnknownWindowsGetCtrlShiftV()
    {
        // Ctrl+Shift+V is the terminal-family paste convention; a keystroke that
        // does nothing is a visible failure, unlike WM_PASTE which unknown GPU
        // terminals drop silently after the clean already succeeded.
        Assert.Equal(PasteChannel.CtrlShiftV, Select("whatever.else"));
    }

    [Fact]
    public void MissingClassNameGetsTheDefaultChannel()
    {
        Assert.Equal(PasteChannel.CtrlShiftV, Select(null));
        Assert.Equal(PasteChannel.CtrlShiftV, Select(""));
    }

    [Fact]
    public void OverrideAddsAnUnlistedWindowClass()
    {
        var overrides = new Dictionary<string, PasteChannel> { ["MyTerminal"] = PasteChannel.WmPaste };
        Assert.Equal(PasteChannel.WmPaste, Select("MyTerminal", overrides));
    }

    [Fact]
    public void OverrideReplacesABuiltInRow()
    {
        var overrides = new Dictionary<string, PasteChannel>
        {
            ["org.wezfurlong.wezterm"] = PasteChannel.WmPaste,
        };
        Assert.Equal(PasteChannel.WmPaste, Select("org.wezfurlong.wezterm", overrides));
    }

    [Fact]
    public void StarOverridesTheDefaultChannelButNotExplicitRows()
    {
        var overrides = new Dictionary<string, PasteChannel>
        {
            [PasteChannelSelector.DefaultKey] = PasteChannel.WmPaste,
        };
        Assert.Equal(PasteChannel.WmPaste, Select("whatever.else", overrides));
        Assert.Equal(PasteChannel.CtrlV, Select("CASCADIA_HOSTING_WINDOW_CLASS", overrides));
    }

    [Fact]
    public void DefaultMapIsTheMeasuredTablePlusTheStarRow()
    {
        // Settings writes this map to settings.json on first run, so what users
        // see must equal the measured rows and the current default policy.
        var map = PasteChannelSelector.DefaultMap;
        Assert.Equal(PasteChannel.CtrlShiftV, map["org.wezfurlong.wezterm"]);
        Assert.Equal(PasteChannel.CtrlV, map["CASCADIA_HOSTING_WINDOW_CLASS"]);
        Assert.Equal(PasteChannel.CtrlShiftV, map[PasteChannelSelector.DefaultKey]);
    }

    [Fact]
    public void IsMappedDistinguishesTableRowsFromTheFallback()
    {
        Assert.True(PasteChannelSelector.IsMapped("org.wezfurlong.wezterm", null));
        Assert.False(PasteChannelSelector.IsMapped("whatever.else", null));
        Assert.True(PasteChannelSelector.IsMapped("whatever.else",
            new Dictionary<string, PasteChannel> { ["whatever.else"] = PasteChannel.CtrlV }));
    }
}
