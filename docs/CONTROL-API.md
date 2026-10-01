# Control API

Connect to `127.0.0.1:19788`, send one UTF-8 line, read one line, then close. STATUS is read-only and does not consume IQ. Mutation replies begin with OK or ERR.

```text
STATUS
START
STOP
CONFIG2 center_hz=2500000000,rate_hz=120000000,reference_level_dbm=-20,preamp_mode=auto
TXSTART {"source":"file","waveform_path":"C:\\waveforms\\example.tdms","center_hz":2500000000,"rate_hz":120000000,"peak_dbm":-10,"rf_enabled":true}
TXSTOP
TXIDLE
TXDEFAULTS {"queue_mi_b":64,"fifo_mi_b":128,"prefill_blocks":16}
SHUTDOWN
```

START/STOP apply to RX only. TXSTART is asynchronous: an acknowledgement means queued startup; wait for `tx.status=STREAMING` and inspect `tx.error`. TXSTOP stops only TX and invokes RF-off. SHUTDOWN releases both directions and the shared session. RF is disabled by default. File source accepts TDMS or CS16 plus a same-basename JSON sidecar.

The versioned JSON envelope is also supported:

```json
{"version":1,"id":"42","method":"rx.configure","params":{"center_hz":2500000000,"reference_level_dbm":-20,"preamp_mode":"auto"}}
```

See `src/Vst.Core/ControlServer.cs` for methods and response fields. Soapy TX source uses `source=live_ring`, `ring_name=Local\vst_tx_v1`, `ring_mi_b=128`, `queue_mi_b=128`. Producers must obey the shared ring protocol; use native Soapy `writeStream` rather than writing raw mapped bytes.

## TX lifecycle and buffer defaults (2.2)

TXIDLE is sent by a normally stopping Soapy client. It stops TX/RF and reports WAITING_CLIENT (No client data). Missing producer heartbeat or an empty live source similarly becomes idle; hardware failures with an active producer remain FAULT. TXSTOP is the explicit operator stop. Neither idle nor failure automatically restarts RF.

TXDEFAULTS updates queue/FIFO/prefill defaults for the next live client activation. Apply TX saves these settings on disk and sends TXDEFAULTS. A live TXSTART that explicitly supplies queue_mi_b, fifo_mi_b or prefill_blocks overrides those defaults. File playback always uses the supplied configuration. The client controls live TX center/rate/peak/RF settings; the TX Configuration radio fields configure file playback.

TX rate is 1–120 MS/s with exact readback within 1 Hz; file metadata must match. Queue is 16–256 MiB in multiples of 4; host FIFO is 64–512 MiB; file prefill is 4–32 blocks of 4 MiB and strictly less than the FIFO. Live startup uses max(requested, min(48, FIFO MiB/4 - 1)) blocks, allowing a bounded 15-second first-data window. Steady live source starvation is limited to 500 ms. At lower rates, DMA backpressure timeout accounts for one block's drain time plus 500 ms.
