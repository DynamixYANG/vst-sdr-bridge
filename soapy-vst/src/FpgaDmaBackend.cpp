#include "SoapyVst.hpp"
#include <SoapySDR/Errors.hpp>
#include <winsock2.h>
#include <ws2tcpip.h>
#include <windows.h>
#include "HighResolutionWait.hpp"
#include <cstring>
#include <stdexcept>
#include <vector>
#include <string>
#include <cstdio>
#include <cstdint>
#include <algorithm>
#include <emmintrin.h>

#pragma comment(lib, "Ws2_32.lib")

namespace vst {

namespace {

static const uint32_t SHM_MAGIC = 0x50313230u; // P120
static const size_t SHM_HEADER = 256;

// SSE2 is mandatory on x64. Convert contiguous spans without a modulo/divide
// for every complex sample; the 120 MS/s path must handle 240M components/s.
void copyIq(const int16_t *src, void *dst, size_t samples, StreamFormat::Type fmt) {
  if (fmt == StreamFormat::CS16) { std::memcpy(dst, src, samples*4); return; }
  float *out = static_cast<float *>(dst);
  const __m128 scale = _mm_set1_ps(1.0f/32768.0f);
  size_t i = 0, components = samples*2;
  for (; i+8<=components; i+=8) {
    const __m128i v = _mm_loadu_si128(reinterpret_cast<const __m128i *>(src+i));
    const __m128i sign = _mm_cmpgt_epi16(_mm_setzero_si128(),v);
    _mm_storeu_ps(out+i, _mm_mul_ps(_mm_cvtepi32_ps(_mm_unpacklo_epi16(v,sign)),scale));
    _mm_storeu_ps(out+i+4, _mm_mul_ps(_mm_cvtepi32_ps(_mm_unpackhi_epi16(v,sign)),scale));
  }
  for (; i<components; ++i) out[i]=src[i]/32768.0f;
}

#pragma pack(push, 1)
struct ShmHeader {
  uint32_t magic;
  uint32_t version;
  double rate_hz;
  uint64_t capacity_samples;
  uint64_t write_idx;
  uint64_t read_idx;
  uint64_t drops;
  uint32_t overflow_flags;
  uint32_t active;
  double center_hz;
  uint64_t epoch;
  uint64_t heartbeat_ms;
  double reference_dbm;
  uint32_t producer_pid;
  uint32_t reserved;
  uint64_t delivered_samples;
  uint64_t read_calls;
  uint64_t consumer_heartbeat_ms;
  uint64_t display_skipped;
  uint32_t frontend_magic;
  int32_t preamp_requested;
  int32_t preamp_actual;
  uint32_t preamp_present;
  double effective_bandwidth_hz;
  uint64_t overflow_count;
  uint64_t recoveries;
  double last_config_ms;
  uint64_t control_error_count;
  uint64_t config_count;
  uint32_t producer_state;
  int32_t last_ni_error;
  uint64_t request_id;
  uint32_t consumer_pid;
  uint32_t consumer_active;
  uint64_t consumer_lease_ms;
  uint64_t idle_discard_samples;
  uint64_t fifo_remaining;
  uint64_t fifo_capacity;
  uint32_t hub_magic;
  uint32_t hub_reserved;
};
#pragma pack(pop)
static_assert(sizeof(ShmHeader)==256,"SHM HUB1 layout changed");

/** Prefer in-process Windows named shared-memory ring (Local\vst_iq_ring).
 *  Fallback: TCP bridge (legacy scaffold).
 */
class FpgaDmaBackend : public IBackend {
public:
  explicit FpgaDmaBackend(const SoapySDR::Kwargs &args) {
    if (args.count("dma_host")) _host = args.at("dma_host");
    if (args.count("dma_port")) _port = std::stoi(args.at("dma_port"));
    if (args.count("control_port")) _controlPort = std::stoi(args.at("control_port"));
    if (args.count("shm_name")) _shmName = args.at("shm_name");
    if (args.count("display_stride")) _displayStride = std::max(1, std::stoi(args.at("display_stride")));
    if (args.count("display_mode")) _displayMode = args.at("display_mode");
    if (args.count("max_out")) _maxOut = std::max(256, std::stoi(args.at("max_out")));
    if (args.count("report_rate")) {
      const auto &v = args.at("report_rate");
      _reportTrueRate = (v == "true" || v == "1" || v == "input" || v == "full");
    }
    if (args.count("transport")) _transport = args.at("transport"); // shm|tcp|auto
    static bool wsa;
    if (!wsa) {
      WSADATA d;
      WSAStartup(MAKEWORD(2, 2), &d);
      wsa = true;
    }
  }
  ~FpgaDmaBackend() override { close(); }

