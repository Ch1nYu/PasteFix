using System;
using System.IO;
using System.Text.Json;

namespace PasteFix;

public sealed class Settings
{
    public bool AutoMonitor { get; set; } = true;

    /// <summary>
    /// Programs whose clipboard writes the monitor leaves alone, by process name
    /// (with or without ".exe"). LazyTyper borrows the clipboard to paste
    /// dictated text, so it is in the default list. An empty list turns the
    /// filter off.
    /// </summary>
    public string[] IgnoredProcesses { get; set; } = ["LazyTyper"];

    /// <summary>
    /// Off by default. AutoMonitor already covers the copy-then-paste flow, so
    /// the hotkey is only for cleaning a clipboard that was filled before the
    /// tool started or while the monitor was off. A global hotkey that claims a
    /// combination out of nowhere is the kind of surprise this tool avoids.
    /// </summary>
    public bool HotkeyEnabled { get; set; }

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

        // Files written before v1.5 also carry "Mode". Single Line is now a
        // one-shot action rather than a mode that stays on, so it is ignored.

        if (root.TryGetProperty(nameof(IgnoredProcesses), out JsonElement ignored) &&
            ignored.ValueKind == JsonValueKind.Array)
        {
            settings.IgnoredProcesses = ignored.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(e.GetString()))
                .Select(e => e.GetString()!)
                .ToArray();
        }

        if (root.TryGetProperty(nameof(HotkeyEnabled), out JsonElement hotkey) &&
            hotkey.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            settings.HotkeyEnabled = hotkey.GetBoolean();
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

