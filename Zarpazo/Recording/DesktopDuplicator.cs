using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Zarpazo.Recording;

/// <summary>
/// Copies a part of one screen with the graphics card (DXGI Desktop Duplication): 1 to 3 ms a picture at 1080p, where
/// CopyFromScreen takes about 33 ms, so a 30 fps recording no longer has pictures that come late. Unlike Windows.Graphics.Capture
/// it draws no yellow border on Windows 10. The pointer is not in the copy; it is drawn afterwards like before.
/// </summary>
/// <remarks>
/// Use it from one thread. When it cannot be used (the area spans two screens, the screen is rotated, or Windows is showing
/// something it does not let be copied, like the UAC prompt) <see cref="TryCopy"/> says so and the caller copies the old way.
/// </remarks>
internal sealed class DesktopDuplicator : IDisposable
{
    private readonly Rectangle _region; // in pixels of the whole desktop
    private readonly IDXGIOutput1 _output;
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly ID3D11Texture2D _staging; // the area, kept between pictures: when the screen has not changed it is copied again
    private readonly Point _offset; // where the area starts on its screen
    private IDXGIOutputDuplication? _duplication;
    private bool _hasPicture;
    private long _nextRestart; // after a failure, starting again is tried at most once a second: it is slow
    private int _copied;
    private int _reused;
    private int _failed;
    private int _restarts;
    private string? _lastError;

    /// <summary>What happened so far, for the diagnosis file: new pictures, repeated ones, failures and the last error.</summary>
    public string Report => $"copias {_copied}, sin cambios {_reused}, fallos {_failed}, reinicios {_restarts}" + (_lastError == null ? "" : $", último error: {_lastError}");

    private DesktopDuplicator(Rectangle region, IDXGIAdapter1 adapter, IDXGIOutput1 output, Point offset)
    {
        _region = region;
        _output = output;
        _offset = offset;
        D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport, null!, out _device!, out _context!).CheckError();
        _staging = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)region.Width,
            Height = (uint)region.Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            CPUAccessFlags = CpuAccessFlags.Read,
        });
        Restart();
    }

    /// <summary>A duplicator for an area that lies on one screen; null when there is none (it spans screens, or DXGI is not available).</summary>
    /// <param name="region">In physical pixels of the whole desktop, as the area is chosen.</param>
    public static DesktopDuplicator? TryCreate(Rectangle region)
    {
        try
        {
            using IDXGIFactory1 factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            for (uint a = 0; factory.EnumAdapters1(a, out IDXGIAdapter1? adapter).Success; a++)
            {
                for (uint o = 0; adapter!.EnumOutputs(o, out IDXGIOutput? output).Success; o++)
                {
                    OutputDescription description = output!.Description;
                    var bounds = description.DesktopCoordinates;
                    var screen = Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
                    if (description.AttachedToDesktop && description.Rotation is ModeRotation.Identity or ModeRotation.Unspecified
                        && screen.Contains(region))
                    {
                        IDXGIOutput1 output1 = output.QueryInterface<IDXGIOutput1>();
                        output.Dispose();
                        try
                        {
                            return new DesktopDuplicator(region, adapter, output1, new Point(region.X - screen.X, region.Y - screen.Y));
                        }
                        finally
                        {
                            adapter.Dispose();
                        }
                    }
                    output.Dispose();
                }
                adapter.Dispose();
            }
        }
        catch (Exception ex) when (ex is SharpGenException or COMException or DllNotFoundException or EntryPointNotFoundException)
        {
            // No DXGI here (a remote session, an old driver): the old way still works.
        }
        return null;
    }

    /// <summary>Fills the bitmap (the area's size, 32 bits a pixel) with what the area shows now.</summary>
    /// <returns>False when the screen could not be copied this time; the bitmap is left as it was.</returns>
    public bool TryCopy(Bitmap into)
    {
        try
        {
            if (_duplication == null && (Environment.TickCount64 < _nextRestart || !Restart()))
            {
                _failed++;
                return false;
            }

            Result result = _duplication!.AcquireNextFrame(0, out OutduplFrameInfo info, out IDXGIResource? resource);
            if (result.Code == Vortice.DXGI.ResultCode.WaitTimeout.Code)
            {
                // Nothing new on the screen: the last picture still holds, if there is one.
                _reused++;
                return _hasPicture && Read(into);
            }
            if (result.Code == Vortice.DXGI.ResultCode.AccessLost.Code)
            {
                Fail("acceso perdido"); // the screen changed mode, or Windows switched to another desktop
                return false;
            }
            result.CheckError();

            if (info.LastPresentTime == 0)
            {
                // Only the pointer moved. The first frame after starting is always like this, and its picture is empty
                // (all zeros), so the last good copy is used, or none yet.
                resource?.Dispose();
                _duplication.ReleaseFrame();
                _reused++;
                return _hasPicture && Read(into);
            }

            try
            {
                using ID3D11Texture2D screen = resource!.QueryInterface<ID3D11Texture2D>();
                var box = new Box(_offset.X, _offset.Y, 0, _offset.X + _region.Width, _offset.Y + _region.Height, 1);
                _context.CopySubresourceRegion(_staging, 0, 0, 0, 0, screen, 0, box);
                _hasPicture = true;
                _copied++;
            }
            finally
            {
                resource!.Dispose();
                _duplication.ReleaseFrame();
            }
            return Read(into);
        }
        catch (Exception ex) when (ex is SharpGenException or COMException)
        {
            Fail(ex.Message);
            return false;
        }
    }

    private void Fail(string reason)
    {
        _failed++;
        _lastError = reason;
        Stop();
        _nextRestart = Environment.TickCount64 + 1000;
    }

    // The area, from the graphics card's memory into the bitmap, row by row (the card's rows can be longer).
    private bool Read(Bitmap into)
    {
        MappedSubresource mapped = _context.Map(_staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            BitmapData data = into.LockBits(new Rectangle(0, 0, _region.Width, _region.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                int rowBytes = _region.Width * 4;
                for (int row = 0; row < _region.Height; row++)
                {
                    CopyMemory(data.Scan0 + row * data.Stride, mapped.DataPointer + (nint)(row * mapped.RowPitch), (nuint)rowBytes);
                }
            }
            finally
            {
                into.UnlockBits(data);
            }
        }
        finally
        {
            _context.Unmap(_staging, 0);
        }
        return true;
    }

    private bool Restart()
    {
        try
        {
            _restarts++;
            _duplication = _output.DuplicateOutput(_device);
            return true;
        }
        catch (SharpGenException ex)
        {
            _duplication = null; // for instance while the UAC prompt is up; tried again in a second
            _lastError = ex.Message;
            _nextRestart = Environment.TickCount64 + 1000;
            return false;
        }
    }

    private void Stop()
    {
        _duplication?.Dispose();
        _duplication = null;
    }

    public void Dispose()
    {
        Stop();
        _staging.Dispose();
        _context.Dispose();
        _device.Dispose();
        _output.Dispose();
    }

    [DllImport("kernel32.dll", EntryPoint = "RtlCopyMemory")]
    private static extern void CopyMemory(IntPtr destination, IntPtr source, nuint length);
}
