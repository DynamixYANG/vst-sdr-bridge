"""GNU Radio TX sink for VST Hub live_ring (mirrors RX-style set_* APIs).

Feeds complex64 IQ into Hub via Local\\vst_tx_v1. Hub owns NI-RFSG/FPGA.
RF defaults OFF. TX settings are independent of RX RefLevel/center/rate.
"""
from __future__ import annotations

import json
import threading
import time

import numpy as np
from gnuradio import gr

from .hub_control import HubControl
from .tx_ring import DEFAULT_NAME, TxShmProducer


class vst_tx_sink(gr.sync_block):
    """Streaming TX sink for PXIe-5644R through VST Hub.

    Parameters
    ----------
    center_freq : float
        TX LO / center frequency in Hz (65e6..6e9).
    samp_rate : float
        TX IQ rate. This Hub release accepts 120e6 only.
    peak_dbm : float
        RFSG peak power in dBm (-50..0). Not RX reference level.
    rf_enabled : bool
        RF output enable. Default False (safe).
    resource : str
        NI resource hint stored in Hub args (session owned by Hub).
    hub_host / hub_port : control port for TXSTART/TXSTOP.
    ring_name : SHM name (must match Hub TxConfiguration.ring_name).
    digital_gain : float
        Scale applied to complex samples before CS16 conversion.
    auto_start : bool
        If True, start() issues TXSTART live_ring. If False, only writes
        after an external TXSTART (advanced).
    """

    def __init__(
        self,
        center_freq=2.5e9,
        samp_rate=120e6,
        peak_dbm=-30.0,
        rf_enabled=False,
        resource="RIO0",
        hub_host="127.0.0.1",
        hub_port=19788,
        ring_name=DEFAULT_NAME,
        ring_mib=64,
        digital_gain=1.0,
        auto_start=True,
        queue_mib=64,
        fifo_mib=256,
        prefill_blocks=16,
    ):
        gr.sync_block.__init__(
            self,
            name="vst_tx_sink",
            in_sig=[np.complex64],
            out_sig=None,
        )
        self._lock = threading.RLock()
        self._center_freq = float(center_freq)
        self._samp_rate = float(samp_rate)
        self._peak_dbm = float(peak_dbm)
        self._rf_enabled = bool(rf_enabled)
        self._resource = str(resource)
        self._hub = HubControl(hub_host, hub_port)
        self._ring_name = str(ring_name)
        self._ring_mib = int(ring_mib)
        self._digital_gain = float(digital_gain)
        self._auto_start = bool(auto_start)
        self._queue_mib = int(queue_mib)
        self._fifo_mib = int(fifo_mib)
        self._prefill_blocks = int(prefill_blocks)
        self._ring: TxShmProducer | None = None
        self._started = False
        self._hub_streaming = False
        self._clip_count = 0
        self._accepted = 0
        self._last_error = ""
        self._i16 = np.empty(0, dtype=np.int16)

    # --- RX-mirrored setters (TX-independent) ---
    def set_center_freq(self, center_freq):
        with self._lock:
            self._center_freq = float(center_freq)
            if self._hub_streaming:
                self._restart_tx_locked()

    def set_freq(self, center_freq):
        self.set_center_freq(center_freq)

    def set_samp_rate(self, samp_rate):
        with self._lock:
            rate = float(samp_rate)
            if abs(rate - 120e6) > 1.0:
                raise ValueError("vst_tx_sink: this Hub release supports 120 MS/s only")
            self._samp_rate = rate
            if self._hub_streaming:
                self._restart_tx_locked()

    def set_peak_dbm(self, peak_dbm):
        with self._lock:
            self._peak_dbm = float(peak_dbm)
            if self._hub_streaming:
                self._restart_tx_locked()

    def set_gain(self, gain):
        """Map generic 'gain' UI to TX peak power (dBm), not RX RefLevel."""
        self.set_peak_dbm(gain)

    def set_rf_enabled(self, enabled):
        with self._lock:
            self._rf_enabled = bool(enabled)
            if self._hub_streaming:
                self._restart_tx_locked()

    def set_digital_gain(self, gain):
        with self._lock:
            self._digital_gain = float(gain)

    def get_center_freq(self):
        return self._center_freq

    def get_samp_rate(self):
        return self._samp_rate

    def get_peak_dbm(self):
        return self._peak_dbm

    def get_rf_enabled(self):
        return self._rf_enabled

    def get_clip_count(self):
        return self._clip_count

    def get_accepted_samples(self):
        return self._accepted

    def get_last_error(self):
        return self._last_error

    def get_tx_status(self):
        try:
            return self._hub.status().get("tx", {})
        except Exception as ex:
            return {"status": "UNREACHABLE", "error": str(ex)}

    def _config_dict(self):
        return {
            "source": "live_ring",
            "center_hz": self._center_freq,
            "rate_hz": self._samp_rate,
            "peak_dbm": self._peak_dbm,
            "rf_enabled": self._rf_enabled,
            "ring_name": self._ring_name,
            "ring_mi_b": self._ring_mib,
            "queue_mi_b": self._queue_mib,
            "fifo_mi_b": self._fifo_mib,
            "prefill_blocks": self._prefill_blocks,
            "waveform_path": "",
        }

    def _restart_tx_locked(self):
        # Brief gap: stop then start with new applied settings. RF follows _rf_enabled.
        try:
            self._hub.tx_stop()
        except Exception:
            pass
        self._hub_streaming = False
        if self._ring is not None:
            self._ring.close()
            self._ring = None
        if self._started:
            self._start_hub_locked()

    def _start_hub_locked(self):
        if abs(self._samp_rate - 120e6) > 1.0:
            raise ValueError("vst_tx_sink: sample rate must be 120e6")
        # Ensure previous TX is down so TXSTART is accepted.
        try:
            self._hub.tx_stop()
        except Exception:
            pass
        self._hub.tx_start(self._config_dict())
        self._ring = TxShmProducer.open(self._ring_name, timeout_s=15.0)
        self._ring.claim_producer()
        self._hub_streaming = True
        # Do not wait for STREAMING here — work() must run to prefill.

    def start(self):
        with self._lock:
            self._started = True
            self._last_error = ""
            if self._auto_start:
                try:
                    self._start_hub_locked()
                except Exception as ex:
                    self._last_error = str(ex)
                    self._started = False
                    raise
        return True

    def stop(self):
        with self._lock:
            self._started = False
            if self._ring is not None:
                try:
                    self._ring.release_producer()
                    self._ring.close()
                except Exception:
                    pass
                self._ring = None
            try:
                self._hub.tx_stop()
            except Exception as ex:
                self._last_error = str(ex)
            self._hub_streaming = False
            # Confirm RF off when Hub reachable.
            try:
                tx = self._hub.status().get("tx", {})
                if tx.get("rf_enabled"):
                    self._hub.tx_stop()
            except Exception:
                pass
        return True

    def work(self, input_items, output_items):
        inp = input_items[0]
        n = len(inp)
        if n == 0:
            return 0
        with self._lock:
            if not self._started or self._ring is None:
                return n  # drop while inactive (graph teardown)
            # CF32 -> CS16 with optional digital gain; saturate and count clips.
            scaled = np.asarray(inp, dtype=np.complex64) * np.float32(self._digital_gain)
            i = np.real(scaled).astype(np.float32, copy=False)
            q = np.imag(scaled).astype(np.float32, copy=False)
            i16 = np.empty(n * 2, dtype=np.int16)
            i_clip = np.clip(np.rint(i * 32767.0), -32768, 32767)
            q_clip = np.clip(np.rint(q * 32767.0), -32768, 32767)
            clips = int(np.count_nonzero((np.abs(i) > 1.0) | (np.abs(q) > 1.0)))
            self._clip_count += clips
            i16[0::2] = i_clip.astype(np.int16)
            i16[1::2] = q_clip.astype(np.int16)
            try:
                wrote = self._ring.write_cs16(i16.tobytes(), n, timeout_s=1.0)
                self._accepted += wrote
                if wrote < n:
                    # Backpressure timeout: consume anyway to avoid GR deadlock; Hub may FAULT on starve.
                    self._last_error = f"TX ring backpressure: wrote {wrote}/{n}"
            except Exception as ex:
                self._last_error = str(ex)
            return n
