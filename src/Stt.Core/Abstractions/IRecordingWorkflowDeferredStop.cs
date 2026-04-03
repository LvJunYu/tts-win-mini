using Stt.Core.Models;

namespace Stt.Core.Abstractions;

public interface IRecordingWorkflowDeferredStop
{
    Task<PendingTranscription> StopForDeferredTranscriptionAsync(CancellationToken cancellationToken);
}
