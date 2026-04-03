using Stt.Core.Abstractions;
using Stt.Core.Diagnostics;
using Stt.Core.Models;

namespace Stt.Infrastructure.Workflows;

public sealed class OpenAiRecordingWorkflow : IRecordingWorkflow, IRecordingWorkflowModeProvider, IRecordingWorkflowDeferredStop
{
    private readonly IAudioCaptureSession _audioCaptureSession;
    private readonly ITranscriptionClient _transcriptionClient;
    private readonly object _syncRoot = new();
    private bool _isRecording;

    public OpenAiRecordingWorkflow(
        IAudioCaptureSession audioCaptureSession,
        ITranscriptionClient transcriptionClient)
    {
        _audioCaptureSession = audioCaptureSession;
        _transcriptionClient = transcriptionClient;
    }

    public event EventHandler<TranscriptUpdatedEventArgs>? TranscriptUpdated
    {
        add { }
        remove { }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        WhisperTrace.Log("BatchWorkflow", "Starting upload-after-stop recording session.");

        lock (_syncRoot)
        {
            if (_isRecording)
            {
                throw new InvalidOperationException("A recording session is already active.");
            }

            _transcriptionClient.ValidateConfiguration();
        }

        await _audioCaptureSession.StartAsync(cancellationToken).ConfigureAwait(false);

        lock (_syncRoot)
        {
            _isRecording = true;
        }
    }

    public async Task<TranscriptResult> StopAndTranscribeAsync(CancellationToken cancellationToken)
    {
        await using var pendingTranscription = await StopForDeferredTranscriptionAsync(cancellationToken).ConfigureAwait(false);
        return await pendingTranscription.TranscribeAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<PendingTranscription> StopForDeferredTranscriptionAsync(CancellationToken cancellationToken)
    {
        lock (_syncRoot)
        {
            if (!_isRecording)
            {
                throw new InvalidOperationException("No active recording session was found.");
            }
        }

        CapturedAudioFile? audioFile = null;

        try
        {
            audioFile = await _audioCaptureSession.StopAsync(cancellationToken).ConfigureAwait(false);
            var capturedAudioFile = audioFile;
            audioFile = null;

            return new PendingTranscription(
                async token =>
                {
                    try
                    {
                        var result = await _transcriptionClient.TranscribeAsync(capturedAudioFile, token).ConfigureAwait(false);
                        WhisperTrace.Log("BatchWorkflow", $"Upload-after-stop transcription completed. Length={result.Text.Length}.");
                        return result;
                    }
                    finally
                    {
                        capturedAudioFile.Dispose();
                    }
                },
                _ =>
                {
                    WhisperTrace.Log("BatchWorkflow", "Upload-after-stop recording discarded before transcription.");
                    capturedAudioFile.Dispose();
                    return Task.CompletedTask;
                });
        }
        finally
        {
            audioFile?.Dispose();

            lock (_syncRoot)
            {
                _isRecording = false;
            }
        }
    }

    public RecordingWorkflowMode GetCurrentMode() => RecordingWorkflowMode.UploadAfterStop;
}
