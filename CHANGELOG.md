# Changelog

## 2.2.0 — 2026-10-01

Permanent RX/TX footer controls, consistent dark editable dropdowns, launch configuration, status badges, monitor layout and initialization logs. Configurable TX rate/queue/FIFO/prefill; no-client idle state. Ordinary GRC TIMEOUT corrected and normal-main 20-minute run passed; TDMS/GQRX ~17-minute interval explicitly accepted by the operator, with original limitations retained.

## 2.1.0 — VST Bridge

- Reposition desktop UI as GNU Radio / GQRX bridge with application choice and pipeline overview.
- Preserve native Soapy RX/TX and independent direction controls.
- Add native direct TDMS I/Q playback and a documented validation profile.
- Add four 20 MHz DL FDD NR TM3.1a carriers at 120 MS/s.
- Export diagnostic snapshots; retain live events and rotating logs.
- Apply RX settings saved while stopped on the next Start; preserve input drafts.
- Prevent buffer resize from unexpectedly stopping TX.
- Embed the current plugin in a reproducible full build.
- Organize current source/examples/evidence and preserve historical experiments in archive.
