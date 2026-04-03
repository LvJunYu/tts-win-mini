using System.Threading;
using Stt.App;
using Stt.App.Common;
using Stt.Core.Abstractions;
using Stt.Core.Diagnostics;
using Stt.Core.Models;

namespace Stt.App.Controllers;

public sealed class AppController
{
    private const int AutoPasteDelayMilliseconds = 75;
    private static readonly TimeSpan LongNonStreamingConfirmationThreshold =
        TimeSpan.FromMinutes(AppDefaults.LongNonStreamingConfirmationThresholdMinutes);
    private static readonly TimeSpan RecordingMonitorInterval = TimeSpan.FromSeconds(5);
    private readonly IClipboardService _clipboardService;
    private readonly Func<bool> _autoPasteAfterCopyAccessor;
    private readonly Func<TimeSpan, CancellationToken, Task<bool>> _confirmLongRecordingTranscriptionAsync;
    private readonly Func<int> _maxStreamingLengthMinutesAccessor;
    private readonly IPasteShortcutService _pasteShortcutService;
    private readonly object _snapshotSync = new();
    private readonly IRecordingWorkflow _recordingWorkflow;
    private readonly IRecordingWorkflowDeferredStop? _recordingWorkflowDeferredStop;
    private readonly IRecordingWorkflowModeProvider? _recordingWorkflowModeProvider;
    private readonly IRecordingWorkflowStartupNotifier? _recordingWorkflowStartupNotifier;
    private readonly Func<bool> _streamingEnabledAccessor;
    private readonly SemaphoreSlim _toggleGate = new(1, 1);
    private AppSnapshot _snapshot;
    private RecordingWorkflowMode _activeWorkflowMode = RecordingWorkflowMode.Unknown;
    private CancellationTokenSource? _recordingMonitorCancellation;
    private DateTimeOffset? _recordingStartedAtUtc;
    private bool _streamingHardCapTriggered;
    private static readonly string[] NonCriticalFailureMessages =
    [
        "No audio was captured.",
        "OpenAI returned an empty transcript."
    ];

