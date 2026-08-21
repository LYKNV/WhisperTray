# WhisperTray

A Windows tray-resident dictation app. Hit a hotkey → record audio → transcribe
through a Whisper-compatible API → type the result into the focused window.
No chrome, no main window — lives in the system tray, configured via a
right-click menu.

## Features

- **Toggle-style recording** via a user-definable global hotkey
  (default `Win+Z`). First press starts; second press stops and kicks off
  transcription.
- **Four transcription providers** behind a single interface:
  [OpenAI](https://platform.openai.com/docs/api-reference/audio),
  [Lemonfox](https://lemonfox.ai/),
  [Hugging Face](https://huggingface.co/inference-api),
  [whisper-api.com](https://whisper-api.com/) (async polling).
- **Compressed uploads** (Opus/OGG) to keep API calls small, or raw WAV if
  the provider rejects compressed audio.
- **Cascading text injection**: first tries `Ctrl+V` against the originally-
  focused window; falls back to Unicode typing; final fallback leaves the text
  on the clipboard. Elevation and focus-loss are detected and handled
  gracefully.
- **Stateful tray icon** that changes shape for Idle / Recording /
  Transcribing / Injecting.
- **Settings window** for every knob (hotkey, provider, model, API key,
  language, audio format, injection mode, autostart).
- **DPAPI-encrypted API key** on disk. Pre-existing plaintext settings are
  migrated on the first save.
- **Local usage log**: one CSV row per dictation in
  `%APPDATA%\WhisperTray\analytics\usage-<YYYY-MM-DD>.csv` — time, outcome,
  audio length, provider, model, and token counts where the provider reports
  them. A new file each day. Transcribed text is never logged and nothing
  leaves the machine.
- **Per-user autostart** via `HKCU\…\Run` — enabled with a checkbox.

## Install

### From a release

1. Grab `WhisperTray-<version>-win-x64.zip` from the
   [Releases](../../releases) page and unzip it anywhere.
2. Ensure the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
   (x64) is installed.
3. Double-click `WhisperTray.exe`. A microphone glyph appears in the tray.
4. Right-click the tray icon → **Settings…** → pick a provider, paste your
   API key, hit **Save**.

### First-run configuration tips

| Provider | Base URL                                 | Recommended model         |
|----------|------------------------------------------|---------------------------|
| OpenAI   | `https://api.openai.com/v1`              | `gpt-4o-transcribe`       |
| Lemonfox | `https://api.lemonfox.ai/v1`             | `whisper-1`               |
| HuggingFace | `https://api-inference.huggingface.co` | `openai/whisper-large-v3` |
| whisper-api.com | `https://api.whisper-api.com`      | `base` / `large-v3`       |

Leave **Language** empty to let the model auto-detect (works well for mixed
RU/EN content). **Prompt hint** is a short phrase passed as context —
useful for domain vocabulary.

## Usage

1. Put the text cursor where you want the transcript to land.
2. Press your hotkey — the tray icon switches to red; speak.
3. Press the hotkey again — the icon goes blue while the API works, then
   green while the text is inserted, then back to gray.
4. If the foreground window rejected keystrokes (e.g. an elevated window
   while WhisperTray is not elevated), a balloon tip tells you the text is
   on the clipboard — just `Ctrl+V` yourself.

## Settings file

Stored at `%APPDATA%\WhisperTray\settings.json`. Everything in the
Settings window is persisted there; the API key is DPAPI-encrypted as
`apiKeyProtected` (per Windows user, not portable).

If you edit the file by hand you can:

- set `apiKey` as plaintext on first run — it gets migrated to
  `apiKeyProtected` on the next save,
- remove `hotkey` to fall back to the default.

## Build from source

```
dotnet restore
dotnet build
dotnet test
```

Single-file release build:

```
dotnet publish src/WhisperTray.App/WhisperTray.App.csproj ^
    --configuration Release ^
    --runtime win-x64 ^
    -p:PublishSingleFile=true ^
    --output publish
```

The result is `publish\WhisperTray.exe` (framework-dependent; requires the
.NET 8 Desktop Runtime on the target machine).

## Architecture

```
src/
  WhisperTray.Core/            platform-agnostic, net8.0
    Configuration/             Settings record, JSON store, DPAPI interface,
                               autostart abstraction, provider defaults,
                               validator.
    Audio/                     recorder and encoder abstractions, Opus/OGG,
                               WAV passthrough.
    Transcription/             OpenAI-compatible client, whisper-api.com
                               polling client, provider-model catalog,
                               exception hierarchy.
    Injection/                 cascading injector, clipboard and typist
                               abstractions.
    Hotkeys/                   parser, combo record, key map.
    Orchestration/             record-transcribe-inject state machine.
  WhisperTray.App/             WPF + WinForms host, net8.0-windows
    Adapters/                  SendInput typist, WPF clipboard, NAudio
                               recorder, Win32 foreground window, low-level
                               keyboard hook, DPAPI, HKCU run-key registry.
    Tray/                      NotifyIcon host, stateful icon set.
    Views/                     SettingsWindow.
    CompositionRoot.cs         wires everything together.

tests/
  WhisperTray.Core.Tests/      xUnit + FluentAssertions + NSubstitute.
```

## Troubleshooting

- **"SendInput dispatched 0/4 events"** — means the INPUT struct size is
  wrong. The codebase pins this via `MOUSEINPUT` in the INPUT union; if you
  see this error again, suspect a new P/Invoke declaration sizing the
  union incorrectly.
- **Hotkey doesn't fire** — another app may already own the combination.
  Pick something less common (Win+Z, Ctrl+Alt+Space, a function key).
- **Text inserts into the wrong window** — WhisperTray captures the
  foreground window once, when you press the hotkey. If you alt-tab during
  recording, the text falls back to the clipboard with a notification.
- **Typing into an elevated window (Task Manager, UAC prompt) silently
  fails** — UIPI blocks SendInput from unelevated processes. Running
  WhisperTray as admin would fix it but removes the drag-and-drop / some
  app-integration ergonomics; the default is unelevated and we rely on the
  clipboard fallback.

## Tests

```
dotnet test
```

172 tests cover: settings parsing/serialization, DPAPI migration flow,
provider defaults, validator, Opus encoder bit-level output, cascading
injector strategy, hotkey parsing, orchestrator state transitions, both
transcription clients (happy path, auth, rate-limit, polling timeout,
cancellation), autostart registry semantics.
