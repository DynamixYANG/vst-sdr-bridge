#include "SoapyVst.hpp"
#include <SoapySDR/Errors.hpp>
#include <winsock2.h>
#include <ws2tcpip.h>
#include <windows.h>
#include <algorithm>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <stdexcept>
#include <vector>

#pragma comment(lib, "Ws2_32.lib")

using namespace vst;

namespace {

void wsaOnce() {
  static bool ready = false;
  if (ready) return;
  WSADATA data;
  if (WSAStartup(MAKEWORD(2, 2), &data) != 0)
    throw std::runtime_error("vst tx: WSAStartup failed");
  ready = true;
}

std::string hubCommand(const std::string &host, int port, const std::string &command) {
  wsaOnce();
  SOCKET s = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
  if (s == INVALID_SOCKET) throw std::runtime_error("vst tx: control socket failed");
  struct CloseSocket { SOCKET s; ~CloseSocket() { closesocket(s); } } guard{s};
  DWORD ms = 20000;
  setsockopt(s, SOL_SOCKET, SO_RCVTIMEO, reinterpret_cast<const char *>(&ms), sizeof(ms));
  setsockopt(s, SOL_SOCKET, SO_SNDTIMEO, reinterpret_cast<const char *>(&ms), sizeof(ms));
  sockaddr_in address{};
  address.sin_family = AF_INET;
  address.sin_port = htons(static_cast<u_short>(port));
  if (inet_pton(AF_INET, host.c_str(), &address.sin_addr) != 1)
    throw std::runtime_error("vst tx: bad hub host");
  if (::connect(s, reinterpret_cast<sockaddr *>(&address), sizeof(address)) != 0)
    throw std::runtime_error("vst tx: Hub control unavailable on " + host + ":" + std::to_string(port));
  const std::string wire = command + "\n";
  size_t sent = 0;
  while (sent < wire.size()) {
    const int n = send(s, wire.data() + sent, static_cast<int>(wire.size() - sent), 0);
    if (n <= 0) throw std::runtime_error("vst tx: control send failed");
    sent += static_cast<size_t>(n);
  }
  std::string reply;
  char c;
  while (reply.size() < 8192) {
    const int n = recv(s, &c, 1, 0);
    if (n != 1) throw std::runtime_error("vst tx: control reply missing");
    if (c == '\n') return reply;
    reply.push_back(c);
  }
  throw std::runtime_error("vst tx: control reply too long");
}

std::string jsonEscape(const std::string &text) {
  std::string out;
  out.reserve(text.size() + 8);
  for (char c : text) {
    if (c == '\\' || c == '"') out.push_back('\\');
    out.push_back(c);
  }
  return out;
}

std::wstring widen(const std::string &text) {
  return std::wstring(text.begin(), text.end());
}

} // namespace

struct Device::TxProducer {
  HANDLE map{nullptr};
  HANDLE mutex{nullptr};
  void *view{nullptr};
  uint8_t *base{nullptr};
  int16_t *data{nullptr};
  uint64_t cap{0};

  ~TxProducer() { close(); }

  static std::shared_ptr<TxProducer> open(const std::string &name, DWORD timeoutMs) {
    const ULONGLONG deadline = GetTickCount64() + timeoutMs;
    DWORD last = 0;
    while (GetTickCount64() <= deadline) {
      auto prod = std::shared_ptr<TxProducer>(new TxProducer());
      if (prod->tryAttach(name, last)) return prod;
      Sleep(20);
    }
    throw std::runtime_error("vst tx: ring " + name + " not available (Hub live_ring). Win32 " + std::to_string(last));
  }

  bool tryAttach(const std::string &name, DWORD &last) {
    const std::wstring wname = widen(name);
    map = OpenFileMappingW(FILE_MAP_ALL_ACCESS, FALSE, wname.c_str());
    if (!map) { last = GetLastError(); return false; }
    view = MapViewOfFile(map, FILE_MAP_ALL_ACCESS, 0, 0, 256);
    if (!view) { last = GetLastError(); close(); return false; }
    base = static_cast<uint8_t *>(view);
    if (ld32(0) != 0x31585456u) { last = ERROR_INVALID_DATA; close(); return false; }
    cap = ld64(16);
    if (cap < 1024) { last = ERROR_INVALID_DATA; close(); return false; }
    const SIZE_T bytes = static_cast<SIZE_T>(cap) * 4 + 256;
    UnmapViewOfFile(view);
    view = nullptr;
    base = nullptr;
    view = MapViewOfFile(map, FILE_MAP_ALL_ACCESS, 0, 0, bytes);
    if (!view) { last = GetLastError(); close(); return false; }
    base = static_cast<uint8_t *>(view);
    data = reinterpret_cast<int16_t *>(base + 256);
    mutex = CreateMutexW(nullptr, FALSE, (wname + L".lock").c_str());
    if (!mutex) { last = GetLastError(); close(); return false; }
    return true;
  }

