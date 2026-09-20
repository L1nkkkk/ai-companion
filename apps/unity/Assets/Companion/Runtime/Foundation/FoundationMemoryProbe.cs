using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Companion.Foundation
{
    // Windows-only measurement fixture. Unity's bundled Mono reports zero for Process memory properties.
    internal static class FoundationMemoryProbe
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessMemoryCountersEx
        {
            public uint Size;
            public uint PageFaultCount;
            public UIntPtr PeakWorkingSetSize;
            public UIntPtr WorkingSetSize;
            public UIntPtr QuotaPeakPagedPoolUsage;
            public UIntPtr QuotaPagedPoolUsage;
            public UIntPtr QuotaPeakNonPagedPoolUsage;
            public UIntPtr QuotaNonPagedPoolUsage;
            public UIntPtr PagefileUsage;
            public UIntPtr PeakPagefileUsage;
            public UIntPtr PrivateUsage;
        }

        internal readonly struct Sample
        {
            public readonly long WorkingSetBytes;
            public readonly long PeakWorkingSetBytes;
            public readonly long PrivateBytes;
            public Sample(long workingSet, long peakWorkingSet, long privateBytes)
            { WorkingSetBytes = workingSet; PeakWorkingSetBytes = peakWorkingSet; PrivateBytes = privateBytes; }
        }

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("psapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetProcessMemoryInfo(IntPtr process,
            ref ProcessMemoryCountersEx counters, uint size);

        internal static Sample Read()
        {
            var counters = new ProcessMemoryCountersEx();
            counters.Size = (uint)Marshal.SizeOf<ProcessMemoryCountersEx>();
            if (!GetProcessMemoryInfo(GetCurrentProcess(), ref counters, counters.Size))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Player memory measurement failed.");
            return new Sample(checked((long)counters.WorkingSetSize.ToUInt64()),
                checked((long)counters.PeakWorkingSetSize.ToUInt64()),
                checked((long)counters.PrivateUsage.ToUInt64()));
        }
    }
}
