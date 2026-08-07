# Pastefix 手動冒煙測試
#
# 前置：
#   1. 先啟動 tray：PasteFix\dist\PasteFix.exe
#   2. 確認 tray 選單「全自動監聽」與「熱鍵 Alt+V」都是開啟狀態
#
# 執行：
#   pwsh -File tools\manual-test.ps1
#
# 注意：腳本會改動剪貼簿，結束前會自動還原。

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

Write-Host "== 自動監聽測試（需 tray 啟用「全自動監聽」） ==" -ForegroundColor Cyan
Test-Case 'dollor shell prompt'  '$ npm install express'                          'npm install express'
Test-Case 'angle shell prompt'   '> npm install'                                  'npm install'
Test-Case 'PowerShell prompt'    'PS C:\Users\me> npm install'                    'npm install'
Test-Case 'fence + slash join'   ('```' + 'bash' + $NL + '    npm install \' + $NL + '    --save express' + $NL + '```') 'npm install --save express'
Test-Case '&& continuation'      ('git add . &&' + $NL + 'git commit -m "init"')  'git add . && git commit -m "init"'
Test-Case 'comment lines'        ('# install deps' + $NL + 'npm install')         'npm install'
Test-Case 'multi prompted lines' ('$ echo a' + $NL + '$ echo b')                  ('echo a' + "`n" + 'echo b')

Write-Host "== 熱鍵 Alt+V（需 tray 啟用「熱鍵 Alt+V」） ==" -ForegroundColor Cyan
Set-Clipboard -Value '$ echo hotkey'
[System.Windows.Forms.SendKeys]::SendWait('%v')
Start-Sleep -Milliseconds 1500
Write-Host ("  output     : {0}" -f ((Get-Clipboard -Raw) -replace "`r?`n", ' / '))

Set-Clipboard -Value $original
Write-Host "== 收尾：剪貼簿已還原 ==" -ForegroundColor Green