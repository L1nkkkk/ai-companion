# R3：最终候选的实际 Windows 缩放矩阵

产品 `924b79f05b6c90983bdc5c18344aa125375fa57c`，使用正式便携包。**4 / 4 格实际操作、记录核对与系统缩放恢复已完成，开发证据已交付，待 A0 复核。** 实际操作由 root / 集成 owner 通过 Sky 完成；desktop_ui 按任务消息转录，并只读核对文件及已保存的 Player PNG。结构化记录见 [dpi-final.json](dpi-final.json)，不在本报告代替 A0 宣布 R3 或 U-G0 验收通过。

| 实际系统缩放 | 1280×800 | 960×640 |
| --- | --- | --- |
| 125% | 已记录：DPI 120、scale 125、HRESULT 0 | 已记录：DPI 120、scale 125、HRESULT 0 |
| 150% | 已记录：DPI 144、scale 150、HRESULT 0 | 已记录：DPI 144、scale 150、HRESULT 0 |

## 125% / 1280×800

证据目录：`C:/Users/Link/Dev/ai-companion-dev-tools/evidence/desktop-r2-924b79f/dpi125-1280x800`。root 在 Windows Settings 将显示器 2 从原 100% 改为 125%，显示器 1 未改。Player 实际记录 1280×800、windowDpi=120、monitorScalePercent=125、monitorScaleHresult=0，采用真实系统缩放，不是拉伸图片。

| 项目 | 实际操作和结果 |
| --- | --- |
| 中文候选 | 物理 n、i 后 root 看到 Windows 拼音候选“1 你”；Space 确认为“你”，没有新增消息或误发 |
| 换行与发送 | Shift+Return 后计数 2；输入“第二行”后两行草稿为 `你\n第二行`、计数 5；Enter 一次发送恰一轮 |
| 停止与历史终态 | Escape 中断实际播放；历史仅 1 条用户 + 1 条 assistant，assistant 为 Interrupted，150016 / 192000 样本 |
| 键盘音量 | 80%→鼠标 56%→物理 Left 46%，PNG 中可见 46% |
| 历史与控件 | 历史显示“你 第二行”、2 条；打开、导出、删除、关闭、清除全部历史、刷新均可见 |
| 角色与布局 | 已保存 PNG 中发布角色帽/脚可见，输入、发送、停止、朗读、音量及历史面板没有必要控件裁切 |
| 关闭 | Alt+F4 后 Player / launcher / wrapper 均退出 0；包未变，运行配置已清理。后台退出码原样记录为 1，不声称全部进程退出码均为 0 |

已独立查看 [草稿与换行](C:/Users/Link/Dev/ai-companion-dev-tools/evidence/desktop-r2-924b79f/dpi125-1280x800/desktop-manual-01.png)、[中断与音量](C:/Users/Link/Dev/ai-companion-dev-tools/evidence/desktop-r2-924b79f/dpi125-1280x800/desktop-manual-02.png)、[历史面板](C:/Users/Link/Dev/ai-companion-dev-tools/evidence/desktop-r2-924b79f/dpi125-1280x800/desktop-manual-03.png)。这些 F8 图片来自 Player 背缓冲，**不包含 OS 拼音候选窗**；候选出现和确认只归于 root 的现场 Sky 观察，不能冒称有候选截图。

本轮 161.816 秒、9,520 帧，P95 16.9 ms、最大 63.9762 ms，无超过一秒帧，运行错误和警告均为 0。它是操作场景记录，不代替 F01 长时间负载结论。manual 模式没有自动提交脚本，`scriptedChecksCompleted=false` 不伪装成脚本通过。

第二格显式复用了第一格 userdata 检查正常关窗后的恢复。因此在下一轮修改前已保存第一格历史原字节为 `history-after-run.json`，SHA-256 `4f657be7d202dd29d1454d3ac6c663654bd5c1cfb2e0345d59ff4c975a18a60a`；后续不能拿持续变化的共享库替换该快照。root 另保留的 `history-after-first-close` / `settings-after-first-close` 不删除。环境、结果、图片、快照及人工观察文件哈希均在 JSON 中。

## 125% / 960×640 与重启恢复

证据目录：`C:/Users/Link/Dev/ai-companion-dev-tools/evidence/desktop-r2-924b79f/dpi125-960x640`。实际环境为 960×640、DPI 120、scale 125、HRESULT 0；case 明确传入上格已存在的 userdata。启动时恢复“你 / 第二行”两条记录、assistant Interrupted、音量 46%，Ready 且没有自动重播，见 [恢复后的第一张实际帧](C:/Users/Link/Dev/ai-companion-dev-tools/evidence/desktop-r2-924b79f/dpi125-960x640/desktop-manual-01.png)。

