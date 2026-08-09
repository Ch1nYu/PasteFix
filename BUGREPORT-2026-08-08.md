# PasteFix 貼上問題調查報告

- 日期：2026-08-08
- 環境：Windows 11 26200 / PowerShell 7.6.4 / PSReadLine 2.4.5 / starship 提示字元（續行符號 `∙`）
- 事發時 PasteFix.exe **正在執行**（PID 35980，啟動 01:55:09，測試時間 02:05），`%APPDATA%\PasteFix\settings.json` = `{"AutoMonitor": true, "HotkeyEnabled": true}`
- 觸發路徑：**AutoMonitor**（不是 Alt+V 熱鍵）。使用者從聊天視窗複製程式碼 → `WM_CLIPBOARDUPDATE` → 300ms debounce → `CleanClipboard()` → `Normalizer.Normalize()` 覆寫剪貼簿 → 使用者貼進 pwsh。

---

## 一、已用實際 DLL 驗證的結果（硬證據）

直接載入 `PasteFix/dotnet/src/PasteFix/bin/Release/net10.0-windows/PasteFix.dll`，呼叫 `PasteFix.Normalizer.Normalize()`（`·` 代表空格）：

```
=== herestring ===
IN :  $text·=·@"        OUT:  $text·=·@"
      line·one                line·one
      ····indented·four       indented·four        ← 4 空格被吃掉
      ········indented·eight  indented·eight       ← 8 空格被吃掉
      back·to·zero            back·to·zero
      "@                      "@

=== foreach-nested ===
IN :  foreach·($i·in·1..5)·{   OUT:  foreach·($i·in·1..5)·{
      ····if·($i·%·2·-eq·0)·{        if·($i·%·2·-eq·0)·{
      ········Write-Host·"even"      Write-Host·"even"
      ····}                          }
      }                              }

=== yaml ===
IN :  services:          OUT:  services:
      ··api:                   api:            ← YAML 結構完全被破壞
      ····build:               build:
      ······context:·.         context:·.

=== python ===
IN :  class·A:           OUT:  class·A:
      ····def·f(self):         def·f(self):    ← Python 直接無法執行
      ········return·1         return·1

=== comment-in-code ===
IN :  if·($true)·{       OUT:  if·($true)·{
      ····#·這行是註解          Write-Host·'hi'  ← 註解行被刪除
      ····Write-Host·'hi'      }
      }
```

這完全對得上使用者終端機看到的現象：

| 使用者觀察到的現象 | 對應原因 |
|---|---|
| here-string 測試印出 `0 spaces` × 4（預期 0/4/8/0） | 縮排在剪貼簿階段就被刪光 |
| 續行畫面 `∙ if ($i % 2 -eq 0) {` 全部靠左 | 同上 |
| 第 6 段自檢器印出 **PASS** | **該測試無效**，見第四節 |

---

## 二、根本原因（依嚴重度排序）

### BUG-1（致命）`Normalizer.cs:51-57` 無條件 `TrimStart()` 砍掉所有行首空白

```csharp
list = list
    .Select(l =>
    {
        string t = l.TrimStart();          // ← 元凶：對每一行都做，不管有沒有 prompt
        Match ps = PowerShellPrompt().Match(t);
        return ps.Success ? t[ps.Length..] : ShellPrompt().Replace(t, "");
    })
```

`Normalizer.cs:45-46` 已經正確處理過「共同縮排」（common indent dedent）：

```csharp
int commonIndent = nonEmpty.Min(l => l.TakeWhile(char.IsWhiteSpace).Count());
list = list.Select(l => l.Length >= commonIndent ? l[commonIndent..] : l).ToList();
```

也就是說 **去除 markdown fence 縮排的需求，在 45-46 行就已經滿足了**。54 行的 `TrimStart()` 是多餘的，它把「相對縮排」也一併消滅。

**修法**：只有在該行確實有 prompt 前綴時才使用 trim 後的字串；沒有 prompt 就原樣保留。例如

```csharp
.Select(l =>
{
    string t = l.TrimStart();
    Match ps = PowerShellPrompt().Match(t);
    if (ps.Success) return t[ps.Length..];
    if (ShellPrompt().IsMatch(t)) return ShellPrompt().Replace(t, "");
    return l;                               // 沒有 prompt → 縮排原封不動
})
```

同時 `Normalizer.cs:58` 的 `.Select(l => l.TrimEnd())` 也要留意：對一般文字無害，但會破壞 markdown 硬換行（行尾兩個空格）。優先度低。

---

### BUG-2（高）`Normalizer.cs:59` 刪掉所有以 `#` 開頭的行

```csharp
.Where(l => !l.TrimStart().StartsWith('#'))
```

