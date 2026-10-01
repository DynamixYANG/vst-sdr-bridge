using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Vst.Core;

/// <summary>
/// SPSC TX IQ ring in named shared memory. Producer = sink/Soapy; consumer = Hub TxEngine.
/// Never overwrites unconsumed samples (backpressure). Distinct from the RX live ring.
/// </summary>
public sealed unsafe class SharedTxRing : IDisposable
{
    public const uint Magic = 0x31585456; // VTX1
    public const uint HubMagic = 0x31425548; // HUB1
    public const string DefaultName = @"Local\vst_tx_v1";
    private nint map, view;
    private readonly Mutex mutex;
    private readonly ulong capacity;
    private readonly string name;
    public string Name => name;
    public ulong CapacitySamples => capacity;

    public SharedTxRing(string name, int capacityMiB, bool create)
    {
        this.name = string.IsNullOrWhiteSpace(name) ? DefaultName : name;
        mutex = new Mutex(false, this.name + ".lock");
        capacity = (ulong)Math.Max(16, capacityMiB) * 1048576UL / 4UL;
        var bytes = capacity * 4 + 256;
        if (create)
        {
            map = CreateFileMappingW(-1, 0, 4, (uint)(bytes >> 32), (uint)bytes, this.name);
            if (map == 0) throw new Win32Exception();
            view = MapViewOfFile(map, 0x000F001F, 0, 0, (nuint)bytes);
            if (view == 0) { CloseHandle(map); map = 0; throw new Win32Exception(); }
            WithLock(() =>
            {
                NativeMemory.Clear((void*)view, 256);
                U32(0) = Magic; U32(4) = 1; U64(16) = capacity;
                F64(8) = 120e6; U32(120) = HubMagic; U32(60) = 1; // consumer_active
                U32(92) = (uint)Environment.ProcessId; U64(104) = GetTickCount64();
            });
        }
        else
        {
            map = OpenFileMappingW(0x000F001F, false, this.name);
            if (map == 0) throw new Win32Exception();
            view = MapViewOfFile(map, 0x000F001F, 0, 0, 256);
            if (view == 0) { CloseHandle(map); map = 0; throw new Win32Exception(); }
            if (U32(0) != Magic) throw new InvalidDataException("TX ring magic mismatch; expected VTX1.");
            capacity = U64(16);
            bytes = capacity * 4 + 256;
            UnmapViewOfFile(view);
            view = MapViewOfFile(map, 0x000F001F, 0, 0, (nuint)bytes);
            if (view == 0) { CloseHandle(map); map = 0; throw new Win32Exception(); }
        }
    }

    private ref uint U32(int offset) => ref *(uint*)(view + offset);
    private ref ulong U64(int offset) => ref *(ulong*)(view + offset);
    private ref double F64(int offset) => ref *(double*)(view + offset);
    private void Lock()
    {
        try { if (!mutex.WaitOne(1000)) throw new TimeoutException("TX SHM mutex timeout"); }
        catch (AbandonedMutexException) { }
    }
    private void WithLock(Action action) { Lock(); try { action(); } finally { mutex.ReleaseMutex(); } }

    public void PublishApplied(double centerHz, double rateHz, double peakDbm, bool rfEnabled, string status)
    {
        WithLock(() =>
        {
            F64(128) = centerHz; F64(144) = rateHz; F64(136) = peakDbm;
            U32(152) = rfEnabled ? 1u : 0u;
            U32(60) = 1; U32(92) = (uint)Environment.ProcessId; U64(104) = GetTickCount64();
            U32(156) = status switch
            {
                "STREAMING" => 1u,
                "PREFILLING" => 2u,
                "CONFIGURING" => 3u,
                "FAULT" => 4u,
                "STOPPED" => 5u,
                _ => 0u
            };
        });
    }

