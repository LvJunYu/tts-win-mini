using System.Windows.Forms;

namespace Stt.App.Services;

public sealed record HotkeyGesture(
    uint Modifiers,
    Keys Key,
    string DisplayText);

