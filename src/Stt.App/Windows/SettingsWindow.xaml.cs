using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Stt.App.Services;
using Stt.App.ViewModels;
using FormsKeys = System.Windows.Forms.Keys;

namespace Stt.App.Windows;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    public bool AllowClose { get; set; }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is SettingsViewModel oldViewModel)
        {
            oldViewModel.CloseRequested -= OnCloseRequested;
        }

        if (e.NewValue is SettingsViewModel newViewModel)
        {
            newViewModel.CloseRequested += OnCloseRequested;
        }
    }

    private void OnCloseRequested(object? sender, EventArgs e)
    {
        Hide();
    }

    private void HotkeyTextBox_OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (DataContext is not SettingsViewModel viewModel)
        {
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Tab)
        {
            return;
        }

        if (key is Key.Back or Key.Delete)
        {
            viewModel.ToggleRecordingHotkey = string.Empty;
            e.Handled = true;
            return;
        }

        if (IsModifierOnlyKey(key))
        {
            e.Handled = true;
            return;
        }

        var modifiers = Keyboard.Modifiers;
        var formsKey = (FormsKeys)KeyInterop.VirtualKeyFromKey(key);
        var modifierFlags = HotkeyParser.ToModifierFlags(modifiers);

        viewModel.ToggleRecordingHotkey = HotkeyParser.FormatDisplayText(modifierFlags, formsKey);
        e.Handled = true;
    }

    private void HotkeyTextBox_OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = true;
    }

    private void ClearHotkeyButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel)
        {
            viewModel.ToggleRecordingHotkey = string.Empty;
        }
    }

    private static bool IsModifierOnlyKey(Key key)
    {
        return key is Key.LeftCtrl
            or Key.RightCtrl
            or Key.LeftAlt
            or Key.RightAlt
            or Key.LeftShift
            or Key.RightShift
            or Key.LWin
            or Key.RWin;
    }
}
