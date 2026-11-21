using System.Runtime.InteropServices;

namespace Zarpazo.Recording;

/// <summary>Media Foundation's holder of the Direct3D device its decoders share (mfobjects.h); NAudio does not bring it. Methods in vtable order.</summary>
[ComImport]
[Guid("eb533d5d-2db6-40f8-97a9-494692014f07")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFDXGIDeviceManager
{
    void CloseDeviceHandle(IntPtr device);

    void GetVideoService(IntPtr device, ref Guid riid, out IntPtr service);

    void LockDevice(IntPtr device, ref Guid riid, out IntPtr unknownDevice, [MarshalAs(UnmanagedType.Bool)] bool block);

    void OpenDeviceHandle(out IntPtr device);

    void ResetDevice(IntPtr unknownDevice, uint resetToken);

    void TestDevice(IntPtr device);

    void UnlockDevice(IntPtr device, [MarshalAs(UnmanagedType.Bool)] bool saveState);
}
