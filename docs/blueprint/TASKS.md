# 可执行任务清单

版本 1.0 · 状态以 planning/tasks.json 为准 · 前置任务表示代码或验证结果依赖

每个任务卡都包含可复制的派工提示。机器可读台账在 [planning/tasks.json](planning/tasks.json)。真实资源不足时按协作规则记录 awaiting_external，并继续不受影响的工作。

**ADR15 执行覆盖**：新的客户端使用 Unity。T09、T10、T11、T12、T13、T18、T19 的原客户端目录与实现提示作为历史记录保留，台账标为 `changed` 且禁用原派工，必须重写后再领取；不能执行下面旧的 WebView / React / RN 提示。近期独立开发请使用 [U01 任务书](../tasks/U01/TASKBOOK.md)、[U01 台账](planning/unity-desktop.json) 和 [U01-00 派工提示](../tasks/U01/DISPATCH.md)。其他任务的业务目标和 AC 编号继续保留。A0 本会话仅设计与验收，实际实现/集成由独立开发会话承担。

| 任务 | 负责人 | 阶段 | 前置任务 |
|---|---|---|---|
| T00 建立正式仓库与冻结契约 | A0 | G0 | 无 |
| T01 验证 iOS 后台音频与实时活动 | A5 | G0 | T00 |
| T02 验证 Android 后台音频 | A6 | G0 | T00 |
| T03 验证直播平台权限和真实弹幕 | A7 | G0 | T00 |
| T04 建立契约测试与离线模拟 | A8 | G0 | T00 |
| T05 实现与比较云端 AI 适配器 | A2 | G1 | T04 |
| T06 实现身份设备与会话服务 | A1 | G1 | T04 |
| T07 实现流式对话与取消调度 | A1 | G1 | T05、T06 |
| T08 实现人设记忆与数据隔离 | A1 | G1 | T06 |
| T09 实现共享 Live2D viewer | A3 | G1 | T04 |
| T10 实现电脑聊天控制台与 OBS 页面 | A3 | G1 | T06、T09 |
| T11 实现手机共享界面与原生接口 | A4 | G2 | T04、T09 |
| T12 实现 iOS 生产音频与灵动岛 | A5 | G2 | T01、T06、T11 |
| T13 实现 Android 生产音频与通知 | A6 | G2 | T02、T06、T11 |
| T14 实现首发直播平台适配器 | A7 | G3 | T03、T06 |
| T15 实现直播回应策略与队列 | A1 | G3 | T07、T14 |
| T16 部署观测预算与备份 | A8 | G2 | T05、T06 |
| T17 桌面完整流程集成验收 | A8 | G1 | T07、T08、T09、T10 |
| T18 双端手机后台与跨设备验收 | A8 | G2 | T07、T08、T12、T13、T16 |
| T19 真实直播与 OBS 联调验收 | A8 | G3 | T10、T15、T16 |
| T20 完整第一版验收与交付 | A0 | G3 | T17、T18、T19、T16 |

## 任务依赖图

~~~mermaid
flowchart LR
  T00["T00"]
  T01["T01"]
  T02["T02"]
  T03["T03"]
  T04["T04"]
  T05["T05"]
  T06["T06"]
  T07["T07"]
  T08["T08"]
  T09["T09"]
  T10["T10"]
  T11["T11"]
  T12["T12"]
  T13["T13"]
  T14["T14"]
  T15["T15"]
  T16["T16"]
  T17["T17"]
  T18["T18"]
  T19["T19"]
  T20["T20"]
  T00 --> T01
  T00 --> T02
  T00 --> T03
  T00 --> T04
  T04 --> T05
  T04 --> T06
  T05 --> T07
  T06 --> T07
  T06 --> T08
  T04 --> T09
  T06 --> T10
  T09 --> T10
  T04 --> T11
  T09 --> T11
  T01 --> T12
  T06 --> T12
  T11 --> T12
  T02 --> T13
  T06 --> T13
  T11 --> T13
  T03 --> T14
  T06 --> T14
  T07 --> T15
  T14 --> T15
  T05 --> T16
  T06 --> T16
  T07 --> T17
  T08 --> T17
  T09 --> T17
  T10 --> T17
  T07 --> T18
  T08 --> T18
  T12 --> T18
  T13 --> T18
  T16 --> T18
  T10 --> T19
  T15 --> T19
  T16 --> T19
  T17 --> T20
  T18 --> T20
  T19 --> T20
  T16 --> T20
~~~

## T00 建立正式仓库与冻结契约

负责人：A0。阶段：G0。前置任务：无。

