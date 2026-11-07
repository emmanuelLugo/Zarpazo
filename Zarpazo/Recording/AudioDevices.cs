using NAudio.CoreAudioApi;

namespace Zarpazo.Recording;

/// <param name="Id">What Windows calls the device; empty for "whichever Windows uses by default".</param>
internal sealed record AudioDeviceInfo(string Id, string Name);

internal static class AudioDevices
{
    /// <summary>The microphones that are plugged in and enabled right now.</summary>
    public static IReadOnlyList<AudioDeviceInfo> Microphones()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            return enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                .Select(device => new AudioDeviceInfo(device.ID, device.FriendlyName))
                .OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return []; // no audio service: the list is empty and recording without sound still works
        }
    }
}
