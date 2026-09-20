# 最终便携包独立复核

最新复核时间：2026-09-21 02:09（Asia/Shanghai，600 秒运行退出后再次核对）。复核 owner：preview_backend。仅读取最终产物、来源文件和已有测试日志；本轮没有执行包内程序、启动服务或操作 Unity。最终包的授权启动及真实 Player 运行仅由 root 集成 owner 进行。

## 当前推荐候选 b94af81

最终包的清单、ZIP、来源副本与冻结启动器指纹一致，完整性检查无失败项。此结论覆盖交付包内容，不代替最终真实 Player 的界面、音频和 600 秒运行验收。

- 源码：`b94af81d73101d684b856bd0942de778ea74074a`。
- 干净构建工作区：`C:/Users/Link/Dev/Neuro-Saki-U01-desktop-verify`；复核 HEAD 与上述 SHA 一致，`git status --porcelain` 无输出。
- 包目录：`C:/Users/Link/Dev/ai-companion-dev-tools/builds/NeuroSaki-Desktop-b94af81`。
- ZIP：`C:/Users/Link/Dev/ai-companion-dev-tools/builds/NeuroSaki-Desktop-b94af81.zip`。
- 外部清单：同目录 `NeuroSaki-Desktop-b94af81-package-manifest.json`。
- 独立复核数据：`C:/Users/Link/Dev/ai-companion-dev-tools/evidence/desktop-b94af81-package-review.json`。

| 项目 | 实际结果 |
| --- | --- |
| ZIP 长度 | 70,313,849 bytes |
| ZIP SHA-256 | `64b016202945914afd59d70a3ea3ef07dbf9a8a82ef435eee3597a8654486b84` |
| 包内清单 SHA-256 | `5ab365e6f43eaae7f73cdb0f82c8b0a2c2ab7a94276a4e44b6380ee9d8ae18e1` |
| 外部清单 SHA-256 | `26c59b953ffaab8f6bf6c02c4887b80549fd1c83fde50e7f7cc3beefe0a93aa7` |
| 启动器 SHA-256 | `6feae1488c22cadbb7205ef5192471ccba581f04eb98eb35cc571c9f0c368875` |
| 复核数据 SHA-256 | `9caf6c5c639e59490a08c9db66e7555ec13e1d42bfe284e9c3315144d3c38cf8` |
| 清单文件数 / ZIP 成员数 | 3,014 / 3,015；差额为清单自身 |
| 解压后文件总长度 | 175,143,409 bytes |
| source_dirty / mode | `false` / `fixture` |

相对 e3fd507，源码仅改自有 `MaoAvatarPresenter.cs` 和 `DesktopEvidenceRunner.cs`：集成 owner 修复重复 greeting 导致的旧 SDK playable 图累积，并增加图大小观测；本轮未改第三方 SDK。包内只改变 `boot.config`、Avatar/Composition 的 DLL/PDB 及 `unity-build-result.json` 共 6 项，文件集合不变；launcher、后台、依赖和许可文件全部保持相同哈希。对此新 ZIP 仍重新执行了全部成员和来源核对，没有仅凭增量差异沿用旧包结论。

## b94af81 真实便携包运行完成及退出复核

root 使用本包的 Python、launcher、backend 和 Player 执行真实 Windows 运行，2026-09-21 02:08:42 正常结束，launcher 返回 0；没有重建或替换包内文件。复现入口为 [run-portable-qa.py](run-portable-qa.py)，命令见 [report.md](report.md)。本轮独立复核读取最终 `player-result.json`、原始日志和文件校验结果，不另行启动测试。

| 实测项 | 最终结果 |
| --- | --- |
| 运行时长 | 600.0128248 s |
| 预热后采样帧 | 35,813 |
| 帧时间 P95 / 最大 | 16.9 / 32.3072 ms |
| 采样帧 ≤33.3 ms / >1 s | 100% / 0 帧 |
| scriptedChecksCompleted / failures | `true` / `[]` |
| 运行期 Error / Warning 事件计数 | 0 / 0 |
| 实际播放开始 / 完整播放 | 22 / 1 |
| 有声播放中停止 / 提交后立即取消 | 20 / 10 |
| 仅文字完成 | 1 |
| greeting 次数 / 每帧观测最大 playable 节点 | 38 / 6 |

