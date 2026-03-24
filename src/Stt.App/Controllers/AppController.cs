using System.Threading;
using Stt.Core.Abstractions;
using Stt.Core.Diagnostics;
using Stt.Core.Models;

namespace Stt.App.Controllers;

public sealed class AppController
{
    private readonly IClipboardService _clipboardService;
    private readonly object _snapshotSync = new();
    private readonly IRecordingWorkflow _recordingWorkflow;
    private readonly IRecordingWorkflowModeProvider? _recordingWorkflowModeProvider;
    private readonly Func<bool> _streamingEnabledAccessor;
    private readonly SemaphoreSlim _toggleGate = new(1, 1);
    private AppSnapshot _snapshot;
    private RecordingWorkflowMode _activeWorkflowMode = RecordingWorkflowMode.Unknown;
    private static readonly string[] NonCriticalFailureMessages =
    [
        "No audio was captured.",
        "OpenAI returned an empty transcript."
    ];

    public AppController(
        IRecordingWorkflow recordingWorkflow,
        IClipboardService clipboardService,
        Func<bool>? streamingEnabledAccessor = null,
        AppSnapshot? initialSnapshot = null)
    {
        _recordingWorkflow = recordingWorkflow;
        _recordingWorkflowModeProvider = recordingWorkflow as IRecordingWorkflowModeProvider;
        _clipboardService = clipboardService;
        _streamingEnabledAccessor = streamingEnabledAccessor ?? (() => true);
        _snapshot = initialSnapshot ?? AppSnapshot.Idle;
        _recordingWorkflow.TranscriptUpdated += OnTranscriptUpdated;
    }

    public event EventHandler<AppSnapshot>? SnapshotChanged;

    public RecordingWorkflowMode CurrentWorkflowMode
    {
        get
        {
            lock (_snapshotSync)
            {
                return _activeWorkflowMode;
            }
        }
    }

    public async Task ToggleRecordingAsync()
    {
        await _toggleGate.WaitAsync().ConfigureAwait(false);

        try
        {
            switch (_snapshot.State)
            {
                case AppSessionState.Starting:
                    break;
                case AppSessionState.Recording:
                    await StopRecordingAsync().ConfigureAwait(false);
                    break;
                case AppSessionState.Processing:
                    break;
                default:
                    await StartRecordingAsync().ConfigureAwait(false);
                    break;
            }
        }
        finally
        {
            _toggleGate.Release();
        }
    }

    private async Task StartRecordingAsync()
    {
        SetSnapshot(new AppSnapshot(
            AppSessionState.Starting,
            "Connecting to OpenAI and opening the microphone.",
            string.Empty));

        try
        {
            await _recordingWorkflow.StartAsync(CancellationToken.None).ConfigureAwait(false);
            UpdateActiveWorkflowMode();

            SetSnapshot(new AppSnapshot(
                AppSessionState.Recording,
                BuildRecordingStatusMessage(),
                string.Empty));
        }
        catch (Exception ex)
        {
            ResetActiveWorkflowMode();
            ShowFailure(ex.Message);
        }
    }

    private async Task StopRecordingAsync()
    {
        SetSnapshot(new AppSnapshot(
            AppSessionState.Processing,
            BuildProcessingStatusMessage(),
            _snapshot.TranscriptText));

        try
        {
            var transcript = await _recordingWorkflow
                .StopAndTranscribeAsync(CancellationToken.None)
                .ConfigureAwait(false);

            _clipboardService.CopyText(transcript.Text);

            SetSnapshot(new AppSnapshot(
                AppSessionState.Ready,
                BuildReadyStatusMessage(),
                transcript.Text));
        }
        catch (Exception ex)
        {
            ResetActiveWorkflowMode();
            ShowFailure(ex.Message);
        }
        finally
        {
            ResetActiveWorkflowMode();
        }
    }

    private void ShowFailure(string message)
    {
        ResetActiveWorkflowMode();

        if (IsNonCriticalFailure(message))
        {
            SetSnapshot(new AppSnapshot(
                AppSessionState.Idle,
                BuildNonCriticalStatusMessage(message),
                string.Empty));
            return;
        }

        SetSnapshot(new AppSnapshot(
            AppSessionState.Error,
            message,
            string.Empty));
    }

