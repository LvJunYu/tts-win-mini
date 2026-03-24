using System.ComponentModel;
using System.Windows;
namespace Stt.App.Windows;

public partial class TranscriptWindow : Window
{
    public TranscriptWindow()
    {
        InitializeComponent();
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
}
