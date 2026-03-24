namespace Stt.App;

public static class AppDefaults
{
    public const string UploadAfterStopTranscriptionModel = "gpt-4o-mini-transcribe";
    public const string RealtimeTranscriptionModel = "gpt-4o-transcribe";

    public static IReadOnlyList<TranscriptionModelOption> UploadAfterStopTranscriptionModelOptions { get; } =
    [
        new(
            Value: "gpt-4o-mini-transcribe",
            DisplayName: "gpt-4o-mini-transcribe"),
        new(
            Value: "gpt-4o-transcribe",
            DisplayName: "gpt-4o-transcribe")
    ];

    public static IReadOnlyList<TranscriptionModelOption> RealtimeTranscriptionModelOptions { get; } =
    [
        new(
            Value: "gpt-4o-transcribe",
            DisplayName: "gpt-4o-transcribe"),
        new(
            Value: "gpt-4o-mini-transcribe",
            DisplayName: "gpt-4o-mini-transcribe")
    ];

    public static string NormalizeUploadAfterStopTranscriptionModel(string? value)
    {
        return NormalizeTranscriptionModel(
            value,
            UploadAfterStopTranscriptionModelOptions,
            UploadAfterStopTranscriptionModel);
    }

    public static string NormalizeRealtimeTranscriptionModel(string? value)
    {
        return NormalizeTranscriptionModel(
            value,
            RealtimeTranscriptionModelOptions,
            RealtimeTranscriptionModel);
    }

    private static string NormalizeTranscriptionModel(
        string? value,
        IReadOnlyList<TranscriptionModelOption> options,
        string defaultValue)
    {
        if (!string.IsNullOrWhiteSpace(value)
            && options.Any(option => string.Equals(option.Value, value, StringComparison.OrdinalIgnoreCase)))
        {
            return value;
        }

        return defaultValue;
    }
}

public sealed record TranscriptionModelOption(
    string Value,
    string DisplayName);
