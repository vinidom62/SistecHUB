using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SistecHub.Core;

/// <summary>Rotinas centralizadas para libertação pró-activa de memória física (Working Set e GC).</summary>
public static class MemoryOptimizer
{
    [DllImport("psapi.dll")]
    static extern bool EmptyWorkingSet(IntPtr hProcess);

    /// <summary>Força recolha agressiva do GC e liberta páginas de memória física de volta ao Windows.</summary>
    public static void TrimWorkingSet()
    {
        try
        {
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);

            if (OperatingSystem.IsWindows())
            {
                using var current = Process.GetCurrentProcess();
                EmptyWorkingSet(current.Handle);
            }
        }
        catch
        {
            // ignorar
        }
    }
}
