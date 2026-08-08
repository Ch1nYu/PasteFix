# Pastefix 手動冒煙測試（修正後版本，對應 BUG-1~0 修復）
# 前置：啟動 tray（PasteFix\dist\PasteFix.exe），確認「全自動監聽」與「熱鍵 Alt+V」開啟
# 執行：pwsh -File tools\manual-test.ps1
# 注意：腳本會改動剪貼簿，結束前自動還原。

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms

function Test-Case([string]$name, [string]$text, [string]$expected) {
    Set-Clipboard -Value $text
    Start-Sleep -Milliseconds 1500
    $actual = Get-Clipboard -Raw
    $ok = $actual -eq $expected
    $mark = if ($ok) { 'PASS' } else { 'FAIL' }
    $color = if ($ok) { 'Green' } else { 'Red' }
    Write-Host ("[{0}] {1}" -f $mark, $name) -ForegroundColor $color
    Write-Host ("  input      : {0}" -f ($text -replace "`r?`n", " / "))
    if ($ok) {
        Write-Host ("  output     : {0}" -f ($actual -replace "`r?`n", " / "))
    } else {
        Write-Host ("  expected   : {0}" -f ($expected -replace "`r?`n", " / "))
        Write-Host ("  actual     : {0}" -f ($actual -replace "`r?`n", " / "))
    }
}

$NL = "`r`n"
$original = Get-Clipboard -Raw -ErrorAction SilentlyContinue

Write-Host "== 自動監聽測試（需 tray 啟用「全自動監聽」）==" -ForegroundColor Cyan
# BUG-1: 縮排保留（非 prompt 多行不被砍）
Test-Case 'indent preserved (code)'  "if (x) {`n    y`n}"  "if (x) {`r`n    y`r`n}"
# BUG-3: CRLF 保留
Test-Case 'CRLF preserved'  "$ echo a`r`n$ echo b"  "echo a`r`necho b"
# BUG-0: AutoMonitor gate 對非命令文字放行（不改寫）
Test-Case 'non-command pass-through'  "services:`n  api:`n    build: ."  "services:`r`n  api:`r`n    build: ."
# BUG-4: > prompt 僅在 multiPrompt 才剝除
Test-Case 'angle prompt stripped (multiPrompt)'  "> npm install"  "npm install"
# BUG-2: 註解僅在 multiPrompt 刪除
Test-Case 'comment removed (multiPrompt)'  "$ # hello`r`n$ npm install"  "npm install"
# BUG-5: 單行續行合併維持（非 multiPrompt 仍合併）
Test-Case 'backslash continuation'  "npm install \`n--save express"  "npm install --save express"

Write-Host "== 熱鍵 Alt+V（需 tray 啟用「熱鍵 Alt+V」）==" -ForegroundColor Cyan
Set-Clipboard -Value '$ echo hotkey'
[System.Windows.Forms.SendKeys]::SendWait('%v')
Start-Sleep -Milliseconds 1500
Write-Host ("  hotkey output: {0}" -f ((Get-Clipboard -Raw) -replace "`r?`n", ' / '))

Set-Clipboard -Value $original
Write-Host "== 收尾：剪貼簿已還原 ==" -ForegroundColor Green
