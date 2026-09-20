# U01-04 桌面基础会话、传输与音频交接

范围：本轮 `DESKTOP-PRIORITIES.md` 的当前桌面基础功能。以冻结 `Companion.Contracts`、`unity-preview/1` 与 Composition 注入 WAV 验证器方案实施。没有修改根 `contracts/`、共享 C# 接口、锁文件或项目设置。真实麦克风、ASR 和云模型仍属后续阶段。

## 实现与接线

- `AICompanion.Preview.Session.DesktopSessionController(gateway, audio, microphone, history, settingsPath)` 在 Unity 主线程构造，再调用 `InitializeAsync`。UI 只使用 `ISessionController`。初始化恢复最近会话并读取本机后台能力；连接失败保持可操作离线界面，刷新能力后可恢复。设置保存在独立版本化本机 JSON。音量立即送到播放器，落盘在滑块停止变化 250 ms 后合并执行；关闭时同步保存最后一次待保存设置。
- `AICompanion.Preview.Transport.HttpConversationGateway(baseUri, bearerToken, WavValidator.ValidateAsync, tokenProvider)` 只连接 `http://127.0.0.1:8000`。`tokenProvider` 可选且每次请求重新读取本机运行配置，以支持先开前台再启动后台。禁用代理、Cookie 和自动重定向；没有 POST 自动重试。
- `AICompanion.Preview.Audio.UnityAudioPlayer` 为唯一实际输出组件，使用一个 `AudioSource`。`Arm` 绑定当前操作；`Play` 只有接受后接管 PCM 所有权；停止、失败或播放完成释放 PCM 与 AudioClip。`OnAudioFilterRead` 对真正交付 Unity 输出的音频块应用音量并计算 RMS，主线程最多 30 Hz 转发最新窗口；停止和音量零同步归零。
- Composition 可订阅播放器的 `PlaybackStarted / PlaybackEnded / PostVolumeLevel`，并使用 Session 的 `SpeechExpression` 在实际开始播放时应用情绪。该本地事件没有新增共享契约或网络字段。
- `UnavailableMicrophoneCapture` 明确报告本版尚未启用录音，不会取得设备、生成假录音或假转写。

## 身份、取消及历史

Session 的操作 ID、generation、conversation 与 history write token 联合约束每次异步回调。开始新轮次、切换/删除会话、停止和关闭窗口使旧操作失效。停止同步先调用播放器静音与清零，再终止消费并请求后台取消；网络取消失败不恢复本机输出。音频下载后再次检查身份，迟到 PCM 由尚持有者释放。

正常关闭窗口时，Composition 先 `Cancel(WindowClosing)`，再异步等待具体实现方法 `FlushHistoryAsync()` 排空已经入队的历史更新，最后释放组件。不能在 Unity 主线程用 `Wait` 或 `Result` 阻塞；关闭期限由 Composition 负责。该方法不改共享接口，写盘失败继续通过快照错误公开，不能当作保存成功。

文字 `generation.completed` 仍需等待合法 EOF 才标记 `displayed`。朗读仅在真实播放器回报 `Completed` 且已输出样本数等于总样本数时标记 `played`。中断为 `interrupted`，失败保留已生成文字及脱敏错误。下一轮上下文仅携带音频 `played` 或文字 `displayed` 的完整助手回复。逐帧进度不写磁盘；历史状态更新串行写入，以防晚到保存覆盖终态；删除前先使旧操作失效并排空终态写入。

## 传输和 WAV 校验

使用 U01-03 的同一组 schema/固定样例。网关验证 UTF-8、严格 JSON 字段集、重复 JSON key、关联 ID、协议版本、uint32 generation/seq、序号连续性、事件顺序、单行 64 KiB、总流 1 MiB 与文本字符数。完全相同的重复序号可忽略，同序号不同内容失败。缺少终态/换行、过早 EOF、错误身份、晚于终态的新事件均失败。单轮网络消费 90 秒、单次读取空闲 30 秒、获取音频 15 秒；取消主动关闭响应流以兼容 Mono 的流读取取消行为。

音频仅能下载当前操作已公布的精确相对路径；8 MiB 内完整下载后调用注入委托。WAV 验证遍历全部 RIFF chunk（包含奇数 chunk padding），拒绝重复 fmt/data、长度不一致、压缩/通道/采样率/位深伪装、截断与声明样本数不一致。输出固定 24 kHz、单声道 PCM16，最长 120 秒。验证后拥有独立样本，不保留借用输入视图。

## 自动检查与实际 Player 边界

Editor 自检入口：`AICompanion.Preview.Tests.SessionAudioChecks.Run`。不依赖 Unity Test Framework，也不修改冻结依赖。输出环境变量 `COMPANION_TEST_OUTPUT` 所指目录下的 `session-audio-checks.json`。检查覆盖共用 NDJSON 正常/无音频/取消/错误/重复/丢序号/错误身份、真实 fixture WAV 的奇数 JUNK chunk 与损坏拒绝、PCM 生命周期、文本 EOF、实际播放完成语义、停止次序、迟到文字/下载、下一轮上下文过滤及删除。

集成 owner 在实际 Unity 环境完成 **41 项检查全部通过**，原始结果为 `C:/Users/Link/Dev/ai-companion-dev-tools/builds/desktop-checks-06-evidence/session-audio-checks.json`，包含音量设置合并保存、关闭保存最终音量、真实队列阻塞期间 flush 等待及终态写入顺序。本模块完成时的候选源码为 `e3fd507aacbce6a3e87311e9a33ef4fb93d032dc`，构建与后续 Avatar 修复候选的完整 Player 结果见本轮集成报告。模块纯逻辑测试不能代替实际 Windows Player、系统音频回环或 UA05 次数/时延验收。

播放器额外提供 `LastOutputTicks`、`LastNonzeroOutputTicks`、`OutputBlockFrames` 供集成采样，时钟为 `Stopwatch.GetTimestamp()`。这些字段记录 Unity 输出回调；操作系统/设备缓冲尾音须由真实 loopback 另行测量，不能将调用 Stop 的时间冒充可听静音时间。

网关具体实现事件 `AudioDownloadStarted` 在合法 GET 响应头验证之后、读取 body 之前触发，供集成 owner 与当前 `TurnKey` 关联真实下载中取消；回调可能位于工作线程，没有改变共享契约。Avatar 修复后的 b94af81 候选 Player native QPC 与实际进程回环结果见 [独立回环报告](qa-loopback.md)：明确底噪阈值下 20 次停止 P95 为 155.845 ms，并保留严格零阈值未达完全静音的结果、测量边界及 e3fd507 前候选证据。

## 已知限制

- 音频为来源明确的随包演示测试信号，不是 TTS 合成结果；固定回复和音频模式必须在 UI 可见。
- 本机 PCM16 WAV 完整下载后播放，没有实现正式 R1 PCM 分段、认证会话、设备租约或移动后台。
- 本版没有真实录音/ASR；历史是本机对话记录，不是长期记忆。
- 本模块测试不宣告 U-G0、U-G1、U-G2 整体验收通过。设备、OS 缓冲、完整取消次数和角色视听证据由同一候选 Player 的集成/QA 报告承接。