  std::string name() const override { return "dma"; }

  void open(const std::string &resource) override {
    _resource = resource;
    if (_transport == "tcp") {
      connectSocket();
      _mode = Mode::Tcp;
      return;
    }
    if (openShm()) {
      _mode = Mode::Shm;
      SoapySDR::logf(SOAPY_SDR_INFO, "vst dma: SHM %s cap=%llu", _shmName.c_str(),
                     (unsigned long long)_hdr->capacity_samples);
      return;
    }
    if (_transport == "shm") {
      throw std::runtime_error("dma: cannot open SHM " + _shmName + " — start VSTHub.exe first");
    }
    // auto: fall back to TCP
    connectSocket();
    _mode = Mode::Tcp;
    SoapySDR::log(SOAPY_SDR_WARNING, "vst dma: SHM unavailable, using TCP bridge");
  }

  void close() override {
    stop();
    closeShm();
    if (_sock != INVALID_SOCKET) {
      closesocket(_sock);
      _sock = INVALID_SOCKET;
    }
  }

  void configure(double centerHz, double iqRate, double refLevelDbm, size_t) override {
    if (_mode == Mode::Shm) {
      controlConfig(centerHz, iqRate, refLevelDbm);
      return;
    }
    _center = centerHz; _rate = iqRate; _ref = refLevelDbm; _rateActual = iqRate;
    if (_mode == Mode::Tcp) {
      ensureConnected();
      char line[256];
      std::snprintf(line, sizeof(line),
        "CONFIG center=%.3f rate=%.3f ref=%.3f resource=%s\n",
        centerHz, iqRate, refLevelDbm, _resource.c_str());
      sendAll(line, (int)std::strlen(line));
    }
  }

  void start() override {
    if (_mode == Mode::Shm && _shmMutex) {
      DWORD locked = WaitForSingleObject(_shmMutex, 1000);
      if (locked != WAIT_OBJECT_0 && locked != WAIT_ABANDONED)
        throw std::runtime_error("dma: start ring lock timeout");
      if (_hdr->hub_magic==0x31425548u) {
        if (_hdr->consumer_active) {
          HANDLE process=OpenProcess(SYNCHRONIZE,FALSE,_hdr->consumer_pid);
          const bool alive=process && WaitForSingleObject(process,0)==WAIT_TIMEOUT;
          if(process) CloseHandle(process);
          if(alive) { ReleaseMutex(_shmMutex); throw std::runtime_error("RX already has an active IQ consumer; stop the other GQRX/GNU Radio stream first"); }
        }
        _hdr->consumer_pid=GetCurrentProcessId(); _hdr->consumer_active=1;
        _hdr->consumer_lease_ms=GetTickCount64(); _hdr->consumer_heartbeat_ms=GetTickCount64();
        _ownsConsumer=true;
      }
      _hdr->read_idx = _hdr->write_idx;
      ReleaseMutex(_shmMutex);
      _discardCache = true;
      // A Hub can initialize without acquiring. RX activation explicitly starts
      // acquisition after claiming the consumer; merely configuring never does.
      try {
        const auto reply=controlExchange("START");
        if(reply.rfind("OK ",0)!=0) throw std::runtime_error("RX start failed: "+reply);
      } catch (...) { stop(); throw; }
    }
    if (_mode == Mode::Tcp) {
      ensureConnected();
      const char *cmd = "START\n";
      sendAll(cmd, (int)std::strlen(cmd));
    }
    _running = true;
  }

