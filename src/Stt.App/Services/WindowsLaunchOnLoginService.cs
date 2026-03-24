using Microsoft.Win32;
using Stt.App;

namespace Stt.App.Services;

public sealed class WindowsLaunchOnLoginService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public void ApplySetting(bool launchOnWindowsLogin)
    {
        using var runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (runKey is null)
        {
            throw new InvalidOperationException("Couldn't open the Windows startup registry key.");
        }

        if (!launchOnWindowsLogin)
        {
            runKey.DeleteValue(AppIdentity.StartupValueName, throwOnMissingValue: false);
            runKey.DeleteValue(AppIdentity.LegacyStartupValueName, throwOnMissingValue: false);
            return;
        }

        runKey.DeleteValue(AppIdentity.LegacyStartupValueName, throwOnMissingValue: false);
        runKey.SetValue(AppIdentity.StartupValueName, BuildLaunchCommand(), RegistryValueKind.String);
    }

    private static string BuildLaunchCommand()
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new InvalidOperationException("Couldn't determine the current app executable path.");
        }

        return $"\"{executablePath}\"";
    }
}
