# Control API

Connect to `127.0.0.1:19788`, send one UTF-8 line, read one line, then close. STATUS is read-only and does not consume IQ. Mutation replies begin with OK or ERR.

```text
STATUS
START
STOP
CONFIG2 center_hz=2500000000,rate_hz=120000000,reference_level_dbm=-20,preamp_mode=auto
TXSTART {"source":"file","waveform_path":"C:\\waveforms\\example.tdms","center_hz":2500000000,"rate_hz":120000000,"peak_dbm":-10,"rf_enabled":true}
TXSTOP
SHUTDOWN
```

START/STOP apply to RX only. TXSTART is asynchronous: an acknowledgement means queued startup; wait for `tx.status=STREAMING` and inspect `tx.error`. TXSTOP stops only TX and invokes RF-off. SHUTDOWN releases both directions and the shared session. RF is disabled by default. File source accepts TDMS or CS16 plus a same-basename JSON sidecar.

The versioned JSON envelope is also supported:

```json
{"version":1,"id":"42","method":"rx.configure","params":{"center_hz":2500000000,"reference_level_dbm":-20,"preamp_mode":"auto"}}
```

See `src/Vst.Core/ControlServer.cs` for methods and response fields. Soapy TX source uses `source=live_ring`, `ring_name=Local\vst_tx_v1`, `ring_mi_b=128`, `queue_mi_b=128`. Producers must obey the shared ring protocol; use native Soapy `writeStream` rather than writing raw mapped bytes.
