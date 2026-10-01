# waveform

The project's TX waveform library. All maintained paths use this singular directory name; the immutable v2.2.1 release archives retain their original `waveforms/` layout.

| Files | Purpose |
|---|---|
| `nr-tm3.1a-fdd-4x20mhz-120msps.tdms`, `.cs16`, `.json` | Recommended four-carrier, nominal 80 MHz NR stimulus. Direct TDMS playback and equivalent CS16 share the reference/provenance recorded in JSON. |
| `cw-1mhz-120msps.cs16`, `.json` | Simple 120 MS/s tone stimulus. |
| `reference/` | Pinned 20 MHz source grid and upstream MIT license for reproducible generation. |
| `legacy/nr-tm3.1a-fdd-100mhz-120msps.cs16`, `.json` | Previously selected TX file, moved from the retired Stream Demo folder with its SHA-256 metadata unchanged. |

See [TDMS guide](../docs/TDMS.md) and [requirements](../docs/REQUIREMENTS.md). Playback needs no generator/Python service. Every CS16 file requires its matching JSON sidecar.
