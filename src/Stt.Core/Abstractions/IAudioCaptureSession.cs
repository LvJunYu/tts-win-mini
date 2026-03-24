using Stt.Core.Models;

namespace Stt.Core.Abstractions;

public interface IAudioCaptureSession
{
    Task StartAsync(CancellationToken cancellationToken);
    Task<CapturedAudioFile> StopAsync(CancellationToken cancellationToken);
}

