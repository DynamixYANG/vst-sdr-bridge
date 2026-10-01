#include "SoapyVst.hpp"
#include <algorithm>
#include <cmath>
#include <stdexcept>
#include <cctype>
#include <cstdlib>

namespace {
bool parseBool(const std::string &value) {
  std::string s;
  s.reserve(value.size());
  for (unsigned char c : value) s.push_back(static_cast<char>(std::tolower(c)));
  if (s=="1" || s=="true" || s=="yes" || s=="on") return true;
  if (s=="0" || s=="false" || s=="no" || s=="off") return false;
  throw std::invalid_argument("expected true/false: "+value);
}
}

using namespace vst;

Device::Device(const SoapySDR::Kwargs &args) : _args(args)
{
  // GNU Radio queries this preference when its graph allocates buffers, after
  // constructing the source. Process-local only; preserve explicit user values.
  if (!std::getenv("GR_CONF_DEFAULT_BUFFER_SIZE")) _putenv_s("GR_CONF_DEFAULT_BUFFER_SIZE","1048576");
  if (args.count("resource")) _resource = args.at("resource");
  if (args.count("rate")) _iqRate = std::stod(args.at("rate"));
  if (args.count("backend")) {
    const auto &b = args.at("backend");
    if (b == "dma" || b == "fpga" || b == "FPGA") _backendKind = BackendKind::Dma;
    else _backendKind = BackendKind::Fetch;
  }
  if (args.count("serial")) { /* ignore */ }
  ensureBackend();
  if (_backendKind==BackendKind::Dma && (!args.count("transport") || args.at("transport")!="tcp")) {
    const double initialCenter=std::stod(_backend->frontend("actual_center_hz"));
    // A cold, stopped Hub has no RFSA readback yet (zero in the ring).
    if(initialCenter>=65e6 && initialCenter<=6e9) _centerHz=initialCenter;
    _refLevel=std::stod(_backend->frontend("actual_reference_dbm"));
    if (!args.count("rate")) _iqRate=std::stod(_backend->frontend("actual_rate_sps"));
    if (args.count("reference_level_dbm")) writeSetting("reference_level_dbm",args.at("reference_level_dbm"));
    if (args.count("preamp_mode")) writeSetting("preamp_mode",args.at("preamp_mode"));
  }
  if (args.count("peak_dbm")) _txPeakDbm = std::stod(args.at("peak_dbm"));
  if (args.count("rf_enabled")) _txRfEnabled = parseBool(args.at("rf_enabled"));
  if (args.count("tx_center")) _txCenterHz = std::stod(args.at("tx_center"));
  if (args.count("tx_rate")) _txRate = std::stod(args.at("tx_rate"));
  if (args.count("tx_ring")) _txRing = args.at("tx_ring");
  if (args.count("tx_ring_mib")) _txRingMiB = std::stoi(args.at("tx_ring_mib"));
  if (args.count("digital_gain")) _txDigitalGain = std::stod(args.at("digital_gain"));
  if (args.count("control_port")) _txControlPort = std::stoi(args.at("control_port"));
  if (args.count("hub_host")) _txHost = args.at("hub_host");
  SoapySDR::logf(SOAPY_SDR_INFO, "vst: resource=%s backend=%s tx_ring=%s",
    _resource.c_str(), _backendKind == BackendKind::Dma ? "dma" : "fetch", _txRing.c_str());
}

Device::~Device()
{
  try { if (_txOwns.load()) stopTx(); } catch (...) {}
  std::lock_guard<std::mutex> lock(_mutex);
  delete _rxStream; _rxStream = nullptr;
  delete _txStream; _txStream = nullptr;
  if (_backend) {
    try { _backend->stop(); } catch (...) {}
    try { _backend->close(); } catch (...) {}
    _backend.reset();
  }
}

void Device::ensureBackend()
{
  if (_backend) return;
  if (_backendKind == BackendKind::Dma)
    _backend = makeDmaBackend(_args);
  else
    _backend = makeFetchBackend();
  _backend->open(_resource);
}