原意是刪掉 AI 產生的「`# 安裝相依套件`」說明行，但它會無差別刪除：

- PowerShell / Python / Ruby / YAML / shell script 裡的**程式碼內註解**
- Python 的 `#!/usr/bin/env python` shebang
- C 前置處理器 `#include` / `#define`
- CSS 的 `#id { ... }` 選擇器
- Markdown 標題行

只要貼的是「程式碼」而不是「單行指令」，這個規則就是破壞性的。

**修法**：僅在「整段輸入看起來是命令列」時才啟用；或只刪除位於開頭/結尾、且後面確實接著命令行的註解區塊。最保守：加設定開關，預設關閉。

---

### BUG-3（高）`Normalizer.cs:27 / 64 / 83` 輸出使用裸 `\n`，未還原 CRLF

```csharp
string[] lines = input.Replace("\r\n", "\n").Split('\n');   // 27：輸入正規化為 LF
...
return string.Join("\n", list.Where(l => l.Length > 0));    // 64
return string.Join("\n", joined);                           // 83
```

Windows 剪貼簿 `CF_UNICODETEXT` 的慣例是 **CRLF**。`Clipboard.SetText()` 寫入純 LF 的字串後，下游（終端機貼上路徑 / PSReadLine bracketed paste / conhost 的 `\n → VK_RETURN` 轉換）可能對換行做二次轉換。

這與使用者觀察到的第二個症狀高度吻合：

> 每貼一段 N 行的程式碼，區塊後面就會多出 **正好 N 個** 空白續行 `∙`。
> 逐行核對：foreach 7 行 → 7 個 `∙`；function 10 行 → 10 個 `∙`；here-string 6 行 → 6 個 `∙`；`$lines = @(...)` 7 行 → 7 個 `∙`。**完全 1:1**。

**這一項尚未實測驗證，屬於高信心假設**，請 debug agent 先做第三節的實驗確認機制，再決定修法。

修法方向：`Clipboard.SetText()` 前把輸出換回 `\r\n`（或至少與輸入原本的行尾格式一致）。

**這也直接解釋了多行管線為什麼失敗**：

```
❯ Get-Process |
∙
ParserError: An empty pipe element is not allowed.
```

`Get-Process |` 之後 PowerShell 進入續行等待，但緊接著送進一個**空白行**，空白行終結了續行 → 管線尾端沒有元素 → `EmptyPipeElement`。而 `foreach {`、`@"`、`@(` 這幾種區塊之所以「看起來正常」，是因為空白行在大括號 / here-string / 陣列內是合法的，所以只是多出 `∙` 而不會報錯。

---

### BUG-4（中）`Normalizer.cs:10, 16` `^[>$]\s+` 會誤判

`> ` 開頭的行會被當成 shell prompt 剝掉。誤傷範圍：

- Markdown 引言 `> 這是引用`（已實測：`> quoted markdown line` → `quoted markdown line`）
- PowerShell 續行後貼回的內容
- diff / patch 的部分格式

優先度中。至少應該要求「整段輸入多數行都有 prompt」才啟用剝除（`multiPrompt` 的判斷已經算出來了，但 51-57 行的剝除**沒有**受 `multiPrompt` 保護）。

---

### BUG-5（中）`Normalizer.cs:67-82` `\` 與 `&&` 續行合併會破壞程式碼

在「單行 shell 指令」情境正確，但貼 C / JS / PowerShell 原始碼時：

```js
if (a &&
    b) { ... }
```

會被合併成一行。雖然多數語言仍可執行，但對已經被 BUG-1 砍掉縮排的程式碼而言是二次破壞。

---

### BUG-0（設計層，根源）`TrayApplication.cs:47-61` AutoMonitor 對**所有**複製動作無條件清洗

```csharp
private void OnClipboardChanged()
{
    ...
    _clipDebounce.Tick += (_, _) => { _clipDebounce.Stop(); CleanClipboard(showNotification: false); };
    ...
}
```

`CleanClipboard()` 只檢查 `Clipboard.ContainsText()`，沒有任何「這看起來像不像終端機指令」的判斷就直接呼叫 `Normalize()` 並寫回。

`README.md:34` 寫著：

> Plain text without command-shaped features is left untouched.

**這個說明與實作不符。** `Normalize()` 對任何多行文字都會執行 dedent + TrimStart + 刪 `#` 行 + 合併續行。使用者只要在 AutoMonitor 開啟時複製任何程式碼、YAML、Python、Markdown，內容就被改寫了。

**這是所有症狀的放大器**：即使 BUG-1~5 全修好，缺少「輸入是否為命令列」的判斷仍然讓工具在非預期場合介入。

