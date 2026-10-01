# VST Bridge 2.2.0

NI PXIe-5644R middleware connecting Windows GNU Radio and GQRX through one RFSA/RFSG/FPGA session owner and a native SoapySDR 0.8 x64 plugin.

## Interface and controls

- Equal RX/TX status badges in the upper-right header; RX/TX Start/Stop controls remain permanently visible in the window footer.
- Bridge page configures GQRX/GNU Radio executable paths and launch arguments and launches either application.
- RX/TX numeric controls use matching dark-background/white-text editable dropdowns, including lists and arrow buttons, with suggested values and option-specific hover help; unnecessary inline notes removed.
- TX sample rate: 1–120 MS/s; source queue, DMA FIFO and prefill are configurable. Saved buffer defaults also apply to subsequent live clients.
- Monitor labels, bars and plot headings use independent layout space at default/minimum size. RF checkbox reads Enable RF.
- A stopped/non-sending TX client displays No client data with RF off. Actual hardware faults remain errors; RF never automatically restarts.
- Logs & Debug records plugin verification, control/shared-memory setup, RFSG/RFSA/FPGA initialization, RX/TX configuration and client lifecycle.

## Streaming correction

Ordinary GRC startup is revalidated through the generated script's normal main(), with no special flowgraph wrapper or GNU Radio buffer environment override. Initial prefill has a bounded 15-second allowance; live reserve is enlarged. CF32 conversion uses exact-rounding SSE2, and per-worker high-resolution timers avoid Sleep(1) system-tick delays. Low-rate DMA timeout scales with block drain time. A stopped Hub ring returns STREAM_ERROR instead of repeatedly timing out while filling an orphaned mapping.

Direct native TDMS/CS16 playback, independent RX/TX/duplex operation, four 20 MHz DL FDD TM3.1a 256QAM reference carriers, full-rate spectra/waterfalls, structured logs and snapshot export are retained.

## Assets

VST-Bridge-2.2.0-windows-x64.zip: self-contained application embedding the matching plugin, examples, waveforms, documentation and acceptance evidence.
SoapyVST-2.2.0-windows-x64.zip: standalone native plugin with installation instructions.
VST-Bridge-2.2.0-source.zip: tagged source, tests and documentation.
SHA256SUMS.txt: archive checksums.

NI drivers/bitfile and radioconda are required separately. See docs/VALIDATION.md for measured results, capture provenance and historical failed attempts. Source, local tags and release packages are prepared before upload; GitHub account authentication is required for publication.

## Recorded validation and scope

| Test | Duration / s | RX delivered / MS/s | TX processed / MS/s | Result |
|---|---:|---:|---:|---|
| GNU Radio normal main() | 1200.485 | 120.001120 | 120.001129 | PASS |
| TDMS + GQRX | 1026.875 | 120.001127 | 120.001130 | USER_ACCEPTED (~17 min) |

Final UI-only rebuild: 36 self-tests, 12 actual default/minimum page renders and fresh short real-hardware independent RX/TX test pass. Long runs belong to the earlier executable; streaming core/native module are unchanged. TDMS/GQRX is explicitly USER_ACCEPTED, not a 20-minute automatic PASS. Original interrupted FAIL and the -30 MHz initial spectrum coverage failure (65.142% vs 95%) remain documented. Four actual occupied bands are visible near the end. RF is off after delivery. See ../../VALIDATION.md.