让所有 agent 从同一版本和接口开始。

**允许修改**：docs/；contracts/；tools/；root configuration；initial app scaffolds

**实施内容**

- 创建独立开发目录及项目骨架，按用户后续启动开发时的授权建立或连接私有仓库。
- 导入任务书，contracts 与 tools 放根目录，其余文档与 planning 放 docs/blueprint，修正链接且不保留重复契约。
- 锁定 Node、Python、包管理器、React Native 和移动构建版本；准备根 AGENTS 与忽略规则。
- 登记目标设备、Mac 与签名条件；临时以 iOS17 和 Android10 及以上为基线，最终按设备确定。

**交付物**：正式仓库地址或本地目录及起点提交；版本与资源清单；可运行的契约检查；初始任务台账

**验收**：AC01、AC02。对应需求：R08。

**外部条件**：GitHub 账号由项目负责人提供；Mac、两端真机及签名条件需要确认。

**派工提示**

~~~text
你负责 T00 建立正式仓库与冻结契约，角色 A0。请阅读 docs/blueprint/README.md、ARCHITECTURE.md、CONTRACTS.md、自己的任务卡及 ACCEPTANCE.md。允许修改：docs/；contracts/；tools/；root configuration；initial app scaffolds。前置任务：无。目标：让所有 agent 从同一版本和接口开始。验收：AC01、AC02。外部条件：GitHub 账号由项目负责人提供；Mac、两端真机及签名条件需要确认。在独立分支或worktree实现并测试，共享契约变更交A0协调。提交复现步骤、真实测试证据、限制和交接报告。
~~~

## T01 验证 iOS 后台音频与实时活动

负责人：A5。阶段：G0。前置任务：T00。

确认 iOS 的真实可行路径和系统边界。

**允许修改**：spikes/ios-audio/；docs/reports/T01/

**实施内容**

- 建立最小原生音频实验，前台授权并开始会话，使用固定音频或回声测试服务验证收音播放。
- 真机30分钟覆盖锁屏、切换应用、静音、耳机路由和系统中断；不要依赖 WebView 或 RN 运行。
- 验证灵动岛和锁屏实时活动，确认结束与静音操作真正作用于音频。
- 报告支持条件和失败情形；不采用循环静音、伪造通话或无限重建活动。

**交付物**：可复现实验工程；真机记录及系统版本；后台与灵动岛接口建议

**验收**：AC10、AC12、AC13。对应需求：R05、R06。

**外部条件**：Mac/Xcode、真实 iPhone、可安装签名；没有真机时只能提交待验证状态。

**派工提示**

~~~text
你负责 T01 验证 iOS 后台音频与实时活动，角色 A5。请阅读 docs/blueprint/README.md、ARCHITECTURE.md、CONTRACTS.md、自己的任务卡及 ACCEPTANCE.md。允许修改：spikes/ios-audio/；docs/reports/T01/。前置任务：T00。目标：确认 iOS 的真实可行路径和系统边界。验收：AC10、AC12、AC13。外部条件：Mac/Xcode、真实 iPhone、可安装签名；没有真机时只能提交待验证状态。在独立分支或worktree实现并测试，共享契约变更交A0协调。提交复现步骤、真实测试证据、限制和交接报告。
~~~

## T02 验证 Android 后台音频

负责人：A6。阶段：G0。前置任务：T00。

确认前台服务、系统通知和录音生命周期。

**允许修改**：spikes/android-audio/；docs/reports/T02/

**实施内容**

- 从可见界面授权并启动麦克风前台服务，加入播放与通知控制。
- 真机30分钟覆盖锁屏、切换应用、静音、耳机和权限撤销。
- 验证服务被终止后的真实状态与下一次启动恢复；记录厂商电池管理影响。

**交付物**：原生实验工程；真机报告；生产音频引擎建议

**验收**：AC11、AC12、AC13。对应需求：R04、R06。

**外部条件**：Android SDK、主力真机；额外厂商或系统版本用于后续回归。

**派工提示**

~~~text
你负责 T02 验证 Android 后台音频，角色 A6。请阅读 docs/blueprint/README.md、ARCHITECTURE.md、CONTRACTS.md、自己的任务卡及 ACCEPTANCE.md。允许修改：spikes/android-audio/；docs/reports/T02/。前置任务：T00。目标：确认前台服务、系统通知和录音生命周期。验收：AC11、AC12、AC13。外部条件：Android SDK、主力真机；额外厂商或系统版本用于后续回归。在独立分支或worktree实现并测试，共享契约变更交A0协调。提交复现步骤、真实测试证据、限制和交接报告。
~~~

