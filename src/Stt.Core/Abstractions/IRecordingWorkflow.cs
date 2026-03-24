using Stt.Core.Models;

namespace Stt.Core.Abstractions;

public interface IRecordingWorkflow
{
    event EventHandler<TranscriptUpdatedEventArgs>? TranscriptUpdated;

    Task StartAsync(CancellationToken cancellationToken);
    Task<TranscriptResult> StopAndTranscribeAsync(CancellationToken cancellationToken);
}
