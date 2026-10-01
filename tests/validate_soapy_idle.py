"""Native Soapy configuration stays idle and activation applies staged settings."""
import gc,hashlib,json,os,pathlib,time
radio=pathlib.Path.home()/'radioconda'
os.environ['PATH']=str(radio/'Library/bin')+';'+os.environ.get('PATH','')
dll_directory=os.add_dll_directory(str(radio/'Library/bin'))
import numpy as np
import SoapySDR
from bridge_soak import command,status
root=pathlib.Path(__file__).resolve().parents[1]
out=root/'tests/artifacts/soapy-idle-2.2.1';out.mkdir(exist_ok=False)
paths={'VSTHub.exe':root/'dist/VSTHub/VSTHub.exe','vstSupport.dll':root/'soapy-vst/build-vst/vstSupport.dll'}
hashes={name:hashlib.sha256(path.read_bytes()).hexdigest() for name,path in paths.items()}
checks=[];snapshots=[];dev=None;stream=None
result={'status':'FAIL'}
def check(name,condition):
 assert condition,name
 checks.append(name);snapshots.append(status())
try:
 command('TXSTOP');command('STOP')
 dev=SoapySDR.Device('driver=vst,resource=RIO0,backend=dma')
 check('Device construction leaves acquisition stopped',status()['status']=='STOPPED')
 dev.setSampleRate(SoapySDR.SOAPY_SDR_RX,0,30.72e6)
 dev.setFrequency(SoapySDR.SOAPY_SDR_RX,0,2.5e9)
 dev.setGain(SoapySDR.SOAPY_SDR_RX,0,'RefLevel',-20)
 check('Idle getters retain staged rate, frequency and reference',dev.getSampleRate(SoapySDR.SOAPY_SDR_RX,0)==30.72e6 and dev.getFrequency(SoapySDR.SOAPY_SDR_RX,0)==2.5e9 and dev.getGain(SoapySDR.SOAPY_SDR_RX,0,'RefLevel')==-20)
 check('Setting RX parameters does not start DMA',status()['status']=='STOPPED' and not status()['ring']['active'])
 stream=dev.setupStream(SoapySDR.SOAPY_SDR_RX,SoapySDR.SOAPY_SDR_CS16,[0])
 check('Stream setup alone remains stopped',status()['status']=='STOPPED')
 check('RX activation succeeds from STOPPED',dev.activateStream(stream)==0)
 samples=np.empty(65536*2,dtype=np.int16)
 check('Native RX delivers CS16 IQ',dev.readStream(stream,[samples],65536,timeoutUs=1000000).ret>0)
 s=status()
 check('Activation applies staged 30.72 MS/s / 2500 MHz / -20 dBm',s['status']=='RUNNING' and abs(s['applied_rate_hz']-30.72e6)<1 and s['applied_center_hz']==2.5e9 and s['applied_ref_dbm']==-20 and abs(dev.getSampleRate(SoapySDR.SOAPY_SDR_RX,0)-30.72e6)<1)
 dev.deactivateStream(stream);dev.closeStream(stream);stream=None;dev=None;gc.collect()
 command('STOP')
 check('Explicit stop leaves RX/TX stopped and RF off',status()['status']=='STOPPED' and status()['tx']['status']=='STOPPED' and not status()['tx']['rf_enabled'])
 result={'status':'PASS','checks':checks,'snapshots':snapshots,'scope':'Native Soapy idle configuration and explicit activation; short RX regression with RF output off.'}
except Exception as ex:
 result={'status':'FAIL','checks':checks,'snapshots':snapshots,'error':repr(ex)};raise
finally:
 if stream is not None:
  try:dev.deactivateStream(stream);dev.closeStream(stream)
  except Exception:pass
 command('STOP');command('TXSTOP')
 command('CONFIG2 center_hz=2500000000,rate_hz=120000000,reference_level_dbm=-20')
 result['tested_binary_sha256']=hashes
 (out/'result.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
 print(result['status'],checks,flush=True)
