"""Producer side of Local\\vst_tx_v1 (CS16 SPSC ring for Hub TxEngine)."""
from __future__ import annotations

import ctypes
import struct
import time
from ctypes import wintypes

MAGIC = 0x31585456  # VTX1
HEADER = 256
DEFAULT_NAME = "Local\\vst_tx_v1"

kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
FILE_MAP_ALL_ACCESS = 0x000F001F
INVALID_HANDLE_VALUE = ctypes.c_void_p(-1).value

kernel32.OpenFileMappingW.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.LPCWSTR]
kernel32.OpenFileMappingW.restype = wintypes.HANDLE
kernel32.MapViewOfFile.argtypes = [wintypes.HANDLE, wintypes.DWORD, wintypes.DWORD, wintypes.DWORD, ctypes.c_size_t]
kernel32.MapViewOfFile.restype = ctypes.c_void_p
kernel32.UnmapViewOfFile.argtypes = [ctypes.c_void_p]
kernel32.UnmapViewOfFile.restype = wintypes.BOOL
kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
kernel32.CloseHandle.restype = wintypes.BOOL
kernel32.CreateMutexW.argtypes = [ctypes.c_void_p, wintypes.BOOL, wintypes.LPCWSTR]
kernel32.CreateMutexW.restype = wintypes.HANDLE
kernel32.WaitForSingleObject.argtypes = [wintypes.HANDLE, wintypes.DWORD]
kernel32.WaitForSingleObject.restype = wintypes.DWORD
kernel32.ReleaseMutex.argtypes = [wintypes.HANDLE]
kernel32.ReleaseMutex.restype = wintypes.BOOL
kernel32.GetTickCount64.restype = ctypes.c_ulonglong


class TxShmProducer:
    """Attach to Hub-created TX ring and write interleaved int16 IQ."""

    def __init__(self, name: str = DEFAULT_NAME):
        self.name = name
        self._map = None
        self._view = None
        self._mutex = None
        self.capacity = 0
        self._mv = None

    @classmethod
    def open(cls, name: str = DEFAULT_NAME, timeout_s: float = 15.0) -> "TxShmProducer":
        deadline = time.monotonic() + timeout_s
        last_err = None
        while time.monotonic() < deadline:
            try:
                obj = cls(name)
                obj._attach()
                return obj
            except OSError as ex:
                last_err = ex
                time.sleep(0.05)
        raise TimeoutError(f"TX ring {name} not available within {timeout_s}s: {last_err}")

    def _attach(self) -> None:
        h = kernel32.OpenFileMappingW(FILE_MAP_ALL_ACCESS, False, self.name)
        if not h:
            raise OSError(f"OpenFileMappingW({self.name}) err={ctypes.get_last_error()}")
        self._map = h
        view = kernel32.MapViewOfFile(h, FILE_MAP_ALL_ACCESS, 0, 0, HEADER)
        if not view:
            kernel32.CloseHandle(h)
            self._map = None
            raise OSError(f"MapViewOfFile header err={ctypes.get_last_error()}")
        magic = struct.unpack_from("<I", ctypes.string_at(view, 4))[0]
        if magic != MAGIC:
            kernel32.UnmapViewOfFile(view)
            kernel32.CloseHandle(h)
            self._map = None
            raise OSError(f"bad TX ring magic {magic:#x}")
        cap = struct.unpack_from("<Q", ctypes.string_at(view + 16, 8))[0]
        need = HEADER + int(cap) * 4
        kernel32.UnmapViewOfFile(view)
        view = kernel32.MapViewOfFile(h, FILE_MAP_ALL_ACCESS, 0, 0, need)
        if not view:
            kernel32.CloseHandle(h)
            self._map = None
            raise OSError(f"MapViewOfFile full err={ctypes.get_last_error()}")
        self._view = view
        self.capacity = int(cap)
        self._mv = (ctypes.c_char * need).from_address(view)
        self._mutex = kernel32.CreateMutexW(None, False, self.name + ".lock")
        if not self._mutex:
            self.close()
            raise OSError(f"CreateMutexW err={ctypes.get_last_error()}")

    def _lock(self) -> None:
        rc = kernel32.WaitForSingleObject(self._mutex, 1000)
        if rc not in (0, 0x80):  # OBJECT_0 or ABANDONED
            raise TimeoutError("TX ring mutex timeout")

    def _unlock(self) -> None:
        kernel32.ReleaseMutex(self._mutex)

    def claim_producer(self) -> None:
        self._lock()
        try:
            # producer_active@56, producer_pid@88, producer_heartbeat@96
            struct.pack_into("<I", self._mv, 56, 1)
            struct.pack_into("<I", self._mv, 88, ctypes.windll.kernel32.GetCurrentProcessId())
            struct.pack_into("<Q", self._mv, 96, kernel32.GetTickCount64())
        finally:
            self._unlock()

    def heartbeat(self) -> None:
        self._lock()
        try:
            struct.pack_into("<Q", self._mv, 96, kernel32.GetTickCount64())
        finally:
            self._unlock()

    def write_cs16(self, buf: memoryview | bytes, samples: int, timeout_s: float = 1.0) -> int:
        """Write interleaved int16 IQ (samples complex). Blocks with timeout if ring full. Returns samples written."""
        if samples <= 0:
            return 0
        src = (ctypes.c_char * (samples * 4)).from_buffer_copy(buf[: samples * 4])
        written = 0
        deadline = time.monotonic() + timeout_s
        while written < samples:
            self._lock()
            try:
                w = struct.unpack_from("<Q", self._mv, 24)[0]
                r = struct.unpack_from("<Q", self._mv, 32)[0]
                occ = w - r if w >= r else 0
                free = self.capacity - occ
                if free <= 0:
                    struct.pack_into("<Q", self._mv, 96, kernel32.GetTickCount64())
                else:
                    n = min(samples - written, int(free))
                    pos = w % self.capacity
                    first = min(n, self.capacity - pos)
                    off = HEADER + pos * 4
                    ctypes.memmove(ctypes.addressof(self._mv) + off, ctypes.addressof(src) + written * 4, first * 4)
                    if first < n:
                        ctypes.memmove(
                            ctypes.addressof(self._mv) + HEADER,
                            ctypes.addressof(src) + (written + first) * 4,
                            (n - first) * 4,
                        )
                    struct.pack_into("<Q", self._mv, 24, w + n)
                    struct.pack_into("<Q", self._mv, 96, kernel32.GetTickCount64())
                    struct.pack_into("<I", self._mv, 56, 1)
                    written += n
                    continue
            finally:
                self._unlock()
            if time.monotonic() >= deadline:
                break
            time.sleep(0.001)
        return written

    def release_producer(self) -> None:
        if not self._mv:
            return
        try:
            self._lock()
            try:
                struct.pack_into("<I", self._mv, 56, 0)
            finally:
                self._unlock()
        except Exception:
            pass

    def close(self) -> None:
        self.release_producer()
        if self._view:
            kernel32.UnmapViewOfFile(self._view)
            self._view = None
        if self._map:
            kernel32.CloseHandle(self._map)
            self._map = None
        if self._mutex:
            kernel32.CloseHandle(self._mutex)
            self._mutex = None

    def __del__(self):
        try:
            self.close()
        except Exception:
            pass
