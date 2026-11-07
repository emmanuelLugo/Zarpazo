using System.Runtime;

namespace Zarpazo.Recording;

/// <summary>Hands memory back after a recording: the large picture buffers would otherwise stay allocated until the runtime decides to collect them.</summary>
internal static class MemoryTrim
{
    public static void Release()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
    }
}
