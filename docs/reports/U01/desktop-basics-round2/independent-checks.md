# 第二轮独立工具检查与构建来源准备

2026-09-21，preview_backend 执行。此记录来自冻结前开发目录，不是最终 SHA 的 R2 干净构建结论。真实 Player 操作由 root 完成；本 owner 仅只读复核其初测 CSV/JSON，并在 root 授权、该 Player 退出后运行项目检查。

## 检查入口与实际结果

固定工具环境中执行：

```powershell
. C:/Users/Link/Dev/ai-companion-dev-tools/scripts/Enter-Environment.ps1
uv run python tools/check.py
```

完整入口第二次退出 0：固定工具/9 份冻结契约、蓝图、Ruff check、Ruff format（36 文件）、默认 pytest **75 passed**、工作区 typecheck、历史 web 构建、Android/iOS JS bundle 全部完成。Python 保留两个既有弃用警告；Metro 保留 ReactNativeFeatureFlags exports fallback 警告。JS bundle 不表示手机原生行为通过。

默认 pytest 受 `pyproject.toml` 的 `testpaths = ["services/api/tests"]` 限制：2 foundation、53 preview、6 TCP loopback、14 portable tests，共 75。它不自动收集 `tools/unity/qa`。本轮另行显式执行以下离线工具用例，**20 passed in 1.04s**（导出 8、既有回环分析 5、QPC 音画分析 7）：

```powershell
& .venv/Scripts/python.exe -B -m pytest tools/unity/qa/test_export_qa.py tools/unity/qa/test_process_loopback_analysis.py tools/unity/qa/test_qpc_evidence.py -q -p no:cacheprovider
```

因此本轮共运行 **95 个不同 Python 用例**，不是把重复执行相加。一次显式收齐两类用例的命令为 `uv run python -m pytest services/api/tests tools/unity/qa`；本轮已分别运行，不为计数再重复。`tools/unity/tests/test_player_harness.py` 是需要显式 `--report` 的独立 controlled-process 驱动，不属于这些 pytest 用例；本轮未重跑它，不把合成画面当成真实 Player 证据。

第一次完整入口在尚未冻结的 QPC 两份 Python 工具 Ruff 检查处退出 1（56 项格式/导入规则），未执行后续测试。相应 owner 完成格式修复后才重跑，保留首次失败：

| 文件 | SHA-256 |
| --- | --- |
| `C:/Users/Link/Dev/ai-companion-dev-tools/evidence/desktop-r2-check-all.log` | `f8a9cb16b1532a766cdd84b74150e340439d32ba46a135e054a0d0542b2a26df` |
| `C:/Users/Link/Dev/ai-companion-dev-tools/evidence/desktop-r2-check-all-attempt01-ruff.log` | `708aae9effb0ab4a5ff92083d6dd9cd0d9f638ab598e0488dd971a4f8b1afbf7` |

只读确认 root 的 `C:/Users/Link/Dev/ai-companion-dev-tools/builds/desktop-r2-check01-evidence/Check.log`：第 357 行 History 32、第 369 行 UI 34、第 381 行 Session/Audio 53、第 393 行总通过，最后退出码 0。日志 SHA-256：`5d6a9a8d8f7fadcdda1eefbb3346a856e954b7466795cab74345ff582654b205`。最终冻结产品仍须独立新目录导入与检查。

## 临时 dirty Player 初测，只用于排查

root 操作的 `C:/Users/Link/Dev/ai-companion-dev-tools/evidence/desktop-r2-dev01-export` 最终持续 235.8010943 秒，实际播放中点击导出，再取消并正常关窗。独立读取事件确认 TriggerPhase=Speaking、generation=3；先停止后开原生窗，Stopped 嘴值为 0。最终本机历史为 Interrupted，播放位置 114176 / 192000，未保留为旧 Generated。

主线程观察到的 DialogOpen→Cancelled 区间为 186.059302 秒，其中逐帧 CSV 有 11163 帧、最大间隔 20.9662 ms；全程 13964 帧，最大间隔 26.367 ms，无一秒长帧。此区间 37 次 avatar 采样嘴值均 0，breath 有 37 个不同值、blink 有 5 个不同值，支持主循环与角色继续更新。这里使用 CSV 的 observed_seconds 同一 Unity 时间轴；不把 Stopwatch 原点和原生 QPC 原点直接相减。画面与音量 80→58 的人工可操作性由 root 的实际观察负责。

这次初测既不是最终干净包，也没有六种阶段/结果矩阵，不能计为最终 R1 或 R2 通过。原始文件哈希留作复核锚点：

| 文件 | SHA-256 |
| --- | --- |
| player-result.json | `7f0ccb6ada97d8c399ff47ece9dd0f50242f514fe572e06317c98183b318abe2` |
| export-events.csv | `15a8c928a0c4679faa12539fd26c49c133aadf2950b765cafd44c3bf17522cd2` |
| frame-times.csv | `f8e62fc47f9ec310b7b00732c36ab1a7d0d8b681ae1427658a19cb2dd7e3f0b1` |
| memory-avatar.csv | `e99047c5f74a88d5c3851e4380e02f521eba1cdb8b0de8d538b69347fc3b3eb6` |
| userdata/history/conversations.v1.json | `abb36cac41af7337d5899167abe4ea9fa341a74e87b89a08894319b777786a0f` |

## R2 原始资源缓存复核

实际只读重算 `C:/Users/Link/Dev/ai-companion-dev-tools/resources` 下三份原始文件，长度和 SHA-256 全部匹配冻结清单与 `docs/reports/U01/local-environment/downloads.json`，未恢复或修改任何目标资源：

| 缓存文件 | 字节数 | SHA-256 |
| --- | ---: | --- |
| CubismSdkForUnity-5-r.4.1.unitypackage | 16266045 | `2777b69d4cd02fecd48dc0fe9871700c95943f6d141c18758027bd9aa2ed1de6` |
| NotoSansCJKsc-Regular.otf | 16437364 | `2c76254f6fc379fddfce0a7e84fb5385bb135d3e399294f6eeb6680d0365b74b` |
| Noto-OFL.txt | 4301 | `6a73f9541c2de74158c0e7cf6b0a58ef774f5a780bf191f2d7ec9cc53efe2bf2` |

root 在最终提交的新 worktree 内运行 `python tools/unity/restore_assets.py --accept-live2d-terms --cache C:/Users/Link/Dev/ai-companion-dev-tools/resources`，并保留首次导入前 Library 不存在、HEAD 与干净状态、恢复清单/日志。独立包核对将在 root 提供最终 SHA 与新产物后进行；旧工作区 Library 和本次 dirty 初测不代替这项证据。
