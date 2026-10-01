"""Hardware rate, queue allocation, and missing-client lifecycle regression (RF off)."""
import json,pathlib,time
import numpy as np
import SoapySDR
from bridge_soak import command,status
out=pathlib.Path(__file__).resolve().parent/'artifacts/tx-controls-2.2';out.mkdir(exist_ok=True)
rows=[]
def wait_state(target,timeout=20):
 end=time.monotonic()+timeout
 while time.monotonic()<end:
  s=status()
  if s['tx']['status']==target:return s
  if s['tx']['status']=='FAULT':raise RuntimeError(s['tx'])
  time.sleep(.1)
 raise TimeoutError((target,status()['tx']))
try:
 command('TXSTOP')
 assert command('TXDEFAULTS '+json.dumps({'queue_mi_b':64,'fifo_mi_b':128,'prefill_blocks':16})).startswith('OK')
 for rate in [1e6,30.72e6,60e6,100e6,120e6]:
  d=SoapySDR.Device('driver=vst,resource=RIO0,rf_enabled=false')
  d.setSampleRate(SoapySDR.SOAPY_SDR_TX,0,rate)
  stream=d.setupStream(SoapySDR.SOAPY_SDR_TX,SoapySDR.SOAPY_SDR_CF32)
  try:
   d.activateStream(stream)
   iq=np.full(262144,.25+.25j,np.complex64)
   begin=None;end=time.monotonic()+70;next_status=0
   while time.monotonic()<end:
    rc=d.writeStream(stream,[iq],len(iq),timeoutUs=3000000)
    assert rc.ret>0,rc.ret
    if time.monotonic()<next_status:continue
    next_status=time.monotonic()+.25
    s=status()['tx']
    if s['status']=='FAULT':raise RuntimeError(s)
    if s['status']=='STREAMING':
     if begin is None:begin=s
     elif s['elapsed_s']-begin['elapsed_s']>=5:
      measured=(s['processed_samples']-begin['processed_samples'])/(s['elapsed_s']-begin['elapsed_s'])
      assert abs(measured/rate-1)<.01,(rate,measured)
      assert s['underflows']==0 and not s['rf_enabled'],s
      assert s['queue_capacity']==64*1048576//4 and s['host_fifo_capacity']==128*1048576//4,s
      rows.append({'rate_hz':rate,'measured_hz':measured,'queue_capacity':s['queue_capacity'],'fifo_capacity':s['host_fifo_capacity'],'status':'PASS'})
      break
   else:raise TimeoutError('TX rate test did not complete')
   # Keep the client allocated but cease all writes: this is idle, not failure.
   s=wait_state('WAITING_CLIENT')
   assert not s['tx']['rf_enabled'] and not s['tx']['error'],s['tx']
   rows[-1]['no_data_status']=s['tx']['status']
  finally:
   d.deactivateStream(stream);d.closeStream(stream);del d
  assert status()['tx']['status']=='WAITING_CLIENT'
  command('TXSTOP')
 result={'status':'PASS','checks':rows}
except Exception as ex:
 result={'status':'FAIL','error':repr(ex),'checks':rows};raise
finally:
 command('TXSTOP');command('TXDEFAULTS '+json.dumps({'queue_mi_b':128,'fifo_mi_b':256,'prefill_blocks':16}));(out/'result.json').write_text(json.dumps(result,indent=2));print(json.dumps(result,indent=2))
