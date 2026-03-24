using System.Globalization;
using NAudio.Wave;
using Stt.Core.Models;

namespace Stt.Infrastructure.Audio;

public static class MicrophoneDeviceCatalog
{
    private const string DefaultDeviceId = "";
    private const string DefaultDeviceLabel = "System default";

    public static IReadOnlyList<MicrophoneDeviceOption> GetAvailableDevices()
    {
        var devices = new List<MicrophoneDeviceOption>
        {
            new(DefaultDeviceId, DefaultDeviceLabel)
        };

        var displayNameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var deviceNumber = 0; deviceNumber < WaveInEvent.DeviceCount; deviceNumber++)
        {
            var capabilities = WaveInEvent.GetCapabilities(deviceNumber);
            var baseName = string.IsNullOrWhiteSpace(capabilities.ProductName)
                ? $"Microphone {deviceNumber + 1}"
                : capabilities.ProductName.Trim();

            displayNameCounts.TryGetValue(baseName, out var duplicateCount);
            duplicateCount++;
            displayNameCounts[baseName] = duplicateCount;

            var displayName = duplicateCount == 1
                ? baseName
                : $"{baseName} ({duplicateCount.ToString(CultureInfo.InvariantCulture)})";

            devices.Add(new(
                DeviceId: deviceNumber.ToString(CultureInfo.InvariantCulture),
                DisplayName: displayName));
        }

        return devices;
    }

    public static int? ResolveDeviceNumber(string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return null;
        }

        if (!int.TryParse(deviceId, NumberStyles.None, CultureInfo.InvariantCulture, out var deviceNumber))
        {
            return null;
        }

        return deviceNumber >= 0 && deviceNumber < WaveInEvent.DeviceCount
            ? deviceNumber
            : null;
    }
}
