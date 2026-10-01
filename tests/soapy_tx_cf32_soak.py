"""RF-off sustained Soapy writeStream soak.

Producer loop never calls Hub STATUS (that blocked writeStream in the same
thread and contributed to live-ring starve). Metrics are sampled on a side thread.
"""
from __future__ import annotations

import argparse
import json
import socket
import sys
import threading
import time
from pathlib import Path

import numpy as np
import SoapySDR
from SoapySDR import SOAPY_SDR_CF32, SOAPY_SDR_CS16, SOAPY_SDR_TX

ROOT = Path(__file__).resolve().parents[1]
OLD = str(Path.home() / "radioconda/Library/lib/SoapySDR/modules0.8/vstSupport.dll")
NEW = str(ROOT / "soapy-vst" / "build-vst" / "vstSupport.dll")


def hub(cmd: str) -> str:
    with socket.create_connection(("127.0.0.1", 19788), 20) as sock:
        sock.sendall((cmd + "\n").encode("utf-8"))
        buf = b""
        while not buf.endswith(b"\n"):
            chunk = sock.recv(1 << 20)
            if not chunk:
                break
            buf += chunk
    return buf.decode("utf-8").strip()


def status() -> dict:
    return json.loads(hub("STATUS"))


def load_module() -> None:
    SoapySDR.Device.enumerate("driver=vst")
    err = SoapySDR.unloadModule(OLD)
    if err:
        print("WARN unload installed:", err)
    err = SoapySDR.loadModule(NEW)
    if err:
        raise RuntimeError("load built vstSupport: " + err)