    public AppController(
        IRecordingWorkflow recordingWorkflow,
        IClipboardService clipboardService,
        IPasteShortcutService pasteShortcutService,
        Func<bool>? streamingEnabledAccessor = null,
        Func<bool>? autoPasteAfterCopyAccessor = null,
        Func<int>? maxStreamingLengthMinutesAccessor = null,
        Func<TimeSpan, CancellationToken, Task<bool>>? confirmLongRecordingTranscriptionAsync = null,
        AppSnapshot? initialSnapshot = null)
    {
        _recordingWorkflow = recordingWorkflow;
        _recordingWorkflowDeferredStop = recordingWorkflow as IRecordingWorkflowDeferredStop;
        _recordingWorkflowModeProvider = recordingWorkflow as IRecordingWorkflowModeProvider;
        _recordingWorkflowStartupNotifier = recordingWorkflow as IRecordingWorkflowStartupNotifier;
        _clipboardService = clipboardService;
        _pasteShortcutService = pasteShortcutService;
        _streamingEnabledAccessor = streamingEnabledAccessor ?? (() => true);
        _autoPasteAfterCopyAccessor = autoPasteAfterCopyAccessor ?? (() => false);
        _maxStreamingLengthMinutesAccessor = maxStreamingLengthMinutesAccessor
            ?? (() => AppDefaults.DefaultMaxStreamingLengthMinutes);
        _confirmLongRecordingTranscriptionAsync = confirmLongRecordingTranscriptionAsync
            ?? ((_, _) => Task.FromResult(true));
        _snapshot = initialSnapshot ?? AppSnapshot.Idle;
        _recordingWorkflow.TranscriptUpdated += OnTranscriptUpdated;
        _recordingWorkflowStartupNotifier?.RecordingStarted += OnRecordingStarted;
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
            "Getting things ready.\n\nOpening the microphone now.",
            string.Empty));

        try
        {
            await _recordingWorkflow.StartAsync(CancellationToken.None).ConfigureAwait(false);
            UpdateActiveWorkflowMode();

            SetSnapshot(new AppSnapshot(
                AppSessionState.Recording,
                BuildRecordingStatusMessage(),
                string.Empty));
            BeginRecordingMonitoring();
        }
        catch (Exception ex)
        {
            EndRecordingMonitoring();
            ResetActiveWorkflowMode();
            ShowFailure(ex.Message);
        }
    }

    private void OnRecordingStarted(object? sender, EventArgs e)
    {
        AppSnapshot? snapshotToPublish = null;

        lock (_snapshotSync)
        {
            if (_snapshot.State != AppSessionState.Starting)
            {
                return;
            }

            _snapshot = new AppSnapshot(
                AppSessionState.Recording,
                BuildRecordingStatusMessage(),
                _snapshot.TranscriptText);

            snapshotToPublish = _snapshot;
        }

        BeginRecordingMonitoring();

        if (snapshotToPublish is not null)
        {
            SnapshotChanged?.Invoke(this, snapshotToPublish);
        }
    }

    private async Task StopRecordingAsync()
    {
        if (ShouldUseDeferredStopForCurrentMode())
        {
            await StopNonStreamingRecordingAsync().ConfigureAwait(false);
            return;
        }

        var mode = CurrentWorkflowMode;

        SetSnapshot(new AppSnapshot(
            AppSessionState.Processing,
            BuildProcessingStatusMessage(mode),
            _snapshot.TranscriptText));
        EndRecordingMonitoring();

        try
        {
            var transcript = await _recordingWorkflow
                .StopAndTranscribeAsync(CancellationToken.None)
                .ConfigureAwait(false);

            await PublishCompletedTranscriptAsync(
                transcript,
                mode,
                allowAutoPaste: true).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ResetActiveWorkflowMode();
            ShowFailure(ex.Message);
        }
        finally
        {
            EndRecordingMonitoring();
            ResetActiveWorkflowMode();
        }
    }

    private async Task StopNonStreamingRecordingAsync()
    {
        var mode = CurrentWorkflowMode;
        var elapsed = GetElapsedRecordingDuration() ?? TimeSpan.Zero;
        PendingTranscription? pendingTranscription = null;

        try
        {
            EndRecordingMonitoring();
            pendingTranscription = await _recordingWorkflowDeferredStop!
                .StopForDeferredTranscriptionAsync(CancellationToken.None)
                .ConfigureAwait(false);

            var shouldTranscribe = true;
            if (elapsed >= LongNonStreamingConfirmationThreshold)
            {
                shouldTranscribe = await _confirmLongRecordingTranscriptionAsync(elapsed, CancellationToken.None)
                    .ConfigureAwait(false);
            }

            if (!shouldTranscribe)
            {
                await pendingTranscription.DiscardAsync(CancellationToken.None).ConfigureAwait(false);
                SetSnapshot(new AppSnapshot(
                    AppSessionState.Idle,
                    $"Discarded {DurationTextFormatter.FormatReadable(elapsed)} recording. Ready to record again.",
                    string.Empty));
                return;
            }

            SetSnapshot(new AppSnapshot(
                AppSessionState.Processing,
                BuildProcessingStatusMessage(mode),
                _snapshot.TranscriptText));

            var transcript = await pendingTranscription
                .TranscribeAsync(CancellationToken.None)
                .ConfigureAwait(false);

            await PublishCompletedTranscriptAsync(
                transcript,
                mode,
                allowAutoPaste: true).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ResetActiveWorkflowMode();
            ShowFailure(ex.Message);
        }
        finally
        {
            if (pendingTranscription is not null)
            {
                await pendingTranscription.DisposeAsync().ConfigureAwait(false);
            }

            EndRecordingMonitoring();
            ResetActiveWorkflowMode();
        }
    }

    private void ShowFailure(string message)
    {
        EndRecordingMonitoring();
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

            if (string.Equals(_snapshot.TranscriptText, e.Text, StringComparison.Ordinal))
            {
                return;
            }

            _snapshot = new AppSnapshot(
                _snapshot.State,
                _snapshot.State == AppSessionState.Recording
                    ? BuildRecordingStatusMessage()
                    : _snapshot.StatusMessage,
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
                "Recording started.\n\nLive transcript will appear here.",
            RecordingWorkflowMode.UploadAfterStopFallback =>
                "Recording started.\n\nRealtime was unavailable, so your transcript will appear here after you stop.",
            RecordingWorkflowMode.UploadAfterStop =>
                "Recording started.\n\nYour transcript will appear here after you stop.",
            _ when _streamingEnabledAccessor() =>
                "Recording started.\n\nLive transcript will appear here.",
            _ =>
                "Recording started.\n\nYour transcript will appear here after you stop."
        };
    }

    private static string BuildProcessingStatusMessage(RecordingWorkflowMode mode)
    {
        return mode switch
        {
            RecordingWorkflowMode.RealtimeStreaming =>
                "Wrapping up your realtime transcript.\n\nThis should only take a moment.",
            RecordingWorkflowMode.UploadAfterStopFallback =>
                "Transcribing your recording now.\n\nThis should only take a moment.",
            RecordingWorkflowMode.UploadAfterStop =>
                "Transcribing your recording now.\n\nThis should only take a moment.",
            _ =>
                "Working on your transcript now.\n\nThis should only take a moment."
        };
    }

    private static string BuildReadyStatusMessage(
        RecordingWorkflowMode mode,
        bool autoPasteRequested,
        bool pasteShortcutSent)
    {
        var baseMessage = mode switch
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

        if (!autoPasteRequested)
        {
            return baseMessage;
        }

        return pasteShortcutSent
            ? baseMessage.Replace(
                "copied to clipboard.",
                "copied to clipboard and Ctrl+V sent to the current focus.",
                StringComparison.Ordinal)
            : $"{baseMessage} Ctrl+V could not be sent.";
    }

    private void UpdateActiveWorkflowMode()
    {
        var mode = _recordingWorkflowModeProvider?.GetCurrentMode() ?? RecordingWorkflowMode.Unknown;

        lock (_snapshotSync)
        {
            _activeWorkflowMode = mode;
        }

        WhisperTrace.Log("AppController", $"Active workflow mode set to {mode}.");
    }

    private void ResetActiveWorkflowMode()
    {
        lock (_snapshotSync)
        {
            _activeWorkflowMode = RecordingWorkflowMode.Unknown;
        }
    }

    private bool ShouldUseDeferredStopForCurrentMode()
    {
        var mode = CurrentWorkflowMode;
        return _recordingWorkflowDeferredStop is not null
            && mode is RecordingWorkflowMode.UploadAfterStop or RecordingWorkflowMode.UploadAfterStopFallback;
    }

    private void BeginRecordingMonitoring()
    {
        CancellationTokenSource? cancellationToDispose = null;
        CancellationTokenSource? monitorCancellation;

        lock (_snapshotSync)
        {
            if (_recordingStartedAtUtc is not null)
            {
                return;
            }

            cancellationToDispose = _recordingMonitorCancellation;
            _recordingStartedAtUtc = DateTimeOffset.UtcNow;
            _streamingHardCapTriggered = false;
            _recordingMonitorCancellation = new CancellationTokenSource();
            monitorCancellation = _recordingMonitorCancellation;
        }

        cancellationToDispose?.Cancel();
        cancellationToDispose?.Dispose();

        _ = Task.Run(() => MonitorRecordingAsync(monitorCancellation.Token));
    }

    private void EndRecordingMonitoring()
    {
        CancellationTokenSource? cancellationToDispose;

        lock (_snapshotSync)
        {
            cancellationToDispose = _recordingMonitorCancellation;
            _recordingMonitorCancellation = null;
            _recordingStartedAtUtc = null;
            _streamingHardCapTriggered = false;
        }

        cancellationToDispose?.Cancel();
        cancellationToDispose?.Dispose();
    }

    private async Task MonitorRecordingAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(RecordingMonitorInterval);

            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                lock (_snapshotSync)
                {
                    if (_snapshot.State != AppSessionState.Recording || _recordingStartedAtUtc is null)
                    {
                        return;
                    }

                    if (_activeWorkflowMode != RecordingWorkflowMode.RealtimeStreaming)
                    {
                        continue;
                    }

                    var maxStreamingLength = TimeSpan.FromMinutes(Math.Max(1, _maxStreamingLengthMinutesAccessor()));
                    var elapsed = DateTimeOffset.UtcNow - _recordingStartedAtUtc.Value;

                    if (!_streamingHardCapTriggered && elapsed >= maxStreamingLength)
                    {
                        _streamingHardCapTriggered = true;
                        _ = Task.Run(StopStreamingForHardCapAsync);
                        return;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when recording stops or the app exits.
        }
    }

    private async Task StopStreamingForHardCapAsync()
    {
        await _toggleGate.WaitAsync().ConfigureAwait(false);

        try
        {
            TimeSpan elapsed;
            string transcriptText;

            lock (_snapshotSync)
            {
                if (_snapshot.State != AppSessionState.Recording)
                {
                    return;
                }

                elapsed = _recordingStartedAtUtc is null
                    ? TimeSpan.FromMinutes(Math.Max(1, _maxStreamingLengthMinutesAccessor()))
                    : DateTimeOffset.UtcNow - _recordingStartedAtUtc.Value;
                transcriptText = _snapshot.TranscriptText;
            }

            SetSnapshot(new AppSnapshot(
                AppSessionState.Processing,
                $"Recording auto-stopped at {DurationTextFormatter.FormatReadable(elapsed)}.\n\nWrapping up your realtime transcript now.",
                transcriptText));
            EndRecordingMonitoring();

            try
            {
                var transcript = await _recordingWorkflow
                    .StopAndTranscribeAsync(CancellationToken.None)
                    .ConfigureAwait(false);

                await PublishCompletedTranscriptAsync(
                    transcript,
                    RecordingWorkflowMode.RealtimeStreaming,
                    allowAutoPaste: true).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ResetActiveWorkflowMode();
                ShowFailure(ex.Message);
            }
            finally
            {
                EndRecordingMonitoring();
                ResetActiveWorkflowMode();
            }
        }
        finally
        {
            _toggleGate.Release();
        }
    }

    private async Task PublishCompletedTranscriptAsync(
        TranscriptResult transcript,
        RecordingWorkflowMode mode,
        bool allowAutoPaste)
    {
        _clipboardService.CopyText(transcript.Text);
        var autoPasteRequested = allowAutoPaste && _autoPasteAfterCopyAccessor();
        var pasteShortcutSent = false;

        if (autoPasteRequested)
        {
            await Task.Delay(AutoPasteDelayMilliseconds).ConfigureAwait(false);
            pasteShortcutSent = _pasteShortcutService.TrySendPasteShortcut();
        }

        SetSnapshot(new AppSnapshot(
            AppSessionState.Ready,
            BuildReadyStatusMessage(mode, autoPasteRequested, pasteShortcutSent),
            transcript.Text));
    }

    private TimeSpan? GetElapsedRecordingDuration()
    {
        lock (_snapshotSync)
        {
            return _recordingStartedAtUtc is null
                ? null
                : DateTimeOffset.UtcNow - _recordingStartedAtUtc.Value;
        }
    }
}
