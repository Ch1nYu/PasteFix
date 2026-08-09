<#
paste-probe.ps1 — 量測「從聊天視窗複製的內容長什麼樣」與「終端機貼上時到底送進來什麼」

用法（兩種模式，都不需要 PasteFix 在跑）：

  # 模式 A：看剪貼簿裡現在是什麼（先去 Claude/GPT 按「複製程式碼」，再跑這行）
  pwsh -NoProfile -File tools\paste-probe.ps1

  # 模式 B：量測終端機貼上時實際送進來的位元組（會請你按三次 Ctrl+V）
  pwsh -NoProfile -File tools\paste-probe.ps1 -Paste

模式 B 會暫時改動剪貼簿，結束時自動還原。
#>
[CmdletBinding()]
param(
    [switch]$Paste,
    [switch]$Reset,
    [switch]$Restore
)

$ErrorActionPreference = 'Stop'

$WezCfg = Join-Path $env:USERPROFILE '.config\wezterm\wezterm.lua'
$WezBak = "$WezCfg.bak"

# ---------------------------------------------------------------- 清場 / 還原
if ($Reset -or $Restore) {
    $running = @(Get-Process PasteFix -ErrorAction SilentlyContinue)
    if ($running.Count -gt 0) {
        Write-Host ("PasteFix 正在執行 (PID {0}) — 關閉中" -f ($running.Id -join ',')) -ForegroundColor Yellow
        $running | Stop-Process -Force
    }
    else {
        Write-Host "PasteFix 沒有在執行" -ForegroundColor DarkGray
    }

    if ($Restore) {
        if (Test-Path $WezBak) {
            Copy-Item $WezBak $WezCfg -Force
            Write-Host "已從備份還原 wezterm.lua" -ForegroundColor Green
        }
        else {
            Write-Host "找不到備份 $WezBak — 未還原" -ForegroundColor Red
        }
    }
    else {
        $link = Join-Path $env:USERPROFILE '.wezterm.lua'
        if (Test-Path $link -ErrorAction SilentlyContinue) {
            Remove-Item $link -Force
            Write-Host "已刪除 ~/.wezterm.lua" -ForegroundColor Green
        }
        elseif ((Get-Item $link -Force -ErrorAction SilentlyContinue)) {
            Remove-Item $link -Force
            Write-Host "已刪除斷鏈 ~/.wezterm.lua" -ForegroundColor Green
        }
        else {
            Write-Host "~/.wezterm.lua 不存在" -ForegroundColor DarkGray
        }

        if (-not (Test-Path $WezBak)) { Copy-Item $WezCfg $WezBak -Force }
        (Get-Content $WezCfg) `
            -replace '^(\s*)canonicalize_pasted_newlines', '$1-- canonicalize_pasted_newlines' `
        | Set-Content $WezCfg
        Write-Host "已註解掉 canonicalize_pasted_newlines（備份在 wezterm.lua.bak）" -ForegroundColor Green
    }

    Write-Host ""
    Write-Host "--- 目前的 wezterm.lua ---" -ForegroundColor Cyan
    Get-Content $WezCfg
    Write-Host ""
    Write-Host "接著：完全關掉 WezTerm 再重開，然後跑" -ForegroundColor Green
    Write-Host "  pwsh -NoProfile -File tools\paste-probe.ps1 -Paste" -ForegroundColor Green
    return
}

function Show-Bytes([string]$label, [string]$text) {
    Write-Host ""
    Write-Host "--- $label ---" -ForegroundColor Cyan

    if ([string]::IsNullOrEmpty($text)) {
        Write-Host "  (空)" -ForegroundColor DarkGray
        return
    }

    # 行尾統計
    $crlf = ([regex]::Matches($text, "\r\n")).Count
    $bareLf = ([regex]::Matches($text, "(?<!\r)\n")).Count
    $bareCr = ([regex]::Matches($text, "\r(?!\n)")).Count
    Write-Host ("  行尾  : CRLF={0}  裸LF={1}  裸CR={2}" -f $crlf, $bareLf, $bareCr) -ForegroundColor Yellow

    # 縮排：tab 還是空格
    $lines = $text -split "\r\n|\r|\n"
    $tabLines = 0
    $spaceLines = 0
    Write-Host "  逐行  : (S=空格 T=Tab，行首縮排逐字元展開)"
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $l = $lines[$i]
        $indent = ''
        foreach ($c in $l.ToCharArray()) {
            if ($c -eq ' ') { $indent += 'S' }
            elseif ($c -eq "`t") { $indent += 'T' }
            else { break }
        }
        if ($indent.Contains('T')) { $tabLines++ }
        elseif ($indent.Contains('S')) { $spaceLines++ }

        $body = $l.Substring($indent.Length)
        if ($body.Length -gt 60) { $body = $body.Substring(0, 60) + '…' }
        Write-Host ("    {0,2}  [{1,-8}] {2}" -f ($i + 1), $indent, $body)
    }
    Write-Host ("  縮排  : Tab 行={0}  空格行={1}" -f $tabLines, $spaceLines) -ForegroundColor Yellow

    # 其他容易咬人的字元
    $weird = @{}
    foreach ($c in $text.ToCharArray()) {
        $code = [int]$c
        if ($code -in 0x00A0, 0x200B, 0x200C, 0x200D, 0xFEFF, 0x2018, 0x2019, 0x201C, 0x201D, 0x2013, 0x2014) {
            $weird[$code] = 1 + ($weird[$code] ?? 0)
        }
    }
    if ($weird.Count -gt 0) {
        $desc = ($weird.GetEnumerator() | ForEach-Object { "U+{0:X4}×{1}" -f $_.Key, $_.Value }) -join ' '
        Write-Host "  可疑  : $desc" -ForegroundColor Red
    }
}