void Device::reconfigureIfActive()
{
  if (!_streamSetup || !_backend) return;
  bool was = _streamActive;
  if (was) _backend->stop();
  _backend->configure(_centerHz, _iqRate, _refLevel, _blockSamples);
  if (was) {
    _backend->start();
    _iqRate = _backend->actualIqRate() > 0 ? _backend->actualIqRate() : _iqRate;
  }
}

SoapySDR::Kwargs Device::getHardwareInfo() const
{
  SoapySDR::Kwargs info;
  info["resource"] = _resource;
  info["backend"] = _backendKind == BackendKind::Dma ? "dma" : "fetch";
  info["manufacturer"] = "NI";
  info["product"] = "PXIe-5644R";
  info["fetch_ceiling_note"] = "~90e6 measured on this host via RFSA Fetch";
  info["dma_target_note"] = "1e6..120e6 native FIFO to memory; one IQ consumer";
  info["iq_units"] = "normalized raw IQ; not calibrated volts or dBm";
  info["tx_path"] = "writeStream -> Local\\vst_tx_v1 live_ring -> Hub; RF default off";
  info["tx_rx"] = "independent; TX level is PeakDbm, not RX RefLevel";
  return info;
}

size_t Device::getNumChannels(const int direction) const
{
  return (direction == SOAPY_SDR_RX || direction == SOAPY_SDR_TX) ? 1 : 0;
}

bool Device::getFullDuplex(const int, const size_t) const { return true; }

std::vector<std::string> Device::listGains(const int direction, const size_t) const
{
  if (direction == SOAPY_SDR_TX) return {"PeakDbm"};
  return {"RefLevel"};
}

void Device::setGain(const int direction, const size_t channel, const double value)
{
  setGain(direction, channel, direction == SOAPY_SDR_TX ? "PeakDbm" : "RefLevel", value);
}

void Device::setGain(const int direction, const size_t, const std::string &name, const double value)
{
  if (direction == SOAPY_SDR_TX) {
    if (name != "PeakDbm" && name != "PEAK" && name != "peak_dbm" && !name.empty())
      throw std::invalid_argument("Unknown TX gain name: "+name+"; use PeakDbm (dBm)");
    if (!std::isfinite(value) || value < -50.0 || value > 0.0)
      throw std::invalid_argument("TX peak level: -50 to 0 dBm");
    bool restart = false;
    {
      std::lock_guard<std::mutex> lock(_mutex);
      _txPeakDbm = value;
      restart = _txActive;
    }
    if (restart) startTx();
    return;
  }
  if (name != "RefLevel" && name != "REFLEVEL" && name != "reference_level_dbm" && !name.empty())
    throw std::invalid_argument("Unknown gain name: "+name+"; use RefLevel (dBm)");
  std::lock_guard<std::mutex> lock(_mutex);
  if (_backendKind == BackendKind::Dma && _backend) {
    try {
      _backend->setRefLevelDbm(value);
      _refLevel = value;
      _lastError.clear();
    } catch (const std::exception &e) {
      _lastError = e.what();
      SoapySDR::log(SOAPY_SDR_ERROR, _lastError);
    }
    return;
  }
  _refLevel = value;
  if (_backend) {
    try { _backend->setRefLevelDbm(_refLevel); }
    catch (...) { reconfigureIfActive(); }
  }
}

double Device::getGain(const int direction, const size_t channel) const
{
  return getGain(direction, channel, direction == SOAPY_SDR_TX ? "PeakDbm" : "RefLevel");
}

double Device::getGain(const int direction, const size_t, const std::string &name) const
{
  if (direction == SOAPY_SDR_TX) {
    if (name != "PeakDbm" && name != "PEAK" && name != "peak_dbm" && !name.empty())
      throw std::invalid_argument("Unknown TX gain name: "+name+"; use PeakDbm (dBm)");
    return _txPeakDbm;
  }
  if (name!="RefLevel" && name!="REFLEVEL" && name!="reference_level_dbm" && !name.empty()) throw std::invalid_argument("Unknown gain name: "+name);
  if (_backendKind==BackendKind::Dma && _streamActive) return std::stod(_backend->frontend("actual_reference_dbm"));
  return _refLevel;
}

