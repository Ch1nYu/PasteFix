# Pastefix

**Makes a multi-line command copied from a chat window pasteable into a terminal
in one go.** A small Windows tray app that does two things and nothing else:

1. **Removes the indent common to every line** — relative indentation is kept, so
   Python, YAML and here-strings survive. If the selection missed the left margin
   and line 1 came out flush while the rest did not, that margin is taken off too,
   on the strength of a grammatical rule rather than a guess.
2. **Joins continuation lines** — a trailing `\`, backtick, `&&` or `|` is
   consumed and the lines are glued into one command.

Everything the normalizer used to do beyond these two — line-ending rewriting,
prompt stripping, comment filtering, markdown fence removal, guessing whether a
block "looks like a command" — was removed on 2026-08-09. Each removal has a
reason on record; see [`docs/records/PLAN-2026-08-09.md`](docs/records/PLAN-2026-08-09.md).

## The whole feature set

Two functions. `Dedent` is lossless and may run on anything; `Normalize` rewrites
the text and therefore waits to be asked.

| Surface | Runs | Default |
|---|---|---|
| **全自動監聽** — clipboard listener | `Dedent`, on every clipboard change | on |
| **Alt+V** — hotkey | `Normalize`, then sends the paste | on |
| **立即清洗** — tray item, or double-click the icon | `Normalize`, no paste | — |

`Normalize` = `Dedent` → margin repair (only when line 1 is a finished statement)
→ join continuations.

- Settings: `%APPDATA%\PasteFix\settings.json` — two booleans, the two toggles above.
- Log: `%APPDATA%\PasteFix\pastefix.log` — one entry per Alt+V, including which
  paste channel the foreground window got.
- `PASTEFIX_TRACE=1` adds a verbose trace in `%TEMP%\pastefix-trace.log`.

## Start here

| | |
|---|---|
| [`PasteFix/README.md`](PasteFix/README.md) | **The documentation of record.** The only file that describes current behaviour: what it does, what it deliberately does not, how the hotkey delivers the paste, the known gap in the margin rule, build instructions. |
| `PasteFix/dist/PasteFix.exe` | The tray app. A *publish* artifact — `dotnet build` does not update it, and the running tray must be closed before republishing. |
| `tools/manual-test.ps1` | Hands-off smoke test against the running tray app. |
| `tools/manual-cases.ps1` | Fifteen cases you paste yourself, for what the smoke test cannot see. |
| `tools/toggle-test.ps1` | Restarts the tray with each toggle off and then on, and checks the effect. |
| `tools/paste-probe.ps1` | Measures what a terminal actually hands the application. |

## Layout

```
PasteFix/dotnet/     the tray app (C#, .NET 10) and its unit tests
PasteFix/legacy/     the original Node.js CLI and AutoHotkey script, kept for reference
PasteFix/examples/   input/expected fixtures used by the tests
tools/               PowerShell test and measurement scripts
docs/records/        dated records of past work — history, not current behaviour
```

## Build and test

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```sh
dotnet build PasteFix/dotnet/PasteFix.slnx -c Release -warnaserror
dotnet test  PasteFix/dotnet/PasteFix.slnx -c Release          # 59 tests

dotnet publish PasteFix/dotnet/src/PasteFix/PasteFix.csproj -c Release -r win-x64 \
  --self-contained true -o PasteFix/dist -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
```

## Records

In [`docs/records/`](docs/records/). Each one opens with a banner saying what it
is and what it no longer describes. They are kept because the reasoning is the
point — but none of them is a description of how the program behaves today.

- `BUGREPORT-2026-08-08.md` — what the over-eager version broke, case by case.
- `PLAN-2026-08-09.md` — the verdict on every rule, and why it stayed or went.
  **Read this before touching `Normalizer`.**
- `WORKLOG-2026-08-09.md` — the running account, including one acceptance
  criterion that was not met.
- `SUMMARY-2026-08-09.md` — the same in plain language.

## Not solved

Pasting into PowerShell can still execute lines in the wrong order. The
corruption happens *after* the clipboard is read, so Pastefix cannot reach it;
`tools/paste-probe.ps1` is the instrument for measuring it when it reappears.
