using System.Diagnostics;
using System.Runtime.InteropServices;
using LightSpace.Core.Config;
using NAudio.CoreAudioApi;

namespace LightSpace.Engine.Sources;

/// <summary>
/// Откуда слушать звук: системный звук или микрофон и конкретное устройство.
/// </summary>
/// <param name="DeviceId">Системный идентификатор устройства; пусто — устройство Windows по умолчанию.</param>
public sealed record AudioInput(AudioInputKind Kind, string DeviceId);

/// <summary>
/// Устройство звука: системный идентификатор и название, как в Windows.
/// </summary>
public sealed record AudioDeviceInfo(string Id, string Name);

/// <summary>
/// Подключённые устройства звука.
/// </summary>
public static class AudioDevices
{
    /// <summary>
    /// Включённые сейчас устройства вывода (для системного звука) или записи (для микрофона);
    /// пусто — устройств нет или Windows не ответила.
    /// </summary>
    public static IReadOnlyList<AudioDeviceInfo> List(AudioInputKind kind)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();

            return enumerator.EnumerateAudioEndPoints(FlowOf(kind), DeviceState.Active)
                .Select(d => new AudioDeviceInfo(d.ID, d.FriendlyName))
                .ToList();
        }
        catch (COMException e)
        {
            Trace.TraceWarning($"Light Space: не удалось получить список устройств звука: {e.Message}");

            return Array.Empty<AudioDeviceInfo>();
        }
    }

    /// <summary>
    /// Устройство, с которого слушать звук: выбранное, а если его нет или оно не выбрано — устройство по умолчанию.
    /// </summary>
    public static MMDevice Open(MMDeviceEnumerator enumerator, AudioInput input)
    {
        if (!string.IsNullOrEmpty(input.DeviceId))
        {
            try
            {
                var device = enumerator.GetDevice(input.DeviceId);

                if (device.State == DeviceState.Active)
                    return device;
            }
            catch (COMException)
            {
                // Устройство отключили или удалили — берём устройство по умолчанию
            }
        }

        return enumerator.GetDefaultAudioEndpoint(FlowOf(input.Kind), Role.Multimedia);
    }

    private static DataFlow FlowOf(AudioInputKind kind)
    {
        return kind == AudioInputKind.Microphone ? DataFlow.Capture : DataFlow.Render;
    }
}