# ---------------------------------------------------------------- 模式 A
if (-not $Paste) {
    Write-Host "== 模式 A：剪貼簿內容量測 ==" -ForegroundColor Green
    Write-Host "（先去 Claude / ChatGPT 按「複製程式碼」，再跑這支腳本）" -ForegroundColor DarkGray
    $raw = Get-Clipboard -Raw
    Show-Bytes "剪貼簿現況" $raw
    Write-Host ""
    Write-Host "把上面整段貼回給 Claude Code 就好。" -ForegroundColor Green
    return
}

# ---------------------------------------------------------------- 模式 B
Write-Host "== 模式 B：終端機貼上量測 ==" -ForegroundColor Green
Write-Host ""
Write-Host "接下來會做 3 回合。每回合我把一段 3 行文字放進剪貼簿，" -ForegroundColor DarkGray
Write-Host "你只要按 Ctrl+V（或 WezTerm 的 Ctrl+Shift+V），然後等它自己跳下一回合。" -ForegroundColor DarkGray
Write-Host "不用按 Enter，不用複製任何東西。" -ForegroundColor DarkGray
Write-Host ""

$original = Get-Clipboard -Raw -ErrorAction SilentlyContinue

$variants = [ordered]@{
    'LF   (Web/Claude 常見)' = "one`n    two`nthree"
    'CRLF (Windows 慣例)'    = "one`r`n    two`r`nthree"
    'CR   (終端機原生)'      = "one`r    two`rthree"
}

function Read-RawUntilQuiet([int]$quietMs = 1200, [int]$maxMs = 30000) {
    $received = [System.Collections.Generic.List[int]]::new()
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $lastInput = $null

    while ($true) {
        if ([Console]::KeyAvailable) {
            $k = [Console]::ReadKey($true)
            $received.Add([int]$k.KeyChar)
            $lastInput = $sw.ElapsedMilliseconds
        }
        else {
            Start-Sleep -Milliseconds 15
            if ($null -ne $lastInput -and ($sw.ElapsedMilliseconds - $lastInput) -gt $quietMs) { break }
            if ($sw.ElapsedMilliseconds -gt $maxMs) { break }
        }
    }
    return $received
}

function Format-Codes($codes) {
    $parts = foreach ($c in $codes) {
        switch ($c) {
            13 { 'CR' }
            10 { 'LF' }
            9 { 'TAB' }
            27 { 'ESC' }
            32 { '·' }
            default { if ($c -lt 32) { "^$c" } else { [char]$c } }
        }
    }
    return ($parts -join ' ')
}

$results = [ordered]@{}
$oldCtrlC = [Console]::TreatControlCAsInput
[Console]::TreatControlCAsInput = $true

try {
    foreach ($bracketed in @($true, $false)) {
        $mode = if ($bracketed) { 'bracketed paste 開啟 (應用程式端 DECSET 2004)' } else { 'bracketed paste 關閉' }
        Write-Host ""
        Write-Host "########## $mode ##########" -ForegroundColor Magenta

        foreach ($name in $variants.Keys) {
            Set-Clipboard -Value $variants[$name]
            Start-Sleep -Milliseconds 200

            Write-Host ""
            Write-Host ">>> 剪貼簿已設為：$name — 請按 Ctrl+V" -ForegroundColor Green -NoNewline
            Write-Host ""

            if ($bracketed) { [Console]::Write("`e[?2004h") }
            $codes = Read-RawUntilQuiet
            if ($bracketed) { [Console]::Write("`e[?2004l") }

            $key = "$mode | $name"
            $results[$key] = $codes

            if ($codes.Count -eq 0) {
                Write-Host "    (沒收到任何輸入 — 逾時)" -ForegroundColor Red
            }
            else {
                Write-Host ("    收到 {0} 個字元：" -f $codes.Count) -ForegroundColor Yellow
                Write-Host ("    " + (Format-Codes $codes))
            }
        }
    }
}
finally {
    [Console]::TreatControlCAsInput = $oldCtrlC
    if ($null -ne $original) { Set-Clipboard -Value $original }
}

Write-Host ""
Write-Host "== 摘要（把這段貼回給 Claude Code）==" -ForegroundColor Green
Write-Host ("終端機: WT_SESSION={0} TERM_PROGRAM={1}" -f $env:WT_SESSION, $env:TERM_PROGRAM)
foreach ($k in $results.Keys) {
    $codes = $results[$k]
    $cr = ($codes | Where-Object { $_ -eq 13 }).Count
    $lf = ($codes | Where-Object { $_ -eq 10 }).Count
    $esc = ($codes | Where-Object { $_ -eq 27 }).Count
    Write-Host ("  {0,-60} CR={1} LF={2} ESC={3} 總長={4}" -f $k, $cr, $lf, $esc, $codes.Count)
}
Write-Host ""
Write-Host "剪貼簿已還原。" -ForegroundColor DarkGray
