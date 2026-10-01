# Cold startup regression

Seven real-device checks, snapshots and binary identities in result.json. Cold initialization keeps both directions stopped, RX FIFO unallocated and no IQ publication. Apply RX while stopped only stages settings. TX alone runs at 120 MS/s before the first RX start; explicit RX START and independent stops pass. RF output is off throughout. This is a short regression.
