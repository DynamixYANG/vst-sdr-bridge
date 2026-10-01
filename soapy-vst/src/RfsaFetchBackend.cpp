#include "SoapyVst.hpp"
#include <niRFSA.h>
#include <SoapySDR/Errors.hpp>
#include <cstring>
#include <stdexcept>
#include <vector>

#ifndef NIRFSA_VAL_IQ
#define NIRFSA_VAL_IQ 100
#endif

namespace vst {

namespace {
constexpr ViAttr ATTR_IQ_RATE = (ViAttr)1150007;
constexpr ViAttr ATTR_HOST_DMA_BUFFER_SIZE = (ViAttr)1150285;

class RfsaFetchBackend : public IBackend {
public:
  ~RfsaFetchBackend() override { close(); }

  std::string name() const override { return "fetch"; }

  void open(const std::string &resource) override {
    close();
    _resource = resource;
    ViStatus st = niRFSA_init((ViRsrc)_resource.c_str(), VI_TRUE, VI_FALSE, &_session);
    if (st < VI_SUCCESS) throwError("niRFSA_init", st);
  }

  void close() override {
    if (_session) {
      niRFSA_Abort(_session);
      niRFSA_close(_session);
      _session = 0;
    }
  }

  void configure(double centerHz, double iqRate, double refLevelDbm, size_t blockSamples) override {
    if (!_session) throw std::runtime_error("fetch backend not open");
    _block = blockSamples;
    ViStatus st;
    st = niRFSA_ConfigureAcquisitionType(_session, NIRFSA_VAL_IQ);
    if (st < VI_SUCCESS) throwError("ConfigureAcquisitionType", st);
    st = niRFSA_ConfigureReferenceLevel(_session, "", refLevelDbm);
    if (st < VI_SUCCESS) throwError("ConfigureReferenceLevel", st);
    st = niRFSA_ConfigureIQCarrierFrequency(_session, "", centerHz);
    if (st < VI_SUCCESS) throwError("ConfigureIQCarrierFrequency", st);
    st = niRFSA_ConfigureIQRate(_session, "", iqRate);
    if (st < VI_SUCCESS) throwError("ConfigureIQRate", st);
    st = niRFSA_ConfigureNumberOfSamples(_session, "", VI_FALSE, (ViInt64)blockSamples);
    if (st < VI_SUCCESS) throwError("ConfigureNumberOfSamples", st);
    // Optional host DMA buffer — may return unsupported; non-fatal.
    niRFSA_SetAttributeViInt64(_session, "", ATTR_HOST_DMA_BUFFER_SIZE, (ViInt64)(256LL * 1024 * 1024));
    _center = centerHz;
    _rateReq = iqRate;
    _ref = refLevelDbm;
  }

  void start() override {
    if (!_session) throw std::runtime_error("fetch backend not open");
    niRFSA_Abort(_session);
    ViStatus st = niRFSA_Initiate(_session);
    if (st < VI_SUCCESS) throwError("niRFSA_Initiate", st);
    ViReal64 rate = 0;
    st = niRFSA_GetAttributeViReal64(_session, "", ATTR_IQ_RATE, &rate);
    if (st < VI_SUCCESS) throwError("Get IQ rate", st);
    _rateActual = rate;
    SoapySDR::logf(SOAPY_SDR_INFO, "vst fetch: actual IQ rate = %.3f MS/s", _rateActual / 1e6);
  }

  void stop() override {
    if (_session) niRFSA_Abort(_session);
  }

  double actualIqRate() const override { return _rateActual; }

  void setCenterHz(double hz) override {
    _center = hz;
    if (!_session) return;
    ViStatus st = niRFSA_ConfigureIQCarrierFrequency(_session, "", hz);
    if (st < VI_SUCCESS) throwError("setCenterHz", st);
  }

  void setRefLevelDbm(double dbm) override {
    _ref = dbm;
    if (!_session) return;
    ViStatus st = niRFSA_ConfigureReferenceLevel(_session, "", dbm);
    if (st < VI_SUCCESS) throwError("setRefLevel", st);
  }

  int readIq(void *buff, size_t numElems, StreamFormat::Type fmt, long timeoutUs) override {
    if (!_session || numElems == 0) return 0;
    double timeoutS = timeoutUs > 0 ? (timeoutUs / 1e6) : 1.0;
    niRFSA_wfmInfo info{};
    ViStatus st;
    if (fmt == StreamFormat::CS16) {
      st = niRFSA_FetchIQSingleRecordComplexI16(
          _session, "", 0, (ViInt64)numElems, timeoutS,
          reinterpret_cast<NIComplexI16 *>(buff), &info);
    } else {
      st = niRFSA_FetchIQSingleRecordComplexF32(
          _session, "", 0, (ViInt64)numElems, timeoutS,
          reinterpret_cast<NIComplexNumberF32 *>(buff), &info);
    }
    if (st < VI_SUCCESS) {
      // Underflow / timeout style — map to Soapy timeout or overflow.
      if (st == -1074118643 /* timeout-ish */ || st == (ViStatus)0xFFFA5E85)
        return SOAPY_SDR_TIMEOUT;
      char msg[2048] = {};
      ViStatus code = 0;
      niRFSA_GetError(_session, &code, sizeof(msg), msg);
      SoapySDR::logf(SOAPY_SDR_ERROR, "Fetch failed %d: %s", (int)st, msg);
      niRFSA_Abort(_session);
      niRFSA_Initiate(_session);
      return SOAPY_SDR_STREAM_ERROR;
    }
    if (info.actualSamples > 0) return (int)info.actualSamples;
    return (int)numElems;
  }

private:
  void throwError(const char *op, ViStatus st) {
    char msg[2048] = {};
    ViStatus code = 0;
    if (_session) niRFSA_GetError(_session, &code, sizeof(msg), msg);
    throw std::runtime_error(std::string(op) + " failed: " + std::to_string((int)st) + " " + msg);
  }

  ViSession _session{0};
  std::string _resource;
  size_t _block{65536};
  double _center{1e9}, _rateReq{10e6}, _rateActual{0}, _ref{0};
};

} // namespace

std::unique_ptr<IBackend> makeFetchBackend() {
  return std::unique_ptr<IBackend>(new RfsaFetchBackend());
}

} // namespace vst