  void close() {
    if (mutex) { CloseHandle(mutex); mutex = nullptr; }
    if (view) { UnmapViewOfFile(view); view = nullptr; }
    if (map) { CloseHandle(map); map = nullptr; }
    base = nullptr;
    data = nullptr;
    cap = 0;
  }

  void lock() {
    const DWORD rc = WaitForSingleObject(mutex, 1000);
    if (rc != WAIT_OBJECT_0 && rc != WAIT_ABANDONED)
      throw std::runtime_error("vst tx: ring mutex timeout");
  }
  void unlock() { ReleaseMutex(mutex); }

  uint32_t ld32(int offset) const { uint32_t v; std::memcpy(&v, base + offset, 4); return v; }
  uint64_t ld64(int offset) const { uint64_t v; std::memcpy(&v, base + offset, 8); return v; }
  void st32(int offset, uint32_t v) { std::memcpy(base + offset, &v, 4); }
  void st64(int offset, uint64_t v) { std::memcpy(base + offset, &v, 8); }

  void claim() {
    lock();
    st32(56, 1);
    st32(88, GetCurrentProcessId());
    st64(96, GetTickCount64());
    unlock();
  }

  void release() {
    if (!base || !mutex) return;
    try {
      lock();
      if (ld32(88) == GetCurrentProcessId()) st32(56, 0);
      unlock();
    } catch (...) {}
  }

  int writeCs16(const int16_t *src, size_t samples, long timeoutUs) {
    if (!samples) return 0;
    const ULONGLONG deadline = GetTickCount64() + static_cast<ULONGLONG>(timeoutUs > 0 ? (timeoutUs + 999) / 1000 : 100);
    size_t done = 0;
    while (done < samples) {
      lock();
      const uint64_t w = ld64(24);
      const uint64_t r = ld64(32);
      if (w < r) { unlock(); return done ? static_cast<int>(done) : SOAPY_SDR_STREAM_ERROR; }
      const uint64_t occ = w - r;
      const uint64_t free = cap > occ ? cap - occ : 0;
      size_t n = static_cast<size_t>(std::min<uint64_t>(free, samples - done));
      if (n == 0) {
        st64(96, GetTickCount64());
        unlock();
        if (GetTickCount64() >= deadline) return done ? static_cast<int>(done) : SOAPY_SDR_TIMEOUT;
        Sleep(1); // yield to Hub ring consumer; Sleep(0) busy-spins and worsens duplex scheduling
        continue;
      }
      const size_t pos = static_cast<size_t>(w % cap);
      const size_t first = std::min(n, static_cast<size_t>(cap - pos));
      std::memcpy(data + pos * 2, src + done * 2, first * 4);
      if (first < n) std::memcpy(data, src + (done + first) * 2, (n - first) * 4);
      st64(24, w + n);
      st32(56, 1);
      st32(88, GetCurrentProcessId());
      st64(96, GetTickCount64());
      st64(40, ld64(40) + n);
      unlock();
      done += n;
    }
    return static_cast<int>(done);
  }
};

std::string Device::txConfigJson() const {
  char buf[1280];
  std::snprintf(buf, sizeof(buf),
    "{\"source\":\"live_ring\",\"center_hz\":%.17g,\"rate_hz\":%.17g,\"peak_dbm\":%.17g,"
    "\"rf_enabled\":%s,\"ring_name\":\"%s\",\"ring_mi_b\":%d,\"queue_mi_b\":128,"
    "\"fifo_mi_b\":256,\"prefill_blocks\":16,\"waveform_path\":\"\"}",
    _txCenterHz, _txRate, _txPeakDbm, _txRfEnabled ? "true" : "false",
    jsonEscape(_txRing).c_str(), _txRingMiB);
  return buf;
}

void Device::startTx() {
  std::lock_guard<std::mutex> life(_txLife);
  std::string json, host, ring;
  int port = 19788;
  {
    std::lock_guard<std::mutex> lock(_mutex);
    if (!std::isfinite(_txCenterHz) || _txCenterHz < 65e6 || _txCenterHz > 6e9)
      throw std::invalid_argument("TX frequency: 65 MHz to 6 GHz");
    if (std::abs(_txRate - 120e6) > 1.0)
      throw std::invalid_argument("TX sample rate must be 120e6 in this release");
    if (!std::isfinite(_txPeakDbm) || _txPeakDbm < -50.0 || _txPeakDbm > 0.0)
      throw std::invalid_argument("TX peak level: -50 to 0 dBm");
    json = txConfigJson();
    host = _txHost;
    port = _txControlPort;
    ring = _txRing;
    _txOwns.store(true);
    _txActive = false;
    _txProducer.reset();
  }
  try {
    try { hubCommand(host, port, "TXSTOP"); }
    catch (const std::exception &ex) {
      SoapySDR::logf(SOAPY_SDR_WARNING, "vst tx: TXSTOP before start: %s", ex.what());
    }
    const std::string reply = hubCommand(host, port, "TXSTART " + json);
    if (reply.compare(0, 2, "OK") != 0) throw std::runtime_error(reply.empty() ? "TXSTART failed" : reply);
    auto prod = TxProducer::open(ring, 15000);
    prod->claim();
    {
      std::lock_guard<std::mutex> lock(_mutex);
      _txProducer = std::move(prod);
      _txActive = true;
      _lastError.clear();
    }
    SoapySDR::logf(SOAPY_SDR_INFO, "vst tx: writeStream -> %s (Hub live_ring, rf_enabled=%s)",
      ring.c_str(), _txRfEnabled ? "true" : "false");
  } catch (const std::exception &ex) {
    try { hubCommand(host, port, "TXSTOP"); } catch (...) {}
    std::lock_guard<std::mutex> lock(_mutex);
    _txActive = false;
    _txProducer.reset();
    _txOwns.store(false);
    _lastError = ex.what();
    throw;
  }
}

