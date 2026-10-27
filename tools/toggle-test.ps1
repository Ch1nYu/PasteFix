<#
AutoMonitor 與自動清洗的 runtime 回歸測試。

驗證：
  - AutoMonitor 關閉時不改動剪貼簿。
  - SmartContinuation 保留兩條獨立指令的換行。
  - 舊版存了 Mode=1（強制單行）的 settings.json，不再讓自動清洗壓成單行。
  - 成功的清洗會在永久 log 留下長度、行數與容器（不寫內容），以及寫入者的程序名稱。
  - 寫入時標了 ExcludeClipboardContentFromMonitorProcessing 的剪貼簿不被清洗。
  - 全域熱鍵依設定註冊或保持關閉。

腳本結束時會還原完整 settings.json、文字剪貼簿內容，以及原本執行中的
PasteFix process。立即清洗與「還原上一次清洗」的選單點擊仍由 GUI smoke
驗證；這裡只釘住設定與 log 這兩條可自動化的路徑。

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

$pass = 0
$fail = 0
$testError = $null
$restoreErrors = @()

function Stop-PasteFix {
    Get-Process PasteFix -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 800
}

function Write-TestSettings([bool]$autoMonitor, [int]$mode, [bool]$hotkey = $false) {
    if (-not (Test-Path -LiteralPath $settingsDirectory -PathType Container)) {
        New-Item -ItemType Directory -Path $settingsDirectory | Out-Null
    }

    [ordered]@{
        AutoMonitor = $autoMonitor
        Mode = $mode
        HotkeyEnabled = $hotkey
    } | ConvertTo-Json | Set-Content -LiteralPath $settings -Encoding utf8
}

function Start-TestTray {
    $before = @(Get-Content -LiteralPath $log -ErrorAction SilentlyContinue).Count
    Start-Process -FilePath $exe -WindowStyle Hidden
    Start-Sleep -Seconds 2
    return $before
}

