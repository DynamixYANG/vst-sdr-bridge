# WaveformTests

Small executable testing WaveformFile against independently generated npTDMS fixtures. Run make_tdms_fixtures.py, then dotnet run --project tests/WaveformTests -- tests/artifacts/tdms-fixtures. Expected IQ bytes are compared exactly; malformed layouts/rates/non-finite inputs must be rejected.
