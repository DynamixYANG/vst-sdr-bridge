"""Direct TDMS TX + actual GQRX GUI consumer, including RF-on/off spectrum checks."""
import os,pathlib,subprocess,time,json,sys,argparse
from PyQt5.QtCore import QSettings
from bridge_soak import command,status,monitor
from rx_spectrum_probe import probe
from review_rf_spectrum import compare
root=pathlib.Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser();p.add_argument('--seconds',type=int,default=1200);p.add_argument('--name',default='gqrx-tdms-2.2-20min');a=p.parse_args()
out=root/'tests/artifacts'/a.name;out.mkdir(parents=True,exist_ok=False)
config=out/'gqrx.conf';q=QSettings(str(config),QSettings.IniFormat)
settings={'configversion':4,'crashed':False,'input/device':'soapy=0,driver=vst,resource=RIO0','input/sample_rate':120000000,'input/frequency':2500000000,'input/gains':{'RefLevel':-200},'input/antenna':'RF_IN','receiver/demod':'Demod Off','receiver/frequency':2500000000,'receiver/filter_offset':0,'receiver/sql_enabled':False,'fft/fft_window':'hann','fft/fft_size':16384,'fft/fft_rate':20,'fft/averaging':50,'fft/panadapter_min_db':-130,'fft/panadapter_max_db':-30,'fft/waterfall_min_db':-115,'fft/waterfall_max_db':-65,'fft/plot_y_unit':'dbfs','remote_control/enabled':True,'remote_control/allowed_hosts':'127.0.0.1','audio/gain':-60}
for key,value in settings.items():q.setValue(key,value)
q.sync();del q
env=os.environ.copy();radio=pathlib.Path.home()/'radioconda';env['PATH']=str(radio/'Library/bin')+';'+str(radio)+';'+env.get('PATH','');env['QT_PLUGIN_PATH']=str(radio/'Library/plugins');env['GR_CONF_DEFAULT_BUFFER_SIZE']='1048576';env['SOAPY_SDR_ROOT']=str(radio/'Library');env['VST_GQRX_CAPTURE_DIR']=str(out)
command('TXSTOP');command('CONFIG2 center_hz=2500000000,rate_hz=120000000,reference_level_dbm=-20,preamp_mode=auto')
exe=root/'work/bridge-upgrade/gqrx-capture/build/src/gqrx.exe'
log=(out/'gqrx.log').open('w',encoding='utf-8');process=subprocess.Popen([str(exe),'-c',str(config)],env=env,stdout=log,stderr=subprocess.STDOUT)
result=None
try:
 for _ in range(120):
  if process.poll() is not None:raise RuntimeError('GQRX exited: '+str(process.returncode))
  try:
   if command('U DSP 1',7356)=='RPRT 0':break
  except OSError:pass
  time.sleep(.5)
 else:raise TimeoutError('GQRX DSP remote control')
 time.sleep(5);command('L RefLevel_GAIN -20',7356);command('CONFIG2 reference_level_dbm=-20');time.sleep(2)
 baseline=probe(out/'rx-rf-off-before')
 cfg={'source':'file','waveform_path':str(root/'waveforms/nr-tm3.1a-fdd-4x20mhz-120msps.tdms'),'center_hz':2500000000,'rate_hz':120000000,'peak_dbm':-10,'rf_enabled':True}
 assert command('TXSTART '+json.dumps(cfg)).startswith('OK')
 for _ in range(100):
  s=status()
  if s['tx']['status']=='STREAMING':break
  if s['tx']['status']=='FAULT':raise RuntimeError(s['tx']['error'])
  time.sleep(.2)
 time.sleep(5);on=probe(out/'rx-four-carrier-on');result=monitor(a.seconds,out)
 end=probe(out/'rx-four-carrier-end');command('TXSTOP');time.sleep(2);off=probe(out/'rx-rf-off-after')
 contrast={key:end['carrier_band_mean_bin_dbfs'][key]-off['carrier_band_mean_bin_dbfs'][key] for key in end['carrier_band_mean_bin_dbfs']}
 result['rf_spectrum_checks']={'band_on_off_db':contrast,'all_four_above_off_by_10db':all(v>10 for v in contrast.values()),'rf_off_after_stop':not status()['tx']['rf_enabled'],'gqrx_running':process.poll() is None,'gqrx_frames':len(list(out.glob('gqrx-render-*.png')))}
 result['spectral_review']=compare(out)
 if not all([result['spectral_review']['pass'],result['rf_spectrum_checks']['rf_off_after_stop'],result['rf_spectrum_checks']['gqrx_running']]):result['status']='FAIL'
 (out/'result.json').write_text(json.dumps(result,indent=2),encoding='utf-8');print(json.dumps(result['rf_spectrum_checks'],indent=2),flush=True)
except Exception as ex:
 (out/'failure.json').write_text(json.dumps({'error':repr(ex)},indent=2),encoding='utf-8');raise
finally:
 command('TXSTOP')
 try:command('U DSP 0',7356)
 except OSError:pass
 (out/'close.request').write_text('acceptance finished',encoding='utf-8')
 log.close()
if result is not None:raise SystemExit(result['status']!='PASS')
