# Pastefix

**Zero-friction clipboard optimizer for pasting terminal commands on Windows.**

Pastefix is a lightweight Windows background tray app. It automatically cleans and optimizes commands copied from AI chat windows (ChatGPT, Claude) or web pages so that pressing **`Ctrl+V`** or right-clicking in your terminal pastes clean, runnable commands in one go.

---

## What It Fixes

- **Cross-Shell Continuations:** Automatically joins lines ending in `\`, backtick, `&&`, or `|` into single executable commands.
- **Broken Command Lines:** Reconnects split lines when flags (like `--flag` or `-m`) or operators (`|`) wrap to the next line.
- **Common Indentation (Dedent):** Strips outer margins while preserving internal relative indents for Python, YAML, and here-strings.
- **Empty Pipeline Errors:** Drops empty lines between pipes to prevent PowerShell `EmptyPipeElement` parser errors.
- **Invisible Characters:** Turns no-break spaces and other space lookalikes into ordinary spaces and removes zero-width characters, so `git commit` copied from a web page is two words again. Anything visible — curly quotes, dashes, the full-width space `　` — is left as copied.
- **Copied Wrappers:** Removes a markdown fence, a repeated shell prompt (`PS C:\> `, `$ `, `❯ `), or a line-number gutter (`1: `, `42| `) when it wraps the entire copy. A wrapper that does not cover everything — a fence around only part of the text, a single prompt line, a markdown list like `1. Run the installer` — is left untouched.

---

## How It Works

1. **Copy as Usual:** When you copy any text (`Ctrl+C`), Pastefix automatically optimizes the clipboard in the background within 300ms.
2. **Paste Directly:** Switch to your terminal (WezTerm, Windows Terminal, PowerShell, etc.) and simply press **`Ctrl+V`** or right-click to paste.
3. **No Keyboard Interception:** Operates entirely through standard clipboard events — no key injection and no keyboard hooks.
4. **Undo if It Got It Wrong:** The tray menu's **還原上一次清洗** puts the original copy back, for as long as the clipboard still holds what the clean produced. Nothing is written to disk; the way back lives in memory only.
5. **See What It Did:** Every clean adds one line to `%APPDATA%\PasteFix\pastefix.log` with sizes, line counts, which wrapper was removed, which mode ran, and how many invisible characters were fixed (`chars=`) — never the text itself. Copies that needed no cleaning are logged too, so the log can tell "nothing to do" apart from "never ran".

### Global Hotkey (optional, off by default)

**Ctrl+Alt+Shift+V** cleans the clipboard on demand, for copies made while the monitor was off or before Pastefix started. Enable it from the tray menu; if another program already owns the combination, the menu item reports it and stays off.

The key is registered with `RegisterHotKey`, which asks Windows to notify Pastefix when the combination is pressed. It does not send keystrokes and does not hook the keyboard.

### Programs That Borrow the Clipboard

Some programs put text on the clipboard only to paste it, then put the previous content back — dictation tools such as LazyTyper do this for every sentence. Those writes are not copies you made, so the monitor leaves them alone:

- a write tagged with the Windows convention `ExcludeClipboardContentFromMonitorProcessing` (or `Clipboard Viewer Ignore`) is never cleaned;
- a write from a program listed in `IgnoredProcesses` in `settings.json` is never cleaned. The default list is `["LazyTyper"]`; an empty list turns this off.

Each log line from the monitor ends with `owner=<process name>` (never content), and a skipped write is logged as `monitor skipped: owner=… reason=…`. If a program keeps showing up where you did not copy anything, add its `owner=` name to the list. Manual cleans — **立即清洗**, the hotkey, **壓成單行（這一次）** — are never filtered.

### Single Line, One Copy at a Time

Every copy is cleaned with **Smart Continuation**, which keeps structured multi-line content and joins recognized shell continuations. When one particular copy should be a single line, choose **壓成單行（這一次）** from the tray menu: it trims each physical line of that copy and joins every non-empty line with one space. The next copy is cleaned with Smart Continuation again, so there is no mode to switch back.

It flattens what you originally copied, not the Smart Continuation result, so **還原上一次清洗** afterwards gives back the original copy. Flattening is intentionally destructive for Python, YAML, here-strings, heredocs, and blocks containing multiple commands.

---

## Download & Usage

1. Download the latest **`PasteFix.exe`** from [Releases](https://github.com/Ch1nYu/pastefix/releases).
2. Run `PasteFix.exe`. It sits quietly in the Windows system tray notification area (under the `^` hidden icons menu).
3. *(Optional)* To launch on Windows startup, place a shortcut to `PasteFix.exe` in your `shell:startup` folder.

- **Standalone Executable:** Single self-contained binary with embedded runtime (~51 MB, no installation needed).
- **Single-Instance:** Automatically prevents multiple background instances.
- **Config & Logs:** Stored locally in `%APPDATA%\PasteFix`.
