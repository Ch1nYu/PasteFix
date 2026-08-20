# Pastefix Development & Architecture Handoff

> **Target Audience:** Future AI coding agents and human maintainers.  
> **Last Updated:** 2026-08-21 (v1.2.0)  
> **Repository:** `C:\active\實用小東西\processing\pastefix`

---

## 1. Project Overview & Core Philosophy

Pastefix is a zero-friction, lightweight Windows background utility (.NET 10 WinForms Tray Application) designed to solve real-world terminal pasting breakages when copying commands or scripts from AI chat interfaces (ChatGPT, Claude, web docs).

### Core Principles
1. **Pure Clipboard-In → Clipboard-Out:** Pastefix operates solely on clipboard text updates (`WM_CLIPBOARDUPDATE`). It does **not** intercept keystrokes, does not hook keyboard drivers, and does not simulate virtual key events (`SendInput`).
2. **Zero Learning Curve:** The user copies with standard `Ctrl+C`, Pastefix cleans the clipboard in the background within 300ms, and the user pastes with standard `Ctrl+V` or mouse right-click into any terminal emulator.
3. **No Destructive Guessing:** Pastefix does **not** strip shell prompts (`$ `, `PS >`), does not delete comments (`#`), and does not strip Markdown fences (````). It only fixes indentation and syntax-neutral continuation glue.

---

## 2. System Architecture & Data Flow

```
[User copies text anywhere (Ctrl+C / Selection)]
                      │
                      ▼
             WM_CLIPBOARDUPDATE
                      │
                      ▼
     MessageWindow.cs (AddClipboardFormatListener)
                      │
                      ▼
         300ms Debounce Timer (TrayApplication.cs)
                      │
                      ▼
            OwnWriteGate.cs
       (Matches GetClipboardSequenceNumber()
        to skip self-triggered clipboard updates)
                      │
                      ▼
            Normalizer.cs (Pure Functional Core)
            ├── 1. Dedent (strip common outer whitespace)
            ├── 2. FirstLineMissedTheMargin (repair asymmetric selection)
            └── 3. JoinContinuations (glue \, `, &&, trailing |, & leading --flags)
                      │
                      ▼
         Clipboard.SetText(cleaned)
                      │
                      ▼
