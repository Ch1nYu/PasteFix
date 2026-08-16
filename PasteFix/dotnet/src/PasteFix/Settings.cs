using System;
using System.IO;
using System.Text.Json;

namespace PasteFix;

public sealed class Settings
{
    public bool AutoMonitor { get; set; } = true;
    public bool HotkeyEnabled { get; set; } = true;

    public Dictionary<string, PasteChannel>? PasteChannels { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    internal static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PasteFix",
        "settings.json");

    public static Settings Load()
    {
        Settings settings;
        try
        {
            settings = File.Exists(FilePath)
                ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), JsonOptions) ?? new Settings()
                : new Settings();
        }
        catch
        {
            // 損毀的設定檔就從預設值重來
            settings = new Settings();
        }

        if (settings.PasteChannels is null)
        {
            // 1.0.x 的設定檔沒有這個欄位：補上完整預設表寫回，使用者才看得到、改得動。
            settings.PasteChannels = new Dictionary<string, PasteChannel>(PasteChannelSelector.DefaultMap);
            settings.Save();
        }

        return settings;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch
        {
            // 儲存失敗不應令 tray 崩潰
        }
    }
}