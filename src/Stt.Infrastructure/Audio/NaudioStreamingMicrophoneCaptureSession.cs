using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Stt.Core.Abstractions;
using Stt.Core.Diagnostics;
using Stt.Core.Models;

namespace Stt.Infrastructure.Audio;

public sealed class NaudioStreamingMicrophoneCaptureSession : IStreamingAudioCaptureSession, IDisposable
{
    private const int CaptureSampleRate = 16_000;
    private const int TargetSampleRate = 24_000;
    private const int BitsPerSample = 16;
    private const int ChannelCount = 1;
    private const int ChunkDurationMs = 100;
    private const int PublishedChunkBytes = TargetSampleRate * ChannelCount * (BitsPerSample / 8) * ChunkDurationMs / 1_000;
    private static readonly WaveFormat CaptureWaveFormat = new(CaptureSampleRate, BitsPerSample, ChannelCount);

    private readonly Func<string?> _selectedDeviceIdAccessor;
    private readonly object _syncRoot = new();
    private BufferedWaveProvider? _captureBufferProvider;
    private byte[] _pendingResampledAudio = [];
    private int _pendingResampledAudioCount;
    private byte[]? _resampleReadBuffer;
    private SampleToWaveProvider16? _resampledWaveProvider;
    private int _selectedDeviceNumber = -1;
    private long _totalCapturedInputBytes;
    private int _publishedChunkCount;
    private long _totalPublishedOutputBytes;
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

            _captureBufferProvider = new BufferedWaveProvider(CaptureWaveFormat)
            {
                DiscardOnBufferOverflow = true,
                ReadFully = false
            };
            _resampledWaveProvider = new SampleToWaveProvider16(
                new WdlResamplingSampleProvider(
                    _captureBufferProvider.ToSampleProvider(),
                    TargetSampleRate));
            _resampleReadBuffer = new byte[PublishedChunkBytes * 2];
            _pendingResampledAudio = new byte[PublishedChunkBytes * 2];
            _pendingResampledAudioCount = 0;
            _totalCapturedInputBytes = 0;
            _totalPublishedOutputBytes = 0;
            _publishedChunkCount = 0;

            _waveInEvent = new WaveInEvent
            {
                BufferMilliseconds = ChunkDurationMs,
                NumberOfBuffers = 3,
                WaveFormat = CaptureWaveFormat
            };

            var deviceNumber = MicrophoneDeviceCatalog.ResolveDeviceNumber(_selectedDeviceIdAccessor());
            _selectedDeviceNumber = deviceNumber ?? -1;
            if (deviceNumber.HasValue)
            {
                _waveInEvent.DeviceNumber = deviceNumber.Value;
            }

            _waveInEvent.DataAvailable += OnDataAvailable;
            _waveInEvent.RecordingStopped += OnRecordingStopped;
            _waveInEvent.StartRecording();

            WhisperTrace.Log(
                "RealtimeCapture",
                $"Streaming microphone capture started. Device={FormatDeviceNumber(_selectedDeviceNumber)} InputRate={CaptureSampleRate}Hz OutputRate={TargetSampleRate}Hz ChunkMs={ChunkDurationMs}.");
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

        List<byte[]> publishedChunks;
        int bufferedBytes;

        lock (_syncRoot)
        {
            if (_captureBufferProvider is null || _resampledWaveProvider is null || _resampleReadBuffer is null)
            {
                return;
            }

            _totalCapturedInputBytes += e.BytesRecorded;
            _captureBufferProvider.AddSamples(e.Buffer, 0, e.BytesRecorded);
            publishedChunks = DrainResamplerLocked(flushAll: false);
            bufferedBytes = _captureBufferProvider.BufferedBytes;
        }

        PublishChunks(publishedChunks);

