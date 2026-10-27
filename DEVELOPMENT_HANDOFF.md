# Pastefix Development & Architecture Handoff

> **Target Audience:** Future AI coding agents and human maintainers.  
> **Last Updated:** 2026-10-12 (v1.4.0 + post-release mode logging)
> **Repository:** `C:\active\實用小東西\processing\pastefix`

---

## 1. Project Overview & Core Philosophy

Pastefix is a zero-friction, lightweight Windows background utility (.NET 10 WinForms Tray Application) designed to solve real-world terminal pasting breakages when copying commands or scripts from AI chat interfaces (ChatGPT, Claude, web docs).

### Core Principles
1. **Pure Clipboard-In → Clipboard-Out:** Pastefix operates solely on clipboard text updates (`WM_CLIPBOARDUPDATE`). It does **not** intercept keystrokes, does not hook keyboard drivers, and does not simulate virtual key events (`SendInput`).
2. **Zero Learning Curve:** The user copies with standard `Ctrl+C`, Pastefix cleans the clipboard in the background within 300ms, and the user pastes with standard `Ctrl+V` or mouse right-click into any terminal emulator.
3. **Explicit Processing Mode:** Smart Continuation remains the safe default. Force Single Line is an explicit, persisted opt-in for flattening every physical line.
4. **No Destructive Guessing in Smart Mode:** Smart mode does **not** strip shell prompts (`$ `, `PS >`), delete comments (`#`), or strip Markdown fences (````).
5. **Nothing Is Rewritten Without a Visible Trace:** Every clean writes one permanent log line (sizes, line counts, containers, and the mode — never content), including the copies that needed no cleaning. Until v1.4.0 only failures were logged, so a working tool left no evidence it had worked, and there was no way to answer which mode a given copy wanted.
6. **Every Rewrite Has a Way Back:** A clean that replaced the clipboard can be undone from the tray menu for as long as the clipboard still holds what the clean produced. AutoMonitor had been toggled off eight times before this existed — an automatic action with no undo can only be avoided, not corrected.

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
        HtmlClipboard.Classify (pure, CF_HTML → CopySource)
            └── text | html | mixed | code:<language>
                      │
                      ▼
       ClipboardTransformer.cs (Mode Dispatcher)
            ├── CharacterNormalizer.Clean (pure)
            │   └── NBSP & space lookalikes → space, zero-width → removed
            └── ContainerStripper.Strip (pure)
                ├── Fence       (``` or ~~~ wrapping the whole copy)
                ├── Prompt      (PS C:\> / $ / ❯ on every non-blank line, 2+)
                └── LineNumber  (1: / 1| / 1\t running in sequence, 2+)
                      │
                      ├── SmartContinuation → Normalizer.Normalize
                      │   ├── Dedent
                      │   ├── FirstLineMissedTheMargin
                      │   └── JoinContinuations
                      └── SingleLine → SingleLineNormalizer.Flatten
                          (unless CopySource.KeepsLineStructure → Normalize, guard=<language>)
                      │
                      ▼
       ClipboardWriteback.Decide (pure)
            ├── EmptyResult   → keep the clipboard as copied
            ├── NothingToDo   → keep the clipboard as copied
            └── Write         → Clipboard.SetText(cleaned)
                                 └── ExternalException → log + warn, paste goes out uncleaned
                      │
                      ▼
       LastClean.Record (in-memory only, never on disk)
            └── 還原上一次清洗 → Clipboard.SetText(before)
                      │
                      ▼
[User presses Ctrl+V or Right-Clicks in WezTerm / Windows Terminal]
```

### File Responsibilities

| File | Path | Responsibility |
|---|---|---|
| **Program.cs** | `PasteFix/dotnet/src/PasteFix/Program.cs` | Entry point. Manages `PasteFix_App_SingleInstance_Mutex` to ensure strictly one running instance and handles unhandled exception logging. |
| **Normalizer.cs** | `PasteFix/dotnet/src/PasteFix/Normalizer.cs` | Pure, fully unit-tested normalization logic (`Dedent` + `Normalize`). 100% deterministic and stateless. Also hosts `SplitLines` and the shared `ScanState` / `ScanLine` quote-and-bracket scanner used by `SingleLineNormalizer`. |
| **CharacterNormalizer.cs** | `PasteFix/dotnet/src/PasteFix/CharacterNormalizer.cs` | Pure. Replaces space lookalikes (U+00A0, U+2000–200A, U+202F, U+205F) with a space and removes U+200B, U+2060, U+FEFF, U+00AD. Never touches anything visible (curly quotes, dashes, U+3000) or meaningful (ZWJ/ZWNJ). Returns the fix count for `chars=`. |
| **HtmlClipboard.cs** | `PasteFix/dotnet/src/PasteFix/HtmlClipboard.cs` | Pure. Classifies the CF_HTML fragment: one `<pre>`/`<code>` covering the whole selection is `Code` with a language from a fixed list (else `other` — the log never carries page markup); anything less is `Mixed`, `Html` or `PlainText` and changes no behavior. `KeepsLineStructure` is the only thing that alters a transform. |
| **ContainerStripper.cs** | `PasteFix/dotnet/src/PasteFix/ContainerStripper.cs` | Pure wrapper removal: a markdown fence, a repeated shell prompt, or a line-number gutter. Removes a wrapper only when it provably covers the whole copy; text with no wrapper is returned byte for byte. |
| **SingleLineNormalizer.cs** | `PasteFix/dotnet/src/PasteFix/SingleLineNormalizer.cs` | Explicitly flattens physical lines and removes only scanner-confirmed `\` or backtick continuation markers. |
| **ClipboardTransformer.cs** | `PasteFix/dotnet/src/PasteFix/ClipboardTransformer.cs` | Strips the container first, then selects exactly one transformer from the persisted `ProcessingMode`. Returns a `ClipboardResult` (text + which containers were stripped) so the log can report what happened without naming content. |
| **ClipboardWriteback.cs** | `PasteFix/dotnet/src/PasteFix/ClipboardWriteback.cs` | Pure decision on whether the cleaned text replaces the clipboard. Keeps the skip cases testable without a real clipboard. |
| **ClipboardSummary.cs** | `PasteFix/dotnet/src/PasteFix/ClipboardSummary.cs` | Describes clipboard text for the log by length, line counts, containers, and mode only. `pastefix.log` is permanent plain text and passwords get copied too. The mode is recorded on every path — including the skips, which are the control group for the open question in section 5. |
| **LastClean.cs** | `PasteFix/dotnet/src/PasteFix/LastClean.cs` | Holds the text from before the last successful write-back so one clean can be undone. **In memory only** — a restore file would defeat the `len=N` log rule. |
| **HotkeyWindow.cs** | `PasteFix/dotnet/src/PasteFix/HotkeyWindow.cs` | Registers one global hotkey (`Ctrl+Alt+Shift+V`) via `RegisterHotKey` and raises `Pressed`. This is not input simulation; see gotcha 1. |
| **OwnWriteGate.cs** | `PasteFix/dotnet/src/PasteFix/OwnWriteGate.cs` | Remembers the Win32 `GetClipboardSequenceNumber()` when Pastefix writes back to the clipboard, preventing infinite loop re-entry. |
| **MessageWindow.cs**| `PasteFix/dotnet/src/PasteFix/MessageWindow.cs`| Hidden `NativeWindow` that registers `AddClipboardFormatListener` to receive `WM_CLIPBOARDUPDATE`. |
| **TrayApplication.cs**| `PasteFix/dotnet/src/PasteFix/TrayApplication.cs`| WinForms `ApplicationContext` managing system tray icon, context menu, 300ms debounce timer, and clean orchestration. |
| **Settings.cs** | `PasteFix/dotnet/src/PasteFix/Settings.cs` | JSON serialization for `%APPDATA%\PasteFix\settings.json` (`AutoMonitor`, `Mode`, `HotkeyEnabled`). Every property is validated in `FromJson`; anything missing or invalid falls back to the default rather than throwing, so an old settings file always loads. |

---

## 3. Hard-Won Lessons & Non-Obvious Gotchas

Future agents **MUST** review these design decisions before proposing changes:

### ⚠️ Gotcha 1: Why Keyboard Simulation Was Removed — but `RegisterHotKey` Is Fine
- **History:** Earlier versions attempted to use `Alt+V` with `SendInput` to simulate pasting into target windows, and a keyboard hook (`WH_KEYBOARD_LL`) was once tested to intercept `Ctrl+V`.
- **The Failure Mode:** When the user holds down physical modifier keys (e.g. holding `Ctrl` while pressing `V`), simulated character strokes combined with the held `Ctrl` key resulted in unintended shortcuts (e.g. `Ctrl+T` opened dozens of tabs in WezTerm, `Ctrl+W` closed tabs).
- **Rule:** **Never re-introduce simulated key injection (`SendInput` / `keybd_event`) or low-level keyboard hooks.** Keep the tool strictly focused on clipboard modification.
- **The nuance:** v1.2.0 removed the `Alt+V` hotkey together with the simulation, and the README then advertised "No New Hotkeys" as a feature. Those were two separate decisions. `RegisterHotKey` asks Windows to deliver `WM_HOTKEY` when the user presses a combination — it sends nothing and hooks nothing, and its handler runs the same `CleanClipboard` path the tray menu already runs. v1.4.0 reintroduced exactly that, off by default. **The ban is on synthesising input, not on owning a key combination.**

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

### ⚠️ Gotcha 7: Container Stripping Must Stay All-or-Nothing
- **History:** The 2026-08-08 bug report was caused by rules that decided, line by line, which line was a command — YAML structure was destroyed, Python indentation eaten, comments deleted. Those rules were removed on 2026-08-09 (`docs/records/PLAN-2026-08-09.md`).
- **The distinction that makes `ContainerStripper` safe:** no rule in it decides what a line *means*. A wrapper is either provably uniform across the entire copy (the fence opens on line 1 and closes on the last with nothing fence-shaped between; every non-blank line carries the same prompt style; the numbers run in sequence) or it is not a wrapper and the text is returned byte for byte.
- **The visible-miss trade:** `1. ` / `1) ` are markdown ordered-list syntax, not gutter syntax, and are therefore never stripped — a numbered code block written that way is a miss the user can see. A single `$ npm install` is not stripped either: a gutter needs 2+ lines to have a side. Always prefer a visible miss over silent damage.
- **The fence guard test in `tools/manual-test.ps1` was broken for three releases:** it asserted on `'''bash` (three apostrophes) rather than backticks, so it never exercised a real fence. PowerShell treats a backtick as an escape character inside double-quoted strings — build fence literals from `[char]0x60`.

### ⚠️ Gotcha 8: The Log Is the Only Record of What the Tool Did

- `pastefix.log` is the sole durable trace. Clipboard content never reaches it (see the `len=N` rule), so the mode field is what makes the log answer *which transformer ran on this copy* after the fact. Dropping the mode from a log line removes the only evidence for the open question in section 5.
- **Do not make the mode field optional on the write path.** The skips matter as much as the writes: a copy neither mode would touch is the control group. A log that only records changes cannot distinguish "nothing to do" from "never ran" — that gap is exactly why v1.4.0 added the skip line.
- **Do not trust the version string to tell you whether the mode field is present.** `334d377` added `mode=` without bumping `<Version>`, so the local `PasteFix/dist/PasteFix.exe` was republished at 18:30:30 — after that commit — and still reports `1.4.0+65e04c7a86e1dfe5e71b66d5b3b85d592d340e1d`, the same version as the GitHub `v1.4.0` asset, which was built from `65e04c7` and has no field. Two different binaries, one version string. The only reliable test is whether the log lines themselves contain `mode=`.
- **Roadmap items may be implemented after a tag without a new version.** `334d377` and `8972163` are on `main` but inside no release, and `<Version>` still reads `1.4.0`. When counting the two weeks of evidence in section 5, count lines that contain `mode=` rather than lines from a version range.

---

## 4. Verification Playbook

Always run all test gates before publishing or merging:

### 1. Unit Tests (xUnit)
```powershell
dotnet test PasteFix/dotnet/PasteFix.slnx -c Release --no-restore
```
- Target: 212 tests covering invisible-character fixes, CF_HTML classification and the Single Line structure guard, Dedent, margin repair, SmartContinuation, SingleLine flattening, container stripping, mode dispatch/settings, write-back decisions, restore decisions, the log summary format, quote/bracket guards, YAML/Python behavior, idempotency, and OwnWriteGate.

### 2. Automated Machine Smoke Test
```powershell
pwsh -NoProfile -File tools\manual-test.ps1
```
- Runs end-to-end clipboard operations against the active tray process.

### 3. Tray Toggle Regression Test
```powershell
pwsh -NoProfile -File tools\toggle-test.ps1
```
- Covers AutoMonitor off, both processing modes, mode persistence across a restart, the successful-clean log line (sizes, counts, and mode — never content), the mode recorded on a copy that needed no cleaning, and hotkey registration. Restores `settings.json`, the clipboard, and the original tray process on exit.

### 4. Tray Menu End-to-End Test
```powershell
pwsh -NoProfile -File tools\verify-v14.ps1
```
- Drives the real tray menu through `tools\tray-menu.ps1` (posts the `NotifyIcon` callback message, then uses UI Automation to invoke an item — no `SendInput`, no mouse coordinates, no keyboard hook).
- Verifies what unit tests cannot: the menu is actually wired to the code, a real clean writes a real `cleaned:` line, **還原上一次清洗** puts the original text back, the restore is single-shot, and it refuses to overwrite content copied since the clean.
- Requires the tray to be running. If it reports that the menu never appeared, check `Get-Process PasteFix` first — a stale or absent process is the usual cause, not the helper.

### 5. Check the Log for Swallowed Exceptions
```powershell
Select-String -Path "$env:APPDATA\PasteFix\pastefix.log" -Pattern 'ThreadException' | Select-Object -Last 20
```
- A `ThreadException` entry means the tray survived an error the user never saw. Investigate before every release; both v1.3.0 crash fixes were found this way.

### 6. Build and Publish Artifact
```powershell
# Stop running instance first
Get-Process PasteFix -ErrorAction SilentlyContinue | Stop-Process -Force

# Publish self-contained single-file binary
dotnet publish PasteFix/dotnet/src/PasteFix/PasteFix.csproj -c Release -r win-x64 `
  --self-contained true -o PasteFix/dist -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true

# Launch updated tray app
Start-Process "PasteFix\dist\PasteFix.exe" -WindowStyle Hidden
```

---

## 5. Roadmap & Guidelines for Future Work

- **Keep Dependencies at Zero:** Do not add third-party NuGet packages unless strictly necessary. The project uses standard .NET 10 runtime libraries.
- **Preserve .gitattributes:** Ensure `.gitattributes` maintains `linguist-vendored` for `PasteFix/legacy/**` and `tools/**` so GitHub language statistics reflect 100% C#.

### Open Question: Should the Two Modes Be Merged?

Do not merge `SmartContinuation` and `SingleLine` without evidence. The log now provides it, and nothing else does.

**Why they are separate:** they are not interchangeable. Smart Continuation rejoins marked continuations and leaves everything else alone; Single Line collapses every physical line and is deliberately destructive to Python, YAML, and heredocs. The user switched between them **32 times between 2026-09-07 and 2026-10-07 — 16 in and 16 out**, five of those round trips within a single minute, which means the switch is usually correcting a wrong mode rather than expressing a lasting preference. The longest Single Line stretch was 1 day 6 hours, so it is not a mode that should never be used.

**The prerequisite is data, not design.** Merging on a guess would replace 32 human judgements with an untested heuristic — exactly the class of change that caused the 2026-08-08 incident. There is no data yet: before `334d377`, no log line said which mode ran, so the 32 switches have no matching record of what the copies actually needed. The two weeks of evidence start from the first `mode=` line, not from the switches.

**What to read when the data has accumulated:** every permanent log line from a build that carries `mode=` records it, including the path that changed nothing. The skipped copies are the control group.

```
cleaned: len=17->17 lines=2->1 containers=none mode=SingleLine
cleaned: len=29->17 lines=4->2 containers=Fence mode=SmartContinuation
clean skipped, no change needed: len=41 mode=SmartContinuation
```

`lines=A->B` is the discriminator: `2->1` is a copy that needed collapsing, `4->4` is one that was already fine, and a skip is one neither mode would touch. Count the ratio, then decide.

```powershell
Get-Content "$env:APPDATA\PasteFix\pastefix.log" |
  Select-String 'mode=SingleLine|clean skipped' |
  Select-Object -Last 100
```

**Before counting, check the build.** `git show v1.4.0:PasteFix/dotnet/src/PasteFix/ClipboardSummary.cs` contains no `mode=`; the field arrived in `334d377`, which is on `main` inside no release. The `v1.4.0` GitHub asset is `1.4.0+65e04c7` and also lacks it. Only lines from a build reporting a version at or after `334d377` are usable evidence.
