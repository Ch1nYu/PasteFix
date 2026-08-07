using System;
using System.IO;
using System.Text.Json;

namespace PasteFix;

public sealed class Settings
{
    public bool AutoMonitor { get; set; } = true;
    public bool HotkeyEnabled { get; set; } = true;

    internal static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PasteFix",
        "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
            }
        }
        catch
        {
            // 損毀的設定檔就從預設值重來
        }

        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // 儲存失敗不應令 tray 崩潰
        }
    }
}