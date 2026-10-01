# VST Bridge 2.2 handoff

Read root README.zh-CN.md / README.md and docs/VALIDATION.md. Current app: dist/VSTHub/VSTHub.exe (2.2.0), with matching native plugin. TX/RX Start/Stop are fixed in the footer on every page; TX/RX suggested numeric dropdowns use dark backgrounds and white text. Separate header states, launch-only Bridge parameters, corrected monitor spacing, editable TX rate/queue/FIFO/prefill, hover help, idle No client data and initialization logs are complete.

## Validation

Normal generated GRC main() completed 1200.485 s at 120 MS/s with no stream errors. Operator explicitly accepted TDMS/GQRX's approximately 17-minute continuous interval and cancelled its repeat. Accepted near-end real GQRX spectrum shows four wideband carriers; original interrupted FAIL and strict initial -30 MHz coverage failure remain preserved. Do not describe TDMS as a 20-minute automatic PASS.

Final UI-only rebuild keeps streaming core/native plugin unchanged and passes 36 self-tests, 12 real page renders at default/minimum size and a fresh short real-hardware independent-direction regression. Long-test and final-UI binary hashes are separate. No repeated long-duration test on the final UI executable is claimed. RF is disabled at delivery.

## TIMEOUT correction

Old wrapper-based 2.1 testing did not establish ordinary GRC startup repeatability. Evidence and failed development attempts remain. 2.2 uses bounded initial prefill, larger live reserve, high-resolution worker waits, exact SSE2 CF32 conversion and stopped-ring detection. No client data disables RF and becomes idle; true active-producer faults remain errors. Windows GNU Radio thread-priority calls are unimplemented and not used.

## Publication

Planned repository: DynamixYANG/vst-sdr-bridge, private; local release tag v2.2.0. Package/Publish scripts check the distinct automatic-PASS, user-accepted and final-UI records plus hashes. GitHub CLI is not authenticated; no remote repository or release is claimed. App/plugin/source packages and SHA256SUMS are prepared under docs/releases/2.2 and chat outputs. Run scripts/Publish-GitHub.ps1 after human GitHub CLI login.

## Organization

Maintained folders contain Markdown indexes. Intermediate flowgraphs/wrapper/timer scripts are archived locally; dependency caches and scratch helpers are excluded from source publication. Superseded results remain documented in docs/VALIDATION-2.1.md. Failed/interrupted evidence is retained, not rewritten.
