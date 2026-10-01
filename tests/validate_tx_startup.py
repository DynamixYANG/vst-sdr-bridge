"""Hardware regression: delayed first write is startup; stopped ring is STREAM_ERROR."""
import json,time,pathlib
import numpy as np
import SoapySDR
from bridge_soak import command,status

out=pathlib.Path(__file__).resolve().parent/'artifacts/tx-startup-2.2';out.mkdir(exist_ok=True)
checks=[];snapshots=[]
command('TXSTOP')
d=SoapySDR.Device('driver=vst,resource=RIO0,rf_enabled=false')
stream=d.setupStream(SoapySDR.SOAPY_SDR_TX,SoapySDR.SOAPY_SDR_CF32)
try:
 d.activateStream(stream)
 time.sleep(1.2)
 s=status();snapshots.append(s)
 assert s['tx']['status']=='PREFILLING' and not s['tx']['rf_enabled'],s['tx']
 checks.append('1.2-second first-write delay remains PREFILLING with RF off')
 iq=np.full(262144,.25+.25j,np.complex64)
 end=time.monotonic()+4
 while time.monotonic()<end:
  rc=d.writeStream(stream,[iq],len(iq),timeoutUs=1000000)
  assert rc.ret>0,rc.ret
 s=status();snapshots.append(s)
 assert s['tx']['status']=='STREAMING' and s['tx']['underflows']==0,s['tx']
 checks.append('Delayed producer subsequently streams at 120 MS/s without underflow')
 command('TXSTOP')
 rc=d.writeStream(stream,[iq],len(iq),timeoutUs=100000)
 assert rc.ret==SoapySDR.SOAPY_SDR_STREAM_ERROR,rc.ret
 checks.append('Writing a stopped Hub ring returns STREAM_ERROR, not TIMEOUT')
 (out/'result.json').write_text(json.dumps({'status':'PASS','checks':checks,'snapshots':snapshots},indent=2))
 print(json.dumps(checks,indent=2))
finally:
 d.deactivateStream(stream);d.closeStream(stream);command('TXSTOP')
