"""GNU Radio sync source wrapping NI-RFSA continuous IQ fetch.

Practical host limits on SFF-PXI (measured, not theoretical):
- Standard Fetch path sustains ~90 MS/s (~360 MB/s); 100 MS/s overflows.
- 120 MS/s continuous capture uses the FPGA Streaming bitfile + DMA/TDMS
  LabVIEW path, not this Fetch-into-GR path.
- For live FFT UI, default to <= 20 MS/s so Qt GUI keeps up; raise carefully.
"""
from __future__ import annotations

import threading

import numpy as np
from gnuradio import gr

from .nirfsa_ctypes import RfsaError, RfsaSession


class vst_rfsa_source(gr.sync_block):
    """Streaming IQ source for PXIe-5644R (complex64 output).

    Parameters
    ----------
    resource : str
        NI-RFSA resource (default RIO0).
    center_freq : float
        LO / IQ carrier frequency in Hz.
    samp_rate : float
        Requested IQ rate in S/s (actual rate is read back after Initiate).
    reference_level : float
        Reference level in dBm (RFSA gain control).
    block_samples : int
        Samples per Fetch call (also ConfigureNumberOfSamples unit).
    dma_buffer_bytes : int
        Optional HOST_DMA_BUFFER_SIZE attribute (non-fatal if unsupported).
    use_streaming_bitfile : bool
        If True, InitWithOptions loads NI Streaming for VST.lvbitx (same as
        Validate-Streaming.py). Fetch still uses standard RFSA APIs.
    """

    def __init__(
        self,
        resource="RIO0",
        center_freq=1e9,
        samp_rate=10e6,
        reference_level=0.0,
        block_samples=65536,
        dma_buffer_bytes=256 * 1024 * 1024,
        use_streaming_bitfile=False,
    ):
        gr.sync_block.__init__(
            self,
            name="vst_rfsa_source",
            in_sig=None,
            out_sig=[np.complex64],
        )
        self._lock = threading.RLock()
        self._resource = str(resource)
        self._center_freq = float(center_freq)
        self._samp_rate = float(samp_rate)
        self._reference_level = float(reference_level)
        self._block_samples = int(block_samples)
        self._dma_buffer_bytes = int(dma_buffer_bytes)
        self._use_streaming_bitfile = bool(use_streaming_bitfile)
        self._session = None
        self._i16 = None
        self._started = False
        self._fetch_errors = 0
        self._total_samples = 0
        self.actual_samp_rate = float(samp_rate)

    def start(self):
        with self._lock:
            self._open_and_configure()
            self._started = True
        return True

    def stop(self):
        with self._lock:
            self._started = False
            if self._session is not None:
                self._session.close()
                self._session = None
        return True

    def _open_and_configure(self):
        if self._session is not None:
            self._session.close()
            self._session = None
        sess = RfsaSession()
        sess.open(self._resource, use_streaming_bitfile=self._use_streaming_bitfile)
        sess.configure(
            center_hz=self._center_freq,
            iq_rate=self._samp_rate,
            reference_level_dbm=self._reference_level,
            block_samples=self._block_samples,
            dma_buffer_bytes=self._dma_buffer_bytes,
            finite=False,
        )
        self.actual_samp_rate = sess.actual_iq_rate or self._samp_rate
        self._session = sess
        self._i16 = np.empty(self._block_samples * 2, dtype=np.int16)

    def _reconfigure_stream(self):
        """Abort and restart acquisition after rate/CF/gain changes that need it."""
        if not self._started:
            return
        self._open_and_configure()

    def set_center_freq(self, center_freq):
        with self._lock:
            self._center_freq = float(center_freq)
            if self._session is None:
                return
            try:
                # Many RFSA devices accept CF change while running.
                self._session.set_center_frequency(self._center_freq)
            except RfsaError:
                self._reconfigure_stream()

    def set_freq(self, center_freq):
        self.set_center_freq(center_freq)

    def set_samp_rate(self, samp_rate):
        with self._lock:
            self._samp_rate = float(samp_rate)
            if self._session is None:
                return
            self._reconfigure_stream()

    def set_reference_level(self, reference_level):
        with self._lock:
            self._reference_level = float(reference_level)
            if self._session is None:
                return
            try:
                self._session.set_reference_level(self._reference_level)
            except RfsaError:
                self._reconfigure_stream()

    def set_gain(self, gain_db):
        """Map 'gain' UI to RFSA reference level (dBm). Higher ref => less RF gain."""
        self.set_reference_level(gain_db)

    def get_center_freq(self):
        return self._center_freq

    def get_samp_rate(self):
        return self.actual_samp_rate

    def get_reference_level(self):
        return self._reference_level

    def get_fetch_errors(self):
        return self._fetch_errors

    def get_total_samples(self):
        return self._total_samples

    def work(self, input_items, output_items):
        out = output_items[0]
        nout = len(out)
        if nout == 0:
            return 0
        with self._lock:
            if self._session is None or not self._started:
                out[:] = 0
                return nout
            produced = 0
            try:
                while produced < nout:
                    n_fetch = min(self._block_samples, nout - produced)
                    # Reuse buffer; slice view for partial fetch.
                    buf = self._i16
                    if n_fetch * 2 > buf.size:
                        buf = np.empty(n_fetch * 2, dtype=np.int16)
                        self._i16 = buf
                    info = self._session.fetch_i16(buf, n_fetch, timeout_s=5.0)
                    got = int(info.ActualSamples) if info.ActualSamples else n_fetch
                    if got <= 0:
                        break
                    # Interleaved I16 -> complex64 using waveform gain/offset.
                    iq = buf[: got * 2].astype(np.float32)
                    gain = np.float32(self._session.last_gain)
                    offset = np.float32(self._session.last_offset)
                    i = iq[0::2] * gain + offset
                    q = iq[1::2] * gain + offset
                    out[produced : produced + got] = i + 1j * q
                    produced += got
                    self._total_samples += got
            except RfsaError:
                self._fetch_errors += 1
                if produced == 0:
                    out[:] = 0
                    return nout
            return produced if produced > 0 else 0
