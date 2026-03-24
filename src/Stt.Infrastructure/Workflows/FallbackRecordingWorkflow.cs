using Stt.Core.Abstractions;
using Stt.Core.Diagnostics;
using Stt.Core.Models;

namespace Stt.Infrastructure.Workflows;

public sealed class FallbackRecordingWorkflow : IRecordingWorkflow, IRecordingWorkflowModeProvider
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
    }

    public event EventHandler<TranscriptUpdatedEventArgs>? TranscriptUpdated;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _primaryWorkflow.StartAsync(cancellationToken).ConfigureAwait(false);

            lock (_syncRoot)
            {
                _activeWorkflow = _primaryWorkflow;
                _activeMode = ResolveWorkflowMode(_primaryWorkflow);
            }

            JotMicTrace.Log("FallbackWorkflow", $"Primary workflow active: {_activeMode}.");

            return;
        }
        catch (Exception primaryException)
        {
            JotMicTrace.Log(
                "FallbackWorkflow",
                $"Primary workflow failed to start. Falling back. Reason={primaryException.Message}");

            try
            {
                await _fallbackWorkflow.StartAsync(cancellationToken).ConfigureAwait(false);

                lock (_syncRoot)
                {
                    _activeWorkflow = _fallbackWorkflow;
                    _activeMode = RecordingWorkflowMode.UploadAfterStopFallback;
                }

                JotMicTrace.Log("FallbackWorkflow", "Fallback workflow active: UploadAfterStopFallback.");
            }
            catch
            {
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