function Restart-TestTray([bool]$autoMonitor, [int]$mode, [bool]$hotkey = $false) {
    Stop-PasteFix
    Write-TestSettings -autoMonitor $autoMonitor -mode $mode -hotkey $hotkey
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
    Write-Host '== 舊設定 Mode=1 ==' -ForegroundColor Cyan
    # v1.4 以前強制單行是會一直開著的模式，存成 Mode=1。現在單行是選單上的一次性
    # 動作（由 verify-v14.ps1 驗證），舊檔案裡的 Mode 必須被忽略。
    $before = Restart-TestTray -autoMonitor $true -mode 1
    $lines = Get-NewLogLines $before
    $lines | ForEach-Object { Write-Host "  $_" -ForegroundColor DarkGray }

    $actual = Set-AndReadClipboard $twoCommands
    Assert-State 'legacy: Mode=1 不再讓自動清洗壓成單行' ($actual -eq $twoCommands) `
        "預期 [$($twoCommands -replace "`n", ' / ')]，實際 [$($actual -replace "`n", ' / ')]"

    Write-Host ''
    Write-Host '== 可觀察性：成功清洗會留下永久記錄 ==' -ForegroundColor Cyan
    $before = Restart-TestTray -autoMonitor $true -mode 0
    $lines = Get-NewLogLines $before
    $lines | ForEach-Object { Write-Host "  $_" -ForegroundColor DarkGray }

    # PowerShell 把倒引號當轉義字元，所以 fence 用字元碼組出來。
    $bt = [char]0x60
    $fence = "$bt$bt$bt"
    $fenced = (@("${fence}bash", 'echo one', 'echo two', $fence) -join "`n")
    $actual = Set-AndReadClipboard $fenced
    $lines = Get-NewLogLines $before

    Assert-State 'observable: 容器被剝除' ($actual -eq $twoCommands) `
        "預期 [$($twoCommands -replace "`n", ' / ')]，實際 [$($actual -replace "`n", ' / ')]"

    # 這是 2026-10-12 之前的缺口：只有失敗會寫永久 log，工具正常工作時
    # 一點痕跡都不留，使用者無從得知它幫上忙了沒有。
    Assert-State 'observable: log 記下成功清洗（長度、行數、容器、模式）' `
        ([bool]($lines -match 'cleaned: len=\d+->\d+ lines=\d+->\d+ containers=Fence mode=SmartContinuation')) `
        "log 沒有成功清洗的記錄：$($lines -join ' | ')"

    Assert-State 'observable: log 不含剪貼簿內容' `
        (-not ($lines -match 'echo one')) 'log 洩漏了剪貼簿內容'

    # 模式必須記在「沒有改變」的路徑上，因為那才是判斷該用哪個模式的對照組：
    # 一行沒被動到的複製，證明另一種模式也沒事可做。
    $before = Restart-TestTray -autoMonitor $true -mode 0
    $lines = Get-NewLogLines $before
    $actual = Set-AndReadClipboard $twoCommands
    $lines = Get-NewLogLines $before

    Assert-State 'observable: 不需清洗時也記下模式' `
        ([bool]($lines -match 'clean skipped, no change needed: len=\d+ mode=SmartContinuation')) `
        "log 沒有記下被跳過的複製：$($lines -join ' | ')"

    # 寫入者的程序名稱是找出「借用剪貼簿的程式」、把它加進 IgnoredProcesses 的依據。
    # 沒有 owner 視窗的寫入記為 owner=unknown，也算通過。
    Assert-State 'observable: log 記下寫入者' `
        ([bool]($lines -match 'clean skipped, no change needed: len=\d+ mode=SmartContinuation owner=\S+')) `
        "log 沒有 owner 欄位：$($lines -join ' | ')"

    Write-Host ''
    Write-Host '== 借用剪貼簿的程式 ==' -ForegroundColor Cyan
    # LazyTyper 這類程式把文字放上剪貼簿、貼上、再放回原內容。這裡模擬一個
    # 照 Windows 慣例標了 ExcludeClipboardContentFromMonitorProcessing 的寫入。
    $before = Restart-TestTray -autoMonitor $true -mode 0
    Add-Type -AssemblyName System.Windows.Forms
    $indented = "    echo one`n    echo two"
    $data = New-Object System.Windows.Forms.DataObject
    $data.SetText($indented)
    $data.SetData('ExcludeClipboardContentFromMonitorProcessing', [System.IO.MemoryStream]::new([byte[]](0, 0, 0, 0)))
    [System.Windows.Forms.Clipboard]::SetDataObject($data, $true)
    Start-Sleep -Milliseconds 1200
    $actual = Get-Clipboard -Raw
    $lines = Get-NewLogLines $before

    Assert-State 'borrowed: 標了 Exclude 格式的寫入不被清洗' ($actual -eq $indented) `
        "預期 [$($indented -replace "`n", ' / ')]，實際 [$($actual -replace "`n", ' / ')]"
    Assert-State 'borrowed: log 記下跳過原因' `
        ([bool]($lines -match 'monitor skipped: owner=\S+ reason=WriterAskedToBeIgnored')) `
        "log 沒有跳過記錄：$($lines -join ' | ')"

    Write-Host ''
    Write-Host '== 全域熱鍵 ==' -ForegroundColor Cyan
    $before = Restart-TestTray -autoMonitor $true -mode 0 -hotkey $false
    $lines = Get-NewLogLines $before
    $lines | ForEach-Object { Write-Host "  $_" -ForegroundColor DarkGray }

    Assert-State 'hotkey: 預設關閉時不註冊' `
        (-not ($lines -match 'hotkey Ctrl\+Alt\+Shift\+V registered')) '預設關閉卻註冊了熱鍵'

    $before = Restart-TestTray -autoMonitor $true -mode 0 -hotkey $true
    $lines = Get-NewLogLines $before
    $lines | ForEach-Object { Write-Host "  $_" -ForegroundColor DarkGray }

    # 註冊失敗也算通過：組合鍵可能被別的程式佔用，那條路徑自己會回報。
    # 這裡釘住的是設定真的走到了 RegisterHotKey，而不是被靜默忽略。
    Assert-State 'hotkey: 開啟時向 Windows 註冊' `
        ([bool]($lines -match 'hotkey Ctrl\+Alt\+Shift\+V (registered|unavailable)')) `
        "log 沒有熱鍵註冊的結果：$($lines -join ' | ')"
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

