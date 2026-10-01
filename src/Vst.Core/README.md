# Vst.Core

Hardware and bridge core. HubEngine and NiDeviceSession coordinate RX/TX; NiRxHardware and NiTxHardware wrap NI APIs; SharedIqRing/SharedTxRing and TxSampleQueue transport IQ; WaveformFile validates TDMS/CS16; ControlServer handles the API; RotatingLog records bounded events. No GUI dependency.
