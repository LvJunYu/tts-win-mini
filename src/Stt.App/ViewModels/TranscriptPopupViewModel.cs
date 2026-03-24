using Stt.App.Common;
using Stt.Core.Models;
using Stt.App;

namespace Stt.App.ViewModels;

public sealed class TranscriptPopupViewModel : ObservableObject
{
    private string _displayText = string.Empty;
    private string _windowTitle = "Transcript";

    public string DisplayText
    {
        get => _displayText;
        private set => SetProperty(ref _displayText, value);
    }

    public string WindowTitle
    {
        get => _windowTitle;
        private set => SetProperty(ref _windowTitle, value);
    }

    public void ApplySnapshot(AppSnapshot snapshot)
    {
        WindowTitle = snapshot.State == AppSessionState.Error
            ? $"{AppIdentity.DisplayName} Error"
            : $"{AppIdentity.DisplayName} Transcript";

        DisplayText = snapshot.State == AppSessionState.Error
            ? $"Error: {snapshot.StatusMessage}"
            : !string.IsNullOrWhiteSpace(snapshot.TranscriptText)
                ? snapshot.TranscriptText
                : snapshot.StatusMessage;
    }
}
