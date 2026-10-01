# scripts

Maintained build and usage tools. Build-Native.ps1 configures/builds/installs the C++ plugin. generate_nr_four_carrier.py generates the TDMS/CS16 NR reference. Launch-Example.ps1 compiles and launches the editable GRC example. Package-Release.ps1 stages release archives and SHA-256 checksums after fresh 2.2 acceptance. Publish-GitHub.ps1 verifies the authenticated owner, clean source/tag identity, acceptance results and checksums before creating/pushing the repository and release.

Test-ReleaseAcceptance.ps1 is the shared release gate: automatic GRC/regression PASS, explicit USER_ACCEPTED TDMS/GQRX interval and final UI-binary review are checked separately. Original failures remain intact.

Patch 2.2.1 uses its own release folder and binary-bound cold-start/native-client/UI/GRC regression gate. Prior long-run records remain scoped to 2.2.0; prior assets are not overwritten.
