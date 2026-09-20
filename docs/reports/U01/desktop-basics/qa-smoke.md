# desktop-smoke-02 独立只读 QA

日期：2026-09-21。复核对象为 `C:/Users/Link/Dev/ai-companion-dev-tools/evidence/desktop-smoke-02` 的现有文件及对应源码。本次没有启动 Player、操作 UI 或修改其他 owner 的代码。

结论：本轮 **100.016 秒真实 Windows Player 冒烟过程完成**，日志/结果显示 0 errors、0 warnings、无脚本失败；事件、导出历史及音频振幅互相吻合。它证明若干桌面 fixture 路径已实际执行，**不能判完整 U-G0、UA03/UA05、10 分钟角色稳定性或 U-G1/U-G2 通过**。

## 环境与失败记录

本轮 `player.log` 实际加载 `desktop-dev-03/NeuroSaki.exe` 对应资源，Unity `2022.3.62f3c1`、D3D11、NVIDIA GeForce RTX 5060 Ti，输出 48000 Hz / Stereo，DSP 配置 1024 帧 × 4 缓冲。未将代码当前 HEAD 冒称该 Player 的干净构建提交；精确构建/源码清单由集成报告绑定。

保留第一次失败：`desktop-smoke-01/player.log` 加载 `desktop-dev-02`，有 `DESKTOP_STARTUP_FAILED` 与 `TMPro.MaterialReference..ctor` 的 `NullReferenceException`，没有完成结果/CSV。第一次运行失败，不与第二次合并为一次成功。原日志仍位于该目录；后续便携交付必须使用复测构建，不能使用基于 dev-02 的打包工具中间冒烟包。

## 20 次停止重算

按 `events.csv` 中每个 `stop-command` 的 request_id / generation，匹配唯一后续 `playback-ended-Stopped`，共 **20 对**。使用本机 Windows QueryPerformanceFrequency 与 .NET Stopwatch.Frequency 的独立查询值 **10,000,000 ticks/s**，公式为 `(ended_ticks - command_ticks) / 10000` 毫秒。

| 本地时间指标 | 结果 |
|---|---:|
| 样本数 | 20 |
| P50（中位数） | 0.05805 ms |
| P95（nearest rank，第 19 个升序样本） | 0.0628 ms |
| 线性插值 P95，供比较 | 0.069555 ms |
| 最小 / 最大 | 0.0479 / 0.1979 ms |

按发生顺序的毫秒值：`0.0590, 0.0615, 0.0564, 0.0534, 0.0587, 0.0591, 0.0600, 0.0595, 0.0575, 0.0578, 0.0548, 0.1979, 0.0583, 0.0583, 0.0628, 0.0479, 0.0577, 0.0533, 0.0569, 0.0573`。

**这个指标只测主线程发出停止命令到本地 PlaybackEnded 回调，包含同步 Stop / 释放 / 通知路径，绝不是系统扬声器静音延迟。** 它不包含 Windows mixer、驱动或设备缓冲的末尾声音。不能拿该数值宣布 UA05 的实际输出 P95≤200 ms 已验收；本轮无 WASAPI loopback 或带系统输出声的录屏。

测试源码确实先等待当前轮 `LastNonzeroOutputTicks > submittedAt` 才发停止；20 轮停止时均为 12288 / 192000 样本，即音频起点后约 0.512 秒，第一段正弦从 0.5 秒开始。主线程约 30 Hz 的 `output-levels.csv` 仅在 **8/20** 停止轮次保存到非零行；另 12 轮可能在首次非零音频回调后、下一次主线程采样前即停止。当前文件没有逐次保存当时的 LastNonzeroOutputTicks，故保留这一原始证据缺口。CSV 所记录的停止轮次没有在命令后出现旧 ID 的非零振幅。

下一轮需要逐次记录命令 tick、最后非零实际输出块 tick 和频率，并用输出回环捕捉尾音；还需独立完成已接受生成中 10 次、下载中 10 次取消。本轮 10 次“生成取消”是在 SubmitText 之后立即 Cancel，不能据此声称已在后台接受后取消，也不包含下载中取消。

## 历史与生成/播放状态

导出 JSON 为 schemaVersion=1、storeRevision=119，共 **64 条、32 个操作**：