def tone_cs16(n: int) -> np.ndarray:
    period = 120
    phase = 2 * np.pi * (np.arange(period) / period)
    tile = np.empty(period * 2, np.int16)
    tile[0::2] = np.clip(np.rint(np.cos(phase) * 0.5 * 32767), -32768, 32767).astype(np.int16)
    tile[1::2] = np.clip(np.rint(np.sin(phase) * 0.5 * 32767), -32768, 32767).astype(np.int16)
    return np.tile(tile, n // period)


def tone_cf32(n: int) -> np.ndarray:
    period = 120
    phase = 2 * np.pi * (np.arange(period) / period)
    tile = (0.5 * np.exp(1j * phase)).astype(np.complex64)
    return np.tile(tile, n // period)


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--seconds", type=float, default=600.0)
    ap.add_argument("--chunk", type=int, default=262144)
    ap.add_argument("--format", choices=("cs16", "cf32"), default="cs16")
    ap.add_argument("--out", type=Path, default=ROOT / "tests" / "artifacts" / "tx-soak")
    args = ap.parse_args()
    args.out.mkdir(parents=True, exist_ok=True)

    before = status()
    tx0 = before.get("tx") or {}
    if tx0.get("status") in ("STREAMING", "PREFILLING", "CONFIGURING") or tx0.get("rf_enabled"):
        print("TXSTOP clearing", tx0.get("status"), "rf", tx0.get("rf_enabled"))
        print(hub("TXSTOP"))
        time.sleep(0.5)

    print("using installed/default vst module (copy build DLL before run)")
    fmt = SOAPY_SDR_CS16 if args.format == "cs16" else SOAPY_SDR_CF32
    buf = tone_cs16(args.chunk) if args.format == "cs16" else tone_cf32(args.chunk)
    n = (len(buf) // 2) if args.format == "cs16" else len(buf)
    # Never pass numElems larger than the buffer (tile length is floor-aligned to tone period).

    dev = SoapySDR.Device("driver=vst,resource=RIO0,rf_enabled=false,peak_dbm=-30")
    dev.setFrequency(SOAPY_SDR_TX, 0, 2.5e9)
    dev.setSampleRate(SOAPY_SDR_TX, 0, 120e6)
    dev.setGain(SOAPY_SDR_TX, 0, "PeakDbm", -30)
    dev.writeSetting("rf_enabled", "false")
    stream = dev.setupStream(SOAPY_SDR_TX, fmt)
    dev.activateStream(stream)

    stop = threading.Event()
    samples = []
    fault = {"row": None}
    t0 = time.perf_counter()

    def metrics():
        while not stop.wait(1.0):
            try:
                snap = status()
            except Exception as ex:
                print("status err", ex)
                continue
            tx = snap.get("tx") or {}
            row = {
                "t": time.perf_counter() - t0,
                "status": tx.get("status"),
                "source_msps": tx.get("source_msps"),
                "processed_msps": tx.get("processed_msps"),
                "queue_percent": tx.get("queue_percent"),
                "host_fifo_percent": tx.get("host_fifo_percent"),
                "underflows": tx.get("underflows"),
                "rf_enabled": tx.get("rf_enabled"),
                "error": tx.get("error"),
                "queue_capacity": tx.get("queue_capacity"),
                "host_fifo_capacity": tx.get("host_fifo_capacity"),
            }
            samples.append(row)
            print(
                f"t={row['t']:.1f}s src={row['source_msps']} proc={row['processed_msps']} "
                f"q%={row['queue_percent']} fifo%={row['host_fifo_percent']} uf={row['underflows']} st={row['status']}"
            )
            if tx.get("status") == "FAULT" or (tx.get("underflows") or 0) > 0:
                fault["row"] = row
                stop.set()
                return
            if tx.get("rf_enabled"):
                fault["row"] = row
                stop.set()
                return

    th = threading.Thread(target=metrics, name="tx-soak-metrics", daemon=True)
    th.start()
    accepted = 0
    timeouts = 0
    try:
        while not stop.is_set() and (time.perf_counter() - t0) < args.seconds:
            rc = dev.writeStream(stream, [buf], n, timeoutUs=1_000_000)
            rc = getattr(rc, "ret", rc[0] if isinstance(rc, tuple) else rc)
            if rc < 0:
                timeouts += 1
                if timeouts > 200:
                    raise RuntimeError(f"too many timeouts rc={rc} accepted={accepted}")
                continue
            accepted += int(rc)
    finally:
        stop.set()
        th.join(timeout=5)
        try:
            dev.deactivateStream(stream)
            dev.closeStream(stream)
        except Exception as ex:
            print("deactivate:", ex)
        try:
            print(hub("TXSTOP"))
        except Exception as ex:
            print("TXSTOP:", ex)

    after = status()
    tx = after.get("tx") or {}
    elapsed = time.perf_counter() - t0
    src = [s["source_msps"] for s in samples if isinstance(s.get("source_msps"), (int, float)) and s.get("status") == "STREAMING"]
    faulted = fault["row"] is not None or any(
        s.get("status") == "FAULT" or (s.get("underflows") or 0) > 0 for s in samples
    )
    result = {
        "ok": (not faulted) and tx.get("rf_enabled") is False and (min(src) if src else 0) > 100,
        "format": args.format,
        "seconds_requested": args.seconds,
        "elapsed_s": elapsed,
        "accepted_samples": accepted,
        "accepted_msps": accepted / elapsed / 1e6 if elapsed else 0,
        "timeouts": timeouts,
        "source_msps_min": min(src) if src else None,
        "source_msps_avg": (sum(src) / len(src)) if src else None,
        "source_msps_max": max(src) if src else None,
        "fault_row": fault["row"],
        "final_tx": tx,
        "samples": samples,
        "rf_enabled_final": tx.get("rf_enabled"),
        "note": "RF-off soak; metrics on side thread; producer never calls STATUS",
    }
    out = args.out / "result.json"
    out.write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(json.dumps({k: result[k] for k in result if k != "samples"}, indent=2))
    print("wrote", out)
    return 0 if result["ok"] else 1


if __name__ == "__main__":
    sys.exit(main())
