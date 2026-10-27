<#
html-probe.ps1 — 看剪貼簿的 HTML 格式裡，複製來源有沒有標明「這是程式碼、什麼語言」

PasteFix 會讀 CF_HTML：整份選取剛好是一個 <pre>/<code> 區塊時，
log 會記 source=code:<語言>；Force Single Line 遇到 python、yaml 這類區塊會保留換行。
這支腳本讓你驗證各個聊天視窗、網站實際放了什麼標記。

用法（不需要 PasteFix 在跑）：先在瀏覽器裡用滑鼠選取一段程式碼按 Ctrl+C，再跑
  pwsh -NoProfile -File tools\html-probe.ps1

只印出標籤與屬性，不印程式碼內容。
#>
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms

$html = [System.Windows.Forms.Clipboard]::GetText([System.Windows.Forms.TextDataFormat]::Html)
if (-not $html) {
    Write-Host '剪貼簿沒有 HTML 格式 → PasteFix 記為 source=text' -ForegroundColor Yellow
    Write-Host '（「複製」按鈕通常只放純文字；用滑鼠選取再 Ctrl+C 才會帶 HTML）' -ForegroundColor DarkGray
    return
}

$m = [regex]::Match($html, '<!--StartFragment-->(.*)<!--EndFragment-->', 'Singleline, IgnoreCase')
if (-not $m.Success) {
    Write-Host '有 HTML，但沒有 StartFragment/EndFragment 標記 → source=text' -ForegroundColor Yellow
    return
}

$fragment = $m.Groups[1].Value
$tags = [regex]::Matches($fragment, '<(?:pre|code)\b[^>]*>', 'IgnoreCase')
Write-Host ("fragment 長度：{0}，<pre>/<code> 開頭標籤：{1} 個" -f $fragment.Length, $tags.Count) -ForegroundColor Cyan
foreach ($t in $tags) { Write-Host "  $($t.Value)" }
if ($tags.Count -eq 0) {
    Write-Host '沒有 <pre>/<code> → source=html（不改變任何行為）' -ForegroundColor Yellow
}
