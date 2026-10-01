using System.Runtime.InteropServices;
using System.Text;
using Native=Vst.Core.NiRxHardware.Native;

namespace Vst.Core;

// One lifetime for both driver sessions and the shared FPGA. Direction workers
// must be joined before disposal. No reset/download is allowed during streaming.
internal sealed class NiDeviceSession : IDisposable
{
    public uint Rfsa { get; private set; }
    public uint Rfsg { get; private set; }
    public uint Fpga { get; private set; }
    static NiDeviceSession()
    {
        NativeLibrary.SetDllImportResolver(typeof(NiDeviceSession).Assembly,(name,_,_)=>
            name is "niRFSA_64.dll" or "niRFSG_64.dll"
            ? NativeLibrary.Load(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),@"IVI Foundation\IVI\Bin",name)) : 0);
    }
    public NiDeviceSession(HubOptions options,Action<string>? progress=null)
    {
        if(!File.Exists(options.BitfilePath)) throw new FileNotFoundException("NI streaming bitfile missing",options.BitfilePath);
        try
        {
            progress?.Invoke("Opening NI-RFSG session with Streaming for VST bitfile");
            int code=TxNative.niRFSG_InitWithOptions(options.Resource,1,0,"DriverSetup=Bitfile:NI Streaming for VST.lvbitx",out var sg);
            Rfsg=sg; CheckTx(code,"RFSG init");
            progress?.Invoke("Disabling RF output before configuring the device");
            CheckTx(TxNative.niRFSG_ConfigureOutputEnabled(Rfsg,0),"RF output off");
            progress?.Invoke("Opening NI-RFSA session on the shared device");
            code=Native.niRFSA_InitWithOptions(options.Resource,1,0,"DriverSetup=Bitfile:NI Streaming for VST.lvbitx",out var sa);
            Rfsa=sa; CheckRx(code,"RFSA init");
            progress?.Invoke("Enabling shared FPGA session access");
            CheckRx(Native.niRFSA_EnableSessionAccess(Rfsa,1),"FPGA session access");
            progress?.Invoke("Opening shared FPGA and checking bitfile signature");
            code=Native.NiFpgaDll_Open(options.BitfilePath,"F9744DDE15670B63D27A69F7CB821C89",options.Resource,1,out var fpga);
            Fpga=fpga; CheckRx(code,"Shared FPGA open");
            progress?.Invoke("RFSA, RFSG and shared FPGA sessions ready");
        }
        catch {Dispose();throw;}
    }
    public void CheckTx(int code,string operation)
    {
        if(code>=0) return;
        var message=new StringBuilder(4096);
        if(Rfsg!=0) TxNative.niRFSG_GetError(Rfsg,out _,message.Capacity,message);
        throw new NiException(operation,code,message.ToString());
    }
    private void CheckRx(int code,string operation)
    {
        if(code>=0) return;
        var message=new StringBuilder(4096);
        if(Rfsa!=0) Native.niRFSA_GetError(Rfsa,out _,message.Capacity,message);
        throw new NiException(operation,code,message.ToString());
    }
    public void Dispose()
    {
        if(Rfsg!=0) {TxNative.niRFSG_Abort(Rfsg);TxNative.niRFSG_ConfigureOutputEnabled(Rfsg,0);TxNative.niRFSG_Commit(Rfsg);}
        if(Rfsa!=0) Native.niRFSA_Abort(Rfsa);
        if(Fpga!=0) {Native.NiFpgaDll_Close(Fpga,1);Fpga=0;}
        if(Rfsa!=0) {Native.niRFSA_close(Rfsa);Rfsa=0;}
        if(Rfsg!=0) {TxNative.niRFSG_close(Rfsg);Rfsg=0;}
    }
}

internal static class TxNative
{
    private const string R="niRFSG_64.dll",F="NiFpga.dll";
    [DllImport(R,CharSet=CharSet.Ansi)] internal static extern int niRFSG_InitWithOptions(string resource,ushort query,ushort reset,string options,out uint session);
    [DllImport(R)] internal static extern int niRFSG_close(uint s);
    [DllImport(R)] internal static extern int niRFSG_Abort(uint s);
    [DllImport(R)] internal static extern int niRFSG_Commit(uint s);
    [DllImport(R)] internal static extern int niRFSG_Initiate(uint s);
    [DllImport(R)] internal static extern int niRFSG_ConfigureOutputEnabled(uint s,ushort enabled);
    [DllImport(R)] internal static extern int niRFSG_ConfigureRF(uint s,double frequency,double power);
    [DllImport(R)] internal static extern int niRFSG_ConfigurePowerLevelType(uint s,int type);
    [DllImport(R,CharSet=CharSet.Ansi)] internal static extern int niRFSG_SetAttributeViInt32(uint s,string channel,uint id,int value);
    [DllImport(R,CharSet=CharSet.Ansi)] internal static extern int niRFSG_SetAttributeViReal64(uint s,string channel,uint id,double value);
    [DllImport(R,CharSet=CharSet.Ansi)] internal static extern int niRFSG_GetAttributeViReal64(uint s,string channel,uint id,out double value);
    [DllImport(R,CharSet=CharSet.Ansi)] internal static extern int niRFSG_GetAttributeViBoolean(uint s,string channel,uint id,out ushort value);
    [DllImport(R,CharSet=CharSet.Ansi)] internal static extern int niRFSG_WriteArbWaveform(uint s,string name,int count,double[] i,double[] q,ushort more);
    [DllImport(R,CharSet=CharSet.Ansi)] internal static extern int niRFSG_ClearArbWaveform(uint s,string name);
    [DllImport(R)] internal static extern int niRFSG_ClearAllArbWaveforms(uint s);
    [DllImport(R,CharSet=CharSet.Ansi)] internal static extern int niRFSG_SelectArbWaveform(uint s,string name);
    [DllImport(R,CharSet=CharSet.Ansi)] internal static extern int niRFSG_GetError(uint s,out int code,int length,StringBuilder message);
    [DllImport(F)] internal static extern int NiFpgaDll_WriteFifoU32(uint s,uint fifo,nint data,nuint count,uint timeout,out nuint remaining);
    [DllImport(F)] internal static extern int NiFpgaDll_WriteU32(uint s,uint register,uint value);
    [DllImport(F)] internal static extern int NiFpgaDll_WriteU16(uint s,uint register,ushort value);
    [DllImport(F)] internal static extern int NiFpgaDll_ReadU32(uint s,uint register,out uint value);
    [DllImport(F)] internal static extern int NiFpgaDll_ReadU16(uint s,uint register,out ushort value);
}
