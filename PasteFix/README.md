# Pastefix

**Makes a multi-line command copied from a chat window pasteable into a terminal in one go.**

A small Windows tray application. It operates as a background clipboard optimizer to ensure commands copied from AI chat interfaces paste cleanly and execute reliably in terminal environments.

> **Documentation of Record:** This document describes current behavior (v1.2.0). Historical records and design rationales live under `docs/records/`.

---

## The Problems

When copying scripts from Claude or ChatGPT into a Windows terminal (WezTerm, Windows Terminal, PowerShell), several common issues occur:

1. **Unwanted Common Indentation:** Selecting code quoted in Markdown lists includes leading whitespace, causing the terminal shell to see commands starting columns in.
2. **Cross-Shell Continuation Breakage:** Bash uses trailing `\` while PowerShell uses a trailing backtick (`` ` ``). Pasting a Bash command into PowerShell breaks the arguments across lines and fails.
3. **Broken Pipelines from Blank Lines:** Copying multi-line pipelines with blank lines (`Get-Process | \n\n Select-Object`) causes PowerShell to fail with `An empty pipe element is not allowed`.
4. **Mouse Selection Margin Asymmetry:** Drag-selecting text from a terminal UI often catches the margin on lines 2..n while line 1 starts at column 0, leaving an asymmetric indent that ordinary dedent tools cannot fix.

---

## What It Does

### 1. Strips Outer Indentation (Dedent)
Removes common leading whitespace while preserving relative indentation inside Python blocks, YAML scalars, and here-strings.

```
    if (x) {                 if (x) {
        y = 1        →           y = 1
    }                        }
```

### 2. Joins Continuation Lines
Glues continuation markers (`\`, backtick, `&&`, or trailing `|`) into single commands:

```
docker run \
    -e FOO=bar \      →    docker run -e FOO=bar image
    image
```

Once continuation markers are consumed, **the command becomes shell-agnostic** — there is no continuation syntax left to get wrong.

### 3. Repairs Missed Left Margins
When line 1 has no indent and is a complete syntactic statement, while lines 2..n share extra spaces, Pastefix removes the stray margin automatically.

### 4. Cleans Empty Lines in Continuations
Blank lines caught after a continuation marker or pipe are dropped, preventing pipeline termination errors.

---

## How It Operates

| Trigger | Action | Details |
|---|---|---|
| **全自動監聽** (*Auto monitor*, default on) | Normalize on copy | Operates in background within 300ms of any clipboard copy |
| **立即清洗** (*Clean now*, tray icon) | Manual normalize | Triggered from tray menu or icon double-click |

Because AutoMonitor normalizes the clipboard immediately at **copy time**, you simply use standard **`Ctrl+V`** or mouse right-click to paste into WezTerm, Windows Terminal, or any application. No custom hotkeys or keyboard interception hooks are required.

---

## Safety Guards (What It Deliberately Does NOT Touch)

- **No Shell Translation:** `$VAR`, `$(...)`, path separators (`/` vs `\`) remain untouched.
- **Bracket Depth & Quote Protection:** Markers inside unclosed parentheses (`if (a &&`), braces (`do { \`), single/double quotes, or here-strings are never merged.
- **Windows Path Protection:** A trailing backslash attached to a path token (e.g. `cd C:\temp\`) is guarded and never treated as a continuation.
- **No Prompt Stripping / No Comment Stripping:** Comments (`#`) and markdown blocks are preserved.

---

## Terminal Setup & Troubleshooting

### Reverse-Order Paste in PowerShell
If pasting multi-line text into PowerShell outputs lines in reverse order (bottom to top), verify your terminal's newline configuration:

- In **WezTerm**, avoid setting `canonicalize_pasted_newlines = "LineFeed"` on Windows.
- Windows ConPTY and PSReadLine require standard Windows line endings (`CRLF` or `CR`). Forcing raw `\n` into ConPTY causes PSReadLine's asynchronous input parser to reorder incoming stream chunks.
- Leaving `canonicalize_pasted_newlines` disabled preserves correct, sequential multi-line pasting.

---

## Build and Run

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```sh
# Build and run tests (65 unit tests)
dotnet build PasteFix/dotnet/PasteFix.slnx -c Release
dotnet test  PasteFix/dotnet/PasteFix.slnx -c Release

# Publish single-file binary
dotnet publish PasteFix/dotnet/src/PasteFix/PasteFix.csproj -c Release -r win-x64 \
  --self-contained true -o PasteFix/dist -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
```

Published binary: `PasteFix/dist/PasteFix.exe` (~51 MB single-file standalone executable).