  void stop() override {
    if (_ownsConsumer && _hdr && _shmMutex) {
      const DWORD locked=WaitForSingleObject(_shmMutex,1000);
      if(locked==WAIT_OBJECT_0 || locked==WAIT_ABANDONED) {
        if(_hdr->consumer_pid==GetCurrentProcessId()) _hdr->consumer_active=0;
        ReleaseMutex(_shmMutex);
      }
      _ownsConsumer=false;
    }
    if (_mode == Mode::Tcp && _sock != INVALID_SOCKET && _running) {
      const char *cmd = "STOP\n";
      try { sendAll(cmd, (int)std::strlen(cmd)); } catch (...) {}
    }
    _running = false;
  }

  double actualIqRate() const override {
    const double trueRate = (_mode == Mode::Shm && _hdr && _hdr->rate_hz > 0)
      ? _hdr->rate_hz : _rateActual;
    return _reportTrueRate ? trueRate : trueRate / (double)_displayStride;
  }

  void setCenterHz(double hz) override {
    if (_mode == Mode::Shm) { setFrontend("center_hz",std::to_string(hz)); return; }
    _center = hz;
    if (_mode != Mode::Tcp || _sock == INVALID_SOCKET) return;
    char line[128];
    std::snprintf(line, sizeof(line), "CONFIG center=%.3f rate=%.3f ref=%.3f resource=%s\n",
      _center, _rate, _ref, _resource.c_str());
    sendAll(line, (int)std::strlen(line));
  }

  void setRefLevelDbm(double dbm) override {
    if (_mode == Mode::Shm) { setFrontend("reference_level_dbm",std::to_string(dbm)); return; }
    _ref = dbm;
    setCenterHz(_center);
  }

  int readIq(void *buff, size_t numElems, StreamFormat::Type fmt, long timeoutUs) override {
    if (!_running) return SOAPY_SDR_TIMEOUT;
    if (_mode == Mode::Shm) return readShm(buff, numElems, fmt, timeoutUs);
    return readTcp(buff, numElems, fmt, timeoutUs);
  }

  void setFrontend(const std::string &key, const std::string &value) override {
    if (_mode!=Mode::Shm) throw std::runtime_error("Frontend settings require SHM");
    if (key!="preamp_mode" && key!="reference_level_dbm" && key!="center_hz") throw std::invalid_argument("Unknown frontend setting: "+key);
    if (value.find_first_of("\r\n,= ")!=std::string::npos) throw std::invalid_argument("Invalid frontend value");
    std::lock_guard<std::mutex> guard(_controlMutex);
    _configuring=true;
    try {
      const std::string reply=controlExchange("CONFIG2 "+key+"="+value);
      if (reply.rfind("OK ",0)!=0) throw std::runtime_error(reply);
      const auto values=SoapySDR::KwargsFromString(reply.substr(3));
      _center=std::stod(values.at("center_hz"));
      _rate=_rateActual=std::stod(values.at("rate_hz"));
      _ref=std::stod(values.at("reference_level_dbm"));
      _configuring=false;
    } catch (...) { _configuring=false; throw; }
  }

