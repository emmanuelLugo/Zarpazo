using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;

namespace Zarpazo.Recording;

/// <summary>Hands memory back after a capture or recording: the large picture buffers would otherwise stay allocated until the runtime decides to collect them.</summary>
internal static class MemoryTrim
{
    public static void Release()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers(); // the finalizers are what free WPF's native bitmap memory

        // Most of a capture's memory is native (WPF, GDI+), which the collector doesn't manage. Windows keeps those pages in
        // the process until it needs the RAM; this asks for them back now, and whatever is used again is reloaded on demand.
        using Process self = Process.GetCurrentProcess();
        EmptyWorkingSet(self.Handle);
    }

    [DllImport("psapi.dll")]
    private static extern bool EmptyWorkingSet(IntPtr process);
}
