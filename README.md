# Pastefix

**Zero-friction clipboard optimizer for pasting terminal commands on Windows.**

Pastefix is a lightweight Windows background tray app. It automatically cleans and optimizes commands copied from AI chat windows (ChatGPT, Claude) or web pages so that pressing **`Ctrl+V`** or right-clicking in your terminal pastes clean, runnable commands in one go.

---

## What It Fixes

- **Cross-Shell Continuations:** Automatically joins lines ending in `\`, backtick, `&&`, or `|` into single executable commands.
- **Broken Command Lines:** Reconnects split lines when flags (like `--flag` or `-m`) or operators (`|`) wrap to the next line.
- **Common Indentation (Dedent):** Strips outer margins while preserving internal relative indents for Python, YAML, and here-strings.
- **Empty Pipeline Errors:** Drops empty lines between pipes to prevent PowerShell `EmptyPipeElement` parser errors.

---

## How It Works

1. **Copy as Usual:** When you copy any text (`Ctrl+C`), Pastefix automatically optimizes the clipboard in the background within 300ms.
2. **Paste Directly:** Switch to your terminal (WezTerm, Windows Terminal, PowerShell, etc.) and simply press **`Ctrl+V`** or right-click to paste.
3. **No New Hotkeys:** Operates entirely through standard clipboard events — no global hotkey conflicts and no keyboard interception.

---

## Download & Usage

1. Download the latest **`PasteFix.exe`** from [Releases](https://github.com/Ch1nYu/pastefix/releases).
2. Run `PasteFix.exe`. It sits quietly in the Windows system tray notification area (under the `^` hidden icons menu).
3. *(Optional)* To launch on Windows startup, place a shortcut to `PasteFix.exe` in your `shell:startup` folder.

- **Standalone Executable:** Single self-contained binary with embedded runtime (~51 MB, no installation needed).
- **Single-Instance:** Automatically prevents multiple background instances.
- **Config & Logs:** Stored locally in `%APPDATA%\PasteFix`.