  std::string frontend(const std::string &key) const override {
    if (_mode!=Mode::Shm || !_hdr) throw std::runtime_error("SHM unavailable");
    if (key=="last_error") {
      const auto reply=controlExchange("GET last_error");
      if (reply.rfind("VALUE ",0)!=0) throw std::runtime_error(reply);
      return reply.substr(6);
    }
    const DWORD locked=WaitForSingleObject(_shmMutex,1000);
    if (locked!=WAIT_OBJECT_0 && locked!=WAIT_ABANDONED) throw std::runtime_error("Sensor ring lock timeout");
    ShmHeader h;
    std::memcpy(&h,const_cast<const ShmHeader *>(_hdr),sizeof(h));
    ReleaseMutex(_shmMutex);
    if (h.frontend_magic!=0x31474643u) throw std::runtime_error("Restart producer: CFG1 frontend protocol required");
    const auto mode=[](int32_t x)->std::string { return x==2500?"off":x==2502?"on":x==2503?"auto":"unknown"; };
    if (key=="preamp_mode") return mode(h.preamp_requested);
    if (key=="preamp_actual") return mode(h.preamp_actual);
    if (key=="preamp_present") return h.preamp_present?"true":"false";
    if (key=="actual_center_hz") return std::to_string(h.center_hz);
    if (key=="actual_rate_sps") return std::to_string(h.rate_hz);
    if (key=="reference_level_dbm" || key=="actual_reference_dbm") return std::to_string(h.reference_dbm);
    if (key=="effective_bandwidth_hz") return std::to_string(h.effective_bandwidth_hz);
    if (key=="active") return h.active && GetTickCount64()-h.heartbeat_ms<2000 ? "true":"false";
    if (key=="healthy") return h.active && h.producer_state==1 && GetTickCount64()-h.heartbeat_ms<2000 ? "true":"false";
    if (key=="producer_state") { const char *names[]={"INITIALIZING","RUNNING","TUNING","RECOVERING","ERROR","STOPPED"}; return h.producer_state<=5?names[h.producer_state]:"UNKNOWN"; }
    if (key=="producer_heartbeat_ms") return std::to_string(GetTickCount64()-h.heartbeat_ms);
    if (key=="consumer_heartbeat_ms") return std::to_string(GetTickCount64()-h.consumer_heartbeat_ms);
    if (key=="drops") return std::to_string(h.drops);
    if (key=="overflow_count") return std::to_string(h.overflow_count);
    if (key=="recoveries") return std::to_string(h.recoveries);
    if (key=="epoch") return std::to_string(h.epoch);
    if (key=="delivered_samples") return std::to_string(h.delivered_samples);
    if (key=="display_skipped") return std::to_string(h.display_skipped);
    if (key=="last_config_ms") return std::to_string(h.last_config_ms);
    if (key=="control_error_count") return std::to_string(h.control_error_count);
    if (key=="last_ni_error") return std::to_string(h.last_ni_error);
    if (key=="request_id") return std::to_string(h.request_id);
    throw std::invalid_argument("Unknown sensor: "+key);
  }

private:
  enum class Mode { None, Shm, Tcp };

  std::string controlExchange(const std::string &command) const {
    SOCKET s=socket(AF_INET,SOCK_STREAM,IPPROTO_TCP);
    if (s==INVALID_SOCKET) throw std::runtime_error("Control socket failed");
    struct CloseSocket { SOCKET s; ~CloseSocket() { closesocket(s); } } closeSocket{s};
    DWORD ms=20000;
    setsockopt(s,SOL_SOCKET,SO_RCVTIMEO,reinterpret_cast<const char *>(&ms),sizeof(ms));
    setsockopt(s,SOL_SOCKET,SO_SNDTIMEO,reinterpret_cast<const char *>(&ms),sizeof(ms));
    sockaddr_in address{}; address.sin_family=AF_INET; address.sin_port=htons((u_short)_controlPort);
    inet_pton(AF_INET,"127.0.0.1",&address.sin_addr);
    if (::connect(s,reinterpret_cast<sockaddr *>(&address),sizeof(address))) throw std::runtime_error("Producer control unavailable");
    const std::string wire=command+"\n";
    size_t sent=0;
    while (sent<wire.size()) {
      const int n=send(s,wire.data()+sent,(int)(wire.size()-sent),0);
      if (n<=0) throw std::runtime_error("Control send failed");
      sent+=(size_t)n;
    }
    std::string reply; char c;
    while (reply.size()<8192) {
      if (recv(s,&c,1,0)!=1) throw std::runtime_error("Control reply missing or timed out");
      if (c=='\n') return reply;
      reply+=c;
    }
    throw std::runtime_error("Control reply too long");
  }

