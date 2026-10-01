"""Read-only monitoring via Hub's cached status; then intentional RX changes.

The product itself writes rotated JSONL. This test writes an independent,
unrotated evidence log, never edits the running producer's status file.
"""
import argparse, ctypes, hashlib, json, socket, time
from pathlib import Path
OUT=Path(__file__).resolve().parent/'artifacts'

def command(text,port=19788):
    with socket.create_connection(('127.0.0.1',port),timeout=3) as s:
        s.settimeout(20); s.sendall((text+'\n').encode())
        return s.makefile(encoding='utf-8').readline().strip()
def status(): return json.loads(command('STATUS'))

def main():
    ap=argparse.ArgumentParser(); ap.add_argument('--seconds',type=float,default=600); args=ap.parse_args()
    run=OUT/('hub-gqrx-'+time.strftime('%Y%m%d-%H%M%S')); run.mkdir(parents=True)
    (OUT/'latest-gqrx.txt').write_text(str(run))
    rows=[]; tunes=[]; front=[]; checks={}
    def snap(phase):
        s=status(); row=dict(monotonic=time.monotonic(),phase=phase,data=s)
        rows.append(row); log.write(json.dumps(row)+'\n'); return s
    with (run/'monitor.jsonl').open('w',encoding='utf-8',buffering=1) as log:
        try:
            assert command('U DSP 1',7356)=='RPRT 0'
            time.sleep(3)
            first=snap('soak'); start=time.monotonic(); next_report=60
            while time.monotonic()-start<args.seconds:
                time.sleep(.25); now=snap('soak')
                if time.monotonic()-start>=next_report:
                    print(json.dumps(dict(phase='soak',elapsed=round(time.monotonic()-start,1),dma=now['dma_msps'],delivered=now['delivered_msps'],drops=now['ring']['drops']-first['ring']['drops'],fifo=now['fifo_percent'])),flush=True)
                    next_report+=60
            soak=rows.copy()
            for frequency in [500e6,2.4e9,5e9,100e6,1e9]*2:
                before=snap('tune_before'); previous=float(command('f',7356)); rate=before['applied_rate_hz']
                assert abs(frequency-previous)>rate
                offset=(1 if frequency>previous else -1)*round(.072*rate)
                target=frequency-offset-float(command('LNB_LO',7356)); t=time.monotonic()
                assert command('F '+str(int(frequency)),7356)=='RPRT 0'
                while True:
                    after=snap('tune_wait')
                    if after['applied_center_hz']==target and after['ring']['epoch']>before['ring']['epoch'] and after['ring']['delivered_samples']>before['ring']['delivered_samples']: break
                    if time.monotonic()-t>5: raise AssertionError('Hardware tune/readback timed out')
                    time.sleep(.02)
                tunes.append(dict(requested=frequency,applied=target,elapsed=time.monotonic()-t,epoch=after['ring']['epoch']))
                for _ in range(12): time.sleep(.25); snap('tune_hold')
                print(json.dumps(tunes[-1]),flush=True)
            for ref in [-30,-10,0,-30,0]:
                before=snap('frontend_before'); t=time.monotonic()
                assert command(f'CONFIG2 reference_level_dbm={ref},preamp_mode=auto').startswith('OK ')
                after=snap('frontend_ack'); assert after['applied_ref_dbm']==ref
                assert after['ring']['epoch']>before['ring']['epoch']
                front.append(dict(ref=ref,actual_preamp=after['preamp_actual'],elapsed=time.monotonic()-t))
                for _ in range(8): time.sleep(.25); snap('frontend_hold')
            final=snap('final'); first_row,last_row=soak[0],soak[-1]; elapsed=last_row['monotonic']-first_row['monotonic']
            w0,w1=first_row['data']['ring'],last_row['data']['ring']
            ingest=(w1['write_idx']-w0['write_idx'])/elapsed/1e6
            delivered=(w1['delivered_samples']-w0['delivered_samples'])/elapsed/1e6
            steady=[r['data'] for r in rows if r['phase'] in ('soak','tune_hold','frontend_hold','final')]
            checks=dict(soak_complete=elapsed>=args.seconds,full_ingest=119.8<ingest<120.2,full_delivery=119.8<delivered<120.2,
                no_drops=final['ring']['drops']==first['ring']['drops'],no_skipping=final['ring']['display_skipped']==first['ring']['display_skipped'],
                no_overflow=final['overflow_count']==0,no_recovery=final['recoveries']==0,
                always_active=all(x['ring']['active'] for x in steady),producer_running=all(x['status']=='RUNNING' for x in steady),
                producer_heartbeat=max(x['ring']['heartbeat_age_ms'] for x in steady)<250,
                consumer_heartbeat=max(x['ring']['consumer_heartbeat_age_ms'] for x in steady)<250,
                publish_gap=final['max_publish_gap_s']<.25,
                all_tunes=len(tunes)==10,all_frontend=len(front)==5,
                log_no_drop=final['log_dropped']==0,log_no_error=not final['log_error'])
            result=dict(status='PASS' if all(checks.values()) else 'FAIL',checks=checks,seconds=elapsed,ingest_msps=ingest,delivered_msps=delivered,
                drops_delta=final['ring']['drops']-first['ring']['drops'],max_publish_gap_s=final['max_publish_gap_s'],
                max_consumer_heartbeat_ms=max(x['ring']['consumer_heartbeat_age_ms'] for x in steady),
                max_fifo_percent=max(x['fifo_percent'] for x in steady),max_ring_percent=max(x['ring']['buffer_percent'] for x in steady),
                cpu_cores_average=sum(x['cpu_cores'] for x in steady)/len(steady),rows=len(rows),tunes=tunes,frontend=front,
                initial=first,final=final)
            (run/'summary.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
            print(json.dumps({k:v for k,v in result.items() if k not in ('initial','final','tunes','frontend')},indent=2),flush=True)
            assert result['status']=='PASS'
        except BaseException as exc:
            (run/'failure.json').write_text(json.dumps(dict(error=str(exc),rows=len(rows),checks=checks,tunes=tunes,frontend=front),indent=2))
            raise
if __name__=='__main__': main()
