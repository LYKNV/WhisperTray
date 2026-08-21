# Changelog

All notable changes to WhisperTray are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/).

## [Unreleased]

## [0.4.0] — 2026-08-21

### Added
- Local usage analytics: each dictation appends one row to
  `%APPDATA%\WhisperTray\analytics\usage-<YYYY-MM-DD>.csv`, a new file per day,
  with columns
  `timestamp,status,error,audio_seconds,provider,model,tokens_in,tokens_out,tokens_total,billed_seconds`.
  Token and billed-second counts come from the provider's `usage` block where
  one is returned; empty cells mean "not reported", which is distinct from a
  reported zero. Failures are logged with their error kind, so reliability can
  be measured alongside consumption. Writing is best-effort and never
  interrupts a dictation; transcribed text is never recorded.

### Changed
- The .NET SDK is pinned to 8.0.424 in `global.json` and in both CI workflows.
  Previously `dotnet-version: 8.0.x` let the runner pick whichever 8.x SDK it
  had, and since `dotnet format` ships with the SDK, its rules could change
  under the repository — turning the CI formatting gate red without a single
  commit. Formatting behaviour now moves only when the pin is bumped.

## [0.3.0] — 2026-06-11

### Added
- Configurable transcription timeout: new "Timeout (seconds)" field in the
  Settings window (`timeoutSeconds` in `settings.json`, default 120, valid
  range 10–600). The deadline is enforced for every provider, including the
  whisper-api.com submit + poll loop, so a stuck transcription can no longer
  hang the app for minutes. On expiry a "Transcription timed out" error is
  shown — distinct from user cancellation.

### Changed
- The previously hardcoded transport timeouts (2-minute `HttpClient` limit
  and 5-minute whisper-api.com poll budget) are replaced by the single
  configurable timeout above.

## [0.2.1] — 2026-05-08

### Fixed
- Paste injection silently failed when the user kept holding the hotkey
  modifier (e.g. Win in Win+Z) past the moment the transcription was
  ready: the synthesised Ctrl+V got combined into Win+Ctrl+V and dropped
  on the floor. Injection now polls `GetAsyncKeyState` until Ctrl/Alt/
  Shift/Win are all physically released (5-second timeout) before firing
  the shortcut. On timeout the transcription stays on the clipboard so
  the user can paste it manually.

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
