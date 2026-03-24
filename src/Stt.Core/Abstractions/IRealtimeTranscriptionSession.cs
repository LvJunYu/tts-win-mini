using Stt.Core.Models;

namespace Stt.Core.Abstractions;

public interface IRealtimeTranscriptionSession : IAsyncDisposable
{
    event EventHandler<TranscriptUpdatedEventArgs>? TranscriptUpdated;

    Task AppendAudioAsync(ReadOnlyMemory<byte> audioBytes, CancellationToken cancellationToken);
    Task<TranscriptResult> CompleteAsync(CancellationToken cancellationToken);
}
