namespace Stt.Core.Abstractions;

public interface IRecordingWorkflowStartupNotifier
{
    event EventHandler? RecordingStarted;
}
