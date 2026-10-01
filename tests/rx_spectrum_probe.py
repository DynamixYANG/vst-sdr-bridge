"""Non-consuming, read-only snapshot of the Hub ring, independent RF spectrum evidence."""
import ctypes as c,struct,json,pathlib,argparse,time
import numpy as np
from bridge_soak import status
k=c.WinDLL('kernel32',use_last_error=True)
k.OpenFileMappingW.argtypes=[c.c_ulong,c.c_int,c.c_wchar_p];k.OpenFileMappingW.restype=c.c_void_p
k.MapViewOfFile.argtypes=[c.c_void_p,c.c_ulong,c.c_ulong,c.c_ulong,c.c_size_t];k.MapViewOfFile.restype=c.c_void_p
k.UnmapViewOfFile.argtypes=[c.c_void_p];k.CloseHandle.argtypes=[c.c_void_p]
k.OpenMutexW.argtypes=[c.c_ulong,c.c_int,c.c_wchar_p];k.OpenMutexW.restype=c.c_void_p
k.WaitForSingleObject.argtypes=[c.c_void_p,c.c_ulong];k.ReleaseMutex.argtypes=[c.c_void_p]
def capture(n=262144):
 name='Local\\vst_live_v2';h=k.OpenFileMappingW(4,False,name)
 if not h:raise c.WinError(c.get_last_error())
 v=k.MapViewOfFile(h,4,0,0,0);mutex=k.OpenMutexW(0x100001,False,name+'.lock')
 if not v or not mutex:raise c.WinError(c.get_last_error())
 try:
  if k.WaitForSingleObject(mutex,500) not in (0,128):raise TimeoutError('RX snapshot mutex')
  try:
   header=c.string_at(v,256);cap,w=struct.unpack_from('<QQ',header,16)
   assert struct.unpack_from('<I',header)[0]==0x50313230 and w>=n
   pos=(w-n)%cap;first=min(n,cap-pos)
   raw=c.string_at(v+256+pos*4,first*4)
   if first<n:raw+=c.string_at(v+256,(n-first)*4)
  finally:k.ReleaseMutex(mutex)
 finally:k.UnmapViewOfFile(v);k.CloseHandle(mutex);k.CloseHandle(h)
 iq=np.frombuffer(raw,dtype='<i2').reshape(-1,2).astype(np.float32)/32768
 return iq[:,0]+1j*iq[:,1]
def probe(out):
 out=pathlib.Path(out);out.parent.mkdir(parents=True,exist_ok=True);s=status();nfft=8192;win=np.hanning(nfft);powers=[];peaks=[]
 for _ in range(8):
  x=capture();peaks.append(float(np.max(abs(x))))
  powers.append(np.mean(abs(np.fft.fftshift(np.fft.fft(x.reshape(-1,nfft)*win,axis=1),axes=1))**2,axis=0)/sum(win)**2);time.sleep(.1)
 power=np.mean(powers,axis=0);db=10*np.log10(np.maximum(power,1e-30));f=np.fft.fftshift(np.fft.fftfreq(nfft,1/s['applied_rate_hz']))
 bands={str(offset):float(10*np.log10(np.mean(power[abs(f-offset)<8e6]))) for offset in [-30e6,-10e6,10e6,30e6]}
 result={'source':'Read-only latest RX shared-memory IQ snapshot; does not modify consumer cursors','status':s,'peak_full_scale':max(peaks),'peak_offset_hz':float(f[np.argmax(power)]),'peak_bin_dbfs':float(max(db)),'median_bin_dbfs':float(np.median(db)),'carrier_band_mean_bin_dbfs':bands,'fft_size':nfft}
 out.with_suffix('.json').write_text(json.dumps(result,indent=2),encoding='utf-8');np.savez_compressed(out.with_suffix('.npz'),frequency_hz=f,dbfs=db)
 import matplotlib;matplotlib.use('Agg');import matplotlib.pyplot as plt
 fig,ax=plt.subplots(figsize=(13,5));ax.plot((f+s['applied_center_hz'])/1e6,db,lw=.7);ax.set(xlabel='RF frequency (MHz)',ylabel='FFT bin power (dBFS)',title=f"Measured antenna RX | {s['applied_rate_hz']/1e6:.0f} MS/s | TX RF {'ON' if s['tx']['rf_enabled'] else 'OFF'} | RX ref {s['applied_ref_dbm']} dBm",ylim=(-145,5));ax.grid(alpha=.3);fig.tight_layout();fig.savefig(out.with_suffix('.png'),dpi=150);plt.close(fig)
 print(json.dumps({k:v for k,v in result.items() if k!='status'},indent=2));return result
if __name__=='__main__':
 p=argparse.ArgumentParser();p.add_argument('--out',required=True);a=p.parse_args();probe(a.out)
