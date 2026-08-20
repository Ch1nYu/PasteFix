# Pastefix

**Makes a multi-line command copied from a chat window pasteable into a terminal in one go.** A small Windows tray app that does two things and nothing else:

1. **Removes the indent common to every line** — relative indentation is kept, so Python, YAML and here-strings survive. If the selection missed the left margin and line 1 came out flush while the rest did not, that margin is taken off too, on the strength of a grammatical rule rather than a guess.
2. **Joins continuation lines** — a trailing `\`, backtick, `&&` or `|` is consumed and the lines are glued into one command.

Everything the normalizer used to do beyond these two — line-ending rewriting, prompt stripping, comment filtering, markdown fence removal, guessing whether a block "looks like a command" — was removed on 2026-08-09. Each removal has a reason on record; see [`docs/records/PLAN-2026-08-09.md`](docs/records/PLAN-2026-08-09.md).

## The whole feature set

| Surface | Runs | Default |
|---|---|---|
| **全自動監聽** — clipboard listener | `Normalize` (dedent + join continuations), on every clipboard change | on |
| **立即清洗** — tray item, or double-click the icon | `Normalize`, manual clean | — |

- Settings: `%APPDATA%\PasteFix\settings.json`
- Log: `%APPDATA%\PasteFix\pastefix.log`
- `PASTEFIX_TRACE=1` adds a verbose trace in `%TEMP%\pastefix-trace.log`.

## Start here

| | |
|---|---|
| [`PasteFix/README.md`](PasteFix/README.md) | **The documentation of record.** The only file that describes current behaviour: what it does, what it deliberately does not, build instructions. |
| `PasteFix/dist/PasteFix.exe` | The tray app. A *publish* artifact — `dotnet build` does not update it, and the running tray must be closed before republishing. |
| `tools/manual-test.ps1` | Automated smoke test against the running tray app. |
| `tools/manual-cases.ps1` | Hand-tested verification cases. |
| `tools/toggle-test.ps1` | Regression test for the tray toggle. |
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
dotnet test  PasteFix/dotnet/PasteFix.slnx -c Release
dotnet publish PasteFix/dotnet/src/PasteFix/PasteFix.csproj -c Release -r win-x64 \
  --self-contained true -o PasteFix/dist -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
```

