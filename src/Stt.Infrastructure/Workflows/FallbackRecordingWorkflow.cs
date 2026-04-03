using Stt.Core.Abstractions;
using Stt.Core.Diagnostics;
using Stt.Core.Models;

namespace Stt.Infrastructure.Workflows;

public sealed class FallbackRecordingWorkflow : IRecordingWorkflow, IRecordingWorkflowModeProvider, IRecordingWorkflowStartupNotifier, IRecordingWorkflowDeferredStop
{
    private readonly IRecordingWorkflow _fallbackWorkflow;
    private readonly IRecordingWorkflow _primaryWorkflow;
    private readonly object _syncRoot = new();
    private IRecordingWorkflow? _activeWorkflow;
    private RecordingWorkflowMode _activeMode = RecordingWorkflowMode.Unknown;

    public FallbackRecordingWorkflow(
        IRecordingWorkflow primaryWorkflow,
        IRecordingWorkflow fallbackWorkflow)
    {
        _primaryWorkflow = primaryWorkflow;
        _fallbackWorkflow = fallbackWorkflow;

        _primaryWorkflow.TranscriptUpdated += OnTranscriptUpdated;
        _fallbackWorkflow.TranscriptUpdated += OnTranscriptUpdated;

        if (_primaryWorkflow is IRecordingWorkflowStartupNotifier primaryStartupNotifier)
        {
            primaryStartupNotifier.RecordingStarted += OnRecordingStarted;
        }

        if (_fallbackWorkflow is IRecordingWorkflowStartupNotifier fallbackStartupNotifier)
        {
            fallbackStartupNotifier.RecordingStarted += OnRecordingStarted;
        }
    }

    public event EventHandler<TranscriptUpdatedEventArgs>? TranscriptUpdated;
    public event EventHandler? RecordingStarted;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            lock (_syncRoot)
            {
                _activeWorkflow = _primaryWorkflow;
                _activeMode = ResolveWorkflowMode(_primaryWorkflow);
            }

            await _primaryWorkflow.StartAsync(cancellationToken).ConfigureAwait(false);

            lock (_syncRoot)
            {
                _activeWorkflow = _primaryWorkflow;
                _activeMode = ResolveWorkflowMode(_primaryWorkflow);
            }

            WhisperTrace.Log("FallbackWorkflow", $"Primary workflow active: {_activeMode}.");

            return;
        }
        catch (Exception primaryException)
        {
            WhisperTrace.Log(
                "FallbackWorkflow",
                $"Primary workflow failed to start. Falling back. Reason={primaryException.Message}");

            try
            {
                lock (_syncRoot)
                {
                    _activeWorkflow = _fallbackWorkflow;
                    _activeMode = RecordingWorkflowMode.UploadAfterStopFallback;
                }

                await _fallbackWorkflow.StartAsync(cancellationToken).ConfigureAwait(false);

                lock (_syncRoot)
                {
                    _activeWorkflow = _fallbackWorkflow;
                    _activeMode = RecordingWorkflowMode.UploadAfterStopFallback;
                }

                WhisperTrace.Log("FallbackWorkflow", "Fallback workflow active: UploadAfterStopFallback.");
            }
            catch
            {
                lock (_syncRoot)
                {
                    _activeWorkflow = null;
                    _activeMode = RecordingWorkflowMode.Unknown;
                }

                throw new InvalidOperationException(
                    $"Streaming transcription could not start. {primaryException.Message}",
                    primaryException);
            }
        }
    }

    public async Task<TranscriptResult> StopAndTranscribeAsync(CancellationToken cancellationToken)
    {
        IRecordingWorkflow activeWorkflow;

        lock (_syncRoot)
        {
            activeWorkflow = _activeWorkflow
                ?? throw new InvalidOperationException("No active recording session was found.");
        }

        try
        {
            return await activeWorkflow.StopAndTranscribeAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (_syncRoot)
            {
                _activeWorkflow = null;
                _activeMode = RecordingWorkflowMode.Unknown;
            }
        }
    }

    public async Task<PendingTranscription> StopForDeferredTranscriptionAsync(CancellationToken cancellationToken)
    {
        IRecordingWorkflow activeWorkflow;

        lock (_syncRoot)
        {
            activeWorkflow = _activeWorkflow
                ?? throw new InvalidOperationException("No active recording session was found.");
        }

        if (activeWorkflow is not IRecordingWorkflowDeferredStop deferredStop)
        {
            throw new InvalidOperationException("Deferred stop is unavailable for the current recording workflow.");
        }

        try
        {
            return await deferredStop.StopForDeferredTranscriptionAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (_syncRoot)
            {
                _activeWorkflow = null;
                _activeMode = RecordingWorkflowMode.Unknown;
            }
        }
    }

    private void OnTranscriptUpdated(object? sender, TranscriptUpdatedEventArgs e)
    {
        lock (_syncRoot)
        {
            if (!ReferenceEquals(sender, _activeWorkflow))
            {
                return;
            }
        }

        TranscriptUpdated?.Invoke(this, e);
    }

    private void OnRecordingStarted(object? sender, EventArgs e)
    {
        lock (_syncRoot)
        {
            if (!ReferenceEquals(sender, _activeWorkflow))
            {
                return;
            }
        }

        RecordingStarted?.Invoke(this, EventArgs.Empty);
    }

    public RecordingWorkflowMode GetCurrentMode()
    {
        lock (_syncRoot)
        {
            return _activeMode;
        }
    }

    private static RecordingWorkflowMode ResolveWorkflowMode(IRecordingWorkflow workflow)
    {
        return workflow is IRecordingWorkflowModeProvider provider
            ? provider.GetCurrentMode()
            : RecordingWorkflowMode.Unknown;
    }
}
