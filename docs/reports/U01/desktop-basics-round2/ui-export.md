# U01-DB-R1：异步原生历史导出

本轮针对第一轮验收的原生保存窗口阻塞：原实现从 Unity UI continuation 同步调用 `GetSaveFileName`，导致实际 Player 出现 17.7 秒 Update 间隔。本次把文件选择与文件写入移到独立后台 STA 线程，进入导出前先停止回复并等待历史终态。本文记录实现和模块检查；最终源码、真实 Windows 对话窗口与阶段矩阵由同目录集成证据登记，不把模块检查当作原生操作已通过。

## 用户行为

点击任一会话的“导出”会先同步停止当前生成/播放，再等待 Session 的终态写队列并取得所选会话快照。写入失败时返回失败，不打开文件窗口、不导出旧的 Generating / Generated 磁盘状态；失败屏障由 Session owner 实现。取消导出不恢复之前的回复，过期音频也不补播。

历史遮罩收起，状态提示回复已停止。原生窗口标题为“SAKI · 导出本机会话”，不使用 Unity HWND 作为模态 owner，因此可以切回 Player。角色更新、音量和停止继续可用；发送位置显示“取消导出”，停止 / Esc 也能取消导出。导出完成前不发新消息，不切换/新建/删除会话，历史和设置入口提示先完成或取消；草稿编辑保留。原生窗口自身的 Esc / 取消按 Windows 标准关闭文件选择。

用户保存时保留原生窗口确认的完整路径，不再追加扩展名。数据先在所选目录写临时文件并 flush，再通过同卷 `MoveFileExW(REPLACE_EXISTING | WRITE_THROUGH)` 原子替换；目标被占用或写入失败时保留原文件。最后提交前检查取消，提交成功后到达的取消不撤销已成功保存的文件。

## 线程与退出

`HistoryFileExport.SaveAsync` 使用一个专用 `IsBackground=true` / STA 线程和异步 continuation 的 TaskCompletionSource。全进程 single-flight 只在原生窗口及相关资源清理结束后释放，收到取消请求不会提前允许第二个窗口。任何 Unity API、UI 更新和观察事件都留在主线程；线程等待不使用主线程 Join、同步 Task.Wait 或 SendMessage。

