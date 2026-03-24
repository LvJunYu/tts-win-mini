using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Stt.App;
using Stt.Core.Models;

namespace Stt.App.Services;

public sealed class TrayIconHost : IDisposable
{
    private readonly Dictionary<AppSessionState, Icon> _icons;
    private readonly NotifyIcon _notifyIcon;
    private string _recordingStopHint;
    private readonly ToolStripMenuItem _hotkeyMenuItem;
    private readonly ToolStripMenuItem _settingsMenuItem;
    private readonly ToolStripMenuItem _statusMenuItem;
    private readonly ToolStripMenuItem _showTranscriptMenuItem;
    private readonly ToolStripMenuItem _exitMenuItem;
    private readonly Func<Task> _toggleRecordingAsync;
    private AppSessionState? _lastState;

    public TrayIconHost(
        Func<Task> toggleRecordingAsync,
        Action showTranscript,
        Action showSettings,
        Action exitApplication,
        string hotkeyLabel,
        string recordingStopHint)
    {
        _toggleRecordingAsync = toggleRecordingAsync;
        _recordingStopHint = recordingStopHint;
        _icons = Enum
            .GetValues<AppSessionState>()
            .ToDictionary(state => state, TrayIconArtwork.Create);

        _statusMenuItem = new ToolStripMenuItem("Status: Ready to record")
        {
            Enabled = false
        };

        _hotkeyMenuItem = new ToolStripMenuItem($"Hotkey: {hotkeyLabel}")
        {
            Enabled = false
        };

        _settingsMenuItem = new ToolStripMenuItem("Settings");
        _settingsMenuItem.Click += (_, _) => showSettings();

        _showTranscriptMenuItem = new ToolStripMenuItem("Show Last Transcript");
        _showTranscriptMenuItem.Click += (_, _) => showTranscript();

        _exitMenuItem = new ToolStripMenuItem("Exit");
        _exitMenuItem.Click += (_, _) => exitApplication();

        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusMenuItem);
        menu.Items.Add(_hotkeyMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_settingsMenuItem);
        menu.Items.Add(_showTranscriptMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_exitMenuItem);

        _notifyIcon = new NotifyIcon
        {
            ContextMenuStrip = menu,
            Icon = _icons[AppSessionState.Idle],
            Text = $"{AppIdentity.DisplayName} - Ready",
            Visible = true
        };

        _notifyIcon.MouseClick += OnMouseClick;
    }

    public void ApplySnapshot(AppSnapshot snapshot)
    {
        _notifyIcon.Icon = _icons[snapshot.State];
        _notifyIcon.Text = BuildToolTip(snapshot);
        _statusMenuItem.Text = $"Status: {BuildStatusLabel(snapshot)}";
        _showTranscriptMenuItem.Enabled = snapshot.State is not AppSessionState.Starting and not AppSessionState.Recording;

        if (_lastState.HasValue && _lastState.Value != snapshot.State)
        {
            ShowStateHint(snapshot);
        }

        _lastState = snapshot.State;
    }

    public void Dispose()
    {
        _notifyIcon.MouseClick -= OnMouseClick;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _hotkeyMenuItem.Dispose();
        _settingsMenuItem.Dispose();
        _statusMenuItem.Dispose();
        _showTranscriptMenuItem.Dispose();
        _exitMenuItem.Dispose();

        foreach (var icon in _icons.Values)
        {
            icon.Dispose();
        }
    }

    private async void OnMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        try
        {
            await _toggleRecordingAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    private static string BuildToolTip(AppSnapshot snapshot)
    {
        var text = snapshot.State switch
        {
            AppSessionState.Idle => $"{AppIdentity.DisplayName} - Ready to record",
            AppSessionState.Starting => $"{AppIdentity.DisplayName} - Starting recording",
            AppSessionState.Recording => $"{AppIdentity.DisplayName} - Recording. Click again to stop",
            AppSessionState.Processing => $"{AppIdentity.DisplayName} - Transcribing",
            AppSessionState.Ready => $"{AppIdentity.DisplayName} - Transcript copied",
            AppSessionState.Error => $"{AppIdentity.DisplayName} - Error. Details shown",
            _ => AppIdentity.DisplayName
        };

        return text.Length <= 63 ? text : text[..63];
    }

    private static string BuildStatusLabel(AppSnapshot snapshot)
    {
        return snapshot.State switch
        {
            AppSessionState.Idle => "Ready to record",
            AppSessionState.Starting => "Starting recording",
            AppSessionState.Recording => "Recording",
            AppSessionState.Processing => "Transcribing",
            AppSessionState.Ready => "Transcript copied",
            AppSessionState.Error => "Attention needed",
            _ => "Ready"
        };
    }

    private void ShowStateHint(AppSnapshot snapshot)
    {
        switch (snapshot.State)
        {
            case AppSessionState.Recording:
                _notifyIcon.ShowBalloonTip(
                    800,
                    "Recording started",
                    _recordingStopHint,
                    ToolTipIcon.Info);
                break;
            case AppSessionState.Processing:
                _notifyIcon.ShowBalloonTip(
                    700,
                    "Recording stopped",
                    "Transcribing audio now.",
                    ToolTipIcon.Info);
                break;
            case AppSessionState.Error:
                _notifyIcon.ShowBalloonTip(
                    1200,
                    $"{AppIdentity.DisplayName} error",
                    snapshot.StatusMessage,
                    ToolTipIcon.Error);
                break;
        }
    }

    public void UpdateHotkeyDisplay(string hotkeyLabel, string recordingStopHint)
    {
        _hotkeyMenuItem.Text = $"Hotkey: {hotkeyLabel}";
        _recordingStopHint = recordingStopHint;
    }
}
