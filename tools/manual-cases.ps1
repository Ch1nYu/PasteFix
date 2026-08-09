<#
Pastefix 手動測試用例（人在迴路版）

tools/manual-test.ps1 是自動冒煙，全程不用手。
這一支相反：它只負責把「髒的」原始文字放進剪貼簿並印出預期值，
真正的貼上動作由你在終端機裡親手按 Ctrl+V / Alt+V 完成——
因為要驗的正是「終端機收到的東西」，那段路自動化量不到。

用法：
  . tools\manual-cases.ps1      # 注意前面那個點，要 dot-source 才會帶入函式
  Show-Cases                    # 看全部用例
  Use-Case C                    # 把 C 放上剪貼簿，然後自己按 Ctrl+V 或 Alt+V

前置：PasteFix\dist\PasteFix.exe 已啟動，托盤選單「全自動監聽」與「熱鍵 Alt+V」都勾選。

預期值全部用 Release 版 PasteFix.dll 實際跑過，不是紙上推導。
#>

$script:Cases = [ordered]@{
    A = @{
        Why    = 'Dedent 主場景：從聊天視窗框選複製，整段帶共同縮排'
        Raw    = (@('    if (x) {', '        y = 1', '    }') -join "`n")
        CtrlV  = (@('if (x) {', '    y = 1', '}') -join "`n")
        AltV   = (@('if (x) {', '    y = 1', '}') -join "`n")
    }
    B = @{
        Why    = '相對縮排必須留著，否則 Python 直接壞掉'
        Raw    = (@('class A:', '    def f(self):', '        return 1') -join "`n")
        CtrlV  = '（原樣不動）'
        AltV   = '（原樣不動）'
    }
    C = @{
        Why    = '反斜線續行：Ctrl+V 不該合併，Alt+V 才合併'
        Raw    = (@('npm install \', '  --save express') -join "`n")
        CtrlV  = '（原樣不動，兩行）'
        AltV   = 'npm install --save express'
    }
    D = @{
        Why    = '管線後面夾空行——專案宣稱要修的頭號案例，自我測試才抓到'
        Raw    = (@('Get-Process |', '', '  Where-Object CPU -gt 10') -join "`n")
        CtrlV  = '（原樣不動，含中間空行）'
        AltV   = 'Get-Process | Where-Object CPU -gt 10'
    }
    E = @{
        Why    = 'Windows 路徑守衛：結尾的 \ 是路徑不是續行，合併就毀了'
        Raw    = (@('cd C:\temp\', 'dir') -join "`n")
        CtrlV  = '（原樣不動）'
        AltV   = '（原樣不動，兩行）'
    }
    F = @{
        Why    = '引號守衛：字串裡的 \ 不是續行'
        Raw    = (@("`$s = 'a \", "b'", 'echo done') -join "`n")
        CtrlV  = '（原樣不動）'
        AltV   = '（原樣不動，三行）'
    }
    G = @{
        Why    = 'PowerShell here-string 守衛：整段內容原封不動'
        Raw    = (@("`$t = @`"", '  line1 \', '  line2', "`"@", 'echo ok') -join "`n")
        CtrlV  = '（原樣不動）'
        AltV   = '（原樣不動，五行）'
    }
    H = @{
        Why    = 'bash heredoc 守衛'
        Raw    = (@('cat <<EOF', '  a \', '  b', 'EOF', 'echo ok') -join "`n")
        CtrlV  = '（原樣不動）'
        AltV   = '（原樣不動，五行）'
    }
    I = @{
        Why    = '已移除的規則：fence / prompt / 註解都不准動'
        Raw    = (@('```bash', '$ npm install', '# comment', '```') -join "`n")
        CtrlV  = '（原樣不動）'
        AltV   = '（原樣不動，四行）'
    }
    J = @{
        Why    = '&& 續行：Alt+V 合併，且 && 保留'
        Raw    = (@('cd /tmp &&', 'ls -la') -join "`n")
        CtrlV  = '（原樣不動）'
        AltV   = 'cd /tmp && ls -la'
    }
    K = @{
        Why    = '括號深度守衛：if (...) 裡的換行不是續行'
        Raw    = (@('if ($a -and', '    $b) {', '  echo hi', '}') -join "`n")
        CtrlV  = '（原樣不動）'
        AltV   = '（原樣不動，四行）'
    }
    L = @{
        Why    = '尾端空行要砍掉，否則貼上當下最後一行就被執行'
        Raw    = "`n`ngit status`n`n"
        CtrlV  = 'git status（前後無空行）'
        AltV   = 'git status'
    }
}

function Show-Text([string]$s) {
    if ($null -eq $s) { return '<null>' }
    ($s -replace "`r`n", ' ⏎ ' -replace "`n", ' ⏎ ' -replace ' ', [char]0xB7)
}

function Show-Cases {
    foreach ($k in $script:Cases.Keys) {
        $c = $script:Cases[$k]
        Write-Host ("{0}  {1}" -f $k, $c.Why) -ForegroundColor Cyan
        Write-Host ("    原始   : {0}" -f (Show-Text $c.Raw))
        Write-Host ("    Ctrl+V : {0}" -f (Show-Text $c.CtrlV)) -ForegroundColor DarkGray
        Write-Host ("    Alt+V  : {0}" -f (Show-Text $c.AltV))  -ForegroundColor DarkGray
    }
}

function Use-Case {
    param([Parameter(Mandatory)][string]$Name)

    $key = $Name.ToUpperInvariant()
    if (-not $script:Cases.Contains($key)) {
        Write-Host "沒有這個用例：$Name（可用：$($script:Cases.Keys -join ', ')）" -ForegroundColor Red
        return
    }
    $c = $script:Cases[$key]

    Set-Clipboard -Value $c.Raw
    Start-Sleep -Milliseconds 1200   # tray 的 debounce 是 300ms，留餘裕

    Write-Host ""
    Write-Host ("用例 {0} — {1}" -f $key, $c.Why) -ForegroundColor Cyan
    Write-Host ("  放進去的原始文字   : {0}" -f (Show-Text $c.Raw))
    Write-Host ("  AutoMonitor 之後   : {0}" -f (Show-Text (Get-Clipboard -Raw))) -ForegroundColor Yellow
    Write-Host ("  這一行就是 Ctrl+V 會貼出來的東西；預期 {0}" -f (Show-Text $c.CtrlV))
    Write-Host ("  按 Alt+V 則預期     : {0}" -f (Show-Text $c.AltV)) -ForegroundColor Green
    Write-Host "  （現在動手：Ctrl+V 看一次，Ctrl+C 清掉，再 Use-Case $key 後按 Alt+V 看一次）" -ForegroundColor DarkGray
}
