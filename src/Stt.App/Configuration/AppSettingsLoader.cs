using System.IO;
using System.Text.Json;
using Stt.App;
using Stt.Core.Models;

namespace Stt.App.Configuration;

public sealed record AppSettings(
    string OpenAiApiKey,
    string SelectedMicrophoneDeviceId,
    bool EnableStreamingTranscription,
    int MaxStreamingLengthMinutes,
    string ToggleRecordingHotkey,
    bool ShowTranscriptWindowWhenSpeaking,
    bool AutoPasteAfterCopy,
    bool LaunchOnWindowsLogin,
    RealtimeVadMode RealtimeVadMode,
    int RealtimeSilenceDurationMs,
    RealtimeVadEagerness RealtimeVadEagerness);

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
        var loadedSettingsPath = FindExistingSettingsPath(preferredSettingsPath);

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
                    Environment.GetEnvironmentVariable("WHISPER_SELECTED_MICROPHONE_DEVICE_ID"))
                ?? string.Empty,
            EnableStreamingTranscription: FirstNonNull(
                    payload?.EnableStreamingTranscription,
                    ParseBoolean(Environment.GetEnvironmentVariable("WHISPER_ENABLE_STREAMING_TRANSCRIPTION")))
                ?? AppDefaults.DefaultEnableStreamingTranscription,
            MaxStreamingLengthMinutes: FirstNonNull(
                    payload?.MaxStreamingLengthMinutes,
                    ParsePositiveInteger(Environment.GetEnvironmentVariable("WHISPER_MAX_STREAMING_LENGTH_MINUTES")))
                ?? AppDefaults.DefaultMaxStreamingLengthMinutes,
            ToggleRecordingHotkey: FirstNonEmpty(
                    payload?.ToggleRecordingHotkey,
                    Environment.GetEnvironmentVariable("WHISPER_TOGGLE_RECORDING_HOTKEY"))
                ?? "Ctrl+Alt+Space",
            ShowTranscriptWindowWhenSpeaking: FirstNonNull(
                    payload?.ShowTranscriptWindowWhenSpeaking,
                    ParseBoolean(Environment.GetEnvironmentVariable("WHISPER_SHOW_TRANSCRIPT_WINDOW_WHEN_SPEAKING")),
                    payload?.ShowLiveTranscriptWhileStreaming,
                    payload?.ShowTranscriptWindowOnCompletion,
                    ParseBoolean(Environment.GetEnvironmentVariable("WHISPER_SHOW_LIVE_TRANSCRIPT_WHILE_STREAMING")),
                    ParseBoolean(Environment.GetEnvironmentVariable("WHISPER_SHOW_TRANSCRIPT_WINDOW_ON_COMPLETION")))
                ?? false,
            AutoPasteAfterCopy: FirstNonNull(
                    payload?.AutoPasteAfterCopy,
                    ParseBoolean(Environment.GetEnvironmentVariable("WHISPER_AUTO_PASTE_AFTER_COPY")))
                ?? false,
            LaunchOnWindowsLogin: FirstNonNull(
                    payload?.LaunchOnWindowsLogin,
                    ParseBoolean(Environment.GetEnvironmentVariable("WHISPER_LAUNCH_ON_WINDOWS_LOGIN")))
                ?? true,
            RealtimeVadMode: FirstNonNull(
                    ParseRealtimeVadMode(payload?.RealtimeVadMode),
                    ParseRealtimeVadMode(Environment.GetEnvironmentVariable("WHISPER_REALTIME_VAD_MODE")))
                ?? AppDefaults.DefaultRealtimeVadMode,
            RealtimeSilenceDurationMs: FirstNonNull(
                    payload?.RealtimeSilenceDurationMs,
                    ParseInteger(Environment.GetEnvironmentVariable("WHISPER_REALTIME_SILENCE_DURATION_MS")))
                ?? AppDefaults.DefaultRealtimeSilenceDurationMs,
            RealtimeVadEagerness: FirstNonNull(
                    ParseRealtimeVadEagerness(payload?.RealtimeVadEagerness),
                    ParseRealtimeVadEagerness(Environment.GetEnvironmentVariable("WHISPER_REALTIME_VAD_EAGERNESS")))
                ?? AppDefaults.DefaultRealtimeVadEagerness);

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

        var payload = new SettingsFilePayload
        {
            OpenAiApiKey = settings.OpenAiApiKey,
            SelectedMicrophoneDeviceId = settings.SelectedMicrophoneDeviceId,
            EnableStreamingTranscription = settings.EnableStreamingTranscription,
            MaxStreamingLengthMinutes = settings.MaxStreamingLengthMinutes,
            ToggleRecordingHotkey = settings.ToggleRecordingHotkey,
            ShowTranscriptWindowWhenSpeaking = settings.ShowTranscriptWindowWhenSpeaking,
            AutoPasteAfterCopy = settings.AutoPasteAfterCopy,
            LaunchOnWindowsLogin = settings.LaunchOnWindowsLogin,
            RealtimeVadMode = FormatRealtimeVadMode(settings.RealtimeVadMode),
            RealtimeSilenceDurationMs = settings.RealtimeSilenceDurationMs,
            RealtimeVadEagerness = FormatRealtimeVadEagerness(settings.RealtimeVadEagerness)
        };

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

    private static string? FindExistingSettingsPath(string preferredSettingsPath)
    {
        var candidates = new List<string?>
        {
            preferredSettingsPath,
            Path.Combine(AppContext.BaseDirectory, AppIdentity.SettingsFileName),
            TryFindSourceSettingsPath(AppIdentity.SettingsFileName)
        };

        foreach (var directory in EnumerateAncestorDirectories(Environment.CurrentDirectory))
        {
            candidates.Add(Path.Combine(directory, AppIdentity.SettingsFileName));
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

    private static T? FirstNonNull<T>(params T?[] values)
        where T : struct
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

    private static int? ParseInteger(string? value)
    {
        if (int.TryParse(value, out var parsed) && parsed >= 0)
        {
            return parsed;
        }

        return null;
    }

    private static int? ParsePositiveInteger(string? value)
    {
        if (int.TryParse(value, out var parsed) && parsed > 0)
        {
            return parsed;
        }

        return null;
    }

    private static RealtimeVadMode? ParseRealtimeVadMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "server_vad" or "server" => RealtimeVadMode.ServerVad,
            "semantic_vad" or "semantic" => RealtimeVadMode.SemanticVad,
            _ => null
        };
    }

    private static RealtimeVadEagerness? ParseRealtimeVadEagerness(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "auto" => RealtimeVadEagerness.Auto,
            "low" => RealtimeVadEagerness.Low,
            "medium" => RealtimeVadEagerness.Medium,
            "high" => RealtimeVadEagerness.High,
            _ => null
        };
    }

    private static string FormatRealtimeVadMode(RealtimeVadMode mode)
    {
        return mode switch
        {
            RealtimeVadMode.SemanticVad => "semantic_vad",
            _ => "server_vad"
        };
    }

    private static string FormatRealtimeVadEagerness(RealtimeVadEagerness eagerness)
    {
        return eagerness switch
        {
            RealtimeVadEagerness.Low => "low",
            RealtimeVadEagerness.Medium => "medium",
            RealtimeVadEagerness.High => "high",
            _ => "auto"
        };
    }

    private sealed class SettingsFilePayload
    {
        public string? OpenAiApiKey { get; init; }
        public string? SelectedMicrophoneDeviceId { get; init; }
        public bool? EnableStreamingTranscription { get; init; }
        public int? MaxStreamingLengthMinutes { get; init; }
        public string? ToggleRecordingHotkey { get; init; }
        public bool? ShowTranscriptWindowWhenSpeaking { get; init; }
        public bool? AutoPasteAfterCopy { get; init; }
        public bool? LaunchOnWindowsLogin { get; init; }
        public string? RealtimeVadMode { get; init; }
        public int? RealtimeSilenceDurationMs { get; init; }
        public string? RealtimeVadEagerness { get; init; }

        // Legacy settings kept for migration from older versions.
        public bool? ShowLiveTranscriptWhileStreaming { get; init; }
        public bool? ShowTranscriptWindowOnCompletion { get; init; }
        public int? RealtimePrefixPaddingMs { get; init; }
    }
}
