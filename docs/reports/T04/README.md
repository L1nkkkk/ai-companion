# T04 离线 mock 与自动测试交接

日期：2026-09-20。状态：**本地交付与独立审阅通过，任务进入 `review`，等待按原任务标准验收与集成**。T00 保持 `awaiting_external`；本报告没有关闭完整 AC02/AC07、G0 或 R1。

## 代码与工作目录

| 项目 | 实际值 |
|---|---|
| 开发分支 | `agent/T04-contract-mocks` |
| 开发目录 | `C:\Users\Link\Dev\Neuro-Saki-T04` |
| 基线提交 | `66b0e541a91aa727b33b3997b39bce178bbaee6a` |
| 已验收 Unity 实现起点 | `cfffbf6b307e58421d0316cda67d027a5f39be7d` |
| 合入的设计提交 | `28d62d0898e1e1ff57113b3613fcddfc87a0e469` |
| **本次实现提交** | **`345fd5db9175b69270b6112da16e62a14fefb841`** |
| 干净复现目录 | `C:\Users\Link\Dev\Neuro-Saki-T04-verify`，detached 于同一实现提交 |
| 远端状态 | 已 fetch 核对原实现/设计 tips 未变；本次分支未推送，未声称远端 CI 已执行 |

原目录 `C:\Users\Link\Dev\Neuro-Saki` 仍在 `env/U01-local-windows`，其未提交的环境报告保留。没有改动 Unity 产品源码、资源清单、C# 共享边界、生产 FastAPI 服务或正式 `contracts/`。

