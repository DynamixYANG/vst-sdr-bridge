# Architecture

## Ownership and direction independence

`NiDeviceSession` opens RFSA, RFSG and the shared FPGA once. `HubEngine` owns the RX worker, serializes control requests, handles configuration readback/rollback and keeps the session available when RX is stopped. `TxEngine` uses a separate above-normal-priority worker, a producer thread and the output DMA FIFO. Neither GUI painting nor JSON log writing runs on the DMA hot path.

RX and TX start/stop independently. RX center/reference may change while TX streams. Changing the RX sample rate while TX is active is rejected because the hardware clock/streaming bitfile is shared. Resizing the RX ring requires both directions stopped because it recreates the engine. RX settings entered while stopped remain pending until START; monitors retain actual readback until then.

## RX

`NiRxHardware` drains `streaming input.dma` in 1,048,576-sample blocks. `SharedIqRing` publishes packed CS16 into `Local\vst_live_v2`, with a configurable 64–1024 MiB capacity. The native Soapy plugin claims one consumer PID and maintains a heartbeat. It supports CF32 conversion, retune epochs and stale-consumer reclamation. Idle overwrites with no client are intentional and are separate from active drops.

## TX

`WaveformFile` validates CS16 sidecar hashes or reads standard TDMS I/Q segments into CS16. The repeat source feeds `TxSampleQueue`. Alternatively, native Soapy `writeStream` fills `Local\vst_tx_v1`; CF32 conversion reuses thread-local scratch. Defaults are 128 MiB live ring, 128 MiB queue, 256 MiB host FIFO and 64 MiB prefill. Full buffers apply backpressure.

`NiTxHardware` resolves `streaming output.dma` by name, configures 120 MS/s, writes and selects a unit-peak timing arb, resets and primes the DMA path, and explicitly enables RF only when requested. The timing arb establishes RFSG peak-power scaling; FPGA DMA replaces its sample content. Named waveforms are cleared after Abort before rewriting, preventing NI -370024 on repeated starts.

Underflow, source timeout or DMA failure latches FAULT and invokes the shutdown sequence. There is no automatic TX replay after failure. A stopped/closed live Soapy TX client sends TXSTOP; closing an RX-only client does not stop TX.

## Control, UI and evidence

Loopback TCP port 19788 serves legacy commands and versioned JSON requests. Each connection carries one command/response. Mutations execute on the RX owner thread; TX starts asynchronously and must be followed through `tx.status`.

The WinForms application has Bridge, RX Monitor, TX Monitor, RX Configuration, TX Configuration and Logs & Debug pages. It launches GQRX or GRC, exports snapshots, displays pipeline rates and queue reserves, and preserves drafts in configuration controls. An asynchronous bounded logger rotates UTC JSONL files. Status snapshots are atomic files, not synchronization primitives.

Native plugin deployment compares SHA-256 with the embedded DLL, backs up previous versions and atomically replaces the destination. Close client applications before updating a loaded DLL. A full build compiles the native plugin before embedding it; matching hashes eliminate the old manual-copy workaround.
