using Native=Vst.Core.NiRxHardware.Native;

namespace Vst.Core;

internal sealed class NiTxHardware : ITxHardware
{
    private readonly NiDeviceSession device;
    private readonly Dictionary<string,uint> registers=new();
    private uint fifo;
    private bool disposed;
    public ulong Capacity {get;private set;}
    public ulong Free {get;private set;}
    public double Rate {get;private set;}
    public double Center {get;private set;}
    public double Peak {get;private set;}
    public bool RfEnabled {get;private set;}
    private void Check(int code,string operation)=>device.CheckTx(code,operation);
    private uint Register(string name)
    {
        if(registers.TryGetValue(name,out uint r)) return r;
        Check(Native.NiFpgaDll_FindRegister(device.Fpga,"streaming output."+name,out r),"TX register "+name);
        registers[name]=r;return r;
    }
    private void Bool(string name,bool value)=>Check(Native.NiFpgaDll_WriteBool(device.Fpga,Register(name),(byte)(value?1:0)),"TX "+name);
    private void U32(string name,uint value)=>Check(TxNative.NiFpgaDll_WriteU32(device.Fpga,Register(name),value),"TX "+name);
    public uint ReadU32(string name) {Check(TxNative.NiFpgaDll_ReadU32(device.Fpga,Register(name),out uint v),"TX read "+name);return v;}
    public ushort ReadState() {Check(TxNative.NiFpgaDll_ReadU16(device.Fpga,Register("state"),out ushort v),"TX state");return v;}
    public bool ReadBool(string name) {Check(Native.NiFpgaDll_ReadBool(device.Fpga,Register(name),out byte v),"TX read "+name);return v!=0;}
    private double Attribute(uint id) {Check(TxNative.niRFSG_GetAttributeViReal64(device.Rfsg,"",id,out var v),"RFSG readback");return v;}
    public NiTxHardware(NiDeviceSession device,int fifoMiB)
    {
        this.device=device;
        Check(Native.NiFpgaDll_FindFifo(device.Fpga,"streaming output.dma",out fifo),"TX find FIFO");
        Check(Native.NiFpgaDll_ConfigureFifo2(device.Fpga,fifo,(nuint)fifoMiB*1048576/4,out var actual),"TX FIFO capacity");
        Capacity=actual;Free=actual;
    }
    public void Configure(double centerHz,double sampleRate,double outputLevelDbm)
    {
        Check(TxNative.niRFSG_Abort(device.Rfsg),"TX abort");
        // Named arb waveforms survive Abort; rewriting without clear returns NI -370024 on the next TXSTART.
        Check(TxNative.niRFSG_ClearAllArbWaveforms(device.Rfsg),"TX clear arb waveforms");
        Check(TxNative.niRFSG_ConfigureOutputEnabled(device.Rfsg,0),"TX RF off");
        Check(TxNative.niRFSG_ConfigureRF(device.Rfsg,centerHz,outputLevelDbm),"TX RF configuration");
        Check(TxNative.niRFSG_ConfigurePowerLevelType(device.Rfsg,7001),"TX peak power mode");
        Check(TxNative.niRFSG_SetAttributeViInt32(device.Rfsg,"",1150018,1001),"TX arb mode");
        Check(TxNative.niRFSG_SetAttributeViReal64(device.Rfsg,"",1250452,sampleRate),"TX IQ rate");
        // NI streaming extension replaces this arb data at the FPGA output mux.
        // Placeholder keeps RFSG generation timing active AND defines peak-power scaling.
        // An all-zero arb has undefined peak; RFSG may mute/mis-scale RF while DMA metrics look fine.
        // Unit-peak I reference; DMA IQ (select=1) still replaces sample content after the mux.
        var dummyI=new double[1024];
        var dummyQ=new double[1024];
        for(int i=0;i<dummyI.Length;i++) dummyI[i]=1.0;
        Check(TxNative.niRFSG_WriteArbWaveform(device.Rfsg,"vst_clock",dummyI.Length,dummyI,dummyQ,0),"TX timing waveform");
        Check(TxNative.niRFSG_SelectArbWaveform(device.Rfsg,"vst_clock"),"TX select timing waveform");
        Check(TxNative.niRFSG_Commit(device.Rfsg),"TX commit");
        Rate=Attribute(1250452);Center=Attribute(1250001);Peak=Attribute(1250002);
        if(Math.Abs(Rate-sampleRate)>1) throw new InvalidOperationException("TX rate was coerced outside requested tolerance.");
        Bool("enable",false);Check(Native.NiFpgaDll_StopFifo(device.Fpga,fifo),"TX FIFO stop");
        Bool("reset",true);Bool("reset",false);Bool("abort",false);Bool("start on primed",false);
        Check(TxNative.NiFpgaDll_WriteU16(device.Fpga,Register("select"),1),"TX select DMA");
        U32("threshold",32768);U32("burst size",0);
        Check(Native.NiFpgaDll_StartFifo(device.Fpga,fifo),"TX FIFO start");
    }
    public void Initiate(bool rfEnabled)
    {
        Check(TxNative.niRFSG_ConfigureOutputEnabled(device.Rfsg,(ushort)(rfEnabled?1:0)),"TX RF enable setting");
        Check(TxNative.niRFSG_Commit(device.Rfsg),"TX RF commit");
        Bool("enable",true);Bool("start on primed",true);
        Check(TxNative.niRFSG_Initiate(device.Rfsg),"TX initiate");
        Check(TxNative.niRFSG_GetAttributeViBoolean(device.Rfsg,"",1250004,out var actual),"TX RF readback");RfEnabled=actual!=0;
    }
    public void Write(nint interleavedIq,int complexSamples)
    {
        // A 1 Mi-sample block takes >1 s at 1 MS/s. Account for the
        // configured drain rate when a full FIFO applies backpressure.
        uint timeout=(uint)Math.Ceiling(complexSamples/Rate*1000)+500;
        Check(TxNative.NiFpgaDll_WriteFifoU32(device.Fpga,fifo,interleavedIq,(nuint)complexSamples,timeout,out var free),"TX DMA write");Free=free;
    }
    public unsafe void RefreshFree()
    {
        uint dummy=0;
        Check(TxNative.NiFpgaDll_WriteFifoU32(device.Fpga,fifo,(nint)(&dummy),0,0,out var free),"TX FIFO space");Free=free;
    }
    public void Stop()
    {
        // Attempt every shutdown step even if one fails. Never auto-restart TX.
        Exception? error=null;
        void Attempt(Action action) {try{action();}catch(Exception ex){error??=ex;}}
        Attempt(()=>Check(TxNative.niRFSG_Abort(device.Rfsg),"TX stop abort"));
        Attempt(()=>Check(TxNative.niRFSG_ConfigureOutputEnabled(device.Rfsg,0),"TX stop RF off"));
        Attempt(()=>Check(TxNative.niRFSG_ClearAllArbWaveforms(device.Rfsg),"TX stop clear arb"));
        Attempt(()=>Check(TxNative.niRFSG_Commit(device.Rfsg),"TX stop commit"));
        Attempt(()=>Bool("enable",false));Attempt(()=>Bool("abort",true));
        Attempt(()=>Check(Native.NiFpgaDll_StopFifo(device.Fpga,fifo),"TX stop FIFO"));
        RfEnabled=false;
        if(error!=null) throw error;
    }
    public void Dispose() {if(disposed)return;disposed=true;Stop();}
}
