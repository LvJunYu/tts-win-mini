using System.Windows.Input;
using Stt.App.Common;
using Stt.App.Configuration;
using Stt.Core.Models;

namespace Stt.App.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private IReadOnlyList<MicrophoneDeviceOption> _availableMicrophones;
    private IReadOnlyList<TranscriptionModelOption> _availableRealtimeTranscriptionModels;
    private IReadOnlyList<TranscriptionModelOption> _availableUploadAfterStopTranscriptionModels;
    private bool _enableStreamingTranscription;
    private bool _launchOnWindowsLogin;
    private string _openAiApiKey;
    private string _realtimeTranscriptionModel;
    private string _selectedMicrophoneDeviceId;
    private bool _showLiveTranscriptWhileStreaming;
    private bool _showTranscriptWindowOnCompletion;
    private string _toggleRecordingHotkey;
    private string _uploadAfterStopTranscriptionModel;

    public SettingsViewModel(
        AppSettings settings,
        string settingsPath,
        IReadOnlyList<MicrophoneDeviceOption> availableMicrophones,
        IReadOnlyList<TranscriptionModelOption> availableUploadAfterStopTranscriptionModels,
        IReadOnlyList<TranscriptionModelOption> availableRealtimeTranscriptionModels)
    {
        _availableMicrophones = availableMicrophones;
        _availableUploadAfterStopTranscriptionModels = availableUploadAfterStopTranscriptionModels;
        _availableRealtimeTranscriptionModels = availableRealtimeTranscriptionModels;
        _openAiApiKey = settings.OpenAiApiKey;
        _selectedMicrophoneDeviceId = settings.SelectedMicrophoneDeviceId;
        _uploadAfterStopTranscriptionModel = AppDefaults.NormalizeUploadAfterStopTranscriptionModel(
            settings.UploadAfterStopTranscriptionModel);
        _realtimeTranscriptionModel = AppDefaults.NormalizeRealtimeTranscriptionModel(
            settings.RealtimeTranscriptionModel);
        _enableStreamingTranscription = settings.EnableStreamingTranscription;
        _showLiveTranscriptWhileStreaming = settings.ShowLiveTranscriptWhileStreaming;
        _toggleRecordingHotkey = settings.ToggleRecordingHotkey;
        _showTranscriptWindowOnCompletion = settings.ShowTranscriptWindowOnCompletion;
        _launchOnWindowsLogin = settings.LaunchOnWindowsLogin;
        SettingsPath = settingsPath;
        SaveCommand = new RelayCommand(RequestSave);
        CancelCommand = new RelayCommand(RequestClose);
    }

    public event EventHandler<AppSettingsSaveRequestedEventArgs>? SaveRequested;
    public event EventHandler? CloseRequested;

    public string OpenAiApiKey
    {
        get => _openAiApiKey;
        set => SetProperty(ref _openAiApiKey, value);
    }

    public IReadOnlyList<TranscriptionModelOption> AvailableUploadAfterStopTranscriptionModels
    {
        get => _availableUploadAfterStopTranscriptionModels;
        private set => SetProperty(ref _availableUploadAfterStopTranscriptionModels, value);
    }

    public IReadOnlyList<TranscriptionModelOption> AvailableRealtimeTranscriptionModels
    {
        get => _availableRealtimeTranscriptionModels;
        private set => SetProperty(ref _availableRealtimeTranscriptionModels, value);
    }

    public bool LaunchOnWindowsLogin
    {
        get => _launchOnWindowsLogin;
        set => SetProperty(ref _launchOnWindowsLogin, value);
    }

    public bool EnableStreamingTranscription
    {
        get => _enableStreamingTranscription;
        set
        {
            if (!SetProperty(ref _enableStreamingTranscription, value))
            {
                return;
            }

            if (!value)
            {
                ShowLiveTranscriptWhileStreaming = false;
            }
        }
    }

    public bool ShowLiveTranscriptWhileStreaming
    {
        get => _showLiveTranscriptWhileStreaming;
        set => SetProperty(ref _showLiveTranscriptWhileStreaming, value);
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

    public string UploadAfterStopTranscriptionModel
    {
        get => _uploadAfterStopTranscriptionModel;
        set => SetProperty(
            ref _uploadAfterStopTranscriptionModel,
            AppDefaults.NormalizeUploadAfterStopTranscriptionModel(value));
    }

    public string RealtimeTranscriptionModel
    {
        get => _realtimeTranscriptionModel;
        set => SetProperty(
            ref _realtimeTranscriptionModel,
            AppDefaults.NormalizeRealtimeTranscriptionModel(value));
    }

    public string ToggleRecordingHotkey
    {
        get => _toggleRecordingHotkey;
        set => SetProperty(ref _toggleRecordingHotkey, value);
    }

    public bool ShowTranscriptWindowOnCompletion
    {
        get => _showTranscriptWindowOnCompletion;
        set => SetProperty(ref _showTranscriptWindowOnCompletion, value);
    }

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
        UploadAfterStopTranscriptionModel = settings.UploadAfterStopTranscriptionModel;
        RealtimeTranscriptionModel = settings.RealtimeTranscriptionModel;
        EnableStreamingTranscription = settings.EnableStreamingTranscription;
        ShowLiveTranscriptWhileStreaming = settings.ShowLiveTranscriptWhileStreaming && settings.EnableStreamingTranscription;
        ToggleRecordingHotkey = settings.ToggleRecordingHotkey;
        ShowTranscriptWindowOnCompletion = settings.ShowTranscriptWindowOnCompletion;
        LaunchOnWindowsLogin = settings.LaunchOnWindowsLogin;
    }

    private void RequestSave()
    {
        SaveRequested?.Invoke(this, new AppSettingsSaveRequestedEventArgs(new AppSettings(
            OpenAiApiKey: OpenAiApiKey.Trim(),
            SelectedMicrophoneDeviceId: SelectedMicrophoneDeviceId.Trim(),
            UploadAfterStopTranscriptionModel: UploadAfterStopTranscriptionModel,
            RealtimeTranscriptionModel: RealtimeTranscriptionModel,
            EnableStreamingTranscription: EnableStreamingTranscription,
            ShowLiveTranscriptWhileStreaming: EnableStreamingTranscription && ShowLiveTranscriptWhileStreaming,
            ToggleRecordingHotkey: ToggleRecordingHotkey.Trim(),
            ShowTranscriptWindowOnCompletion: ShowTranscriptWindowOnCompletion,
            LaunchOnWindowsLogin: LaunchOnWindowsLogin)));
    }

    private void RequestClose()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
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
