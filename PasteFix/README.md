# Pastefix

**Makes a multi-line command copied from a chat window pasteable into a terminal in one go.**

A small Windows tray app. It does two things and nothing else.

> **This file is the documentation of record.** It describes how the program
> behaves now. The dated files in `docs/records/` describe how it got here and are
> deliberately not kept up to date.

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

### How the hotkey delivers the paste

Alt+V cleans the clipboard and then has to make the foreground window paste it.
How, depends on what that window is:

| Window class | Paste sent as |
|---|---|
| `org.wezfurlong.wezterm` | Ctrl+Shift+V keystrokes |
| `CASCADIA_HOSTING_WINDOW_CLASS` (Windows Terminal) | Ctrl+V keystrokes |
| anything else | Ctrl+Shift+V keystrokes — the default channel |

The two terminal rows are measured facts (2026-08-09, both times because the
clipboard came out clean and nothing was pasted): both terminals host their
input in a child surface, so a `WM_PASTE` posted to the top-level window is
silently dropped. Ordinary Edit controls do handle `WM_PASTE`, and there it is
the better channel — immune to focus, stuck modifier keys and custom key
bindings.

**The table lives in `settings.json`** — a `PasteChannels` map, written out in
full on first run so it can be edited directly. An unknown GPU terminal that
ignores `WM_PASTE` therefore no longer needs a code change: add a row for its
window class, or change the `"*"` entry that stands for every unlisted window.
The default for unlisted windows became Ctrl+Shift+V on 2026-08-16 — it is the
terminal-family paste convention, and a keystroke that does nothing is a
*visible* failure, unlike `WM_PASTE`, which such windows drop silently after
the clean already succeeded. That default is policy, not a measurement; if you
spend your Alt+V presses in plain Edit controls, point `"*"` back at
`"WmPaste"`.

Every Alt+V press logs which channel it took to
`%APPDATA%\PasteFix\pastefix.log`; if a terminal ever cleans but does not paste,
that log line is the first thing to read. The log records the clipboard's
length, never its contents — passwords get copied too, and a permanent
plaintext log is the wrong place for them. (The opt-in `PASTEFIX_TRACE` file is
exempt: it exists precisely to show content while debugging.)

## Supported environment

Verified on **WezTerm + PowerShell 7.6 on Windows 11**. Windows Terminal is
covered for the paste path only (its `WM_PASTE` blindness was measured on
2026-08-09 and worked around); the rest is untested there. conhost and
non-PowerShell shells remain unmeasured — not unsupported, just untested. The
line-ending finding above is specific to WezTerm and may not hold elsewhere.

### When the selection misses the left margin

Copying a code block from the **Claude Code terminal UI** gives you two spaces on
every line except the first (measured 2026-08-09). The common prefix is therefore
empty, and dedenting — which by definition removes what every line shares — has
nothing to remove.

**Why the first line differs is inferred, not measured.** The block is drawn with
a two-column left margin, so a drag that starts at the first character of line 1
would begin *after* that margin while every later line is taken whole. That fits
the asymmetry exactly, but nobody has run the experiment. It takes twenty seconds:
copy the same block twice, once dragging from the first character and once from
the left edge of the line, and compare. If the second copy comes out with a
uniform two-space indent, the explanation holds and **starting the drag in the
margin is the real fix** — an ordinary dedent then takes the margin off. If both
copies come out the same, this paragraph is wrong and the workaround below is all
there is.

For text already on the clipboard, **立即清洗 / Alt+V repairs it** — but only when
line 1 is a finished statement:

```
x = 1              x = 1          for i in ...:        for i in ...:
  y = 2      →     y = 2                print(i)   →         print(i)
                                   (unchanged)
```

An unclosed bracket, an open quote, a here-string, a heredoc, a continuation
marker or a trailing `:` are all reasons for the next line to sit further in.
Rule every one of them out and the indent below has no grammar left to justify
it — in Python an indent may only follow a colon, so `x = 1` with an indented
`y = 2` under it is not merely unusual, it is an IndentationError. That is the
whole basis for the decision; it is not a judgement about whether the text
"looks like" a command.

The refusals are the good half of the bargain. **Where the rule declines, the
unrepaired text is still correct**: a Python body under `for i in ...:` shifted
by a uniform two spaces is valid Python, and inside brackets or after a
continuation marker leading whitespace never survives to mean anything. Where it
acts, the unrepaired text was already broken.

**It is not sound, and here is the gap.** A YAML multi-line plain scalar has
meaningful indentation with nothing opening it:

```
- a          →     - a         # the scalar was "a b"; now it is two nodes
  b                b
```

Nothing detects that. The trade was made deliberately on 2026-08-10 — the risk
is silent rewriting of string content, the gain is that the common case needs no
decision from you. `MarginRepairRewritesAYamlPlainScalar` asserts the damage so
it stays visible rather than becoming folklore.

Two measurements bound it. Sweeping 381 six-line samples of this repository's own
source: zero non-idempotent results, and the rule fired exactly once — on a
Chinese `用法：` heading above an indented command list. That is why the fullwidth
colon counts as an opener too. After that fix it fires zero times on real source.

Copying from the Claude or ChatGPT apps themselves is unaffected (measured:
4-space indentation, zero tabs).

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
  - `TrayApplication.cs` — tray menu, hotkey flow, paste delivery
  - `PasteChannelSelector.cs` — window class → paste mechanism, overridable from settings
  - `OwnWriteGate.cs` — tells our own clipboard write-backs apart from user copies
  - `ClipboardSummary.cs` — content-free clipboard description for the log
  - `Settings.cs` — persistent settings
- `PasteFix/dotnet/tests/PasteFix.Tests` — xUnit tests
- `PasteFix/examples` — paired input/expected samples used as test fixtures
- `PasteFix/legacy` — the old Node.js CLI, kept for reference
- `tools/paste-probe.ps1` — measures what a terminal actually receives on paste
- `tools/manual-test.ps1` — end-to-end smoke test against a running tray app
- `tools/manual-cases.ps1` — fifteen cases you paste by hand, with expected results
- `tools/toggle-test.ps1` — regression test for the two tray toggles
- `docs/records/` — dated records of past work; history, not current behaviour
