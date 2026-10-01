using System.Runtime.InteropServices;

namespace Vst.Core;

// SPSC bounded block queue: consumer releases a slot only after DMA accepted it.
// No overwrite/drop mode. Independent producer and DMA workers remain bounded.
internal sealed unsafe class TxSampleQueue : IDisposable
{
    public const int BlockSamples=1048576;
    private readonly nint data;
    private readonly int slots;
    private readonly SemaphoreSlim writable,readable=new(0);
    private long produced,consumed,waits;
    public ulong Produced=>(ulong)Interlocked.Read(ref produced)*BlockSamples;
    public ulong Consumed=>(ulong)Interlocked.Read(ref consumed)*BlockSamples;
    public ulong Occupancy=>(ulong)Math.Clamp((long)Produced-(long)Consumed,0,(long)Capacity);
    public ulong Capacity=>(ulong)slots*BlockSamples;
    public ulong Waits=>(ulong)Interlocked.Read(ref waits);
    public TxSampleQueue(int mib)
    {
        slots=mib/4;writable=new(slots,slots);
        data=(nint)NativeMemory.AlignedAlloc((nuint)mib*1048576,64);
        if(data==0) throw new OutOfMemoryException();
    }
    public nint AcquireWrite(CancellationToken token)
    {
        if(!writable.Wait(0)) {Interlocked.Increment(ref waits);writable.Wait(token);}
        return data+(nint)(Interlocked.Read(ref produced)%slots*BlockSamples*4);
    }
    public void Publish() {Interlocked.Increment(ref produced);readable.Release();}
    public nint AcquireRead(CancellationToken token)
    {
        if(!readable.Wait(500,token)) throw new TimeoutException("TX source starved for 500 ms. TX latched off.");
        return data+(nint)(Interlocked.Read(ref consumed)%slots*BlockSamples*4);
    }
    public void Release() {Interlocked.Increment(ref consumed);writable.Release();}
    public void Dispose() {NativeMemory.AlignedFree((void*)data);writable.Dispose();readable.Dispose();}
}
