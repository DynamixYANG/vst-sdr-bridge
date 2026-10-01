# soapy-vst

Native SoapySDR 0.8 plugin, driver=vst, module=vstSupport.dll. RX readStream reads Hub shared memory and TX writeStream fills the independent Hub TX ring. CF32 and CS16 supported. The normal DMA path never opens a competing NI session. Build through ../build.ps1. Explicit legacy Fetch support remains for historical use only. See ../docs/ARCHITECTURE.md.


2.2: TX rates are 1–120 MS/s. Live activation uses saved Bridge queue/FIFO/prefill defaults; the application and DLL must match for TXIDLE and TXDEFAULTS support. CF32 conversion uses exact-rounding SSE2; the standalone equivalence/throughput regression is in tests/. Empty-ring/backpressure waits use per-thread high-resolution timers. A stopped Hub ring returns STREAM_ERROR rather than filling an orphaned mapping and repeatedly returning TIMEOUT. The client must explicitly deactivate/reactivate after a Bridge stop.

In 2.2.1, opening/configuring a device or setting up a stream leaves Hub RX stopped. RX activateStream explicitly sends START. Staged CONFIG/CONFIG2 replies remain parseable; inactive getters retain requested values, while active streams use hardware readback. Use the matching 2.2.1 Hub and plugin.