    private void SetSnapshot(AppSnapshot snapshot)
    {
        lock (_snapshotSync)
        {
            _snapshot = snapshot;
        }

        SnapshotChanged?.Invoke(this, snapshot);
    }

    private void OnTranscriptUpdated(object? sender, TranscriptUpdatedEventArgs e)
    {
        AppSnapshot? snapshotToPublish = null;

        lock (_snapshotSync)
        {
            if (_snapshot.State is not AppSessionState.Recording and not AppSessionState.Processing)
            {
                return;
            }

            var statusMessage = _snapshot.State == AppSessionState.Recording
                ? BuildRecordingStatusMessage()
                : BuildProcessingStatusMessage();

            if (string.Equals(_snapshot.TranscriptText, e.Text, StringComparison.Ordinal))
            {
                return;
            }

            _snapshot = new AppSnapshot(
                _snapshot.State,
                statusMessage,
                e.Text);

            snapshotToPublish = _snapshot;
        }

        if (snapshotToPublish is not null)
        {
            SnapshotChanged?.Invoke(this, snapshotToPublish);
        }
    }

    private static bool IsNonCriticalFailure(string message)
    {
        return NonCriticalFailureMessages.Any(knownMessage =>
            string.Equals(knownMessage, message, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildNonCriticalStatusMessage(string message)
    {
        if (string.Equals(message, "No audio was captured.", StringComparison.OrdinalIgnoreCase))
        {
            return "No speech was captured. Ready to record again.";
        }

        if (string.Equals(message, "OpenAI returned an empty transcript.", StringComparison.OrdinalIgnoreCase))
        {
            return "Transcript was empty. Ready to record again.";
        }

        return "Nothing was transcribed. Ready to record again.";
    }

    private string BuildRecordingStatusMessage()
    {
        var mode = CurrentWorkflowMode;

        return mode switch
        {
            RecordingWorkflowMode.RealtimeStreaming =>
                "Recording. Realtime streaming is active.",
            RecordingWorkflowMode.UploadAfterStopFallback =>
                "Recording. Realtime was unavailable, using upload-after-stop fallback.",
            RecordingWorkflowMode.UploadAfterStop =>
                "Recording. Audio will be uploaded after you stop.",
            _ when _streamingEnabledAccessor() =>
                "Recording. Streaming mode was requested.",
            _ =>
                "Recording. Audio will be uploaded after you stop."
        };
    }

    private string BuildProcessingStatusMessage()
    {
        return CurrentWorkflowMode switch
        {
            RecordingWorkflowMode.RealtimeStreaming =>
                "Finalizing realtime transcript and copying text.",
            RecordingWorkflowMode.UploadAfterStopFallback =>
                "Finalizing upload-after-stop fallback transcript and copying text.",
            RecordingWorkflowMode.UploadAfterStop =>
                "Uploading audio and copying text.",
            _ =>
                "Finalizing transcript and copying text."
        };
    }

    private string BuildReadyStatusMessage()
    {
        return CurrentWorkflowMode switch
        {
            RecordingWorkflowMode.RealtimeStreaming =>
                "Realtime transcript copied to clipboard.",
            RecordingWorkflowMode.UploadAfterStopFallback =>
                "Fallback transcript copied to clipboard.",
            RecordingWorkflowMode.UploadAfterStop =>
                "Upload-after-stop transcript copied to clipboard.",
            _ =>
                "Transcript copied to clipboard."
        };
    }

    private void UpdateActiveWorkflowMode()
    {
        var mode = _recordingWorkflowModeProvider?.GetCurrentMode() ?? RecordingWorkflowMode.Unknown;

        lock (_snapshotSync)
        {
            _activeWorkflowMode = mode;
        }

        JotMicTrace.Log("AppController", $"Active workflow mode set to {mode}.");
    }

    private void ResetActiveWorkflowMode()
    {
        lock (_snapshotSync)
        {
            _activeWorkflowMode = RecordingWorkflowMode.Unknown;
        }
    }
}