SoapySDR::Range Device::getGainRange(const int direction, const size_t channel) const
{
  return getGainRange(direction, channel, "RefLevel");
}

SoapySDR::Range Device::getGainRange(const int direction, const size_t, const std::string &) const
{
  if (direction == SOAPY_SDR_TX) return SoapySDR::Range(-50.0, 0.0);
  return SoapySDR::Range(-50.0, 30.0);
}

void Device::setFrequency(const int direction, const size_t channel, const double frequency, const SoapySDR::Kwargs &args)
{
  setFrequency(direction, channel, "RF", frequency, args);
}

void Device::setFrequency(const int direction, const size_t, const std::string &, const double frequency, const SoapySDR::Kwargs &)
{
  if (direction == SOAPY_SDR_TX) {
    if (!std::isfinite(frequency) || frequency < 65e6 || frequency > 6e9)
      throw std::invalid_argument("TX frequency: 65 MHz to 6 GHz");
    bool restart = false;
    {
      std::lock_guard<std::mutex> lock(_mutex);
      _txCenterHz = frequency;
      restart = _txActive;
    }
    if (restart) startTx();
    return;
  }
  std::lock_guard<std::mutex> lock(_mutex);
  if (_backendKind == BackendKind::Dma && _backend) {
    try {
      _backend->setCenterHz(frequency);
      _centerHz = frequency;
      _lastError.clear();
    } catch (const std::exception &e) {
      // Qt/GQRX does not catch exceptions from tuning callbacks. Retain the
      // last applied value and expose the failure, instead of killing its UI.
      _lastError = e.what();
      SoapySDR::log(SOAPY_SDR_ERROR, _lastError);
    }
    return;
  }
  _centerHz = frequency;
  if (_backend) {
    try { _backend->setCenterHz(_centerHz); }
    catch (...) { reconfigureIfActive(); }
  }
}

double Device::getFrequency(const int direction, const size_t channel) const
{
  return getFrequency(direction, channel, "RF");
}

double Device::getFrequency(const int direction, const size_t, const std::string &) const
{
  if (direction == SOAPY_SDR_TX) return _txCenterHz;
  if (_backendKind==BackendKind::Dma && _streamActive) return std::stod(_backend->frontend("actual_center_hz"));
  return _centerHz;
}

std::vector<std::string> Device::listFrequencies(const int, const size_t) const
{
  return {"RF"};
}

SoapySDR::RangeList Device::getFrequencyRange(const int direction, const size_t channel) const
{
  return getFrequencyRange(direction, channel, "RF");
}

SoapySDR::RangeList Device::getFrequencyRange(const int, const size_t, const std::string &) const
{
  // PXIe-5644R: 65 MHz – 6 GHz
  return {SoapySDR::Range(65e6, 6e9)};
}

void Device::setSampleRate(const int direction, const size_t, const double rate)
{
  if (direction == SOAPY_SDR_TX) {
    if (!std::isfinite(rate) || rate < 1e6 || rate > 120e6)
      throw std::invalid_argument("TX sample rate must be 1 to 120 MS/s");
    bool restart = false;
    {
      std::lock_guard<std::mutex> lock(_mutex);
      _txRate = rate;
      restart = _txActive;
    }
    if (restart) startTx();
    return;
  }
  std::lock_guard<std::mutex> lock(_mutex);
  if (_backendKind==BackendKind::Dma) {
    try {
      _backend->configure(_centerHz,rate,_refLevel,_blockSamples);
      _iqRate=_streamActive?std::stod(_backend->frontend("actual_rate_sps")):rate;
      _lastError.clear();
    } catch (const std::exception &e) { _lastError=e.what(); SoapySDR::log(SOAPY_SDR_ERROR,_lastError); }
    return;
  }
  _iqRate = rate;
  // Auto-hint: >95 MS/s prefer dma if user did not force fetch
  if (_backendKind == BackendKind::Fetch && rate > 95e6) {
    SoapySDR::log(SOAPY_SDR_WARNING,
      "vst: requested rate >95 MS/s on fetch backend; host Fetch ceiling ~90 MS/s. "
      "Use backend=dma + bridge for 120 MS/s path.");
  }
  reconfigureIfActive();
}

