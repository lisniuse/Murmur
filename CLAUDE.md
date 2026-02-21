# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run

All commands run from the `src/` directory (or with `-p src/VoiceAssistant.csproj`):

```bash
# Development (run directly)
dotnet run

# Debug build only
dotnet build -c Debug

# Release single-file EXE
dotnet publish -c Release
```

Output paths are configured in the csproj:
- Debug → `output/debug/`
- Release publish → `output/prd/VoiceAssistant.exe`

There are no automated tests in this project.

## Architecture

This is a Windows WinForms tray application (`.NET 9`, `net9.0-windows`). Entry point is `Program.cs` → `TrayApp`.

### Audio Pipeline

Two-phase hybrid design:

1. **Wake-word phase** (`VoiceListener.StartWakeEngine`): `System.Speech.SpeechRecognitionEngine` runs continuously and cheaply, listening only for the configured wake words. It always uses the system default audio device.

2. **Recognition phase** (triggered on wake): `SpeechRecognitionEngine` is stopped to release the mic, then one of three paths runs:
   - **SystemSpeech** — new `SpeechRecognitionEngine` with `DictationGrammar`
   - **Whisper** — NAudio `WaveInEvent` → `AudioRecorder` → `WhisperTranscriber` (Whisper.net)
   - **Qwen** — NAudio `WaveInEvent` → `AudioRecorder` → `QwenTranscriber` (Python subprocess)

`AudioRecorder` uses dynamic RMS thresholding: first 400ms measures background noise, then uses 4× that as the speech threshold. The microphone device number (from `AppSettings.MicrophoneDeviceName`, resolved via `AudioRecorder.ResolveDeviceNumber`) only affects the NAudio phases (Whisper/Qwen); System.Speech always uses the OS default.

### Qwen3-ASR IPC Protocol

`QwenTranscriber` keeps a long-lived Python subprocess. Communication over stdin/stdout:
- Startup: Python emits `LOADING` → `DEVICE:<id>` → `MODEL_READY` (or `ERROR:<msg>`)
- Per inference: C# writes a temp WAV file path to stdin; Python responds with `TIMING:<ms>` then `OK:<text>` or `ERROR:<msg>`
- Shutdown: C# sends `EXIT` to stdin

`QwenTranscriber.IsRunning` gates re-initialization — the Python process is intentionally **not** restarted when settings are saved unless the mode or Python path actually changed.

### UI Thread Marshaling

Audio callbacks fire on NAudio/System.Speech background threads. All UI updates go through `TrayApp.RunOnUI(action)`, which calls `_uiInvoker.BeginInvoke(action)` — `_uiInvoker` is a hidden `Control` created on the main STA thread specifically for this purpose.

### Path Resolution (`ProjectPaths`)

`ProjectPaths.FindRoot()` uses compile-time conditional (`#if PRODUCTION`):
- **Debug**: walks up the directory tree looking for a folder containing `src/` or `models/`
- **Production** (publish): uses `Path.GetDirectoryName(Environment.ProcessPath)` — do NOT use `AppDomain.CurrentDomain.BaseDirectory` in single-file mode as it points to a temp extraction directory

`settings.json` and `app.log` always land in `Root`; the models directory defaults to `Root/models/` but is overridable via `AppSettings.ModelsPath`.

### Settings Persistence

`AppSettings` is a plain JSON-serialized class (`System.Text.Json`). It is loaded once at startup and mutated in place by `SettingsForm`. `TrayApp.OpenSettings()` calls `_listener.ReloadAsync(_settings)` after the dialog returns `OK`.

### Key Files

| Path | Purpose |
|---|---|
| `src/UI/TrayApp.cs` | Application root: tray icon, menu, event wiring, command execution |
| `src/Core/VoiceListener.cs` | Orchestrates wake-word → record → transcribe pipeline |
| `src/Core/AudioRecorder.cs` | NAudio recording with dynamic silence detection |
| `src/Transcribers/WhisperTranscriber.cs` | Whisper.net wrapper, auto-downloads models |
| `src/Transcribers/QwenTranscriber.cs` | Python subprocess IPC for Qwen3-ASR |
| `src/UI/SettingsForm.cs` | WinForms settings dialog |
| `src/UI/ToastForm.cs` | Borderless animated toast notifications (stacking, fade) |
| `src/Infra/AppSettings.cs` | Settings model + JSON load/save |
| `src/Infra/ProjectPaths.cs` | Root/models/settings/log path resolution |