        if (bufferedBytes > CaptureWaveFormat.AverageBytesPerSecond)
        {
            WhisperTrace.Log(
                "RealtimeCapture",
                $"Streaming capture backlog detected. BufferedBytes={bufferedBytes} Device={FormatDeviceNumber(_selectedDeviceNumber)}.");
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        TaskCompletionSource<bool>? completionSource;
        List<byte[]> publishedChunks;
        long totalCapturedInputBytes;
        long totalPublishedOutputBytes;
        int publishedChunkCount;
        int selectedDeviceNumber;

        lock (_syncRoot)
        {
            publishedChunks = DrainResamplerLocked(flushAll: true);
            completionSource = _stopTaskCompletionSource;
            totalCapturedInputBytes = _totalCapturedInputBytes;
            totalPublishedOutputBytes = _totalPublishedOutputBytes;
            publishedChunkCount = _publishedChunkCount;
            selectedDeviceNumber = _selectedDeviceNumber;
            DisposeActiveSession();
        }

        PublishChunks(publishedChunks);

        if (completionSource is null)
        {
            return;
        }

        if (e.Exception is not null)
        {
            completionSource.TrySetException(e.Exception);
            return;
        }

        WhisperTrace.Log(
            "RealtimeCapture",
            $"Streaming microphone capture stopped. Device={FormatDeviceNumber(selectedDeviceNumber)} InputBytes={totalCapturedInputBytes} OutputBytes={totalPublishedOutputBytes} Chunks={publishedChunkCount}.");
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

        _captureBufferProvider = null;
        _resampledWaveProvider = null;
        _resampleReadBuffer = null;
        _pendingResampledAudio = [];
        _pendingResampledAudioCount = 0;
        _selectedDeviceNumber = -1;
        _totalCapturedInputBytes = 0;
        _publishedChunkCount = 0;
        _totalPublishedOutputBytes = 0;
        _stopTaskCompletionSource = null;
    }

    private List<byte[]> DrainResamplerLocked(bool flushAll)
    {
        var chunks = new List<byte[]>();
        if (_resampledWaveProvider is null || _resampleReadBuffer is null)
        {
            return chunks;
        }

        while (true)
        {
            var read = _resampledWaveProvider.Read(_resampleReadBuffer, 0, _resampleReadBuffer.Length);
            if (read <= 0)
            {
                break;
            }

            AppendPendingResampledAudioLocked(_resampleReadBuffer.AsSpan(0, read));
            EmitReadyChunksLocked(chunks);
        }

        if (flushAll && _pendingResampledAudioCount > 0)
        {
            var finalChunk = new byte[_pendingResampledAudioCount];
            Buffer.BlockCopy(_pendingResampledAudio, 0, finalChunk, 0, _pendingResampledAudioCount);
            chunks.Add(finalChunk);
            _totalPublishedOutputBytes += finalChunk.Length;
            _publishedChunkCount++;
            _pendingResampledAudioCount = 0;
        }

        return chunks;
    }

    private void AppendPendingResampledAudioLocked(ReadOnlySpan<byte> audioBytes)
    {
        if (audioBytes.IsEmpty)
        {
            return;
        }

        var requiredLength = _pendingResampledAudioCount + audioBytes.Length;
        if (_pendingResampledAudio.Length < requiredLength)
        {
            var newLength = Math.Max(
                requiredLength,
                _pendingResampledAudio.Length == 0
                    ? PublishedChunkBytes * 2
                    : _pendingResampledAudio.Length * 2);
            Array.Resize(ref _pendingResampledAudio, newLength);
        }

        audioBytes.CopyTo(_pendingResampledAudio.AsSpan(_pendingResampledAudioCount));
        _pendingResampledAudioCount = requiredLength;
    }

    private void EmitReadyChunksLocked(List<byte[]> chunks)
    {
        while (_pendingResampledAudioCount >= PublishedChunkBytes)
        {
            var chunk = new byte[PublishedChunkBytes];
            Buffer.BlockCopy(_pendingResampledAudio, 0, chunk, 0, PublishedChunkBytes);
            chunks.Add(chunk);
            _totalPublishedOutputBytes += chunk.Length;
            _publishedChunkCount++;

            _pendingResampledAudioCount -= PublishedChunkBytes;
            if (_pendingResampledAudioCount > 0)
            {
                Buffer.BlockCopy(
                    _pendingResampledAudio,
                    PublishedChunkBytes,
                    _pendingResampledAudio,
                    0,
                    _pendingResampledAudioCount);
            }
        }
    }

    private void PublishChunks(IEnumerable<byte[]> chunks)
    {
        foreach (var chunk in chunks)
        {
            AudioChunkCaptured?.Invoke(this, new AudioChunkCapturedEventArgs(chunk, chunk.Length));
        }
    }

    private static string FormatDeviceNumber(int deviceNumber)
    {
        return deviceNumber >= 0
            ? deviceNumber.ToString()
            : "default";
    }
}
