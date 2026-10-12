# Pastefix Development & Architecture Handoff

> **Target Audience:** Future AI coding agents and human maintainers.  
> **Last Updated:** 2026-10-12 (v1.3.0)
> **Repository:** `C:\active\實用小東西\processing\pastefix`

---

## 1. Project Overview & Core Philosophy

Pastefix is a zero-friction, lightweight Windows background utility (.NET 10 WinForms Tray Application) designed to solve real-world terminal pasting breakages when copying commands or scripts from AI chat interfaces (ChatGPT, Claude, web docs).

### Core Principles
1. **Pure Clipboard-In → Clipboard-Out:** Pastefix operates solely on clipboard text updates (`WM_CLIPBOARDUPDATE`). It does **not** intercept keystrokes, does not hook keyboard drivers, and does not simulate virtual key events (`SendInput`).
2. **Zero Learning Curve:** The user copies with standard `Ctrl+C`, Pastefix cleans the clipboard in the background within 300ms, and the user pastes with standard `Ctrl+V` or mouse right-click into any terminal emulator.
3. **Explicit Processing Mode:** Smart Continuation remains the safe default. Force Single Line is an explicit, persisted opt-in for flattening every physical line.
4. **No Destructive Guessing in Smart Mode:** Smart mode does **not** strip shell prompts (`$ `, `PS >`), delete comments (`#`), or strip Markdown fences (````).

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
       ClipboardTransformer.cs (Mode Dispatcher)
            ├── SmartContinuation → Normalizer.Normalize
            │   ├── Dedent
            │   ├── FirstLineMissedTheMargin
            │   └── JoinContinuations
            └── SingleLine → SingleLineNormalizer.Flatten
                      │
                      ▼
       ClipboardWriteback.Decide (pure)
            ├── EmptyResult   → keep the clipboard as copied
            ├── NothingToDo   → keep the clipboard as copied
            └── Write         → Clipboard.SetText(cleaned)
                                 └── ExternalException → log + warn, paste goes out uncleaned
                      │
                      ▼
[User presses Ctrl+V or Right-Clicks in WezTerm / Windows Terminal]
```

### File Responsibilities

| File | Path | Responsibility |
|---|---|---|
| **Program.cs** | `PasteFix/dotnet/src/PasteFix/Program.cs` | Entry point. Manages `PasteFix_App_SingleInstance_Mutex` to ensure strictly one running instance and handles unhandled exception logging. |
| **Normalizer.cs** | `PasteFix/dotnet/src/PasteFix/Normalizer.cs` | Pure, fully unit-tested normalization logic (`Dedent` + `Normalize`). 100% deterministic and stateless. |
| **SingleLineNormalizer.cs** | `PasteFix/dotnet/src/PasteFix/SingleLineNormalizer.cs` | Explicitly flattens physical lines and removes only scanner-confirmed `\` or backtick continuation markers. |
| **ClipboardTransformer.cs** | `PasteFix/dotnet/src/PasteFix/ClipboardTransformer.cs` | Selects exactly one transformer from the persisted `ProcessingMode`. |
| **ClipboardWriteback.cs** | `PasteFix/dotnet/src/PasteFix/ClipboardWriteback.cs` | Pure decision on whether the cleaned text replaces the clipboard. Keeps the skip cases testable without a real clipboard. |
| **ClipboardSummary.cs** | `PasteFix/dotnet/src/PasteFix/ClipboardSummary.cs` | Describes clipboard text for the log by length only. `pastefix.log` is permanent plain text and passwords get copied too. |
| **OwnWriteGate.cs** | `PasteFix/dotnet/src/PasteFix/OwnWriteGate.cs` | Remembers the Win32 `GetClipboardSequenceNumber()` when Pastefix writes back to the clipboard, preventing infinite loop re-entry. |
| **MessageWindow.cs**| `PasteFix/dotnet/src/PasteFix/MessageWindow.cs`| Hidden `NativeWindow` that registers `AddClipboardFormatListener` to receive `WM_CLIPBOARDUPDATE`. |
| **TrayApplication.cs**| `PasteFix/dotnet/src/PasteFix/TrayApplication.cs`| WinForms `ApplicationContext` managing system tray icon, context menu, 300ms debounce timer, and clean orchestration. |
| **Settings.cs** | `PasteFix/dotnet/src/PasteFix/Settings.cs` | JSON serialization for `%APPDATA%\PasteFix\settings.json` (`AutoMonitor` and `ProcessingMode`). Missing or invalid modes fall back to Smart Continuation. |

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

### ⚠️ Gotcha 5: Processing Modes Must Stay Isolated
- `AutoMonitor` controls whether clipboard updates are processed. `ProcessingMode` controls which transformer handles the text.
- `ClipboardTransformer.Transform` must call exactly one transformer. Never run Smart Continuation before or after Force Single Line.
- Force Single Line intentionally destroys multi-line structure. Keep it opt-in and keep Smart Continuation as the upgrade-safe default.

### ⚠️ Gotcha 6: `Clipboard.SetText` Rejects Empty Text, and `Application.ThreadException` Hides Every Failure
- `Clipboard.SetText("")` throws `ArgumentNullException` (`ThrowIfNullOrEmpty`). Cleaning a copy that held only whitespace produces exactly that empty string, so the write-back must be skipped — `ClipboardWriteback.Decide` returns `EmptyResult` for it. Six such crashes are in `pastefix.log` between 2026-09-03 and 2026-10-10.
- `Program.cs` installs an `Application.ThreadException` handler so a clipboard error cannot kill the tray. The cost is that a failure is **silent**: the log records it, the tray keeps running, and the only symptom is a paste that was not cleaned. Thirteen `ExternalException 0x800401D0 CLIPBRD_E_CANT_OPEN` failures were logged the same way before anyone noticed.
- **Rule:** treat every exception on the clipboard path as something the user must be able to see. Log it **and** surface it; do not let `ThreadException` be the only witness. Before shipping, check `pastefix.log` for `ThreadException` entries — that is how these two were found.
- Do not add a retry loop around `SetText`: it already retries ten times over a full second before throwing.

---

## 4. Verification & Testing Playbook

Always run all test gates before publishing or merging:

### 1. Unit Tests (xUnit)
```powershell
dotnet test PasteFix/dotnet/PasteFix.slnx -c Release --no-restore
```
- Target: 90+ tests covering Dedent, margin repair, SmartContinuation, SingleLine flattening, mode dispatch/settings, write-back decisions, quote/bracket guards, YAML/Python behavior, idempotency, and OwnWriteGate.

### 2. Automated Machine Smoke Test
```powershell
pwsh -NoProfile -File tools\manual-test.ps1
```
- Runs end-to-end clipboard operations against the active tray process.

### 3. Tray Toggle Regression Test
```powershell
pwsh -NoProfile -File tools\toggle-test.ps1
```
- Covers AutoMonitor off, both processing modes, and mode persistence across a restart. Restores `settings.json`, the clipboard, and the original tray process on exit.

### 4. Check the Log for Swallowed Exceptions
```powershell
Select-String -Path "$env:APPDATA\PasteFix\pastefix.log" -Pattern 'ThreadException' | Select-Object -Last 20
```
- A `ThreadException` entry means the tray survived an error the user never saw. Investigate before every release; both v1.3.0 crash fixes were found this way.

### 5. Build and Publish Artifact
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
