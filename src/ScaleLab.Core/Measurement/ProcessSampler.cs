using System.Diagnostics;

namespace ScaleLab.Core.Measurement;

/// <summary>Reads the current process's memory and CPU figures for <see cref="ProcessStats"/>.</summary>
public static class ProcessSampler
{
    /// <param name="forceFullGc">
    /// Run a blocking, compacting full collection first. Memory per connection is then the memory connections really
    /// retain, not garbage that happens not to be collected yet. It pauses the process, so the endpoint that asks for it
    /// is protected and the load generator only calls it between measurements.
    /// </param>
    public static ProcessStats Sample(
        string node,
        string role,
        IReadOnlyDictionary<string, int> connections,
        bool backplane,
        bool forceFullGc)
    {
        if (forceFullGc)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        }

        using var process = Process.GetCurrentProcess();
        var info = GC.GetGCMemoryInfo();
        return new ProcessStats(
            node,
            role,
            connections,
            process.WorkingSet64,
            info.HeapSizeBytes,
            info.TotalCommittedBytes,
            process.Threads.Count,
            process.TotalProcessorTime.TotalSeconds,
            backplane);
    }
}
