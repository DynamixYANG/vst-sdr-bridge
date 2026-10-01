# Test evidence

Current GRC golden acceptance: grc-normal-2.2-20min. TDMS/GQRX operator acceptance: gqrx-tdms-2.2-accepted17; original automatic FAIL and raw evidence: gqrx-tdms-2.2-interrupted. The operator cancelled the repeat in gqrx-tdms-2.2-repeat-cancelled. Hardware controls/delayed-start regressions: tx-controls-2.2, tx-startup-2.2. Final UI build: ui-footer-final-2.2 (12 real renders) and independence-footer-2.2 (real hardware control regression). release-review-2.2.json relates long-run and final UI-only binary identities; release-ui-binary-hashes-2.2.json is the shipped build.

Historical 2.1 acceptance and failed development/manual TIMEOUT attempts remain preserved, with limitations documented in docs/VALIDATION-2.1.md and each relevant record. They are never substituted for current final-build acceptance. Per-folder README files describe contents; golden status comes from ../../docs/VALIDATION.md.

Patch 2.2.1: startup-2.2.1 contains real cold-start/control evidence; ui-idle-2.2.1 contains 12 WinForms renders and native selection records; release-review-2.2.1.json binds the shipped binary to the patch regressions. Historical long runs remain scoped to 2.2.0.
