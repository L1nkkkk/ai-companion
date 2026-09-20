# U01 桌面基础功能：指定进程音频回环证据

2026-09-21，候选源码 `e3fd507aacbce6a3e87311e9a33ef4fb93d032dc`。本报告只评价程序触发停止到 Windows 指定进程音频回环的尾音子检查，不单独宣告 UA03、UA05 或整个 U-G0 通过。

该候选随后因 Avatar motion 图生命周期问题需要重新打包，本次证据保留为 **e3fd507 前候选** 的实际结果；新候选必须重新采样，不能沿用本次结果宣称相同 SHA 验收完成。Audio、Session、Transport 本次未因该修复改变。

## 实测结果

同一 Windows Player 的 20 次播放中停止全部获得完整采样窗口。以明确声明的 `1e-9 FS`（约 −180 dBFS）阈值区分微小底噪与测试信号，停止到最后测试信号样本的 P50 为 **137.315 ms**，P95 为 **153.252 ms**，最大 **154.795 ms**；20 个有效样本满足本子检查的数量和 P95≤200 ms 条件。

不能把该结果写成“输出绝对零”：原始 float32 回环在无测试音的区间仍存在峰值约 `5.99999994e-10 FS` 的持续微小信号，来源尚未归因。保留的严格零阈值结果为 **0/20 有效、P95 不可计算**，原因是每个窗口都缺少完全为零的 50 ms 尾段。声明阈值是在观察底噪后选择；阈值从 `1e-9` 至 `1/32768 FS` 变化时，20 个逐项尾音结果完全一致，未用提高阈值缩短尾音。

| 测量项 | 结果 |
| --- | --- |
| 采集对象 | 指定 `NeuroSaki.exe` 的 PID 8464 及其子进程树 |
| 请求时长 / 实际音频 | 90 s / 89.98 s，4,319,040 帧 |
| 原始格式 | IEEE float32，48 kHz，立体声 |
| 数据包 | 8,998 个 |
| 断流 / 时间戳错误 | 0 / 0 |
| 有效播放中停止样本 | 20；生成中和下载中取消未混入统计 |
| 阈值 `1e-9 FS` 的最小 / P50 / P95 / 最大 | 122.829 / 137.315 / 153.252 / 154.795 ms |
| 窗口 | 每次停止前 250 ms、后 500 ms；最后至少 50 ms 低于阈值 |
| 最短相邻停止间隔 | 1,616.761 ms；本组窗口未包含下一轮的可听测试音 |

## 对象、工具与采集边界

被测程序为 `C:/Users/Link/Dev/ai-companion-dev-tools/builds/NeuroSaki-Desktop-e3fd507/NeuroSaki.exe`，EXE SHA-256 为 `fa01ccdbaa5f74c777609235b99ba8988285b2bf0754445e85bba268b2e61eb7`。完整候选包身份由同目录包清单和集成构建报告承接。运行环境为 Windows 11 build 26200，Unity 2022.3.62f3c1 Windows Player。

集成 owner 为该确切 PID、路径和 90 秒窗口启动唯一一次本候选采集；分析任务没有另外启动采集。只使用自有合成 fixture，未采集并行比较 Player PID 30800、其他应用、全系统回环或麦克风。

独立工具 `tools/unity/qa/ProcessLoopback.cpp` 使用 Windows 官方 `VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK` 与 `PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE`。运行前核对目标进程的真实镜像路径并持有进程句柄；失败不会回退为全系统回环。工具用现有 Visual Studio 2022 x64 工具链及固定 Windows SDK `10.0.26100.0` 编译，无新增产品依赖。实际工具为 `process-loopback-tool-03/ProcessLoopback.exe`，SHA-256 `7f5af0a62bff4f80734e767341ccaa94ca13778ccfa57650af241d50828b77e0`；构建时无录音自检通过。分析器的 5 项测试验证样本分辨率、窗口缺失、时间戳错误、错误时钟原点和错误采集范围拒绝。

## 时钟与底噪方法

