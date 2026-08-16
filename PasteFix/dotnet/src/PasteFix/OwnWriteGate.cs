namespace PasteFix;

/// <summary>
/// 分辨「剪貼簿事件是不是我們自己寫回造成的」。CleanClipboard 在 format
/// listener 還掛著的時候寫回剪貼簿；2026-08-16 以前只靠寫回內容的 dedent
/// 冪等來擋自我觸發，現在直接比對剪貼簿序號：事件當下的序號等於自己寫回
/// 那一刻的序號，就是我們造成的，監聽路徑跳過。序號在前之後都不同，所以
/// 使用者接著複製的新內容不受影響。
/// </summary>
public sealed class OwnWriteGate
{
    private uint? _ownSequence;

    public void MarkOwnWrite(uint sequence) => _ownSequence = sequence;

    public bool IsOwnWrite(uint sequence) => _ownSequence == sequence;
}
