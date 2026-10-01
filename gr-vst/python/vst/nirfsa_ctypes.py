"""NI-RFSA ctypes bindings for PXIe-5644R (mirrors NativeRfsa.cs in Stream Demo)."""
from __future__ import annotations

import ctypes
from ctypes import (
    POINTER,
    Structure,
    byref,
    c_char_p,
    c_double,
    c_int32,
    c_int64,
    c_uint16,
    c_uint32,
    c_void_p,
    create_string_buffer,
)
from pathlib import Path

DLL_CANDIDATES = [
    Path(r"C:\Program Files\IVI Foundation\IVI\Bin\niRFSA_64.dll"),
    Path(r"C:\Program Files (x86)\IVI Foundation\IVI\Bin\niRFSA_64.dll"),
]

ACQUISITION_IQ = 100
ATTR_IQ_RATE = 1150007
ATTR_HOST_DMA_BUFFER_SIZE = 1150285
ATTR_FPGA_BITFILE_PATH = 1150221

# Streaming bitfile used by Validate-Streaming.py / LabVIEW TDMS path.
STREAMING_BITFILE = (
    r"C:\Users\Public\Documents\National Instruments\FPGA Extensions Bitfiles"
    r"\NI PXIe-5644R\NI Streaming for VST.lvbitx"
)


class NativeWfmInfo(Structure):
    _pack_ = 8
    _fields_ = [
        ("AbsoluteInitialX", c_double),
        ("RelativeInitialX", c_double),
        ("XIncrement", c_double),
        ("ActualSamples", c_int64),
        ("Offset", c_double),
        ("Gain", c_double),
        ("Reserved1", c_double),
        ("Reserved2", c_double),
    ]


class RfsaError(RuntimeError):
    pass


def _load_dll():
    for path in DLL_CANDIDATES:
        if path.is_file():
            return ctypes.WinDLL(str(path))
    raise FileNotFoundError("niRFSA_64.dll not found in IVI Bin paths")


