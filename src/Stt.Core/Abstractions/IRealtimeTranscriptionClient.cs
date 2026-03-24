namespace Stt.Core.Abstractions;

public interface IRealtimeTranscriptionClient
{
    void ValidateConfiguration();
    Task<IRealtimeTranscriptionSession> CreateSessionAsync(CancellationToken cancellationToken);
}
