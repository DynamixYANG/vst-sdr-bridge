"""Run the grcc-generated Qt GUI unchanged; grab its actual rendered spectrum widgets."""
import sys,pathlib,threading,time,json
from PyQt5 import Qt
root=pathlib.Path(__file__).resolve().parents[1];sys.path.insert(0,str(root/'examples/grc'))
from vst_bridge_120_duplex import vst_bridge_120_duplex,snippets_main_after_init
from bridge_soak import monitor,command,status
out=root/'tests/artifacts/grc-120-20min';out.mkdir(parents=True,exist_ok=True)
app=Qt.QApplication(sys.argv);tb=vst_bridge_120_duplex();snippets_main_after_init(tb);tb.resize(1400,900);tb.show();tb.start();tb.flowgraph_started.set()
result={};start=time.monotonic();captured=set()
def worker():
 try:
  time.sleep(12);result.update(monitor(1200,out))
 finally:command('TXSTOP')
threading.Thread(target=worker,daemon=True).start()
def tick():
 elapsed=time.monotonic()-start
 for t in [10,600,1205]:
  if elapsed>=t and t not in captured:
   tb.grab().save(str(out/f'grc-spectrum-{t:04d}s.png'));captured.add(t)
 if result:
  tb.grab().save(str(out/'grc-spectrum-final.png'));tb.stop();tb.wait();app.quit()
timer=Qt.QTimer();timer.timeout.connect(tick);timer.start(500)
app.exec_()
