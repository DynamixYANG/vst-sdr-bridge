using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Vst.Core;

// ABI declarations are copied from the installed niRFSA.h and the verified
// six-argument NiFpga FIFO interface. No per-block managed array allocation.
public sealed unsafe class NiRxHardware : IRxHardware
{
    private uint session, fpga, fifo;
    private readonly NiDeviceSession device;
    private readonly bool ownsDevice;
    private readonly Dictionary<string,uint> registers = new();
    private readonly nint buffer;
    public int BlockSamples { get; }
    public ulong FifoRemaining { get; private set; }
    public ulong FifoCapacity { get; private set; }
    public NiRxHardware(HubOptions options) : this(options,new NiDeviceSession(options),true) { }
    internal NiRxHardware(HubOptions options,NiDeviceSession device,bool ownsDevice=false)
    {
        this.device=device; this.ownsDevice=ownsDevice; session=device.Rfsa; fpga=device.Fpga;
        if (!File.Exists(options.BitfilePath)) throw new FileNotFoundException("NI Streaming for VST bitfile is not installed.", options.BitfilePath);
        BlockSamples = options.BlockSamples;
        buffer = (nint)NativeMemory.AlignedAlloc((nuint)BlockSamples*4,64);
        if (buffer == 0) throw new OutOfMemoryException();
        try
        {
            Check(Native.NiFpgaDll_FindFifo(fpga,"streaming input.dma",out fifo),"Find FIFO");
            Check(Native.NiFpgaDll_ConfigureFifo2(fpga,fifo,(nuint)options.FifoMiB*1024*1024/4,out var depth),"Configure FIFO");
            FifoCapacity = depth;
        }
        catch { Dispose(); throw; }
    }
    private void Check(int code, string operation)
    {
        if (code >= 0) return;
        var detail=new StringBuilder(4096);
        if(session!=0) Native.niRFSA_GetError(session,out _,detail.Capacity,detail);
        throw new NiException(operation,code,detail.ToString());
    }
    private uint Register(string name)
    {
        if(registers.TryGetValue(name,out var value)) return value;
        Check(Native.NiFpgaDll_FindRegister(fpga,"streaming input."+name,out value),"Find "+name);
        registers[name]=value; return value;
    }
    private void Boolean(string name, bool value) => Check(Native.NiFpgaDll_WriteBool(fpga,Register(name),(byte)(value?1:0)),"Write "+name);
    public bool Overflow { get { Check(Native.NiFpgaDll_ReadBool(fpga,Register("overflow"),out var value),"Read overflow"); return value!=0; } }
    private double Attribute(uint id)
    {
        Check(Native.niRFSA_GetAttributeViReal64(session,"",id,out var value),"Read attribute "+id); return value;
    }
    public Frontend Configure(RxConfiguration c)
    {
        c.Validate();
        Check(Native.niRFSA_Abort(session),"Abort");
        Boolean("enable dma",false);
        Check(Native.NiFpgaDll_StopFifo(fpga,fifo),"Stop FIFO");
        Check(Native.niRFSA_ConfigureAcquisitionType(session,100),"Acquisition type");
        Check(Native.niRFSA_ConfigureReferenceLevel(session,"",c.ReferenceDbm),"Reference level");
        Check(Native.niRFSA_ConfigureIQCarrierFrequency(session,"",c.CenterHz),"Center frequency");
        Check(Native.niRFSA_ConfigureIQRate(session,"",c.RateHz),"IQ rate");
        Check(Native.niRFSA_ConfigureNumberOfSamples(session,"",0,BlockSamples),"Continuous samples");
        Check(Native.niRFSA_SetAttributeViInt32(session,"",1150129,2503),"Preamp auto");
        Check(Native.niRFSA_Commit(session),"Commit");
        var actual=c with {CenterHz=Attribute(1150059),RateHz=Attribute(1150007),ReferenceDbm=Attribute(1150004)};
        Check(Native.niRFSA_GetAttributeViInt32(session,"",1150129,out var preamp),"Read preamp");
        Check(Native.niRFSA_GetAttributeViBoolean(session,"",1150137,out var present),"Read preamp presence");
        var front=new Frontend(actual,preamp,present!=0,Attribute(1150125));
        Boolean("reset",true); Boolean("reset",false); Boolean("abort",false);
        foreach(var path in new[]{"local","p2p","dio"}) Boolean("enable "+path,false);
        Check(Native.NiFpgaDll_StartFifo(fpga,fifo),"Start FIFO");
        Boolean("enable dma",true);
        Check(Native.niRFSA_Initiate(session),"Initiate");
        return front;
    }
    public nint Read()
    {
        Check(Native.NiFpgaDll_ReadFifoU32(fpga,fifo,buffer,(nuint)BlockSamples,500,out var remaining),"Read DMA FIFO");
        FifoRemaining=remaining; return buffer;
    }
    private bool disposed;
    public void Dispose()
    {
        if(disposed) return; disposed=true;
        if(session!=0) Native.niRFSA_Abort(session);
        if(fpga!=0)
        {
            try { Boolean("enable dma",false); } catch(NiException) { }
            Native.NiFpgaDll_StopFifo(fpga,fifo);
            fpga=0;
        }
        session=0;
        NativeMemory.AlignedFree((void*)buffer);
        if(ownsDevice) device.Dispose();
    }
    internal static class Native
    {
        private const string R="niRFSA_64.dll", F="NiFpga.dll";
        [DllImport(R,CharSet=CharSet.Ansi)] internal static extern int niRFSA_InitWithOptions(string resource,ushort query,ushort reset,string options,out uint session);
        [DllImport(R)] internal static extern int niRFSA_EnableSessionAccess(uint s,ushort enabled);
        [DllImport(R)] internal static extern int niRFSA_close(uint s);
        [DllImport(R)] internal static extern int niRFSA_Abort(uint s);
        [DllImport(R)] internal static extern int niRFSA_Commit(uint s);
        [DllImport(R)] internal static extern int niRFSA_Initiate(uint s);
        [DllImport(R)] internal static extern int niRFSA_ConfigureAcquisitionType(uint s,int type);
        [DllImport(R,CharSet=CharSet.Ansi)] internal static extern int niRFSA_ConfigureReferenceLevel(uint s,string channel,double value);
        [DllImport(R,CharSet=CharSet.Ansi)] internal static extern int niRFSA_ConfigureIQCarrierFrequency(uint s,string channel,double value);
        [DllImport(R,CharSet=CharSet.Ansi)] internal static extern int niRFSA_ConfigureIQRate(uint s,string channel,double value);
        [DllImport(R,CharSet=CharSet.Ansi)] internal static extern int niRFSA_ConfigureNumberOfSamples(uint s,string channel,ushort finite,long samples);
        [DllImport(R,CharSet=CharSet.Ansi)] internal static extern int niRFSA_GetAttributeViReal64(uint s,string channel,uint id,out double value);
        [DllImport(R,CharSet=CharSet.Ansi)] internal static extern int niRFSA_GetAttributeViInt32(uint s,string channel,uint id,out int value);
        [DllImport(R,CharSet=CharSet.Ansi)] internal static extern int niRFSA_SetAttributeViInt32(uint s,string channel,uint id,int value);
        [DllImport(R,CharSet=CharSet.Ansi)] internal static extern int niRFSA_GetAttributeViBoolean(uint s,string channel,uint id,out ushort value);
        [DllImport(R,CharSet=CharSet.Ansi)] internal static extern int niRFSA_GetError(uint s,out int code,int length,StringBuilder message);
        [DllImport(F,CharSet=CharSet.Ansi)] internal static extern int NiFpgaDll_Open(string bitfile,string signature,string resource,uint attribute,out uint session);
        [DllImport(F)] internal static extern int NiFpgaDll_Close(uint s,uint attribute);
        [DllImport(F,CharSet=CharSet.Ansi)] internal static extern int NiFpgaDll_FindFifo(uint s,string name,out uint fifo);
        [DllImport(F,CharSet=CharSet.Ansi)] internal static extern int NiFpgaDll_FindRegister(uint s,string name,out uint register);
        [DllImport(F)] internal static extern int NiFpgaDll_ConfigureFifo2(uint s,uint fifo,nuint requested,out nuint actual);
        [DllImport(F)] internal static extern int NiFpgaDll_StartFifo(uint s,uint fifo);
        [DllImport(F)] internal static extern int NiFpgaDll_StopFifo(uint s,uint fifo);
        [DllImport(F)] internal static extern int NiFpgaDll_ReadFifoU32(uint s,uint fifo,nint buffer,nuint count,uint timeout,out nuint remaining);
        [DllImport(F)] internal static extern int NiFpgaDll_WriteBool(uint s,uint register,byte value);
        [DllImport(F)] internal static extern int NiFpgaDll_ReadBool(uint s,uint register,out byte value);
    }
}
