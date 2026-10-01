#include "SoapyVst.hpp"
#include <SoapySDR/Errors.hpp>
#include <algorithm>
#include <stdexcept>

using namespace vst;

std::vector<std::string> Device::getStreamFormats(const int direction, const size_t) const
{
  if (direction != SOAPY_SDR_RX && direction != SOAPY_SDR_TX) return {};
  return {SOAPY_SDR_CF32, SOAPY_SDR_CS16};
}

std::string Device::getNativeStreamFormat(const int direction, const size_t, double &fullScale) const
{
  if (direction != SOAPY_SDR_RX && direction != SOAPY_SDR_TX)
    throw std::runtime_error("unsupported direction");
  fullScale = 32768.0;
  return SOAPY_SDR_CS16;
}

SoapySDR::Stream *Device::setupStream(const int direction, const std::string &format,
    const std::vector<size_t> &channels, const SoapySDR::Kwargs &)
{
  if (direction != SOAPY_SDR_RX && direction != SOAPY_SDR_TX)
    throw std::runtime_error("unsupported direction");
  if (!channels.empty() && !(channels.size() == 1 && channels[0] == 0))
    throw std::runtime_error("vst supports one channel per direction");
  StreamFormat::Type fmt;
  if (format == SOAPY_SDR_CS16) fmt = StreamFormat::CS16;
  else if (format == SOAPY_SDR_CF32) fmt = StreamFormat::CF32;
  else throw std::runtime_error("unsupported format: " + format);

  std::lock_guard<std::mutex> lock(_mutex);
  if (direction == SOAPY_SDR_TX) {
    if (_txStream) throw std::runtime_error("Only one TX stream per Device is supported");
    _txStream = new StreamTag{SOAPY_SDR_TX, fmt};
    _txSetup = true;
    _txMtu = 262144;
    SoapySDR::log(SOAPY_SDR_INFO, "vst tx: setupStream TX -> Hub live_ring (not a second RF stack)");
    return reinterpret_cast<SoapySDR::Stream *>(_txStream);
  }
  if (_rxStream || _streamSetup) throw std::runtime_error("Only one RX stream per Device is supported");
  ensureBackend();
  _fmt = fmt;
  _backend->configure(_centerHz, _iqRate, _refLevel, _blockSamples);
  _streamSetup = true;
  _mtu = (_backendKind == BackendKind::Dma) ? 65536 : _blockSamples;
  _rxStream = new StreamTag{SOAPY_SDR_RX, fmt};
  return reinterpret_cast<SoapySDR::Stream *>(_rxStream);
}

void Device::closeStream(SoapySDR::Stream *stream)
{
  auto *tag = reinterpret_cast<StreamTag *>(stream);
  if (tag && tag->direction == SOAPY_SDR_TX) {
    if (_txOwns.load()) stopTx();
    std::lock_guard<std::mutex> lock(_mutex);
    if (_txStream == tag) {
      delete _txStream;
      _txStream = nullptr;
      _txSetup = false;
    }
    return;
  }
  std::lock_guard<std::mutex> lock(_mutex);
  if (_backend && _streamActive) {
    _backend->stop();
    _streamActive = false;
  }
  _streamSetup = false;
  if (_rxStream && _rxStream == tag) {
    delete _rxStream;
    _rxStream = nullptr;
  }
}

size_t Device::getStreamMTU(SoapySDR::Stream *stream) const
{
  auto *tag = reinterpret_cast<StreamTag *>(stream);
  if (tag && tag->direction == SOAPY_SDR_TX) return _txMtu;
  return _mtu;
}

int Device::activateStream(SoapySDR::Stream *stream, const int, const long long, const size_t)
{
  auto *tag = reinterpret_cast<StreamTag *>(stream);
  if (tag && tag->direction == SOAPY_SDR_TX) {
    try {
      startTx();
      return 0;
    } catch (const std::exception &ex) {
      std::lock_guard<std::mutex> lock(_mutex);
      _lastError = ex.what();
      SoapySDR::log(SOAPY_SDR_ERROR, _lastError);
      return SOAPY_SDR_STREAM_ERROR;
    }
  }
  std::lock_guard<std::mutex> lock(_mutex);
  ensureBackend();
  _backend->configure(_centerHz, _iqRate, _refLevel, _blockSamples);
  _backend->start();
  double actual = _backend->actualIqRate();
  if (actual > 0) _iqRate = actual;
  _streamActive = true;
  return 0;
}

int Device::deactivateStream(SoapySDR::Stream *stream, const int, const long long)
{
  auto *tag = reinterpret_cast<StreamTag *>(stream);
  if (tag && tag->direction == SOAPY_SDR_TX) {
    if (_txOwns.load()) stopTx();
    return 0;
  }
  std::lock_guard<std::mutex> lock(_mutex);
  if (_backend) _backend->stop();
  _streamActive = false;
  return 0;
}

int Device::readStream(SoapySDR::Stream *stream, void *const *buffs, const size_t numElems,
                       int &flags, long long &timeNs, const long timeoutUs)
{
  flags = 0;
  timeNs = 0;
  auto *tag = reinterpret_cast<StreamTag *>(stream);
  if (tag && tag->direction == SOAPY_SDR_TX) return SOAPY_SDR_NOT_SUPPORTED;
  IBackend *backend = nullptr;
  StreamFormat::Type fmt = StreamFormat::CF32;
  size_t n = numElems;
  {
    std::lock_guard<std::mutex> lock(_mutex);
    if (!_backend || !_streamActive) return SOAPY_SDR_TIMEOUT;
    backend = _backend.get();
    fmt = _fmt;
    if (n > _mtu) n = _mtu;
  }
  return backend->readIq(buffs[0], n, fmt, timeoutUs);
}
