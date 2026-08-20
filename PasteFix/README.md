# Pastefix

**Makes a multi-line command copied from a chat window pasteable into a terminal in one go.**

A small Windows tray app. It does two things and nothing else.

> **This file is the documentation of record.** It describes how the program behaves now. The dated files in `docs/records/` describe how it got here and are deliberately not kept up to date.

## The problem

You ask Claude or ChatGPT for a shell script, hit *copy*, paste it into your terminal — and it doesn't run. Two things go wrong:

**The block carries the indentation of wherever it was quoted from.** Selecting a fenced block by hand takes the fence's leading whitespace with it, and the shell sees a command that starts four columns in.

**Continuation lines use the wrong marker.** Bash continues a line with a trailing `\`; PowerShell uses a trailing backtick. A command written for one shell breaks in the other, and the first line runs on its own with the remaining arguments lost.

## What it does

**1. Removes the indent common to every line.** Relative indentation is untouched, so Python, YAML and here-strings survive intact.

```
    if (x) {                 if (x) {
        y = 1        →           y = 1
    }                        }
```

**2. Joins continuation lines into one command.** A trailing `\`, backtick, `&&` or `|` is consumed and the lines are glued together.

```
docker run \
    -e FOO=bar \      →    docker run -e FOO=bar image
    image
```

This is the part that makes the tool shell-agnostic: **once the continuation markers are gone there is no continuation marker to get wrong**, so Pastefix never has to know which shell you are pasting into.

Blank lines caught between a continuation marker and the rest of the command are dropped — that is what turns `Get-Process |` into `An empty pipe element is not allowed.`

### When it runs

| Trigger | Dedent | Join continuations |
|---|:-:|:-:|
| **全自動監聽** (auto monitor, on by default) | ✅ | ✅ |
| **立即清洗** / tray double-click | ✅ | ✅ |

Because auto monitor fixes the clipboard at *copy* time in the background, an ordinary `Ctrl+V` or right-click in your terminal already gets the benefit with zero extra keystrokes required.

## What it does **not** do

- **No shell translation.** `$VAR` expansion, `$(...)` subshells, `&&` semantics, `/` versus `\` path separators — all out of scope.
- **No line-ending rewriting.** Line endings are preserved as-is.
- **No prompt stripping** (`$ `, `> `, `PS C:\>`), **no comment deletion**, **no markdown fence removal.**

Guards exist so that joining never fires on code: a trailing `&&` or `\` inside an unclosed bracket (`if (a &&`, `do { \`), inside a quoted string, or inside a here-string is left alone, and a marker must have whitespace in front of it so `cd C:\temp\` stays a path.

## Usage

Run `PasteFix\dist\PasteFix.exe`. It sits in the system tray:

- **立即清洗** (*Clean now*) — cleans the clipboard and shows a notification.
- **全自動監聽** (*Auto monitor*, default on) — cleans the clipboard whenever it changes.
- **退出** — quit.

Settings live in `%APPDATA%\PasteFix\settings.json`.

## Build from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```sh
dotnet build PasteFix/dotnet/PasteFix.slnx      # build
dotnet test PasteFix/dotnet/PasteFix.slnx       # run unit tests
```

Ship as a self-contained single exe:

```sh
dotnet publish PasteFix/dotnet/src/PasteFix/PasteFix.csproj -c Release -r win-x64 --self-contained true -o PasteFix/dist -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
```

## Layout

- `PasteFix/dotnet/src/PasteFix` — the WinForms tray app
  - `Normalizer.cs` — `Dedent` and `Normalize`, pure and unit-tested
  - `MessageWindow.cs` — hidden window for clipboard change listener
  - `TrayApplication.cs` — tray icon and menu
  - `OwnWriteGate.cs` — tells our own clipboard write-backs apart from user copies
  - `ClipboardSummary.cs` — content-free clipboard description for the log
  - `Settings.cs` — persistent settings
- `PasteFix/dotnet/tests/PasteFix.Tests` — xUnit tests
- `PasteFix/examples` — paired input/expected samples used as test fixtures
- `PasteFix/legacy` — the old Node.js CLI, kept for reference
- `tools/paste-probe.ps1` — measures what a terminal actually receives on paste
- `tools/manual-test.ps1` — end-to-end smoke test against a running tray app
- `tools/manual-cases.ps1` — fifteen cases you paste by hand, with expected results
- `tools/toggle-test.ps1` — regression test for the tray toggle
- `docs/records/` — dated records of past work; history, not current behaviour

