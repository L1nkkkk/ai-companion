# R1 独立审查与实际导出验证计划

日期：2026-09-21。Owner：preview_backend；UI/原生窗口由 desktop_ui 实现，Composition 与真实 Player 启动由 root 统一执行。本文件是返工审查与证据计划，不是 R1/R2 已通过结论。验收依据为 `C:/Users/Link/Dev/Neuro-Saki-U01-acceptance/docs/reports/U01/acceptance/desktop-basics-round1-review.md`。

## 根因与最小修复边界

旧 `HistoryFileExport.Save` 在 Unity 主线程同步调用 `GetSaveFileNameW`；外层 `async` 不会移动这一调用。因此保存窗口等待会阻止主循环的角色更新、状态投递和停止处理。此前 17,728.3486 ms 长帧及关联事件属于已保留的缺陷复现，不能改写为回环尾音测量。

UI owner 拟保留已经过 Mono ABI 验证的 Win32 common dialog，移动到专用 STA 后台线程，通过异步完成结果返回 Unity 主线程。进入导出时先同步 `Session.Cancel(User)`，再等待导出快照；界面说明回复已暂停，禁止发起新会话写操作，停止/音量/角色更新保持可用。原生选择文件不再持有 Unity 主循环。

独立审查还确认写入一致性缺陷：`WriteAfterAsync` 将写入拒绝/异常转为界面错误并完成 Task，旧 `ExportConversationAsync` 随后仍可成功读出磁盘上的旧 Generated 记录。root 已授权 preview_backend 仅修改 `DesktopSessionController.cs` 和 `SessionAudioChecks.cs`：按 operation/role 保留尚未成功保存的记录错误；导出等候稳定写队列后，若目标会话有未保存记录，明确返回 `history_write_failed` 而不调用磁盘导出。同条记录成功保存后可恢复，其他会话不会被错误污染，删除/清空会清理相应错误。Contracts 不变。

新增 12 项有意义的检查，Session/Audio 断言由 41 增至 53。集成 owner 执行的本轮 Check01 已报告 History 32、UI 34、Session/Audio 53 全部通过；后加的正常退出等待接口仍由最终冻结版本检查覆盖。新增 Session 检查覆盖：真正挂起的终态写队列、写入拒绝、写入异常、清除界面错误不能绕过失败、其他会话可导出、同条后续成功写入恢复，以及等候期间继续排队的终态不能漏掉。模块检查不冒充真实原生窗口验收。

## STA 原生窗口必须满足的条件

1. 主循环不使用 `.Wait`、`.Result`、线程 `Join` 或跨线程同步 `SendMessage` 等待文件选择。工作线程只处理原生窗口与文件字节；Unity UI/日志更新回到主线程。
2. `hwndOwner=0` 可避免跨线程模态禁用 Unity，但独立窗口需要明确标题和可发现的取消入口；必须实际检查 Alt+Tab、主窗口点击、原生窗口取消及失焦策略。即使设置“失焦继续播放”为开，进入导出也先本地停止。
3. Explorer hook 获得的是自定义子窗，保存顶窗由 `GetParent` 得到；委托、原生缓冲和取消登记必须存活至原生函数返回。取消覆盖打开前、hook 创建时和返回后；异步 `PostMessage` 不能被当成窗口已经关闭，single-flight 只能在真正返回清理后释放。
4. 覆盖确认或路径错误提示可能成为第二层原生模态窗口。必须测试其打开期间“取消导出”和正常退出，不能仅向被禁用的保存父窗发一次关闭消息便宣称完成。不得按全局窗口标题或进程名清理其他程序。
5. 导出的 conversation ID、store revision、字节快照在选择文件前固定。停止的 Interrupted 终态必须成功保存后才能提供文件；取消文件选择不回滚停止、不恢复旧语音，也不声称保存成功。用户确认的精确文件路径不再自动拼接扩展名绕过覆盖确认。
6. 正常退出仍先本地静音，再让主循环排空历史写队列（现有 Composition 上限 2 秒），随后释放资源。导出返回、取消、关窗重叠时，不访问已销毁的 UI、不恢复旧请求、不在窗口关闭后留下等待线程阻止进程退出。

