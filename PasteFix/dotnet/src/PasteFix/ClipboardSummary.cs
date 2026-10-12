namespace PasteFix;

/// <summary>
/// 剪貼簿文字的 log 描述。pastefix.log 是永久的明文檔，而密碼也會被複製，
/// 所以 log 只能描述內容（長度）、不能攜帶內容——2026-08-16 的決定。
/// 自願開啟的 PASTEFIX_TRACE 檔不在此限：它就是為了看內容而存在的診斷工具。
/// </summary>
public static class ClipboardSummary
{
    public static string Describe(string? text) => $"len={text?.Length ?? 0}";

    /// <summary>
    /// What a clean did, in a form safe to keep forever: sizes and counts, never
    /// text. Before this existed the permanent log recorded only failures, so a
    /// working tool left no trace of working.
    /// </summary>
    public static string Describe(string? before, string? after, IReadOnlyList<ContainerKind> stripped)
    {
        int linesBefore = CountLines(before);
        int linesAfter = CountLines(after);

        return $"len={before?.Length ?? 0}->{after?.Length ?? 0} " +
               $"lines={linesBefore}->{linesAfter} " +
               $"containers={(stripped.Count == 0 ? "none" : string.Join("+", stripped))}";
    }

    private static int CountLines(string? text)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        int lines = 1;
        foreach (char c in text)
        {
            if (c == '\n') lines++;
        }
        return lines;
    }
}
