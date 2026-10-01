# tests

Current 2.2 acceptance: run_grc_normal_acceptance.py launches the canonical generated script's normal main(), captures its actual Qt window every 30 seconds, and observes 1,200 seconds of RX/TX hardware counters. No special wrapper or GNU Radio buffer environment override is used. run_gqrx_acceptance.py accepts --name/--seconds for a fresh TDMS/GQRX run; its capture-only receiver provenance is in GQRX-CAPTURE.md.

validate_tx_controls.py checks real TX rates at 1/30.72/60/100/120 MS/s, custom 64 MiB queue/128 MiB DMA FIFO allocation, graceful client stop and cessation of IQ writes. validate_tx_startup.py checks delayed first data and writes after an explicit stop. bridge_soak.py uses each independent worker's snapshot timestamp for its counter rate; TCP observation time has up to 0.5 s of counter publication age.

Maintained validation tools. bridge_soak.py monitors hardware counters without consuming IQ; the former run_grc_acceptance.py wrapper is archived under archive/validation-before-2.2/scripts; validate_independence.py proves TX-only and start/stop independence. WaveformTests plus make_tdms_fixtures.py verify native TDMS layouts/rejections. Existing validate_hub_soapy.py, validate_duplex.py and Soapy smoke/soak scripts cover lower-level regressions. artifacts contains raw evidence, not claims inferred from screenshots.

run_gqrx_acceptance.py tests native TDMS playback with the capture-only GQRX build described in GQRX-CAPTURE.md. rx_spectrum_probe.py reads independent RX snapshots without taking the consumer cursor; review_rf_spectrum.py reproduces the wideband ON/OFF review from saved NPZ files. The original arithmetic-power result is retained when a narrow interferer biases it.

validate_idle_startup.py validates real-device cold STOPPED state, no RX FIFO/IQ publication, idle configuration, TX before the first RX start, and independent START/STOP at 120 MS/s. Use a freshly launched Hub; RF remains off in this test.

validate_soapy_idle.py checks native idle configuration/getters and explicit RX activation. run_grc_normal_acceptance.py --client-start-rx begins with STOP rather than prestarting RX, records prelaunch status and binary SHA-256, and exercises the normal generated main().
