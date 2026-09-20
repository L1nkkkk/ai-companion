# U01-02 桌面界面与本机历史

本次依据 `DESKTOP-PRIORITIES.md` 的桌面基础功能交付。实现沿用冻结 `Companion.Contracts`，没有改变正式 R1 或预览网络契约。当前回复与音频由 Session 提供真实模式；界面持续显示“演示模式 / 固定回复 / 测试音频”。麦克风入口明确尚未开放。

## 实现与接线

- `AICompanion.Preview.UI.DesktopChatView.Initialize(ISessionController, TMP_FontAsset, string character = "mao", Action<HistoryExport> onExport = null)`：运行时创建独立 Canvas；UI 仅依赖 Contracts、uGUI、TMP。Composition 传入动态中文字体。角色相机应位于窗口左侧 42%，避开底部 184 个参考像素的角色说明与音量控件；参考分辨率 1280×800。
- 界面包括中文多行草稿、发送、生成/声音状态、生成/显示/已播完/中断/失败标识、停止/Esc、新会话、音量/静音、自动朗读、失焦播放策略、音色选择及后台状态刷新。Tab / Shift+Tab 切换控件；音量支持方向键，按钮使用 uGUI 标准键盘确认。
- `ChatInputField` 使用 TMP 3.0.6 的原生 Event 队列和编辑方法，按事件自身的 modifiers 区分 Enter / Shift+Enter，避免同一帧内修饰键按下/释放后误判。候选期间和候选提交后的两个帧内不把 Enter 解释为发送；物理 Return 与其额外字符事件去重，粘贴换行保留。只监听一个发送入口，发送后立即清空草稿，并拒绝同一帧重复发送。Unicode 按标量计数，拒绝无效代理项；服务端仍负最终校验责任。所有用户文字 `richText=false`。
- 动态创建输入框时在 inactive 状态先接好 textComponent / viewport 再启用，确保 TMP.OnEnable 建立 caret 与滚动路径；每个编辑事件刷新文字几何，使同一批事件中的删除、选区和方向键使用当前字符索引。
- 本机历史列表有滚动、打开、导出、单会话删除、全部清除；删除有界面内确认。导出由 UI 的 `HistoryFileExport` 打开 Windows 另存为对话框，文件名从 UUID 构造，不信任 DTO 路径；JSON 最大 2 MiB。导出失败或用户取消不会修改原历史。已导出文件由用户自行管理。
- 界面只在 Session 快照变化时更新，消息行复用且仅更新变化的内容；不在每帧扫描聊天、访问网络或写盘。每帧仅处理必要键盘入口。

`AICompanion.Preview.History.JsonHistoryStore(string directory)` 实现冻结的 `IHistoryStore`；独立 `Companion.History` 程序集防止 UI/Session 环依赖。调用方先经 List/Create/Load 完成惰性初始化；`CapacityChanged` 可来自存储工作线程，必须由 Session 转发到主线程。Composition 可读取初始化后的 `RecoveryMessage`，通过 `DesktopChatView.ShowNotice(message, true)` 呈现损坏/上次未完成回复的恢复结果。

## 存储语义

存储为 `conversations.v1.json`，设置由 Session 保存于另一文件。最多 20 个会话，每会话 80 条记录；满额淘汰最旧非当前会话，单会话按 operation 整组淘汰。版本、存储代数、索引修订、操作/轮次身份、Unicode 正文、声音采样进度与失败信息均有校验。不包含音频、录音、配置令牌或服务商凭据字段。

文件写入在有界串行执行器中完成（最多 64 个待执行操作）：写临时文件、flush 到磁盘、同卷原子替换。Windows 使用 `MoveFileExW(REPLACE_EXISTING | WRITE_THROUGH)`；正式 Unity 检查发现目标 Mono 的 `File.Replace` 在重开历史后失败，因此改为该平台原生原子入口，禁止 Delete+Move。替换失败保留原文件和有效索引，初始化恢复失败仍允许下次重新读取。重启忽略并清理中断的临时文件。不保留会复活已删除记录的历史备份。坏 JSON / 不支持版本保留一份本机 `.corrupt` 隔离件并恢复为空；删除或清除会同时移除此隔离件。原始内容和路径不输出到日志。

只有 Create 能创建会话。每次新建取得单调增长的 StorageGeneration，Delete/Clear 后旧 token 无法 upsert；同 ID 显式重建也使用新代数。最终交付记录拒绝迟到状态回退。分页游标绑定 StoreRevision，变更后旧游标失败并要求刷新。重启将未完成 assistant 的 Generating/Generated 恢复为 Interrupted，不会补播或标为 Played。Played 仅接受实际完整样本计数；纯文字 Displayed、语音 Failed 与 Error 一致性分别校验。

## 验证

已用固定 Unity `2022.3.62f3c1` 内置 Roslyn、.NET Standard 2.1 和工程 TMP/uGUI 引用独立编译 UI、History 与自检源码，0 编译错误/警告。正式 Editor/Player 的整包验证由集成 owner 记录于本目录总报告。

