# Changelog

All notable changes to WhisperTray are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/).

## [Unreleased]

## [0.2.0] — 2026-04-25

### Added
- Stateful tray icons: the glyph now changes between Idle, Recording,
  Transcribing and Injecting instead of the static system icon.
- WPF Settings window (tray → "Settings…"). Captures hotkeys live, fills
  provider defaults, validates the form, writes to `%APPDATA%\WhisperTray\settings.json`.
- DPAPI-encrypted `apiKey` persistence. Existing plaintext settings files
  are read on load and migrated to the encrypted form on the first save.
- Per-user autostart via `HKCU\…\Run`. The registry entry is quoted to
  handle "Program Files"-style paths and self-heals when the exe is moved.
- Single-file publish pipeline (`dotnet publish -p:PublishSingleFile=true`)
  and GitHub Actions release workflow that attaches `WhisperTray-vX.Y-win-x64.zip`
  to tags matching `v*`.

### Changed
- `JsonFileSettingsStore` now accepts an optional `ISecretProtector`. Without
  one it still writes plaintext; with one it writes `apiKeyProtected`.
- `CompositionRoot` re-registers the global hotkey when the Settings window
  saves a new combination, without requiring an app restart.

### Fixed
- `SendInput` used to dispatch 0/N events because the INPUT union was sized
  from `KEYBDINPUT` only. The union now declares `MOUSEINPUT` explicitly so
  `Marshal.SizeOf<INPUT>()` returns the 40 bytes Windows expects on x64.
- `GetSidSubAuthorityCount` P/Invoke returned `int`, causing random
  `AccessViolationException`s on hotkey press. Changed to `nint` and
  dereferenced with `Marshal.ReadByte`.

## [0.1.0] — MVP

Initial functional build:

- Global hotkey via `WH_KEYBOARD_LL`.
- 16 kHz mono recording via NAudio `WaveInEvent`.
- Opus/OGG encoding (Concentus) or raw WAV passthrough.
- Two transcription protocols behind one interface: OpenAI-compatible
  multipart to `/audio/transcriptions`, and whisper-api.com asynchronous
  POST + poll `status/{task_id}`.
- Cascading text injection: clipboard paste → Unicode typing → clipboard-only
  fallback, with automatic clipboard save/restore.
- Orchestrator state machine: Idle → Recording → Transcribing → Injecting.
- JSON settings under `%APPDATA%\WhisperTray\settings.json`.
- Tray icon + balloon-tip notifications (WinForms `NotifyIcon` hosted in WPF).
- CI: formatter check + build + test on `windows-latest`.
