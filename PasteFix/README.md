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
| Comment lines | `# install deps`<br>`npm install` | `npm install` |
| Multiple prompted commands | `$ echo a`<br>`$ echo b` | `echo a`<br>`echo b` (kept separate) |

Plain text without command-shaped features is left untouched.

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