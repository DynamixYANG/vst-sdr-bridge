# vst_tx_sink (Hub live_ring)

Design: `docs/VST-TX-SINK-DESIGN.md`.

## Architecture

- Hub owns NI-RFSG / FPGA TX DMA.
- `TXSTART {"source":"live_ring",...}` creates `Local\vst_tx_v1`.
- Producers: GNU Radio `vst_tx_sink` (Python), or native `tests/FeedTxLiveRing`.
- TX settings (CF, 120 MS/s, peak_dbm, rf_enabled) are independent of RX.

## Full-rate proof (RF off)

```powershell
# Hub already running
$sdk = '.\tools\dotnet\dotnet.exe'   # or dotnet
& $sdk run --project .\tests\FeedTxLiveRing\FeedTxLiveRing.csproj -c Release -- 15
# Expect processed_msps ~120, underflows 0, rf_enabled false; TXSTOP leaves RF off.
```

Evidence: `tests/artifacts/tx-live-ring-native-20260930/`.

## GNU Radio / GRC

```powershell
$rc = 'C:\Users\yang\radioconda'
$root = 'C:\Users\yang\Documents\vst-sdr-bridge'
$env:PATH = "$rc;$rc\Library\bin;$rc\Scripts;" + $env:PATH
$env:PYTHONPATH = "$root\gr-vst\python"
$env:GRC_BLOCKS_PATH = "$root\gr-vst\grc;$root\soapy-vst\grc"
& "$rc\python.exe" "$root\gr-vst\examples\run_tx_tone.py" --seconds 5 --peak-dbm -30
```

**Rate note:** The Python `vst_tx_sink` mirrors RX-style `set_*` APIs and correctly drives Hub live_ring, but a pure-Python `work()` path does **not** sustain 120 MS/s (Hub will FAULT on underflow). For continuous 120 MS/s use `FeedTxLiveRing` / file `TXSTART` today, or the implemented native Soapy `writeStream` module (recommended; see ../examples/grc). Keep `rf_enabled=False` unless a rated RF path is connected.

## API

| Control | Method |
|---|---|
| Center frequency | `set_center_freq` / `set_freq` |
| Sample rate | `set_samp_rate` (120e6 only) |
| Output power | `set_peak_dbm` / `set_gain` (peak dBm, not RX RefLevel) |
| RF enable | `set_rf_enabled` (default False) |
| Digital FS scale | `set_digital_gain` |
