# T04 独立审阅记录

本记录针对 `agent/T04-contract-mocks` 工作树，起点为 `66b0e541a91aa727b33b3997b39bce178bbaee6a`。审阅者只读实现；修复由对应开发 owner 完成。**独立审阅结论：本次离线 mock 与测试消费者范围通过，可交集成 owner 提交验收；没有遗留的本次审阅阻塞项。** 这不是原 AC02/AC07 全产品或 G0 关闭结论。审阅针对待提交候选工作树，完整交付提交 SHA 由集成报告登记。

## 审阅依据与范围

- [派发记录](../../tasks/T04/DISPATCH.md)及 [AC02、AC07](../../blueprint/ACCEPTANCE.md)：冻结正反例、实际时序、PCM 边界和未匹配片段防播放。
- [R1 契约](../../blueprint/CONTRACTS.md)：控制连接内 `client_seq`、同 epoch 权威快照、终态取消、500 ms / 64 KiB 元数据等待、2 秒待播队列、输入租约、uint32 边界及消息超时。
- 审查 `tests/mocks/`、`tests/contract/`、运行/检查工具、CI 和根开发依赖；未修改产品实现、共享契约、Unity 工程或冻结 gate。

## 已复现问题及修复跟踪

| 编号 | 问题与最短复现 | 影响 | 当前状态 |
|---|---|---|---|
| R01 | 参考消费者已收到 `turn.started → assistant.segment → PCM`；同 epoch 快照将 `active_turn_id` 设为 null 后，旧 PCM 仍被接收并可从测试队列取出。 | 权威快照未清空旧轮次待播内容。 | owner 已修复；`test_authoritative_same_epoch_snapshot_retires_old_turn_and_all_pending_work` 已独立通过。 |
| R02 | 未知音频等待达到 500 ms 后触发 resync；同 epoch `session.snapshot` 被 `resync_required` 拒绝。 | 无法依契约在同 epoch 恢复。 | owner 已修复；`test_same_epoch_resync_snapshot_recovers_without_replaying_failed_stream` 已独立通过。失败流不会重新播放。 |
| R03 | 首控制连接以 `client_seq=1` 完成指令，关闭并取得新票据；重连取得新 epoch 后，新连接 `client_seq=1` 被拒绝。 | 序号错误地按 device 跨连接累积，与契约的连接作用域不符。 | 已修复；新连接重置序号、保留 session 幂等窗口。`test_server_edges.py` 回归独立通过，真实回环 smoke 也接受新连接的 seq 1。 |
| R04 | `Session.event` / 音频发送在 session 锁内等待无超时的 WebSocket 发送；使用永不返回的测试 socket 可持续占锁。 | 慢接收者可阻塞取消等后续指令；缺少出站消息等待上限。 | 已修复；控制广播并行，send/close 各有 100 ms 测试实现等待上限并剔除失联连接。两个 blocked-socket 回归独立通过；另测 send 与 close 均不返回时仍能取回锁。此为控制流测试，不是真实慢网性能结果。 |
| R05 | 创建 epoch `4294967294`，连续两次显式 speaker 接管；第二次返回 500，并留下 epoch `4294967296`。 | 在校验前写入超出 uint32 的执行代次；恢复连接亦需同类保护。 | 已修复；第二次接管受控返回 409，epoch 保持 `4294967295`、会话 ended，需新建会话。接管/重连上界回归和独立定向复测通过。 |
| R06 | 启动器只设 `access_log=False`；固定 Uvicorn 的 WebSocket INFO 握手日志仍包含查询参数。本地首次 smoke 日志计得 3 条含 `ticket` 的握手记录。 | 未满足契约对票据日志隐藏的要求，尽管这些仅是离线短期 mock 票据。 | 已修复；启动器使用 warning 级日志，真实 smoke 在子进程树退出后检查当次完整日志，结果 `server_log_ticket_values=absent`。本文及审阅输出不包含票据值。 |

输入租约到期时遗留 `listening` 状态的问题也已由 owner 修复，新增回归证明快照转为 idle、输入设备清空且后续上传被拒绝。

## 独立验证与证据

在本机已冻结工具环境中执行：

```powershell
uv run python -m pytest tests/contract tests/mocks -o 'pythonpath=tests services/api' --junitxml=.tmp/t04-review-tests.xml -q
```

结果为 **161 passed, 2 warnings in 7.41s**：143 项契约/参考消费者测试，18 项服务测试。包含冻结 schema 正反例、PCM 编解码及 UUID 端序、参考消费者时序、取消和快照回归、Python / Node / PowerShell 承载的独立 C# 固定向量，以及本次全部服务边界回归。两条警告来自已锁定依赖中的 Starlette/httpx 与 anyio 旧接口弃用提示，没有为了消除警告升级基线。

独立 JUnit 证据位于本机候选 worktree 的 `.tmp/t04-review-tests.xml`，SHA-256 为 `34a2a25eed973f77c6b5fd025409227c8aac759a7c6ddd4ed33a51ad17f96062`。测试和原始日志属于本地证据，不是源码运行依赖。

另已审阅集成 owner 的真实 TCP smoke 证据 `.tmp/t04-smoke.json`：3 帧/7,200 样本；重复命令维持同一 turn；1,000 ms 模拟生成等待期间可处理取消；直播 mock 重复事件未增加队列；注入断线后 epoch 提升到 2 且新连接 seq 1 被接受；当次服务日志未输出票据。取消回报时间只属于本机模拟服务往返，不能等同真实扬声器静音时延。后续最终统一检查和源码提交身份以集成报告为准，不使用旧 smoke 的数值替代新检查。

集成 owner 随后执行 `uv run python tools/check.py` 返回 0；已核对 `.tmp/t04-full-check.log` 的最终成功记录。统一检查包含原基础 API 和历史 Node 工程检查；历史手机 JavaScript bundle 成功不能代表 Android/iOS 原生构建或真机运行。

根依赖差异仅增加开发依赖 `websockets==16.0` 及对应锁条目；已有依赖没有升级。`contracts/` 与 `docs/blueprint/CONTRACTS.md` 无差异。CI 使用明确的依赖恢复与同一检查入口；本次没有声称远端矩阵已运行。

## 验收边界

参考消费者的 `take_ready` 只是取出测试队列，模拟 `playback.progress` 只证明完成语义；均不代表物理扬声器播放、录音、真实 TTS 或云端取消性能。手动时钟的 500 ms / 2 秒测试验证阈值逻辑，不能作为设备实测延迟。

本地回环 TCP 与分开的控制/音频 WebSocket 能证明 mock 可启动及通道互通；ASGI 测试和跨语言向量能证明本次离线实现一致。它们不证明 Unity、手机原生消费者已经集成，也不证明第二台物理开发机的 AC01 或远端 CI 已运行。

T00 继续保留外部等待状态。T01、T02、T03 的资源盘点与准备不等于 iOS / Android 真机后台或真实直播接入可行性通过。U01 的 NDJSON/WAV 预览协议保持独立；T04 没有引入长期记忆，历史记录不能视为记忆。
