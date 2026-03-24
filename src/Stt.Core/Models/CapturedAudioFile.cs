namespace Stt.Core.Models;

public sealed class CapturedAudioFile : IDisposable
{
    public CapturedAudioFile(
        string filePath,
        string fileName,
        string contentType)
    {
        FilePath = filePath;
        FileName = fileName;
        ContentType = contentType;
    }

    public string FilePath { get; }
    public string FileName { get; }
    public string ContentType { get; }

    public void Dispose()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                File.Delete(FilePath);
            }
        }
        catch
        {
            // Best effort cleanup for temp audio files.
        }
    }
}

