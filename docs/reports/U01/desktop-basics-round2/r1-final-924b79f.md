# 924b79f 原生导出独立复核（六格记录完成）

2026-09-21，preview_backend 仅读取实际产物、源码和证据，未操作 UI、启动 Player/后台/Unity 或改动包。**R1 六个有效格、相关取消与正常关闭的记录检查通过，root 的人工观察均已归档；完整 U-G0 仍交 A0 判定。** 结构化数据及原始文件哈希见 [r1-final-924b79f.json](r1-final-924b79f.json)。人工操作观察属于集成 owner root，本报告不将其转述成复核者亲自操作。

全部有效格均来自 `924b79f05b6c90983bdc5c18344aa125375fa57c` 的同一便携包，内清单 SHA-256 为 `b05472f60a49424f6c198f35980f472d0e2ff13f2116d0bc5c5885e411a8c934`；每次 case 的包身份与退出后重新读取完全一致。证据根为 `C:/Users/Link/Dev/ai-companion-dev-tools/evidence/desktop-r2-924b79f`。未沿用旧 3110 的成功格或 dev03 预检。

## 六个有效格

表中阶段来自实际点击导出的 `Stopping.TriggerPhase`，并关联相同 request/generation 的耐久历史；`export-ready.json` 本身不等于点击时处于该阶段。文件选择时长由原生 worker 保留的 Stopwatch 时间戳计算，不用主线程收到通知的时间替代。

| 实际阶段 / 操作 | case | 原生窗口秒数 | 窗口期间帧数 / 最大 ms | 全运行最大帧 ms | 历史终态与采样 |
| --- | --- | ---: | ---: | ---: | --- |
| Thinking / 保存 | export-generation-save-02 | 24.5604944 | 1473 / 21.6235 | 40.0467 | Interrupted，0/0，已接受 turn 存在、正文为空 |
| Speaking / 保存 | export-playback-save-02 | 16.3764677 | 982 / 23.8588 | 26.4619 | Interrupted，137216/192000 |
| Speaking / 取消 | export-playback-cancel | 31.8087270 | 1908 / 27.9717 | 27.9717 | Interrupted，111104/192000 |
| Ready / 保存 | export-idle-save | 13.7636783 | 825 / 22.3294 | 23.8699 | Played，192000/192000 |
| Ready / 取消 | export-idle-cancel | 10.7683295 | 646 / 20.9501 | 25.7881 | Played，192000/192000 |
| Thinking / 取消 | export-generation-cancel | 42.2600101 | 2536 / 22.2115 | 27.9957 | Interrupted，0/0，已接受 turn 存在、正文为空 |

用冻结的 `tools/unity/qa/analyze_export_qa.py` 重新分析以上六格，结果均为 `automated_checks_passed`。独立补查得到：

- 顺序均为 Stopping → Stopped → Capturing → DialogOpen → Saved 或 Cancelled → Finished；保存额外包含 Saving。进入文件选择前已本机停止，Stopped 时 mouth=0。
- 全程帧数与 Player 结果一致，覆盖运行结束，逐帧间隔连续；没有达到一秒的帧。表中 40.0467 ms 是生成保存整次运行的实际最大值，不隐藏为窗口期间较小的 21.6235 ms。
- 原生窗口期间各次五秒角色样本都有不同 breath 值，记录到的 mouth 均为 0，支持主循环与角色参数持续更新。窗口区间的帧/参数筛选使用主线程 observed_seconds，边界有一次通知递送帧的误差；原生停留时长独立用 worker 时间计算。
- 三个保存 JSON 的会话 ID、记录列表与最终耐久历史完全相等。生成保存具有非空 turn ID、空正文、0/0 Interrupted，实际播放启动为 0；不会把停止前旧 Generated 作为导出结果。空闲导出保留原 Played，不制造中断。
- 三个取消格没有写出目标文件；全部格在 Stopped 后旧 request/generation 的非零音频回调为 0，且没有旧 operation 再次启动播放。这是 Unity 回调证据，不是 OS 回环尾音延迟。
- 全部格 Player errors=0、failures 为空，脚本完成；launcher 正常退出 0，私有运行配置清除，包文件未变。程序计数不替代人工窗口操作观察。

六格均已有 root 通过 Sky 实际操作、desktop_ui 依据任务消息转录的 `manual-observations.json`；本复核已核对其关联文件哈希并收录原始观察归属。播放取消格验证主窗口音量 80%→鼠标 59%→Left 键 49%、角色 idle 姿态变化、主窗口 Stop 取消导出、旧回复仍 Interrupted，以及 Alt+F4 正常退出。以上有效格当前为 DPI 96 / 100%、1280×800，不占 R3 的 125% / 150% 格。

生成取消格还覆盖了此前阻断 3110 的嵌套覆盖确认：root 在实际 Thinking 打开导出、选中同 case 的 guard 文件并出现覆盖确认后，切回 Player 按 Escape。诊断记录为同一 STA 先向 DirectUI 子提示投递 `TDM_CLICK_BUTTON / IDNO`，随后父保存窗恢复启用才投递 `IDCANCEL`，最终原生调用真实返回 `selected=False error=0`；CSV 在 85.671 秒记录 Cancelled 和 Finished。它不是仅观察窗口消失或消息投递成功。

