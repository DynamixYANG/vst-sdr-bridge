using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Vst.Core;

/// <summary>A worker-owned 1 ms wait without changing the process/system timer resolution.</summary>
internal sealed class HighResolutionWait : IDisposable
{
    private readonly nint timer=CreateWaitableTimerExW(0,null,2,0x00100002);
    public HighResolutionWait()
    {
        if(timer==0) throw new Win32Exception(Marshal.GetLastWin32Error(),"A high-resolution queue timer requires Windows 10 1803 or newer.");
    }
    public void Wait()
    {
        long due=-10000;
        if(!SetWaitableTimer(timer,ref due,0,0,0,false) || WaitForSingleObject(timer,uint.MaxValue)!=0)
            throw new Win32Exception(Marshal.GetLastWin32Error(),"High-resolution queue wait failed.");
    }
    public void Dispose()=>CloseHandle(timer);
    [DllImport("kernel32",CharSet=CharSet.Unicode,ExactSpelling=true,SetLastError=true)] private static extern nint CreateWaitableTimerExW(nint attributes,string? name,uint flags,uint access);
    [DllImport("kernel32",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool SetWaitableTimer(nint timer,ref long due,int period,nint routine,nint argument,[MarshalAs(UnmanagedType.Bool)] bool resume);
    [DllImport("kernel32",SetLastError=true)] private static extern uint WaitForSingleObject(nint handle,uint milliseconds);
    [DllImport("kernel32")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(nint handle);
}