Player 在调用 `Session.Cancel` 前记录 Win32 `QueryPerformanceCounter`，另记 `QueryPerformanceFrequency=10,000,000`。WASAPI 给出包起点 QPC 的 100 ns 单位时间戳，分析器按实际 48 kHz 样本索引换算最后超过阈值的帧，计算与停止命令之差。没有使用 Unity Mono 的 `Stopwatch` 原点猜测偏移，也没有将 Stop 返回时间或 Unity 输出回调时间冒充 OS 输出结束时间。

独立底噪区间选为捕获开始后 0–0.25 s（25 包、测试信号前）和 65–85 s（2,000 包、本轮脚本已停止音频，只剩角色招呼）。两个区间的最大包峰值均为 `5.99999994e-10 FS`，最大包 RMS 分别为 `2.50416322e-10` 和 `3.16359500e-10 FS`。这与测试信号全程峰值 `0.124450684 FS` 有明显幅值间隔。零阈值和全部阈值敏感性结果均保存，没有覆盖失败结果。

工具保留每包原始 flags、QPC 和实际样本。分析时，窗口覆盖不足、数据断流、时间戳错误、超过 2 ms 的包时间缺口、没有目标信号或不足 50 ms 静音尾段都记为 invalid，不能填成零延迟。本组 20 个阈值窗口均有效。

该回环记录覆盖 Windows 音频引擎可见的进程输出及其缓冲影响，不能证明物理扬声器/耳机、DAC 或无线设备之后的声学尾音；触发来自测试程序，也不是人手按键到声音消失的测量。它可补充 UA03 的实际输出音频与 UA05 播放中停止证据；口型同步、生成/下载中各 10 次、旧结果隔离、切换会话和关窗等条目仍由同候选的其他证据承接。

## 可复核文件

证据目录：`C:/Users/Link/Dev/ai-companion-dev-tools/evidence/desktop-final-600/`，原始音频等大文件保留在本机证据目录，不加入源代码仓库。

- [指定进程原始音频](C:/Users/Link/Dev/ai-companion-dev-tools/evidence/desktop-final-600/audio.wav)
- [原始逐包采样](C:/Users/Link/Dev/ai-companion-dev-tools/evidence/desktop-final-600/audio.wav.packets.csv) 与 [采集范围摘要](C:/Users/Link/Dev/ai-companion-dev-tools/evidence/desktop-final-600/audio.wav.json)
- [原始停止命令与 native QPC](C:/Users/Link/Dev/ai-companion-dev-tools/evidence/desktop-final-600/stop-measurements.csv)
- [严格零阈值结果：不通过](C:/Users/Link/Dev/ai-companion-dev-tools/evidence/desktop-final-600/stop-analysis.json)
- [声明阈值结果：20 项逐样本时间](C:/Users/Link/Dev/ai-companion-dev-tools/evidence/desktop-final-600/stop-analysis-threshold-1e-9.json)
- [底噪区间、阈值敏感性及全部证据 SHA-256](C:/Users/Link/Dev/ai-companion-dev-tools/evidence/desktop-final-600/stop-analysis-sensitivity.json)

原始 WAV SHA-256：`9a7396d07b94c7e5067d96558d4fcd752305dc818447eaedab148c29a449421b`。原始停止 CSV SHA-256：`a636fffe89ba6b5cab9664e315bc2829034fba9a0e033160bd4e8e782afc986b`。

只读复核命令（输出必须是一个尚不存在的新文件）：

```powershell
./.venv/Scripts/python.exe tools/unity/qa/analyze_process_loopback.py --wav C:/Users/Link/Dev/ai-companion-dev-tools/evidence/desktop-final-600/audio.wav --stops C:/Users/Link/Dev/ai-companion-dev-tools/evidence/desktop-final-600/stop-measurements.csv --output C:/absolute/new/analysis.json --amplitude-threshold 1e-9
```

先前 `desktop-loopback-01` 是探索性工具验证：PCM16 转换有 ±1 LSB 微小信号，且当轮 Player 缺少 native QPC 锚点，因此没有从那轮推算或宣称延迟通过。本报告的时延仅来自上述 e3fd507 候选的新采样。
