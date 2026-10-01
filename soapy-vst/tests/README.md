# Native conversion regression

`tx_convert_test.cpp` compares SSE2 CF32-to-CS16 output and clipping counts against an independent scalar implementation across gains, unaligned input, odd tails, NaN/infinity and random values. It also reports a non-gating throughput benchmark. Compile from an MSVC x64 developer shell:

```powershell
cl /O2 /EHsc /std:c++17 /I soapy-vst/include soapy-vst/tests/tx_convert_test.cpp /Fe:tx_convert_test.exe
./tx_convert_test.exe
```

Hardware streaming acceptance is in ../../tests. A benchmark alone does not establish RF or stream stability.
