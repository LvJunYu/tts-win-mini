# JotMic Project Guide

## Project
- `JotMic` is a lightweight Windows tray app for voice-to-text.
- The app records from the microphone, sends audio to OpenAI, and copies the final transcript to the clipboard.
- The product is Windows-only and built as a native `.NET 10 + WPF` desktop app.

## Current App Shape
- Tray-first UI with global hotkey support
- Manual start / end recording
- Two transcription paths:
  upload-after-stop
  optional realtime streaming while recording
- In-app settings for API key, microphone, hotkey, startup behavior, popup behavior, and per-mode model selection

## Codebase Structure
- `src/Stt.App`
  WPF app shell, tray integration, windows, settings UI, startup wiring
- `src/Stt.Core`
  shared abstractions, diagnostics, and simple models
- `src/Stt.Infrastructure`
  audio capture, OpenAI clients, workflow implementations
- `scripts`
  local developer helpers

## Important Files
- `src/Stt.App/App.xaml.cs`
  startup composition, dependency wiring, settings load/save, window orchestration
- `src/Stt.App/Controllers/AppController.cs`
  app state machine and clipboard completion flow
- `src/Stt.App/Configuration/AppSettingsLoader.cs`
  settings persistence, defaults, and migration
- `src/Stt.App/Services/TrayIconHost.cs`
  tray icon behavior, tray menu, status hints
- `src/Stt.App/ViewModels/SettingsViewModel.cs`
  settings state for the WPF settings window
- `src/Stt.Infrastructure/Audio`
  microphone capture implementations for batch and realtime paths
- `src/Stt.Infrastructure/OpenAi/OpenAiTranscriptionClient.cs`
  upload-after-stop transcription client
- `src/Stt.Infrastructure/OpenAi/OpenAiRealtimeTranscriptionClient.cs`
  realtime transcription session client
- `src/Stt.Infrastructure/Workflows/OpenAiRecordingWorkflow.cs`
  upload-after-stop workflow
- `src/Stt.Infrastructure/Workflows/OpenAiRealtimeRecordingWorkflow.cs`
  realtime streaming workflow
- `src/Stt.Infrastructure/Workflows/SelectableRecordingWorkflow.cs`
  runtime switch between streaming and non-streaming paths

## Current Defaults
- Non-streaming is the default recording mode
- Default non-streaming model: `gpt-4o-mini-transcribe`
- Default streaming model: `gpt-4o-transcribe`
- Primary settings path:
  `%LocalAppData%\\JotMic\\jotmic.settings.json`

## Realtime Notes
- Realtime transcription has been sensitive to audio format details.
- Keep an eye on the `16 kHz` vs `24 kHz` question when changing the streaming path.
- OpenAI's current realtime transcription docs describe `audio/pcm` input as `24 kHz` mono, while the transcription-session creation endpoint has still been working with the older flat request shape in this project.
- If streaming quality regresses, verify sample rate, session payload shape, and local trace output before changing higher-level workflow logic.
- Current status: realtime streaming can still produce random rubbish transcripts or inconsistent quality, especially on first-run or longer real mic recordings.
- It is not clear yet whether that remaining issue is caused by the OpenAI realtime path, the chosen realtime transcription model, or this app's capture / session implementation.
- Treat realtime mode as experimental until that behavior is understood and rechecked.

## Build
- From the repo root:
  `dotnet build STT.sln`
- Run locally:
  `dotnet run --project .\\src\\Stt.App\\Stt.App.csproj`
