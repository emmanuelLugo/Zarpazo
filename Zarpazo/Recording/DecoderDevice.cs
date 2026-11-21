using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;

namespace Zarpazo.Recording;

/// <summary>
/// The graphics card, handed to Media Foundation so a source reader decodes H.264 on it (DXVA) instead of on the processor.
/// The pictures are the same either way: H.264 decoding is exact, so every decoder gives the same pixels.
/// </summary>
internal sealed class DecoderDevice : IDisposable
{
    private readonly ID3D11Device _device;

    private DecoderDevice(ID3D11Device device, IMFDXGIDeviceManager manager)
    {
        _device = device;
        Manager = manager;
    }

    /// <summary>What goes into the source reader's MF_SOURCE_READER_D3D_MANAGER attribute.</summary>
    public IMFDXGIDeviceManager Manager { get; }

    /// <returns>Null when there is no card that decodes video (a remote session, a machine with none).</returns>
    public static DecoderDevice? TryCreate()
    {
        ID3D11Device? device = null;
        try
        {
            D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.VideoSupport | DeviceCreationFlags.BgraSupport, null!, out device!).CheckError();
            // The reader decodes on its own threads while ours copies the pictures out.
            using (ID3D11Multithread multithread = device.QueryInterface<ID3D11Multithread>())
            {
                multithread.SetMultithreadProtected(true);
            }
            Marshal.ThrowExceptionForHR(MFCreateDXGIDeviceManager(out uint token, out IMFDXGIDeviceManager manager));
            manager.ResetDevice(device.NativePointer, token);
            return new DecoderDevice(device, manager);
        }
        catch (Exception ex) when (ex is COMException or SharpGen.Runtime.SharpGenException or InvalidCastException)
        {
            device?.Dispose();
            return null;
        }
    }

    public void Dispose()
    {
        Marshal.ReleaseComObject(Manager);
        _device.Dispose();
    }

    [DllImport("mfplat.dll")]
    private static extern int MFCreateDXGIDeviceManager(out uint resetToken, out IMFDXGIDeviceManager manager);
}
