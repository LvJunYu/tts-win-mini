using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Stt.App.Services;

public sealed class GlobalHotkeyService : NativeWindow, IDisposable
{
    private const int WmHotKey = 0x0312;
    private const uint ModNoRepeat = 0x4000;

    private readonly Func<Task> _onTriggered;
    private readonly int _hotkeyId;
    private bool _isRegistered;

    public GlobalHotkeyService(
        HotkeyGesture gesture,
        Func<Task> onTriggered)
    {
        Gesture = gesture;
        _onTriggered = onTriggered;
        _hotkeyId = GetHashCode();

        CreateHandle(new CreateParams());

        if (!RegisterHotKey(Handle, _hotkeyId, gesture.Modifiers | ModNoRepeat, (uint)gesture.Key))
        {
            DestroyHandle();
            throw new InvalidOperationException(
                $"Couldn't register global hotkey {gesture.DisplayText}. It may already be used by another app.");
        }

        _isRegistered = true;
    }

    public HotkeyGesture Gesture { get; }

    public void Dispose()
    {
        if (_isRegistered && Handle != IntPtr.Zero)
        {
            UnregisterHotKey(Handle, _hotkeyId);
            _isRegistered = false;
        }

        if (Handle != IntPtr.Zero)
        {
            DestroyHandle();
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotKey && m.WParam.ToInt32() == _hotkeyId)
        {
            TriggerAsync();
            return;
        }

        base.WndProc(ref m);
    }

    private async void TriggerAsync()
    {
        try
        {
            await _onTriggered();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
