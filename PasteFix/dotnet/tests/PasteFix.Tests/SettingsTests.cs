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
}
