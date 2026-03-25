namespace Stt.Infrastructure.OpenAi;

public sealed record OpenAiTranscriptionOptions(
    string? ApiKey,
    string TranscriptionModel,
    string? TranscriptionLanguage = null,
    string? TranscriptionPrompt = null);
