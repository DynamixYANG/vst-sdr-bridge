"""Launch the generated script's real main(), as GRC does; no flowgraph wrapper."""
import argparse,hashlib,json,os,pathlib,subprocess,time
from bridge_soak import command,status,monitor

root=pathlib.Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser();p.add_argument('--seconds',type=int,default=1200);p.add_argument('--name',default='grc-normal-2.2-20min');p.add_argument('--client-start-rx',action='store_true');a=p.parse_args()
out=root/'tests/artifacts'/a.name;out.mkdir(parents=True,exist_ok=False)
radio=pathlib.Path.home()/'radioconda';env=os.environ.copy()
env['PATH']=str(radio/'Library/bin')+';'+str(radio)+';'+env.get('PATH','')
env['QT_PLUGIN_PATH']=str(radio/'Library/plugins');env['SOAPY_SDR_ROOT']=str(radio/'Library')
env.pop('GR_CONF_DEFAULT_BUFFER_SIZE',None)
env['VST_GRC_CAPTURE_DIR']=str(out)
script=root/'examples/grc/vst_bridge_120_duplex.py'
binary_paths={'VSTHub.exe':root/'dist/VSTHub/VSTHub.exe','vstSupport.dll':root/'soapy-vst/build-vst/vstSupport.dll'}
binary_hashes={name:hashlib.sha256(path.read_bytes()).hexdigest() for name,path in binary_paths.items()}
command('TXSTOP');command('STOP' if a.client_start_rx else 'START')
prelaunch=status()
started=time.time()
with (out/'console.log').open('w',encoding='utf-8') as log:
 process=subprocess.Popen([str(radio/'python.exe'),'-u',str(script)],cwd=script.parent,env=env,stdout=log,stderr=subprocess.STDOUT)
 try:
  time.sleep(6)
  for _ in range(120):
   if process.poll() is not None:raise RuntimeError('Flowgraph exited during startup')
   s=status()
   if s['tx']['status']=='STREAMING' and s['ring']['consumer_pid']==process.pid:break
   if s['tx']['status']=='FAULT':raise RuntimeError(s['tx']['error'])
   time.sleep(.25)
  else:raise TimeoutError('Normal entrypoint never reached duplex streaming')
  result=monitor(a.seconds,out)
  result['tested_binary_sha256']=binary_hashes
  result['rx_start_mode']='Native Soapy activation from STOPPED' if a.client_start_rx else 'Explicit START before flowgraph'
  result['prelaunch']=prelaunch
  (out/'close.request').write_text('acceptance completed')
  process.wait(timeout=40)
  console=(out/'console.log').read_text(encoding='utf-8')
  result['normal_entrypoint']={'script':script.name,'pid':process.pid,'exit_code':process.returncode,'GR_CONF_DEFAULT_BUFFER_SIZE':None,'special_flowgraph_wrapper':False,'console_stream_errors':any(x in console for x in ['Soapy sink error','FPGA TX underflow','thread :error:']),'application_captures':len(list(out.glob('grc-render-*.png'))),'rf_off_after_close':not status()['tx']['rf_enabled'],'tx_state_after_close':status()['tx']['status']}
  extra=result['normal_entrypoint']
  if extra['exit_code'] or extra['console_stream_errors'] or not extra['rf_off_after_close'] or extra['tx_state_after_close']!='WAITING_CLIENT':result['status']='FAIL'
  if any(hashlib.sha256(path.read_bytes()).hexdigest()!=binary_hashes[name] for name,path in binary_paths.items()):result['status']='FAIL'
  (out/'result.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
  print(json.dumps(extra),flush=True)
 except Exception as ex:
  (out/'failure.json').write_text(json.dumps({'error':repr(ex)},indent=2));raise
 finally:
  if process.poll() is None:process.terminate();process.wait()
  command('TXSTOP')
raise SystemExit(result['status']!='PASS')
