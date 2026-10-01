import pathlib,json,time,argparse
from bridge_soak import command,status
parser=argparse.ArgumentParser();parser.add_argument('--name',default='independence-2.2');args=parser.parse_args()
root=pathlib.Path(__file__).resolve().parents[1];out=root/'tests/artifacts'/args.name;out.mkdir(exist_ok=True)
checks=[];snapshots=[]
def wait_tx():
 for _ in range(100):
  s=status()
  if s['tx']['status']=='STREAMING':return s
  if s['tx']['status']=='FAULT':raise RuntimeError(s['tx']['error'])
  time.sleep(.1)
 raise TimeoutError('TX start')
def check(name,test):
 s=status();snapshots.append(s);assert test(s),name;checks.append(name)
try:
 command('TXSTOP');command('STOP')
 cfg={'source':'file','waveform_path':str(root/'waveforms/nr-tm3.1a-fdd-4x20mhz-120msps.tdms'),'center_hz':2500000000,'rate_hz':120000000,'peak_dbm':-10,'rf_enabled':False}
 assert command('TXSTART '+json.dumps(cfg)).startswith('OK');wait_tx();time.sleep(5)
 check('TDMS TX alone while RX stopped',lambda s:s['status']=='STOPPED' and s['tx']['status']=='STREAMING' and s['tx']['processed_msps']>119 and s['tx']['underflows']==0)
 assert command('CONFIG2 center_hz=2500000000,reference_level_dbm=-20,rate_hz=120000000').startswith('OK')
 assert command('START').startswith('OK');time.sleep(5)
 check('Start RX does not interrupt TX',lambda s:s['status']=='RUNNING' and s['tx']['status']=='STREAMING' and s['tx']['underflows']==0)
 assert command('CONFIG2 reference_level_dbm=-15').startswith('OK');time.sleep(2)
 check('RX reference retune leaves TX streaming',lambda s:s['applied_ref_dbm']==-15 and s['tx']['status']=='STREAMING' and s['tx']['underflows']==0)
 assert command('CONFIG2 rate_hz=60000000').startswith('ERR');checks.append('Shared rate change rejected while TX active')
 command('STOP');time.sleep(5)
 check('Stop RX leaves TX streaming',lambda s:s['status']=='STOPPED' and s['tx']['status']=='STREAMING' and s['tx']['underflows']==0)
 command('START');command('TXSTOP');time.sleep(1)
 check('Stop TX leaves RX running and RF off',lambda s:s['status']=='RUNNING' and s['tx']['status']=='STOPPED' and not s['tx']['rf_enabled'])
 command('CONFIG2 reference_level_dbm=-20')
 result={'status':'PASS','checks':checks,'snapshots':snapshots}
except Exception as ex:
 result={'status':'FAIL','error':repr(ex),'checks':checks,'snapshots':snapshots};raise
finally:
 command('TXSTOP');(out/'result.json').write_text(json.dumps(result,indent=2),encoding='utf-8');print(result['status'],checks,flush=True)
