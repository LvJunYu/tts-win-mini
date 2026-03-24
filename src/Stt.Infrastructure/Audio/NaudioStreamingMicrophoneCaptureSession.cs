using NAudio.Wave;
using Stt.Core.Abstractions;
using Stt.Core.Models;

namespace Stt.Infrastructure.Audio;

public sealed class NaudioStreamingMicrophoneCaptureSession : IStreamingAudioCaptureSession, IDisposable
{
    private readonly Func<string?> _selectedDeviceIdAccessor;
    private readonly object _syncRoot = new();
    private WaveInEvent? _waveInEvent;
    private TaskCompletionSource<bool>? _stopTaskCompletionSource;

    public NaudioStreamingMicrophoneCaptureSession(Func<string?> selectedDeviceIdAccessor)
    {
        _selectedDeviceIdAccessor = selectedDeviceIdAccessor;
    }

    public event EventHandler<AudioChunkCapturedEventArgs>? AudioChunkCaptured;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            if (_waveInEvent is not null)
            {
                throw new InvalidOperationException("A streaming recording session is already active.");
            }

            _stopTaskCompletionSource = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            _waveInEvent = new WaveInEvent
            {
                BufferMilliseconds = 100,
                NumberOfBuffers = 3,
                WaveFormat = new WaveFormat(24_000, 16, 1)
            };

            var deviceNumber = MicrophoneDeviceCatalog.ResolveDeviceNumber(_selectedDeviceIdAccessor());
            if (deviceNumber.HasValue)
            {
                _waveInEvent.DeviceNumber = deviceNumber.Value;
            }

            _waveInEvent.DataAvailable += OnDataAvailable;
            _waveInEvent.RecordingStopped += OnRecordingStopped;
            _waveInEvent.StartRecording();
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Task stopTask;

        lock (_syncRoot)
        {
            if (_waveInEvent is null || _stopTaskCompletionSource is null)
            {
                throw new InvalidOperationException("No streaming recording session is currently active.");
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
        if (e.BytesRecorded <= 0)
        {
            return;
        }

        var copy = new byte[e.BytesRecorded];
        Buffer.BlockCopy(e.Buffer, 0, copy, 0, e.BytesRecorded);
        AudioChunkCaptured?.Invoke(this, new AudioChunkCapturedEventArgs(copy, e.BytesRecorded));
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        TaskCompletionSource<bool>? completionSource;

        lock (_syncRoot)
        {
            completionSource = _stopTaskCompletionSource;
            DisposeActiveSession();
        }

        if (completionSource is null)
        {
            return;
        }

        if (e.Exception is not null)
        {
            completionSource.TrySetException(e.Exception);
            return;
        }

        completionSource.TrySetResult(true);
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

        _stopTaskCompletionSource = null;
    }
}
