<#
AutoMonitor 與處理模式的 runtime 回歸測試。

驗證：
  - AutoMonitor 關閉時不改動剪貼簿。
  - SmartContinuation 保留兩條獨立指令的換行。
  - SingleLine 將兩條指令壓成一行。
  - SingleLine 模式在重新啟動後仍會保留。

腳本結束時會還原完整 settings.json、文字剪貼簿內容，以及原本執行中的
PasteFix process。立即清洗仍由 GUI smoke 驗證。

用法：
  pwsh -File tools\toggle-test.ps1
  pwsh -File tools\toggle-test.ps1 -ExePath .\PasteFix\dist\PasteFix.exe
#>

[CmdletBinding()]
param(
    [string]$ExePath = (Join-Path $PSScriptRoot '..\PasteFix\dist\PasteFix.exe')
)

$ErrorActionPreference = 'Stop'

$exe = (Resolve-Path -LiteralPath $ExePath).Path
$settings = Join-Path $env:APPDATA 'PasteFix\settings.json'
$settingsDirectory = Split-Path -Parent $settings
$log = Join-Path $env:APPDATA 'PasteFix\pastefix.log'

$settingsExisted = Test-Path -LiteralPath $settings -PathType Leaf
$originalSettingsBytes = if ($settingsExisted) {
    [System.IO.File]::ReadAllBytes($settings)
}
else {
    $null
}

$originalClipboard = Get-Clipboard -Raw -ErrorAction SilentlyContinue
if ($null -eq $originalClipboard) {
    throw '剪貼簿目前沒有純文字；為避免覆蓋圖片或檔案格式，runtime 測試未啟動。'
}
$originalProcesses = @(Get-Process PasteFix -ErrorAction SilentlyContinue)
$originalWasRunning = $originalProcesses.Count -gt 0
$originalProcessPaths = @(
    $originalProcesses |
        ForEach-Object {
            try { $_.Path }
            catch { $null }
        } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Sort-Object -Unique
)

$twoCommands = "echo one`necho two"
$singleLine = 'echo one echo two'

$pass = 0
$fail = 0
$testError = $null
$restoreErrors = @()

function Stop-PasteFix {
    Get-Process PasteFix -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 800
}

function Write-TestSettings([bool]$autoMonitor, [int]$mode) {
    if (-not (Test-Path -LiteralPath $settingsDirectory -PathType Container)) {
        New-Item -ItemType Directory -Path $settingsDirectory | Out-Null
    }

    [ordered]@{
        AutoMonitor = $autoMonitor
        Mode = $mode
    } | ConvertTo-Json | Set-Content -LiteralPath $settings -Encoding utf8
}

function Start-TestTray {
    $before = @(Get-Content -LiteralPath $log -ErrorAction SilentlyContinue).Count
    Start-Process -FilePath $exe -WindowStyle Hidden
    Start-Sleep -Seconds 2
    return $before
}

function Restart-TestTray([bool]$autoMonitor, [int]$mode) {
    Stop-PasteFix
    Write-TestSettings -autoMonitor $autoMonitor -mode $mode
    return Start-TestTray
}

function Restart-TestTrayWithExistingSettings {
    Stop-PasteFix
    return Start-TestTray
}

function Get-NewLogLines([int]$before) {
    return @(Get-Content -LiteralPath $log -ErrorAction SilentlyContinue | Select-Object -Skip $before)
}

