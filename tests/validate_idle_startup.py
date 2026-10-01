"""Real-device regression for cold initialization with both directions stopped."""
import hashlib,json,pathlib,time
from bridge_soak import command,status
root=pathlib.Path(__file__).resolve().parents[1]
out=root/'tests/artifacts/startup-2.2.1';out.mkdir(exist_ok=True)
checks=[];snapshots=[]
def check(name,predicate):
    s=status();snapshots.append(s);assert predicate(s),name;checks.append(name);return s
def wait_tx():
    for _ in range(150):
        s=status()
        if s['tx']['status']=='STREAMING':return
        if s['tx']['status']=='FAULT':raise RuntimeError(s['tx']['error'])
        time.sleep(.1)
    raise TimeoutError('TX startup')
result={'status':'FAIL'}
try:
    for _ in range(90):
        if status()['status']!='INITIALIZING':break
        time.sleep(.5)
    check('Cold initialization keeps RX/TX stopped, RF off, RX DMA unallocated',lambda s:s['status']=='STOPPED' and s['tx']['status']=='STOPPED' and not s['ring']['active'] and s['ring']['write_idx']==0 and s['fifo_capacity']==0 and not s['tx']['rf_enabled'] and s['capabilities']['tx'])
    time.sleep(3)
    check('No automatic RX start or IQ publication after idle wait',lambda s:s['status']=='STOPPED' and not s['ring']['active'] and s['ring']['write_idx']==0 and s['dma_msps']==0)
    assert command('CONFIG2 center_hz=2500000000,rate_hz=120000000,reference_level_dbm=-20').startswith('OK')
    check('Applying RX settings while idle does not start acquisition',lambda s:s['status']=='STOPPED' and not s['ring']['active'] and s['ring']['write_idx']==0)
    config={'source':'file','waveform_path':str(root/'waveform/nr-tm3.1a-fdd-4x20mhz-120msps.tdms'),'center_hz':2500000000,'rate_hz':120000000,'peak_dbm':-10,'rf_enabled':False,'queue_mi_b':64,'fifo_mi_b':128,'prefill_blocks':16}
    assert command('TXSTART '+json.dumps(config)).startswith('OK');wait_tx();time.sleep(3)
    check('TX alone works at 120 MS/s before RX has ever started',lambda s:s['status']=='STOPPED' and s['ring']['write_idx']==0 and s['tx']['status']=='STREAMING' and 119.5<s['tx']['processed_msps']<120.5 and s['tx']['underflows']==0 and not s['tx']['rf_enabled'])
    assert command('START').startswith('OK');time.sleep(3)
    check('Explicit START runs RX alongside existing TX',lambda s:s['status']=='RUNNING' and s['ring']['active'] and 119<s['dma_msps']<121 and s['tx']['status']=='STREAMING' and s['tx']['underflows']==0)
    assert command('STOP').startswith('OK');time.sleep(1)
    check('Stop RX preserves TX and disables RX shared memory',lambda s:s['status']=='STOPPED' and not s['ring']['active'] and s['tx']['status']=='STREAMING')
    assert command('TXSTOP').startswith('OK');time.sleep(1)
    check('Both stopped after independent controls; no hardware errors',lambda s:s['status']=='STOPPED' and s['tx']['status']=='STOPPED' and not s['tx']['rf_enabled'] and s['tx']['underflows']==0 and s['overflow_count']==0 and s['recoveries']==0 and s['ring']['drops']==0 and not s['log_error'])
    result={'status':'PASS','checks':checks,'snapshots':snapshots,'scope':'Cold startup and explicit direction control regression; not long-duration acceptance.'}
except Exception as ex:
    result={'status':'FAIL','checks':checks,'snapshots':snapshots,'error':repr(ex)};raise
finally:
    command('TXSTOP');command('STOP')
    result['tested_binary_sha256']={name:hashlib.sha256((root/path).read_bytes()).hexdigest() for name,path in [('VSTHub.exe','dist/VSTHub/VSTHub.exe'),('vstSupport.dll','soapy-vst/build-vst/vstSupport.dll')]}
    (out/'result.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
    print(result['status'],checks,flush=True)
