using System.IO;
using System.Text.Json;
using Stt.App;

namespace Stt.App.Configuration;

public sealed record AppSettings(
    string OpenAiApiKey,
    string SelectedMicrophoneDeviceId,
    string UploadAfterStopTranscriptionModel,
    string RealtimeTranscriptionModel,
    bool EnableStreamingTranscription,
    bool ShowLiveTranscriptWhileStreaming,
    string ToggleRecordingHotkey,
    bool ShowTranscriptWindowOnCompletion,
    bool LaunchOnWindowsLogin);

public sealed record LoadedAppSettings(
    AppSettings Settings,
    string PreferredSettingsPath,
    string? LoadedSettingsPath,
    string? LoadErrorMessage);

public static class AppSettingsLoader
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private static readonly JsonSerializerOptions WriteSerializerOptions = new()
    {
        WriteIndented = true
    };

    public static LoadedAppSettings Load()
    {
        var preferredSettingsPath = ResolvePreferredSettingsPath();
        var legacySettingsPath = ResolveLegacySettingsPath();
        var loadedSettingsPath = FindExistingSettingsPath(preferredSettingsPath, legacySettingsPath);

        SettingsFilePayload? payload = null;
        string? loadErrorMessage = null;

        if (!string.IsNullOrWhiteSpace(loadedSettingsPath))
        {
            try
            {
                var json = File.ReadAllText(loadedSettingsPath);
                payload = JsonSerializer.Deserialize<SettingsFilePayload>(json, SerializerOptions);
            }
            catch (Exception ex)
            {
                loadErrorMessage = $"Couldn't read settings file: {loadedSettingsPath}. {ex.Message}";
            }
        }

        var settings = new AppSettings(
            OpenAiApiKey: FirstNonEmpty(
                payload?.OpenAiApiKey,
                Environment.GetEnvironmentVariable("OPENAI_API_KEY"))
                ?? string.Empty,
            SelectedMicrophoneDeviceId: FirstNonEmpty(
                    payload?.SelectedMicrophoneDeviceId,
                    Environment.GetEnvironmentVariable("JOTMIC_SELECTED_MICROPHONE_DEVICE_ID"),
                    Environment.GetEnvironmentVariable("STT_SELECTED_MICROPHONE_DEVICE_ID"))
                ?? string.Empty,
            UploadAfterStopTranscriptionModel: AppDefaults.NormalizeUploadAfterStopTranscriptionModel(
                FirstNonEmpty(
                    payload?.UploadAfterStopTranscriptionModel,
                    payload?.TranscriptionModel,
                    Environment.GetEnvironmentVariable("JOTMIC_UPLOAD_AFTER_STOP_TRANSCRIPTION_MODEL"),
                    Environment.GetEnvironmentVariable("STT_UPLOAD_AFTER_STOP_TRANSCRIPTION_MODEL"),
                    Environment.GetEnvironmentVariable("JOTMIC_TRANSCRIPTION_MODEL"),
                    Environment.GetEnvironmentVariable("STT_TRANSCRIPTION_MODEL"))),
            RealtimeTranscriptionModel: AppDefaults.NormalizeRealtimeTranscriptionModel(
                FirstNonEmpty(
                    payload?.RealtimeTranscriptionModel,
                    payload?.TranscriptionModel,
                    Environment.GetEnvironmentVariable("JOTMIC_REALTIME_TRANSCRIPTION_MODEL"),
                    Environment.GetEnvironmentVariable("STT_REALTIME_TRANSCRIPTION_MODEL"),
                    Environment.GetEnvironmentVariable("JOTMIC_TRANSCRIPTION_MODEL"),
                    Environment.GetEnvironmentVariable("STT_TRANSCRIPTION_MODEL"))),
            EnableStreamingTranscription: FirstNonNull(
                    payload?.EnableStreamingTranscription,
                    ParseBoolean(Environment.GetEnvironmentVariable("JOTMIC_ENABLE_STREAMING_TRANSCRIPTION")),
                    ParseBoolean(Environment.GetEnvironmentVariable("STT_ENABLE_STREAMING_TRANSCRIPTION")))
                ?? false,
            ShowLiveTranscriptWhileStreaming: FirstNonNull(
                    payload?.ShowLiveTranscriptWhileStreaming,
                    ParseBoolean(Environment.GetEnvironmentVariable("JOTMIC_SHOW_LIVE_TRANSCRIPT_WHILE_STREAMING")),
                    ParseBoolean(Environment.GetEnvironmentVariable("STT_SHOW_LIVE_TRANSCRIPT_WHILE_STREAMING")))
                ?? false,
            ToggleRecordingHotkey: FirstNonEmpty(
                    payload?.ToggleRecordingHotkey,
                    Environment.GetEnvironmentVariable("JOTMIC_TOGGLE_RECORDING_HOTKEY"),
                    Environment.GetEnvironmentVariable("STT_TOGGLE_RECORDING_HOTKEY"))
                ?? "Ctrl+Alt+Space",
            ShowTranscriptWindowOnCompletion: FirstNonNull(
                    payload?.ShowTranscriptWindowOnCompletion,
                    ParseBoolean(Environment.GetEnvironmentVariable("JOTMIC_SHOW_TRANSCRIPT_WINDOW_ON_COMPLETION")),
                    ParseBoolean(Environment.GetEnvironmentVariable("STT_SHOW_TRANSCRIPT_WINDOW_ON_COMPLETION")))
                ?? false,
            LaunchOnWindowsLogin: FirstNonNull(
                    payload?.LaunchOnWindowsLogin,
                    ParseBoolean(Environment.GetEnvironmentVariable("JOTMIC_LAUNCH_ON_WINDOWS_LOGIN")),
                    ParseBoolean(Environment.GetEnvironmentVariable("STT_LAUNCH_ON_WINDOWS_LOGIN")))
                ?? true);

        return new LoadedAppSettings(
            settings,
            preferredSettingsPath,
            loadedSettingsPath,
            loadErrorMessage);
    }

    public static void Save(AppSettings settings, string targetPath)
    {
        var directory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var payload = new SettingsFilePayload(
            settings.OpenAiApiKey,
            settings.SelectedMicrophoneDeviceId,
            settings.UploadAfterStopTranscriptionModel,
            settings.RealtimeTranscriptionModel,
            settings.EnableStreamingTranscription,
            settings.ShowLiveTranscriptWhileStreaming,
            settings.ToggleRecordingHotkey,
            settings.ShowTranscriptWindowOnCompletion,
            settings.LaunchOnWindowsLogin);

        var json = JsonSerializer.Serialize(payload, WriteSerializerOptions);
        File.WriteAllText(targetPath, json);
    }

    private static string ResolvePreferredSettingsPath()
    {
        var appDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppIdentity.SettingsDirectoryName);

        return Path.Combine(appDataDirectory, AppIdentity.SettingsFileName);
    }

    private static string ResolveLegacySettingsPath()
    {
        var appDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppIdentity.LegacySettingsDirectoryName);

        return Path.Combine(appDataDirectory, AppIdentity.LegacySettingsFileName);
    }

    private static string? FindExistingSettingsPath(string preferredSettingsPath, string legacySettingsPath)
    {
        var candidates = new List<string?>
        {
            preferredSettingsPath,
            legacySettingsPath,
            Path.Combine(AppContext.BaseDirectory, AppIdentity.SettingsFileName),
            Path.Combine(AppContext.BaseDirectory, AppIdentity.LegacySettingsFileName),
            TryFindSourceSettingsPath(AppIdentity.SettingsFileName),
            TryFindSourceSettingsPath(AppIdentity.LegacySettingsFileName)
        };

        foreach (var directory in EnumerateAncestorDirectories(Environment.CurrentDirectory))
        {
            candidates.Add(Path.Combine(directory, AppIdentity.SettingsFileName));
            candidates.Add(Path.Combine(directory, AppIdentity.LegacySettingsFileName));
        }

        return candidates
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(path => File.Exists(path!));
    }

    private static string? TryFindSourceSettingsPath(string settingsFileName)
    {
        foreach (var directory in EnumerateAncestorDirectories(AppContext.BaseDirectory))
        {
            if (File.Exists(Path.Combine(directory, "Stt.App.csproj")))
            {
                return Path.Combine(directory, settingsFileName);
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateAncestorDirectories(string startPath)
    {
        var current = new DirectoryInfo(startPath);
        while (current is not null)
        {
            yield return current.FullName;
            current = current.Parent;
        }
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private static bool? FirstNonNull(params bool?[] values)
    {
        return values.FirstOrDefault(value => value.HasValue);
    }

    private static bool? ParseBoolean(string? value)
    {
        if (bool.TryParse(value, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private sealed record SettingsFilePayload(
        string? OpenAiApiKey,
        string? SelectedMicrophoneDeviceId,
        string? UploadAfterStopTranscriptionModel,
        string? RealtimeTranscriptionModel,
        bool? EnableStreamingTranscription,
        bool? ShowLiveTranscriptWhileStreaming,
        string? ToggleRecordingHotkey,
        bool? ShowTranscriptWindowOnCompletion,
        bool? LaunchOnWindowsLogin,
        string? TranscriptionModel = null);
}
