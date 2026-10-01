# Legacy waveform retained for playback

`nr-tm3.1a-fdd-100mhz-120msps.cs16` and its original JSON were moved from the old Stream Demo deployment without changing their bytes. The saved local Hub selection remains this same waveform at its new project path. Its SHA-256 is `8ea6553b510859d50a6ec07fc5e5fe8fbdf33165ed50ba907e6e5051a293a424`.

The JSON records 120 MS/s, 1,200,000 complex samples, the python_5gtoolbox generator commit/reference and digital checks. The upstream MIT notice is retained at ../reference/LICENSE.txt. This 100 MHz stimulus exceeds the PXIe-5644R's nominal 80 MHz analog bandwidth and is historical, not the accepted four-carrier golden example. For the recommended stimulus select ../nr-tm3.1a-fdd-4x20mhz-120msps.tdms.
