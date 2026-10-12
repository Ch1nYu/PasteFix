using System;
using System.IO;
using System.Text.Json;

namespace PasteFix;

public sealed class Settings
{
    public bool AutoMonitor { get; set; } = true;
    public ProcessingMode Mode { get; set; } = ProcessingMode.SmartContinuation;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    internal static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PasteFix",
        "settings.json");

    public static Settings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? FromJson(File.ReadAllText(FilePath))
                : new Settings();
        }
        catch
        {
            return new Settings();
        }
    }

    public static Settings FromJson(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        var settings = new Settings();

        if (root.TryGetProperty(nameof(AutoMonitor), out JsonElement autoMonitor) &&
            autoMonitor.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            settings.AutoMonitor = autoMonitor.GetBoolean();
        }

        if (root.TryGetProperty(nameof(Mode), out JsonElement mode) &&
            mode.ValueKind == JsonValueKind.Number &&
            mode.TryGetInt32(out int rawMode) &&
            Enum.IsDefined((ProcessingMode)rawMode))
        {
            settings.Mode = (ProcessingMode)rawMode;
        }

        return settings;
    }

    public string ToJson()
    {
        return JsonSerializer.Serialize(this, JsonOptions);
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, ToJson());
        }
        catch
        {
        }
    }
}

