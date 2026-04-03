using Stt.Core.Models;

namespace Stt.App;

public static class AppDefaults
{
    public const bool DefaultEnableStreamingTranscription = false;
    public const string TranscriptionModel = "gpt-4o-mini-transcribe";
    public const int LongNonStreamingConfirmationThresholdMinutes = 10;
    public const int DefaultMaxStreamingLengthMinutes = 15;
    public const RealtimeVadMode DefaultRealtimeVadMode = RealtimeVadMode.SemanticVad;
    public const int DefaultRealtimeSilenceDurationMs = 900;
    public const int DefaultRealtimePrefixPaddingMs = 300;
    public const RealtimeVadEagerness DefaultRealtimeVadEagerness = RealtimeVadEagerness.Low;
}
