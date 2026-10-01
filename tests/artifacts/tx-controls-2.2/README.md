# TX controls hardware regression

result.json contains five actual FPGA counter-rate checks (1, 30.72, 60, 100, 120 MS/s), verifies a configured 64 MiB source queue and 128 MiB DMA FIFO in every run, and checks normal no-data/graceful-close WAITING_CLIENT with RF off. RF remained off throughout. Benchmark output checks bit-exact scalar/SSE2 conversion and reports local CPU throughput; it is not a hardware streaming result.
