#pragma once
#include <SoapySDR/Device.hpp>
#include <SoapySDR/Logger.hpp>
#include <SoapySDR/Formats.hpp>
#include <atomic>
#include <memory>
#include <mutex>
#include <string>
#include <vector>
#include <cstdint>

namespace vst {

enum class BackendKind { Fetch, Dma };

struct StreamFormat {
  enum Type { CF32, CS16 } type = CF32;
};

/** Abstract IQ acquisition backend. */
class IBackend {
public:
  virtual ~IBackend() = default;
  virtual void open(const std::string &resource) = 0;
  virtual void close() = 0;
  virtual void configure(double centerHz, double iqRate, double refLevelDbm,
                         size_t blockSamples) = 0;
  virtual void start() = 0;
  virtual void stop() = 0;
  virtual double actualIqRate() const = 0;
  /** Read up to numElems complex samples into buff (format depends on fmt).
   *  Returns samples read, or negative Soapy error code. */
  virtual int readIq(void *buff, size_t numElems, StreamFormat::Type fmt,
                     long timeoutUs) = 0;
  virtual void setCenterHz(double hz) = 0;
  virtual void setRefLevelDbm(double dbm) = 0;
  virtual std::string name() const = 0;
  virtual void setFrontend(const std::string &, const std::string &) { throw std::runtime_error("Frontend settings require DMA backend"); }
  virtual std::string frontend(const std::string &) const { throw std::runtime_error("Frontend sensors require DMA backend"); }
};

std::unique_ptr<IBackend> makeFetchBackend();
std::unique_ptr<IBackend> makeDmaBackend(const SoapySDR::Kwargs &args);

class Device : public SoapySDR::Device {
public:
  explicit Device(const SoapySDR::Kwargs &args);
  ~Device() override;

  std::string getDriverKey() const override { return "vst"; }
  std::string getHardwareKey() const override { return "PXIe-5644R"; }
  SoapySDR::Kwargs getHardwareInfo() const override;

  size_t getNumChannels(const int direction) const override;
  bool getFullDuplex(const int direction, const size_t channel) const override;

  std::vector<std::string> getStreamFormats(const int direction, const size_t channel) const override;
  std::string getNativeStreamFormat(const int direction, const size_t channel, double &fullScale) const override;

  SoapySDR::Stream *setupStream(const int direction, const std::string &format,
      const std::vector<size_t> &channels = {},
      const SoapySDR::Kwargs &args = {}) override;
  void closeStream(SoapySDR::Stream *stream) override;
  size_t getStreamMTU(SoapySDR::Stream *stream) const override;
  int activateStream(SoapySDR::Stream *stream, const int flags = 0,
                     const long long timeNs = 0, const size_t numElems = 0) override;
  int deactivateStream(SoapySDR::Stream *stream, const int flags = 0,
                       const long long timeNs = 0) override;
  int readStream(SoapySDR::Stream *stream, void *const *buffs, const size_t numElems,
                 int &flags, long long &timeNs, const long timeoutUs = 100000) override;
  int writeStream(SoapySDR::Stream *stream, const void *const *buffs, const size_t numElems,
                  int &flags, const long long timeNs = 0, const long timeoutUs = 100000) override;

  std::vector<std::string> listGains(const int direction, const size_t channel) const override;
  void setGain(const int direction, const size_t channel, const double value) override;
  void setGain(const int direction, const size_t channel, const std::string &name, const double value) override;
  double getGain(const int direction, const size_t channel) const override;
  double getGain(const int direction, const size_t channel, const std::string &name) const override;
  SoapySDR::Range getGainRange(const int direction, const size_t channel) const override;
  SoapySDR::Range getGainRange(const int direction, const size_t channel, const std::string &name) const override;
  bool hasGainMode(const int direction, const size_t channel) const override { return false; }