本轮 `acceptedGenerationStops=0`、`downloadStops=0`；上述 10 次不能写成已接受生成中或下载中取消。相关独立场景证据另见 [qa-faults.md](qa-faults.md)。Windows 指定进程回环另见 [qa-loopback.md](qa-loopback.md)：本候选 20 个有声停止样本的 P95 为 155.845 ms，使用声明的 `1e-9 FS` 阈值，严格零阈值不通过的结果保留；该回环指标不是本报告中的 Unity 同步回调计时。

退出后 02:09:10 再次核对目录 3,015 个文件和 ZIP 全部成员：无新增、缺失或 SHA-256 改变，上述 ZIP/manifest/launcher 指纹全部保持。02:09:12 的只读系统查询未发现该便携目录下的存活进程或本机 8000 监听；私有 runtime 配置已删除。launcher 日志按顺序记录 `player_closed` 与 `owned_processes_stopped`。

- 运行证据目录：`C:/Users/Link/Dev/ai-companion-dev-tools/evidence/desktop-b94af81-600/`。
- `player-result.json` SHA-256：`9b51d15ce46a9fbf167f9e07c07c2716fe56816dbbf75e21436ad764b7704f16`。
- `runtime/launcher.log` SHA-256：`2a60d49c186ba951cf55d3efafad9093dcf5172d8b8a38e0337da54c38f21568`。
- 退出后独立复核：同证据目录 `package-after-run-review.json`，SHA-256 `f41859a26349dad63222866a92240009a82bf94d3d525b0b65a77e2aba5a5d32`。

退出日志仍有原生 `MemoryLeaks` 即时标记，`allocatedMemory=65812` bytes；它不在运行期 Error/Warning 计数中，未隐藏，也不据此报告“零泄漏”。内存趋势的独立分析见 [qa-memory.md](qa-memory.md)。完整 gate、第二台机器验证和视频证据仍待完成，不能用本机 600 秒和包完整性通过替代。

## 前候选 e3fd507：保留完整性记录，不再推荐

以下为 2026-09-21 01:48–01:50 的前候选核对。其包完整性检查通过；后续运行发现 greeting playable 累积，因此被 b94af81 替代，完整性通过不代表当时的 F01 运行通过。

- 源码：`e3fd507aacbce6a3e87311e9a33ef4fb93d032dc`。
- 干净构建工作区：`C:/Users/Link/Dev/Neuro-Saki-U01-desktop-verify`；复核时 `git status --porcelain` 无输出。
- 包目录：`C:/Users/Link/Dev/ai-companion-dev-tools/builds/NeuroSaki-Desktop-e3fd507`。
- ZIP：`C:/Users/Link/Dev/ai-companion-dev-tools/builds/NeuroSaki-Desktop-e3fd507.zip`。
- 外部清单：同目录 `NeuroSaki-Desktop-e3fd507-package-manifest.json`。
- 独立复核数据：`C:/Users/Link/Dev/ai-companion-dev-tools/evidence/desktop-final-package-review.json`。

| 项目 | 实际结果 |
| --- | --- |
| ZIP 长度 | 70,313,107 bytes |
| ZIP SHA-256 | `a81f568d30c7383c236acdb4351cd8499eb7ea6333a826ac4c372f9af4221de1` |
| 包内清单 SHA-256 | `bc68938538700f24a1f6418c5b04d53d5ca8967e4ee43ec7fef03698c92fae60` |
| 外部清单 SHA-256 | `a6ec3287dd3074de7e6cf6a58629350feb3c054f9c25d09df3f8906a9fa299f1` |
| 启动器 SHA-256 | `6feae1488c22cadbb7205ef5192471ccba581f04eb98eb35cc571c9f0c368875` |
| 复核数据 SHA-256 | `b9b7b60531fd9bd9289fb280cc376dda0d1e800139864e7cb561045b11c45a10` |
| 清单文件数 / ZIP 成员数 | 3,014 / 3,015；差额为清单自身 |
| 解压后文件总长度 | 175,142,114 bytes |
| source_dirty / mode | `false` / `fixture` |