## T03 验证直播平台权限和真实弹幕

负责人：A7。阶段：G0。前置任务：T00。

先证明有可用权限和真实事件，再确定首发平台。

**允许修改**：spikes/live-access/；docs/reports/T03/

**实施内容**

- 优先验证 B站实际账号的官方接入条件、授权流程、事件范围与心跳。
- 以最小程序接收真实测试直播间消息，记录脱敏样例和断线恢复过程。
- 如 B站不可行，比较用户允许的抖音官方接入类型，提交差异和准确卡点。
- 以 capabilities 描述普通评论、指令评论、用户信息和礼物能力。

**交付物**：权限与接入结论；脱敏真实事件样例；建议的首发平台及替代路径

**验收**：AC15。对应需求：R03。

**外部条件**：开发者账号、直播账号授权及可测试直播间；文档可读性和权限均需实际核实。

**派工提示**

~~~text
你负责 T03 验证直播平台权限和真实弹幕，角色 A7。请阅读 docs/blueprint/README.md、ARCHITECTURE.md、CONTRACTS.md、自己的任务卡及 ACCEPTANCE.md。允许修改：spikes/live-access/；docs/reports/T03/。前置任务：T00。目标：先证明有可用权限和真实事件，再确定首发平台。验收：AC15。外部条件：开发者账号、直播账号授权及可测试直播间；文档可读性和权限均需实际核实。在独立分支或worktree实现并测试，共享契约变更交A0协调。提交复现步骤、真实测试证据、限制和交接报告。
~~~

## T04 建立契约测试与离线模拟

负责人：A8。阶段：G0。前置任务：T00。

让其他 agent 不依赖云账号即可开发。

**允许修改**：tests/contract/；tests/mocks/；tools/；.github/workflows/contract.yml

**实施内容**

- 导入 schema 正反例，建立自动校验和多语言序列化固定测试向量。
- 实现控制、音频和直播 mock，可注入延迟、取消、过期代次、重复事件和断线。
- 提供模拟音频片段、角色占位和独立启动步骤，显式标记 mock。
- 为根共享配置提交最小变更，由 A0 协调合并。

**交付物**：可运行 mock 服务；契约持续集成；固定测试向量与使用文档

**验收**：AC02、AC07。对应需求：R08。

**外部条件**：无需真实账号；使用 mock。

**派工提示**

~~~text
你负责 T04 建立契约测试与离线模拟，角色 A8。请阅读 docs/blueprint/README.md、ARCHITECTURE.md、CONTRACTS.md、自己的任务卡及 ACCEPTANCE.md。允许修改：tests/contract/；tests/mocks/；tools/；.github/workflows/contract.yml。前置任务：T00。目标：让其他 agent 不依赖云账号即可开发。验收：AC02、AC07。外部条件：无需真实账号；使用 mock。在独立分支或worktree实现并测试，共享契约变更交A0协调。提交复现步骤、真实测试证据、限制和交接报告。
~~~

## T05 实现与比较云端 AI 适配器

负责人：A2。阶段：G1。前置任务：T04。

提供可取消、可计量的中文 ASR、对话和 TTS。

**允许修改**：services/api/app/providers/；tests/providers/；docs/reports/T05/

**实施内容**

- 按协议实现三个适配器及 capabilities、错误映射、超时、取消和用量。
- 用同一组至少30条中文样本比较两个实际可用方案，记录地域、音质和首播耗时。
- 将输出重采样为约定 PCM，支持句级合成和背压，默认情绪有稳定降级。
- 服务商配置可切换；没有密钥或预算时明确报错，不能静默请求真实服务。

**交付物**：适配器及模拟测试；真实服务对比报告；配置样例和费用统计方法

**验收**：AC04、AC05、AC21、AC22。对应需求：R01、R07。

**外部条件**：由项目负责人提供可用云服务账户、密钥、允许预算和音色偏好。

**派工提示**

~~~text
你负责 T05 实现与比较云端 AI 适配器，角色 A2。请阅读 docs/blueprint/README.md、ARCHITECTURE.md、CONTRACTS.md、自己的任务卡及 ACCEPTANCE.md。允许修改：services/api/app/providers/；tests/providers/；docs/reports/T05/。前置任务：T04。目标：提供可取消、可计量的中文 ASR、对话和 TTS。验收：AC04、AC05、AC21、AC22。外部条件：由项目负责人提供可用云服务账户、密钥、允许预算和音色偏好。在独立分支或worktree实现并测试，共享契约变更交A0协调。提交复现步骤、真实测试证据、限制和交接报告。
~~~

