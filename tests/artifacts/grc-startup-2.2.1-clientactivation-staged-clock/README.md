# Development regression evidence

After a 30.72 MS/s RX run, staged 120 MS/s duplex initially rejected RX because it compared the old RX readback with the new TX clock. Startup failure preserved. Final patch compares stopped RX with the active TX requested rate and rejects mismatched clocks on START.