double Device::getSampleRate(const int direction, const size_t) const
{
  if (direction == SOAPY_SDR_TX) return _txRate;
  if (_backendKind==BackendKind::Dma && _streamActive) return std::stod(_backend->frontend("actual_rate_sps"));
  return _iqRate;
}

SoapySDR::RangeList Device::getSampleRateRange(const int direction, const size_t) const
{
  if (direction == SOAPY_SDR_TX) return {SoapySDR::Range(1e6, 120e6)};
  return {SoapySDR::Range(1e6, 120e6)};
}

std::vector<double> Device::listSampleRates(const int direction, const size_t) const
{
  if (direction == SOAPY_SDR_TX) return {1e6,5e6,10e6,20e6,30.72e6,40e6,60e6,61.44e6,80e6,100e6,120e6};
  return {1e6, 5e6, 10e6, 20e6, 40e6, 80e6, 90e6, 100e6, 120e6};
}

void Device::setBandwidth(const int direction, const size_t, const double bw)
{
  if (direction == SOAPY_SDR_TX) return;
  // Fixed analog path; 0 means automatic. Do not advertise a fictitious filter.
  const double actual=getBandwidth(SOAPY_SDR_RX,0);
  if (!std::isfinite(bw) || (bw!=0 && std::abs(bw-actual)>1)) {
    std::lock_guard<std::mutex> lock(_mutex);
    _lastError="Analog bandwidth is fixed; use 0 (auto) or the actual bandwidth. Use a GNU Radio FIR for digital filtering.";
    SoapySDR::log(SOAPY_SDR_ERROR,_lastError);
  }
}

double Device::getBandwidth(const int direction, const size_t) const
{
  if (direction == SOAPY_SDR_TX) return _txRate;
  return _backendKind==BackendKind::Dma ? std::stod(_backend->frontend("effective_bandwidth_hz")) : 80e6;
}

SoapySDR::RangeList Device::getBandwidthRange(const int direction, const size_t) const
{
  const double bandwidth=getBandwidth(direction,0);
  return {SoapySDR::Range(bandwidth,bandwidth)};
}

std::vector<std::string> Device::listAntennas(const int direction, const size_t) const
{
  return {direction == SOAPY_SDR_TX ? "RF_OUT" : "RF_IN"};
}

void Device::setAntenna(const int direction, const size_t, const std::string &name) {
  const char *expect = direction == SOAPY_SDR_TX ? "RF_OUT" : "RF_IN";
  if (!name.empty() && name != expect) throw std::invalid_argument(std::string("Only ")+expect+" is supported");
}

std::string Device::getAntenna(const int direction, const size_t) const {
  return direction == SOAPY_SDR_TX ? "RF_OUT" : "RF_IN";
}

