using NAudio.Wave;
using Stt.Core.Abstractions;
using Stt.Core.Models;

namespace Stt.Infrastructure.Audio;

public sealed class NaudioMicrophoneCaptureSession : IAudioCaptureSession, IDisposable
{
    private readonly Func<string?> _selectedDeviceIdAccessor;
    private readonly object _syncRoot = new();
    private WaveInEvent? _waveInEvent;
    private WaveFileWriter? _waveFileWriter;
    private string? _activeFilePath;
    private TaskCompletionSource<CapturedAudioFile>? _stopTaskCompletionSource;

    public NaudioMicrophoneCaptureSession(Func<string?> selectedDeviceIdAccessor)
    {
        _selectedDeviceIdAccessor = selectedDeviceIdAccessor;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            if (_waveInEvent is not null)
            {
                throw new InvalidOperationException("A recording session is already active.");
            }

            var tempDirectory = Path.Combine(Path.GetTempPath(), "Stt");
            Directory.CreateDirectory(tempDirectory);

            _activeFilePath = Path.Combine(
                tempDirectory,
                $"capture-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.wav");

            _stopTaskCompletionSource = new TaskCompletionSource<CapturedAudioFile>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            _waveInEvent = new WaveInEvent
            {
                BufferMilliseconds = 100,
                WaveFormat = new WaveFormat(16_000, 16, 1)
            };

            var deviceNumber = MicrophoneDeviceCatalog.ResolveDeviceNumber(_selectedDeviceIdAccessor());
            if (deviceNumber.HasValue)
            {
                _waveInEvent.DeviceNumber = deviceNumber.Value;
            }

            _waveFileWriter = new WaveFileWriter(_activeFilePath, _waveInEvent.WaveFormat);
            _waveInEvent.DataAvailable += OnDataAvailable;
            _waveInEvent.RecordingStopped += OnRecordingStopped;
            _waveInEvent.StartRecording();
        }

        return Task.CompletedTask;
    }

    public Task<CapturedAudioFile> StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Task<CapturedAudioFile> stopTask;

        lock (_syncRoot)
        {
            if (_waveInEvent is null || _stopTaskCompletionSource is null)
            {
                throw new InvalidOperationException("No recording session is currently active.");
            }

            stopTask = _stopTaskCompletionSource.Task;
            _waveInEvent.StopRecording();
        }

        return stopTask;
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            DisposeActiveSession();
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        lock (_syncRoot)
        {
            _waveFileWriter?.Write(e.Buffer, 0, e.BytesRecorded);
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        TaskCompletionSource<CapturedAudioFile>? completionSource;
        string? filePath;

        lock (_syncRoot)
        {
            completionSource = _stopTaskCompletionSource;
            filePath = _activeFilePath;
            DisposeActiveSession();
        }

        if (completionSource is null)
        {
            return;
        }

        if (e.Exception is not null)
        {
            DeleteFileIfPresent(filePath);
            completionSource.TrySetException(e.Exception);
            return;
        }

        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            completionSource.TrySetException(
                new InvalidOperationException("Recording stopped, but no audio file was created."));
            return;
        }

        completionSource.TrySetResult(new CapturedAudioFile(
            filePath,
            Path.GetFileName(filePath),
            "audio/wav"));
    }

    private void DisposeActiveSession()
    {
        if (_waveInEvent is not null)
        {
            _waveInEvent.DataAvailable -= OnDataAvailable;
            _waveInEvent.RecordingStopped -= OnRecordingStopped;
            _waveInEvent.Dispose();
            _waveInEvent = null;
        }

        _waveFileWriter?.Dispose();
        _waveFileWriter = null;
        _activeFilePath = null;
        _stopTaskCompletionSource = null;
    }

    private static void DeleteFileIfPresent(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch
        {
            // Best effort cleanup for temp audio files.
        }
    }
}