  void controlConfig(double center, double rate, double ref) {
    std::lock_guard<std::mutex> guard(_controlMutex);
    _configuring = true;
    SOCKET s = INVALID_SOCKET;
    try {
      s = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
      if (s == INVALID_SOCKET) throw std::runtime_error("control socket failed");
      DWORD ms = 20000;
      setsockopt(s, SOL_SOCKET, SO_RCVTIMEO, (const char *)&ms, sizeof(ms));
      setsockopt(s, SOL_SOCKET, SO_SNDTIMEO, (const char *)&ms, sizeof(ms));
      sockaddr_in address{};
      address.sin_family = AF_INET; address.sin_port = htons((u_short)_controlPort);
      inet_pton(AF_INET, "127.0.0.1", &address.sin_addr);
      if (::connect(s, (sockaddr *)&address, sizeof(address)) != 0)
        throw std::runtime_error("Live producer control unavailable on 127.0.0.1:" + std::to_string(_controlPort));
      char command[256];
      int length = std::snprintf(command, sizeof(command), "CONFIG %.9f %.9f %.9f\n", center, rate, ref);
      int sent = 0;
      while (sent < length) {
        int n = send(s, command+sent, length-sent, 0);
        if (n <= 0) throw std::runtime_error("control send failed");
        sent += n;
      }
      std::string reply; char c;
      while (reply.size() < 4096 && recv(s, &c, 1, 0) == 1 && c != '\n') reply += c;
      closesocket(s); s = INVALID_SOCKET;
      double appliedCenter, appliedRate, appliedRef; unsigned long long epoch;
      if (std::sscanf(reply.c_str(), "OK %lf %lf %lf %llu", &appliedCenter, &appliedRate, &appliedRef, &epoch) != 4)
        throw std::runtime_error("Hardware tuning failed: " + reply);
      _center = appliedCenter; _rate = appliedRate; _rateActual = appliedRate; _ref = appliedRef;
      _configuring = false;
    } catch (...) {
      if (s != INVALID_SOCKET) closesocket(s);
      _configuring = false;
      throw;
    }
  }

  bool openShm() {
    std::wstring wname(_shmName.begin(), _shmName.end());
    _map = OpenFileMappingW(FILE_MAP_ALL_ACCESS, FALSE, wname.c_str());
    if (!_map) return false;
    _view = MapViewOfFile(_map, FILE_MAP_ALL_ACCESS, 0, 0, 0);
    if (!_view) { CloseHandle(_map); _map = nullptr; return false; }
    _hdr = reinterpret_cast<volatile ShmHeader *>(_view);
    if (_hdr->magic != SHM_MAGIC || _hdr->version != 2) {
      closeShm();
      return false;
    }
    _data = reinterpret_cast<int16_t *>(reinterpret_cast<uint8_t *>(_view) + SHM_HEADER);
    _shmMutex = OpenMutexW(SYNCHRONIZE | MUTEX_MODIFY_STATE, FALSE, (wname + L".lock").c_str());
    if (!_shmMutex) { closeShm(); return false; }
    _center = _hdr->center_hz; _rate = _hdr->rate_hz; _ref = _hdr->reference_dbm;
    return true;
  }

  void closeShm() {
    if (_shmMutex) { CloseHandle(_shmMutex); _shmMutex = nullptr; }
    if (_view) { UnmapViewOfFile(_view); _view = nullptr; }
    if (_map) { CloseHandle(_map); _map = nullptr; }
    _hdr = nullptr; _data = nullptr;
  }