先完成 [T00 核对与并行条件](../T00/parallel-readiness.md)，再按 [派发记录](../../tasks/T04/DISPATCH.md) 实施。根共享依赖的唯一新增是固定开发依赖 `websockets==16.0`，用于实际 WebSocket 运行及测试；已有依赖版本没有升级。来源为 [项目在 PyPI 的 16.0 发布](https://pypi.org/project/websockets/16.0/)，下载 URL 和 SHA-256 已写入 `uv.lock` 与 [验证记录](verification.json)。契约九份冻结文件校验通过，`contracts/baseline.json` 未改变。

## 现在可以使用什么

- 独立 FastAPI mock，默认 `127.0.0.1:8765`，显式返回 `mode=mock`；R1 控制和音频 WebSocket 分离，模拟会话、设备角色、单次票据、租约、幂等、取消和恢复。
- 确定性 300 ms / 24 kHz / mono / PCM16LE 测试片段，每轮三个 AIC1 帧；有明确标记的自制静态角色占位。输入 PCM 只计数、不存录音。占位不是 Live2D 模型或真实语音。
- 延迟、取消、旧 epoch、重复控制/音频、断线注入；直播模拟事件始终 `platform=mock`，队列有界、去重并过期。
- schema 正反例、严格帧解析、有界参考消费者、Python/Node/C# 三语言固定字节向量及独立坏帧测试；新增契约 CI，并接入原统一检查。

完整 API、故障配置和客户端时序见 [mock 使用说明](../../../tests/mocks/README.md)；字段、缓冲上限、消费者方法及覆盖矩阵见 [契约测试说明](../../../tests/contract/README.md)。测试控制入口 `/mock/*` 不加入生产协议。未实现的 `/v1/*` HTTP REST 返回明确的 501；没有伪装生产账号、数据库或云服务能力。

## 本机直接运行

本机已实测：Windows 11 `10.0.26200`、Python `3.12.10`、uv `0.12.17`、Node `24.19.0`、pnpm `11.19.0`、PowerShell `7.6.5`。原环境安装的完整路径记录仍在 `C:\Users\Link\Dev\Neuro-Saki\docs\reports\U01\local-environment\README.md`。

```powershell
Set-Location C:\Users\Link\Dev\Neuro-Saki-T04
. C:\Users\Link\Dev\ai-companion-dev-tools\scripts\Enter-Environment.ps1
uv sync --locked
uv run python tools/run_mock.py
```

另开一个终端并使用同一固定环境：

```powershell
Set-Location C:\Users\Link\Dev\Neuro-Saki-T04
. C:\Users\Link\Dev\ai-companion-dev-tools\scripts\Enter-Environment.ps1
Invoke-RestMethod http://127.0.0.1:8765/health/live
uv run python tools/check_contract.py
```

`check_contract.py` 自行在空闲回环端口启动临时服务、执行真实连接测试并清理自建进程，不依赖手工运行的 8765 实例。手工服务以 `Ctrl+C` 停止。完整仓库检查仍是：

```powershell
pnpm install --frozen-lockfile --ignore-scripts
uv sync --locked
uv run python tools/check.py
```

其他机器安装上述固定工具并使用自己的 PATH 即可；提交的运行器没有作者绝对路径依赖。新目录从实现 SHA 创建 worktree，再 `uv sync --locked`、`uv run python tools/check_contract.py`，无需复制 `.venv`、`node_modules` 或 Unity Library。

## 真实验证结果

| 检查 | 结果与证据 |
|---|---|
| 冻结依赖安装 | 新开发 worktree 的 pnpm frozen/ignore-scripts 和 uv locked 安装通过；新增依赖经精确锁定后恢复。未升级旧依赖 |
| `uv run python tools/check.py` | **退出 0**：原 API 2 项、新增 T04 **161 项**；Ruff、冻结契约/设计检查、历史 TypeScript/网页/手机 JS 打包通过。原始日志留在开发目录 `.tmp/t04-full-check.log` |
| T04 自动测试 | **161 passed，0 failed，0 error，0 skipped**；覆盖 schema、AIC1、时序消费者和 mock 服务。包含实际执行 Node 与 C# 子进程 |
| 三语言向量 | Python/Node/C# 各验证三个字节常量向量；非重复 UUID、uint32 边界、短末帧和 PCM 正负极值；Node/C# 各拒绝十类坏帧 |
| 实际本机 HTTP + 双 WebSocket | 进程存活、正常三帧/7200 样本、生成与播放确认分开、重试不重复生成、延迟中取消、直播去重、注入断线后 epoch=2 且新 client_seq=1 接受；[TCP 证据](tcp-smoke.json) |
| 日志与进程 | 启动器关闭含票据的握手 INFO 日志；smoke 在结束后检查当次日志没有票据，精确清理自己启动的 Windows 进程树。本次未留下运行的测试服务 |
| 干净源码复现 | 从实现 SHA 新建 detached worktree、创建新 `.venv`，仅复用下载缓存；**161 passed、0 skip**，再次真实 TCP smoke 通过；[干净目录 TCP 证据](clean-tcp-smoke.json)。这是同机复现，不代替第二物理机 AC01 |
| 独立审阅 | 发现并修复六项问题：快照遗留旧队列、同 epoch resync、重连序号作用域、慢连接发送无上限、epoch 溢出、票据日志；全部回归通过，见 [独立审阅](review.md) |
| 远端 CI / 正式消费者 | workflow 已交付；**未推送、未运行远端 CI、未集成正式 Unity/手机消费者**，继续保留相应验收边界 |

机器可读结果、测试数量、环境版本、实现文件哈希和原始检查日志哈希见 [verification.json](verification.json)。复现目录日志与 JUnit 留在对应 `.tmp`；报告只保存不含凭据的结构化结果。两个已存在的 Python 依赖弃用提示，以及历史 RN 模块导出/颜色变量提示，均未导致失败；本次不为消除提示升级冻结依赖。

本机 TCP 测试的取消回报约 2.653 ms，干净复现约 3.015 ms，均只是一轮 mock 控制通道测量，不是 P95，不是本机扬声器静音指标。参考消费者 `take_ready()` 不播放声音，合成 `playback.progress` 不算真实播放证据。手动时钟验证 500 ms、64 KiB、两秒缓冲等阈值逻辑，也不代表真实设备性能。

## 后续交接与未关闭项

T00 仍为 `awaiting_external`：新增本台 Windows 证据可参与 AC01 核销，但 Mac/Xcode/签名、实际移动工具链及跨机验收仍按记录逐项审阅。T01–T03 已登记资源准备与外部阻塞；见 [可行性准备](../T00/feasibility-readiness.md)。没有将模拟器、JS 打包、离线弹幕当作真机或真实平台成功。

桌面个人陪伴顺序已锁定为 **文字 → TTS 与开发期 Live2D → 语音输入 → 显式长期记忆**，对应 [既有任务映射](../../development/DESKTOP-PRIORITIES.md)。继续使用已冻结 Unity/C#/Python 架构。U01 的 `unity-preview/1`/NDJSON/WAV 与本次正式 R1 AIC1 mock 保持分离；U01-03 提供自身 fixture。显式长期记忆仍归 T06/T08 的会话、权限、来源与删除规则，本地 History 不算长期记忆完成。

T04 交付可供后续 owner 直接复用；台账进入 `review`，由验收/集成 owner 按原 AC02/AC07 和任务范围关闭。真实云端、原生播放录音、手机后台、授权直播、完整门槛均保持原标准，没有缩小或替换。