无需新增 Test Framework 包，自检是 Editor-only 程序集中的 executeMethod：

- `AICompanion.Preview.Tests.HistoryChecks.Run`：真实磁盘与 Unity JSON；版本和导出、Unicode、生成/实际播放区别、真实文件锁下原子替换失败仍保留原文件及索引、重启中断、终态拒绝回退、删除/清除/同 ID 重建旧 token、20/80 容量与完整消息组、分页与游标过期、取消、原子写临时残留、坏 JSON 及隔离件清除。
- `AICompanion.Preview.Tests.UiChecks.Run`：Enter / Shift+Enter / IME 候选提交保护 / 原生键事件修饰符与自动重复去重 / Return 字符识别 / Unicode 标量与不合法代理项；Windows OPENFILENAME ABI 尺寸与真实 Unity Mono marshaling。

2026-09-21 正式 Unity Editor 集成执行 Check05 已通过：History **32 项断言**（包含真实文件锁失败保护），UI **17 项断言**，同批 Session / Audio **39 项**。日志为 `C:/Users/Link/Dev/ai-companion-dev-tools/builds/desktop-checks-05-evidence/Check.log`。该批包含事件修饰符、导出 ABI、重复换行与 caret 初始化修正；此前 Check04 的 32 / 15 / 39 结果保留为修复过程证据。后续 Check08 再次通过 History 32 / UI 17，Session / Audio 增至 41，日志为 `C:/Users/Link/Dev/ai-companion-dev-tools/builds/desktop-checks-08-evidence/Check.log`。这些结果不冒充第二机器验证，也不把策略检查等同于真实 Windows IME 验证。

## 已取得的 Windows 操作证据

集成 owner 在 `desktop-dev-05` 的真实 960×640 Player 中完成原生导出窗口选择路径并保存；`C:/Users/Link/Dev/ai-companion-dev-tools/evidence/desktop-manual-03/manual-export.json` 为 1962 bytes、合法 schema 1 JSON。先前导出瞬间失败已通过显式 unmanaged Unicode buffers / IntPtr OPENFILENAME 修复，日志仅记录异常类别、HResult 或原生错误码，不记录正文与目标路径。

同批操作确认重启恢复原有两条消息并保留“已播完”，界面删除确认后存储仅剩新建空会话且旧 ID 不再存在。960×640 下角色帽子、鞋子与必要控件未裁剪。另独立查看 `C:/Users/Link/Dev/ai-companion-dev-tools/evidence/desktop-manual-01/desktop-start.png`（1280×800），当前角色完整可见，背景无旧棕色条带，中文、演示提示与主要控件清晰。静态视检只覆盖截图可见状态。

实际键盘 QA 先后发现同帧 Shift 释放误发、重复换行和第二行不可见，已分别修正事件 modifiers、Return 字符事件去重与 TMP caret 初始化顺序。最新 `desktop-dev-06` 在真实 Windows 960×640 Player 中复测通过：

- 初始 5 字草稿按 Shift+Enter 后计数为 6，仅增加一个换行且不发送；继续输入第二行后计数为 9，第二行可见。
- 光标与 Microsoft Pinyin 候选窗口位于输入区域旁。真实按下 `n`、`i`、Return，仅将 `ni` 提交到草稿，没有误发；另一次 `n`、`i`、Space 提交单字“你”，随后一次 Return 恰发送一轮。
- Escape 停止当前操作，历史记录转为“中断”；音量 Slider 聚焦后 Left 操作使显示从 80% 变为 70%。

本批证据为 `C:/Users/Link/Dev/ai-companion-dev-tools/evidence/desktop-manual-04/player.log` 与同目录 `userdata/history/conversations.v1.json`；历史中保留实际中文“你”的一轮记录。这覆盖本次具体键盘与候选提交路径，不代表全部输入法、快捷键时序或长文本滚动组合均已验收。

使用与构建说明已更新于 [Unity README](../../../../apps/unity/README.md)：便携包双击 `Start-NeuroSaki.cmd`，源码使用 `Build-Desktop.ps1` / `Start-Desktop.ps1`，正式包路径与哈希由最终交付 manifest 指定。只发布 neutral / idle / TapBody[0] 招呼；本轮不发布未经视觉验收的 special 动作。F02 排除 drawable 的最终配置与运行证据由 Avatar / 集成报告记录。

## 仍需真实桌面验收

Windows 125% / 150% OS DPI 和大规模 IME 矩阵尚未测试；其他输入法、长按/快速连续操作、完整键盘导航、长文本滚动组合与设备切换仍需对应 Player 证据。已有 Microsoft Pinyin 具体路径、两种分辨率的可见布局和原生导出操作记录不能替代这些检查。异常终止文件恢复和持续窗口操作需按 UA04/UA07 继续独立 QA。本实现不宣告完整 UA04、UA07 或 U-G0/1/2 通过；不包含真实 ASR、云端 TTS、长期记忆。
