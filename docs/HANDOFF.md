# VST Bridge 2.1 handoff

Start with the root README and docs/VALIDATION.md. VSTHub.exe, driver=vst, the control API and shared-memory identifiers remain compatible.

## Completed

Bridge UI/launchers, native TDMS playback, four-carrier NR stimulus, diagnostic export, stopped-RX configuration correction, independent direction control, full native/C# build and regression tests. Both 120 MS/s GUI runs completed 20 minutes; golden examples and real application images are documented. The GQRX spectrum review and original failed average-power check are preserved transparently.

The tested release binary embeds the matching plugin. Do not rebuild after testing unless the new binary is separately validated. RF was turned off at the end of acceptance. RX remains available in the open Bridge.

## Publication

Release assets are built by scripts/Package-Release.ps1, which refuses missing/failed acceptance. Planned repository: DynamixYANG/vst-sdr-bridge; tag v2.1.0. GitHub CLI authentication is the only remaining external prerequisite for upload. See release notes for functionality and asset contents.

## History

Original handoffs remain in archive/documentation-2.0; old outputs in archive/legacy-workspace/outputs; old evidence in archive/validation-before-2.1. Superseded scripts and legacy standalone bridge code are archived locally. No historical evidence was deleted. SDKs, scratch sources and archives are excluded from publication.
