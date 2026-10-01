# VST Bridge 2.2 handoff

Read root README.zh-CN.md / README.md and docs/VALIDATION.md. Current app: dist/VSTHub/VSTHub.exe (2.2.0), with matching native plugin. TX/RX Start/Stop are fixed in the footer on every page; TX/RX suggested numeric dropdowns use dark backgrounds and white text. Separate header states, launch-only Bridge parameters, corrected monitor spacing, editable TX rate/queue/FIFO/prefill, hover help, idle No client data and initialization logs are complete.

## Validation

Normal generated GRC main() completed 1200.485 s at 120 MS/s with no stream errors. Operator explicitly accepted TDMS/GQRX's approximately 17-minute continuous interval and cancelled its repeat. Accepted near-end real GQRX spectrum shows four wideband carriers; original interrupted FAIL and strict initial -30 MHz coverage failure remain preserved. Do not describe TDMS as a 20-minute automatic PASS.

Final UI-only rebuild keeps streaming core/native plugin unchanged and passes 36 self-tests, 12 real page renders at default/minimum size and a fresh short real-hardware independent-direction regression. Long-test and final-UI binary hashes are separate. No repeated long-duration test on the final UI executable is claimed. RF is disabled at delivery.

## TIMEOUT correction

Old wrapper-based 2.1 testing did not establish ordinary GRC startup repeatability. Evidence and failed development attempts remain. 2.2 uses bounded initial prefill, larger live reserve, high-resolution worker waits, exact SSE2 CF32 conversion and stopped-ring detection. No client data disables RF and becomes idle; true active-producer faults remain errors. Windows GNU Radio thread-priority calls are unimplemented and not used.

## Publication

Published on 1 October 2026: [DynamixYANG/vst-sdr-bridge](https://github.com/DynamixYANG/vst-sdr-bridge), private; [v2.2.0 Release](https://github.com/DynamixYANG/vst-sdr-bridge/releases/tag/v2.2.0). The operator completed browser authorization. Remote main and the annotated tag target were verified against release commit 9bc096bd1e4a556ca44d92dc8b22ce3fcea4b69c; all four uploaded asset SHA-256 digests and sizes match local files.

The published source archive records the pre-publication handoff. This documentation-only follow-up records completed publication on main; the release tag, tested executable/plugin and uploaded archives remain immutable. Details: releases/2.2/PUBLISHED.md. Package/Publish scripts distinguish automatic PASS, USER_ACCEPTED and final UI-build review.

## Organization

Maintained folders contain Markdown indexes. Intermediate flowgraphs/wrapper/timer scripts are archived locally; dependency caches and scratch helpers are excluded from source publication. Superseded results remain documented in docs/VALIDATION-2.1.md. Failed/interrupted evidence is retained, not rewritten.
