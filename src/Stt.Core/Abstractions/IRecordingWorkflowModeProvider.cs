using Stt.Core.Models;

namespace Stt.Core.Abstractions;

public interface IRecordingWorkflowModeProvider
{
    RecordingWorkflowMode GetCurrentMode();
}
