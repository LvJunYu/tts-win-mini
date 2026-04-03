using System.Windows.Input;
using Stt.App;
using Stt.App.Common;
using Stt.App.Configuration;
using Stt.Core.Models;

namespace Stt.App.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private IReadOnlyList<MicrophoneDeviceOption> _availableMicrophones;
    private IReadOnlyList<SettingOption<RealtimeVadEagerness>> _availableRealtimeVadEagernessOptions;
    private IReadOnlyList<SettingOption<RealtimeVadMode>> _availableRealtimeVadModes;
    private bool _autoPasteAfterCopy;
    private bool _enableStreamingTranscription;
    private bool _launchOnWindowsLogin;
    private string _maxStreamingLengthMinutesText;
    private string _openAiApiKey;
    private string _realtimeSilenceDurationMsText;
    private RealtimeVadEagerness _selectedRealtimeVadEagerness;
    private string _selectedMicrophoneDeviceId;
    private RealtimeVadMode _selectedRealtimeVadMode;
    private bool _showTranscriptWindowWhenSpeaking;
    private string _toggleRecordingHotkey;

    public SettingsViewModel(
        AppSettings settings,
        string settingsPath,
        IReadOnlyList<MicrophoneDeviceOption> availableMicrophones)
    {
        _availableMicrophones = availableMicrophones;
        _availableRealtimeVadModes = CreateRealtimeVadModeOptions();
        _availableRealtimeVadEagernessOptions = CreateRealtimeVadEagernessOptions();
        _openAiApiKey = settings.OpenAiApiKey;
        _selectedMicrophoneDeviceId = settings.SelectedMicrophoneDeviceId;
        _enableStreamingTranscription = settings.EnableStreamingTranscription;
        _maxStreamingLengthMinutesText = settings.MaxStreamingLengthMinutes.ToString();
        _toggleRecordingHotkey = settings.ToggleRecordingHotkey;
        _showTranscriptWindowWhenSpeaking = settings.ShowTranscriptWindowWhenSpeaking;
        _autoPasteAfterCopy = settings.AutoPasteAfterCopy;
        _launchOnWindowsLogin = settings.LaunchOnWindowsLogin;
        _selectedRealtimeVadMode = settings.RealtimeVadMode;
        _realtimeSilenceDurationMsText = settings.RealtimeSilenceDurationMs.ToString();
        _selectedRealtimeVadEagerness = settings.RealtimeVadEagerness;
        SettingsPath = settingsPath;
        SaveCommand = new RelayCommand(RequestSave);
        CancelCommand = new RelayCommand(RequestClose);
    }

    public event EventHandler<AppSettingsSaveRequestedEventArgs>? SaveRequested;
    public event EventHandler? CloseRequested;
    public event EventHandler<string>? ValidationFailed;

    public string OpenAiApiKey
    {
        get => _openAiApiKey;
        set => SetProperty(ref _openAiApiKey, value);
    }

    public bool LaunchOnWindowsLogin
    {
        get => _launchOnWindowsLogin;
        set => SetProperty(ref _launchOnWindowsLogin, value);
    }

    public bool EnableStreamingTranscription
    {
        get => _enableStreamingTranscription;
        set => SetProperty(ref _enableStreamingTranscription, value);
    }

    public string MaxStreamingLengthMinutesText
    {
        get => _maxStreamingLengthMinutesText;
        set => SetProperty(ref _maxStreamingLengthMinutesText, value);
    }

    public IReadOnlyList<MicrophoneDeviceOption> AvailableMicrophones
    {
        get => _availableMicrophones;
        private set => SetProperty(ref _availableMicrophones, value);
    }

    public string SelectedMicrophoneDeviceId
    {
        get => _selectedMicrophoneDeviceId;
        set => SetProperty(ref _selectedMicrophoneDeviceId, value);
    }

    public string ToggleRecordingHotkey
    {
        get => _toggleRecordingHotkey;
        set => SetProperty(ref _toggleRecordingHotkey, value);
    }

    public bool ShowTranscriptWindowWhenSpeaking
    {
        get => _showTranscriptWindowWhenSpeaking;
        set => SetProperty(ref _showTranscriptWindowWhenSpeaking, value);
    }

    public bool AutoPasteAfterCopy
    {
        get => _autoPasteAfterCopy;
        set => SetProperty(ref _autoPasteAfterCopy, value);
    }

    public IReadOnlyList<SettingOption<RealtimeVadMode>> AvailableRealtimeVadModes => _availableRealtimeVadModes;

    public RealtimeVadMode SelectedRealtimeVadMode
    {
        get => _selectedRealtimeVadMode;
        set
        {
            if (!SetProperty(ref _selectedRealtimeVadMode, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsServerVadSelected));
            OnPropertyChanged(nameof(IsSemanticVadSelected));
        }
    }

    public string RealtimeSilenceDurationMsText
    {
        get => _realtimeSilenceDurationMsText;
        set => SetProperty(ref _realtimeSilenceDurationMsText, value);
    }

    public IReadOnlyList<SettingOption<RealtimeVadEagerness>> AvailableRealtimeVadEagernessOptions => _availableRealtimeVadEagernessOptions;

    public RealtimeVadEagerness SelectedRealtimeVadEagerness
    {
        get => _selectedRealtimeVadEagerness;
        set => SetProperty(ref _selectedRealtimeVadEagerness, value);
    }

    public bool IsServerVadSelected => SelectedRealtimeVadMode == RealtimeVadMode.ServerVad;

    public bool IsSemanticVadSelected => SelectedRealtimeVadMode == RealtimeVadMode.SemanticVad;

    public string SettingsPath { get; }

    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }

    public void SetAvailableMicrophones(IReadOnlyList<MicrophoneDeviceOption> availableMicrophones)
    {
        AvailableMicrophones = availableMicrophones;

        if (!AvailableMicrophones.Any(option => option.DeviceId == SelectedMicrophoneDeviceId))
        {
            SelectedMicrophoneDeviceId = AvailableMicrophones.FirstOrDefault()?.DeviceId ?? string.Empty;
        }
    }

    public void ApplySettings(AppSettings settings)
    {
        OpenAiApiKey = settings.OpenAiApiKey;
        SelectedMicrophoneDeviceId = AvailableMicrophones.Any(option => option.DeviceId == settings.SelectedMicrophoneDeviceId)
            ? settings.SelectedMicrophoneDeviceId
            : AvailableMicrophones.FirstOrDefault()?.DeviceId ?? string.Empty;
        EnableStreamingTranscription = settings.EnableStreamingTranscription;
        MaxStreamingLengthMinutesText = settings.MaxStreamingLengthMinutes.ToString();
        ToggleRecordingHotkey = settings.ToggleRecordingHotkey;
        ShowTranscriptWindowWhenSpeaking = settings.ShowTranscriptWindowWhenSpeaking;
        AutoPasteAfterCopy = settings.AutoPasteAfterCopy;
        LaunchOnWindowsLogin = settings.LaunchOnWindowsLogin;
        SelectedRealtimeVadMode = settings.RealtimeVadMode;
        RealtimeSilenceDurationMsText = settings.RealtimeSilenceDurationMs.ToString();
        SelectedRealtimeVadEagerness = settings.RealtimeVadEagerness;
    }

    private void RequestSave()
    {
        if (!TryParsePositiveInt(MaxStreamingLengthMinutesText, out var maxStreamingLengthMinutes))
        {
            ValidationFailed?.Invoke(
                this,
                "Max streaming length must be a whole number greater than zero.");
            return;
        }

        var realtimeSilenceDurationMs = AppDefaults.DefaultRealtimeSilenceDurationMs;
        if (IsServerVadSelected
            && !TryParseNonNegativeInt(
                RealtimeSilenceDurationMsText,
                out realtimeSilenceDurationMs))
        {
            ValidationFailed?.Invoke(
                this,
                "Realtime silence duration must be a non-negative whole number.");
            return;
        }

        if (!IsServerVadSelected
            && TryParseNonNegativeInt(RealtimeSilenceDurationMsText, out var parsedRealtimeSilenceDurationMs))
        {
            realtimeSilenceDurationMs = parsedRealtimeSilenceDurationMs;
        }

        SaveRequested?.Invoke(this, new AppSettingsSaveRequestedEventArgs(new AppSettings(
            OpenAiApiKey: OpenAiApiKey.Trim(),
            SelectedMicrophoneDeviceId: SelectedMicrophoneDeviceId.Trim(),
            EnableStreamingTranscription: EnableStreamingTranscription,
            MaxStreamingLengthMinutes: maxStreamingLengthMinutes,
            ToggleRecordingHotkey: ToggleRecordingHotkey.Trim(),
            ShowTranscriptWindowWhenSpeaking: ShowTranscriptWindowWhenSpeaking,
            AutoPasteAfterCopy: AutoPasteAfterCopy,
            LaunchOnWindowsLogin: LaunchOnWindowsLogin,
            RealtimeVadMode: SelectedRealtimeVadMode,
            RealtimeSilenceDurationMs: realtimeSilenceDurationMs,
            RealtimeVadEagerness: SelectedRealtimeVadEagerness)));
    }

    private void RequestClose()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private static bool TryParseNonNegativeInt(string value, out int result)
    {
        return int.TryParse(value.Trim(), out result) && result >= 0;
    }

    private static bool TryParsePositiveInt(string value, out int result)
    {
        return int.TryParse(value.Trim(), out result) && result > 0;
    }

    private static IReadOnlyList<SettingOption<RealtimeVadMode>> CreateRealtimeVadModeOptions()
    {
        return
        [
            new SettingOption<RealtimeVadMode>(RealtimeVadMode.ServerVad, "Server VAD - cuts based on silence"),
            new SettingOption<RealtimeVadMode>(RealtimeVadMode.SemanticVad, "Semantic VAD - cuts based on meaning")
        ];
    }

    private static IReadOnlyList<SettingOption<RealtimeVadEagerness>> CreateRealtimeVadEagernessOptions()
    {
        return
        [
            new SettingOption<RealtimeVadEagerness>(RealtimeVadEagerness.Auto, "Auto"),
            new SettingOption<RealtimeVadEagerness>(RealtimeVadEagerness.Low, "Low"),
            new SettingOption<RealtimeVadEagerness>(RealtimeVadEagerness.Medium, "Medium"),
            new SettingOption<RealtimeVadEagerness>(RealtimeVadEagerness.High, "High")
        ];
    }
}

public sealed class AppSettingsSaveRequestedEventArgs : EventArgs
{
    public AppSettingsSaveRequestedEventArgs(AppSettings settings)
    {
        Settings = settings;
    }

    public AppSettings Settings { get; }
}

public sealed record SettingOption<T>(T Value, string DisplayName);