SoapySDR::ArgInfoList Device::getSettingInfo() const
{
  SoapySDR::ArgInfoList infos;
  SoapySDR::ArgInfo b;
  b.key = "backend";
  b.value = _backendKind == BackendKind::Dma ? "dma" : "fetch";
  b.name = "Backend";
  b.description = "fetch = NI-RFSA Fetch (<=~90 MS/s); dma = FPGA DMA bridge (target 120 MS/s)";
  b.type = SoapySDR::ArgInfo::STRING;
  b.options = {"fetch", "dma"};
  infos.push_back(b);

  SoapySDR::ArgInfo r;
  r.key = "resource";
  r.value = _resource;
  r.name = "Resource";
  r.description = "NI-RFSA resource name (e.g. RIO0)";
  r.type = SoapySDR::ArgInfo::STRING;
  infos.push_back(r);
  if (_backendKind==BackendKind::Dma) {
    SoapySDR::ArgInfo ref;
    ref.key="reference_level_dbm"; ref.name="Reference level"; ref.units="dBm";
    ref.type=SoapySDR::ArgInfo::FLOAT; ref.range=SoapySDR::Range(-50,30);
    ref.value=_backend->frontend(ref.key);
    ref.description="Maximum expected RF input level. Lower values generally increase sensitivity; not gain in dB.";
    infos.push_back(ref);
    SoapySDR::ArgInfo preamp;
    preamp.key="preamp_mode"; preamp.name="RF preamplifier"; preamp.type=SoapySDR::ArgInfo::STRING;
    preamp.options={"auto"}; preamp.optionNames={"Automatic (5644R)"};
    preamp.value=_backend->frontend(preamp.key);
    preamp.description="5644R permits Automatic only. Set reference level and read preamp_actual for committed hardware state.";
    infos.push_back(preamp);
  }
  SoapySDR::ArgInfo rf;
  rf.key = "rf_enabled";
  rf.value = _txRfEnabled ? "true" : "false";
  rf.name = "TX RF output";
  rf.description = "TX RF gate into Hub. Default false. Does not change RX.";
  rf.type = SoapySDR::ArgInfo::STRING;
  rf.options = {"false", "true"};
  infos.push_back(rf);
  SoapySDR::ArgInfo peak;
  peak.key = "peak_dbm";
  peak.value = std::to_string(_txPeakDbm);
  peak.name = "TX peak power";
  peak.units = "dBm";
  peak.description = "RFSG peak power. Not RX reference level.";
  peak.type = SoapySDR::ArgInfo::FLOAT;
  peak.range = SoapySDR::Range(-50, 0);
  infos.push_back(peak);
  SoapySDR::ArgInfo dig;
  dig.key = "digital_gain";
  dig.value = std::to_string(_txDigitalGain);
  dig.name = "TX digital gain";
  dig.description = "Scale applied to TX IQ before CS16. 1.0 is full scale.";
  dig.type = SoapySDR::ArgInfo::FLOAT;
  infos.push_back(dig);
  return infos;
}

void Device::writeSetting(const std::string &key, const std::string &value)
{
  if (key=="rf_enabled" || key=="peak_dbm" || key=="digital_gain" || key=="tx_center_hz" || key=="tx_rate_hz" || key=="tx_ring") {
    bool restart = false;
    {
      std::lock_guard<std::mutex> lock(_mutex);
      if (key=="rf_enabled") _txRfEnabled = parseBool(value);
      else if (key=="peak_dbm") {
        const double v = std::stod(value);
        if (!std::isfinite(v) || v < -50.0 || v > 0.0) throw std::invalid_argument("TX peak level: -50 to 0 dBm");
        _txPeakDbm = v;
      } else if (key=="digital_gain") {
        _txDigitalGain = std::stod(value);
      } else if (key=="tx_center_hz") {
        const double v = std::stod(value);
        if (!std::isfinite(v) || v < 65e6 || v > 6e9) throw std::invalid_argument("TX frequency: 65 MHz to 6 GHz");
        _txCenterHz = v;
      } else if (key=="tx_rate_hz") {
        const double v = std::stod(value);
        if (!std::isfinite(v) || v < 1e6 || v > 120e6) throw std::invalid_argument("TX sample rate must be 1 to 120 MS/s");
        _txRate = v;
      } else if (key=="tx_ring") {
        if (_txActive) throw std::runtime_error("cannot change tx_ring while TX streaming");
        if (value.empty()) throw std::invalid_argument("tx_ring empty");
        _txRing = value;
      }
      restart = _txActive && key != "digital_gain" && key != "tx_ring";
    }
    if (restart) startTx();
    return;
  }
  std::lock_guard<std::mutex> lock(_mutex);
  if (key=="reference_level_dbm" || key=="preamp_mode") {
    try {
      _backend->setFrontend(key,value);
      _centerHz=std::stod(_backend->frontend("actual_center_hz"));
      _iqRate=std::stod(_backend->frontend("actual_rate_sps"));
      _refLevel=std::stod(_backend->frontend("actual_reference_dbm"));
      _lastError.clear();
    } catch (const std::exception &e) { _lastError=e.what(); throw; }
    return;
  }
  if (key == "backend") {
    BackendKind nk = (value == "dma" || value == "fpga") ? BackendKind::Dma : BackendKind::Fetch;
    if (nk != _backendKind) {
      if (_streamActive) throw std::runtime_error("cannot switch backend while streaming");
      if (_backend) { _backend->close(); _backend.reset(); }
      _backendKind = nk;
      ensureBackend();
    }
  } else if (key == "resource") {
    if (value!=_resource) throw std::invalid_argument("Resource is initialization-only; reopen the device");
  } else throw std::invalid_argument("Unknown setting: "+key);
}