## T06 实现身份设备与会话服务

负责人：A1。阶段：G1。前置任务：T04。

建立资源授权和会话唯一控制权。

**允许修改**：services/api/app/identity/；services/api/app/session/；services/api/migrations/；tests/session/

**实施内容**

- 实现单拥有者登录、设备注册、原生刷新凭据、网页 cookie 和 OBS 限权显示入口。
- 实现 session CRUD、mode 不变、epoch、单输入单播放租约、显式接管和 WSS 票据。
- 按幂等键入库，提供快照、控制序号、断线恢复及进程重启失效规则。
- 所有接口按 owner 与角色授权，准备数据库迁移与失败测试。

**交付物**：REST 与连接骨架；迁移文件；权限、租约和幂等测试

**验收**：AC03、AC14、AC20。对应需求：R08、R09。

**外部条件**：无需真实账号；使用 mock。

**派工提示**

~~~text
你负责 T06 实现身份设备与会话服务，角色 A1。请阅读 docs/blueprint/README.md、ARCHITECTURE.md、CONTRACTS.md、自己的任务卡及 ACCEPTANCE.md。允许修改：services/api/app/identity/；services/api/app/session/；services/api/migrations/；tests/session/。前置任务：T04。目标：建立资源授权和会话唯一控制权。验收：AC03、AC14、AC20。外部条件：无需真实账号；使用 mock。在独立分支或worktree实现并测试，共享契约变更交A0协调。提交复现步骤、真实测试证据、限制和交接报告。
~~~

## T07 实现流式对话与取消调度

负责人：A1。阶段：G1。前置任务：T05、T06。

把语音、回复、播放和取消串成确定的状态机。

**允许修改**：services/api/app/orchestrator/；services/api/app/audio/；tests/orchestrator/

**实施内容**

- 实现输入语音段边界、最终识别、单活动turn、短句封存、TTS与输出事件。
- 处理控制和音频跨通道到达顺序、尾帧等待、背压和播放回报。
- 取消贯穿供应商任务、发送缓冲和角色事件，拒绝迟到旧数据。
- 按实际完成播放片段保存上下文，区分生成完成、播放完成、取消和失败。

**交付物**：完整调度器；竞争条件与故障测试；一次文字和语音流程演示

**验收**：AC04、AC05、AC06、AC07、AC08。对应需求：R01、R02、R07。

**外部条件**：无需真实账号；使用 mock。

**派工提示**

~~~text
你负责 T07 实现流式对话与取消调度，角色 A1。请阅读 docs/blueprint/README.md、ARCHITECTURE.md、CONTRACTS.md、自己的任务卡及 ACCEPTANCE.md。允许修改：services/api/app/orchestrator/；services/api/app/audio/；tests/orchestrator/。前置任务：T05、T06。目标：把语音、回复、播放和取消串成确定的状态机。验收：AC04、AC05、AC06、AC07、AC08。外部条件：无需真实账号；使用 mock。在独立分支或worktree实现并测试，共享契约变更交A0协调。提交复现步骤、真实测试证据、限制和交接报告。
~~~

## T08 实现人设记忆与数据隔离

负责人：A1。阶段：G1。前置任务：T06。

建立可解释、可修改且按范围隔离的记忆。

**允许修改**：services/api/app/memory/；services/api/app/characters/；services/api/migrations/；tests/memory/

**实施内容**

- 实现近期上下文、会话摘要、显式确认的长期记忆和来源追踪。
- 上下文拼装按 owner、character、personal或broadcast过滤，避免仅在UI隔离。
- 实现查看、修改、删除，处理会话删除后的派生摘要和记忆来源。
- 加入广播提示注入和私人唯一标记泄漏测试。

**交付物**：记忆与人设服务；删除和隔离测试；数据保留配置

**验收**：AC17、AC18。对应需求：R09。

**外部条件**：无需真实账号；使用 mock。

**派工提示**

~~~text
你负责 T08 实现人设记忆与数据隔离，角色 A1。请阅读 docs/blueprint/README.md、ARCHITECTURE.md、CONTRACTS.md、自己的任务卡及 ACCEPTANCE.md。允许修改：services/api/app/memory/；services/api/app/characters/；services/api/migrations/；tests/memory/。前置任务：T06。目标：建立可解释、可修改且按范围隔离的记忆。验收：AC17、AC18。外部条件：无需真实账号；使用 mock。在独立分支或worktree实现并测试，共享契约变更交A0协调。提交复现步骤、真实测试证据、限制和交接报告。
~~~

## T09 实现共享 Live2D viewer

