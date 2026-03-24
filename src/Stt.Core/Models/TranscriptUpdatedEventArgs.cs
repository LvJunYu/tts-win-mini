namespace Stt.Core.Models;

public sealed class TranscriptUpdatedEventArgs : EventArgs
{
    public TranscriptUpdatedEventArgs(string text)
    {
        Text = text;
    }

    public string Text { get; }
}