std::string Device::readSetting(const std::string &key) const
{
  if (key == "rf_enabled") return _txRfEnabled ? "true" : "false";
  if (key == "peak_dbm") return std::to_string(_txPeakDbm);
  if (key == "digital_gain") return std::to_string(_txDigitalGain);
  if (key == "tx_center_hz") return std::to_string(_txCenterHz);
  if (key == "tx_rate_hz") return std::to_string(_txRate);
  if (key == "tx_ring") return _txRing;
  if (key == "tx_accepted") return std::to_string(_txAccepted.load());
  if (key == "tx_clips") return std::to_string(_txClips.load());
  if (key == "last_error") return readSensor(key);
  if (key == "backend") return _backendKind == BackendKind::Dma ? "dma" : "fetch";
  if (key == "resource") return _resource;
  if (key=="reference_level_dbm" || key=="preamp_mode") return _backend->frontend(key);
  throw std::invalid_argument("Unknown setting: "+key);
}

std::vector<std::string> Device::listSensors() const {
  if (_backendKind!=BackendKind::Dma) return {};
  return {"actual_center_hz","actual_rate_sps","actual_reference_dbm","effective_bandwidth_hz",
          "preamp_mode","preamp_actual","preamp_present","active","healthy","producer_state",
          "producer_heartbeat_ms","consumer_heartbeat_ms","drops","overflow_count","recoveries","epoch",
          "delivered_samples","display_skipped","last_config_ms","control_error_count","last_ni_error","request_id","last_error"};
}

SoapySDR::ArgInfo Device::getSensorInfo(const std::string &key) const {
  const auto keys=listSensors();
  if (std::find(keys.begin(),keys.end(),key)==keys.end()) throw std::invalid_argument("Unknown sensor: "+key);
  SoapySDR::ArgInfo info; info.key=key; info.name=key; info.type=SoapySDR::ArgInfo::INT;
  if (key=="preamp_mode" || key=="preamp_actual" || key=="producer_state" || key=="last_error") info.type=SoapySDR::ArgInfo::STRING;
  else if (key=="active" || key=="healthy" || key=="preamp_present") info.type=SoapySDR::ArgInfo::BOOL;
  else if (key=="actual_center_hz" || key=="effective_bandwidth_hz") { info.type=SoapySDR::ArgInfo::FLOAT; info.units="Hz"; }
  else if (key=="actual_reference_dbm") { info.type=SoapySDR::ArgInfo::FLOAT; info.units="dBm"; }
  else if (key=="actual_rate_sps") { info.type=SoapySDR::ArgInfo::FLOAT; info.units="S/s"; }
  else if (key.find("_ms")!=std::string::npos) { info.type=SoapySDR::ArgInfo::FLOAT; info.units="ms"; }
  // GNU Radio 3.10's Python binding reads ArgInfo.value rather than readSensor.
  // Keep it live, and retain 64-bit counters as decimal strings (its INT cast
  // is only 32-bit on Windows).
  if (key=="drops" || key=="overflow_count" || key=="recoveries" || key=="epoch" ||
      key=="delivered_samples" || key=="display_skipped" || key=="control_error_count" || key=="request_id")
    info.type=SoapySDR::ArgInfo::STRING;
  info.value=readSensor(key);
  info.description="Read-only live producer state. Counters are cumulative; compare deltas over an observation interval.";
  return info;
}

std::string Device::readSensor(const std::string &key) const {
  if (key=="last_error") {
    std::lock_guard<std::mutex> lock(_mutex);
    if (!_lastError.empty()) return _lastError;
  }
  return _backend->frontend(key);
}
