# U01-DB-R1：异步原生历史导出

本轮针对第一轮验收的原生保存窗口阻塞：原实现从 Unity UI continuation 同步调用 `GetSaveFileName`，导致实际 Player 出现 17.7 秒 Update 间隔。本次把文件选择与文件写入移到独立后台 STA 线程，进入导出前先停止回复并等待历史终态。本文记录实现和模块检查；最终源码、真实 Windows 对话窗口与阶段矩阵由同目录集成证据登记，不把模块检查当作原生操作已通过。

## 用户行为

点击任一会话的“导出”会先同步停止当前生成/播放，再等待 Session 的终态写队列并取得所选会话快照。写入失败时返回失败，不打开文件窗口、不导出旧的 Generating / Generated 磁盘状态；失败屏障由 Session owner 实现。取消导出不恢复之前的回复，过期音频也不补播。

历史遮罩收起，状态提示回复已停止。原生窗口标题为“SAKI · 导出本机会话”，不使用 Unity HWND 作为模态 owner，因此可以切回 Player。角色更新、音量和停止继续可用；发送位置显示“取消导出”，停止 / Esc 也能取消导出。导出完成前不发新消息，不切换/新建/删除会话，历史和设置入口提示先完成或取消；草稿编辑保留。原生窗口自身的 Esc / 取消按 Windows 标准关闭文件选择。

用户保存时保留原生窗口确认的完整路径，不再追加扩展名。数据先在所选目录写临时文件并 flush，再通过同卷 `MoveFileExW(REPLACE_EXISTING | WRITE_THROUGH)` 原子替换；目标被占用或写入失败时保留原文件。最后提交前检查取消，提交成功后到达的取消不撤销已成功保存的文件。

## 线程与退出

`HistoryFileExport.SaveAsync` 使用一个专用 `IsBackground=true` / STA 线程和异步 continuation 的 TaskCompletionSource。全进程 single-flight 只在原生窗口及相关资源清理结束后释放，收到取消请求不会提前允许第二个窗口。任何 Unity API、UI 更新和观察事件都留在主线程；线程等待不使用主线程 Join、同步 Task.Wait 或 SendMessage。

Win32 `OPENFILENAME` 沿用已经确认的 IntPtr / Unicode buffer ABI，增加 Explorer hook。hook 的 WM_INITDIALOG 接收到子窗口，保存顶窗由 GetParent 获得；委托、缓冲区和取消注册保留到原生调用返回。取消发生在创建前、创建中或选择后均被检查；创建后用 PostMessage 请求关闭，每 100 ms 重试嵌套覆盖/错误提示，覆盖提示只投递 No / Cancel，不自动确认覆盖。WM_DESTROY 和 finally 清除保存窗句柄，再释放资源。真实覆盖提示下取消与退出必须由 Player 实测确认，线程检查不替代这一点。[Microsoft OPENFILENAME](https://learn.microsoft.com/en-us/windows/win32/api/commdlg/ns-commdlg-openfilenamew)、[Explorer hook](https://learn.microsoft.com/en-us/windows/win32/dlgbox/open-and-save-as-dialog-boxes)、[PostMessage](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-postmessagew)。

Composition 在首次 wantsToQuit 开始时调用 `DesktopChatView.CancelPendingExport()`，再异步等待历史排空和 `WaitForPendingExportAsync()`，共享两秒上限；UI 的 OnApplicationQuit / OnDestroy 另作兜底。取消方法不阻塞主线程，完成 Task 代表整个导出 finally 和 STA / 原生 / 文件资源清理已经结束，始终正常完成，不用于推断是否保存成功；没有待导出时已完成。后台线程不会阻止进程退出。强制结束进程或存储操作无法在退出期限内结束时，已批准导出的同目录临时文件可能尚未来得及清理，原目标仍受原子替换保护；这不改变本机历史文件。

## 观察接口

UI 公开 `IsExporting`、`CancelPendingExport()`、`WaitForPendingExportAsync()` 和主线程事件 `Action<HistoryExportProgress> ExportProgressChanged`。记录只包含阶段、ConversationId、点击导出时的 TriggerPhase / TriggerOperation 及 Stopwatch 单调时钟 TimestampTicks，不包含聊天正文、导出路径或凭据。

阶段为 Stopping → Stopped → Capturing → DialogOpen → Saving → Saved / Cancelled / Failed → Finished。取消或失败可以跳过之后的阶段。DialogOpen / Saving 从工作线程排入固定阶段队列，主线程派发时保留原始时间戳；可据此关联文件窗口等待区间与原始帧记录。触发阶段以真实点击时快照为准，不能用测试入口的预设阶段代替。

## 模块验证与待集成检查

固定 Unity 2022.3.62f3c1 的 Roslyn、.NET Standard 2.1、冻结 TMP/uGUI 与 Contracts 引用独立编译 UI 和 UI Checks：0 错误。固定 Unity Mono 独立执行新增 **17 项断言通过**：后台 STA、调用方不被等待任务阻塞、single-flight、取消清理前不解锁、异常传播与恢复、预取消不启动、UTF-8 保存/替换、真实 Windows 文件锁下不破坏目标、取消保护目标、临时文件清理。

正式 `UiChecks.Run` 保留原有 17 项键盘策略/Unicode/ABI 断言。集成 owner 已运行本轮 Check01，History **32**、UI **34**、Session/Audio **53** 全部通过；新增退出完成 Task 后由最终统一检查继续确认。模块检查不打开原生文件窗口，不声称原生对话行为已验收。真实检查需要：生成 / 播放 / 空闲各覆盖保存与取消；窗口保持打开时帧/角色持续、切回 Player 操作音量与停止、重复导出拒绝、无旧结果复活；覆盖确认提示打开时取消与正常关闭；终态 JSON 正确；导出后继续中文输入与正常关闭。

模块检查曾发现冻结 Mono 未实现 `Marshal.GetHRForLastWin32Error`；已使用支持的 GetLastWin32Error 转换数值 HResult。日志只输出异常类别和数值，不记录正文、选择路径或含这些内容的异常消息。
