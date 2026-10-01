#pragma once
#include <windows.h>
#include <stdexcept>

namespace vst {
// Sleep(1) can sleep for a whole ~15.6 ms system tick. At 120 MS/s that
// changes queue throughput materially. A per-thread high-resolution timer
// avoids global timer-resolution settings and occluded-window dependencies.
inline void waitOneMillisecond() {
  struct Timer {
    HANDLE handle = CreateWaitableTimerExW(nullptr, nullptr, 0x00000002,
                                           TIMER_MODIFY_STATE | SYNCHRONIZE);
    Timer() { if (!handle) throw std::runtime_error("vst: high-resolution timer requires Windows 10 1803 or newer"); }
    ~Timer() { CloseHandle(handle); }
  };
  thread_local Timer timer;
  LARGE_INTEGER due; due.QuadPart = -10000; // relative 1 ms, in 100 ns units
  if (!SetWaitableTimer(timer.handle, &due, 0, nullptr, nullptr, FALSE) ||
      WaitForSingleObject(timer.handle, INFINITE) != WAIT_OBJECT_0)
    throw std::runtime_error("vst: high-resolution queue wait failed");
}
}
