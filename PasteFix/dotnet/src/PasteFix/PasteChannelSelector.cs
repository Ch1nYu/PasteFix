namespace PasteFix;

public enum PasteChannel
{
    CtrlShiftV,
    CtrlV,
    WmPaste,
}

/// <summary>
/// 依前景視窗的類名決定用哪種機制把貼上送出去。內建兩列是實測事實：
/// WezTerm 與 Windows Terminal 把輸入放在子表面，送到頂層 HWND 的 WM_PASTE
/// 會被靜默丟掉（2026-08-09 實測），這類視窗只認自己的貼上鍵。其餘是政策：
/// 未知視窗送 Ctrl+Shift+V（2026-08-16 決定）——貼不上時失敗是看得到的，
/// 不再是清完卻靜默沒貼；整張表可用 settings.json 的 PasteChannels 覆寫，
/// 新終端機不必改程式碼。
/// </summary>
public static class PasteChannelSelector
{
    /// <summary>settings.json 裡代表「所有沒列到的視窗」的萬用鍵。</summary>
    public const string DefaultKey = "*";

    private static readonly IReadOnlyDictionary<string, PasteChannel> BuiltIns =
        new Dictionary<string, PasteChannel>
        {
            ["org.wezfurlong.wezterm"] = PasteChannel.CtrlShiftV,
            ["CASCADIA_HOSTING_WINDOW_CLASS"] = PasteChannel.CtrlV,
        };

    /// <summary>第一次啟動寫進 settings.json 的完整表；使用者看得到、改得動。</summary>
    public static IReadOnlyDictionary<string, PasteChannel> DefaultMap { get; } =
        new Dictionary<string, PasteChannel>(BuiltIns)
        {
            [DefaultKey] = PasteChannel.CtrlShiftV,
        };

    public static PasteChannel Select(string? windowClass, IReadOnlyDictionary<string, PasteChannel>? overrides)
    {
        if (!string.IsNullOrEmpty(windowClass))
        {
            if (overrides is not null && overrides.TryGetValue(windowClass, out var userChannel))
            {
                return userChannel;
            }

            if (BuiltIns.TryGetValue(windowClass, out var builtIn))
            {
                return builtIn;
            }
        }

        return overrides is not null && overrides.TryGetValue(DefaultKey, out var fallback)
            ? fallback
            : PasteChannel.CtrlShiftV;
    }

    /// <summary>視窗是否真的列在（內建或使用者）表上——沒列的才會落到預設通道。</summary>
    public static bool IsMapped(string? windowClass, IReadOnlyDictionary<string, PasteChannel>? overrides)
        => !string.IsNullOrEmpty(windowClass)
           && (overrides is not null && overrides.ContainsKey(windowClass)
               || BuiltIns.ContainsKey(windowClass));
}
