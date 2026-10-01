#!/usr/bin/env python3
"""Tone into vst_tx_sink (Hub live_ring). RF defaults OFF.

Example:
  $env:PYTHONPATH = '...\\gr-vst\\python'
  & C:\\Users\\yang\\radioconda\\python.exe run_tx_tone.py --seconds 10 --peak-dbm -30
"""
from __future__ import annotations

import argparse
import os
import sys
import time

import numpy as np
from gnuradio import analog, blocks, gr


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--center", type=float, default=2.5e9)
    p.add_argument("--rate", type=float, default=120e6)
    p.add_argument("--tone-hz", type=float, default=1e6, help="Baseband tone offset")
    p.add_argument("--peak-dbm", type=float, default=-30.0)
    p.add_argument("--rf-enabled", action="store_true", help="Enable RF (default OFF)")
    p.add_argument("--seconds", type=float, default=10.0)
    p.add_argument("--amplitude", type=float, default=0.8)
    p.add_argument("--hub-host", default="127.0.0.1")
    p.add_argument("--hub-port", type=int, default=19788)
    args = p.parse_args()

    # Import after PYTHONPATH is set by the caller.
    from vst import vst_tx_sink

    tb = gr.top_block("vst_tx_tone")
    src = analog.sig_source_c(args.rate, analog.GR_COS_WAVE, args.tone_hz, args.amplitude, 0)
    # Soft throttle optional for GUI demos; at 120e MS/s rely on TX ring backpressure.
    sink = vst_tx_sink(
        center_freq=args.center,
        samp_rate=args.rate,
        peak_dbm=args.peak_dbm,
        rf_enabled=args.rf_enabled,
        digital_gain=1.0,
        hub_host=args.hub_host,
        hub_port=args.hub_port,
        auto_start=True,
    )
    tb.connect(src, sink)
    print(
        json_dumps(
            {
                "center_hz": args.center,
                "rate_hz": args.rate,
                "tone_hz": args.tone_hz,
                "peak_dbm": args.peak_dbm,
                "rf_enabled": args.rf_enabled,
                "seconds": args.seconds,
            }
        ),
        flush=True,
    )
    tb.start()
    try:
        t0 = time.monotonic()
        while time.monotonic() - t0 < args.seconds:
            time.sleep(0.5)
            st = sink.get_tx_status()
            print(
                {
                    "hub_tx": st.get("status"),
                    "rf_enabled": st.get("rf_enabled"),
                    "processed_msps": st.get("processed_msps"),
                    "underflows": st.get("underflows"),
                    "accepted": sink.get_accepted_samples(),
                    "clips": sink.get_clip_count(),
                    "last_error": sink.get_last_error(),
                },
                flush=True,
            )
            if st.get("status") == "FAULT":
                raise RuntimeError(st)
    finally:
        tb.stop()
        tb.wait()
        final = sink.get_tx_status()
        print({"final_tx": final.get("status"), "rf_enabled": final.get("rf_enabled")}, flush=True)
        if final.get("rf_enabled"):
            print("WARNING: RF still enabled after stop — send TXSTOP", file=sys.stderr)
            sys.exit(2)


def json_dumps(obj):
    import json

    return json.dumps(obj)


if __name__ == "__main__":
    main()
