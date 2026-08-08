# Pastefix

**Makes a multi-line command copied from a chat window pasteable into a terminal in one go.**

A small Windows tray app. It does two things and nothing else.

## The problem

You ask Claude or ChatGPT for a shell script, hit *copy*, paste it into your
terminal — and it doesn't run. Two things go wrong:

**The block carries the indentation of wherever it was quoted from.** Selecting a
fenced block by hand takes the fence's leading whitespace with it, and the shell
sees a command that starts four columns in.

**Continuation lines use the wrong marker.** Bash continues a line with a trailing
`\`; PowerShell uses a trailing backtick. A command written for one shell breaks
in the other, and the first line runs on its own with the remaining arguments
lost — the classic "I got an error about a missing parameter, and a stray extra
argument below it".

## What it does

**1. Removes the indent common to every line.** Relative indentation is untouched,
so Python, YAML and here-strings survive intact.

```
    if (x) {                 if (x) {
        y = 1        →           y = 1
    }                        }
```

**2. Joins continuation lines into one command.** A trailing `\`, backtick, `&&`
or `|` is consumed and the lines are glued together.

```
docker run \
    -e FOO=bar \      →    docker run -e FOO=bar image
    image
```

This is the part that makes the tool shell-agnostic: **once the continuation
markers are gone there is no continuation marker to get wrong**, so Pastefix
never has to know which shell you are pasting into.

Blank lines caught between a continuation marker and the rest of the command are
dropped — that is what turns `Get-Process |` into
`An empty pipe element is not allowed.`

### When each one runs

| Trigger | Dedent | Join continuations |
|---|:-:|:-:|
| **全自動監聽** (auto monitor, on by default) | ✅ | — |
| **Alt+V** / **立即清洗** / tray double-click | ✅ | ✅ |

Dedenting preserves structure, so the automatic path can run it on anything —
there is no guessing about whether your clipboard "looks like a command", and no
way for it to quietly damage code you copied. Joining lines rewrites the text, so
it only happens when you ask for it. Because auto monitor fixes the clipboard at
*copy* time, an ordinary `Ctrl+V` already gets the benefit.

## What it does **not** do

Deliberately, and permanently:

- **No shell translation.** `$VAR` expansion, `$(...)` subshells, `&&` semantics,
  `/` versus `\` path separators — all out of scope. Joining continuations is the
  one cross-shell exception, and only because a continuation marker is pure
  syntactic noise that carries no meaning.
- **No line-ending rewriting.** Measured on 2026-08-09 with
  `tools/paste-probe.ps1`: WezTerm hands the application the same bytes whether
  the clipboard holds LF or CRLF (both arrive as LF, 17 characters in and 17
  out). Whatever Pastefix wrote there was unobservable, so the code was deleted.
- **No prompt stripping** (`$ `, `> `, `PS C:\>`), **no comment deletion**, **no
  markdown fence removal.** Every one of these had a plausible story and no real
  scenario behind it.

Guards exist so that joining never fires on code: a trailing `&&` or `\` inside an
unclosed bracket (`if (a &&`, `do { \`), inside a quoted string, or inside a
here-string is left alone, and a marker must have whitespace in front of it so
`cd C:\temp\` stays a path.

## A problem this tool cannot fix

While diagnosing the above, a worse bug turned up — worth writing down because
knowing where it lives is the point.

**Symptom.** Paste a multi-line script into PowerShell and the buffer sometimes
comes out with its lines in reverse order — and PowerShell then *executes* them
reversed. It is not a display glitch: a paste that was supposed to back a file up
and then modify it instead ran the deletion first.

**Where it is not.** Ruled out one layer at a time:

| Layer | Verdict |
|---|---|
| Clipboard line endings | Not it — LF, CRLF and CR all reverse |
| Terminal → application | Not it — `paste-probe.ps1` measured the delivered bytes as correct and complete |
| PowerShell profile (starship, coreutils) | Not it — `pwsh -NoProfile` reverses too |
| Pastefix | Not it — reproduced with the app not running |

What is left correlates with how many display rows the content occupies, and it
does not reproduce reliably — pasting a forty-line script often works fine.

**Pastefix cannot address it.** The corruption happens after the clipboard is
read, and Pastefix only ever touches the clipboard. `tools/paste-probe.ps1` is
kept in this repo as the instrument for measuring it when it next appears.

## Usage

Run `PasteFix\dist\PasteFix.exe`. It sits in the system tray:

- **立即清洗** (*Clean now*) — dedents and joins continuations, then shows a balloon tip.
- **全自動監聽** (*Auto monitor*, default on) — dedents the clipboard whenever it changes.
- **熱鍵 Alt+V** (*Hotkey*, default on) — clean the clipboard and paste, from anywhere.
- **退出** — quit.

Settings live in `%APPDATA%\PasteFix\settings.json` and apply immediately.

## Supported environment

Verified on **WezTerm + PowerShell 7.6 on Windows 11** only. That is the one
setup it has actually been measured against. Windows Terminal, conhost and
non-PowerShell shells are untested — not unsupported, just unmeasured. The
line-ending finding above is specific to WezTerm and may not hold elsewhere.

### Known limitation

Copying a code block from the **Claude Code terminal UI** adds two spaces to
every line except the first. Since the first line then has no indent, the common
prefix is empty and dedenting does nothing. Copying from the Claude or ChatGPT
apps themselves is unaffected (measured: 4-space indentation, zero tabs).

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

Note that `PasteFix/dist/PasteFix.exe` is a *publish* artifact — `dotnet build`
does not update it, and the tray app must be closed before republishing.

## Layout

- `PasteFix/dotnet/src/PasteFix` — the WinForms tray app
  - `Normalizer.cs` — `Dedent` and `Normalize`, pure and unit-tested
  - `MessageWindow.cs` — hidden window for clipboard + hotkey messages
  - `Settings.cs` — persistent settings
- `PasteFix/dotnet/tests/PasteFix.Tests` — xUnit tests
- `PasteFix/examples` — paired input/expected samples used as test fixtures
- `PasteFix/legacy` — the old Node.js CLI, kept for reference
- `tools/paste-probe.ps1` — measures what a terminal actually receives on paste
- `tools/manual-test.ps1` — end-to-end smoke test against a running tray app