负责人：A3。阶段：G1。前置任务：T04。

提供不依赖 AI 供应商的角色展示组件。

**允许修改**：packages/avatar-viewer/；assets/manifest/；tests/viewer/

**实施内容**

- 加载官方SDK和可用示例模型，建立资源校验与来源清单。
- 实现实际播放振幅口型、眨眼呼吸、有限情绪动作和取消。
- 提供 WebView 窄桥接、前后台暂停恢复及模型缺能力降级。
- 网页及双方 WebView 性能与兼容性记录交给后续真机任务。

**交付物**：viewer组件；桥接接口与示例；模型资源说明

**验收**：AC09、AC26。对应需求：R02。

**外部条件**：合法可用模型及官方SDK；WebView最终兼容性由T18真机完成。

**派工提示**

~~~text
你负责 T09 实现共享 Live2D viewer，角色 A3。请阅读 docs/blueprint/README.md、ARCHITECTURE.md、CONTRACTS.md、自己的任务卡及 ACCEPTANCE.md。允许修改：packages/avatar-viewer/；assets/manifest/；tests/viewer/。前置任务：T04。目标：提供不依赖 AI 供应商的角色展示组件。验收：AC09、AC26。外部条件：合法可用模型及官方SDK；WebView最终兼容性由T18真机完成。在独立分支或worktree实现并测试，共享契约变更交A0协调。提交复现步骤、真实测试证据、限制和交接报告。
~~~

## T10 实现电脑聊天控制台与 OBS 页面

负责人：A3。阶段：G1。前置任务：T06、T09。

提供桌面聊天和直播操作界面。

**允许修改**：apps/web/；packages/client/；tests/web/

**实施内容**

- 实现登录、角色、会话、麦克风、停止、静音与字幕状态。
- 实现网页音频采集播放、按实际播放推进字幕和模型状态。
- 实现透明OBS显示页、限定显示凭据和单播放端规则。
- 建立弹幕队列控制界面；AI和平台尚未联调时使用明确标记的mock。

**交付物**：聊天与控制台；OBS显示页面；网页交互测试

**验收**：AC03、AC09、AC14、AC19。对应需求：R01、R02、R03。

**外部条件**：无需真实账号；使用 mock。

**派工提示**

~~~text
你负责 T10 实现电脑聊天控制台与 OBS 页面，角色 A3。请阅读 docs/blueprint/README.md、ARCHITECTURE.md、CONTRACTS.md、自己的任务卡及 ACCEPTANCE.md。允许修改：apps/web/；packages/client/；tests/web/。前置任务：T06、T09。目标：提供桌面聊天和直播操作界面。验收：AC03、AC09、AC14、AC19。外部条件：无需真实账号；使用 mock。在独立分支或worktree实现并测试，共享契约变更交A0协调。提交复现步骤、真实测试证据、限制和交接报告。
~~~

## T11 实现手机共享界面与原生接口

负责人：A4。阶段：G2。前置任务：T04、T09。

统一双端界面和原生音频的调用方式。

**允许修改**：apps/mobile/src/；packages/mobile-bridge/；tests/mobile-ui/

**实施内容**

- 实现导航、登录、角色、聊天和陪伴状态页面。
- 冻结原生startSession、mute、cancelTurn、endSession、getSnapshot接口及事件类型。
- 前台viewer读取原生实际播放状态；后台暂停WebView。
- 提供模拟原生引擎以支持界面测试，真实音频由A5和A6实现。

**交付物**：共享手机界面；桥接类型；UI和原生状态一致性测试

**验收**：AC09、AC12、AC19。对应需求：R04、R05、R06。

**外部条件**：无需真实账号；使用 mock。

**派工提示**

~~~text
你负责 T11 实现手机共享界面与原生接口，角色 A4。请阅读 docs/blueprint/README.md、ARCHITECTURE.md、CONTRACTS.md、自己的任务卡及 ACCEPTANCE.md。允许修改：apps/mobile/src/；packages/mobile-bridge/；tests/mobile-ui/。前置任务：T04、T09。目标：统一双端界面和原生音频的调用方式。验收：AC09、AC12、AC19。外部条件：无需真实账号；使用 mock。在独立分支或worktree实现并测试，共享契约变更交A0协调。提交复现步骤、真实测试证据、限制和交接报告。
~~~

## T12 实现 iOS 生产音频与灵动岛

负责人：A5。阶段：G2。前置任务：T01、T06、T11。

让手机界面暂停时仍能正确管理持续语音。

**允许修改**：apps/mobile/ios/；tests/ios/；docs/reports/T12/

