namespace Stt.Core.Models;

public sealed record AppSnapshot(
    AppSessionState State,
    string StatusMessage,
    string TranscriptText)
{
    public static AppSnapshot Idle { get; } = new(
        AppSessionState.Idle,
        "Ready to record.",
        string.Empty);
}