guard SHA-256 仍为 `8b60a6a25c500f67c11604aa032e7612cffeccc83f86992dd96027b990ff195d`，与 root 操作前值相同。原会话仍保留空正文、0/0 Interrupted；随后 root 点击“+”并正常关窗，新空会话 `5e86bb75-252e-4750-b2a2-dbbab1900f3d` 已持久化，原记录未被覆盖。末尾 active conversation 改变不影响按导出时原 ID 复核。该格的原生区间有 2536 帧、9 个持续变化的呼吸样本、mouth 均为 0，正常退出 0 且无 CLOSE_TIMEOUT。人工消息已在 JSON 中明确归属 root，独立核对保留 F8 截图及原生日志哈希。

## 额外正常退出回归

`export-idle-nested-quit` 在覆盖确认尚未选择“是/否”时，由 **240 秒 QA deadline 调用 Application.Quit** 触发正常退出路径。root 后来的 Alt+F4 工具操作遇到前台窗口已不存在，因此不把物理按键写成退出原因。完整 Player 记录为 240.006778 秒、14218 帧、最大 22.0032 ms；原生保存调用从打开到返回历时 175.3239646 秒。

退出请求日志依次为 cancel_request、DirectUI 的 IDNO、父窗 IDCANCEL、closed 和 `native_return selected=False error=0`，最后一项时间戳 `2435012640`；取消请求至真实原生返回为 39.4183 ms。未出现 CLOSE_TIMEOUT，launcher 退出 0、配置清除、包未变，guard 哈希保持上述值。该时长是原生清理时间，不是音频停止延迟。

本例的 EvidenceRunner 在 deadline 先 Finish、关闭 CSV 并解除导出事件监听，再请求退出，因此 CSV 只到 DialogOpen，Player 保留 `Scripted checks did not finish within the run.`，原导出分析器仍判失败；这些原始结果没有被覆盖或放宽。本例依据独立原生日志、正常退出和文件保护证据，单列为自动正常退出清理回归，不当成第七个导出矩阵成功格，也不把开发预检中的手动 Alt+F4 移记到最终候选。

另三组使用同一最终包的实际回归已独立重算：

| case | 实际记录 | 耐久历史 | 最大帧 ms |
| --- | --- | --- | ---: |
| regression-generation | 10 个不同 operation 在已接受、正文未回的阶段取消；播放启动 0 | 10 条 Interrupted，均 0/0、turn ID 非空 | 27.2263 |
| regression-late-audio | 10 个不同 operation 已收到音频 HTTP 响应头、body 未完成时取消；播放启动 0 | 10 条 Interrupted，均 0/192000；总长已从 audio.ready 获知，未播放 | 29.2244 |
| regression-close | 有真实非零输出后自动 Application.Quit；1 次 playback-started，再 Stopped | Interrupted，12800/192000 | 18.2871 |

生成/下载使用原有 `slow_generation` / `slow_download` 场景，不使用导出格的八秒额外延迟。三组均脚本完成、零错误及失败、帧记录连续；Player 和 launcher 退出 0，无关闭超时或历史写盘失败，运行配置及 pending 配置清除，包身份未变。后台 exit 1 来自启动器在 Player 退出后回收自有后台，不是遗漏在运行的服务。播放关闭是脚本触发正常生命周期，不声称物理 Alt+F4；生成/下载取消不计入有声停止回环分布。

## 生成阶段的明确延迟注入

生成格由报告封装 `run-generation-export.py` 使用包内 Python、原启动器及冻结 `PreviewSettings` / `create_preview_app` 公共接口运行。`fixture_delay=8.0` 加上原 `slow_generation` 的 3 秒等待，为实际 accepted generation 留出人工点击窗口；若未取消，正文最早约 11 秒后返回，speech fixture 也受 8 秒延迟影响。此设置、确切 backend_command 和封装 SHA 均记录于 case。没有修改包、验收阈值、原分析器或产品默认时序；本格不能代表默认回复速度。

## 无效尝试完整保留

- `export-playback-save`：错过八秒播放窗，实际触发为 Ready，随后取消，原分析器标 `invalid_attempt`。整次最大帧 619.0711 ms 和阶段不匹配失败均保留，不作为播放格通过，也不改写成产品保存失败。
- `export-generation-save`：root 报告操作因用量中断未执行，240.009 秒自动结束，实际没有请求或打开导出窗口，保留 `Scripted checks did not finish within the run.`。原分析器因缺少 export-ready 等文件返回 `incomplete_evidence`，本次复核分类为“未实际执行的无效尝试”；正常退出 0 不等于该格通过。

最终包重启也已独立复核：`dpi125-960x640/case.json` 明确复用已存在的 `dpi125-1280x800/userdata`。使用两次运行各自归档的不可变历史快照，而非读取后来继续变化的共享数据库，确认原两条记录逐字段保持一致：原 Interrupted 为 150016/192000，新增一轮后共四条，新 Interrupted 为 187904/192000。原设置 volume=0.462811023，显示为 46%。复核者查看已有 `desktop-manual-01.png`，可见恢复的“你 / 第二行”、已中断回复、准备好状态及音量 46；后次 events 中原 request 没有 playback-started。root 的实际 Ready / 未自动重播观察、截图与快照哈希均附于 JSON。正常重启和第二次关闭均使用同一候选，未借用旧包结果。

错误路径仅“确定”提示的取消已在 dev03 做真实预检，范围和来源见 [dev03-precheck.json](dev03-precheck.json)，不将旧预检转记为最终包实测，也不新增 A0 的门槛。运行后完整包再次核对归 [R2 报告](qa-package-924b79f.md)；R3/R4 独立登记。完整门禁交 A0 审阅。