**实施内容**

- 原生实现收音、播放、VAD、回声处理、WSS、凭据续期和恢复。
- 接入会话租约、epoch、取消集合、尾帧边界和播放回报。
- 实现ActivityKit、锁屏布局、真实控制动作与生命周期清理。
- 处理来电、蓝牙、耳机、静音和权限，记录真机行为。

**交付物**：Swift原生引擎与实时活动扩展；真机测试记录；签名与构建说明

**验收**：AC10、AC12、AC13、AC19、AC20。对应需求：R05、R06。

**外部条件**：Mac/Xcode、目标iPhone、签名和真实或受控语音服务。

**派工提示**

~~~text
你负责 T12 实现 iOS 生产音频与灵动岛，角色 A5。请阅读 docs/blueprint/README.md、ARCHITECTURE.md、CONTRACTS.md、自己的任务卡及 ACCEPTANCE.md。允许修改：apps/mobile/ios/；tests/ios/；docs/reports/T12/。前置任务：T01、T06、T11。目标：让手机界面暂停时仍能正确管理持续语音。验收：AC10、AC12、AC13、AC19、AC20。外部条件：Mac/Xcode、目标iPhone、签名和真实或受控语音服务。在独立分支或worktree实现并测试，共享契约变更交A0协调。提交复现步骤、真实测试证据、限制和交接报告。
~~~

## T13 实现 Android 生产音频与通知

负责人：A6。阶段：G2。前置任务：T02、T06、T11。

可靠运行前台服务和后台语音引擎。

**允许修改**：apps/mobile/android/；tests/android/；docs/reports/T13/

**实施内容**

- 原生实现音频、VAD、回声处理、WSS、凭据续期和连接恢复。
- 实现正确服务类型与权限，通知中的静音和结束直接作用原生引擎。
- 接入租约、epoch、取消、尾帧等待和真实播放回报。
- 处理厂商电池管理、权限撤销、耳机与来电，提供准确结束状态。

**交付物**：Kotlin原生引擎与服务；通知入口；设备回归报告

**验收**：AC11、AC12、AC13、AC19、AC20。对应需求：R04、R06。

**外部条件**：主力Android真机及另一厂商或版本回归资源。

**派工提示**

~~~text
你负责 T13 实现 Android 生产音频与通知，角色 A6。请阅读 docs/blueprint/README.md、ARCHITECTURE.md、CONTRACTS.md、自己的任务卡及 ACCEPTANCE.md。允许修改：apps/mobile/android/；tests/android/；docs/reports/T13/。前置任务：T02、T06、T11。目标：可靠运行前台服务和后台语音引擎。验收：AC11、AC12、AC13、AC19、AC20。外部条件：主力Android真机及另一厂商或版本回归资源。在独立分支或worktree实现并测试，共享契约变更交A0协调。提交复现步骤、真实测试证据、限制和交接报告。
~~~

## T14 实现首发直播平台适配器

负责人：A7。阶段：G3。前置任务：T03、T06。

用实际授权把平台事件转换成统一LiveEvent。

**允许修改**：services/api/app/live/；tests/live/

**实施内容**

- 按T03结论实现首发平台登录授权、心跳、重连和关闭。
- 标准化消息ID、时间、用户标识和能力清单，建立去重。
- 平台凭据只在服务端使用，真实与mock模式明确。
- 补充断线、权限失效和字段缺失测试。

**交付物**：生产直播适配器；平台契约样例；真实连接验证

**验收**：AC15、AC20、AC21。对应需求：R03。

**外部条件**：T03通过的实际平台权限与测试直播间。

**派工提示**

~~~text
你负责 T14 实现首发直播平台适配器，角色 A7。请阅读 docs/blueprint/README.md、ARCHITECTURE.md、CONTRACTS.md、自己的任务卡及 ACCEPTANCE.md。允许修改：services/api/app/live/；tests/live/。前置任务：T03、T06。目标：用实际授权把平台事件转换成统一LiveEvent。验收：AC15、AC20、AC21。外部条件：T03通过的实际平台权限与测试直播间。在独立分支或worktree实现并测试，共享契约变更交A0协调。提交复现步骤、真实测试证据、限制和交接报告。
~~~

## T15 实现直播回应策略与队列

负责人：A1。阶段：G3。前置任务：T07、T14。

让角色有节奏地回应弹幕。

**允许修改**：services/api/app/orchestrator/live_scheduler/；tests/live-scheduler/

**实施内容**

