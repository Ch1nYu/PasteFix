<#
Pastefix 手動冒煙測試（全自動清洗統一管線版）
#>

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms

$script:Pass = 0
$script:Fail = 0

function Show-Text([string]$s) {
    if ($null -eq $s) { return '<null>' }
    return ($s -replace "`r`n", ' / ' -replace "`n", ' / ' -replace ' ', [char]0xB7)
}

function Test-AutoMonitor([string]$name, [string]$text, [string]$expected) {
    Set-Clipboard -Value $text
    Start-Sleep -Milliseconds 800
    $actual = Get-Clipboard -Raw

    if ($actual -eq $expected) {
        $script:Pass++
        Write-Host "[PASS] $name" -ForegroundColor Green
    }
    else {
        $script:Fail++
        Write-Host "[FAIL] $name" -ForegroundColor Red
        Write-Host "       in       : $(Show-Text $text)"
        Write-Host "       expected : $(Show-Text $expected)"
        Write-Host "       actual   : $(Show-Text $actual)"
    }
}

$original = Get-Clipboard -Raw -ErrorAction SilentlyContinue

try {
    Write-Host ""
    Write-Host "== 全自動監聽：去除共同縮排 + 合併續行 + 保留語法結構 ==" -ForegroundColor Cyan

    Test-AutoMonitor 'normalize: 去除共同縮排' `
        "    if (x) {`n        y = 1`n    }" `
        "if (x) {`n    y = 1`n}"

    Test-AutoMonitor 'normalize: 保留相對縮排（Python）' `
        "class A:`n    def f(self):`n        return 1" `
        "class A:`n    def f(self):`n        return 1"

    Test-AutoMonitor 'normalize: YAML 結構不變' `
        "services:`n  api:`n    build: ." `
        "services:`n  api:`n    build: ."

    Test-AutoMonitor 'normalize: 自動合併 bash 續行' `
        "npm install \`n--save express" `
        "npm install --save express"

    Test-AutoMonitor 'normalize: 自動合併 powershell 續行' `
        "Get-ChildItem ```n    -Path ." `
        "Get-ChildItem -Path ."

    Test-AutoMonitor 'normalize: 自動合併換行參數選項' `
        "gh release create v1.2.0`n--notes-file release.md" `
        "gh release create v1.2.0 --notes-file release.md"

    Test-AutoMonitor 'normalize: 去掉頭尾空行' `
        "`n`ngit status`n`n" `
        "git status"

    Test-AutoMonitor 'normalize: CRLF 原樣保留' `
        "  foo`r`n  bar" `
        "foo`r`nbar"

    Write-Host ""
    Write-Host "== 守衛：非指令區塊保護 ==" -ForegroundColor Cyan

    Test-AutoMonitor -name 'guard: prompt 不剝除' -text '$ npm install' -expected '$ npm install'
    Test-AutoMonitor -name 'guard: 註解不刪除' -text "# install deps`nnpm install" -expected "# install deps`nnpm install"
    Test-AutoMonitor -name 'guard: fence 不移除' -text "'''bash`ngit status`n'''" -expected "'''bash`ngit status`n'''"
    Test-AutoMonitor -name 'guard: windows 路徑不被當成續行' -text "cd C:\temp\`ndir" -expected "cd C:\temp\`ndir"
}
finally {
    if ($null -ne $original) { Set-Clipboard -Value $original }
    Write-Host ""
    Write-Host ("== 結果：{0} 通過，{1} 失敗（剪貼簿已還原）==" -f $script:Pass, $script:Fail) `
        -ForegroundColor $(if ($script:Fail -eq 0) { 'Green' } else { 'Red' })
}

