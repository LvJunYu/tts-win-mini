namespace Stt.Core.Models;

public sealed class AudioChunkCapturedEventArgs : EventArgs
{
    public AudioChunkCapturedEventArgs(byte[] buffer, int bytesRecorded)
    {
        Buffer = buffer;
        BytesRecorded = bytesRecorded;
    }

    public byte[] Buffer { get; }
    public int BytesRecorded { get; }

    public ReadOnlyMemory<byte> AudioBytes => Buffer.AsMemory(0, BytesRecorded);
}
