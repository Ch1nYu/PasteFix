namespace PasteFix.Tests;

public class SettingsTests
{
    [Fact]
    public void LegacyJsonDefaultsToSmartModeWithoutChangingAutoMonitor()
    {
        Settings settings = Settings.FromJson("{\"AutoMonitor\":false}");

        Assert.False(settings.AutoMonitor);
        Assert.Equal(ProcessingMode.SmartContinuation, settings.Mode);
    }

    [Fact]
    public void UnknownModeFallsBackToSmartWithoutChangingAutoMonitor()
    {
        Settings settings = Settings.FromJson("{\"AutoMonitor\":false,\"Mode\":999}");

        Assert.False(settings.AutoMonitor);
        Assert.Equal(ProcessingMode.SmartContinuation, settings.Mode);
    }

    [Fact]
    public void SingleLineModeRoundTripsThroughJson()
    {
        var original = new Settings
        {
            AutoMonitor = false,
            Mode = ProcessingMode.SingleLine,
        };

        Settings restored = Settings.FromJson(original.ToJson());

        Assert.False(restored.AutoMonitor);
        Assert.Equal(ProcessingMode.SingleLine, restored.Mode);
    }

    [Fact]
    public void InvalidModeTypeFallsBackToSmartWithoutChangingAutoMonitor()
    {
        Settings settings = Settings.FromJson("{\"AutoMonitor\":false,\"Mode\":\"invalid\"}");

        Assert.False(settings.AutoMonitor);
        Assert.Equal(ProcessingMode.SmartContinuation, settings.Mode);
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
            "{\"AutoMonitor\":false,\"Mode\":1,\"HotkeyEnabled\":\"yes\"}");

        Assert.False(settings.AutoMonitor);
        Assert.Equal(ProcessingMode.SingleLine, settings.Mode);
        Assert.False(settings.HotkeyEnabled);
    }

    [Fact]
    public void ASettingsFileFromBeforeTheHotkeyExistedStillLoads()
    {
        // v1.3.0 wrote exactly these two keys. Loading must not throw and must
        // not silently change either of them.
        Settings settings = Settings.FromJson("{\"AutoMonitor\":false,\"Mode\":1}");

        Assert.False(settings.AutoMonitor);
        Assert.Equal(ProcessingMode.SingleLine, settings.Mode);
        Assert.False(settings.HotkeyEnabled);
    }
}