  void setFrequency(const int direction, const size_t channel, const double frequency,
                    const SoapySDR::Kwargs &args = {}) override;
  void setFrequency(const int direction, const size_t channel, const std::string &name,
                    const double frequency, const SoapySDR::Kwargs &args = {}) override;
  double getFrequency(const int direction, const size_t channel) const override;
  double getFrequency(const int direction, const size_t channel, const std::string &name) const override;
  std::vector<std::string> listFrequencies(const int direction, const size_t channel) const override;
  SoapySDR::RangeList getFrequencyRange(const int direction, const size_t channel) const override;
  SoapySDR::RangeList getFrequencyRange(const int direction, const size_t channel, const std::string &name) const override;

  void setSampleRate(const int direction, const size_t channel, const double rate) override;
  double getSampleRate(const int direction, const size_t channel) const override;
  SoapySDR::RangeList getSampleRateRange(const int direction, const size_t channel) const override;
  std::vector<double> listSampleRates(const int direction, const size_t channel) const override;

  void setBandwidth(const int direction, const size_t channel, const double bw) override;
  double getBandwidth(const int direction, const size_t channel) const override;
  SoapySDR::RangeList getBandwidthRange(const int direction, const size_t channel) const override;

  std::vector<std::string> listAntennas(const int direction, const size_t channel) const override;
  void setAntenna(const int direction, const size_t channel, const std::string &name) override;
  std::string getAntenna(const int direction, const size_t channel) const override;

  SoapySDR::ArgInfoList getSettingInfo() const override;
  void writeSetting(const std::string &key, const std::string &value) override;
  std::string readSetting(const std::string &key) const override;
  SoapySDR::ArgInfoList getSettingInfo(const int, const size_t) const override { return getSettingInfo(); }
  void writeSetting(const int, const size_t, const std::string &key, const std::string &value) override { writeSetting(key,value); }
  std::string readSetting(const int, const size_t, const std::string &key) const override { return readSetting(key); }
  std::vector<std::string> listSensors() const override;
  SoapySDR::ArgInfo getSensorInfo(const std::string &key) const override;
  std::string readSensor(const std::string &key) const override;
  std::vector<std::string> listSensors(const int, const size_t) const override { return listSensors(); }
  SoapySDR::ArgInfo getSensorInfo(const int, const size_t, const std::string &key) const override { return getSensorInfo(key); }
  std::string readSensor(const int, const size_t, const std::string &key) const override { return readSensor(key); }

private:
  void ensureBackend();
  void reconfigureIfActive();
  void startTx();
  void stopTx();
  std::string txConfigJson() const;
  struct TxProducer;
  struct StreamTag {
    int direction;
    StreamFormat::Type fmt;
  };

  mutable std::mutex _mutex;
  SoapySDR::Kwargs _args;
  std::string _resource{"RIO0"};
  BackendKind _backendKind{BackendKind::Dma};
  std::unique_ptr<IBackend> _backend;
  StreamFormat::Type _fmt{StreamFormat::CF32};
  double _centerHz{1e9};
  double _iqRate{10e6};
  double _refLevel{0.0}; // dBm (mapped as Soapy "gain" / RefLevel)
  double _bandwidth{0.0};
  size_t _blockSamples{65536};
  size_t _mtu{65536};
  bool _streamActive{false};
  bool _streamSetup{false};
  std::string _lastError;
  std::mutex _txLife;
  std::shared_ptr<TxProducer> _txProducer;
  StreamTag *_rxStream{nullptr};
  StreamTag *_txStream{nullptr};
  std::string _txHost{"127.0.0.1"};
  int _txControlPort{19788};
  std::string _txRing{"Local\\vst_tx_v1"};
  int _txRingMiB{128};
  double _txCenterHz{2.5e9};
  double _txRate{120e6};
  double _txPeakDbm{-30.0};
  double _txDigitalGain{1.0};
  bool _txRfEnabled{false};
  bool _txActive{false};
  bool _txSetup{false};
  std::atomic<bool> _txOwns{false};
  std::atomic<uint64_t> _txAccepted{0};
  std::atomic<uint64_t> _txClips{0};
  size_t _txMtu{262144};
};

} // namespace vst
