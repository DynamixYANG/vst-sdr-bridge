# Third-party notices

## Default project license

Project-owned source, scripts and documentation are licensed under **GPL-3.0-or-later**, unless a file carries a different notice or is identified below. See [LICENSE](LICENSE), [NOTICE](NOTICE), and the limited additional [NI driver linking permission](LICENSE-EXCEPTIONS.md). Copyright 2026 DynamixYANG and VST Bridge contributors. This grant covers project-owned contributions; it does not replace third-party rights or notices. Hobby and commercial use are permitted under the GPL terms.

## Files with retained licenses

| Material | Applicable license / notice |
|---|---|
| `examples/grc/vst_bridge_120_duplex.py` and its companion `.grc` flowgraph | GPL-3.0; the generated Python's original SPDX header is retained. Full text: `LICENSES/GPL-3.0.txt`. |
| `tests/gqrx-capture-instrumentation.patch` | GPL-3.0-or-later, following patched GQRX v2.17.6 source. Upstream attribution and exact source revision: `tests/GQRX-CAPTURE.md`. Full version 3 text: `LICENSES/GPL-3.0.txt`. |
| `waveform/reference/` reference grid and derived `waveform/nr-tm3.1a-fdd-4x20mhz-120msps.*` fixtures and `waveform/legacy/nr-tm3.1a-fdd-100mhz-120msps.*` | MIT provenance from hahaliu2001/python_5gtoolbox; copyright 2023 hahaliu2001 is retained in `waveform/reference/LICENSE.txt`. Metadata records source and SHA-256. The default project license does not replace these notices. |

The GQRX test executable and GNU Radio/GQRX application installers are not included in the product release. The patch is provided as source with its upstream revision recorded.

## Dependencies and binary distributions

* **SoapySDR** uses the Boost Software License 1.0. Its API/runtime is a separate radioconda dependency of the GPL-licensed native VST plugin. Text: `LICENSES/BSL-1.0.txt`; [upstream](https://github.com/pothosware/SoapySDR).
* **.NET / Windows Desktop runtime 10.0.12** is bundled in the self-contained Windows executable. Preserve its MIT notices and third-party terms: `LICENSES/DOTNET-RUNTIME-LICENSE.txt`, `LICENSES/DOTNET-RUNTIME-THIRD-PARTY-NOTICES.txt`, and `LICENSES/DOTNET-WINDOWSDESKTOP-LICENSE.txt`. These are copied from the exact runtime packages used by the build.
* **GNU Radio and GQRX**, with their own dependencies, retain their upstream GPL terms. They are separately installed applications. Existing GPL file headers and upstream attribution are preserved; the project default does not replace applicable third-party terms.
* **NI drivers, NI import libraries, and NI FPGA bitfiles** retain their vendor terms. They must be obtained separately; proprietary NI binaries are not redistributed in this public repository or release packages. The exact used bitfile is archived locally in `hardware/ni-pxie-5644r/local/` and excluded from Git; provenance/hash and the original NI Sample Code terms are retained in `hardware/ni-pxie-5644r/`. The limited linking permission in LICENSE-EXCEPTIONS.md grants no vendor redistribution rights.

Licensing supplements for existing release archives supply these notices without replacing the tested binaries or moving their tags. Future application and plugin packages include LICENSE, NOTICE, LICENSE-EXCEPTIONS.md, this file and LICENSES/.