- 实现50条上限、30秒过期、去重合并和优先级。
- 实现主播选消息、暂停自动回应、停止当前回复与空闲发言开关。
- 广播上下文隔离；观众消息不能修改人设或触发任意操作。
- 以20条每秒负载测试稳定性，确认语音队列不会无限增长。

**交付物**：直播调度策略；压力与隔离测试；可调参数说明

**验收**：AC16、AC17、AC19。对应需求：R03、R09。

**外部条件**：无需真实账号；使用 mock。

**派工提示**

~~~text
你负责 T15 实现直播回应策略与队列，角色 A1。请阅读 docs/blueprint/README.md、ARCHITECTURE.md、CONTRACTS.md、自己的任务卡及 ACCEPTANCE.md。允许修改：services/api/app/orchestrator/live_scheduler/；tests/live-scheduler/。前置任务：T07、T14。目标：让角色有节奏地回应弹幕。验收：AC16、AC17、AC19。外部条件：无需真实账号；使用 mock。在独立分支或worktree实现并测试，共享契约变更交A0协调。提交复现步骤、真实测试证据、限制和交接报告。
~~~

## T16 部署观测预算与备份

负责人：A8。阶段：G2。前置任务：T05、T06。

让不同设备连接同一可观测、可恢复的服务。

**允许修改**：infra/；.github/workflows/；docs/deployment/；tests/deployment/

**实施内容**

- 提供本地及HTTPS部署配置，保持单会话运行进程约束。
- 加入阶段耗时、错误、供应商用量、额度与并发限制。
- 配置秘密注入、日志脱敏、数据库备份和恢复。
- 建立网页、后台、Android和Mac上iOS构建流程；原生平台修改由对应owner审阅。

**交付物**：部署脚本和操作说明；观测与预算面板；备份恢复及构建记录

**验收**：AC01、AC21、AC22、AC25。对应需求：R07、R08。

**外部条件**：服务器与预算需要明确；真实云资源创建在相应授权后执行。

**派工提示**

~~~text
你负责 T16 部署观测预算与备份，角色 A8。请阅读 docs/blueprint/README.md、ARCHITECTURE.md、CONTRACTS.md、自己的任务卡及 ACCEPTANCE.md。允许修改：infra/；.github/workflows/；docs/deployment/；tests/deployment/。前置任务：T05、T06。目标：让不同设备连接同一可观测、可恢复的服务。验收：AC01、AC21、AC22、AC25。外部条件：服务器与预算需要明确；真实云资源创建在相应授权后执行。在独立分支或worktree实现并测试，共享契约变更交A0协调。提交复现步骤、真实测试证据、限制和交接报告。
~~~

## T17 桌面完整流程集成验收

负责人：A8。阶段：G1。前置任务：T07、T08、T09、T10。

证明桌面端在真实AI服务下完整工作。

**允许修改**：tests/integration/desktop/；docs/reports/T17/

**实施内容**

- 运行真实中文样本及取消、回声、记忆和删除场景。
- 测量首播和停止延迟，记录统计样本与方法。
- 缺陷分派给原owner修复并复测，不在验收任务直接重写他人模块。

**交付物**：桌面端到端报告；缺陷与回归记录

**验收**：AC04、AC05、AC06、AC07、AC08、AC09、AC18、AC19。对应需求：R01、R02、R07、R09。

**外部条件**：真实云端服务、可用麦克风和扬声器或耳机。

**派工提示**

~~~text
你负责 T17 桌面完整流程集成验收，角色 A8。请阅读 docs/blueprint/README.md、ARCHITECTURE.md、CONTRACTS.md、自己的任务卡及 ACCEPTANCE.md。允许修改：tests/integration/desktop/；docs/reports/T17/。前置任务：T07、T08、T09、T10。目标：证明桌面端在真实AI服务下完整工作。验收：AC04、AC05、AC06、AC07、AC08、AC09、AC18、AC19。外部条件：真实云端服务、可用麦克风和扬声器或耳机。在独立分支或worktree实现并测试，共享契约变更交A0协调。提交复现步骤、真实测试证据、限制和交接报告。
~~~

## T18 双端手机后台与跨设备验收

负责人：A8。阶段：G2。前置任务：T07、T08、T12、T13、T16。

证明两种手机在真实使用条件下达到陪伴目标。

**允许修改**：tests/e2e/mobile/；docs/reports/T18/

**实施内容**

- 双端各做60分钟会话与2小时混合测试，覆盖锁屏、蓝牙和网络。
- 实际让UI/JS暂停后跨越令牌过期，验证原生续期和继续交互。
- 验证电脑转手机接管与OBS观察者重连，不出现双播放或误取消。
- 测WebView模型表现、耗电、流量和中断恢复。

