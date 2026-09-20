# U01 当前开发派工入口

U01-00 已按 [A0 第三轮验收](../../reports/U01/acceptance/PR-003-round3-review.md)通过基础工程范围，版本与共享接口已冻结。以下四项可领取，尚未因本文自动启动。A0 会话继续只设计、审阅和验收；用户在独立开发会话执行实现。

## 共同起点

- 代码/报告起点：`cfffbf6b307e58421d0316cda67d027a5f39be7d`，位于 `agent/U01-00-unity-foundation`；实际构建源码是 `849c6c982d875f477d88933af4abea5a163d0fa6`，两者产品文件相同。
- 从该提交建立各自分支/worktree，再合入 `design/unity-desktop-taskbook` 中包含第三轮验收与 CSHARP-BASELINE 的精确提交，记录两个 SHA。PR 尚未合并不妨碍按此起点开发；不要从旧 main 或 P01 起步。
- 先读 AGENTS.md、ADR15/16、[任务书](TASKBOOK.md)、[预览接口](INTERFACES.md)、[C# 冻结边界](CSHARP-BASELINE.md)、[验收表](ACCEPTANCE.md)和本任务允许目录。
- 统一 Unity `2022.3.62f3c1 / 1623fc0bbb97`、R4_1 / Core 5.1.0 / BiRP。U01-00 已完成，不重做版本试验或 R1/R2。共享 Contracts、Packages、ProjectSettings、总场景和 Composition 仅由集成 owner 按统一决定修改。

## 四个可领取工作包

| 任务 | 首阶段交付 | 必须保留的边界与后续项 |
|---|---|---|
| U01-01 角色呈现 | 在 Avatar 自有目录实现真实 AvatarPresenter：自然眨眼/呼吸、鼠标视线、点击招呼、能力降级；接收真实音频振幅 | 处理 U01-F01 内存定位、F02 未覆盖遮罩、F03 动作取景；不在角色模块实现云请求、音频播放或另一套会话状态 |
| U01-02 桌面 UI 与历史 | 中文聊天输入与状态控件、设置/音色/设备入口、历史列表与导出删除；先接 typed 假 Session | UI 只走 ISessionController；历史位于独立 History 程序集。G0 先交可操作控件与 IME/DPI，G1 再完成历史业务；与角色 owner 处理 F03 |
| U01-03 本机后台与云适配 | 先固定预览 schema、正常/故障/迟到/取消样例并供 A0/U01-04 核对，再实现隔离后台、fixture 和 provider 适配 | C# DTO 不定义新的 wire 字段；根正式 R1 契约不改。无密钥先做 fixture/错误路径，真实云调用与费用按已提供配置执行，不静默伪装 |
| U01-04 会话、传输与音频 | Session 状态机、身份隔离、WAV 校验与播放、音量/停止、增益后真实振幅；先对固定 fixture 打通 G0 | 按 CSHARP-BASELINE 注入 Audio 验证器，严格 PCM 所有权；不吞掉迟到结果，不从假参数曲线声称实际音频口型；录音与 ASR 确认流程按 G2 补齐 |

四模块按任务书的目录分开工作；U01-03/04 使用同一预览样例，UI/Avatar 先用 typed 假服务可并行。模块达到 G0 所需范围后，由 U01-05 集成 owner 统一接线打包，QA/A0 验收。U01-F01/F03 必须在 G0 交付前处理；完整 UA02 必须处理 F02 与真实角色交互。第二机器缺口 F04 单独推进，不冒充已经验证。

## 可复制的开发会话提示

```text
你负责 {U01-01 / U01-02 / U01-03 / U01-04}，角色和允许目录以当前 U01 任务书、台账为准。
这是实现会话；A0 会话只负责设计与验收。

同步 ai-companion 仓库，以 cfffbf6b307e58421d0316cda67d027a5f39be7d
建立独立分支/worktree，合入包含 PR-003-round3-review 与 CSHARP-BASELINE 的
设计提交并记录双方精确 SHA。阅读 AGENTS.md、ADR15/16、U01 任务书、
INTERFACES、CSHARP-BASELINE、ACCEPTANCE，以及最新验收的 U01-F01 至 F04。

U01-00 已通过，不重复处理版本试验和已关闭 R1/R2。
保持冻结的 Unity 2022.3.62f3c1 / R4_1 / Core 5.1.0 / Built-in 组合。
只修改本模块允许目录；总场景、共享类型、依赖锁与 Composition 交集成 owner。
发现共享接口缺口向 A0 提交具体提案，不自行分叉协议或复制另一份 Contracts。

优先完成任务中 U-G0 所需部分，并处理本 owner 对应后续项。
后台与网络模块先对齐预览 schema/固定样例；没有云资源时继续 fixture 与故障路径。
不要把 mock、参数扫动或生成完成当成真实云调用、真实声音口型或播放结束。
每个交付记录源码 SHA、范围、相关测试、实际设备/Player 或接口证据、已知缺口。
提交可审阅 PR，交 A0 按对应 UA 验收；不要自行宣布整个 U01/G0/移动路线完成。
```

不需要每个角色都新开任务；一个用户启动的开发会话也可按依赖顺序承担多个模块。上述放行只表示工程准备就绪，不代表本设计会话已经启动它们。

## 返回 A0 验收

```text
请验收 U01-{子任务/门槛}，只评审、核验已有产物并登记返工。
PR：{URL}
实际构建/测试源码：{SHA}；报告 HEAD：{SHA}
设计与共享边界起点：{SHA}
Windows 包 / 完整文件清单 / SHA-256：{位置}
报告、原始结果与限制：{位置}
声明通过的 UA 或明确子项：{编号与范围}
本 owner 后续项 F01-F04 的处理：{证据与剩余缺口}
如有问题，请给开发 owner 写明复现与通过条件，不在设计会话代写产品实现。
```