Win32 依据：[Open and Save As Dialog Boxes](https://learn.microsoft.com/en-us/windows/win32/dlgbox/open-and-save-as-dialog-boxes)、[GetSaveFileNameW](https://learn.microsoft.com/en-us/windows/win32/api/commdlg/nf-commdlg-getsavefilenamew)、[OFNHookProc](https://learn.microsoft.com/en-us/windows/win32/api/commdlg/nc-commdlg-lpofnhookproc)。官方说明 Explorer hook 使用子对话框，并区分取消和原生错误；本计划中的线程与退出要求是结合当前 Unity 生命周期作出的实施判断。

## 实际 Player 最低矩阵

每个 case 使用独立 runtime/history/evidence 和尚不存在的导出目标。仅 root 操作真实 UI；QA 工具可布置显式 fixture 阶段、记录和分析，不代点原生窗口。

| 阶段 | 文件选择结果 | 导出前状态与终态要求 |
| --- | --- | --- |
| 已接受、正文未回的生成中 | 取消 / 保存，各一轮 | export 命令前 Thinking 且 turn 已知；导出先停止，记录 Interrupted；无迟到正文/声音恢复 |
| 实际播放且已观测非零输出 | 取消 / 保存，各一轮 | export 命令前 Speaking；先静音闭嘴并保存 Interrupted，played/total 合法；保存结果不能还是 Generated |
| 空闲且已完整播放历史存在 | 取消 / 保存，各一轮 | Ready；既有 Played 及 `played=total>0` 保持不变，不制造新的 Interrupted |

每次让原生窗口保持至少 5 秒，记录精确打开/关闭窗口和命令时间；用该窗口覆盖的原始逐帧数据证明主循环持续更新且没有连续 1 秒阻塞，用画面证明角色仍更新。独立观察停止、音量、主窗口可操作性；不要用后台线程自身的定时记录冒充 Unity 帧。

再覆盖以下相关竞态：重复点击导出只出现一个原生窗口；主窗口主动取消文件窗口；覆盖确认打开时取消/正常关窗；导出等待期间正常关窗；保存成功后重启恢复；失焦继续播放开/关；写入故障不生成旧快照导出；取消和保存后下一轮对话仍可用。旧 operation/turn 的音频、字幕不能复活。

每个 case 保留阶段证据、窗口停留区间、Player/launcher 退出结果、frame/event CSV、原始历史/导出 JSON、截图、文件哈希。取消 case 保留导出目标不存在的检查；保存 case 对照 conversation ID、store revision、逐消息状态和采样数。阶段不满足或窗口未打开的运行记为无效，不计入六个完成样本。

## R2 冻结后新构建要求

等待 root 提交最终产品 SHA 后才进入 R2。root 从该 SHA 创建全新 worktree，首次导入前记录绝对路径、HEAD、干净状态、`Library` 不存在；按 README 恢复冻结外部资源并记录来源/哈希。不得从旧工作区复制 Library。Unity 导入、检查、构建由 root 唯一执行并保存日志。

preview_backend 在冻结/产物通知后独立核对：新路径和创建前证据、最终源码 SHA、冻结资源恢复、首次导入/构建日志、源码未漂移、包内 source_dirty=false、Player 来源、ZIP 全体 CRC/路径/长度/SHA-256、许可证与运行配置/缓存排除、launcher 冻结指纹。再对同一包的 R1 关键实测及测试后包完整性复核，不用旧 b94af81 包结果代替。

R3 实际 Windows 125%/150% 缩放矩阵和 R4 同次画面/系统声音演示由相应 owner 处理；本任务不自动改变 DPI，不提前宣告完整 gate 或第二机器完成。

## 工具交接与独立复核状态

`tools/unity/qa/run_export_qa.py` 每次只创建一个不存在的证据目录，先核验包的完整文件清单、SHA-256、干净源码标志与指定产品 SHA，再使用该包的 Python 和正常 launcher。生成阶段唯一差异是显式启用随包已有的 `slow_generation` fixture；不改变三秒等待长度。`--delay -1` 等待 root 在 History 页按 F9，之后真实阶段出现时点击现有导出按钮。工具不会点击 UI、自动改变 DPI 或重用既有运行目录。

root 在最终包就绪后可按以下形式运行。所有占位值必须换成该次冻结候选和新目录，运行期间由 root 唯一操作桌面；`--prepare-only` 仅用于检查计划，也会占用该证据目录，实际运行必须另建 case。

```powershell
& '<最终包>/python/python.exe' -B tools/unity/qa/run_export_qa.py --package '<最终包>' --evidence '<新证据目录>' --phase generation --outcome cancel --seconds 240 --delay -1 --expected-source-sha '<最终产品40位SHA>'
& .venv/Scripts/python.exe -B tools/unity/qa/analyze_export_qa.py --evidence '<同次证据目录>' --output '<同次证据目录>/independent-analysis.json'
```

分析器读取固定 `export-events.csv`，使用 Player 声明的 Stopwatch 频率处理原生阶段原始时间戳，以实际点击时的 TriggerPhase / TriggerOperation 验证阶段。它检查先停后开窗、至少五秒原生选择等待、完整逐帧数据与不足一秒的最大间隔、正确 Played / Interrupted 终态、保存 JSON 与最终磁盘会话一致、旧 operation 不再次开始播放，以及 launcher 清理和包未变化。当前结果名 `automated_checks_passed` 只表示这些记录检查通过，仍要求人工角色/控件/焦点观察；实际阶段错过记为 `invalid_attempt` 并保留，不能算入六个样本。

八项离线 Python 检查通过，Ruff 检查通过：只使用明确标记的不可执行假包与合成 CSV，不启动后台或 Player。其中保留了对 17,728 ms 长帧及导出 Generated / 磁盘 Interrupted 不一致的拒绝检查。独立静态审阅最终 STA、原生句柄/缓冲、single-flight、临时文件提交、UI 导出 finally，以及 Composition 共享两秒异步退出等待，未发现新增具体阻断；覆盖确认二级窗口关闭、owner=0 焦点和真实主窗口操作仍留给实际运行验证。

### 生成阶段人工操作的显式延迟注入

最终 3110a56 首次生成取消尝试中，F9 到后续点击的工具往返约 4.49 秒，超过原有三秒生成窗口。该次 `export-generation-cancel` 保留为 `invalid_attempt`，不是产品失败，也不计入成功矩阵。root 随后明确授权仅在 QA 后台启动参数中注入固定 fixture 延迟；完整便携包不变。

冻结 API 没有 `create_app(provider=...)` 参数。实际可用接口是 `PreviewSettings.fixture_delay` 与 `create_preview_app(settings)`：薄封装 [run-generation-export.py](run-generation-export.py) 从 launcher 的私有配置读取设置，用 `dataclasses.replace` 指定既有 `slow_generation` 场景及 `fixture_delay=8.0`，再在 127.0.0.1:8000 起单个 worker。现有三秒场景等待保持不变，随后 FixtureProvider 等八秒，因此 accepted 到正文在未取消时约十一秒。该设置同时影响 FixtureSpeechSynthesizer，若未取消而进入 TTS，也会等待八秒；case.json 中完整标注两处延迟，不把它描述为默认产品速度。

封装仍使用原 `run_export_qa.prepare_case`、便携包自带 Python / launcher、原 `analyze_export_qa.py`，不操作 UI、不伪造阶段事件、不放宽主线程阻塞/停止/终态门槛。真实点击仍须是 accepted Thinking；即使错过注入后的窗口也只能判为无效尝试。每次创建新目录，保留之前的三秒尝试。以包内 Python 运行该报告工具，参数与上例相同，但无需 `--phase`，使用 `--outcome cancel` 或 `save`；`--delay -1` 仍通过人工 F9 准备。

三项额外离线检查通过：真实冻结 API 构造后延迟与超时值正确、accepted 先于生成并可取消且无迟到正文/音频；cloud 模式在启动前被拒绝；非可执行假包的 case 元数据与重复目录保护。测试替换 `uvicorn.run` 为内存观察器，没有启动 socket、服务或 Player。这三项不属于此前 95 项计数，不能回写为冻结前已跑的测试。

### 3110 退回后的 TaskDialog 审查与最小重测

3110 的真实覆盖确认取消失败已在 [qa-package.md](qa-package.md) 保留；首次改成 STA 单步取消的 dev02 仍未自动退出，root 手动点击 No 后才继续 Cancelled / Finished。这说明不能把纯窗口策略 fake 通过当成原生消息循环验证。最新修复改为在当前 STA 枚举非 child 窗口，以可见 `#32770` 且 `GW_OWNER` 等于当前层定位 owned 提示，再对实际 Windows 11 DirectUI 覆盖确认使用 `TDM_CLICK_BUTTON / IDNO`。独立只读审查未见 owner 范围越界、跨线程窗口查询回归或 Session 耐久写入回归；真实结果仍待 root 的 dev03 和最终包确认。

官方 `GetLastActivePopup` 可能返回传入窗口自身，包括传入窗口本身也由其他窗口拥有的情形，不能把它作为任意深度 owner 链枚举。`EnumThreadWindows` 枚举指定线程非 child 窗口，配合 `GW_OWNER` 可明确限定来源；`TDM_CLICK_BUTTON` 的参数是按钮 ID、lParam 为零，但 native callback 可以拒绝关闭，因此 Post 成功不是完成依据。[GetLastActivePopup](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getlastactivepopup)、[EnumThreadWindows](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enumthreadwindows)、[TDM_CLICK_BUTTON](https://learn.microsoft.com/en-us/windows/win32/controls/tdm-click-button)

一个需实测的具体边界已交 root/UI owner：`#32770 + DirectUIHWND` 不证明提示一定存在 No 按钮。当前遇无 legacy No/Cancel 的此类提示会投递 IDNO，并对相同 HWND 只发一次；只有 OK/Cancel 的路径错误提示可能不接受该命令。先用真实无效目录/路径提示取消验证，不因假设提前扩大产品改动。

对照首轮审查，后续最小交付范围为：

1. dev03 先复测覆盖提示 Escape、覆盖提示仍在时正常 Alt+F4、路径错误提示取消。以 native_return / Cancelled / Finished、正常进程退出、无 CLOSE_TIMEOUT、保护目标内容不变判定，不以窗口消失判定。
2. 修复确定后新 SHA → 全新无 Library worktree → 冻结资源恢复 → Build / Check → 新便携包完整性。3110 的五个成功 case 与来源链不转记到新包。
3. 在新包完成生成/播放/空闲 × 保存/取消六格；生成延迟注入保持明确记录。原生窗至少五秒，主循环/角色继续，先停止闭嘴，历史/导出状态正确且无旧输出。组合复测嵌套取消/退出、取消后下一轮和重启恢复。
4. 同包重跑相关 10 次已接受生成中取消、10 次已收到音频响应头的取消，以及播放中正常关闭并恢复终态。
5. 同包实际 Windows 125% / 150% × 1280×800 / 960×640 四格，分别保留真实中文 IME 确认、Shift+Enter、键盘停止/音量、历史与必要控件可见性。
6. 同包同次连续画面和指定 Player 系统音频，展示有声/静音片段、音量变化、静音、停止闭嘴，保留振幅/QPC/编码验证和文件哈希，运行后再核对包未变。

首轮已接受的 F01 十分钟动作节点上界和停止回环 P95，不因仅 UI 修复机械重跑；若最终改动影响这些路径再针对性追加。原生退出内存标记限制、遮罩排除和 F04 第二机器外部待办仍如实保留；不提前进入云端、录音、音色克隆等后置工作。最终完整 gate 仍由 A0 判定。