void Device::stopTx() {
  std::lock_guard<std::mutex> life(_txLife);
  std::shared_ptr<TxProducer> prod;
  std::string host;
  int port = 19788;
  {
    std::lock_guard<std::mutex> lock(_mutex);
    _txActive = false;
    prod = std::move(_txProducer);
    host = _txHost;
    port = _txControlPort;
    _txOwns.store(false);
  }
  if (prod) prod->release();
  try {
    const std::string reply = hubCommand(host, port, "TXSTOP");
    if (reply.compare(0, 3, "ERR") == 0) {
      std::lock_guard<std::mutex> lock(_mutex);
      _lastError = reply;
      SoapySDR::log(SOAPY_SDR_ERROR, _lastError);
    }
  } catch (const std::exception &ex) {
    std::lock_guard<std::mutex> lock(_mutex);
    _lastError = ex.what();
    SoapySDR::log(SOAPY_SDR_WARNING, _lastError);
  }
}

int Device::writeStream(SoapySDR::Stream *stream, const void *const *buffs, const size_t numElems,
                        int &flags, const long long, const long timeoutUs) {
  flags = 0;
  auto *tag = reinterpret_cast<StreamTag *>(stream);
  if (!tag || tag->direction != SOAPY_SDR_TX) return SOAPY_SDR_NOT_SUPPORTED;
  if (!buffs || !buffs[0]) return SOAPY_SDR_STREAM_ERROR;
  std::shared_ptr<TxProducer> prod;
  float gain = 1.f;
  size_t mtu = 262144;
  {
    std::lock_guard<std::mutex> lock(_mutex);
    if (!_txActive || !_txProducer) return SOAPY_SDR_TIMEOUT;
    prod = _txProducer;
    gain = static_cast<float>(_txDigitalGain);
    mtu = _txMtu;
  }
  const size_t n = std::min(numElems, mtu);
  // Reuse TLS convert scratch across calls. Per-call std::vector at 262k CF32 (~2 MiB)
  // caused multi-minute heap fragmentation / jitter cliffs (source_msps 90-126, queue%=0).
  thread_local std::vector<int16_t> tlsCs16;
  int rc;
  if (tag->fmt == StreamFormat::CS16 && gain == 1.f) {
    rc = prod->writeCs16(static_cast<const int16_t *>(buffs[0]), n, timeoutUs);
  } else if (tag->fmt == StreamFormat::CS16) {
    const auto *in = static_cast<const int16_t *>(buffs[0]);
    if (tlsCs16.size() < n * 2) tlsCs16.resize(n * 2);
    uint64_t clips = 0;
    for (size_t i = 0; i < n * 2; ++i) {
      const float x = in[i] * gain / 32767.f;
      if (x > 1.f || x < -1.f) ++clips;
      const float c = std::max(-1.f, std::min(1.f, x));
      const float s = c * 32767.f;
      tlsCs16[i] = static_cast<int16_t>(s >= 0.f ? s + 0.5f : s - 0.5f);
    }
    _txClips.fetch_add(clips);
    rc = prod->writeCs16(tlsCs16.data(), n, timeoutUs);
  } else {
    const auto *in = static_cast<const float *>(buffs[0]);
    if (tlsCs16.size() < n * 2) tlsCs16.resize(n * 2);
    uint64_t clips = 0;
    const float g = gain;
    for (size_t i = 0; i < n; ++i) {
      float ii = in[2 * i] * g;
      float qq = in[2 * i + 1] * g;
      if (ii > 1.f || ii < -1.f || qq > 1.f || qq < -1.f) ++clips;
      ii = std::max(-1.f, std::min(1.f, ii));
      qq = std::max(-1.f, std::min(1.f, qq));
      const float si = ii * 32767.f, sq = qq * 32767.f;
      tlsCs16[2 * i] = static_cast<int16_t>(si >= 0.f ? si + 0.5f : si - 0.5f);
      tlsCs16[2 * i + 1] = static_cast<int16_t>(sq >= 0.f ? sq + 0.5f : sq - 0.5f);
    }
    _txClips.fetch_add(clips);
    rc = prod->writeCs16(tlsCs16.data(), n, timeoutUs);
  }
  if (rc > 0) _txAccepted.fetch_add(static_cast<uint64_t>(rc));
  return rc;
}
