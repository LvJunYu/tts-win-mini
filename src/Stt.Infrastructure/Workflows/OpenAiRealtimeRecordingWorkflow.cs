using System.Threading.Channels;
using Stt.Core.Abstractions;
using Stt.Core.Diagnostics;
using Stt.Core.Models;

namespace Stt.Infrastructure.Workflows;

public sealed class OpenAiRealtimeRecordingWorkflow : IRecordingWorkflow, IRecordingWorkflowModeProvider, IRecordingWorkflowStartupNotifier
{
    private readonly IStreamingAudioCaptureSession _audioCaptureSession;
    private readonly IRealtimeTranscriptionClient _realtimeTranscriptionClient;
    private readonly object _syncRoot = new();
    private Channel<byte[]>? _audioChannel;
    private Task? _audioPumpTask;
    private bool _isRecording;
    private long _totalCapturedBytes;
    private IRealtimeTranscriptionSession? _transcriptionSession;

    public OpenAiRealtimeRecordingWorkflow(
        IStreamingAudioCaptureSession audioCaptureSession,
        IRealtimeTranscriptionClient realtimeTranscriptionClient)
    {
        _audioCaptureSession = audioCaptureSession;
        _realtimeTranscriptionClient = realtimeTranscriptionClient;
    }

    public event EventHandler<TranscriptUpdatedEventArgs>? TranscriptUpdated;
    public event EventHandler? RecordingStarted;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        WhisperTrace.Log(
            "RealtimeWorkflow",
            "Starting realtime recording session. Opening microphone first, then connecting the realtime session.");

        lock (_syncRoot)
        {
            if (_isRecording)
            {
                throw new InvalidOperationException("A recording session is already active.");
            }

            _realtimeTranscriptionClient.ValidateConfiguration();
        }

        var audioChannel = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        IRealtimeTranscriptionSession? transcriptionSession = null;
        var microphoneStarted = false;

        lock (_syncRoot)
        {
            _audioChannel = audioChannel;
            _audioPumpTask = null;
            _transcriptionSession = null;
            _totalCapturedBytes = 0;
            _isRecording = true;
        }

        _audioCaptureSession.AudioChunkCaptured += OnAudioChunkCaptured;

        try
        {
            await _audioCaptureSession.StartAsync(cancellationToken).ConfigureAwait(false);
            microphoneStarted = true;
            RecordingStarted?.Invoke(this, EventArgs.Empty);

            WhisperTrace.Log(
                "RealtimeWorkflow",
                "Microphone capture started before realtime session connection completed.");

            transcriptionSession = await _realtimeTranscriptionClient
                .CreateSessionAsync(cancellationToken)
                .ConfigureAwait(false);

            transcriptionSession.TranscriptUpdated += OnTranscriptUpdated;

            lock (_syncRoot)
            {
                _audioPumpTask = PumpAudioAsync(audioChannel.Reader, transcriptionSession, cancellationToken);
                _transcriptionSession = transcriptionSession;
            }

            WhisperTrace.Log(
                "RealtimeWorkflow",
                "Realtime session connected. Streaming buffered microphone audio to OpenAI.");
        }
        catch
        {
            _audioCaptureSession.AudioChunkCaptured -= OnAudioChunkCaptured;

            if (microphoneStarted)
            {
                try
                {
                    await _audioCaptureSession.StopAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // Best effort cleanup when startup fails after the microphone is already active.
                }
            }

            audioChannel.Writer.TryComplete();

            if (_audioPumpTask is not null)
            {
                try
                {
                    await _audioPumpTask.ConfigureAwait(false);
                }
                catch
                {
                    // Best effort cleanup when the realtime startup path fails.
                }
            }

            if (transcriptionSession is not null)
            {
                transcriptionSession.TranscriptUpdated -= OnTranscriptUpdated;
                await transcriptionSession.DisposeAsync().ConfigureAwait(false);
            }

            lock (_syncRoot)
            {
                _audioChannel = null;
                _audioPumpTask = null;
                _transcriptionSession = null;
                _totalCapturedBytes = 0;
                _isRecording = false;
            }

            WhisperTrace.Log(
                "RealtimeWorkflow",
                "Realtime session startup failed after microphone-first startup path.");
            throw;
        }
    }

    public async Task<TranscriptResult> StopAndTranscribeAsync(CancellationToken cancellationToken)
    {
        Channel<byte[]>? audioChannel;
        Task? audioPumpTask;
        IRealtimeTranscriptionSession? transcriptionSession;

        lock (_syncRoot)
        {
            if (!_isRecording)
            {
                throw new InvalidOperationException("No active recording session was found.");
            }

            audioChannel = _audioChannel;
            audioPumpTask = _audioPumpTask;
            transcriptionSession = _transcriptionSession;
        }

        if (audioChannel is null || audioPumpTask is null || transcriptionSession is null)
        {
            throw new InvalidOperationException("Streaming transcription session was not initialized correctly.");
        }

        try
        {
            await _audioCaptureSession.StopAsync(cancellationToken).ConfigureAwait(false);
            _audioCaptureSession.AudioChunkCaptured -= OnAudioChunkCaptured;
            audioChannel.Writer.TryComplete();
            await audioPumpTask.ConfigureAwait(false);

            if (Interlocked.Read(ref _totalCapturedBytes) <= 0)
            {
                WhisperTrace.Log("RealtimeWorkflow", "Realtime session ended with no captured audio.");
                throw new InvalidOperationException("No audio was captured.");
            }

            var result = await transcriptionSession.CompleteAsync(cancellationToken).ConfigureAwait(false);
            WhisperTrace.Log(
                "RealtimeWorkflow",
                $"Realtime transcription completed. AudioBytes={Interlocked.Read(ref _totalCapturedBytes)} Length={result.Text.Length}.");
            return result;
        }
        finally
        {
            _audioCaptureSession.AudioChunkCaptured -= OnAudioChunkCaptured;
            audioChannel.Writer.TryComplete();
            transcriptionSession.TranscriptUpdated -= OnTranscriptUpdated;
            await transcriptionSession.DisposeAsync().ConfigureAwait(false);

            lock (_syncRoot)
            {
                _audioChannel = null;
                _audioPumpTask = null;
                _transcriptionSession = null;
                _totalCapturedBytes = 0;
                _isRecording = false;
            }
        }
    }

    public RecordingWorkflowMode GetCurrentMode() => RecordingWorkflowMode.RealtimeStreaming;

    private async Task PumpAudioAsync(
        ChannelReader<byte[]> reader,
        IRealtimeTranscriptionSession transcriptionSession,
        CancellationToken cancellationToken)
    {
        await foreach (var chunk in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            await transcriptionSession.AppendAudioAsync(chunk, cancellationToken).ConfigureAwait(false);
        }
    }

    private void OnAudioChunkCaptured(object? sender, AudioChunkCapturedEventArgs e)
    {
        var channel = _audioChannel;
        if (channel is null)
        {
            return;
        }

        Interlocked.Add(ref _totalCapturedBytes, e.BytesRecorded);

        if (!channel.Writer.TryWrite(e.Buffer))
        {
            throw new InvalidOperationException("Streaming audio buffer is unavailable.");
        }
    }

    private void OnTranscriptUpdated(object? sender, TranscriptUpdatedEventArgs e)
    {
        TranscriptUpdated?.Invoke(this, e);
    }
}