  int readShm(void *buff, size_t numElems, StreamFormat::Type fmt, long timeoutUs) {
    if (!_hdr || !_data) return SOAPY_SDR_STREAM_ERROR;
    if (_displayStride == 1) return readContinuous(buff, numElems, fmt, timeoutUs);
    const DWORD t0 = GetTickCount();
    const DWORD timeoutMs = timeoutUs > 0 ? (DWORD)(timeoutUs / 1000) : 100;
    // Cap per-call delivery so conversion stays short and the GR thread
    // yields; large MTU + Sleep(0) busy-wait was starving the Qt UI on this
    // 4-core host when ring occupancy sat below display_stride between TDMS chunks.
    const uint64_t maxOut = (uint64_t)(std::min)((size_t)_maxOut, numElems);
    while (true) {
      if (!_running || _configuring) return SOAPY_SDR_TIMEOUT;
      DWORD locked = WaitForSingleObject(_shmMutex, 20);
      if (locked != WAIT_OBJECT_0 && locked != WAIT_ABANDONED) return SOAPY_SDR_TIMEOUT;
      // A bounded critical section protects against overwrite and tuning flush.
      struct Unlock { HANDLE h; ~Unlock() { if (h) ReleaseMutex(h); } } unlock{_shmMutex};
      if (_configuring || !_hdr->active || GetTickCount64() - _hdr->heartbeat_ms > 2000) {
        ReleaseMutex(unlock.h); unlock.h = nullptr;
        if (GetTickCount() - t0 >= timeoutMs) return SOAPY_SDR_TIMEOUT;
        waitOneMillisecond(); continue;
      }
      const uint64_t w = _hdr->write_idx;
      uint64_t r = _hdr->read_idx;
      if (_hdr->epoch != _readEpoch || r != _expectedRead) {
        _snapshotRemaining = 0;
        _readEpoch = _hdr->epoch;
      }
      const uint32_t active = _hdr->active;
      if (w < r) {
        // Producer drop-oldest advanced read past us; resync softly.
        r = w;
        _hdr->read_idx = r;
      }
      if (w > r) {
        const uint64_t avail = w - r;
        const uint64_t cap = _hdr->capacity_samples;
        if (cap == 0) return SOAPY_SDR_STREAM_ERROR;
        // Deliver only 1/stride of wall-rate samples while advancing the full
        // 120 MS/s ring. snapshot mode keeps each returned block contiguous,
        // so each GQRX FFT sees the true full-band waveform (gaps are between
        // blocks); decimate mode is retained for narrow-band compatibility.
        const bool snapshot = (_displayMode != "decimate");
        if (snapshot && _snapshotRemaining == 0 && avail >= _snapshotWindow * (uint64_t)_displayStride)
          _snapshotRemaining = _snapshotWindow;
        uint64_t outN = snapshot ? (std::min)(maxOut, _snapshotRemaining)
                                : (std::min)(maxOut, avail / (uint64_t)_displayStride);
        if (outN == 0 && avail > 0 && !snapshot) {
          // Leftover < stride: discard to avoid permanent spin on a few samples.
          _hdr->read_idx = r + avail;
        } else if (outN > 0) {
          auto *outF = reinterpret_cast<float *>(buff);
          auto *outS = reinterpret_cast<int16_t *>(buff);
          if (snapshot || _displayStride == 1) {
            const uint64_t pos = r % cap;
            const uint64_t first = (std::min)(outN, cap-pos);
            copyIq(_data + pos*2, buff, (size_t)first, fmt);
            if (first < outN) {
              void *next = fmt == StreamFormat::CS16 ? static_cast<void *>(outS+first*2) : static_cast<void *>(outF+first*2);
              copyIq(_data, next, (size_t)(outN-first), fmt);
            }
          } else for (uint64_t i = 0; i < outN; i++) {
            const uint64_t srcOff = snapshot ? i : i * (uint64_t)_displayStride;
            const uint64_t pos = (r + srcOff) % cap;
            const int16_t ii = _data[pos * 2];
            const int16_t qq = _data[pos * 2 + 1];
            if (fmt == StreamFormat::CS16) {
              outS[2 * i] = ii; outS[2 * i + 1] = qq;
            } else {
              outF[2 * i] = ii / 32768.0f; outF[2 * i + 1] = qq / 32768.0f;
            }
          }
          uint64_t skipped = outN * ((uint64_t)_displayStride - 1);
          if (snapshot) {
            _snapshotRemaining -= outN;
            skipped = _snapshotRemaining == 0 ? _snapshotWindow * ((uint64_t)_displayStride - 1) : 0;
          }
          // Preserve 65536 contiguous samples across arbitrarily small GNU
          // Radio reads. Skip only BETWEEN complete windows (FFT <=65536).
          _hdr->read_idx = r + outN + skipped;
          _expectedRead = _hdr->read_idx;
          _hdr->delivered_samples += outN;
          _hdr->read_calls += 1;
          _hdr->consumer_heartbeat_ms = GetTickCount64();
          _hdr->display_skipped += skipped;
          return (int)outN;
        }
      } else if (!active) {
        // Producer stopped and ring empty: soft-abort without burning a core.
        return SOAPY_SDR_TIMEOUT;
      }
      if (GetTickCount() - t0 >= timeoutMs) return SOAPY_SDR_TIMEOUT;
      ReleaseMutex(unlock.h); unlock.h = nullptr;
      // Yield ~1ms instead of Sleep(0) spin; keeps Qt UI responsive under LabVIEW+TDMS load.
      waitOneMillisecond();
    }
  }