**修法方向**：加入一個 gate，只有同時滿足以下條件才清洗（門檻可調）：
- 輸入被 ``` fence 包住，或
- 多數非空行帶有 `$ ` / `> ` / `PS ...>` prompt，或
- 只有單行，且不含明顯的程式碼結構符號

不符合時原樣放行。Alt+V 熱鍵可維持「強制清洗」語意，AutoMonitor 則走保守路線。

---

## 三、給 debug agent 的重現步驟

### 3-1 純函式層（不需要 GUI，最快）

```powershell
$dll = "C:\active\實用小東西\processing\pastefix\PasteFix\dotnet\src\PasteFix\bin\Release\net10.0-windows\PasteFix.dll"
$asm = [System.Reflection.Assembly]::LoadFrom($dll)
$m = $asm.GetType('PasteFix.Normalizer').GetMethod('Normalize')
$in = "class A:`n    def f(self):`n        return 1"
$out = $m.Invoke($null, @([string]$in))
($out -split "`n") | ForEach-Object { "|" + ($_ -replace ' ', '·') }
```

預期（修好後）：縮排完整保留。目前：全部靠左。

### 3-2 換行格式驗證（確認 BUG-3 機制）

```powershell
$out = $m.Invoke($null, @([string]"echo a`r`necho b"))
[int[]][char[]]$out    # 目前會看到 10 (LF)；預期應為 13,10 (CRLF)
```

再做端對端：

```powershell
Set-Clipboard -Value "foreach (`$i in 1..3) {`r`n    Write-Host `$i`r`n}"
Start-Sleep -Milliseconds 800     # 等 PasteFix 的 300ms debounce
$got = Get-Clipboard -Raw
[int[]][char[]]$got | Select-Object -First 200
```

檢查兩件事：(a) 縮排是否還在 (b) 換行是 `13,10` 還是只有 `10`。

### 3-3 終端機層（確認多出來的空白行）

在 WezTerm / Windows Terminal 各測一次，並比較 PasteFix **關閉** 與 **開啟** 的差異：

```powershell
Get-PSReadLineOption | Select-Object EditMode, ContinuationPrompt
$env:WT_SESSION; $env:TERM_PROGRAM
```

判定：如果關閉 PasteFix 後多餘 `∙` 消失，就確認是 BUG-3；如果仍在，則是終端機 / PSReadLine bracketed paste 設定問題，與 PasteFix 無關，需另案處理。

---

## 四、測試覆蓋率缺口

`NormalizerTests.cs` 目前 13 個測試，**沒有任何一個**驗證下列情境，這是缺陷能出貨的原因：

| 缺口 | 建議新增測試 |
|---|---|
| 縮排保留 | `Normalize("if (x) {\n    y\n}")` 應保留 4 空格 |
| YAML / Python 結構 | 巢狀縮排 round-trip |
| 程式碼內註解 | `"if (x) {\n    # c\n    y\n}"` 不應刪掉 `# c` |
| 輸出行尾格式 | 輸出應為 CRLF（或與輸入一致） |
| 非命令文字放行 | 一般散文 / markdown 應原樣返回 |
| `>` 誤判 | markdown 引言不應被剝除 |

另外 `tools/manual-test.ps1` 與 `test.txt` 本身的縮排也已經被吃掉（檔案內容全部靠左），推測是先前用同一條有問題的貼上路徑寫入的。修好後建議重新產生這兩個檔案。

---

## 五、非問題（排除項，避免誤導）

1. 使用者終端機出現的 `ParserError: Unexpected token '單行（基準測試）'` — 這是複製時把 markdown 標題文字一起框選進去了，屬於操作範圍問題，不是 PasteFix 的 bug。標題的 `**` 被去掉是聊天介面的 rich-text 複製行為，`Normalizer` 沒有處理 `**` 的邏輯。
2. 使用者跑的「第 6 段縮排自檢器」印出 **PASS**，這是**假陽性**。該測試的縮排全部寫在 `"    child"` 的**引號內部**，而每一行的行首縮排是 0，所以 `TrimStart()` 剛好沒有東西可砍。它沒有測到真正的問題。**不要拿這個 PASS 當作迴歸測試通過的依據。**

---

## 六、建議修復順序

1. **BUG-1**（`Normalizer.cs:54`）—— 一行改動，解掉最主要的破壞。
2. **BUG-3**（輸出 CRLF）—— 解掉多餘空白行與管線續行失效。
3. **BUG-0**（AutoMonitor 加入 command-shape gate）—— 從設計上避免誤傷。
4. **BUG-2 / BUG-4 / BUG-5** —— 收斂規則適用範圍。
5. 補上第四節的測試，再重新產生 `tools/manual-test.ps1` 與 `test.txt`。
