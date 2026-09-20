# Windows 便携包工具与复核

日期：2026-09-21。本报告覆盖最终便携候选的打包器、启动器与内容复核。包内真实 Player 的授权启动及 600 秒运行由 root 集成 owner 执行。

## 当前交付候选

- 源码：`b94af81d73101d684b856bd0942de778ea74074a`，由干净 verify worktree 构建 `desktop-b94af81`；包清单 `source_dirty=false`。
- 目录：`C:/Users/Link/Dev/ai-companion-dev-tools/builds/NeuroSaki-Desktop-b94af81/`。
- ZIP：`C:/Users/Link/Dev/ai-companion-dev-tools/builds/NeuroSaki-Desktop-b94af81.zip`，70,313,849 bytes。
- ZIP SHA-256：`64b016202945914afd59d70a3ea3ef07dbf9a8a82ef435eee3597a8654486b84`。
- 外部清单：同目录 `NeuroSaki-Desktop-b94af81-package-manifest.json`。
- 独立内容核对见 [qa-package.md](qa-package.md)：3,015 个 ZIP 成员逐个校验 CRC、长度和 SHA-256，无差异；Player、后台、固定 Python 运行时与资源许可来源副本一致。

本轮候选修复自有 Avatar 在重复 greeting 时的旧 SDK playable 图累积，并增加观测；SDK 原文件、启动器、后台和依赖未变。root 集成 owner 已用 b94af81 的真实包内 Python、launcher、backend 和 Player 完成 **600.0128248 秒**运行并正常退出，launcher 返回 0。脚本检查完成、failures 为空，运行期错误/警告计数均为 0；38 次 greeting 后逐帧最大 playable 节点为 6。预热后 35,813 帧的 P95 为 16.9 ms、最大 32.3072 ms，100% 采样帧 ≤33.3 ms。

运行包含 22 次真实播放开始、1 次完整播放、20 次有声播放中停止、10 次提交后立即取消及 1 次仅文字完成；10 次立即取消不冒充已接受生成中或下载中取消。最终指定进程回环 P95 为 155.845 ms，其 `1e-9 FS` 阈值及严格零阈值失败边界见 [qa-loopback.md](qa-loopback.md)。

退出后独立重读全部包文件和 ZIP，仍与上述清单/指纹一致，没有新增缓存或运行文件；私有配置已删除，未发现该包的存活进程或 8000 监听。证据在 `C:/Users/Link/Dev/ai-companion-dev-tools/evidence/desktop-b94af81-600/`，详细哈希和复核见 [qa-package.md](qa-package.md)，复现入口为 [run-portable-qa.py](run-portable-qa.py) 与 [report.md](report.md)。退出日志保留 65,812 bytes 的原生即时内存标记，不声称零泄漏。**完整 gate、第二台机器和视频证据仍待完成。**

## 交付

- `tools/unity/package_desktop.py`：读取既有完整 Player、固定 Python 3.12.10 standalone 与当前冻结 `.venv/Lib/site-packages`，复制 13 个递归运行依赖及各自 dist-info/许可证；不包含 pytest、httpx、ruff 等开发依赖，不复制 Python 原有 pip site-packages、`__pycache__` 或运行令牌/日志。
- `tools/unity/launch_desktop.py`：标准库启动器。包内 `Start-NeuroSaki.cmd` 双击后调用 `python/pythonw.exe`，用户无需安装 Python / PowerShell / Unity。启动器先取得同一用户独占文件锁，检查 8000 端口，生成 URL-safe 随机令牌与私有配置，启动自己的后台及 Player。关闭 Player 后只清理自己创建的子进程和配置文件，不终止其他占端口进程。
- Windows 10+ 使用匿名 Job Object 的 `KILL_ON_JOB_CLOSE`；`CreateProcessW` 的 `PROC_THREAD_ATTRIBUTE_JOB_LIST` 在创建时原子加入，启动器强退也由 Windows 回收受管进程及其后代。Job 句柄仅由启动器持有且不继承，标准流只继承白名单内 NUL 句柄。建立 Job、设置属性或创建失败均明确停止，没有运行无监督子进程的降级路径。
- 启动器强退不执行 Python `finally`：受限 ACL 目录内旧 `config.json` 可能保留，下次持锁启动生成新 token 并原子替换；正常退出才承诺删除运行配置。进程回收与配置删除是两条不同的生命周期保证。
- `%LOCALAPPDATA%/NeuroSaki/preview-runtime` 设置为当前用户独有、禁止继承的 Windows DACL；配置文件在该目录新建后原子替换。Windows 测试确认配置文件 ACL 只有当前用户一条允许项。
- 日志只记录固定事件码，不写令牌、响应正文或异常内容；失败显示 Windows MessageBox 和 `launcher.log` 位置。后台禁用 access log，标准输出/错误不进入包内文件。
- loopback 健康检查不使用系统代理、不跟随重定向。重复启动、缺文件、端口占用、启动超时、后台异常退出均有明确处理。
- 保留 Player 原有资源许可，额外复制 Live2D Components/Core/NOTICE/RedistributableFiles、Mao 资源来源说明、Noto/Liberation 字体许可与测试音频来源。Python 保留 LICENSE.txt，依赖保留 dist-info 许可证。