  int readContinuous(void *buff, size_t count, StreamFormat::Type fmt, long timeoutUs) {
    const ULONGLONG deadline = GetTickCount64() + (timeoutUs > 0 ? (timeoutUs+999)/1000 : 100);
    if (!count) return 0;
    while (_running && !_configuring) {
      // Read-only metadata checks invalidate prefetched data after every retune.
      if (_discardCache.exchange(false) || _cacheEpoch != _hdr->epoch || !_hdr->active) {
        _cacheCount = _cacheOffset = 0;
      }
      if (_cacheOffset < _cacheCount && GetTickCount64()-_hdr->heartbeat_ms < 2000) {
        size_t n = (std::min)((std::min)(count,(size_t)_maxOut),_cacheCount-_cacheOffset);
        copyIq(_cache.data()+2*_cacheOffset,buff,n,fmt);
        _cacheOffset += n;
        // Aligned 64-bit atomic telemetry; acquisition never writes these counters.
        InterlockedAdd64(reinterpret_cast<volatile LONG64 *>(&_hdr->delivered_samples),(LONG64)n);
        InterlockedIncrement64(reinterpret_cast<volatile LONG64 *>(&_hdr->read_calls));
        InterlockedExchange64(reinterpret_cast<volatile LONG64 *>(&_hdr->consumer_heartbeat_ms),(LONG64)GetTickCount64());
        return (int)n;
      }
      DWORD locked = WaitForSingleObject(_shmMutex,20);
      if (locked != WAIT_OBJECT_0 && locked != WAIT_ABANDONED) return SOAPY_SDR_TIMEOUT;
      bool ready = false;
      if (!_configuring && _hdr->active && GetTickCount64()-_hdr->heartbeat_ms < 2000) {
        uint64_t w=_hdr->write_idx, r=_hdr->read_idx, cap=_hdr->capacity_samples;
        if (cap && w>r) {
          size_t n=(size_t)(std::min)(w-r,(uint64_t)262144);
          _cache.resize(n*2);
          size_t pos=(size_t)(r%cap), first=(std::min)(n,(size_t)cap-pos);
          std::memcpy(_cache.data(),_data+pos*2,first*4);
          if (first<n) std::memcpy(_cache.data()+first*2,_data,(n-first)*4);
          _cacheOffset=0; _cacheCount=n; _cacheEpoch=_hdr->epoch;
          _hdr->read_idx=r+n;
          ready=true;
        }
      }
      ReleaseMutex(_shmMutex);
      if (!ready) {
        if (GetTickCount64()>=deadline) return SOAPY_SDR_TIMEOUT;
        waitOneMillisecond();
      }
    }
    return SOAPY_SDR_TIMEOUT;
  }

