<#
兩個開關（全自動監聽 / 熱鍵 Alt+V）的自動回歸測試。

背景：這兩個開關從寫出來就沒作用過。`ToolStripMenuItem` 預設不會因為點擊而翻動
`Checked`，所以 handler 讀到的永遠是舊值，等於把同一個值寫回設定。修正是替兩個
選項加上 `CheckOnClick = true`（2026-08-10）。

這支測的是**關掉之後有沒有真的停止運作**——繞過選單，直接改 settings.json 再重
啟，然後觀察行為。滑鼠點擊本身測不到（tray 選單在另一個行程裡），那一段要人工。

用法：
  pwsh -File tools\toggle-test.ps1

會反覆重啟 PasteFix.exe 並改動剪貼簿與 settings.json，結束前把兩者都還原。
#>

$ErrorActionPreference = 'Stop'

$exe      = Join-Path $PSScriptRoot '..\PasteFix\dist\PasteFix.exe' | Resolve-Path
$settings = Join-Path $env:APPDATA 'PasteFix\settings.json'
$log      = Join-Path $env:APPDATA 'PasteFix\pastefix.log'
$origClip = Get-Clipboard -Raw -ErrorAction SilentlyContinue

$dirty = "    git status`n    git log"
$clean = "git status`ngit log"

function Restart-Tray([bool]$auto, [bool]$hotkey) {
    Get-Process PasteFix -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 800
    @{ AutoMonitor = $auto; HotkeyEnabled = $hotkey } | ConvertTo-Json | Set-Content $settings
    $before = (Get-Content $log).Count
    Start-Process $exe
    Start-Sleep -Seconds 3
    return $before
}

function Get-CleanedClipboard {
    Set-Clipboard -Value $dirty
    Start-Sleep -Milliseconds 1500      # tray 的 debounce 是 300ms
    return Get-Clipboard -Raw
}

$pass = 0
$fail = 0

function Assert-State([string]$name, [bool]$condition, [string]$detail) {
    if ($condition) {
        $script:pass++
        Write-Host "[PASS] $name" -ForegroundColor Green
    }
    else {
        $script:fail++
        Write-Host "[FAIL] $name" -ForegroundColor Red
        Write-Host "       $detail" -ForegroundColor Red
    }
}

try {
    Write-Host ""
    Write-Host "== 兩個開關都關 ==" -ForegroundColor Cyan
    $b = Restart-Tray $false $false
    $lines = Get-Content $log | Select-Object -Skip $b
    $lines | ForEach-Object { Write-Host "  $_" -ForegroundColor DarkGray }

    Assert-State 'off: 沒有註冊剪貼簿監聽' `
        (-not ($lines -match 'clipboard listener on')) '啟動記錄裡出現了監聽註冊'
    Assert-State 'off: 沒有註冊熱鍵' `
        (-not ($lines -match 'hotkey Alt\+V registered')) '啟動記錄裡出現了熱鍵註冊'

    $got = Get-CleanedClipboard
    Assert-State 'off: 剪貼簿沒有被清洗' ($got -eq $dirty) "實際 [$($got -replace "`n", ' / ')]"

    Write-Host ""
    Write-Host "== 兩個開關都開 ==" -ForegroundColor Cyan
    $b = Restart-Tray $true $true
    $lines = Get-Content $log | Select-Object -Skip $b
    $lines | ForEach-Object { Write-Host "  $_" -ForegroundColor DarkGray }

    Assert-State 'on: 註冊了剪貼簿監聽' `
        ([bool]($lines -match 'clipboard listener on ok=True')) '啟動記錄裡沒有監聽註冊'
    Assert-State 'on: 註冊了熱鍵' `
        ([bool]($lines -match 'hotkey Alt\+V registered ok=True')) '啟動記錄裡沒有熱鍵註冊'

    $got = Get-CleanedClipboard
    Assert-State 'on: 剪貼簿已被清洗' ($got -eq $clean) "實際 [$($got -replace "`n", ' / ')]"
}
finally {
    if ($null -ne $origClip) { Set-Clipboard -Value $origClip }
    Write-Host ""
    Write-Host ("== 結果：{0} 通過，{1} 失敗（剪貼簿已還原，設定回到 true/true）==" -f $pass, $fail) `
        -ForegroundColor $(if ($fail -eq 0) { 'Green' } else { 'Red' })
    Write-Host "選單點擊本身測不到，請人工右鍵托盤各點一次確認勾勾會動。" -ForegroundColor DarkGray
}