class RfsaSession:
    """Thin wrapper around niRFSA continuous IQ fetch (Complex I16)."""

    def __init__(self):
        self._dll = _load_dll()
        self._session = c_uint32(0)
        self._bind()
        self.actual_iq_rate = 0.0
        self.last_gain = 1.0 / 32768.0
        self.last_offset = 0.0
        self.resource = "RIO0"

    def _bind(self):
        d = self._dll
        d.niRFSA_init.argtypes = [c_char_p, c_uint16, c_uint16, POINTER(c_uint32)]
        d.niRFSA_init.restype = c_int32
        d.niRFSA_InitWithOptions.argtypes = [
            c_char_p, c_uint16, c_uint16, c_char_p, POINTER(c_uint32)
        ]
        d.niRFSA_InitWithOptions.restype = c_int32
        d.niRFSA_close.argtypes = [c_uint32]
        d.niRFSA_close.restype = c_int32
        d.niRFSA_Abort.argtypes = [c_uint32]
        d.niRFSA_Abort.restype = c_int32
        d.niRFSA_Initiate.argtypes = [c_uint32]
        d.niRFSA_Initiate.restype = c_int32
        d.niRFSA_ConfigureAcquisitionType.argtypes = [c_uint32, c_int32]
        d.niRFSA_ConfigureAcquisitionType.restype = c_int32
        d.niRFSA_ConfigureReferenceLevel.argtypes = [c_uint32, c_char_p, c_double]
        d.niRFSA_ConfigureReferenceLevel.restype = c_int32
        d.niRFSA_ConfigureIQCarrierFrequency.argtypes = [c_uint32, c_char_p, c_double]
        d.niRFSA_ConfigureIQCarrierFrequency.restype = c_int32
        d.niRFSA_ConfigureIQRate.argtypes = [c_uint32, c_char_p, c_double]
        d.niRFSA_ConfigureIQRate.restype = c_int32
        d.niRFSA_ConfigureNumberOfSamples.argtypes = [
            c_uint32, c_char_p, c_uint16, c_int64
        ]
        d.niRFSA_ConfigureNumberOfSamples.restype = c_int32
        d.niRFSA_SetAttributeViInt64.argtypes = [c_uint32, c_char_p, c_uint32, c_int64]
        d.niRFSA_SetAttributeViInt64.restype = c_int32
        d.niRFSA_GetAttributeViReal64.argtypes = [
            c_uint32, c_char_p, c_uint32, POINTER(c_double)
        ]
        d.niRFSA_GetAttributeViReal64.restype = c_int32
        d.niRFSA_GetAttributeViString.argtypes = [
            c_uint32, c_char_p, c_int32, c_int32, c_char_p
        ]
        d.niRFSA_GetAttributeViString.restype = c_int32
        d.niRFSA_FetchIQSingleRecordComplexI16.argtypes = [
            c_uint32, c_char_p, c_int64, c_int64, c_double, c_void_p,
            POINTER(NativeWfmInfo),
        ]
        d.niRFSA_FetchIQSingleRecordComplexI16.restype = c_int32
        d.niRFSA_GetError.argtypes = [
            c_uint32, POINTER(c_int32), c_int32, c_char_p
        ]
        d.niRFSA_GetError.restype = c_int32

    def _check(self, status: int, operation: str):
        if status >= 0:
            return
        code = c_int32()
        buf = create_string_buffer(4096)
        try:
            self._dll.niRFSA_GetError(self._session, byref(code), len(buf), buf)
            detail = buf.value.decode(errors="replace")
        except Exception:
            detail = ""
        raise RfsaError(f"{operation} failed: status={status} code={code.value} {detail}")

    def open(self, resource: str = "RIO0", use_streaming_bitfile: bool = False):
        self.close()
        self.resource = resource
        session = c_uint32(0)
        if use_streaming_bitfile:
            # Custom bitfile enables DMA/TDMS host path; Fetch still uses RFSA APIs.
            opts = b"DriverSetup=Bitfile:NI Streaming for VST.lvbitx"
            status = self._dll.niRFSA_InitWithOptions(
                resource.encode(), 1, 0, opts, byref(session)
            )
            self._session = session
            self._check(status, "niRFSA_InitWithOptions")
        else:
            status = self._dll.niRFSA_init(resource.encode(), 1, 0, byref(session))
            self._session = session
            self._check(status, "niRFSA_init")

    def configure(
        self,
        center_hz: float,
        iq_rate: float,
        reference_level_dbm: float,
        block_samples: int,
        dma_buffer_bytes: int = 256 * 1024 * 1024,
        finite: bool = False,
    ):
        s = self._session
        if not s.value:
            raise RfsaError("Session not open")
        d = self._dll
        self._check(d.niRFSA_ConfigureAcquisitionType(s, ACQUISITION_IQ), "ConfigureAcquisitionType")
        self._check(
            d.niRFSA_ConfigureReferenceLevel(s, b"", float(reference_level_dbm)),
            "ConfigureReferenceLevel",
        )
        self._check(
            d.niRFSA_ConfigureIQCarrierFrequency(s, b"", float(center_hz)),
            "ConfigureIQCarrierFrequency",
        )
        self._check(
            d.niRFSA_ConfigureIQRate(s, b"", float(iq_rate)),
            "ConfigureIQRate",
        )
        self._check(
            d.niRFSA_ConfigureNumberOfSamples(
                s, b"", 1 if finite else 0, int(block_samples)
            ),
            "ConfigureNumberOfSamples",
        )
        dma_status = d.niRFSA_SetAttributeViInt64(
            s, b"", ATTR_HOST_DMA_BUFFER_SIZE, int(dma_buffer_bytes)
        )
        # Attribute may be unsupported on some setups; non-fatal (matches StreamDemo).
        self.dma_attr_status = int(dma_status)
        self._check(d.niRFSA_Initiate(s), "niRFSA_Initiate")
        rate = c_double()
        self._check(
            d.niRFSA_GetAttributeViReal64(s, b"", ATTR_IQ_RATE, byref(rate)),
            "Get IQ Rate",
        )
        self.actual_iq_rate = float(rate.value)

    def set_reference_level(self, reference_level_dbm: float):
        self._check(
            self._dll.niRFSA_ConfigureReferenceLevel(
                self._session, b"", float(reference_level_dbm)
            ),
            "ConfigureReferenceLevel",
        )

    def set_center_frequency(self, center_hz: float):
        self._check(
            self._dll.niRFSA_ConfigureIQCarrierFrequency(
                self._session, b"", float(center_hz)
            ),
            "ConfigureIQCarrierFrequency",
        )

    def fetch_i16(self, buffer, n_samples: int, timeout_s: float = 10.0) -> NativeWfmInfo:
        """Fetch into a writable buffer of interleaved int16 I/Q (length 2*n_samples)."""
        info = NativeWfmInfo()
        if hasattr(buffer, "ctypes"):
            ptr = buffer.ctypes.data_as(c_void_p)
        else:
            ptr = c_void_p(buffer)
        status = self._dll.niRFSA_FetchIQSingleRecordComplexI16(
            self._session, b"", 0, int(n_samples), float(timeout_s), ptr, byref(info)
        )
        self._check(status, "FetchIQSingleRecordComplexI16")
        self.last_gain = float(info.Gain) if info.Gain else (1.0 / 32768.0)
        self.last_offset = float(info.Offset)
        return info

    def abort(self):
        if self._session.value:
            try:
                self._dll.niRFSA_Abort(self._session)
            except Exception:
                pass

    def close(self):
        if self._session.value:
            try:
                self._dll.niRFSA_Abort(self._session)
            except Exception:
                pass
            try:
                self._dll.niRFSA_close(self._session)
            except Exception:
                pass
            self._session = c_uint32(0)

    def __del__(self):
        try:
            self.close()
        except Exception:
            pass
