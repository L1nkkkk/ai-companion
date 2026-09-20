# 桌面基础功能交接

本轮执行用户指定的 `Neuro-Saki-U01-plan/docs/development/DESKTOP-PRIORITIES.md` 中“当前先做：桌面基础功能”。实现及集成在 `agent/U01-desktop-basics` 分支，共同起点为 `66b0e541a91aa727b33b3997b39bce178bbaee6a`。规划 worktree、原本未提交的 local-environment 报告、正式 R1 contracts、冻结 C# Contracts 和依赖锁保持原样。

## 可体验功能

完整 Windows Player 接入本机 Python/FastAPI 后台：输入中文 → 显示标明“演示”的固定回复 → 播放随包测试 WAV → Mao 按实际输出音量开合口型。支持随时停止、音量/静音、文字模式、新会话、本机历史恢复、原生文件导出、单会话删除与全部清除。Mao 有眨眼、呼吸、鼠标视线和点击招呼。

后台仅绑定回环地址；启动器生成每次独立的本机凭据并限制文件访问。正常关闭先停止本机音频，异步排空历史终态写入，再释放资源和本次后台。历史、配置与测试证据分别存储，测试使用隔离目录。没有麦克风采集、云端请求或隐式长期记忆。

最终产品源码为 `b94af81d73101d684b856bd0942de778ea74074a`。可直接使用 [Windows 便携 ZIP](C:/Users/Link/Dev/ai-companion-dev-tools/builds/NeuroSaki-Desktop-b94af81.zip)，或本机已解压目录中的 [Start-NeuroSaki.cmd](C:/Users/Link/Dev/ai-companion-dev-tools/builds/NeuroSaki-Desktop-b94af81/Start-NeuroSaki.cmd)。ZIP 为 70,313,849 字节，SHA-256：`64b016202945914afd59d70a3ea3ef07dbf9a8a82ef435eee3597a8654486b84`。

精确源码及逐文件哈希见 [构建清单](build-manifest.json)。完整解压后双击 `Start-NeuroSaki.cmd`，关闭窗口结束；随包包含冻结 Python 运行时，无需用户另装 Python/PowerShell/Unity。不要只移动 `.exe`。源码构建及故障重现方法见 [Unity README](../../../../apps/unity/README.md)。之后的交接文档提交不改变上述被测产品源码或包。

## 验证与证据

结构化结果见 [verification.json](verification.json)，环境见 [environment.json](environment.json)。模块说明及独立检查：

- [UI / 本机历史](ui-history.md)：实际中文 IME、换行、原生导出、恢复、删除；坏 JSON、容量、旧写入与原子替换检查。
- [会话 / 传输 / 音频](session-audio.md)：NDJSON 身份/顺序、WAV 校验、PCM 所有权、取消、正确历史状态及关窗写队列。
- [预览后台](backend.md)：schema / 固定样例、鉴权、取消与缓存、故障注入、真实 TCP 测试。
- [角色呈现及 F02/F03](avatar.md)：真实 SDK 模型、能力降级、口型数据来源、特效范围与取景。
- [前期实际冒烟](qa-smoke.md)、[故障及阶段取消](qa-faults.md)：保留候选版本、方法和限制。
- [进程音频回环](qa-loopback.md)：最终包 20 次有声停止，声明底噪阈值下 P95 155.845 ms；严格零结果、全部阈值和原始采样一并保留。
- [十分钟内存及帧时间](qa-memory.md)：三种内存指标、动作图节点、前候选定位和最终独立运行。
- [便携运行时](portable-package.md)、[独立包核查](qa-package.md)：冻结依赖闭包、凭据权限、进程生命周期、许可证与全部文件校验。

全仓库检查通过：Python API 75 项、固定依赖/契约和设计一致性、Python lint/format、历史 TypeScript 工程类型检查与构建。固定 Unity 的模块检查最新为 History 32、UI 17、Session/Audio 41，共 90 项。Python QA 回环分析器另有 5 项测试。旧 React/RN 构建仅为回归检查，不作为本次桌面产品。

最终便携包单独运行 **600.013 秒**后正常退出，[实际 Player 摘要](player-result-600.json) 的错误、警告和脚本失败均为 0。共记录 35,813 帧，P95 为 16.9 ms，最大 32.3072 ms，100% 已记录帧不超过 33.3 ms；记录的是初始化后的 Unity 帧间隔，不是启动耗时、GPU 时间或物理输入延迟。148 次眨眼、38 次招呼，动画图最多 6 个节点。真实播放启动 22 次：完整播完 1 次、专门的有声停止 20 次、另 1 次音量/静音检查；另有 10 次接受前取消和 1 次纯文字完成。生成中已接受 / 下载中各 10 次属于 qa-faults.md 明示的前候选独立批次，没有混进本轮 20 次停止延迟统计。

最终包的 [播放中关窗专项](player-result-close.json) 通过：在首次实际非零输出后调用 `Application.Quit`，执行与普通窗口关闭相同的退出钩子；本机先停止，最终历史落为 Interrupted，保存了 12,288 / 192,000 已输出样本，进程退出码 0，日志错误和警告均为 0。它是程序触发的真实 Player 测试，不是人手 Alt+F4 延迟测量。

本轮实际便携包 QA 所用的 [运行入口](run-portable-qa.py) 留作精确复现。它使用包内启动器的正常进程管理、包内 Python 和后台，仅为 Player 注入测试选项与隔离数据目录。以下两个路径须替换为包目录及一个新的证据目录；不要覆盖已有测量。关窗专项在末尾另加 `close`，十分钟正常模式不加。

```powershell
& 'C:/Users/Link/Dev/ai-companion-dev-tools/builds/NeuroSaki-Desktop-b94af81/python/python.exe' docs/reports/U01/desktop-basics/run-portable-qa.py 'C:/Users/Link/Dev/ai-companion-dev-tools/builds/NeuroSaki-Desktop-b94af81' 'C:/absolute/new/evidence'
```

指定进程音频采集需另按 qa-loopback.md 的范围和时钟方法执行；该运行入口本身不会录音。

## 真实范围与后续验收

本轮实现的是文档明确要求的演示桌面基础体验。测试声音为有来源的合成测试信号，并非 TTS 生成；回复为固定演示，并非真实云模型。真实 LLM/TTS、ASR/录音、声音模型选型与长期记忆仍按原计划后置。

完整 U-G0/U-G1/U-G2 不由本开发报告宣布通过。UA04 的 OS 125%/150% DPI 矩阵、含同步系统声音的完整演示视频、第二台机器复现及 A0 最终审阅需继续完成；它们不被截图或同机测试替代。U01-F04 保留给第二机器 owner。测试仅覆盖当前 Windows/Mao 和公布的动作集，没有推广手机、直播或任意模型兼容性。

失败、修复与限制见 [defects.md](defects.md)。原始构建/运行日志和合成测试证据留在本机测试附件目录，不将带本机网络环境的完整日志或任何运行凭据提交源码。

运行期错误/警告计数不包含退出后的原生即时内存标记（65,812 bytes）；该标记已原样保留，尚未归因，不宣传“零泄漏”。最终包退出后没有遗留本次进程、8000 端口监听或鉴权配置，包内全部文件与运行前哈希一致。
