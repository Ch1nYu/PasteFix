namespace PasteFix.Tests;

public class SettingsTests
{
    [Theory]
    [InlineData("{\"AutoMonitor\":false,\"Mode\":1}")]       // v1.3.0, Force Single Line saved
    [InlineData("{\"AutoMonitor\":false,\"Mode\":0}")]       // v1.3.0, Smart Continuation saved
    [InlineData("{\"AutoMonitor\":false,\"Mode\":\"x\"}")]   // a hand-edited file
    public void ASettingsFileThatStillHasAModeLoadsWithoutIt(string json)
    {
        // Single Line is no longer a mode that stays on — it was switched 32
        // times in a month, often back within a minute, and ran for 13 of 1025
        // copies. A file written before that change must still load, and the
        // saved mode must not come back on through the side door.
        Settings settings = Settings.FromJson(json);

        Assert.False(settings.AutoMonitor);
        Assert.DoesNotContain("Mode", settings.ToJson());
    }

    [Fact]
    public void HotkeyIsOffUnlessTheSettingsSayOtherwise()
    {
        // AutoMonitor already covers copy-then-paste, so the hotkey is opt-in.
        // A global hotkey that claims a combination out of nowhere is the kind
        // of surprise this tool avoids.
        Assert.False(new Settings().HotkeyEnabled);
        Assert.False(Settings.FromJson("{\"AutoMonitor\":true}").HotkeyEnabled);
    }

    [Fact]
    public void HotkeySettingRoundTripsThroughJson()
    {
        var original = new Settings { AutoMonitor = false, HotkeyEnabled = true };

        Settings restored = Settings.FromJson(original.ToJson());

        Assert.False(restored.AutoMonitor);
        Assert.True(restored.HotkeyEnabled);
    }

    [Fact]
    public void InvalidHotkeyTypeFallsBackToOffWithoutChangingTheRest()
    {
        Settings settings = Settings.FromJson(
            "{\"AutoMonitor\":false,\"HotkeyEnabled\":\"yes\"}");

        Assert.False(settings.AutoMonitor);
        Assert.False(settings.HotkeyEnabled);
    }

    [Fact]
    public void ASettingsFileFromBeforeTheHotkeyExistedStillLoads()
    {
        // v1.3.0 wrote exactly these two keys. Loading must not throw and must
        // not silently change either of them.
        Settings settings = Settings.FromJson("{\"AutoMonitor\":false,\"Mode\":1}");

        Assert.False(settings.AutoMonitor);
        Assert.False(settings.HotkeyEnabled);
    }

    [Fact]
    public void LazyTyperIsIgnoredByDefault()
    {
        // A settings file from before the list existed gets the default too:
        // the user who asked for this already runs LazyTyper.
        Assert.Equal(["LazyTyper"], new Settings().IgnoredProcesses);
        Assert.Equal(["LazyTyper"], Settings.FromJson("{\"AutoMonitor\":true}").IgnoredProcesses);
    }

    [Fact]
    public void IgnoredProcessesRoundTripThroughJson()
    {
        var original = new Settings { IgnoredProcesses = ["LazyTyper", "espanso"] };

        Assert.Equal(["LazyTyper", "espanso"], Settings.FromJson(original.ToJson()).IgnoredProcesses);
    }

    [Fact]
    public void AnEmptyIgnoredListIsKept()
    {
        // Emptying the list is how the user turns the filter off; it must not
        // come back as the default on the next start.
        Assert.Empty(Settings.FromJson("{\"IgnoredProcesses\":[]}").IgnoredProcesses);
    }

    [Fact]
    public void AnIgnoredListThatIsNotAListFallsBackToTheDefault()
    {
        Assert.Equal(["LazyTyper"], Settings.FromJson("{\"IgnoredProcesses\":\"LazyTyper\"}").IgnoredProcesses);
    }

    [Fact]
    public void EntriesThatAreNotNamesAreDropped()
    {
        Assert.Equal(
            ["LazyTyper"],
            Settings.FromJson("{\"IgnoredProcesses\":[\"LazyTyper\", 3, \"  \", null]}").IgnoredProcesses);
    }
}
