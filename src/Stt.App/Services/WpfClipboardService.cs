using Stt.Core.Abstractions;

namespace Stt.App.Services;

public sealed class WpfClipboardService : IClipboardService
{
    public void CopyText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        System.Windows.Application.Current.Dispatcher.Invoke(
            () => System.Windows.Clipboard.SetText(text));
    }
}