    public bool TryReadProducerHeartbeat(out uint pid, out ulong ageMs, out ulong occupancy)
    {
        Lock();
        try
        {
            var now = GetTickCount64();
            pid = U32(88);
            var hb = U64(96);
            ageMs = hb == 0 ? ulong.MaxValue : now - hb;
            var w = U64(24); var r = U64(32);
            occupancy = w >= r ? w - r : 0;
            return U32(56) != 0 && pid != 0;
        }
        finally { mutex.ReleaseMutex(); }
    }

    /// <summary>Copy up to samples complex CS16 into destination. Returns samples copied (may be 0).</summary>
    /// <summary>Producer write: copy CS16 complex samples. Returns samples written (0 if full).</summary>
    public int TryWrite(nint source, int samples)
    {
        if (samples <= 0) return 0;
        Lock();
        try
        {
            var w = U64(24); var r = U64(32);
            if (w < r) return 0;
            var free = (int)Math.Min((ulong)samples, capacity - (w - r));
            if (free <= 0) { U64(96) = GetTickCount64(); return 0; }
            var pos = w % capacity;
            var first = (int)Math.Min((ulong)free, capacity - pos);
            Buffer.MemoryCopy((void*)source, (void*)(view + 256 + (nint)(pos * 4)), first * 4L, first * 4L);
            if (first < free)
                Buffer.MemoryCopy((void*)(source + first * 4), (void*)(view + 256), (free - first) * 4L, (free - first) * 4L);
            U64(24) = w + (ulong)free;
            U32(56) = 1;
            U32(88) = (uint)Environment.ProcessId;
            U64(96) = GetTickCount64();
            U64(40) += (ulong)free; // accepted_samples field reuse of old drops slot @40
            return free;
        }
        finally { mutex.ReleaseMutex(); }
    }

    public void ClaimProducer()
    {
        WithLock(() =>
        {
            U32(56) = 1;
            U32(88) = (uint)Environment.ProcessId;
            U64(96) = GetTickCount64();
        });
    }

    public void ReleaseProducer()
    {
        WithLock(() => { if (U32(88) == (uint)Environment.ProcessId) U32(56) = 0; });
    }

    public int TryCopy(nint destination, int samples)
    {
        if (samples <= 0) return 0;
        Lock();
        try
        {
            var w = U64(24); var r = U64(32);
            if (w < r) return 0;
            var avail = (int)Math.Min((ulong)samples, w - r);
            if (avail <= 0) return 0;
            var pos = r % capacity;
            var first = (int)Math.Min((ulong)avail, capacity - pos);
            Buffer.MemoryCopy((void*)(view + 256 + (nint)(pos * 4)), (void*)destination, first * 4L, first * 4L);
            if (first < avail)
                Buffer.MemoryCopy((void*)(view + 256), (void*)(destination + first * 4), (avail - first) * 4L, (avail - first) * 4L);
            U64(32) = r + (ulong)avail;
            U64(104) = GetTickCount64();
            return avail;
        }
        finally { mutex.ReleaseMutex(); }
    }

    public void MarkStopped()
    {
        WithLock(() => { U32(60) = 0; U32(152) = 0; U32(156) = 5; });
    }

    public void Dispose()
    {
        try { MarkStopped(); } catch { }
        if (view != 0) { UnmapViewOfFile(view); view = 0; }
        if (map != 0) { CloseHandle(map); map = 0; }
        mutex.Dispose();
    }

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateFileMappingW(nint file, nint attributes, uint protect, uint high, uint low, string name);
    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint OpenFileMappingW(uint access, bool inherit, string name);
    [DllImport("kernel32", SetLastError = true)] private static extern nint MapViewOfFile(nint handle, uint access, uint high, uint low, nuint size);
    [DllImport("kernel32")] private static extern bool UnmapViewOfFile(nint view);
    [DllImport("kernel32")] private static extern bool CloseHandle(nint handle);
    [DllImport("kernel32")] public static extern ulong GetTickCount64();
}
