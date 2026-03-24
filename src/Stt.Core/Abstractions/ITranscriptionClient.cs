using Stt.Core.Models;

namespace Stt.Core.Abstractions;

public interface ITranscriptionClient
{
    void ValidateConfiguration();
    Task<TranscriptResult> TranscribeAsync(CapturedAudioFile audioFile, CancellationToken cancellationToken);
}

