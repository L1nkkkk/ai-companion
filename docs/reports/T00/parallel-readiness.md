# T00 未完成项与离线并行放行

核对日期：2026-09-20。用户授权继续现有架构并派发 T04；本记录不关闭 T00、不修改原验收标准。

代码基线为 `66b0e541a91aa727b33b3997b39bce178bbaee6a`，由实现起点 `cfffbf6b307e58421d0316cda67d027a5f39be7d` 合入设计提交 `28d62d0898e1e1ff57113b3613fcddfc87a0e469`。T04 在独立 `agent/T04-contract-mocks` 分支、`C:\Users\Link\Dev\Neuro-Saki-T04` worktree 开展。原环境分支及其未提交的本机报告保留。

## 尚未关闭的项目

| 项目 | 已有证据与缺口 | 阻塞什么 | 是否阻塞离线 T04 |
|---|---|---|---|
| AC01 第二台开发机复现 | T00 原报告只证明同机干净 clone。本次 Windows 已有独立资源恢复、检查、Unity 干净构建和同包 600 秒证据，位于原 worktree 的 `docs/reports/U01/local-environment/README.md`；尚未按 T00 的跨机身份、全部操作和审阅要求核销。M4 Mac 尚无现场记录 | T00 / AC01 的正式关闭、多平台复现结论 | 否；T04 从冻结基线与锁依赖开始，独立提供启动和测试步骤 |
| Mac 原生依赖锁、Xcode、签名 | 原报告缺实测。旧 RN Pod/Gem 锁属于历史起点；ADR15 后须由移动 owner 明确 Unity + Swift 实际工程的依赖和签名，不能靠补旧锁宣称新路线可用 | T01、后续 iOS 原生构建和真机验收 | 否 |
| Android JDK 发行版与补丁清单 | T00 台账未完成；需在实际移动工具链测量，不沿用另一台机器配置 | T02 实际构建与后续 Android 复现 | 否 |
| AC02 消费者一致性 | T00 已有 schema/样例静态校验；状态、二进制、取消及多语言向量由 T04 增补 | 契约消费侧验收；这是 T04 的工作内容 | 否，不能循环等待 T04 先完成 |
| 手机真机与直播授权 | 对应 T01–T03 外部资源，不能由模拟服务替代 | G0 相关技术验证及后续 AC10–AC15 | 否 |

依据：[T00 原报告](README.md)、[T00 原实测记录](verification.json)、[架构第 11 节](../../blueprint/ARCHITECTURE.md)、[原验收表](../../blueprint/ACCEPTANCE.md)、[ADR15](../../adr/0015-unity-client-and-design-only-a0.md)。原报告历史状态不覆盖本次新增证据，也不据此回填未发生的原生测试。

## 允许并行的条件

1. 保留 `T00.status=awaiting_external` 及 `T04.depends_on=[T00]`。依赖是冻结契约和基线就绪；本次仅显式放行离线子范围，不能传播成 T00 或 G0 完成。
2. T04 使用冻结 R1 1.0 契约、正反例与 AIC1 音频格式。不得改 `contracts/` 或 `CONTRACTS.md`，不得把 U01 的 `generation` / WAV 当作 R1 `session_epoch` / PCM 帧。
3. mock 独立启动、仅本机、无云账号与真实收费调用；界面、健康状态、直播事件及文档明确标为 mock。故障注入仅属于测试入口，不扩充生产接口。
4. 使用独立分支/worktree、固定依赖，记录测试与源码身份；不得改 Unity、原生工程或引入 React/RN 产品实现。
5. 根共享文件仅允许登记过的最小开发依赖变更：本次开发集成 owner 为当前 T04 实现会话，负责 `pyproject.toml`、`uv.lock` 中新增精确固定的 WebSocket 测试依赖，以及将 T04 检查接入 `tools/check.py` / 独立契约 CI。不得升级已有依赖。设计与独立审阅检查契约哈希不变。
6. T04 自动测试证明离线服务与测试消费者行为；正式 Unity/原生消费者、真实扬声器回调、真实云端、真机后台、平台授权和远端 CI 各自保留独立证据门槛。任务只有按原 AC02/AC07 范围审阅后才能关闭。

T01/T02/T03 可同期做资源登记、独立 spike 准备及有条件的可行性测试；缺 Mac/签名/手机/账号授权的步骤记录外部阻塞，不等待它们才开发桌面，也不把本机 mock 计作真实验证。
