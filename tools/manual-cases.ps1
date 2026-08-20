<#
Pastefix 手動測試用例（統一管線版）
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
        Why    = '反斜線續行：自動合併成單一指令'
        Raw    = (@('npm install \', '  --save express') -join "`n")
        CtrlV  = 'npm install --save express'
        AltV   = 'npm install --save express'
    }
    D = @{
        Why    = '管線後面夾空行——自動合併並消除空行，避免 EmptyPipeElement'
        Raw    = (@('Get-Process |', '', '  Where-Object CPU -gt 10') -join "`n")
        CtrlV  = 'Get-Process | Where-Object CPU -gt 10'
        AltV   = 'Get-Process | Where-Object CPU -gt 10'
    }
    E = @{
        Why    = 'Windows 路徑守衛：結尾的 \ 是路徑不是續行，合併就毀了'
        Raw    = (@('cd C:\temp\', 'dir') -join "`n")
        CtrlV  = '（原樣不動，兩行）'
        AltV   = '（原樣不動，兩行）'
    }
    F = @{
        Why    = '引號守衛：字串裡的 \ 不是續行'
        Raw    = (@("`$s = 'a \", "b'", 'echo done') -join "`n")
        CtrlV  = '（原樣不動，三行）'
        AltV   = '（原樣不動，三行）'
    }
    G = @{
        Why    = 'PowerShell here-string 守衛：整段內容原封不動'
        Raw    = (@("`$t = @`"", '  line1 \', '  line2', "`"@", 'echo ok') -join "`n")
        CtrlV  = '（原樣不動，五行）'
        AltV   = '（原樣不動，五行）'
    }
    H = @{
        Why    = 'bash heredoc 守衛'
        Raw    = (@('cat <<EOF', '  a \', '  b', 'EOF', 'echo ok') -join "`n")
        CtrlV  = '（原樣不動，五行）'
        AltV   = '（原樣不動，五行）'
    }
    I = @{
        Why    = '已移除的規則：fence / prompt / 註解都不准動'
        Raw    = (@('```bash', '$ npm install', '# comment', '```') -join "`n")
        CtrlV  = '（原樣不動，四行）'
        AltV   = '（原樣不動，四行）'
    }
    J = @{
        Why    = '&& 續行：自動合併，且 && 保留'
        Raw    = (@('cd /tmp &&', 'ls -la') -join "`n")
        CtrlV  = 'cd /tmp && ls -la'
        AltV   = 'cd /tmp && ls -la'
    }
    K = @{
        Why    = '括號深度守衛：if (...) 裡的換行不是續行'
        Raw    = (@('if ($a -and', '    $b) {', '  echo hi', '}') -join "`n")
        CtrlV  = '（原樣不動，四行）'
        AltV   = '（原樣不動，四行）'
    }
    L = @{
        Why    = '尾端空行要砍掉，否則貼上當下最後一行就被執行'
        Raw    = "`n`ngit status`n`n"
        CtrlV  = 'git status'
        AltV   = 'git status'
    }
    M = @{
        Why    = '選取漏掉左邊界：第一行沒縮排，其餘各行多兩格。第一行是完整敘述 → 自動修掉'
        Raw    = (@('$ErrorActionPreference = ''Stop''', '  Get-Process', '  Write-Host done') -join "`n")
        CtrlV  = (@('$ErrorActionPreference = ''Stop''', 'Get-Process', 'Write-Host done') -join "`n")
        AltV   = (@('$ErrorActionPreference = ''Stop''', 'Get-Process', 'Write-Host done') -join "`n")
    }
    N = @{
        Why    = '同樣的形狀，但第一行以冒號結尾 → 下面的縮排有文法理由，不准動（動了就是壓平 Python）'
        Raw    = (@('class A:', '      def f(self):', '          return 1') -join "`n")
        CtrlV  = '（原樣不動——縮排整體偏移的 Python 仍然合法）'
        AltV   = '（原樣不動——縮排整體偏移的 Python 仍然合法）'
    }
    O = @{
        Why    = '同樣的形狀，但第一行有未閉合的括號 → 不准動'
        Raw    = (@('if (x) {', '      y = 1', '  }') -join "`n")
        CtrlV  = '（原樣不動——shell 不在乎行首空白，多兩格照跑）'
        AltV   = '（原樣不動——shell 不在乎行首空白，多兩格照跑）'
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
    Start-Sleep -Milliseconds 800

    Write-Host ""
    Write-Host ("用例 {0} — {1}" -f $key, $c.Why) -ForegroundColor Cyan
    Write-Host ("  放進去的原始文字   : {0}" -f (Show-Text $c.Raw))
    Write-Host ("  AutoMonitor 之後   : {0}" -f (Show-Text (Get-Clipboard -Raw))) -ForegroundColor Yellow
    Write-Host ("  這一行就是 Ctrl+V / Alt+V 會貼出來的東西；預期 {0}" -f (Show-Text $c.CtrlV))
}

