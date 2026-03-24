using System.Net.Http;
using System.Windows;
using Stt.App.Configuration;
using Stt.App.Controllers;
using Stt.App.Services;
using Stt.App.ViewModels;
using Stt.App.Windows;
using Stt.Core.Models;
using Stt.Infrastructure.Audio;
using Stt.Infrastructure.OpenAi;
using Stt.Infrastructure.Workflows;

namespace Stt.App;

public partial class App : System.Windows.Application
{
    private AppSettings _currentSettings = new(
        OpenAiApiKey: string.Empty,
        SelectedMicrophoneDeviceId: string.Empty,
        UploadAfterStopTranscriptionModel: AppDefaults.UploadAfterStopTranscriptionModel,
        RealtimeTranscriptionModel: AppDefaults.RealtimeTranscriptionModel,
        EnableStreamingTranscription: false,
        ShowLiveTranscriptWhileStreaming: false,
        ToggleRecordingHotkey: "Ctrl+Alt+Space",
        ShowTranscriptWindowOnCompletion: false,
        LaunchOnWindowsLogin: true);
    private AppController? _controller;
    private GlobalHotkeyService? _globalHotkeyService;
    private HttpClient? _httpClient;
    private WindowsLaunchOnLoginService? _launchOnLoginService;
    private NaudioMicrophoneCaptureSession? _microphoneCaptureSession;
    private NaudioStreamingMicrophoneCaptureSession? _streamingMicrophoneCaptureSession;
    private SingleInstanceGuard? _singleInstanceGuard;
    private string? _settingsPath;
    private SettingsViewModel? _settingsViewModel;
    private SettingsWindow? _settingsWindow;
    private TrayIconHost? _trayIconHost;
    private TranscriptPopupViewModel? _viewModel;
    private TranscriptWindow? _transcriptWindow;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        _singleInstanceGuard = new SingleInstanceGuard(AppIdentity.SingleInstanceMutexName);
        if (!_singleInstanceGuard.TryAcquire())
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);

        var loadedSettings = AppSettingsLoader.Load();
        _currentSettings = loadedSettings.Settings;
        _settingsPath = loadedSettings.PreferredSettingsPath;
        var initialSnapshot = AppSnapshot.Idle;
        _launchOnLoginService = new WindowsLaunchOnLoginService();

        _httpClient = CreateHttpClient();
        _controller = CreateAppController();

        var hotkeySetup = ApplyHotkeySetting(_currentSettings.ToggleRecordingHotkey);
        var launchOnLoginError = TryApplyLaunchOnLoginSetting(_currentSettings);
        initialSnapshot = BuildInitialSnapshot(
            loadedSettings,
            hotkeySetup.ErrorMessage,
            hotkeySetup.DisplayText,
            launchOnLoginError);

        var settingsViewModel = new SettingsViewModel(
            _currentSettings,
            loadedSettings.PreferredSettingsPath,
            MicrophoneDeviceCatalog.GetAvailableDevices(),
            AppDefaults.UploadAfterStopTranscriptionModelOptions,
            AppDefaults.RealtimeTranscriptionModelOptions);
        InitializeWindows(settingsViewModel);

        _controller!.SnapshotChanged += OnSnapshotChanged;
        settingsViewModel.SaveRequested += OnSettingsSaveRequested;

        _viewModel!.ApplySnapshot(initialSnapshot);

        _trayIconHost = CreateTrayIconHost(hotkeySetup);

        _trayIconHost.ApplySnapshot(initialSnapshot);
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        if (_transcriptWindow is not null)
        {
            _transcriptWindow.AllowClose = true;
            _transcriptWindow.Close();
        }

        if (_settingsWindow is not null)
        {
            _settingsWindow.AllowClose = true;
            _settingsWindow.Close();
        }

        _trayIconHost?.Dispose();
        _globalHotkeyService?.Dispose();
        _microphoneCaptureSession?.Dispose();
        _streamingMicrophoneCaptureSession?.Dispose();
        _httpClient?.Dispose();
        _singleInstanceGuard?.Dispose();

        base.OnExit(e);
    }

    private void OnSnapshotChanged(object? sender, AppSnapshot snapshot)
    {
        Dispatcher.Invoke(() =>
        {
            _viewModel?.ApplySnapshot(snapshot);
            _trayIconHost?.ApplySnapshot(snapshot);

            if (snapshot.State == AppSessionState.Error)
            {
                ShowTranscriptWindow();
                return;
            }

            if (_currentSettings.EnableStreamingTranscription
                && _currentSettings.ShowLiveTranscriptWhileStreaming
                && snapshot.State == AppSessionState.Recording
                && _controller?.CurrentWorkflowMode == RecordingWorkflowMode.RealtimeStreaming)
            {
                ShowTranscriptWindow(activate: false);
                return;
            }

            if (snapshot.State == AppSessionState.Ready && _currentSettings.ShowTranscriptWindowOnCompletion)
            {
                ShowTranscriptWindow();
            }
        });
    }

    private void ShowTranscriptWindow(bool activate = true)
    {
        ShowManagedWindow(_transcriptWindow, activate);
    }

    private void ShowSettingsWindow()
    {
        if (_settingsWindow is null || _settingsViewModel is null)
        {
            return;
        }

        _settingsViewModel.SetAvailableMicrophones(MicrophoneDeviceCatalog.GetAvailableDevices());
        _settingsViewModel.ApplySettings(_currentSettings);
        ShowManagedWindow(_settingsWindow);
    }

    private void OnSettingsSaveRequested(object? sender, AppSettingsSaveRequestedEventArgs e)
    {
        if (_settingsPath is null)
        {
            System.Windows.MessageBox.Show(
                "Settings path is unavailable.",
                $"{AppIdentity.DisplayName} Settings",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        var savedSettings = NormalizeSettings(e.Settings);
        var hotkeySetup = ApplyHotkeySetting(savedSettings.ToggleRecordingHotkey);

        if (!string.IsNullOrWhiteSpace(hotkeySetup.ErrorMessage))
        {
            System.Windows.MessageBox.Show(
                hotkeySetup.ErrorMessage,
                $"{AppIdentity.DisplayName} Settings",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        _currentSettings = savedSettings;
        _trayIconHost?.UpdateHotkeyDisplay(hotkeySetup.MenuLabel, hotkeySetup.RecordingStopHint);

        try
        {
            AppSettingsLoader.Save(_currentSettings, _settingsPath);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                ex.Message,
                $"{AppIdentity.DisplayName} Settings",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        var launchOnLoginError = TryApplyLaunchOnLoginSetting(_currentSettings);
        _settingsWindow?.Hide();

        if (!string.IsNullOrWhiteSpace(launchOnLoginError))
        {
            System.Windows.MessageBox.Show(
                $"Settings saved, but Windows login startup could not be updated.\n\n{launchOnLoginError}",
                $"{AppIdentity.DisplayName} Settings",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        System.Windows.MessageBox.Show(
            "Settings saved.",
            $"{AppIdentity.DisplayName} Settings",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private HotkeySetupResult ApplyHotkeySetting(string configuredHotkey)
    {
        configuredHotkey = configuredHotkey.Trim();

        if (string.IsNullOrWhiteSpace(configuredHotkey))
        {
            _globalHotkeyService?.Dispose();
            _globalHotkeyService = null;

            return new HotkeySetupResult(
                DisplayText: "disabled",
                MenuLabel: "disabled",
                RecordingStopHint: "Left-click the tray icon again to stop.",
                ErrorMessage: null);
        }

        if (!HotkeyParser.TryParse(configuredHotkey, out var gesture, out var parseError))
        {
            return new HotkeySetupResult(
                DisplayText: configuredHotkey,
                MenuLabel: $"invalid ({configuredHotkey})",
                RecordingStopHint: "Left-click the tray icon again to stop.",
                ErrorMessage: parseError);
        }

        try
        {
            if (_globalHotkeyService is not null
                && string.Equals(
                    _globalHotkeyService.Gesture.DisplayText,
                    gesture!.DisplayText,
                    StringComparison.OrdinalIgnoreCase))
            {
                return new HotkeySetupResult(
                    DisplayText: gesture.DisplayText,
                    MenuLabel: gesture.DisplayText,
                    RecordingStopHint: $"Press {gesture.DisplayText} again or left-click the tray icon to stop.",
                    ErrorMessage: null);
            }

            var existingHotkeyService = _globalHotkeyService;
            var newHotkeyService = new GlobalHotkeyService(gesture!, _controller!.ToggleRecordingAsync);
            existingHotkeyService?.Dispose();
            _globalHotkeyService = newHotkeyService;

            return new HotkeySetupResult(
                DisplayText: gesture!.DisplayText,
                MenuLabel: gesture.DisplayText,
                RecordingStopHint: $"Press {gesture.DisplayText} again or left-click the tray icon to stop.",
                ErrorMessage: null);
        }
        catch (Exception ex)
        {
            return new HotkeySetupResult(
                DisplayText: gesture!.DisplayText,
                MenuLabel: $"unavailable ({gesture.DisplayText})",
                RecordingStopHint: "Left-click the tray icon again to stop.",
                ErrorMessage: ex.Message);
        }
    }

    private static AppSnapshot BuildInitialSnapshot(
        LoadedAppSettings loadedSettings,
        string? hotkeyErrorMessage,
        string hotkeyDisplayText,
        string? launchOnLoginError)
    {
        if (!string.IsNullOrWhiteSpace(loadedSettings.LoadErrorMessage))
        {
            return new AppSnapshot(
                AppSessionState.Error,
                loadedSettings.LoadErrorMessage,
                string.Empty);
        }

        if (string.IsNullOrWhiteSpace(loadedSettings.Settings.OpenAiApiKey))
        {
            return new AppSnapshot(
                AppSessionState.Idle,
                AppendStartupWarning(
                    "Open Settings from the tray menu and add your OpenAI API key.",
                    launchOnLoginError),
                string.Empty);
        }

        if (!string.IsNullOrWhiteSpace(hotkeyErrorMessage))
        {
            return new AppSnapshot(
                AppSessionState.Idle,
                AppendStartupWarning(
                    $"Ready from tray. Hotkey unavailable: {hotkeyErrorMessage}",
                    launchOnLoginError),
                string.Empty);
        }

        return new AppSnapshot(
            AppSessionState.Idle,
            AppendStartupWarning(
                hotkeyDisplayText == "disabled"
                    ? "Ready to record. Hotkey is disabled."
                    : $"Ready to record. Hotkey: {hotkeyDisplayText}.",
                launchOnLoginError),
            string.Empty);
    }

    private HttpClient CreateHttpClient()
    {
        return new HttpClient
        {
            BaseAddress = new Uri("https://api.openai.com/"),
            Timeout = TimeSpan.FromSeconds(90)
        };
    }

    private AppController CreateAppController()
    {
        _microphoneCaptureSession = new NaudioMicrophoneCaptureSession(CreateSelectedMicrophoneDeviceId);
        _streamingMicrophoneCaptureSession = new NaudioStreamingMicrophoneCaptureSession(CreateSelectedMicrophoneDeviceId);

        var transcriptionClient = new OpenAiTranscriptionClient(_httpClient!, CreateBatchTranscriptionOptions);
        var realtimeTranscriptionClient = new OpenAiRealtimeTranscriptionClient(_httpClient!, CreateRealtimeTranscriptionOptions);
        var fallbackRecordingWorkflow = new OpenAiRecordingWorkflow(_microphoneCaptureSession, transcriptionClient);
        var realtimeRecordingWorkflow = new OpenAiRealtimeRecordingWorkflow(
            _streamingMicrophoneCaptureSession,
            realtimeTranscriptionClient);
        var streamingRecordingWorkflow = new FallbackRecordingWorkflow(
            realtimeRecordingWorkflow,
            fallbackRecordingWorkflow);
        var recordingWorkflow = new SelectableRecordingWorkflow(
            () => _currentSettings.EnableStreamingTranscription,
            streamingRecordingWorkflow,
            fallbackRecordingWorkflow);

        return new AppController(
            recordingWorkflow,
            new WpfClipboardService(),
            () => _currentSettings.EnableStreamingTranscription);
    }

    private void InitializeWindows(SettingsViewModel settingsViewModel)
    {
        _viewModel = new TranscriptPopupViewModel();
        _transcriptWindow = new TranscriptWindow
        {
            DataContext = _viewModel
        };

        _settingsViewModel = settingsViewModel;
        _settingsWindow = new SettingsWindow
        {
            DataContext = _settingsViewModel
        };
    }

    private TrayIconHost CreateTrayIconHost(HotkeySetupResult hotkeySetup)
    {
        return new TrayIconHost(
            toggleRecordingAsync: _controller!.ToggleRecordingAsync,
            showTranscript: () => ShowTranscriptWindow(),
            showSettings: ShowSettingsWindow,
            exitApplication: Shutdown,
            hotkeyLabel: hotkeySetup.MenuLabel,
            recordingStopHint: hotkeySetup.RecordingStopHint);
    }

    private OpenAiTranscriptionOptions CreateBatchTranscriptionOptions()
    {
        return new OpenAiTranscriptionOptions(
            ApiKey: string.IsNullOrWhiteSpace(_currentSettings.OpenAiApiKey)
                ? null
                : _currentSettings.OpenAiApiKey,
            TranscriptionModel: _currentSettings.UploadAfterStopTranscriptionModel);
    }

    private OpenAiTranscriptionOptions CreateRealtimeTranscriptionOptions()
    {
        return new OpenAiTranscriptionOptions(
            ApiKey: string.IsNullOrWhiteSpace(_currentSettings.OpenAiApiKey)
                ? null
                : _currentSettings.OpenAiApiKey,
            TranscriptionModel: _currentSettings.RealtimeTranscriptionModel);
    }

    private static AppSettings NormalizeSettings(AppSettings settings)
    {
        return new AppSettings(
            OpenAiApiKey: settings.OpenAiApiKey.Trim(),
            SelectedMicrophoneDeviceId: settings.SelectedMicrophoneDeviceId.Trim(),
            UploadAfterStopTranscriptionModel: AppDefaults.NormalizeUploadAfterStopTranscriptionModel(
                settings.UploadAfterStopTranscriptionModel),
            RealtimeTranscriptionModel: AppDefaults.NormalizeRealtimeTranscriptionModel(
                settings.RealtimeTranscriptionModel),
            EnableStreamingTranscription: settings.EnableStreamingTranscription,
            ShowLiveTranscriptWhileStreaming:
                settings.EnableStreamingTranscription && settings.ShowLiveTranscriptWhileStreaming,
            ToggleRecordingHotkey: settings.ToggleRecordingHotkey.Trim(),
            ShowTranscriptWindowOnCompletion: settings.ShowTranscriptWindowOnCompletion,
            LaunchOnWindowsLogin: settings.LaunchOnWindowsLogin);
    }

    private string? CreateSelectedMicrophoneDeviceId()
    {
        return string.IsNullOrWhiteSpace(_currentSettings.SelectedMicrophoneDeviceId)
            ? null
            : _currentSettings.SelectedMicrophoneDeviceId;
    }

    private string? TryApplyLaunchOnLoginSetting(AppSettings settings)
    {
        if (_launchOnLoginService is null)
        {
            return "Windows startup service is unavailable.";
        }

        try
        {
            _launchOnLoginService.ApplySetting(settings.LaunchOnWindowsLogin);
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private static string AppendStartupWarning(string message, string? launchOnLoginError)
    {
        return string.IsNullOrWhiteSpace(launchOnLoginError)
            ? message
            : $"{message} Windows login startup could not be updated: {launchOnLoginError}";
    }

    private static void ShowManagedWindow(Window? window, bool activate = true)
    {
        if (window is null)
        {
            return;
        }

        if (!window.IsVisible)
        {
            window.ShowActivated = activate;
            window.Show();
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        BringWindowForward(window);

        if (activate)
        {
            window.Activate();
            window.Focus();
        }
    }

    private static void BringWindowForward(Window window)
    {
        window.Topmost = true;
        window.Topmost = false;
    }

    private sealed record HotkeySetupResult(
        string DisplayText,
        string MenuLabel,
        string RecordingStopHint,
        string? ErrorMessage);
}