Win32 `OPENFILENAME` 沿用已经确认的 IntPtr / Unicode buffer ABI，增加 Explorer hook。hook 的 WM_INITDIALOG 接收到子窗口，保存顶窗由 GetParent 获得；委托、缓冲区和取消注册保留到原生调用返回。取消发生在创建前、创建中或选择后均被检查。取消回调及每 100 ms 定时检查只向 hook 子窗投递去重后的私有消息，不查询 HWND、不持有跨线程锁；hook 在原生 STA 内按当前线程、`GW_OWNER` 逐层枚举可见的 `#32770` 提示窗，定位最深的可见、启用弹窗，不再依赖焦点相关的 `GetLastActivePopup`。每个窗口只选择一个消息：传统按钮优先 No，其次 Cancel；包含 `DirectUIHWND` 且没有传统按钮句柄的提示采用 `TDM_CLICK_BUTTON / IDNO`，绝不发 Yes / OK。其他传统对话提示才尝试 Close，不支持的窗口类型不自动确认。不会在同次处理继续关闭父窗；只有覆盖/错误提示退出且保存父窗重新启用后，才向保存窗投递 `WM_COMMAND / IDCANCEL`。WM_DESTROY 和 finally 清除句柄，再释放资源。真实覆盖提示下取消与退出必须由 Player 实测确认，策略检查不替代这一点。[Microsoft OPENFILENAME](https://learn.microsoft.com/en-us/windows/win32/api/commdlg/ns-commdlg-openfilenamew)、[OFNHookProc 取消协议](https://learn.microsoft.com/en-us/windows/win32/api/commdlg/nc-commdlg-lpofnhookproc)、[TaskDialog 点击协议](https://learn.microsoft.com/en-us/windows/win32/controls/tdm-click-button)、[EnumThreadWindows](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enumthreadwindows)。

Composition 在首次 wantsToQuit 开始时调用 `DesktopChatView.CancelPendingExport()`，再异步等待历史排空和 `WaitForPendingExportAsync()`，共享两秒上限；UI 的 OnApplicationQuit / OnDestroy 另作兜底。取消方法不阻塞主线程，完成 Task 代表整个导出 finally 和 STA / 原生 / 文件资源清理已经结束，始终正常完成，不用于推断是否保存成功；没有待导出时已完成。`IsBackground` 本身不能证明 Unity / Mono 能在原生调用未返回时退出，下面保留的真实失败已经否定这一假设；必须验证 GetSaveFileName 返回与进程正常退出。强制结束进程或存储操作无法在退出期限内结束时，已批准导出的同目录临时文件可能尚未来得及清理，原目标仍受原子替换保护；这不改变本机历史文件。

## 候选的嵌套取消失败与修正

候选 `3110a56f30671161ddfb9e39998c9972d4f3a34a` 的真实失败保留于外部证据 `desktop-r2-final/export-generation-cancel-delayed`：实际 Thinking 阶段点击导出，原生保存窗选择已存在的 `overwrite-guard.json` 并打开覆盖确认，切回 Player 按 Esc。两层窗口消失且目标 SHA-256 保持 `853e008d6daeda9d5f3a6925da05e44c5add808de8fe20028d6db1ab4be3103a`，但导出事件停在 DialogOpen、100 秒后仍无 Cancelled / Finished；随后 Alt+F4 出现两秒 `DESKTOP_CLOSE_TIMEOUT`，进程未正常退出。本次使用报告工具显式加长 generation 等待窗口，实际触发阶段仍以事件中的 Thinking 为准；该记录不是默认三秒 fixture 的通过记录，也不把后来强制结束进程算作成功退出。

旧逻辑在一次处理内向覆盖提示发送 No / Close，并同时关闭保存父窗；跨线程窗口查询还与原生销毁回调共用锁。这两种行为都已移除。随后开发包 dev02 的证据 `desktop-r2-dev02-nested` 仍发现取消未完成：空闲导出后打开覆盖提示，在 Player 按 Esc 和“取消导出”，提示窗依然存在、保存父窗处于禁用状态。人工点击“否”后无需其他输入，导出自动在 152.611 秒产生 Cancelled / Finished，之后 Alt+F4 正常退出 0；这证明 STA 私有消息与保存父窗取消链路可工作，问题仍在识别和关闭原生覆盖提示，不是可以用延长等待解决的问题。

Windows 11 的实际覆盖提示使用 DirectUI TaskDialog，按钮是虚拟 CommandButton_6 / CommandButton_7，而非可以直接 GetDlgItem(7) 的传统按钮。`GetLastActivePopup` 在切回 Player 后还可能返回禁用的保存父窗本身。第二次修正采用当前 STA 的直接 owner 链枚举与 TaskDialog 的 IDNO 消息，保留一层一个命令，等待原生循环真实返回再释放资源。下述 dev03 真实预检已验证修正，后续仍需冻结新源码并从全新 worktree 构建正式包，不能将开发包结果代替最终包矩阵。

## dev03 真实窗口预检

集成 owner 操作真实 Windows Player，证据位于 `C:/Users/Link/Dev/ai-companion-dev-tools/evidence/desktop-r2-dev03-nested`。开发包为 `builds/desktop-r2-dev03`，来自 `3110a56f30671161ddfb9e39998c9972d4f3a34a` 加未提交修正，**不是最终冻结源码或正式包**。三次均从实际 Ready 快照点击导出；前两次取消后成功再次打开导出窗口，验证资源释放和 single-flight 恢复。

| 实际路径 | 结果 |
| --- | --- |
| 已存在目标的覆盖确认保持打开，切回 Player 按 Escape | Cancelled / Finished 于 61.717 秒出现；原生调用返回 false、错误码 0。文件窗口打开约 36.82 秒期间主循环继续更新。 |
| 指向不存在目录，出现仅“确定”的 DirectUI 路径错误提示，再切回 Player 按 Escape | Cancelled / Finished 于 105.455 秒出现；原生调用返回 false、错误码 0。此平台该提示接受安全 IDNO 消息，未出现悬挂。 |
| 再次打开覆盖确认且保持未决，在 Player 按 Alt+F4 | 原生窗口 closed 且调用返回 false、错误码 0；集成 owner 确认 launcher 会话 67006 正常退出 0、无残留 Player、无 CLOSE_TIMEOUT。退出前 evidence listener 已卸载，尾部 CSV 无 Finished，不伪造该事件。 |

三次原生日志均依序出现 owned DirectUI 提示的 `1126 / 7`（TDM_CLICK_BUTTON / IDNO），然后保存父窗的 `273 / 2`（WM_COMMAND / IDCANCEL），再 closed / native_return。取消请求至原生返回分别约 119 / 100 / 68 ms，仅说明这三次窗口清理耗时，不当作通用延迟承诺。测试用 `overwrite-guard.json` 的内容保持 `{"qa":"do-not-replace"}`，实测 SHA-256 为 `8b60a6a25c500f67c11604aa032e7612cffeccc83f86992dd96027b990ff195d`（见 [预检清单](dev03-precheck.json)）。完整外部日志含环境信息，不复制进仓库；清单保存文件路径、校验值、边界与结果来源。

此次 Player 汇总为 8,968 帧、P95 16.9 ms、最大 22.71 ms、0 个超过一秒的帧、0 错误/警告，实际窗口 1280×800、DPI 120 / 125%。这是三个空闲导出路径的开发预检，不覆盖最终生成/播放导出矩阵，也不替代 R3 的最终包 DPI 矩阵。

## 观察接口

UI 公开 `IsExporting`、`CancelPendingExport()`、`WaitForPendingExportAsync()` 和主线程事件 `Action<HistoryExportProgress> ExportProgressChanged`。记录只包含阶段、ConversationId、点击导出时的 TriggerPhase / TriggerOperation 及 Stopwatch 单调时钟 TimestampTicks，不包含聊天正文、导出路径或凭据。

阶段为 Stopping → Stopped → Capturing → DialogOpen → Saving → Saved / Cancelled / Failed → Finished。取消或失败可以跳过之后的阶段。DialogOpen / Saving 从工作线程排入固定阶段队列，主线程派发时保留原始时间戳；可据此关联文件窗口等待区间与原始帧记录。触发阶段以真实点击时快照为准，不能用测试入口的预设阶段代替。

仅显式传入 `-evidenceDirectory` 的 QA 运行增加 `HISTORY_EXPORT_NATIVE` 诊断，每次导出最多 64 条，经线程安全队列在 Unity 主线程写日志。内容限单调时钟、线程 ID、数值窗口句柄、固定状态、消息编号与结果；不读取或写入窗口标题、聊天正文、文件路径或凭据。可区分 cancel_request → wake_post → wake_received → target → cancel_post → native_return / closed。诊断观察者失败不会穿过 native callback。

## 模块验证与待集成检查

固定 Unity 2022.3.62f3c1 的 Roslyn、.NET Standard 2.1、冻结 TMP/uGUI 与 Contracts 引用独立编译修正后的 UI 和 UI Checks：0 错误。固定 Unity Mono 独立执行导出相关 **35 项断言通过**：原有 17 项覆盖后台 STA、调用方不被等待任务阻塞、single-flight、取消清理前不解锁、异常传播与恢复、预取消不启动、UTF-8 保存/替换、真实 Windows 文件锁下不破坏目标、取消保护目标、临时文件清理；新增 18 项通过受控窗口接口验证跨线程仅投递去重消息、所有窗口查询在原线程、最深弹窗单一 No、下一轮 Cancel、禁用父窗不被关闭、保存窗只发 IDCANCEL、无重复关闭、无按钮时单一 Close、消息投递失败可重试、销毁后的句柄失效，以及 DirectUI TaskDialog 无传统按钮句柄时仅发送一次 IDNO，待其退出后才取消父窗。受控窗口接口不调用原生保存窗，不是嵌套原生消息循环或窗口发现的实测替代。

正式 `UiChecks.Run` 保留原有 17 项键盘策略/Unicode/ABI 断言。集成 owner 先前 Check01 的 History **32**、UI **34**、Session/Audio **53** 通过对应旧候选，不能覆盖后来发现的真实嵌套取消失败。新修正正式 UI 检查预计为 **52** 项，待统一 Editor 检查确认。真实检查需要：生成 / 播放 / 空闲各覆盖保存与取消；窗口保持打开时帧/角色持续、切回 Player 操作音量与停止、重复导出拒绝、无旧结果复活；覆盖确认提示打开时取消与正常关闭；终态 JSON 正确；导出后继续中文输入与正常关闭。

模块检查曾发现冻结 Mono 未实现 `Marshal.GetHRForLastWin32Error`；已使用支持的 GetLastWin32Error 转换数值 HResult。日志只输出异常类别和数值，不记录正文、选择路径或含这些内容的异常消息。
