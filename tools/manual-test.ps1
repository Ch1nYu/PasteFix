<#
Pastefix 手動冒煙測試（2026-08-09 收斂後版本）

驗證的是「跑起來的 tray app」真的做對事，單元測試涵蓋不到的部分：
剪貼簿監聽有沒有接上、AutoMonitor 走的是不是 Dedent、熱鍵走的是不是 Normalize。

前置：
  1. 啟動 PasteFix\dist\PasteFix.exe（注意 dist 是 publish 產物，改完程式碼要重新 publish）
  2. 確認托盤選單裡「全自動監聽」與「熱鍵 Alt+V」都勾選

執行：
  pwsh -File tools\manual-test.ps1

注意：腳本會改動剪貼簿，結束前自動還原。熱鍵那一段會真的觸發貼上動作，
所以請把焦點放在一個無害的視窗（例如記事本）再跑。
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
    Start-Sleep -Milliseconds 1500      # tray 的 debounce 是 300ms，留足餘裕
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
    Write-Host "== 全自動監聽：只做 Dedent（結構保留，不合併續行）==" -ForegroundColor Cyan

    # 核心情境：從聊天視窗框選複製，整段帶著縮排
    Test-AutoMonitor 'dedent: 去除共同縮排' `
        "    if (x) {`n        y = 1`n    }" `
        "if (x) {`n    y = 1`n}"

    # 相對縮排必須保留，否則 Python / YAML 直接失效
    Test-AutoMonitor 'dedent: 保留相對縮排（Python）' `
        "class A:`n    def f(self):`n        return 1" `
        "class A:`n    def f(self):`n        return 1"

    Test-AutoMonitor 'dedent: YAML 結構不變' `
        "services:`n  api:`n    build: ." `
        "services:`n  api:`n    build: ."

    # AutoMonitor 不得做有損轉換
    Test-AutoMonitor 'dedent: 不合併續行' `
        "npm install \`n--save express" `
        "npm install \`n--save express"

    # 尾端空行會讓終端機在貼上當下就執行最後一行
    Test-AutoMonitor 'dedent: 去掉頭尾空行' `
        "`n`ngit status`n`n" `
        "git status"

    # 行尾原樣保留（不再做 LF/CRLF 轉換）
    Test-AutoMonitor 'dedent: CRLF 原樣保留' `
        "  foo`r`n  bar" `
        "foo`r`nbar"

    Write-Host ""
    Write-Host "== 已移除的規則：以下輸入必須原封不動 ==" -ForegroundColor Cyan

    Test-AutoMonitor 'passthrough: prompt 不剝除'  '$ npm install'          '$ npm install'
    Test-AutoMonitor 'passthrough: 註解不刪除'      "# install deps`nnpm install" "# install deps`nnpm install"
    Test-AutoMonitor 'passthrough: fence 不移除'    "``````bash`ngit status`n``````" "``````bash`ngit status`n``````"

    Write-Host ""
    Write-Host "== 熱鍵 Alt+V：Dedent + 合併續行 ==" -ForegroundColor Cyan
    Write-Host "   （會真的送出貼上動作，請確認焦點在無害的視窗）" -ForegroundColor DarkGray

    Set-Clipboard -Value "docker run \`n    -e FOO=bar \`n    image"
    Start-Sleep -Milliseconds 1500
    [System.Windows.Forms.SendKeys]::SendWait('%v')
    Start-Sleep -Milliseconds 1500
    $hotkeyResult = Get-Clipboard -Raw

    if ($hotkeyResult -eq 'docker run -e FOO=bar image') {
        $script:Pass++
        Write-Host "[PASS] hotkey: 合併續行" -ForegroundColor Green
    }
    else {
        $script:Fail++
        Write-Host "[FAIL] hotkey: 合併續行" -ForegroundColor Red
        Write-Host "       expected : docker·run·-e·FOO=bar·image"
        Write-Host "       actual   : $(Show-Text $hotkeyResult)"
        Write-Host "       （若 actual 仍是三行，代表熱鍵沒被觸發或托盤沒在跑）" -ForegroundColor DarkGray
    }
}
finally {
    if ($null -ne $original) { Set-Clipboard -Value $original }
    Write-Host ""
    Write-Host ("== 結果：{0} 通過，{1} 失敗（剪貼簿已還原）==" -f $script:Pass, $script:Fail) `
        -ForegroundColor $(if ($script:Fail -eq 0) { 'Green' } else { 'Red' })
}
