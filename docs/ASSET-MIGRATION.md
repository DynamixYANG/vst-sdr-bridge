# Local assets and Documents cleanup — 2 October 2026

Current local project root: `C:/Users/yang/Documents/vst-sdr-bridge`.

## Retained assets

| Asset | Current project path | SHA-256 |
|---|---|---|
| Previously selected TX IQ, with unchanged JSON sidecar | `waveform/legacy/nr-tm3.1a-fdd-100mhz-120msps.cs16` | `8ea6553b510859d50a6ec07fc5e5fe8fbdf33165ed50ba907e6e5051a293a424` |
| Golden four-carrier TDMS, with equivalent CS16 and metadata | `waveform/nr-tm3.1a-fdd-4x20mhz-120msps.tdms` | `54d1d5766dafef4a861d0c533cd5c1ebf822f1a23b21f62fe53aa0b4fa5a7075` |
| NI FPGA bitfile local archive | `hardware/ni-pxie-5644r/local/NI Streaming for VST.lvbitx` | `e4c77f0a3b9b00c785be60c9be2f598c6a2412835397284feb0346bec05297f4` |

All formerly maintained `waveforms/` files were moved to the singular `waveform/`; generator, current test scripts, license notices and future packaging paths were updated. Historical test logs/screenshots and immutable release archives retain their original paths. The 100 MHz file is a historical stimulus, not the accepted 80 MHz four-carrier example.

The saved `%LOCALAPPDATA%/VSTHub/settings.json` now points to the project TX file and local FPGA bitfile; previous settings were backed up and RF remains disabled. NI-installed runtime DLLs, the driver-installed named bitfile and radioconda clients remain external installed dependencies, detailed in [REQUIREMENTS.md](REQUIREMENTS.md).

## Cleanup

The retired Documents folders `PXIe5644R`, `PXIe5644R Stream Demo`, `PXIe5644R Streaming Host 2.1` and `PXIe5644R ZeroCopy` are absent after cleanup. The root `PXIe5644R-Streaming-Host-2.1-Source.zip` was archived too. Early deployment/host records, NI/LabVIEW reference examples and remaining old waveform references were retained locally under `archive/documents-before-cleanup-20261002/` before removal. Large raw captures, old executables, duplicate context/installer archives and the validation environment were deleted. Approximately 29.76 GiB of logical file data was removed, separately from the earlier legacy-workspace cleanup. The local historical/vendor archive is excluded from Git.

## Verification and scope

* Dependency audit passed: project-local TX/metadata/bitfile hashes and installed NI/radioconda paths exist; the installed VST plugin matches the embedded-build DLL.
* The production `WaveformFile.Load` parser loaded the relocated 100 MHz CS16 and golden four-carrier TDMS at 120 MS/s; each decoded to 1,200,000 complex samples. TDMS decoded IQ equals its supplied CS16 counterpart.
* After old-folder cleanup, the shipped EXE completed a 20-second headless device initialization/idle probe using the archived bitfile. NI RFSA/RFSG/shared FPGA sessions opened, RX/TX remained STOPPED, RX samples remained zero, RF was disabled and no ERROR events were logged. The probe was then closed.
* PowerShell script syntax and Git whitespace checks passed. No new long-duration TX/RX or RF spectrum acceptance is claimed by this file/layout change.

The shipped EXE SHA-256 remains `6b5a21ff376676317e6246003909973d382b823d792ff248febca6976d4bf392`; native DLL remains `63e2bae5fdeb8fc45cc117f70e97493889e4999b2430b5c16ad512278234c40a`. Existing release assets/tags were not replaced.

## Public repository policy

The exact NI package's original sample-code license is retained with its manifest. Its distribution terms do not give this project a general GPL/public redistribution grant. The bitfile binary remains in the ignored local archive; GitHub contains provenance, license and verified import tools. Public packages explicitly omit the binary. Obtain it through NI and retain its installed original for the shipped driver's filename-based initialization.
