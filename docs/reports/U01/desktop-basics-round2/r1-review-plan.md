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
