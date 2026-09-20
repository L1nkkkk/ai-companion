# 缺陷与复测记录

| 项目 | 复现与修复 | 验证 / 状态 |
|---|---|---|
| TMP 字体首次启动失败 | CreateAsset 先保存了尚未注册的 Material/Atlas null 引用；准备工具改为注册子资源后重绑并保存 | dev03 起实际 Player 可正常显示中文；失败 smoke01 保留 |
| Windows 历史原子替换 | Mono File.Replace 首次落盘报 IOException；改 Windows MoveFileExW(REPLACE_EXISTING\|WRITE_THROUGH)，失败保留旧库 | History 32 条含被占用目标失败，真实恢复/导出/删除通过 |
| 角色过小及背景横带 | 首轮镜头使用了隐藏特效全包络；改可见主体包络、独立全屏背景和 UI 分区 | dev04 起 1280×800、dev05/06 960×640 目视通过 |
| Shift+Enter 误发送 | 用帧级 Input.GetKey 判断修饰键会漏快速按键；改 Unity Event.modifiers | dev06 快速 Shift+Enter 只加一换行；中文候选确认不发送，独立 Return 发一轮 |
| 重复换行与无 caret | Unity 另投递 Return 字符事件；运行时 InputField 在赋 textComponent 前就启用 | 过滤重复字符事件，inactive 完成接线后启用；dev06 计数5→6，第二行和 caret 可见 |
| 原生导出打不开 | Mono 对嵌套 StringBuilder 的封送失败 | 改纯 blittable OPENFILENAME + Unicode buffer；dev05 导出合法1962字节 JSON |
| 正常关窗写队列 | 主线程退出前未等待末次 Interrupted 写入 | wantsToQuit 首次静音并保留消息循环，await drain（上限2秒），再 Dispose/Quit；新增挂起写队列检查 |
| 启动器异常退出遗留进程 | 只依赖 finally 无法处理启动器被强制结束 | Windows Job 在 CreateProcessW 创建时原子关联且句柄不继承，关闭句柄清理本次子树；真实强制终止两种时序通过，无关进程保留 |
| Cubism 动作节点累积 | R4_1 连续播放断开旧节点但不销毁；64 次从2增至130 | Avatar 使用官方禁用/启用生命周期在招呼前重建图；64 次对照最多6节点，旧图均释放；最终 Player 十分钟见 qa-memory.md |
| 回环测量的计时原点 | Unity Mono Stopwatch 与 Win32 QPC 原点不同，不能直接比较 | 增加每次停止原始 QPC。首轮只保留捕获证据，不给错误 P95 |
| PCM16 回环的静音抖动 | Windows float→PCM16 的 ±1 LSB 转换抖动不能当作尾音 | 回环改 float32，时间戳错误/缺覆盖剔除，不将无效样本记为0 |
| 原始批次 CSV 关联限制 | dev04 尚未播放时 Mark 从 Player 取不到 request_id | 阶段 runner 校验与历史交叉核对；新 Composition 先取 Session.Operation，旧报告保留限制 |

U01-F01 的测量与边界见最终内存报告；F02 对五个未验特效路径明确禁用，没有宣称所有遮罩通过；F03 只包含本次公开 idle/greeting 动作；F04 第二机器复现仍待外部设备。OS DPI 矩阵和同步视听视频未完成不写通过。

最终退出的 Unity 原生日志仍有 `MemoryLeaks` / `phase=Immediate` 标记，`allocatedMemory=65812` bytes，位于运行期探针结束之后；未归因到某个产品模块，也未删除这条证据。原始日志见 qa-package.md。U01-06/A0 审阅时应区分该退出期引擎标记、运行期三种内存曲线和已修复的重复动作节点累积，本轮不宣称零泄漏。

磁盘不可写或写队列超过关窗上限时程序记录脱敏警告并退出，最后一次记录可能只恢复到此前已落盘状态。任务管理器直接结束 Player 属异常中断，恢复逻辑把未完成记录标为中断，不能承诺未落盘内容不丢失。没有保存原始录音。

责任与修复提交：UI/History 为 U01-02，Transport/Session/Audio 为 U01-04，后台和便携启动器为 U01-03，Avatar/Composition/构建为 U01-01/05；目录登记见 WORK-ORDER.md。首批修复合于 `40be263`；归档命名 `91c3ff2`，关窗落盘 `d213a85`，原子 Job 生命周期 `e3fd507`，动作图生命周期及最终采样 `b94af81`。所有当前发布的产品修复已包含在 b94af81 ZIP，较早候选的专门证据只用于未变模块的回归依据。

## 交给独立验收的事项

| 事项 | Owner 与复现 / 验收条件 |
|---|---|
| UA03 同步视听 | U01-06：用本候选完整包，记录有声/静音片段、音量变化、静音和停止；视频包含同一 Player 的系统输出声音，并与采样记录对照 |
| UA04 系统 DPI | U01-06：实际 OS 125% 和 150% 缩放下，各检查 1280×800 / 960×640、IME 候选确认、Shift+Enter、键盘停止/音量和历史控件；不能只改窗口尺寸 |
| F01 证据结论 | A0：审阅最终发布动作的节点上界及分段内存；原 Foundation 全动作、加载/释放阶段和截图因果仍不能由本轮有限 workload 推广 |
| F02 / F03 范围 | A0：确认仅发布 neutral/idle/greeting，五个未验特效已排除；按该能力集审阅取景，新增动作须重新 QA |
| F04 第二机器 | U01-06 / 第二机器 owner：在另一台登记硬件和音频设备的 Windows 上验证同一 ZIP 哈希并复现启动、中文、实际声音、停止、历史和退出；源码复现按 Unity README 固定版本与资源执行 |
| UA12 / gate | U01-07 / A0：核对候选 SHA、包清单、当前与前候选证据适用范围，逐项给出结论；本开发会话不改写设计验收台账 |

上述事项保留原标准和数量。本轮没有自动启动新的开发或设计会话。
