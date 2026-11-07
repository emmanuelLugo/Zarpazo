using NAudio.CoreAudioApi;

namespace Zarpazo.Recording;

/// <param name="Id">What Windows calls the device; empty for "whichever Windows uses by default".</param>
internal sealed record AudioDeviceInfo(string Id, string Name);

internal static class AudioDevices
{
    /// <summary>The microphones that are plugged in and enabled right now.</summary>
    public static IReadOnlyList<AudioDeviceInfo> Microphones() => Active(DataFlow.Capture);

    /// <summary>The speakers and headphones that are plugged in and enabled right now: the places "Sonido de Windows" can be taken from.</summary>
    public static IReadOnlyList<AudioDeviceInfo> Speakers() => Active(DataFlow.Render);

    private static IReadOnlyList<AudioDeviceInfo> Active(DataFlow flow)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            return enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active)
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