root 再次用物理 n/i → 候选“你” → Space，Shift+Enter 后输入“小窗口”，形成 `你\n小窗口`、计数 5，无误发；一次 Enter 发送并开始播放，Escape 中断。音量经鼠标 46%→51%、物理 Left→41%。历史为 4 条，六个历史操作仍全部可见，角色全身及输入/发送/停止/音量静音/自动朗读/新会话/设置/历史均在窗口内。已只读查看同目录 `desktop-manual-02/03/04.png`，分别支持草稿、中断与 41%、完整历史面板的观察；这些图片同样不包含 OS 候选窗。

第二轮历史原字节单独保存在本 case 的 `history-after-run.json`，SHA-256 `d471025a18583dd8a778ea9536a47b55e899bd659d693858d13f8c37bf5f0179`。旧 assistant 保留 150016 / 192000，新 assistant 为 Interrupted、187904 / 192000。正常 Alt+F4 后 Player/launcher/wrapper 均退出 0，包未变，配置清理；后台退出码按原记录为 1。本轮 169.841 秒、10,007 帧、P95 16.9 ms、最大 29.429 ms，错误/警告及超过一秒帧均为 0。恢复证据也交 R1 独立复核引用，不新增一轮虚构启动。

## 150% / 1280×800

证据目录：`C:/Users/Link/Dev/ai-companion-dev-tools/evidence/desktop-r2-924b79f/dpi150-1280x800`。root 在 Settings 将显示器 2 的实际缩放从 125% 改到 150%，本格使用新的独立 userdata。Player 记录 1280×800、DPI 144、scale 150、HRESULT 0。

物理 n/i 看到候选“你”，Space 确认无消息；Shift+Return 后计数 2，输入“第二行”后 `你\n第二行` 计数 5。一次 Enter 发送，Escape 中断实际播放，落盘为一轮 2 条，assistant Interrupted、132096 / 192000。音量 80%→鼠标 53%→物理 Left 43%。历史 2 条及全部六个操作、角色全身、底部文字和必要主窗口控件可见。`desktop-manual-01/02/03.png` 已只读查看，支持草稿、43% / 中断和历史面板观察；IME 候选仍只归属 root 的现场观察。

历史快照 `history-after-run.json` SHA-256 为 `9c6cd46a462c66e8b5261558051d7f418e1961458349f368563f166de7a794a0`。Alt+F4 后 Player/launcher/wrapper 退出 0，包未变、配置清理；后台退出码 1 原样保留。本次 144.105 秒、8,458 帧、P95 17.0 ms、最大 63.1898 ms，无超过一秒帧，错误/警告均 0。

## 150% / 960×640

证据目录：`C:/Users/Link/Dev/ai-companion-dev-tools/evidence/desktop-r2-924b79f/dpi150-960x640`。显示器 2 保持实际 150%，本格使用独立新 userdata。Player 记录 960×640、DPI 144、scale 150、HRESULT 0。

root 物理输入 n/i 后看到候选“你”，Space 确认未误发送；Shift+Return 插入一次换行，输入“小窗口”后草稿为 `你\n小窗口`、计数 5。一次 Enter 发送后 Escape 中断实际播放。音量由 80% 经鼠标调为 46%，再由物理 Left 调为 36%。历史 2 条及打开、导出、删除、关闭、清除全部历史、刷新均可见，发布角色全身和必要主窗口控件均无裁切。已只读查看 `desktop-manual-01/02/03.png`，分别支持双行草稿、中断/36% 音量及历史面板观察；不把背缓冲图片当作 OS 候选窗截图。

独立历史快照 `history-after-run.json` SHA-256 为 `80363a6b1ccc413439bdecf7a1c4152acd9620711e8bd38d18aca3c5032f88b5`，仅一轮 2 条，assistant 为 Interrupted、180224 / 192000。Alt+F4 后 Player/launcher/wrapper 均退出 0，包未变、配置已清理；后台退出码仍按原记录为 1。本次 151.162 秒、8,883 帧、P95 16.9 ms、最大 66.8624 ms，无超过一秒帧，错误/警告均为 0。

## 环境恢复与边界

全部四格结束后，root 使用 Sky 在 Windows Settings 将显示器 2 从 150% 恢复到原来的 100%，刷新后确认 100%（推荐）、1920×1080；显示器 1 从未更改。恢复观察与退出后环境清理见 [final-environment.json](final-environment.json)。Settings 包含账号信息，因此不保存其画面；这项声明来自 root 的实际操作，不来自注册表修改或图片缩放。前三格人工记录中的“待矩阵完成后恢复”保留其记录时点含义，由这份最终恢复声明结清。

正式证据根没有遗留运行配置，Player 与 8000 监听均已清理，新源码 worktree 干净。全包运行后复核另见 [包完整性报告](qa-package-924b79f-postrun.md)。R1 的 100% 导出操作、dev03 的 125% 预检和 R4 的录制环境不填入本矩阵；四格均为同一机器实测，不扩展为全部 IME/显示器矩阵或 F04 第二机器结果。最终结论交 A0 复核。
