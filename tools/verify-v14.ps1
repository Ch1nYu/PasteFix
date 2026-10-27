# End-to-end check of the v1.4.0 tray features against the real executable.
# Run: pwsh -NoProfile -File tools\verify-v14.ps1
#
# Covers what the unit tests cannot: that the menu is wired to the code, that a
# real clean leaves a real log line, that 還原上一次清洗 puts the original
# text back on the real clipboard, and that 壓成單行（這一次） flattens one copy
# without the mode sticking.

. (Join-Path $PSScriptRoot 'tray-menu.ps1')

$ErrorActionPreference = 'Stop'
$failures = 0
$checks = 0

function Assert-That {
    param([string]$Name, [bool]$Condition, [string]$Detail = '')
    $script:checks++
    if ($Condition) {
        Write-Host "  [PASS] $Name" -ForegroundColor Green
    }
    else {
        $script:failures++
        Write-Host "  [FAIL] $Name" -ForegroundColor Red
        if ($Detail) { Write-Host "         $Detail" -ForegroundColor DarkGray }
    }
}

function Show-Text {
    param([string]$Text)
    if ($null -eq $Text) { return '<null>' }
    return ($Text -replace "`r`n", ' / ' -replace "`n", ' / ' -replace ' ', '·')
}

$log = Join-Path $env:APPDATA 'PasteFix\pastefix.log'
$originalClipboard = Get-Clipboard -Raw
$originalLogCount = @(Get-Content -LiteralPath $log).Count

try {
    Write-Host ''
    Write-Host '== 1. tray menu contains the new items ==' -ForegroundColor Cyan
    $items = Get-TrayMenuItems
    $names = $items | ForEach-Object { $_.Name }
    Assert-That 'menu shows 還原上一次清洗' ($names -contains '還原上一次清洗')
    Assert-That 'menu shows 全域熱鍵（Ctrl+Alt+Shift+V）' ($names -contains '全域熱鍵（Ctrl+Alt+Shift+V）')
    Assert-That 'menu still shows the v1.3 items' (($names -contains '立即清洗') -and ($names -contains '全自動監聽'))
    Assert-That 'menu shows 壓成單行（這一次） instead of 處理模式' (($names -contains '壓成單行（這一次）') -and -not ($names -contains '處理模式'))

    Write-Host ''
    Write-Host '== 2. a real clean writes a real log line ==' -ForegroundColor Cyan
    $bt = [char]0x60
    $fence = "$bt$bt$bt"
    $wrapped = @("${fence}powershell", 'PS C:\Users\me> git status', 'PS C:\Users\me> git log', $fence) -join "`n"
    Set-Clipboard -Value $wrapped
    Start-Sleep -Milliseconds 1500

    $cleaned = Get-Clipboard -Raw
    Assert-That 'fence and prompts were stripped' ($cleaned.Trim() -eq "git status`ngit log") "actual: $(Show-Text $cleaned)"

    $newLines = @(Get-Content -LiteralPath $log | Select-Object -Skip $originalLogCount)
    $cleanLine = $newLines | Where-Object { $_ -match 'cleaned: ' } | Select-Object -Last 1
    Assert-That 'log has a "cleaned:" line' ($null -ne $cleanLine) "new lines: $($newLines -join ' ;; ')"
    if ($cleanLine) {
        Assert-That 'log line reports the stripped containers' ($cleanLine -match 'containers=Fence\+Prompt') $cleanLine
        Assert-That 'log line leaks no clipboard text' (($cleanLine -notmatch 'git') -and ($cleanLine -notmatch 'PS ')) $cleanLine
        Write-Host "         $cleanLine" -ForegroundColor DarkGray
    }

    Write-Host ''
    Write-Host '== 3. 還原上一次清洗 puts the original back ==' -ForegroundColor Cyan
    [void](Invoke-TrayMenuItem -ItemName '還原上一次清洗')
    Start-Sleep -Milliseconds 800

    $restored = Get-Clipboard -Raw
    Assert-That 'clipboard holds the pre-clean text again' ($restored -eq $wrapped) "actual: $(Show-Text $restored)"

    $restoreLines = @(Get-Content -LiteralPath $log | Select-Object -Skip $originalLogCount)
    $restoreLine = $restoreLines | Where-Object { $_ -match 'restored previous clipboard' } | Select-Object -Last 1
    Assert-That 'log records the restore' ($null -ne $restoreLine) "new lines: $($restoreLines -join ' ;; ')"
    if ($restoreLine) { Write-Host "         $restoreLine" -ForegroundColor DarkGray }

    Write-Host ''
    Write-Host '== 4. restore is single-shot and refuses after the clipboard moves on ==' -ForegroundColor Cyan
    Set-Clipboard -Value 'something else entirely'
    Start-Sleep -Milliseconds 1200

    $afterMoved = Get-TrayMenuItems
    $restoreItem = $afterMoved | Where-Object { $_.Name -eq '還原上一次清洗' } | Select-Object -First 1
    Assert-That '還原上一次清洗 is disabled once the clipboard moved on' ($restoreItem -and -not $restoreItem.Enabled) "enabled: $($restoreItem.Enabled)"

    $survivor = Get-Clipboard -Raw
    Assert-That 'the new clipboard content was not overwritten' ($survivor -eq 'something else entirely') "actual: $(Show-Text $survivor)"

    Write-Host ''
    Write-Host '== 5. 壓成單行（這一次） flattens one copy, and only that one ==' -ForegroundColor Cyan
    $indented = "    echo one`n    echo two"
    Set-Clipboard -Value $indented
    Start-Sleep -Milliseconds 1200
    $smart = Get-Clipboard -Raw
    Assert-That 'the copy was cleaned by Smart Continuation first' ($smart.Trim() -eq "echo one`necho two") "actual: $(Show-Text $smart)"

    [void](Invoke-TrayMenuItem -ItemName '壓成單行（這一次）')
    Start-Sleep -Milliseconds 800
    $flat = Get-Clipboard -Raw
    Assert-That 'the clipboard holds one line' ($flat -eq 'echo one echo two') "actual: $(Show-Text $flat)"

    $flatLines = @(Get-Content -LiteralPath $log | Select-Object -Skip $originalLogCount)
    Assert-That 'log records the one-shot as mode=SingleLine' ([bool]($flatLines -match 'cleaned: .* mode=SingleLine')) "new lines: $($flatLines -join ' ;; ')"

    # The undo goes back to what was copied, not to the Smart Continuation result.
    [void](Invoke-TrayMenuItem -ItemName '還原上一次清洗')
    Start-Sleep -Milliseconds 800
    $original = Get-Clipboard -Raw
    Assert-That 'undo gives back the original copy' ($original -eq $indented) "actual: $(Show-Text $original)"

    Set-Clipboard -Value "echo three`necho four"
    Start-Sleep -Milliseconds 1200
    $next = Get-Clipboard -Raw
    Assert-That 'the next copy keeps its lines' ($next -eq "echo three`necho four") "actual: $(Show-Text $next)"
}
catch {
    $failures++
    $checks++
    Write-Host ''
    Write-Host "  [FAIL] script aborted: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host $_.ScriptStackTrace -ForegroundColor DarkGray
}
finally {
    Set-Clipboard -Value $originalClipboard
    Write-Host ''
    if ($failures -eq 0) {
        Write-Host "verify-v14: $checks passed, 0 failed" -ForegroundColor Green
    }
    else {
        Write-Host "verify-v14: $($checks - $failures) passed, $failures FAILED" -ForegroundColor Red
    }
    $script:exitCode = $failures
}

exit $exitCode
