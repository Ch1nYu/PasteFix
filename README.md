# Pastefix

**Makes a multi-line command copied from a chat window pasteable into a terminal in one go.**

A small Windows tray application that automatically optimizes your clipboard in the background for terminal pasting.

## What it does

When copying code from AI chat windows (ChatGPT, Claude) or web pages, terminal pasting often breaks due to indentation and shell syntax mismatches. Pastefix automatically fixes these issues:

1. **Removes Common Indentation (Dedent)** — Strips uniform outer indentation while strictly preserving relative indentation so Python, YAML, and here-strings survive intact.
2. **Repairs Missed Margins** — Automatically corrects mouse selections that missed the first-line left margin.
3. **Joins Continuation Lines** — Glues multi-line continuation markers (`\`, backtick, `&&`, or trailing `|`) into single executable commands.
4. **Drops Stray Empty Lines in Pipelines** — Prevents PowerShell `EmptyPipeElement` parser errors.

## How it works

Pastefix operates entirely on clipboard events with zero extra keystrokes required:

| Feature | Behavior | Default |
|---|---|---|
| **全自動監聽** (Auto monitor) | Automatically normalizes clipboard content within 300ms on copy | Enabled |
| **立即清洗** (Clean now) | Manually cleans clipboard and displays notification | On demand |

**No new hotkeys to learn**: Simply press `Ctrl+V` or mouse right-click to paste into WezTerm, Windows Terminal, or any terminal emulator as usual.

## Getting Started

### Download & Run
Download the latest `PasteFix.exe` from the [Releases](https://github.com/Ch1nYu/pastefix/releases) page. It sits in your Windows system tray notification area (look under the `^` hidden icons menu).

- **Self-contained**: .NET 10 runtime is bundled inside the executable (~51 MB single file).
- **Single-instance**: Protected by a system mutex to prevent accidental multiple instances.
- **Config & Logs**: Settings persist in `%APPDATA%\PasteFix\settings.json`.

## Build from Source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```sh
# Build and run tests
dotnet build PasteFix/dotnet/PasteFix.slnx -c Release -warnaserror
dotnet test  PasteFix/dotnet/PasteFix.slnx -c Release

# Publish single-file executable
dotnet publish PasteFix/dotnet/src/PasteFix/PasteFix.csproj -c Release -r win-x64 \
  --self-contained true -o PasteFix/dist -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
```

## Repository Layout

```
PasteFix/dotnet/     the .NET 10 WinForms tray app and xUnit test suite
PasteFix/dist/       published single-file binary (PasteFix.exe)
PasteFix/examples/   test fixtures (input/expected pairs)
tools/               PowerShell test, benchmark, and measurement scripts
docs/release-notes/  version release notes (v1.0.0, v1.1.0, v1.2.0)
docs/records/        historical architecture records
```

