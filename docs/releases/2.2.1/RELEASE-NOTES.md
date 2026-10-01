# VST Bridge 2.2.1

Opening the Bridge now initializes the shared NI session while RX and TX remain stopped. Acquisition starts with Start RX or explicit native Soapy RX activation. Merely creating/configuring a client device does not start RX.

Editable RX/TX dropdowns no longer retain inactive blue text selection. Native edit/static color handling keeps disabled fields dark. Normal focused editing/selection and the permanent footer controls remain available.

The matching plugin accepts staged configuration replies, retains inactive requested settings, and starts RX on client activation. Live TX configures DMA/RFSG before exposing its shared ring, fixing the initialization race observed as a sink TIMEOUT.

Validation: 39 self-checks, seven real cold-start/direction checks, eight native Soapy idle/activation checks, 12 actual widget renders (four configuration images visually inspected), and normal GRC 120 MS/s duplex for 120.094 seconds with no stream errors. Prior development failures are preserved. See docs/VALIDATION-2.2.1.md for binary identities and test scope.

The earlier 2.2.0 GRC 20-minute PASS and operator-accepted TDMS/GQRX ~17-minute evidence remain preserved; they are not relabeled as long-run tests of this patch. No TDMS/GQRX repeat was performed.

Assets: Windows x64 application with embedded matching plugin/examples/TDMS/documents/evidence; standalone SoapySDR 0.8 plugin; tagged source archive; SHA256SUMS.txt. Install the matching patch plugin, or let the application install its embedded module while SDR clients are closed. Earlier release assets remain unchanged.

## Licensing update

Project-owned material is now licensed under GNU GPL v3 or later, with a limited NI driver linking permission. Hobby and commercial use are permitted under the GPL terms; binary distribution must include access to Corresponding Source. Third-party GPL/MIT/Boost and bundled .NET notices remain applicable.

Download `VST-Bridge-2.2.1-licenses.zip` alongside the existing application, plugin or source archive. It supplies LICENSE, NOTICE, LICENSE-EXCEPTIONS.md, third-party license texts and a scope guide. Corresponding Source: `VST-Bridge-2.2.1-source.zip` / the v2.2.1 source tag. Original assets and tags retain their tested identities. Future packages include these notices directly. See [license](https://github.com/DynamixYANG/vst-sdr-bridge/blob/main/LICENSE) and [scope](https://github.com/DynamixYANG/vst-sdr-bridge/blob/main/docs/LICENSING.md).

License supplement SHA-256: `ce54614ad7e540b347c2505e4393a344973294bc27a9dcfd56a5575feaceb8d8`.
