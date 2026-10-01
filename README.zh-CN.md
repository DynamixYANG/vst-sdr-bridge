# VST Bridge

[下载 v2.2.1 Release](https://github.com/DynamixYANG/vst-sdr-bridge/releases/tag/v2.2.1)。仓库为 private，请使用有访问权限的 GitHub 账号登录。应用、独立 Soapy 插件、tag 源码及 SHA-256 校验文件均已发布。

面向 **GNU Radio / GQRX 的 NI VST 中间件**。在一个 Windows 界面内完成设备配置、应用启动、TX/RX 控制、数据流监控、故障诊断和日志查看。当前硬件适配器支持 NI PXIe-5644R。

界面名称更新为 VST Bridge，程序文件仍为 `VSTHub.exe`，保留 `driver=vst`、控制协议和共享内存端点，兼容原有工程。GQRX 用于接收；GNU Radio 可用于接收、发射及全双工。两者可同时打开，但只能有一个应用消费 RX IQ 数据；GNU Radio 仅发射流程可与 GQRX 接收并行。

**2.2.1 界面更新**：RX/TX 独立状态框位于右上角；TX/RX 的 Start/Stop 按钮固定在主窗口底栏，切换任何页面均可使用；RX/TX 数字设置使用统一深色底、白字的可编辑推荐下拉框（含展开列表和箭头）；TX queue、FIFO、预填充均可配置；选项帮助以鼠标悬浮显示。客户端停止发送 IQ 显示“ No client data ”并关闭 RF，不记为程序错误；硬件故障仍保留错误与计数。初始化阶段、插件检查和应用启动写入 Logs & Debug。

2.2.1 修复打开程序即自动开始 RX 的行为；配置页未聚焦下拉框不再全选蓝色，禁用项也保持深色白字。详见[补丁验证](docs/VALIDATION-2.2.1.md)。

## 功能

| 模块 | 能力 |
|---|---|
| Bridge | 配置 GQRX / GNU Radio 程序路径、启动参数并启动应用 |
| 应用启动 | 选择启动 GNU Radio Companion 或 GQRX，配置程序路径及启动参数 |
| RX | 1–120 MS/s；65 MHz–6 GHz；参考电平 −50 至 +30 dBm；自动前置放大器 |
| TX | 1–120 MS/s；独立中心频率与 −50 至 0 dBm 峰值设置；显式 RF 开关 |
| 独立运行 | RX 单独、TX 单独、TX/RX 同时运行；互不依赖启动/停止 |
| 文件播放 | 直接读取 TDMS 的 I/Q 通道，或带校验 JSON 的 CS16 文件，内存循环播放 |
| 实时发射 | 原生 SoapySDR `writeStream`，支持 CF32/CS16，经共享内存送入 Hub |
| 监控 | 输入、DMA、交付速率；队列/FIFO 占用与余量；心跳、丢样、欠载、溢出与恢复计数 |
| 调试 | 实时事件、UTC JSONL 轮转日志、导出 JSON 诊断快照、本机控制 API |

120 MS/s 指复数 IQ 采样率，CS16 单方向为 480 MB/s（3.84 Gbit/s），CF32 为 960 MB/s。设备模拟 RF 带宽约 80 MHz，不能将 120 MS/s 等同于 120 MHz 平坦射频带宽。

## 使用流程

1. 安装 NI-RFSA、NI-RIO/FPGA 支持及 NI Streaming for VST bitfile，准备 x64 radioconda、GNU Radio、GQRX 和 SoapySDR 0.8。
2. 运行 `dist/VSTHub/VSTHub.exe`。程序持有唯一 NI 会话，初始化设备并核对内嵌插件；RX、TX 默认保持停止。点击底栏 Start RX，或开启已连接 Soapy 客户端的 RX DSP，才开始采样。
3. 在 **RX Configuration** 设置中心频率、采样率、参考电平，然后应用。停止时保存的 RX 配置会在下次 Start 生效。
4. 在 **Bridge** 页面点击 **Launch GQRX** 或 **Launch GNU Radio**。GQRX 设备串为 `soapy=0,driver=vst,resource=RIO0`；GNU Radio 使用 `driver=vst,resource=RIO0`。客户端采样率须与 Hub 一致。
5. 文件发射：在 **TX Configuration** 选择 TDMS/CS16，设置频率和峰值，按需要勾选 RF，然后点击固定底栏 **Start TX**。在 TX Monitor 检查实际速率、队列余量和欠载计数。
6. **Stop RX** 只停止接收；**Stop TX** 停止发射并关闭 RF。关闭 Bridge 会释放两个方向和设备会话。

RX 中心频率/参考电平可在 TX 运行时调整。改变共享采样时钟或重建缓冲区需要先停止 TX。TX 故障会关闭发射并锁定故障状态，须人工明确重启，不会自动重新发射。

## 两个参考应用

### GNU Radio 120 MS/s 双向范例

打开 `examples/grc/vst_bridge_120_duplex.grc`。TX 使用预计算 +1 MHz 周期向量，Soapy Sink 送往 Hub；RX 经 Soapy Source 接到真实 Qt 频谱和瀑布图。范例配置为 TX/RX 中心 2500 MHz、TX 峰值 −10 dBm、RX 参考 −20 dBm，显式开启 RF，适用于本次近距离天线耦合验证。

为适应当前 i7-6700 主机，TX 源采用大缓冲，源/发送块调度粒度为 262,144 样本；不依赖 Windows 上未实现的 GNU Radio 线程优先级接口。生成的 Python 与 GRC 均保留这些设置。需要纯传输测试时，将 Soapy Sink 两处 `rf_enabled=true` 改为 `false`。

### TDMS 四路 NR 载波

直接选择 `waveforms/nr-tm3.1a-fdd-4x20mhz-120msps.tdms`。四路 DL FDD NR-FR1-TM3.1a，256QAM、每路 20 MHz、30 kHz SCS、51 PRB，载波相对中心为 −30/−10/+10/+30 MHz，总标称带宽 80 MHz。10 ms 波形在 120 MS/s 下包含 1,200,000 个复数样本。

本机不具备 NI NR personality 授权，因此使用具有来源记录的开放参考资源网格，经 OFDM 和重采样生成。资源网格往返检查、样本数、峰均比和 SHA-256 记录在同名 JSON 中。该波形用于传输和频谱验证，不等同于 EVM、ACLR 或 PN23 一致性认证。

TDMS 接受同一组中的 I/Q 两通道、Int16 或归一化 Float32/Float64、连续或交织数据及多段文件；必须有准确采样率元数据。详见 [TDMS 格式说明](docs/TDMS.md)。选择后直接在 C# 读取，不需要 Python 转换或外部播放器；文件与解码后数据限制为 512 MiB。

## 实现与目录

```text
RF IN → FPGA RX DMA → C# Hub → RX共享内存 → 原生SoapySDR → GNU Radio / GQRX
GNU Radio → Soapy writeStream → TX共享内存 ┐
TDMS / CS16 → 校验并预加载 ───────────────┴→ 有界队列 → TX DMA → RF OUT
```

`src/Vst.Core` 管理 NI 会话、DMA、队列、控制与日志；`src/Vst.Hub` 为界面；`soapy-vst` 为原生 C++ 插件；`gr-vst` 保留可选 Python 块和明确标注的旧 Fetch 示例。持续 120 MS/s 使用原生 Soapy 路径。

| 目录 | 内容 |
|---|---|
| `examples` | 用户范例及运行说明 |
| `waveforms` | 当前 TDMS/CS16、元数据、参考文件与许可证 |
| `scripts` | 编译、波形生成、范例启动、Release 打包 |
| `tests` | 回归、自检、独立性及持续运行测试；`artifacts` 保存原始证据 |
| `docs` | 架构、API、交接、测试结果与版本说明 |
| `dist` | 编译好的 Windows 应用 |
| `archive` | 历史验证、中间脚本、旧输出和交接，保留本地，不混入当前发行物 |
| `work` / `tools` | 临时工作及本地工具缓存，不上传源码仓库 |

各维护目录均有 README 索引。历史资料不删除；旧路径与旧结论仅作为历史记录。

## 编译、测试和发布

在项目根目录执行 `.\build.ps1 -SelfTest`。需要 .NET 10 SDK、MSVC x64、CMake/Ninja、NI 开发头文件/导入库及 radioconda。发布 EXE 包含 .NET 运行时与匹配的原生插件，运行时无需安装独立 .NET 或 Python 桥接服务。

GRC 验收连续 1,200 秒；TDMS/GQRX 按用户最新要求接受此前约 17 分钟记录：实际 TX/RX 约 120 MS/s，欠载、活动丢样、FIFO 溢出、恢复和日志错误为零，并核查应用频谱图。完整结果见 [VALIDATION.md](docs/VALIDATION.md)；自动 PASS 与用户接受的参考例子分别标注。

源码、应用 ZIP、独立插件 ZIP、波形/示例、SHA-256 与发布说明会分开组织。NI 驱动及 bitfile 不随项目分发。运行时配置和日志默认保存在 `%LOCALAPPDATA%\VSTHub`，控制端口仅绑定 `127.0.0.1:19788`。

## 本次验收结果

| Test | Duration / s | RX delivered / MS/s | TX processed / MS/s | Result |
|---|---:|---:|---:|---|
| GNU Radio normal main() | 1200.485 | 120.001120 | 120.001129 | PASS |
| TDMS + GQRX | 1026.875 | 120.001127 | 120.001130 | USER_ACCEPTED (~17 min) |

GRC 标准 main() 连续 20 分钟通过，作为 golden 范例。TDMS/GQRX 按用户明确指示，以此前约 17 分钟稳定运行及实际四载波频谱/瀑布图作为接受的参考例子。两段有效运行期间实际速率约 120.001 MS/s，欠载、活动丢样、显示跳样、溢出、恢复和日志错误为零。TDMS 原始自动结果仍保留 FAIL：外部启动 GNU Radio 中断 TX；初始频谱的 −30 MHz 载波未达到严格 95% 覆盖判据（65.142%），四路中位增益均超过 10 dB。详见 [VALIDATION.md](docs/VALIDATION.md)。

当前 2.2.1 修改启动流程、原生客户端按需启动及配置框绘制，通过 39 项自检与真实硬件/原生客户端短测，详见[补丁验证](docs/VALIDATION-2.2.1.md)。之前长测仍对应原有 2.2 二进制，不称为新版 20 分钟验收。交付时 RF 已关闭。

![最终 TX 配置与固定底栏](docs/images/bridge-tx-config-2.2.1.png)
![接受的 GQRX 四载波实测](docs/images/golden-gqrx-four-carrier-2.2.png)
