"""RF-off full-rate smoke: Soapy writeStream TX -> Hub Local\vst_tx_v1.

Loads the freshly built vstSupport.dll (GRC may still lock the installed copy).
Does not enable RF. TX center is independent of the Hub RX center.
"""
from __future__ import annotations

import json
from pathlib import Path
import socket
import sys
import time

import numpy as np
import SoapySDR
from SoapySDR import SOAPY_SDR_CF32, SOAPY_SDR_CS16, SOAPY_SDR_RX, SOAPY_SDR_TX

OLD = str(Path.home() / "radioconda/Library/lib/SoapySDR/modules0.8/vstSupport.dll")
NEW = str(Path(__file__).resolve().parents[1] / "soapy-vst/build-vst/vstSupport.dll")
TX_CENTER = 2.45e9


def status() -> dict:
    with socket.create_connection(("127.0.0.1", 19788), 5) as sock:
        sock.sendall(b"STATUS\n")
        buf = b""
        while not buf.endswith(b"\n"):
            chunk = sock.recv(1 << 20)
            if not chunk:
                break
            buf += chunk
    return json.loads(buf.decode("utf-8"))


def load_new_module() -> None:
    SoapySDR.Device.enumerate("driver=vst")
    unloaded = SoapySDR.unloadModule(OLD)
    if unloaded:
        raise RuntimeError("unload installed vstSupport: " + unloaded)
    loaded = SoapySDR.loadModule(NEW)
    if loaded:
        raise RuntimeError("load built vstSupport: " + loaded)
    result = SoapySDR.getLoaderResult(NEW)
    err = result["vst"] if "vst" in result else ""
    if err:
        raise RuntimeError("vst module: " + err)


def tone_cs16(n: int) -> np.ndarray:
    period = 120  # 1 MHz at 120 MS/s
    phase = 2 * np.pi * (np.arange(period) / period)
    tile = np.empty(period * 2, np.int16)
    tile[0::2] = np.clip(np.rint(np.cos(phase) * 0.5 * 32767), -32768, 32767).astype(np.int16)
    tile[1::2] = np.clip(np.rint(np.sin(phase) * 0.5 * 32767), -32768, 32767).astype(np.int16)
    reps = n // period
    return np.tile(tile, reps)


def tone_cf32(n: int) -> np.ndarray:
    period = 120
    phase = 2 * np.pi * (np.arange(period) / period)
    tile = (0.5 * np.exp(1j * phase)).astype(np.complex64)
    return np.tile(tile, n // period)


def pump(dev, stream, buf, seconds: float) -> tuple[int, float, int]:
    n = len(buf) if buf.dtype == np.complex64 else len(buf) // 2
    accepted = 0
    timeouts = 0
    t0 = time.perf_counter()
    while time.perf_counter() - t0 < seconds:
        rc = dev.writeStream(stream, [buf], n, timeoutUs=1_000_000)
        rc = getattr(rc, "ret", rc[0] if isinstance(rc, tuple) else rc)
        if rc < 0:
            timeouts += 1
            if timeouts > 80:
                raise RuntimeError(f"writeStream failed rc={rc} after {accepted} samples")
            continue
        accepted += int(rc)
    return accepted, time.perf_counter() - t0, timeouts


def main() -> int:
    before = status()
    tx0 = before.get("tx") or {}
    if tx0.get("status") in ("STREAMING", "PREFILLING", "CONFIGURING") or tx0.get("rf_enabled"):
        print("SKIP existing TX", tx0.get("status"), "rf", tx0.get("rf_enabled"))
        return 3
    rx_center = before.get("applied_center_hz")
    load_new_module()
    dev = SoapySDR.Device("driver=vst,resource=RIO0,rf_enabled=false,peak_dbm=-30")
    if dev.getNumChannels(SOAPY_SDR_TX) != 1 or dev.getNumChannels(SOAPY_SDR_RX) != 1:
        raise RuntimeError("expected RX=1 TX=1")
    dev.setFrequency(SOAPY_SDR_TX, 0, TX_CENTER)
    dev.setSampleRate(SOAPY_SDR_TX, 0, 120e6)
    dev.setGain(SOAPY_SDR_TX, 0, "PeakDbm", -30)
    dev.writeSetting("rf_enabled", "false")
    dev.setAntenna(SOAPY_SDR_TX, 0, "RF_OUT")
    print("rx_rate", dev.getSampleRate(SOAPY_SDR_RX, 0), "rx_gain", dev.getGain(SOAPY_SDR_RX, 0),
          "tx_gain", dev.getGain(SOAPY_SDR_TX, 0), "rx_center_status", rx_center)

    stream = dev.setupStream(SOAPY_SDR_TX, SOAPY_SDR_CS16)
    dev.activateStream(stream)
    try:
        acc, elapsed, timeouts = pump(dev, stream, tone_cs16(262144), 10.0)
        snap = status()
    finally:
        dev.deactivateStream(stream)
        dev.closeStream(stream)

    # CF32 path, still RF off, short. Separate stream so format is explicit.
    stream2 = dev.setupStream(SOAPY_SDR_TX, SOAPY_SDR_CF32)
    dev.activateStream(stream2)
    try:
        acc2, elapsed2, timeouts2 = pump(dev, stream2, tone_cf32(65536), 4.0)
        snap2 = status()
    finally:
        dev.deactivateStream(stream2)
        dev.closeStream(stream2)

    deadline = time.time() + 15
    stopped = status()
    while time.time() < deadline:
        stopped = status()
        tx = stopped.get("tx") or {}
        if tx.get("status") in ("STOPPED", "DISABLED", "FAULT") and not tx.get("rf_enabled"):
            break
        time.sleep(0.2)

    def brief(st):
        tx = st.get("tx") or {}
        keys = ("status", "rf_enabled", "source_msps", "dma_msps", "processed_msps", "underflows",
                "applied_center_hz", "applied_rate_hz", "applied_peak_dbm", "error")
        return {k: tx.get(k) for k in keys}

    cs16 = acc / elapsed / 1e6
    cf32 = acc2 / elapsed2 / 1e6
    report = {
        "rx_center_before": rx_center,
        "rx_center_after": snap.get("applied_center_hz"),
        "cs16_client_msps": round(cs16, 3),
        "cs16_timeouts": timeouts,
        "cs16_hub": brief(snap),
        "cf32_client_msps": round(cf32, 3),
        "cf32_timeouts": timeouts2,
        "cf32_hub": brief(snap2),
        "after_stop": brief(stopped),
        "tx_accepted": dev.readSetting("tx_accepted"),
        "tx_clips": dev.readSetting("tx_clips"),
    }
    print(json.dumps(report, indent=2))
    tx = snap.get("tx") or {}
    tx2 = snap2.get("tx") or {}
    end = stopped.get("tx") or {}
    ok = (
        tx.get("status") == "STREAMING"
        and tx.get("rf_enabled") is False
        and tx.get("underflows") == 0
        and (tx.get("source_msps") or 0) > 110
        and cs16 > 110
        and abs((tx.get("applied_center_hz") or 0) - TX_CENTER) < 1
        and snap.get("applied_center_hz") == rx_center
        and tx2.get("rf_enabled") is False
        and (tx2.get("source_msps") or 0) > 100
        and cf32 > 100
        and end.get("rf_enabled") is False
        and end.get("status") in ("STOPPED", "DISABLED")
    )
    print("PASS" if ok else "FAIL")
    return 0 if ok else 1


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as ex:
        print("FAIL", ex)
        sys.exit(1)
