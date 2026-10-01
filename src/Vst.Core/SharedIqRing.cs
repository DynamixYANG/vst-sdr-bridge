using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Vst.Core;

public sealed record RingSnapshot
{
    public ulong CapacitySamples { get; init; }
    public ulong WriteIdx { get; init; }
    public ulong ReadIdx { get; init; }
    public ulong Drops { get; init; }
    public ulong IdleDiscardSamples { get; init; }
    public bool Active { get; init; }
    public double CenterHz { get; init; }
    public double RateHz { get; init; }
    public double ReferenceDbm { get; init; }
    public ulong Epoch { get; init; }
    public ulong HeartbeatAgeMs { get; init; }
    public ulong ConsumerHeartbeatAgeMs { get; init; }
    public ulong DeliveredSamples { get; init; }
    public ulong DisplaySkipped { get; init; }
    public uint ConsumerPid { get; init; }
    public bool ConsumerActive { get; init; }
    public ulong Occupancy => WriteIdx>=ReadIdx ? WriteIdx-ReadIdx : 0;
    public double OccupancyMiB => Occupancy*4d/1048576;
    public double BufferPercent => CapacitySamples==0 ? 0 : Occupancy*100d/CapacitySamples;
}

public sealed unsafe class SharedIqRing : IDisposable
{
    public const uint HubMagic=0x31425548; // HUB1 at 248, compatible with CFG1/v2
    private nint map, view;
    private readonly Mutex mutex;
    private readonly ulong capacity;
    public SharedIqRing(string name, int capacityMiB)
    {
        mutex=new Mutex(false,name+".lock");
        capacity=(ulong)capacityMiB*1048576/4;
        var bytes=capacity*4+256;
        map=CreateFileMappingW(-1,0,4,(uint)(bytes>>32),(uint)bytes,name);
        if(map==0) throw new Win32Exception();
        view=MapViewOfFile(map,0x000F001F,0,0,(nuint)bytes);
        if(view==0) {CloseHandle(map); map=0; throw new Win32Exception();}
        WithLock(() =>
        {
            var previousEpoch=U32(0)==0x50313230 ? U64(64) : 0;
            NativeMemory.Clear((void*)view,256);
            U32(0)=0x50313230; U32(4)=2; U64(16)=capacity; U64(64)=previousEpoch+1;
            F64(8)=120e6; U32(88)=(uint)Environment.ProcessId;
            U32(128)=0x31474643; U32(132)=2503; U32(248)=HubMagic;
        });
    }
    private ref uint U32(int offset) => ref *(uint*)(view+offset);
    private ref ulong U64(int offset) => ref *(ulong*)(view+offset);
    private ref double F64(int offset) => ref *(double*)(view+offset);
    private void Lock()
    {
        try { if(!mutex.WaitOne(1000)) throw new TimeoutException("SHM mutex timeout"); }
        catch(AbandonedMutexException) { /* ownership transferred to this thread */ }
    }
    private void WithLock(Action action) { Lock(); try {action();} finally {mutex.ReleaseMutex();} }
    public void Publish(nint source,int samples)
    {
        // Allocation-free hot path. Consumer and producer agree on the same
        // named mutex; payload always precedes publication of write_idx.
        Lock();
        try
        {
            var w=U64(24); var r=U64(32); var n=(ulong)samples;
            if(n>capacity) throw new ArgumentOutOfRangeException(nameof(samples));
            if(w+n-r>capacity)
            {
                var lost=w+n-r-capacity; U64(32)=r+lost;
                if(U32(212)!=0 && GetTickCount64()-U64(112)<2000) U64(40)+=lost;
                else U64(224)+=lost; // no receiver attached: deliberately discard oldest
            }
            var pos=w%capacity; var first=Math.Min(n,capacity-pos);
            Buffer.MemoryCopy((void*)source,(void*)(view+256+(nint)(pos*4)),(long)(first*4),(long)(first*4));
            if(first<n) Buffer.MemoryCopy((void*)(source+(nint)(first*4)),(void*)(view+256),(long)((n-first)*4),(long)((n-first)*4));
            U64(24)=w+n; U64(72)=GetTickCount64(); U32(52)=1;
        }
        finally {mutex.ReleaseMutex();}
    }
    public void Pause() => WithLock(() => {U32(52)=0; U64(32)=U64(24);});
    public void Configure(Frontend f) => WithLock(() =>
    {
        F64(56)=f.Configuration.CenterHz; F64(8)=f.Configuration.RateHz; F64(80)=f.Configuration.ReferenceDbm;
        U64(64)++; U64(32)=U64(24); U32(132)=2503; U32(136)=(uint)f.PreampActual;
        U32(140)=f.PreampPresent?1u:0; F64(144)=f.BandwidthHz;
    });
    public void Telemetry(HubSnapshot s) => WithLock(() =>
    {
        U64(152)=s.OverflowCount; U64(160)=s.Recoveries; F64(168)=s.LastTuneS*1000;
        U64(176)=s.ControlErrorCount; U64(184)=s.ConfigCount; U32(192)=(uint)s.Status;
        U32(196)=unchecked((uint)s.LastNiError); U64(200)=s.RequestId;
        U64(232)=s.FifoRemaining; U64(240)=s.FifoCapacity;
    });
    public RingSnapshot Snapshot()
    {
        Lock(); try
        {
            var now=GetTickCount64();
            return new RingSnapshot { CapacitySamples=capacity,WriteIdx=U64(24),ReadIdx=U64(32),Drops=U64(40),
                Active=U32(52)!=0,CenterHz=F64(56),RateHz=F64(8),ReferenceDbm=F64(80),Epoch=U64(64),
                HeartbeatAgeMs=now-U64(72),ConsumerHeartbeatAgeMs=U64(112)==0?ulong.MaxValue:now-U64(112),
                DeliveredSamples=U64(96),DisplaySkipped=U64(120),ConsumerPid=U32(208),
                ConsumerActive=U32(212)!=0,IdleDiscardSamples=U64(224)};
        } finally {mutex.ReleaseMutex();}
    }
    public void Dispose()
    {
        if(view!=0) {UnmapViewOfFile(view); view=0;}
        if(map!=0) {CloseHandle(map); map=0;}
        mutex.Dispose();
    }
    [DllImport("kernel32",CharSet=CharSet.Unicode,SetLastError=true)] private static extern nint CreateFileMappingW(nint file,nint attributes,uint protect,uint high,uint low,string name);
    [DllImport("kernel32",SetLastError=true)] private static extern nint MapViewOfFile(nint handle,uint access,uint high,uint low,nuint size);
    [DllImport("kernel32")] private static extern bool UnmapViewOfFile(nint view);
    [DllImport("kernel32")] private static extern bool CloseHandle(nint handle);
    [DllImport("kernel32")] public static extern ulong GetTickCount64();
}
