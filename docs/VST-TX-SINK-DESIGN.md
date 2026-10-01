# Live TX interface

The native SoapySDR sink is the maintained full-rate TX path. `setupStream(TX)` selects CF32 or CS16; `activateStream` starts the Hub live ring; `writeStream` publishes bounded CS16 IQ; `deactivateStream` sends TXSTOP and turns RF off. It does not open another NI session.

| Control | Meaning |
|---|---|
| `setFrequency(TX, 0, hz)` | Independent TX center, 65 MHz to 6 GHz |
| `setSampleRate(TX, 0, 120e6)` | Current supported TX rate |
| `setGain(TX, 0, "PeakDbm", dbm)` | RFSG peak level, −50 to 0 dBm |
| `writeSetting("rf_enabled", "true")` | Explicit RF output gate; defaults false |
| `RF_OUT` | TX antenna/port name |
| `digital_gain` | Optional digital scale; clipped samples are counted |

`Local\vst_tx_v1` is a single-producer/single-consumer ring with a named mutex and monotonically advancing indices. Full buffers apply backpressure. The default ring/queue are 128 MiB each; the host FIFO is 256 MiB. Hub workers consume 1,048,576-sample blocks. RX uses its own `Local\vst_live_v2` ring.

CF32 conversion reuses thread-local storage. The reference GRC example precomputes a periodic tone and configures TX source/sink scheduling at 262,144 samples with above-normal thread priority. This avoids small work calls and priority starvation on the tested four-core Windows host. See the actual long-test record in VALIDATION.md; source rate alone does not prove RF output.

The optional Python `vst_tx_sink` mirrors these controls but is not the recommended sustained 120 MS/s producer. Its `start` returns after obtaining the producer ring/heartbeat so GNU Radio can schedule `work`; it must not wait for hardware STREAMING before work can provide prefill. Legacy design notes are preserved locally under archive/documentation-2.0.