[User presses Ctrl+V or Right-Clicks in WezTerm / Windows Terminal]
```

### File Responsibilities

| File | Path | Responsibility |
|---|---|---|
| **Program.cs** | `PasteFix/dotnet/src/PasteFix/Program.cs` | Entry point. Manages `PasteFix_App_SingleInstance_Mutex` to ensure strictly one running instance and handles unhandled exception logging. |
| **Normalizer.cs** | `PasteFix/dotnet/src/PasteFix/Normalizer.cs` | Pure, fully unit-tested normalization logic (`Dedent` + `Normalize`). 100% deterministic and stateless. |
| **OwnWriteGate.cs** | `PasteFix/dotnet/src/PasteFix/OwnWriteGate.cs` | Remembers the Win32 `GetClipboardSequenceNumber()` when Pastefix writes back to the clipboard, preventing infinite loop re-entry. |
| **MessageWindow.cs**| `PasteFix/dotnet/src/PasteFix/MessageWindow.cs`| Hidden `NativeWindow` that registers `AddClipboardFormatListener` to receive `WM_CLIPBOARDUPDATE`. |
| **TrayApplication.cs**| `PasteFix/dotnet/src/PasteFix/TrayApplication.cs`| WinForms `ApplicationContext` managing system tray icon, context menu, 300ms debounce timer, and clean orchestration. |
| **Settings.cs** | `PasteFix/dotnet/src/PasteFix/Settings.cs` | JSON serialization for `%APPDATA%\PasteFix\settings.json` (`AutoMonitor` toggle). |

---

## 3. Hard-Won Lessons & Non-Obvious Gotchas

Future agents **MUST** review these design decisions before proposing changes:

### ⚠️ Gotcha 1: Why Keyboard Simulation / Global Hotkeys Were Removed
- **History:** Earlier versions attempted to use `Alt+V` with `SendInput` to simulate pasting into target windows, and a keyboard hook (`WH_KEYBOARD_LL`) was once tested to intercept `Ctrl+V`.
- **The Failure Mode:** When the user holds down physical modifier keys (e.g. holding `Ctrl` while pressing `V`), simulated character strokes combined with the held `Ctrl` key resulted in unintended shortcuts (e.g. `Ctrl+T` opened dozens of tabs in WezTerm, `Ctrl+W` closed tabs).
- **Rule:** **Never re-introduce simulated key injection (`SendInput` / `keybd_event`) or low-level keyboard hooks.** Keep the tool strictly focused on clipboard modification.

### ⚠️ Gotcha 2: The PowerShell / WezTerm "Reverse Paste" Root Cause
- **Symptom:** Pasting multi-line text into WezTerm running PowerShell occasionally executed lines in reverse order (LIFO: bottom line executed first).
- **Root Cause:** In WezTerm, configuring `config.canonicalize_pasted_newlines = "LineFeed"` strips `\r` and forces raw `\n` into Windows ConPTY. When PSReadLine receives raw `\n` under certain prompt/continuation states, its asynchronous stream parser processes stream chunks out of order.
- **Fix:** Retain native Windows line breaks (`CRLF` / `CR`). WezTerm should **not** force `LineFeed` canonicalization on Windows.

### ⚠️ Gotcha 3: The Leading Flag Continuation Rule (`--flag`)
- **Problem:** Commands wrapped due to window width or AI formatting often wrap at flags (e.g. `gh release create ...\n--notes-file ...`) without a trailing `\` or backtick.
- **Rule:** `Normalizer.JoinContinuations` inspects whether the *next* non-empty line begins with a CLI flag (regex: `^(?:--?[a-zA-Z]|&&|\|\|?)`). If the current line does not end in a colon (`:`), semicolon, or brace, it is glued into the command while protecting YAML list items (`- item`) and Python syntax.

### ⚠️ Gotcha 4: Self-Triggered Clipboard Re-Entry
- **Rule:** Do not rely on string equality or unhooking listeners to prevent self-triggering loops. Use Win32 `GetClipboardSequenceNumber()` via `OwnWriteGate`.

---

## 4. Verification & Testing Playbook

Always run all test gates before publishing or merging:

### 1. Unit Tests (xUnit)
```powershell
dotnet test PasteFix/dotnet/PasteFix.slnx -c Release --no-restore
```
- Target: 70+ tests covering Dedent, margin repair, continuation joining, flag joining, quote/bracket guards, YAML/Python preservation, idempotency, and OwnWriteGate.

### 2. Automated Machine Smoke Test
```powershell
pwsh -NoProfile -File tools\manual-test.ps1
```
- Runs end-to-end clipboard operations against the active tray process.

### 3. Tray Toggle Regression Test
```powershell
pwsh -NoProfile -File tools\toggle-test.ps1
```

### 4. Build and Publish Artifact
```powershell
# Stop running instance first
Get-Process PasteFix -ErrorAction SilentlyContinue | Stop-Process -Force

# Publish self-contained single-file binary
dotnet publish PasteFix/dotnet/src/PasteFix/PasteFix.csproj -c Release -r win-x64 `
  --self-contained true -o PasteFix/dist -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true --no-restore

# Launch updated tray app
Start-Process "PasteFix\dist\PasteFix.exe" -WindowStyle Hidden
```

---

## 5. Roadmap & Guidelines for Future Work

- **Keep Dependencies at Zero:** Do not add third-party NuGet packages unless strictly necessary. The project uses standard .NET 10 runtime libraries.
- **Preserve .gitattributes:** Ensure `.gitattributes` maintains `linguist-vendored` for `PasteFix/legacy/**` and `tools/**` so GitHub language statistics reflect 100% C#.

