# 桌面个人陪伴的实施优先级

日期：2026-09-20。用户锁定顺序：**文字 → TTS + 占位 Live2D → 语音输入 → 显式长期记忆**。这是现有架构内的产品增量顺序，不另建客户端路线、服务协议或验收门槛，也不修改任何验收数值。

当前前台使用 Unity / C# 和官方 Cubism SDK，后台使用 Python / FastAPI。按 [ADR15](../adr/0015-unity-client-and-design-only-a0.md)、[ADR16](../adr/0016-unity-2022-r41-fallback-validation.md)、[U01 任务书](../tasks/U01/TASKBOOK.md) 和 [C# 边界冻结](../tasks/U01/CSHARP-BASELINE.md) 实施。已验收的工程和精确版本继续复用，不重做 U01-00 选型，也不恢复旧 React/RN/WebView 产品实现。

当前共同代码起点为 `66b0e541a91aa727b33b3997b39bce178bbaee6a`，包含实现提交 `cfffbf6b307e58421d0316cda67d027a5f39be7d` 与设计提交 `28d62d0898e1e1ff57113b3613fcddfc87a0e469`。本机 Windows 安装、依赖、后台存活、Unity 构建与 600 秒基础运行已有实测；报告保留于原环境 worktree 的 `C:\Users\Link\Dev\Neuro-Saki\docs\reports\U01\local-environment\README.md`，当前分支不把该本地报告的未提交内容当作共享源码依赖。

## 按既有任务交付增量

| 顺序 | 用户可体验的增量 | 既有任务及边界 | 何时可以判通过 |
|---|---|---|---|
| 1. 文字 | 中文输入、回复、明确运行模式、停止与基本本机历史 | U01-02 负责 UI/History；U01-03 负责 fixture/cloud LLM；U01-04 负责 Session/Transport、请求身份与取消；U01-05 集成 | 无密钥时先做显式 fixture 与错误路径。真实文字回复要有云调用证据；仅文字成功是内部增量，不能单独宣布 U-G1 完成 |
| 2. TTS + 占位 Live2D | 回复朗读、音量、停止、实际输出驱动口型，配开发期角色 | U01-03 TTS；U01-04 Audio；U01-01 Avatar；U01-02 控件；U01-05 接线。优先复用已验收官方 Mao 作为开发期角色，不等待最终美术 | 随包测试音频对应 U-G0 的相关子项，须标明演示。真实 LLM + 朗读对应 U-G1；系统 TTS 可先用，模式必须明确。真实播放、取消和角色验收均按原表执行 |
| 3. 语音输入 | 用户按住录音、松开转写、检查并编辑转写后确认发送 | U01-04 负责真实采集与生命周期；U01-03 负责 ASR；U01-02 负责草稿/状态；U01-06 做真机桌面音频 QA | 对应 U-G2，需真实麦克风样本、设备/权限/失焦/关闭窗口测试和完整闭环；模拟转写不能计入真实 ASR 验收 |
| 4. 显式长期记忆 | 用户明确要求或确认后记住事实，能查看、修改、删除并追溯来源 | 正式 R1 的 T08，前置 T06 的 owner、会话和范围边界；后续客户端集成按正式迁移任务接入 | 按 AC17/AC18 检查个人/直播隔离、来源与删除。不能把 U01 的本机 History 或传给 LLM 的短期历史当作长期记忆已完成 |

“占位 Live2D”指开发期可替换的真实演示角色。现有 Mao 可复用，最终人设美术可以后移；静态图片、纯占位组件或程序性假口型只能用于有明确标记的开发测试，不能代替 UA02/UA03 或 R1 AC09。角色振幅来自实际输出并受音量、静音、停止影响，不能用 TTS 下载进度或固定动画冒充。

U01-06 按对应候选包独立测试，U01-07/A0 按证据验收。文字优先不改变 U-G0/U-G1/U-G2 的原条件：阶段相关子项可以先交付，完整 gate 仍需全部原条目通过；[U01 验收表](../tasks/U01/ACCEPTANCE.md) 与 [R1 验收表](../blueprint/ACCEPTANCE.md) 中的次数、时长、性能指标均保持原样。

## 两套协议保持分开

- T04 面向冻结 R1 `contracts/` 1.0.0：`/v1`、控制与音频 WSS、AIC1 二进制 PCM、session/epoch/turn 和设备权限。离线 mock 及固定向量服务于这个协议，不能因桌面增量顺序而改写它。
- U01 面向 [unity-preview/1](../tasks/U01/INTERFACES.md)：本机 `/preview/unity/v1`、NDJSON 与整段 WAV、本地 request/generation。预览 schema/样例归 U01-03，位于预览模块的测试/协议目录，不能加入根 `contracts/` 或把 generation 宣称为正式 session_epoch。
- UI 仅通过 Session；Session 通过 Transport 使用后台；Avatar 不直接调用 AI；Playback 唯一拥有实际音频输出。已有 [Transport → Audio 验证委托](../tasks/U01/CSHARP-BASELINE.md) 继续由 Composition 注入，不私增共享协议。
- T05、T06、T07、T08 的正式后台目标保留。U01 的局部功能或 fixture 不自动完成这些任务；未来切换到正式认证 REST/WSS、有界 PCM、正式历史/记忆时，按已有 U01 迁移说明逐项审阅和联调。

## 历史、短期上下文与长期记忆

U01 的本机历史负责版本化记录、容量、导出和删除，并区分 generated、displayed、played、interrupted。未实际播完的回复不能以“已经说过”进入下一轮；关闭朗读时，仅完整显示且正常完成的文字按 displayed 处理。

U01 明确不包含长期记忆。T08 沿用 [总架构的数据与记忆规则](../blueprint/ARCHITECTURE.md)：只有用户明确要求记住或确认的事实可进入长期记忆；自动提取先作为候选。记忆带 owner、character、scope、来源、时间和确认状态；检索时在后台统一过滤个人/直播范围。删除会话须处理派生摘要与引用来源，删除内容不能继续被检索。需要把这部分接入桌面时，先完成 T06/T08 对应交付与正式接口集成安排，不把它偷偷扩入 U01 预览接口。

## 并行工作与状态边界

[T00 离线并行记录](../reports/T00/parallel-readiness.md) 允许冻结基线上的 T04 与外部可行性准备并行；[T01/T02/T03 准备记录](../reports/T00/feasibility-readiness.md) 继续登记 Mac、手机及平台条件。真实资源缺失不阻塞离线测试、U01 fixture、UI/Avatar 独立开发和故障处理；真实云阶段仍受账号、预算和服务配置约束。

本优先级不把 T00、完整 G0/R1、手机后台或真实直播标为完成，不扩张移动/直播产品范围。T09/T10 等旧客户端卡已由 ADR15 禁止原样派发；当前桌面增量使用现有 U01 owner 和目录边界，根锁、总场景、共享配置由已登记的集成 owner 单写。
