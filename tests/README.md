# tests

Maintained validation tools. bridge_soak.py monitors hardware counters without consuming IQ; run_grc_acceptance.py runs the actual generated Qt GUI for 1,200 seconds and saves widget grabs; validate_independence.py proves TX-only and start/stop independence. WaveformTests plus make_tdms_fixtures.py verify native TDMS layouts/rejections. Existing validate_hub_soapy.py, validate_duplex.py and Soapy smoke/soak scripts cover lower-level regressions. artifacts contains raw evidence, not claims inferred from screenshots.

run_gqrx_acceptance.py tests native TDMS playback with the capture-only GQRX build described in GQRX-CAPTURE.md. rx_spectrum_probe.py reads independent RX snapshots without taking the consumer cursor; review_rf_spectrum.py reproduces the wideband ON/OFF review from saved NPZ files. The original arithmetic-power result is retained when a narrow interferer biases it.
