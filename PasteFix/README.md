# Pastefix

A tiny Windows tray app that cleans up AI-generated terminal commands before you paste them.

When an AI assistant hands you shell commands, the copied text usually comes with
Markdown fences, prompt characters (`$ `, `> `, `PS C:\>`), explanatory comments,
and backslash line continuations. Pastefix strips all of that so you can paste
and run a command immediately.

## Usage

Run `PasteFix\dist\PasteFix.exe`. It sits in the system tray:

- **立即清洗** (*Clean now*) — cleans the current clipboard and shows a balloon tip.
- **全自動監聽** (*Auto monitor*, default on) — watches the clipboard; whenever the
  copied text looks like an AI-style command, it cleans it and writes it back automatically.
- **熱鍵 Alt+V** (*Hotkey*, default on) — press `Alt+V` anywhere to clean the current clipboard.
- **退出** — quits the app.

Settings are saved to `%APPDATA%\PasteFix\settings.json` and take effect immediately.

## What gets cleaned

| Source | Example input | Output |
|--------|---------------|--------|
| Markdown code fence + indentation | `` ```bash ``<br>`    npm install express`<br>` ``` `` | `npm install express` |
| Shell prompt | `$ npm install express` | `npm install express` |
| PowerShell prompt | `PS C:\Users\me> npm install` | `npm install` |
| Backslash continuation | `npm install \`<br>`` `--save express` `` | `npm install --save express` |
| `&&` continuation | `git add . &&`<br>`git commit` | `git add . && git commit` |
| Comment lines (prompted blocks only) | `$ # install deps`<br>`$ npm install` | `npm install` |
| Multiple prompted commands | `$ echo a`<br>`$ echo b` | `echo a`<br>`echo b` (kept separate) |

Output always uses CRLF, so a Windows terminal paste does not produce an extra
blank continuation line per source line.

### What is left untouched

Cleaning is deliberately conservative, because the clipboard also carries code.
Indentation, in-code `#` comments, and line structure are preserved for anything
that is not a prompted command block — Python, YAML, here-strings, and code where
a trailing `&&` / `\` sits inside an unclosed bracket (`if (a &&`, `do { \`) all
round-trip unchanged.

**全自動監聽** goes further and only cleans clipboard text that is command-shaped
(fenced, mostly prompt-prefixed, or a single line). Everything else gets its line
endings normalised and nothing more. **立即清洗** and **Alt+V** bypass that gate
and always clean.

## Build from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```sh
dotnet build PasteFix/dotnet/PasteFix.slnx      # build
dotnet test PasteFix/dotnet/PasteFix.slnx       # run unit tests
```

Ship as a self-contained single exe:

```sh
dotnet publish PasteFix/dotnet/src/PasteFix/PasteFix.csproj -c Release -r win-x64 --self-contained true -o dist -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
```

## Layout

- `PasteFix/dotnet/src/PasteFix` — the WinForms tray app
  - `Normalizer.cs` — the cleaning core (pure, unit-tested)
  - `MessageWindow.cs` — hidden window for clipboard + hotkey messages
  - `Settings.cs` — persistent settings
- `PasteFix/dotnet/tests/PasteFix.Tests` — xUnit tests
- `PasteFix/examples` — paired input/expected samples used as test fixtures
- `PasteFix/legacy` — the old Node.js CLI, kept for reference