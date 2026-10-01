# VST Bridge 2.1.0

NI PXIe-5644R middleware for GNU Radio and GQRX on Windows x64.

* Unified bridge dashboard, configurable application launchers, independent RX/TX control, transport counters, logs and diagnostic export.
* Native SoapySDR RX readStream and TX writeStream with CF32/CS16 and shared-session duplex operation.
* Direct TDMS I/Q playback, strict sample-rate/data validation, and CS16 compatibility.
* Golden 120 MS/s GNU Radio duplex example and four-carrier 80 MHz nominal DL FDD TM3.1a stimulus.
* Two completed 20-minute hardware runs with zero underflow, active RX loss, overflow or recovery; real GNU Radio and GQRX application captures included.
* Corrected pending RX settings while stopped and protected shared-clock changes during TX.
* Organized source, build scripts, current tests, documentation and recoverable local historical archives.

The GQRX transport gate passed directly; RF spectrum acceptance includes a disclosed post-run robust band-coverage review because an external narrow tone biased the initial average-power check. See docs/VALIDATION.md for all evidence and limitations.

Assets: VST-Bridge-2.1.0-windows-x64.zip (application, examples, waveforms, documentation and selected evidence); SoapyVST-2.1.0-windows-x64.zip (native plugin and install instructions); source snapshot and SHA256SUMS.txt. NI drivers/bitfile and radioconda are external prerequisites. VSTHub.exe and driver=vst retain compatibility. The capture-only GQRX executable is not distributed.