  int readTcp(void *buff, size_t numElems, StreamFormat::Type fmt, long timeoutUs) {
    if (_sock == INVALID_SOCKET) return SOAPY_SDR_TIMEOUT;
    DWORD ms = timeoutUs > 0 ? (DWORD)(timeoutUs / 1000) : 1000;
    setsockopt(_sock, SOL_SOCKET, SO_RCVTIMEO, (const char *)&ms, sizeof(ms));
    uint32_t hdr[2];
    if (!recvAll(hdr, sizeof(hdr))) return SOAPY_SDR_TIMEOUT;
    if (hdr[0] != 0x50444951u) {
      SoapySDR::log(SOAPY_SDR_ERROR, "dma: bad magic");
      return SOAPY_SDR_STREAM_ERROR;
    }
    uint32_t n = hdr[1];
    if (n == 0) return 0;
    if (n > numElems) n = (uint32_t)numElems;
    std::vector<int16_t> i16(n * 2);
    if (!recvAll(i16.data(), i16.size() * sizeof(int16_t))) return SOAPY_SDR_TIMEOUT;
    if (fmt == StreamFormat::CS16) {
      std::memcpy(buff, i16.data(), n * 2 * sizeof(int16_t));
    } else {
      auto *out = reinterpret_cast<float *>(buff);
      const float scale = 1.0f / 32768.0f;
      for (uint32_t i = 0; i < n; i++) {
        out[2 * i] = i16[2 * i] * scale;
        out[2 * i + 1] = i16[2 * i + 1] * scale;
      }
    }
    return (int)n;
  }

  void connectSocket() {
    if (_sock != INVALID_SOCKET) return;
    SOCKET s = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
    if (s == INVALID_SOCKET) throw std::runtime_error("dma: socket() failed");
    sockaddr_in addr{};
    addr.sin_family = AF_INET;
    addr.sin_port = htons((u_short)_port);
    inet_pton(AF_INET, _host.c_str(), &addr.sin_addr);
    if (::connect(s, (sockaddr *)&addr, sizeof(addr)) != 0) {
      closesocket(s);
      throw std::runtime_error(
        "dma: cannot connect to " + _host + ":" + std::to_string(_port) +
        " — start DmaShmProducer.py (SHM) or bridge first");
    }
    _sock = s;
  }

  void ensureConnected() {
    if (_sock == INVALID_SOCKET) connectSocket();
  }

  void sendAll(const void *p, int n) {
    const char *c = (const char *)p;
    while (n > 0) {
      int k = send(_sock, c, n, 0);
      if (k <= 0) throw std::runtime_error("dma: send failed");
      c += k; n -= k;
    }
  }

  bool recvAll(void *p, size_t n) {
    char *c = (char *)p;
    size_t got = 0;
    while (got < n) {
      int k = recv(_sock, c + got, (int)(n - got), 0);
      if (k <= 0) return false;
      got += (size_t)k;
    }
    return true;
  }

  std::string _host{"127.0.0.1"};
  int _port{19787};
  std::string _shmName{"Local\\vst_live_v2"};
  int _controlPort{19788};
  std::mutex _controlMutex;
  std::atomic<bool> _configuring{false};
  std::atomic<bool> _discardCache{true};
  std::vector<int16_t> _cache;
  size_t _cacheOffset{0}, _cacheCount{0};
  uint64_t _cacheEpoch{0};
  HANDLE _shmMutex{nullptr};
  uint64_t _snapshotRemaining{0}, _expectedRead{0}, _readEpoch{0};
  const uint64_t _snapshotWindow{65536};
  bool _ownsConsumer{false};
  std::string _transport{"shm"};
  int _displayStride{1}; // Full continuous IQ by default; no display gaps.
  std::string _displayMode{"snapshot"};
  int _maxOut{65536};
  bool _reportTrueRate{true}; // GQRX x-axis remains true 120 MHz span
  Mode _mode{Mode::None};
  SOCKET _sock{INVALID_SOCKET};
  HANDLE _map{nullptr};
  void *_view{nullptr};
  volatile ShmHeader *_hdr{nullptr};
  int16_t *_data{nullptr};
  std::string _resource{"RIO0"};
  double _center{1e9}, _rate{120e6}, _rateActual{0}, _ref{0};
  std::atomic<bool> _running{false};
};

} // namespace

std::unique_ptr<IBackend> makeDmaBackend(const SoapySDR::Kwargs &args) {
  return std::unique_ptr<IBackend>(new FpgaDmaBackend(args));
}

} // namespace vst
