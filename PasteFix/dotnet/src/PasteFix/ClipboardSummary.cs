namespace PasteFix;

/// <summary>
/// 剪貼簿文字的 log 描述。pastefix.log 是永久的明文檔，而密碼也會被複製，
/// 所以 log 只能描述內容（長度）、不能攜帶內容——2026-08-16 的決定。
/// 自願開啟的 PASTEFIX_TRACE 檔不在此限：它就是為了看內容而存在的診斷工具。
/// </summary>
public static class ClipboardSummary
{
    public static string Describe(string? text) => $"len={text?.Length ?? 0}";
}
