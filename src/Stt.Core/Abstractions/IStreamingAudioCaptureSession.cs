using Stt.Core.Models;

namespace Stt.Core.Abstractions;

public interface IStreamingAudioCaptureSession
{
    event EventHandler<AudioChunkCapturedEventArgs>? AudioChunkCaptured;

    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}
