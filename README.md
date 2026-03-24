# JotMic

`JotMic` is a lightweight Windows-only tray app for fast voice-to-text.

It stays out of the way, lives in the system tray, and lets you start or end recording with a hotkey or a tray click. When you finish speaking, it copies the transcript to your clipboard so you can paste it anywhere.

## What It Does

- Minimal and lightweight
- Quick voice-to-text without heavy editor window
- Start/End recording from a tray click or a global hotkey
- Auto-Copy the finished transcript to the clipboard
- Optional realtime streaming mode for a faster final result

## Build And Run

You need the `.NET 10 SDK` installed.

Open a terminal in this repository root, the folder that contains `STT.sln`, and run:

```powershell
dotnet build STT.sln
dotnet run --project .\src\Stt.App\Stt.App.csproj
```

## Use It

1. Launch `JotMic`.
2. Open `Settings` from the tray icon and add your OpenAI API key.
3. Pick your microphone, hotkey, and recording mode if you want to change the defaults.
4. Start/End recording with the tray icon or your hotkey.
5. Paste the transcript anywhere.
