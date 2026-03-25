# whisper Project Guide

## Project
- `whisper` is a lightweight Windows tray app for voice-to-text.
- The app records from the microphone, sends audio to OpenAI, and copies the final transcript to the clipboard.
- The product is Windows-only and built as a native `.NET 10 + WPF` desktop app.

## Current App Shape
- Tray-first UI with global hotkey support
- Manual start / end recording
- Two transcription paths:
  upload-after-stop
  optional realtime streaming while recording
- In-app settings for API key, microphone, hotkey, startup behavior, and popup behavior

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
- Transcription model: `gpt-4o-mini-transcribe`
- Primary settings path:
  `%LocalAppData%\\whisper\\whisper.settings.json`

## Realtime Notes
- OpenAI realtime transcription expects `24 kHz` mono PCM audio.
- In this app, the reliable way to produce that audio is:
  capture the microphone at `16 kHz` mono
  resample to `24 kHz` PCM16
  then send the resampled audio to the realtime client
- Realtime startup is microphone-first:
  open the mic first
  switch the UI to recording when the mic is actually live
  finish the realtime connection in the background
- Do not switch realtime capture back to direct `24 kHz` or `48 kHz` without re-testing transcript quality carefully.
- Realtime mode is faster, but transcript quality is still less stable than non-streaming mode.

## Build
- From the repo root:
  `dotnet build STT.sln`
- Run locally:
  `dotnet run --project .\\src\\Stt.App\\Stt.App.csproj`
- Refresh the repo-owned local app on this machine:
  `powershell -ExecutionPolicy Bypass -File .\\scripts\\update-local-app.ps1`