function Set-AndReadClipboard([string]$value, [int]$waitMilliseconds = 1200) {
    Set-Clipboard -Value $value
    Start-Sleep -Milliseconds $waitMilliseconds
    return Get-Clipboard -Raw
}

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
    Write-Host ''
    Write-Host '== AutoMonitor 關閉 ==' -ForegroundColor Cyan
    $before = Restart-TestTray -autoMonitor $false -mode 0
    $lines = Get-NewLogLines $before
    $lines | ForEach-Object { Write-Host "  $_" -ForegroundColor DarkGray }

    Assert-State 'off: 沒有啟用剪貼簿監聽' `
        (-not ($lines -match 'clipboard listener on')) '啟動記錄裡出現了監聽註冊'

    $actual = Set-AndReadClipboard $twoCommands
    Assert-State 'off: 剪貼簿沒有被處理' ($actual -eq $twoCommands) `
        "預期 [$($twoCommands -replace "`n", ' / ')]，實際 [$($actual -replace "`n", ' / ')]"

    Write-Host ''
    Write-Host '== SmartContinuation ==' -ForegroundColor Cyan
    $before = Restart-TestTray -autoMonitor $true -mode 0
    $lines = Get-NewLogLines $before
    $lines | ForEach-Object { Write-Host "  $_" -ForegroundColor DarkGray }

    Assert-State 'smart: 啟用了剪貼簿監聽' `
        ([bool]($lines -match 'clipboard listener on ok=True')) '啟動記錄裡沒有成功註冊監聽'

    $actual = Set-AndReadClipboard $twoCommands
    Assert-State 'smart: 保留兩條獨立指令的換行' ($actual -eq $twoCommands) `
        "預期 [$($twoCommands -replace "`n", ' / ')]，實際 [$($actual -replace "`n", ' / ')]"

    Write-Host ''
    Write-Host '== SingleLine ==' -ForegroundColor Cyan
    $before = Restart-TestTray -autoMonitor $true -mode 1
    $lines = Get-NewLogLines $before
    $lines | ForEach-Object { Write-Host "  $_" -ForegroundColor DarkGray }

    Assert-State 'single-line: 啟用了剪貼簿監聽' `
        ([bool]($lines -match 'clipboard listener on ok=True')) '啟動記錄裡沒有成功註冊監聽'

    $actual = Set-AndReadClipboard $twoCommands
    Assert-State 'single-line: 將兩條指令壓成一行' ($actual -eq $singleLine) `
        "預期 [$singleLine]，實際 [$($actual -replace "`n", ' / ')]"

    Write-Host ''
    Write-Host '== SingleLine 重啟持久化 ==' -ForegroundColor Cyan
    $before = Restart-TestTrayWithExistingSettings
    $lines = Get-NewLogLines $before
    $lines | ForEach-Object { Write-Host "  $_" -ForegroundColor DarkGray }

    $savedSettings = Get-Content -LiteralPath $settings -Raw | ConvertFrom-Json
    Assert-State 'restart: settings 仍為 SingleLine' ($savedSettings.Mode -eq 1) `
        "預期 Mode=1，實際 Mode=$($savedSettings.Mode)"

    $actual = Set-AndReadClipboard $twoCommands
    Assert-State 'restart: SingleLine 行為仍生效' ($actual -eq $singleLine) `
        "預期 [$singleLine]，實際 [$($actual -replace "`n", ' / ')]"
}
catch {
    $testError = $_
    $fail++
    Write-Host "[ERROR] $($_.Exception.Message)" -ForegroundColor Red
}
finally {
    try {
        Stop-PasteFix
    }
    catch {
        $restoreErrors += "停止測試 process 失敗：$($_.Exception.Message)"
    }

    try {
        if ($settingsExisted) {
            [System.IO.File]::WriteAllBytes($settings, $originalSettingsBytes)
        }
        elseif (Test-Path -LiteralPath $settings -PathType Leaf) {
            Remove-Item -LiteralPath $settings -Force
        }
    }
    catch {
        $restoreErrors += "還原 settings 失敗：$($_.Exception.Message)"
    }

    try {
        Set-Clipboard -Value $originalClipboard
    }
    catch {
        $restoreErrors += "還原剪貼簿失敗：$($_.Exception.Message)"
    }

    if ($originalWasRunning) {
        $pathsToRestart = if ($originalProcessPaths.Count -gt 0) {
            $originalProcessPaths
        }
        else {
            @($exe)
        }

        try {
            foreach ($path in $pathsToRestart) {
                Start-Process -FilePath $path -WindowStyle Hidden
            }
        }
        catch {
            $restoreErrors += "重新啟動原始 process 失敗：$($_.Exception.Message)"
        }
    }

    if ($restoreErrors.Count -gt 0) {
        $fail += $restoreErrors.Count
        $restoreErrors | ForEach-Object { Write-Host "[RESTORE ERROR] $_" -ForegroundColor Red }
    }

    Write-Host ''
    $restoreStatus = if ($restoreErrors.Count -eq 0) { '狀態已還原' } else { '狀態還原不完整' }
    Write-Host ("== 結果：{0} 通過，{1} 失敗；{2} ==" -f $pass, $fail, $restoreStatus) `
        -ForegroundColor $(if ($fail -eq 0) { 'Green' } else { 'Red' })
}

if ($null -ne $testError) {
    Write-Error -ErrorRecord $testError
}

if ($fail -gt 0) {
    exit 1
}

