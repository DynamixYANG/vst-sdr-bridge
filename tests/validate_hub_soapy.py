"""Hardware smoke test for the compiled Hub and installed C++ Soapy module."""
import ctypes, json, socket, sys, time, subprocess
from pathlib import Path
import numpy as np
import SoapySDR
OUT=Path(__file__).resolve().parent/'artifacts'
def control(command):
    with socket.create_connection(('127.0.0.1',19788),timeout=3) as s:
        s.settimeout(20); s.sendall((command+'\n').encode())
        return s.makefile(encoding='utf-8').readline().strip()
def status(): return json.loads(control('STATUS'))
def main():
    checks=[]; samples=0; results=[]
    d=SoapySDR.Device('driver=vst')
    stream=d.setupStream(SoapySDR.SOAPY_SDR_RX,SoapySDR.SOAPY_SDR_CF32)
    buf=np.empty(65536,np.complex64)
    def check(value,name):
        assert value,name
        checks.append(name)
    def drain(seconds):
        nonlocal samples
        before=samples
        until=time.monotonic()+seconds
        while time.monotonic()<until:
            result=d.readStream(stream,[buf],buf.size,timeoutUs=100000)
            if result.ret>0: samples+=result.ret
            else: assert result.ret==SoapySDR.SOAPY_SDR_TIMEOUT,result.ret
        check(np.isfinite(buf).all() and np.std(buf)>0,'fresh finite IQ')
        check(samples-before>seconds*110e6,'full-rate actual delivery')
    try:
        check(d.getHardwareInfo()['backend']=='dma','minimal arguments select DMA')
        d.activateStream(stream); drain(2)
        initial=status()
        check(initial['ring']['consumer_active'],'consumer registration')
        code='''import SoapySDR
d=SoapySDR.Device("driver=vst")
s=d.setupStream(SoapySDR.SOAPY_SDR_RX,SoapySDR.SOAPY_SDR_CF32)
try:
 d.activateStream(s)
except RuntimeError:
 print("SECOND_REJECTED")
else:
 raise AssertionError("Second consumer accepted")
finally:
 d.closeStream(s); d.close()
'''
        result=subprocess.run([sys.executable,'-c',code],capture_output=True,timeout=15)
        check(result.returncode==0 and b'SECOND_REJECTED' in result.stdout,'second process consumer rejected')
        for cf,ref in [(500e6,-30),(2.4e9,-10),(5e9,0),(1e9,0)]:
            d.writeSetting('reference_level_dbm',str(ref))
            d.setFrequency(SoapySDR.SOAPY_SDR_RX,0,cf)
            check(d.getFrequency(SoapySDR.SOAPY_SDR_RX,0)==cf,'center hardware readback')
            check(d.getGain(SoapySDR.SOAPY_SDR_RX,0,'RefLevel')==ref,'reference retained over tuning')
            drain(2); results.append(status())
        before=status()
        reply=control('CONFIG2 center_hz=0,reference_level_dbm=-30')
        check(reply.startswith('ERR') and status()['ring']['epoch']==before['ring']['epoch'],'invalid batch atomic')
        check(control('CONFIG2 reference_level_dbm=0').startswith('OK'),'error clear')
        drain(3); final=status()
        check(final['overflow_count']==initial['overflow_count']==0,'no FIFO overflow')
        check(final['recoveries']==initial['recoveries']==0,'no recovery')
        check(final['ring']['drops']==initial['ring']['drops'],'no active ring loss')
        d.deactivateStream(stream)
        time.sleep(.7)
        check(not status()['ring']['consumer_active'],'consumer unregisters on stop')
        report=dict(status='PASS',checks=checks,samples=samples,states=results,final=final)
    except Exception as exc:
        report=dict(status='FAIL',checks=checks,error=str(exc),samples=samples)
        raise
    finally:
        d.deactivateStream(stream); d.closeStream(stream); d.close()
        (OUT/'hub-soapy.json').write_text(json.dumps(report,indent=2))
        print(json.dumps({k:v for k,v in report.items() if k not in ('states','final')}),flush=True)
if __name__=='__main__': main()
