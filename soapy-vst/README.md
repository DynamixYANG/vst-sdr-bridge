# soapy-vst

Native SoapySDR 0.8 plugin, driver=vst, module=vstSupport.dll. RX readStream reads Hub shared memory and TX writeStream fills the independent Hub TX ring. CF32 and CS16 supported. The normal DMA path never opens a competing NI session. Build through ../build.ps1. Explicit legacy Fetch support remains for historical use only. See ../docs/ARCHITECTURE.md.
