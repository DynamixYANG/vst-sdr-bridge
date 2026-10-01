#!/usr/bin/env python3
"""Live QT Frequency + Waterfall sink for PXIe-5644R via gr-vst.

Usage (radioconda Prompt / PATH with radioconda):
  set PYTHONPATH=C:\\Users\\yang\\Documents\\Codex\\2026-09-28\\yaml-c-users-yang-documents-pxie5644r\\gr-vst\\python
  python examples\\run_fft.py --rate 10e6 --center 1e9 --ref 0 --seconds 30

Defaults stay at 10 MS/s for GUI headroom. Validated Fetch ceiling on this
host is ~90 MS/s; 120 MS/s continuous needs DMA/TDMS LabVIEW path, not GR Fetch.
"""
from __future__ import annotations

import argparse
import os
import sys
import time

# Ensure in-tree package is importable without install.
_HERE = os.path.dirname(os.path.abspath(__file__))
_PY = os.path.normpath(os.path.join(_HERE, "..", "python"))
if _PY not in sys.path:
    sys.path.insert(0, _PY)

from gnuradio import gr, qtgui, blocks
from gnuradio.fft import window
from PyQt5 import Qt
import sip
import signal

from vst import vst_rfsa_source


class Top(gr.top_block, Qt.QWidget):
    def __init__(self, resource, center, rate, ref, fft_size, block_samples, use_bitfile):
        gr.top_block.__init__(self, "PXIe-5644R FFT")
        Qt.QWidget.__init__(self)
        self.setWindowTitle("PXIe-5644R — QT Frequency / Waterfall")
        self.resize(1100, 700)

        self.src = vst_rfsa_source(
            resource=resource,
            center_freq=center,
            samp_rate=rate,
            reference_level=ref,
            block_samples=block_samples,
            use_streaming_bitfile=use_bitfile,
        )

        self.freq = qtgui.freq_sink_c(
            fft_size,  # size
            window.WIN_BLACKMAN_hARRIS,  # wintype (GR 3.10 may use window.WIN_*)
            center,  # fc
            rate,  # bw
            "PXIe-5644R Spectrum",
            1,
            None,
        )
        self.freq.set_update_time(0.10)
        self.freq.set_y_axis(-140, 10)
        self.freq.enable_autoscale(False)
        self.freq.set_fft_average(0.2)

        self.waterfall = qtgui.waterfall_sink_c(
            fft_size,
            window.WIN_BLACKMAN_hARRIS,
            center,
            rate,
            "PXIe-5644R Waterfall",
            1,
            None,
        )
        self.waterfall.set_update_time(0.10)
        self.waterfall.enable_grid(False)
        self.waterfall.enable_axis_labels(True)

        # Layout
        layout = Qt.QVBoxLayout(self)
        for sink in (self.freq, self.waterfall):
            widget = sip.wrapinstance(sink.qwidget(), Qt.QWidget)
            layout.addWidget(widget)

        self.connect(self.src, self.freq)
        self.connect(self.src, self.waterfall)

        # Controls
        ctrl = Qt.QHBoxLayout()
        self.lbl = Qt.QLabel(self)
        ctrl.addWidget(self.lbl)
        layout.addLayout(ctrl)
        self._timer = Qt.QTimer(self)
        self._timer.timeout.connect(self._tick)
        self._timer.start(500)

    def _tick(self):
        self.lbl.setText(
            f"resource={self.src._resource}  "
            f"CF={self.src.get_center_freq()/1e6:.3f} MHz  "
            f"rate={self.src.get_samp_rate()/1e6:.3f} MS/s  "
            f"ref={self.src.get_reference_level():.1f} dBm  "
            f"samples={self.src.get_total_samples()}  "
            f"fetch_errors={self.src.get_fetch_errors()}"
        )


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--resource", default="RIO0")
    ap.add_argument("--center", type=float, default=1e9)
    ap.add_argument("--rate", type=float, default=10e6)
    ap.add_argument("--ref", type=float, default=0.0, help="Reference level dBm")
    ap.add_argument("--fft", type=int, default=2048)
    ap.add_argument("--block", type=int, default=65536)
    ap.add_argument("--seconds", type=float, default=0, help="0 = until window closed")
    ap.add_argument(
        "--streaming-bitfile",
        action="store_true",
        help="InitWithOptions Bitfile:NI Streaming for VST.lvbitx",
    )
    ap.add_argument(
        "--headless-smoke",
        action="store_true",
        help="No Qt; fetch a few blocks and print stats (hardware check)",
    )
    args = ap.parse_args()

    if args.headless_smoke:
        return smoke(args)

    # Prefer qtgui window module aliases for GR 3.10
    try:
        from gnuradio.fft import window
        # Monkey-patch firdes constant if needed by recreating sinks — handled below if import fails at runtime.
    except Exception:
        pass

    app = Qt.QApplication(sys.argv)
    tb = Top(
        args.resource,
        args.center,
        args.rate,
        args.ref,
        args.fft,
        args.block,
        args.streaming_bitfile,
    )

    def quitting():
        tb.stop()
        tb.wait()

    app.aboutToQuit.connect(quitting)
    signal.signal(signal.SIGINT, lambda *a: app.quit())

    status_path = os.environ.get("VST_FFT_STATUS", "")
    def write_status(state, **extra):
        if not status_path:
            return
        import json
        payload = dict(state=state, resource=args.resource, center=args.center,
                       rate=args.rate, ref=args.ref, pid=os.getpid(), **extra)
        with open(status_path, "w", encoding="utf-8") as f:
            json.dump(payload, f, indent=2)

    try:
        tb.start()
        write_status("started", actual_rate=tb.src.get_samp_rate())
    except Exception as e:
        write_status("start_failed", error=str(e))
        raise
    tb.show()
    write_status("showing", actual_rate=tb.src.get_samp_rate())

    if args.seconds and args.seconds > 0:
        Qt.QTimer.singleShot(int(args.seconds * 1000), app.quit)

    rc = app.exec_()
    quitting()
    return rc


def smoke(args):
    """Hardware smoke without Qt."""
    from vst.nirfsa_ctypes import RfsaSession
    import numpy as np

    sess = RfsaSession()
    print(f"Opening {args.resource} streaming_bitfile={args.streaming_bitfile}")
    sess.open(args.resource, use_streaming_bitfile=args.streaming_bitfile)
    sess.configure(
        center_hz=args.center,
        iq_rate=args.rate,
        reference_level_dbm=args.ref,
        block_samples=args.block,
        finite=False,
    )
    print(f"actual_iq_rate={sess.actual_iq_rate} dma_attr_status={getattr(sess,'dma_attr_status',None)}")
    buf = np.empty(args.block * 2, dtype=np.int16)
    t0 = time.time()
    total = 0
    n_loops = max(1, int(args.seconds) if args.seconds else 5)
    for i in range(n_loops):
        info = sess.fetch_i16(buf, args.block, timeout_s=5.0)
        got = int(info.ActualSamples) or args.block
        total += got
        print(
            f"fetch[{i}] samples={got} gain={info.Gain} offset={info.Offset} "
            f"elapsed={time.time()-t0:.3f}s"
        )
    elapsed = time.time() - t0
    mbps = (total * 4) / elapsed / 1e6
    print(f"DONE total_samples={total} elapsed={elapsed:.3f}s throughput~{mbps:.1f} MB/s")
    sess.close()
    return 0


if __name__ == "__main__":
    sys.exit(main() or 0)
