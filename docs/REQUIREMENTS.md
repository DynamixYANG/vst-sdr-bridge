# Requirements and external-file audit

Project root on this workstation: `C:\Users\yang\Documents\vst-sdr-bridge`. The following is a local installation snapshot taken on 2 October 2026, not a claim that every listed version is a universal minimum or that this layout update repeats the original long-duration acceptance.

## Runtime

| Component | Purpose / observed installation |
|---|---|
| Windows x64 and NI PXIe-5644R (`RIO0`) | Current supported host/platform and hardware adapter. |
| NI-RFSA and NI-RFSG with PXIe-5644R support | Both drivers are used by the shared NI device initialization. `niRFSA_64.dll` / `niRFSG_64.dll`: 26.5.0f471, installed under `%ProgramFiles%/IVI Foundation/IVI/Bin/`. |
| NI-RIO / NI FPGA runtime | `NiFpga.dll`: 26.5.0f162 in Windows System32. Obtain through NI's installed driver stack. |
| Matching NI Streaming for VST example/bitfile | NI Streaming Host System 2.1.0.1, exact bitfile identity in `hardware/ni-pxie-5644r/manifest.json`. Import a local project copy, but keep the installed Public Documents copy for the shipped driver's filename-based initialization. |
| radioconda x64 / SoapySDR 0.8 | Local root `%USERPROFILE%/radioconda`; SoapySDR 0.8.1. The Bridge installs its matching `vstSupport.dll` into `Library/lib/SoapySDR/modules0.8/`. |
| GNU Radio or GQRX | GNU Radio 3.10.12.0 (`Scripts/gnuradio-companion.exe`); GQRX 2.17.6 (`Library/bin/gqrx.exe`). Use whichever client the workflow needs; file TX itself does not need an open SDR client. The current plugin installation helper expects the radioconda GQRX/Soapy library layout. |

The published self-contained EXE bundles the .NET runtime. It does not require LabVIEW, an external Python bridge, an NI NR waveform personality, or the retired Documents test folders. Windows system DLLs and NI/radioconda runtime DLLs remain installed software rather than copied project payloads. Dependencies of those runtimes are installed by their respective installers.

## Source builds / generation / tests

| Component | Requirement / locally observed version |
|---|---|
| .NET SDK | .NET 10 SDK; local `tools/dotnet` is 10.0.401. `build.ps1` falls back to `dotnet` on PATH; only an SDK, not merely a runtime, can compile. |
| MSVC x64 and Windows SDK | Visual Studio Build Tools C++ workload. Current build script finds `vcvars64.bat` under `%LOCALAPPDATA%/Microsoft/VisualStudio/BuildTools/VC/Auxiliary/Build/`. |
| CMake / Ninja | Local radioconda provides CMake 4.4.3 and Ninja 1.13.2. |
| NI development support | `niRFSA.h`, NI-RFSA x64 import library, `visa.h`; discoverable IVI/VISA Include/Lib paths are listed in `soapy-vst/CMakeLists.txt`. No NI SDK/import libraries are copied to the public repository. |
| Python tools | Python 3.12.9, NumPy 2.2.3, SciPy 1.15.2, npTDMS 1.11.0, PyYAML 6.0.2, Matplotlib 3.10.1. Install the pinned `requirements-dev.txt` inside the radioconda environment. These are needed for generation/tests, not native file playback. |
| Git / GitHub CLI | Git for source/history; authenticated `gh` for publication. Local downloaded CLI caches are not runtime requirements. |

## Local assets and setup

* `waveform/`: golden TDMS/CS16, metadata, source grid/license, and the previously selected 100 MHz file under `legacy/`.
* `hardware/ni-pxie-5644r/local/NI Streaming for VST.lvbitx`: local vendor archive, excluded from Git/public release. SHA-256 `e4c77f0a3b9b00c785be60c9be2f598c6a2412835397284feb0346bec05297f4`; FPGA signature `F9744DDE15670B63D27A69F7CB821C89`.
* `%LOCALAPPDATA%/VSTHub`: generated settings, GQRX config, status, logs and backups. These are per-user runtime state, not missing project dependencies. The assets script backs up settings and updates paths with RF disabled.
* No active runtime file path points into the retired `PXIe5644R*` Documents folders after migration. Historical logs/screenshots retain their original paths and conclusions.

After installing the NI example and client software, start/close the Hub once to create settings. With the Hub closed:

```powershell
.\scripts\Import-NiBitfile.ps1
.\scripts\Set-ProjectAssets.ps1
.\scripts\Test-ProjectDependencies.ps1
```

The default asset selection is the recommended four-carrier TDMS. To preserve the prior 100 MHz selection instead:

```powershell
.\scripts\Set-ProjectAssets.ps1 -Waveform 'waveform\legacy\nr-tm3.1a-fdd-100mhz-120msps.cs16'
```

The dependency audit checks both project assets and installed driver/client paths. It is a deployment/file-identity check, not a substitute for RF/stream testing. See [NI asset notes](../hardware/ni-pxie-5644r/README.md), [architecture](ARCHITECTURE.md) and [existing acceptance](VALIDATION.md).

GNU Radio/GQRX executable checks are optional in the audit because a workflow may install/use only one client. Required driver/runtime and project-asset checks determine PASS/FAIL; the individual client availability is still reported.

## 中文

运行需要 Windows x64、NI-RFSA/RFSG、NI-RIO/FPGA 驱动，以及 radioconda 中的 SoapySDR 和按需选择的 GNU Radio/GQRX。EXE 已包含 .NET 运行时；LabVIEW、Python 桥接服务和旧 Documents 测试目录都不是当前程序运行要求。编译另需 .NET 10 SDK、MSVC/Windows SDK、CMake/Ninja 和 NI 开发头文件/导入库；波形生成与测试依赖见 requirements-dev.txt。

当前 TX 波形及显式 FPGA 打开所需 bitfile 副本已归入项目。原 NI 安装目录的同名 bitfile 仍是驱动初始化依赖，不能随旧测试目录一起删除。NI bitfile 的原许可限制分发，本地归档保留二进制，公开仓库只提供来源、校验、原许可和导入工具。
