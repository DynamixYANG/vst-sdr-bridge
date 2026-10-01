import pathlib,io,json,hashlib,zipfile,sys
import numpy as np
from scipy.io import loadmat
from scipy.signal import resample_poly
from nptdms import TdmsWriter,RootObject,GroupObject,ChannelObject,TdmsFile
root=pathlib.Path(__file__).resolve().parents[1]
reference='nrTestMode_testvec_135_scs_30kHz_BW_20MHz_FDD_NR-FR1-TM3.1a.mat'
raw=(root/'waveform/reference'/reference).read_bytes();m=loadmat(io.BytesIO(raw))
assert int(m['BW'][0,0])==20 and int(m['Scs'][0,0])==30 and m['dm'][0]=='FDD' and m['dlnrref'][0]=='NR-FR1-TM3.1a'
fd=m['fd_slot_data'][0]; nfft=1024; nsc=51*12
assert fd.size==20*14*nsc
td=[]; recovered=[]
for k,symbol in enumerate(fd.reshape(-1,nsc)):
 bins=np.zeros(nfft,complex); bins[(nfft-nsc)//2:(nfft+nsc)//2]=symbol
 x=np.fft.ifft(np.fft.ifftshift(bins))*np.sqrt(nfft)
 recovered.append((np.fft.fftshift(np.fft.fft(x))/np.sqrt(nfft))[(nfft-nsc)//2:(nfft+nsc)//2])
 cp=88 if k%14==0 else 72;td.extend((x[-cp:],x))
err=float(np.max(abs(np.concatenate(recovered)-fd)));assert err<1e-10
x=np.concatenate(td);assert len(x)==307200
y=resample_poly(np.tile(x,3),125,32,window=('kaiser',10));y=y[1200000:2400000]
t=np.arange(len(y))/120e6
mix=sum(np.roll(y,k*30100)*np.exp(2j*np.pi*f*t+1j*k*np.pi/7) for k,f in enumerate([-30e6,-10e6,10e6,30e6]))
mix*=.8/np.max(abs(mix));iq=np.column_stack([np.rint(mix.real*32767),np.rint(mix.imag*32767)]).astype('<i2')
out=root/'waveform/nr-tm3.1a-fdd-4x20mhz-120msps.cs16';out.parent.mkdir(exist_ok=True)
iq.tofile(out.with_suffix('.cs16'))
props={'rate_hz':120e6,'model':'NR-FR1-TM3.1a','duplex':'FDD','modulation':'256QAM','carriers':4,'channel_bandwidth_hz':20e6,'scs_hz':30000.,'carrier_offsets_hz':'-30000000,-10000000,10000000,30000000'}
with TdmsWriter(out.with_suffix('.tdms')) as w:
 w.write_segment([RootObject(props),GroupObject('IQ'),ChannelObject('IQ','I',iq[:,0],{'wf_increment':1/120e6}),ChannelObject('IQ','Q',iq[:,1],{'wf_increment':1/120e6})])
read=TdmsFile.read(out.with_suffix('.tdms'));assert np.array_equal(read['IQ']['I'][:],iq[:,0]) and np.array_equal(read['IQ']['Q'][:],iq[:,1])
meta={**props,'format':'CS16_LE_IQ','samples':len(iq),'sha256':hashlib.sha256(out.with_suffix('.cs16').read_bytes()).hexdigest(),'tdms_sha256':hashlib.sha256(out.with_suffix('.tdms').read_bytes()).hexdigest(),'reference_file':reference,'reference_sha256':hashlib.sha256(raw).hexdigest(),'reference_url':'https://github.com/hahaliu2001/python_5gtoolbox','reference_grid_fft_error':err,'prbs_per_carrier':51,'occupied_subcarriers_hz':18.36e6,'aggregate_nominal_bandwidth_hz':80e6,'papr_db':float(10*np.log10(np.max(abs(mix)**2)/np.mean(abs(mix)**2))),'peak_full_scale':.8,'duration_s':.01,'limitations':'Reference-grid transport and spectrum stimulus; not certified EVM/ACLR or PN23 conformance. Four time/phase shifted copies of the same DL FDD TM3.1a reference.'}
out.with_suffix('.json').write_text(json.dumps(meta,indent=2),encoding='utf-8')
print(json.dumps(meta,indent=2))