| 导出状态 | 数量 | 样本信息 |
|---|---:|---|
| user / Text / Displayed | 32 | 用户输入 |
| assistant / Audio / Played | 1 | 192000 / 192000，与唯一 Completed 事件相同请求 |
| assistant / Audio / Interrupted | 20 | 12288 / 192000，与 20 次播放停止相同请求 |
| assistant / Audio / Interrupted | 10 | 0 / 0，立即取消的生成轮次 |
| assistant / Text / Displayed | 1 | 不朗读的完整演示回复，未伪造 Played |

未发现导出中生成尚未结束或未播放完整的回复被写成 Played。导出在音量测试之前，故不包含之后的第 33 轮；`events.csv` 总计 22 个 PlaybackStarted = 1 个完整播放 + 20 个中断 + 1 个音量测试，与此时序一致。持久目录只有历史 JSON 与设置 JSON，没有原始录音或 WAV。

代码复核：Session 等网络生成终态和 EOF 后才下载；只有真实 PlaybackEnded.Completed 且 played=total 才写 Played。文字模式完整结束写 Displayed。构建下轮上下文时，assistant 只取 Audio/Played 或 Text/Displayed，未完成音频不当作已说过。

## 振幅、静音与角色证据

`output-levels.csv` 共 **493 行**，143 非零、350 零。源码的 RMS 来自 `UnityAudioPlayer.OnAudioFilterRead` 收到的实际 float 输出块，先逐样本乘当前音量，再计算 RMS；不是下载进度或服务端模拟口型。这是 Unity 回调层输出证据，仍不是声卡/扬声器回环。

对唯一完整 8 秒播放，避开边界后核对已知随包 WAV：0–0.45、2.1–2.9、5.1–5.9、7.6–7.9 秒全部为零；0.55–1.9、3.1–4.9、6.1–7.4 秒全部非零，与素材的三段正弦/静音对应。音量 0.8 时非零 RMS 中位数 0.087833；音量 0.2 时 14 行全部非零、中位数 0.022046，约为前者的 0.251；音量 0 时 9 行全部严格为零。比例与音量变化吻合，未把 PCM 源幅度直接冒充实际输出幅度。

角色统计记录 25 次 blink、5 次 greeting；5 秒间隔的 memory-avatar 采样捕捉一次 mouth=0.351779，其余大多在静音/停播时为 0。静音截图为 0% 且显示正在播放/尚未播完，状态区别正确。稀疏采样的 blink 全为 1、gaze_x/y 全为 0，不能据此证明动画全过程或视线交互可见；本次没有移动指针或连续录像。计数器和源码不能替代对应可视验收。

截图中的 1280×800 中文和必要控件可辨，无明显缺字/遮挡；Mao 在约 520 px 高的角色区仅约 190 px 高，留白明显。F04 取景的主观验收仍留给用户/A0；960×640、125%/150% DPI、中文 IME 与键盘操作本轮未验证。

## 帧时间与内存

原始 frame-times 共 **5814 行**（排除最初 3 秒），重算 P50=16.6681 ms、nearest-rank P95=16.8826 ms、最大 65.6283 ms；2 帧超过 33.3 ms，0 帧超过 1 秒，99.9656%≤33.3 ms。结果 JSON 的 16.9 ms P95 来自 0.1 ms 向上分桶，与原始 CSV 相容。100 秒冒烟不替代 600 秒目标。

memory-avatar 共 20 个 5 秒样本。30.047 秒工作集 277.496 MiB，最后一次 95.081 秒采样 286.414 MiB，增加 **8.918 MiB**；同区间 private bytes 减少 3.746 MiB，Unity 分配量增加 0.173 MiB。启动期工作集峰值 525.719 MiB、private 峰值 705.008 MiB，随后有明显回落。30 秒基线发生在连续交互仍在进行时，不能用这段短趋势宣布长期无泄漏；停止交互后仍有少量工作集变化，需要原定 600 秒复核。

Player 退出日志另有 Unity allocator 摘要和 `MemoryLeaks` 标记（allocatedMemory=63161 bytes）。这不等同于业务持续增长，也不能隐藏；应结合长期进程采样和相同构建重复运行继续定位。当前只结论为：短期轨迹没有失控增长，长期内存门槛未由此轮单独验证。
