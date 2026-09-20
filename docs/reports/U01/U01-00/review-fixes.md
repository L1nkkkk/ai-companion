> 历史记录：下文描述 R5/Unity 6.3 阶段。2026-09-20 已依 ADR16 完成 R4_1 正式迁移和 600 秒基础验证；当前状态见 [report.md](report.md)。R1/R2 已由 A0 提交 5286a56 关闭。

# PR #3 首轮审阅修复

日期：2026-09-20。对应 A0 的 `U01-00-R1`、`U01-00-R2`；原审阅位于设计分支提交 `b397e7dccac0b1699387e7f1e59f0ca7040fb2fa` 的 `docs/reports/U01/acceptance/PR-003-review.md`。开发修复提交：`7f905b5d0a027844cf45d554558cc6440e21a2a7`。本文件请求 A0 复核两个审阅项，不自行修改设计台账或宣布关闭。

## R1：UI 可通过 Session 完成所需操作

[接口提案](interfaces-proposal.md) 第 3.1/3.2 节补齐即时音量、当前设置快照、麦克风枚举/选择、音色列表刷新/可用状态、历史分页列表和有界 JSON 导出。HistoryStore 提供对应 ListAsync、ExportAsync 和容量通知；Capture 提供设备枚举。新增类型列明全部公开字段、容量、错误、取消与线程规则，并给出 UI 调用示例及逐项接线表。

UI 只持有 Session。修改 AutoRead 等无关设置时，可保留暂不可用的原设备/音色 ID；只有实际变更选择时校验列表成员，真正录音/朗读前再检查可用性。输出设备提案明确固定系统默认，不展示尚无实现支持的选择器。

开发自查和独立文档审阅覆盖上述调用链，未提前实现业务，也未修改冻结 Contracts、HTTP schema 或设计文档。仍为待 A0 冻结的提案，不是 C# 编译或产品验收证据。

## R2：独立墙钟超时与实际进程回归

`Test-Player.ps1` 在进程启动前开始计时，以 `Seconds + GraceSeconds` 作为外部等待上限（默认 600+30 秒）。超时只终止该脚本保存的 Process 对象，额外终止等待最多 5 秒，返回 124；其他验证失败返回 1。启动后的异常路径同样有限清理自有进程。所有已启动测试均写入运行状态、耗时、PID、退出码、超时/终止信息，并保留可用日志及哈希。该时限不包含启动前文件哈希和结束后的证据写入。

在上述干净提交上运行 `tools/unity/tests/test_player_harness.py`：编译受控 Windows 进程，用真实 PowerShell 子进程执行被测脚本。四个用例检查正常退出、无限挂起、缺少截图和错误结果；另启动同一可执行文件的独立挂起进程，验证超时处理不会按程序名误杀它。外层测试自身也有 40 秒限时，清理使用持有的进程句柄，防止 PID 重用。正常结果、截图、帧文件及实际启动程序/证据哈希的原有验证均保留。

正式结果：[player-harness.json](review-fixes/player-harness.json)。每项有实际耗时、wrapper/fixture 退出码、保留的 fixture 日志、manifest 哈希和证据哈希复核结果。测试摘要明确 `scope=harness_only_not_unity_player`；合成 1×1 图片和 CSV 只保留在忽略的 `.tmp` 内，不能作为真实 Unity 截图或帧率。

完整仓库检查也在该提交上通过，见 [检查日志](review-fixes/foundation-check.txt)。PowerShell 实际执行、Python lint/格式、仓库基础与契约检查、服务测试、TypeScript 和历史 web/mobile JS 构建通过；既有弃用提示和 RN exports 回退警告仍在。未执行 Unity 导入、C# 编译、URP 渲染或原生移动构建。

## 环境与剩余工作

本机复查没有新增目标 Editor；官方发布元数据新增检查的 `6000.3.21f1` 安装器也经重定向返回 404，见 [环境续查](environment-install.md)。没有获得 Unity 6.3，也没有 Windows Player 包。UA01、UA02 与整体 U01-00 继续 `awaiting_external`；接口和超时修复的交审不放行后续依赖任务。
