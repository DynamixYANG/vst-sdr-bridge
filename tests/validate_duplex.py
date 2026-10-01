"""Actual NI DMA soak with optional independent Soapy RX consumer.
RF is OFF unless --rf-enabled is explicitly supplied after checking cabling.
"""
import argparse,json,pathlib,socket,threading,time,sys
import numpy as np
parser=argparse.ArgumentParser();parser.add_argument('--seconds',type=float,default=600)
parser.add_argument('--mode',choices=['tx','duplex'],default='duplex');parser.add_argument('--waveform',required=True)
parser.add_argument('--out',required=True);parser.add_argument('--rf-enabled',action='store_true');parser.add_argument('--center-hz',type=float,default=2.5e9);parser.add_argument('--peak-dbm',type=float,default=-30.0)
args=parser.parse_args();out=pathlib.Path(args.out);out.mkdir(parents=True,exist_ok=True)
def command(text):
 with socket.create_connection(('127.0.0.1',19788),timeout=20) as s:
  s.settimeout(30);s.sendall((text+'\n').encode());return s.makefile(encoding='utf-8').readline().strip()
def status():return json.loads(command('STATUS'))
def ok(text):
 r=command(text)
 if not r.startswith('OK'):raise RuntimeError(r)
 return r
stop=threading.Event();rx_samples=0;rx_error=[];rx_ready=threading.Event();rx_finite=False
def receive():
 global rx_samples,rx_finite
 import SoapySDR
 d=None;stream=None
 try:
  d=SoapySDR.Device('driver=vst');stream=d.setupStream(SoapySDR.SOAPY_SDR_RX,SoapySDR.SOAPY_SDR_CF32)
  b=np.empty(262144,np.complex64);d.activateStream(stream);rx_ready.set();counter=0
  while not stop.is_set():
   result=d.readStream(stream,[b],b.size,timeoutUs=200000)
   if result.ret>0:
    rx_samples+=result.ret;counter+=1
    if counter%100==0:
     if not np.isfinite(b[:result.ret]).all():raise RuntimeError('Nonfinite RX IQ')
     rx_finite=bool(np.std(b[:result.ret])>0)
   elif result.ret!=SoapySDR.SOAPY_SDR_TIMEOUT:raise RuntimeError(f'Soapy RX {result.ret}')
 except Exception as ex:rx_error.append(str(ex));rx_ready.set()
 finally:
  if stream is not None:d.deactivateStream(stream);d.closeStream(stream)
  if d is not None:d.close()
report={'status':'FAIL','mode':args.mode,'duration_requested_s':args.seconds,'rf_enabled':args.rf_enabled}
thread=None
try:
 ok('TXSTOP')
 if args.mode=='tx':ok('STOP')
 else:
  ok('START');ok('CONFIG2 center_hz=%s,rate_hz=120000000' % (format(args.center_hz, '.17g'),))
  thread=threading.Thread(target=receive,daemon=True);thread.start()
  if not rx_ready.wait(30) or rx_error:raise RuntimeError(str(rx_error or 'RX start timeout'))
 config={'waveform_path':str(pathlib.Path(args.waveform).resolve()),'rate_hz':120000000,'center_hz':float(args.center_hz),'peak_dbm':float(args.peak_dbm),'rf_enabled':args.rf_enabled}
 ok('TXSTART '+json.dumps(config))
 deadline=time.monotonic()+45
 while True:
  s=status();state=s['tx']['status']
  if state=='STREAMING':break
  if state=='FAULT' or time.monotonic()>deadline:raise RuntimeError(str(s['tx']))
  time.sleep(.25)
 time.sleep(3)
 initial=status();initial_rx=rx_samples;started=time.monotonic()
 if initial['tx']['status']!='STREAMING':raise RuntimeError(str(initial['tx']))
 if args.mode=='duplex':
  before=initial['ring']['epoch'];r=command('CONFIG2 center_hz=%s' % (format(args.center_hz*0.96, '.17g'),))
  assert r.startswith('ERR') and status()['ring']['epoch']==before,'RX retune guard must not interrupt duplex'
 metrics=out/'monitor.jsonl'
 with metrics.open('w',encoding='utf-8') as log:
  while True:
   s=status();log.write(json.dumps(s)+'\n');log.flush()
   if s['tx']['status']!='STREAMING' or s['tx']['underflows'] or s['log_dropped'] or s['log_error']:raise RuntimeError(str(s))
   if args.mode=='duplex' and (s['status']!='RUNNING' or s['overflow_count']!=initial['overflow_count'] or s['recoveries']!=initial['recoveries'] or s['ring']['drops']!=initial['ring']['drops'] or rx_error):raise RuntimeError(str(s))
   elapsed=time.monotonic()-started
   if int(elapsed)%30==0:print(json.dumps({'elapsed_s':round(elapsed,1),'tx_msps':s['tx']['processed_msps'],'rx_msps':s['dma_msps'],'rx_delivered_msps':s['delivered_msps'],'underflows':s['tx']['underflows']}),flush=True)
   if elapsed>=args.seconds:break
   time.sleep(.5)
 final=s;tx_time=final['tx']['elapsed_s']-initial['tx']['elapsed_s']
 tx_rate=(final['tx']['processed_samples']-initial['tx']['processed_samples'])/tx_time
 assert abs(tx_rate/120e6-1)<.001,tx_rate
 report.update(tx_measured_msps=tx_rate/1e6,initial=initial,final=final,elapsed_s=elapsed,rx_read_samples=rx_samples-initial_rx)
 if args.mode=='duplex':
  rx_time=final['elapsed_s']-initial['elapsed_s'];rx_rate=(final['ring']['write_idx']-initial['ring']['write_idx'])/rx_time
  delivered_rate=(final['ring']['delivered_samples']-initial['ring']['delivered_samples'])/rx_time
  assert abs(rx_rate/120e6-1)<.001,rx_rate
  assert abs(delivered_rate/120e6-1)<.002,delivered_rate
  assert rx_finite and not rx_error
  report.update(rx_measured_msps=rx_rate/1e6,rx_delivered_msps=delivered_rate/1e6)
 report['status']='PASS'
except Exception as ex:
 report['error']=repr(ex)
finally:
 try:ok('TXSTOP')
 except Exception as ex:report['shutdown_error']=str(ex);report['status']='FAIL'
 stop.set()
 if thread:thread.join(10)
 report['after_stop']=status()
 (out/'result.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
 print(json.dumps({k:v for k,v in report.items() if k not in ['initial','final','after_stop']}),flush=True)
sys.exit(0 if report['status']=='PASS' else 1)
