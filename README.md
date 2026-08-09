# Pastefix

**Makes a multi-line command copied from a chat window pasteable into a terminal
in one go.** A small Windows tray app that does two things and nothing else:

1. **Removes the indent common to every line** — relative indentation is kept, so
   Python, YAML and here-strings survive. If the selection missed the left margin
   and line 1 came out flush while the rest did not, that margin is taken off too,
   on the strength of a grammatical rule rather than a guess.
2. **Joins continuation lines** — a trailing `\`, backtick, `&&` or `|` is
   consumed and the lines are glued into one command.

Auto monitor runs step 1 on every clipboard change, so an ordinary `Ctrl+V`
already benefits. `Alt+V` additionally does step 2, which rewrites the text and
therefore stays opt-in.

Everything the normalizer used to do beyond these two — line-ending rewriting,
prompt stripping, comment filtering, markdown fence removal, guessing whether a
block "looks like a command" — was removed on 2026-08-09. Each removal has a
reason on record; see `PLAN-2026-08-09.md`.

## Start here

| | |
|---|---|
| [`PasteFix/README.md`](PasteFix/README.md) | **The real documentation.** What it does, what it deliberately does not, how the hotkey delivers the paste, known limitations, build instructions. |
| `PasteFix/dist/PasteFix.exe` | The tray app. A *publish* artifact — `dotnet build` does not update it. |
| `tools/manual-test.ps1` | Hands-off smoke test against the running tray app. |
| `tools/manual-cases.ps1` | Twelve cases you paste yourself, for what the smoke test cannot see. |
| `tools/paste-probe.ps1` | Measures what a terminal actually hands the application. |

## Layout

```
PasteFix/dotnet/     the tray app (C#, .NET 10) and its unit tests
PasteFix/legacy/     the original Node.js CLI and AutoHotkey script, kept for reference
PasteFix/examples/   input/expected fixtures used by the tests
tools/               PowerShell test and measurement scripts
```

## Records

Written while the work happened, kept because the reasoning is the point:

- `BUGREPORT-2026-08-08.md` — what the over-eager version broke, case by case.
- `PLAN-2026-08-09.md` — the verdict on every rule, and why it stayed or went.
- `WORKLOG-2026-08-09.md` — the running account, including one acceptance
  criterion that was not met.
- `SUMMARY-2026-08-09.md` — the same in plain language.

## Not solved

Pasting into PowerShell can still execute lines in the wrong order. The
corruption happens *after* the clipboard is read, so Pastefix cannot reach it;
`tools/paste-probe.ps1` is the instrument for measuring it when it reappears.