**交付物**：双端真机报告；跨设备演示；已知设备限制

**验收**：AC09、AC10、AC11、AC12、AC13、AC14、AC19、AC20、AC23。对应需求：R04、R05、R06、R09。

**外部条件**：iPhone、Android、Mac签名环境、可访问HTTPS后台与真实AI。

**派工提示**

~~~text
你负责 T18 双端手机后台与跨设备验收，角色 A8。请阅读 docs/blueprint/README.md、ARCHITECTURE.md、CONTRACTS.md、自己的任务卡及 ACCEPTANCE.md。允许修改：tests/e2e/mobile/；docs/reports/T18/。前置任务：T07、T08、T12、T13、T16。目标：证明两种手机在真实使用条件下达到陪伴目标。验收：AC09、AC10、AC11、AC12、AC13、AC14、AC19、AC20、AC23。外部条件：iPhone、Android、Mac签名环境、可访问HTTPS后台与真实AI。在独立分支或worktree实现并测试，共享契约变更交A0协调。提交复现步骤、真实测试证据、限制和交接报告。
~~~

## T19 真实直播与 OBS 联调验收

负责人：A8。阶段：G3。前置任务：T10、T15、T16。

证明实际直播间能稳定使用角色回应。

**允许修改**：tests/e2e/live/；docs/reports/T19/

**实施内容**

- 至少30分钟真实直播演练，验证自动回应、主播控制和断线。
- 核对真实消息来源、公开画面和音频路由。
- 同时打开OBS与控制台，验证仅指定播放端发声。
- 测试OBS刷新、过期凭据、私人记忆隔离和观众指令边界。

**交付物**：真实直播联调报告；OBS配置说明；故障恢复记录

**验收**：AC03、AC15、AC16、AC17、AC20。对应需求：R03、R09。

**外部条件**：授权直播间、平台能力、OBS与云端服务。

**派工提示**

~~~text
你负责 T19 真实直播与 OBS 联调验收，角色 A8。请阅读 docs/blueprint/README.md、ARCHITECTURE.md、CONTRACTS.md、自己的任务卡及 ACCEPTANCE.md。允许修改：tests/e2e/live/；docs/reports/T19/。前置任务：T10、T15、T16。目标：证明实际直播间能稳定使用角色回应。验收：AC03、AC15、AC16、AC17、AC20。外部条件：授权直播间、平台能力、OBS与云端服务。在独立分支或worktree实现并测试，共享契约变更交A0协调。提交复现步骤、真实测试证据、限制和交接报告。
~~~

## T20 完整第一版验收与交付

负责人：A0。阶段：G3。前置任务：T17、T18、T19、T16。

以证据决定R1完成，并交付可安装和可复现版本。

**允许修改**：docs/reports/release/；release metadata

**实施内容**

- 汇总全部必测项；未通过项修复或经明确范围变更后重新定义版本。
- 从干净环境复现，核对版本、资源清单、构建签名和安装。
- 交付Android安装包、iOS签名测试版本、桌面入口、运维和回退说明。
- 公开上架或公开部署作为单独发布事项，保留用户最终发布决定。

**交付物**：完整验收矩阵；可安装产物及校验值；版本说明与下一版候选清单

**验收**：AC01、AC02、AC03、AC04、AC05、AC06、AC07、AC08、AC09、AC10、AC11、AC12、AC13、AC14、AC15、AC16、AC17、AC18、AC19、AC20、AC21、AC22、AC23、AC24、AC25、AC26。对应需求：R01、R02、R03、R04、R05、R06、R07、R08、R09。

**外部条件**：前置任务真实验收完成；测试分发和公开发行明确区分。

**派工提示**

~~~text
你负责 T20 完整第一版验收与交付，角色 A0。请阅读 docs/blueprint/README.md、ARCHITECTURE.md、CONTRACTS.md、自己的任务卡及 ACCEPTANCE.md。允许修改：docs/reports/release/；release metadata。前置任务：T17、T18、T19、T16。目标：以证据决定R1完成，并交付可安装和可复现版本。验收：AC01、AC02、AC03、AC04、AC05、AC06、AC07、AC08、AC09、AC10、AC11、AC12、AC13、AC14、AC15、AC16、AC17、AC18、AC19、AC20、AC21、AC22、AC23、AC24、AC25、AC26。外部条件：前置任务真实验收完成；测试分发和公开发行明确区分。在独立分支或worktree实现并测试，共享契约变更交A0协调。提交复现步骤、真实测试证据、限制和交接报告。
~~~
