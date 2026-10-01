# NI PXIe-5644R Streaming for VST

* Local archive: `local/NI Streaming for VST.lvbitx`.
* Exact identity: [manifest.json](manifest.json), including SHA-256, byte count and FPGA signature.
* Origin: NI Streaming Host System package 2.1.0.1; the installed copy and extracted package copy were verified to be identical.
* Vendor terms: [NI-SAMPLE-CODE-LICENSE.txt](NI-SAMPLE-CODE-LICENSE.txt), retained from that exact package, with only Windows-1252 to UTF-8 encoding conversion.

This binary is NI material, not project-owned GPL code. The original package's section 3 limits binary distribution to accompanying Applications under agreements protecting NI's rights. This public repository therefore provides provenance, notices and an import tool; the vendor binary remains local and is omitted from Git/public release assets. The project GPL grant and NI linking permission do not grant NI redistribution rights.

Import the installed bitfile with `scripts/Import-NiBitfile.ps1`, then run `scripts/Set-ProjectAssets.ps1`. Do not remove the vendor-installed Public Documents copy: the shipped 2.2.1 RFSA/RFSG initialization still names `NI Streaming for VST.lvbitx`; the archived copy is used for the explicit shared NI FPGA open. A different design/hash is rejected by the importer because the adapter has a fixed FPGA signature/register contract.

## 中文

使用中的 bitfile 已在本机 `local` 子目录归档，Hub 的 `bitfile_path` 指向这份副本。原 NI 安装目录中的同名文件仍是当前驱动初始化依赖，应保留。GitHub 上传来源、校验值、原许可和导入脚本，bitfile 本体不公开分发。
