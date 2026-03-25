using Stt.Core.Abstractions;
using Stt.Core.Diagnostics;
using Stt.Core.Models;

namespace Stt.Infrastructure.Workflows;

public sealed class SelectableRecordingWorkflow : IRecordingWorkflow, IRecordingWorkflowModeProvider, IRecordingWorkflowStartupNotifier
{
    private readonly IRecordingWorkflow _nonStreamingWorkflow;
    private readonly Func<bool> _streamingEnabledAccessor;
    private readonly IRecordingWorkflow _streamingWorkflow;
    private readonly object _syncRoot = new();
    private IRecordingWorkflow? _activeWorkflow;

    public SelectableRecordingWorkflow(
        Func<bool> streamingEnabledAccessor,
        IRecordingWorkflow streamingWorkflow,
        IRecordingWorkflow nonStreamingWorkflow)
    {
        _streamingEnabledAccessor = streamingEnabledAccessor;
        _streamingWorkflow = streamingWorkflow;
        _nonStreamingWorkflow = nonStreamingWorkflow;

        _streamingWorkflow.TranscriptUpdated += OnTranscriptUpdated;
        _nonStreamingWorkflow.TranscriptUpdated += OnTranscriptUpdated;

        if (_streamingWorkflow is IRecordingWorkflowStartupNotifier streamingStartupNotifier)
        {
            streamingStartupNotifier.RecordingStarted += OnRecordingStarted;
        }

        if (_nonStreamingWorkflow is IRecordingWorkflowStartupNotifier nonStreamingStartupNotifier)
        {
            nonStreamingStartupNotifier.RecordingStarted += OnRecordingStarted;
        }
    }

    public event EventHandler<TranscriptUpdatedEventArgs>? TranscriptUpdated;
    public event EventHandler? RecordingStarted;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var streamingEnabled = _streamingEnabledAccessor();
        var selectedWorkflow = streamingEnabled
            ? _streamingWorkflow
            : _nonStreamingWorkflow;

        WhisperTrace.Log(
            "SelectableWorkflow",
            streamingEnabled
                ? "Streaming mode requested for this session."
                : "Upload-after-stop mode requested for this session.");

        lock (_syncRoot)
        {
            _activeWorkflow = selectedWorkflow;
        }

        try
        {
            await selectedWorkflow.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            lock (_syncRoot)
            {
                _activeWorkflow = null;
            }

            throw;
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
            return _activeWorkflow is IRecordingWorkflowModeProvider provider
                ? provider.GetCurrentMode()
                : RecordingWorkflowMode.Unknown;
        }
    }
}