## 两候选均执行的核对方法与结果

使用独立 Python 标准库 `hashlib`、`zipfile`、`pathlib` 读取产物，没有调用打包器的校验函数，也没有执行包内代码。全部 ZIP 成员完整读取，同时校验 ZIP CRC、文件长度和 SHA-256；每个成员与包内清单及目录实文件对应。路径集合完全一致，无额外成员、重复路径、大小写冲突、越界路径或符号链接。清单自身由外部清单的独立哈希覆盖；内外清单的共有字段一致。

来源副本比较全部通过：最终 Player 的 226 个文件、后台模块的 43 个文件、固定 Python 3.12.10 独立运行时的 2,297 个文件，以及 9 项资源/Python 许可文件，与各自来源逐字节相同。Python 比较按明确的打包排除规则跳过原始 `site-packages` 和缓存；包内依赖另行核对。包内启动器与干净源码和本轮冻结指纹三方一致。

`Start-NeuroSaki.cmd` 指向同包 `python/pythonw.exe` 和 `launch_desktop.py`，路径使用引号，包含 `-B`。未发现用户运行配置、历史数据库、日志、PID 文件、`.env`、Python 字节码或缓存目录。对文本配置/代码/说明另行扫描私钥头和长 token/api_key/secret 字面值，没有命中；扫描没有输出任何文件内容或凭据。

## 许可证与依赖

Live2D SDK/NOTICE/Core/Redistributable、字体 OFL、Liberation Sans OFL、资源来源说明、测试音频来源与 Python LICENSE 均存在且与来源一致。包内保留原始的 Live2D sample data 版权说明。以下 13 个运行依赖均有 METADATA 与许可文件，名称/版本与清单逐项一致；未夹带 pip、pytest、ruff、httpx 或 packaging 开发依赖。

| 依赖 | 版本 |
| --- | --- |
| annotated-doc | 0.0.5 |
| annotated-types | 0.8.0 |
| anyio | 4.15.1 |
| click | 8.5.0 |
| fastapi | 0.141.1 |
| h11 | 0.16.0 |
| idna | 3.20 |
| pydantic | 2.13.5 |
| pydantic_core | 2.46.5 |
| starlette | 1.6.0 |
| typing_extensions | 4.16.0 |
| typing-inspection | 0.4.4 |
| uvicorn | 0.53.0 |

此项核查证明许可证和来源说明随包保留，不扩展为新的商业使用或再分发授权。

## 启动器修复与已有检查证据

冻结启动器采用 Windows Job Object `KILL_ON_JOB_CLOSE`，通过 `PROC_THREAD_ATTRIBUTE_JOB_LIST` 在 `CreateProcessW` 时直接加入，避免先运行或先创建后加入的空窗；唯一可继承句柄为显式 NUL 标准流句柄。Job 建立、属性设置或进程创建失败均明确停止启动，没有无监督降级路径。

此前同一启动器的 14 项 portable 测试已通过，其中两项用真实自有测试进程强制结束 launcher：分别覆盖首个后台已创建、后台与测试 Player 均已创建；两次均验证直接子进程及孙进程退出、测试端口释放，独立的无关测试进程仍存活。没有结束真实 Unity。强退不执行 Python `finally`，旧私有配置可能留存于受限 ACL 目录；下次持锁启动会生成新 token 并原子替换。正常窗口退出仍执行关闭自有后台和删除运行配置。

已只读核对全仓库日志 `C:/Users/Link/Dev/ai-companion-dev-tools/evidence/desktop-final-check.log`，SHA-256 为 `62740ad288b7f99d3df73bbfc6df15d89e74d37e676ca60b10acc3a95602108b`。其中记录 75 tests passed、2 项既有 Python 弃用警告，Ruff check/format、TypeScript typecheck、历史 web 构建与 mobile bundle 均完成。日志中的 mobile bundle 另有现有 React Native exports fallback 警告，未隐藏；这些历史工程检查不构成真实手机能力验证。

本包明确为 fixture：固定演示回复和随包测试音频，不含真实云对话/TTS 或麦克风启用结论。旧 dev 包、启动失败与此前测量限制仍保留在其他报告中，未用本次完整性检查覆盖或抹除。
