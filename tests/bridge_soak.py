"""Observe the actual GUI consumer and hardware counters without taking an IQ consumer slot."""
import argparse,json,socket,time,pathlib,datetime
def command(text,port=19788):
 with socket.create_connection(('127.0.0.1',port),timeout=10) as s:
  s.settimeout(25);s.sendall((text+'\n').encode());return s.makefile().readline().strip()
def status():return json.loads(command('STATUS'))
def monitor(seconds,out):
 out=pathlib.Path(out);out.mkdir(parents=True,exist_ok=True)
 rows=[];failure=None;start=time.monotonic()
 try:
  with (out/'monitor.jsonl').open('w',encoding='utf-8',buffering=1) as log:
   while True:
    s=status();now=time.monotonic();row={'elapsed':now-start,'utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'data':s};rows.append(row);log.write(json.dumps(row)+'\n')
    if s['tx']['status']!='STREAMING' or s['status']!='RUNNING' or not s['ring']['consumer_active']:raise RuntimeError('TX/RX/client is not streaming')
    if s['tx']['underflows'] or s['overflow_count'] or s['recoveries'] or s['log_dropped'] or s['log_error']:raise RuntimeError('Hardware/bridge error counter nonzero')
    if s['ring']['drops']!=rows[0]['data']['ring']['drops']:raise RuntimeError('RX samples dropped')
    if len(rows)>1 and s['ring']['delivered_samples']<=rows[-2]['data']['ring']['delivered_samples']:raise RuntimeError('Client stalled')
    if now-start>=seconds:break
    time.sleep(1)
 except Exception as ex:failure=repr(ex)
 a,b=rows[0]['data'],rows[-1]['data'];elapsed=rows[-1]['elapsed']-rows[0]['elapsed']
 # Counters are published by independent workers. Use each counter's own
 # snapshot timestamp; TCP observation time includes up to 0.5 s of sample age.
 rx_elapsed=b['elapsed_s']-a['elapsed_s'];tx_elapsed=b['tx']['elapsed_s']-a['tx']['elapsed_s']
 rates={'rx_dma_msps':(b['ring']['write_idx']-a['ring']['write_idx'])/max(rx_elapsed,.001)/1e6,'rx_delivered_msps':(b['ring']['delivered_samples']-a['ring']['delivered_samples'])/max(rx_elapsed,.001)/1e6,'tx_processed_msps':(b['tx']['processed_samples']-a['tx']['processed_samples'])/max(tx_elapsed,.001)/1e6}
 checks={'duration':elapsed>=seconds,'rates':all(119.5<v<120.5 for v in rates.values()),'no_failure':failure is None,'no_rx_drop':b['ring']['drops']==a['ring']['drops'],'no_skip':b['ring']['display_skipped']==a['ring']['display_skipped'],'rf_on':all(r['data']['tx']['rf_enabled'] for r in rows)}
 result={'status':'PASS' if all(checks.values()) else 'FAIL','seconds':elapsed,'checks':checks,'rates':rates,'error':failure,'rows':len(rows),'initial':a,'final':b,'counter_intervals_s':{'rx':rx_elapsed,'tx':tx_elapsed}}
 (out/'result.json').write_text(json.dumps(result,indent=2),encoding='utf-8');print(json.dumps({k:v for k,v in result.items() if k not in ('initial','final')},indent=2),flush=True)
 return result
if __name__=='__main__':
 p=argparse.ArgumentParser();p.add_argument('--seconds',type=int,default=1200);p.add_argument('--out',required=True);a=p.parse_args();r=monitor(a.seconds,a.out);raise SystemExit(r['status']!='PASS')