## 命令

在源码根目录运行：

```text
.venv/Scripts/python.exe tools/unity/package_desktop.py --player-directory <已有完整Player目录> --output <全新便携包目录>
```

默认固定解释器路径为 `C:/Users/Link/Dev/ai-companion-dev-tools/toolchain/python/cpython-3.12.10-windows-x86_64-none`。可以用 `--python-runtime` / `--site-packages` 指向同版本准备好的输入。输出必须不存在，不覆盖、删除或清空已有目录/ZIP。生成包内 `package-manifest.json`、邻接 ZIP 与外部 `*-package-manifest.json`；保存源 HEAD、dirty 状态、精确依赖版本、逐文件 SHA-256、ZIP SHA-256 与包内 manifest 哈希。包内 manifest 不递归包含自身，由外部 manifest 的独立字段覆盖。

## 本机验证

`.venv/Scripts/python.exe -m pytest services/api/tests/unity_preview/test_portable_tools.py -q`：**14 passed**。保留随机配置/日志、锁竞争/释放、端口占用不杀他人进程、缺包错误、受控子进程退出清理、后台启动失败清理、递归依赖和许可证、缓存/运行文件排除、实际 Windows ACL 验证；新增 2 项真实自有 dummy 进程强退测试及 3 项 Job/属性/创建失败测试。

强退测试分别覆盖第一子进程已创建与后台/测试 Player 均已创建的时点，确认直接子进程和孙进程退出、端口释放，独立无关 dummy 保持存活。仅强退自己启动的测试 launcher，没有结束真实 Unity。x64 结构体尺寸与 Win32 ABI 一致，另有独立只读 API 复核，未发现阻断问题。

冻结 launcher SHA-256：`6feae1488c22cadbb7205ef5192471ccba581f04eb98eb35cc571c9f0c368875`，与最终包完全一致。

最终全 API 回归（含上述 14 项）：**75 passed，2 项既有弃用 warnings**；Ruff check/format、typecheck、历史 web/mobile 构建检查通过。日志：`C:/Users/Link/Dev/ai-companion-dev-tools/evidence/desktop-final-check.log`。打包器对最终包的复制后 Python/依赖导入检查也已完成。

## 前候选：e3fd507（已替代）

`NeuroSaki-Desktop-e3fd507.zip` 为前候选，70,313,107 bytes，SHA-256 为 `a81f568d30c7383c236acdb4351cd8499eb7ea6333a826ac4c372f9af4221de1`。当时完整性复核通过，详细清单与证据仍保留在 `qa-package.md`；随后运行发现 greeting playable 累积，故不再作为当前推荐包。75 项 API/14 项 portable 测试覆盖的 launcher、后台未在两候选间改变。新 b94af81 的 3,015 个成员已重新完整校验，不能用此旧包运行结果冒充新包结果。

## 历史尝试：dev-02（不作为当前交付）

用已构建 `desktop-dev-02` 做完整打包冒烟，产物在：

- `C:/Users/Link/Dev/ai-companion-dev-tools/builds/desktop-portable-dev-02/`
- `C:/Users/Link/Dev/ai-companion-dev-tools/builds/desktop-portable-dev-02.zip`
- `C:/Users/Link/Dev/ai-companion-dev-tools/builds/desktop-portable-dev-02-package-manifest.json`
- ZIP SHA-256：`8f800daa5333cfe8866038ef65a0355170b53fc4733043d3dc1f665780f4aed0`

打包器实际调用复制后的 Python 3.12.10 验证 FastAPI/uvicorn/后台模块导入。随后使用包内 Python、包内启动器和真实 FastAPI 后台，给 Player 位置传入受控短时 Python 子进程，在临时 loopback 端口完成私有配置、鉴权就绪、子进程退出、后台回收和配置删除，返回 0。此测试**没有启动 Unity 或操作 UI**，不能替代最终 Player 复核。

该中间包记录源 HEAD `66b0e541a91aa727b33b3997b39bce178bbaee6a` 和 `source_dirty=true`。它只完成了当时的打包/受控子进程冒烟，dev-02 Player 后续真实启动失败另见 `qa-smoke.md`，失败没有隐去。不得把该旧包当成后续提交产物；当前推荐候选以本报告开头列出的 b94af81 包为准。完整 U-G0/U-G1/U-G2 状态保持原验收边界。
