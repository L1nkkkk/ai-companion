# U01 开发会话派工提示

本文件用于用户在另一个开发会话发出指令。设计会话只编写与审阅，不因生成提示自动创建任务或执行开发。

当前 U01-00 已启动并提交 PR #3，状态为 `awaiting_external`。以下首个会话段落保留为初始派工模板；当前应让原开发会话按 [A0 审阅中的续作提示](../../reports/U01/acceptance/PR-003-review.md) 补齐，不重复创建 U01-00。

## 第一个会话：U01-00

复制以下内容到新的开发会话，关联 `ai-companion` 仓库：

```text
你负责 U01-00「Unity 工程与版本验证」，角色为 Unity 集成开发 owner。
这是实现会话；A0 所在会话只负责设计和验收，发现设计问题向 A0 提案。

先同步 origin，找到包含 docs/adr/0015-unity-client-and-design-only-a0.md 的
design/unity-desktop-taskbook 分支（若已合并则使用包含相同决定的 main），
记录实际起点 SHA，再创建独立 worktree 和 agent/U01-00-unity-foundation 分支。
不要从 P01 网页实现分支起步，也不要修改其他开发任务的目录。

阅读 AGENTS.md、docs/blueprint/README.md、docs/blueprint/ARCHITECTURE.md、
docs/tasks/U01/TASKBOOK.md、INTERFACES.md、ACCEPTANCE.md、SOURCES.md，
以及 docs/blueprint/planning/unity-desktop.json。

目标：验证 Unity 6.3 LTS 候选版本与官方 Cubism Unity 正式 R5/URP 组合，
选择合法适用的 Live2D 演示模型，建立可供后续模块协作的 Unity 工程，
实际构建并启动最小 Windows x64 Player。已有 Unity 2022.3 不代表 SDK 兼容。
冻结确切 Unity/URP/SDK/Core/包版本、模型和字体来源及哈希，保留 .meta，
配置缓存忽略规则；提交 C# 模块边界建议，不擅自改变预览协议或 R1 契约。

允许：apps/unity 的初始骨架、ProjectSettings、Packages、共享 Contracts 类型、
assets/manifest、tools/unity、必要 .gitignore，以及 docs/reports/U01/U01-00。
共享配置写入由你单独负责；不得删除旧 React/RN 工程、重写云服务或修改 contracts。
本任务只做工程、示例导入与构建验证；完整 UI、音频、云适配留给后续子任务。

不要调用付费云服务。本任务没有云密钥也可以完成。
缺少 Editor、构建模块、有效许可或可用样例时先检查资源与兼容性，
完成能独立推进的准备，准确记录缺口；不要伪造包版本、模型或构建成功。

交付：分支/起点/最终 SHA、固定版本建议、最小 Player 及 SHA-256、
构建原始日志、Player 实际启动证据、资源获取说明、C# 接口映射、
新工作目录复现步骤、已知限制、给后续 agent 的接线和目录说明。
UA01/UA02 对应范围的结果写入 docs/reports/U01/U01-00/。
完成后提交可审阅的 PR，交 A0 评审；不要把 U01 或 R1 整体标成完成。
```

## 后续模块会话模板

```text
你负责 {U01 子任务 ID}，角色 {owner}，在独立开发会话完成实现和自测。
设计依据：ADR15、docs/tasks/U01/ 下的任务书、接口与验收表。
精确起点 SHA：{已通过 U01-00 的提交，或指定集成提交}。
依赖与证据：{前置子任务、提交和报告}。
允许目录：{只填写本子任务路径}。
共享文件 owner：{集成开发 owner}；接口变更向 A0 提案后再统一实施。
本次阶段：{U-G0 / U-G1 / U-G2}；必须覆盖的 UA：{编号}。
可用资源与费用限制：{真实已提供的模型、设备、provider 和预算}。
无外部资源时继续 fixture/错误处理，真实阶段保留待验证状态。
交付实现、相关测试、实际 Player 或接口证据、复现、限制和交接报告。
不得修改他人总场景、锁文件或借用旧网页实现来声称 Unity 已完成。
```

## 返回设计会话进行验收

```text
请以 A0 身份验收 U01-{子任务/门槛}，只评审、运行已有产物和提出返工项。
PR：{URL}
提交：{SHA}
Windows 包与 SHA-256：{位置 / 校验值}
报告：{路径}
本次声明通过的 UA：{编号}
缺口：{外部资源、未执行项和已知失败}
如发现问题，请给对应开发 owner 写明复现和修复验收条件，不在本会话代写实现。
```
