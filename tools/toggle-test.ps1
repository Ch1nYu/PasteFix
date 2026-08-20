<#
全自動監聽開關的自動回歸測試。

驗證 AutoMonitor 關掉之後有沒有真的停止運作，打開後恢復運作。

用法：
  pwsh -File tools\toggle-test.ps1
#>

$ErrorActionPreference = 'Stop'

$exe      = Join-Path $PSScriptRoot '..\PasteFix\dist\PasteFix.exe' | Resolve-Path
$settings = Join-Path $env:APPDATA 'PasteFix\settings.json'
$log      = Join-Path $env:APPDATA 'PasteFix\pastefix.log'
$origClip = Get-Clipboard -Raw -ErrorAction SilentlyContinue

$dirty = "    git status`n    git log"
$clean = "git status`ngit log"

function Restart-Tray([bool]$auto) {
    Get-Process PasteFix -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 800
    @{ AutoMonitor = $auto } | ConvertTo-Json | Set-Content $settings
    $before = (Get-Content $log -ErrorAction SilentlyContinue).Count
    Start-Process $exe -WindowStyle Hidden
    Start-Sleep -Seconds 2
    return $before
}

function Get-CleanedClipboard {
    Set-Clipboard -Value $dirty
    Start-Sleep -Milliseconds 1200
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
    Write-Host "== AutoMonitor 關閉 ==" -ForegroundColor Cyan
    $b = Restart-Tray $false
    $lines = Get-Content $log | Select-Object -Skip $b
    $lines | ForEach-Object { Write-Host "  $_" -ForegroundColor DarkGray }

    Assert-State 'off: 沒有啟用剪貼簿監聽' `
        (-not ($lines -match 'clipboard listener on')) '啟動記錄裡出現了監聽註冊'

    $got = Get-CleanedClipboard
    Assert-State 'off: 剪貼簿沒有被清洗' ($got -eq $dirty) "實際 [$($got -replace "`n", ' / ')]"

    Write-Host ""
    Write-Host "== AutoMonitor 開啟 ==" -ForegroundColor Cyan
    $b = Restart-Tray $true
    $lines = Get-Content $log | Select-Object -Skip $b
    $lines | ForEach-Object { Write-Host "  $_" -ForegroundColor DarkGray }

    Assert-State 'on: 啟用了剪貼簿監聽' `
        ([bool]($lines -match 'clipboard listener on ok=True')) '啟動記錄裡沒有監聽註冊'

    $got = Get-CleanedClipboard
    Assert-State 'on: 剪貼簿已被清洗' ($got -eq $clean) "實際 [$($got -replace "`n", ' / ')]"
}
finally {
    if ($null -ne $origClip) { Set-Clipboard -Value $origClip }
    Write-Host ""
    Write-Host ("== 結果：{0} 通過，{1} 失敗（剪貼簿已還原，設定回到 true）==" -f $pass, $fail) `
        -ForegroundColor $(if ($fail -eq 0) { 'Green' } else { 'Red' })
}

