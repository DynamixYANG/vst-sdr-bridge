# GQRX acceptance capture provenance

Windows desktop capture returned `IGraphicsCaptureItemInterop.CreateForMonitor: 0x80070057`, and pointer access returned `GetCursorPos: 0x80070005`. The test therefore captures application-rendered pixels, not the desktop.

GNU Radio evidence uses `QWidget.grab()` on the actual running generated flowgraph. For GQRX, the same application-owned capture is implemented in an acceptance-only local build of upstream **v2.17.6**, matching the installed radioconda version. Source commit: `c287a1eb5bc46c6fdbef6db9f750cfb0749cf3d5`, repository <https://github.com/gqrx-sdr/gqrx>.

`gqrx-capture-instrumentation.patch` adds a 30-second Qt timer activated only by `VST_GQRX_CAPTURE_DIR`. It saves the live window's `grab()` to PNG. A `close.request` file in that directory closes the test app on the next timer tick. Two Windows build fixes select the MSVC resource compiler syntax and correct the case of the RDS export macro. No RF, receiver, FFT, waterfall or DSP logic is modified.

The test build links the installed radioconda GNU Radio 3.10.12.0, gr-osmosdr 0.2.6, Qt 5.15 and Volk libraries. The missing Boost 1.86 headers were extracted into the scratch build area from the conda-forge `libboost-headers-1.86.0-h57928b3_3` package; the installed runtime was not upgraded. SHA-256 hashes of test binaries are recorded in `artifacts/release-binary-hashes.json`.

This test-only executable is not installed over the user's GQRX and is not bundled in the product release. Evidence labeled `gqrx-render-*.png` is an actual GQRX application render. `rx-*.png` from `rx_spectrum_probe.py` is a separately calculated diagnostic from real RX IQ, explicitly not a GQRX screenshot. The distinction is retained in validation reports.
