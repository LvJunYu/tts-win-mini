namespace Stt.Core.Models;

public sealed record TranscriptResult(
    string Text,
    DateTimeOffset ReceivedAtUtc);

