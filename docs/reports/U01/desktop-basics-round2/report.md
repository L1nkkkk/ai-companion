# 桌面基础功能第二轮交接

本轮处理 A0 [第一轮独立验收](C:/Users/Link/Dev/Neuro-Saki-U01-acceptance/docs/reports/U01/acceptance/desktop-basics-round1-review.md) 的 R1 产品缺陷与 R2–R4 证据缺口。产品冻结为 `924b79f05b6c90983bdc5c18344aa125375fa57c`。**R1–R4 开发与证据交付已完成，待 A0 独立复核。** R1 六格及必要取消/关闭/恢复记录、R2 全新构建及运行前后完整包核对、R3 四格实际 Windows 缩放与恢复、R4 同次同步 MP4 均已交齐。本报告不代替 A0 宣布 U-G0 通过，结构化状态见 [verification.json](verification.json)。

用户范围仍是可体验的 Windows 桌面基础功能：中文聊天界面、明确标记的固定演示文字与测试 WAV、Mao 实际呈现、停止、音量/静音、本机历史和原生导出。真实 LLM/TTS/ASR、麦克风、音色克隆、微调、长期记忆及手机/直播能力继续后置；演示模式不代替这些能力。

## 候选与证据归属

| 对象 | 归属与当前状态 |
| --- | --- |
| 本次冻结产品 | `924b79f05b6c90983bdc5c18344aa125375fa57c`；新目录构建、前后整包核对与本轮实际矩阵完成，待 A0 |
| 正式便携包 | [NeuroSaki-Desktop-924b79f.zip](C:/Users/Link/Dev/ai-companion-dev-tools/builds/NeuroSaki-Desktop-924b79f.zip)，70,330,552 字节；SHA-256 `b14ca55facd93c55997968ad8d5fe07f3d0950809145b006bcc90a6d2f5e8036` |
| 正式运行证据根 | `C:/Users/Link/Dev/ai-companion-dev-tools/evidence/desktop-r2-924b79f` |
| 全新来源证据根 | `C:/Users/Link/Dev/ai-companion-dev-tools/evidence/desktop-r2-fresh-924b79f` |
| 3110a56 | **已撤回**：真实生成中导出遇嵌套覆盖确认，取消任务不收尾、正常退出超时。原包完整性检查仍保留为该旧包的历史事实，不转记新包 |
| dev03 | dirty 3110a56 加修正的开发包，三条原生窗口预检通过；不是最终源码或正式包 |
| b94af81 | 第一轮验收对象；仅按 A0 已接受范围沿用 F01–F03 及既有停止回环结论 |

3110 的来源、包检查及退回原因见 [旧包核对](qa-package.md)、[退回记录](qa-package-disposition.json)。dev03 的实际预检和哈希见 [dev03-precheck.json](dev03-precheck.json)。旧证据保持原样，失败、无效阶段尝试和强制清理不标作成功。

## R1：原生文件选择与退出

文件选择和原子写入移到独立 STA。点击导出先停止当前生成/播放，等待历史终态成功写入，再固定所选会话快照；写盘失败不会提供旧快照。保存窗口不以 Unity 窗口为模态 owner，主循环继续运行，主窗口保留停止、取消导出和音量操作；导出期间暂停新消息和历史修改，取消不会恢复旧回复。

嵌套提示的取消在同一 STA 内沿直接 owner 链逐层处理。Windows 11 DirectUI 覆盖提示采用安全的“否”，保存父窗恢复启用后才取消它。single-flight 及退出等待以原生调用真正返回和清理完成为准，不以窗口消失或 PostMessage 成功为准。正常关窗先本机停止，再异步等待历史/导出清理，共享两秒上限。实现、失败定位、限定 QA 诊断和模块检查详见 [UI 导出报告](ui-export.md)、[独立审查计划](r1-review-plan.md)。

dev03 已实测覆盖确认 → Player Escape、仅“确定”的无效路径提示 → Player Escape、覆盖确认未决 → Player Alt+F4。原生调用均返回 false / error 0，前两项记录 Cancelled / Finished，第三项正常退出 0，无 CLOSE_TIMEOUT；目标文件未改变。该预检支持进入最终构建，不能代替下面六格。

| 最终 924b79f 实际触发阶段 | 取消文件选择 | 保存文件 | 必须核对 |
| --- | --- | --- | --- |
| 已接受生成中 Thinking | 记录检查及人工观察通过 | 记录检查及人工观察通过 | 实际 operation 存在；Interrupted，空正文、0 / 0 样本，无迟到正文或声音 |
| 已有非零输出的 Speaking | 记录检查及人工观察通过 | 记录检查及人工观察通过 | 两项均 Interrupted；分别 111104 / 192000、137216 / 192000 |
| 完整历史存在的 Ready | 记录检查及人工观察通过 | 记录检查及人工观察通过 | 两项保留 Played、192000 / 192000，不制造新中断 |

六格均保持原生窗口超过五秒，独立复核同次 Unity 帧、角色采样、历史/导出 JSON、原生终态与正常退出；阶段错过只记无效。六格原生等待区间约 10.77–42.26 秒，区间最大帧间隔均低于 28 ms；没有把 STA 定时器当作 Unity 帧。完整逐格数值和哈希见 [最终导出复核](r1-final-924b79f.md)及[结构化记录](r1-final-924b79f.json)。

播放两格为 `export-playback-save-02` 与 `export-playback-cancel`。集成 owner 通过 Sky 实际观察文件选择期间 idle 姿态跨快照改变，保存成功、取消后原回复持续 Interrupted，均由 Alt+F4 正常退出 0。取消格还验证主窗口音量 80%→鼠标 59%→实际 Left 键 49%，并用主窗口停止按钮取消导出；实际 F8 帧为该 case 的 `desktop-manual-01.png`。独立记录复核分别得到约 16.376 / 31.809 秒文件选择区间，区间最大帧间隔 23.859 / 27.972 ms。各 case 的 `manual-observations.json` 明确标为 root 观察、desktop_ui 按任务消息转录，不伪称转录者独立操作了界面。

空闲两格 `export-idle-save` / `export-idle-cancel` 在整轮 WAV 完成后开始，取消使用原生窗口 Cancel，保存产生同次 JSON；退出前始终已播完且无新回复。生成两格 `export-generation-save-02` / `export-generation-cancel` 真实点击均为 Thinking，正文仍未到达；后者还打开既有目标的覆盖确认，切回 Player 按 Escape 后两层窗口收尾，85.671 秒记录 Cancelled / Finished，保护目标哈希不变，随后新会话成功清空且 F8 留帧。生成 QA 明确使用既有 slow_generation 三秒加 fixture_delay 八秒，合计至少十一秒的正文等待，不是默认产品延迟；包文件和分析门槛不变。

首次 `export-playback-save` 因 screenshotId 失效错过八秒播放窗，实际 Ready 后取消；首次 `export-generation-save` 因操作者用量中断未执行请求/导出，自动 240 秒结束。两项均保留为无效尝试，不占用成功格，也不掩去原脚本未完成记录。环境按实际文件区分：无效播放尝试为 DPI 120 / 125%，六个有效格为 DPI 96 / 100%，均 1280×800；它们不算 R3 矩阵。

同一最终包另外完成已接受生成中取消 10 次、收到音频响应头后取消 10 次：两批实际播放启动均为 0，历史均 Interrupted，位置分别 0 / 0 和 0 / 192000。首次真实非零输出后的程序 `Application.Quit` 正常关闭专项保留 Interrupted、12800 / 192000，Player 退出 0。它们不是物理按键或 OS 尾音延迟测量。

覆盖确认未决时的 `export-idle-nested-quit` 由 240 秒 QA 截止触发 `Application.Quit`，通过产品正常退出链完成原生清理，native_return=false / error=0，无 CLOSE_TIMEOUT、launcher 退出 0，保护目标未改；人工 Alt+F4 到达时窗口已关闭，**不把它写成人工关窗触发**。原 export-stage 超时失败和缺少末尾 CSV 事件原样保留，因为记录器先收尾才请求退出；此项仅作为原生清理回归，不是第七个导出成功格。重启恢复已用 R3 的同包 125% / 960×640 实证，旧两条逐字段相同、46% 音量恢复且旧 operation 无重播。路径错误提示取消只保留 dev03 限定预检，不新增为 A0 未要求的最终门槛。

## R2：最终 SHA 的全新构建

新来源记录必须直接指向 924b79f：创建前路径不存在，检出后 HEAD 正确且干净、首次导入前 Library 不存在，按 README 恢复冻结资源，不复制旧 Library；保留恢复、首次导入、检查、构建日志和产物清单。独立核对 ZIP 全体成员与解压目录、Player/后台/运行时来源、许可证、清单外文件和运行前后包哈希。

实际新 worktree 为 `C:/Users/Link/Dev/Neuro-Saki-U01-round2-924b79f`，创建前不存在，最终 SHA 直接检出后及首次导入前 Library 均不存在，构建后 HEAD 正确且源码干净。固定资源恢复、首次 asset database 建立和实际构建日志已独立核对；Build 为 Succeeded，0 errors / 2 warnings。新包清单 3,014 文件、ZIP 3,015 成员，全量 CRC / 长度 / SHA-256 与源码来源核对无差异。这里是新包本身的完整读取，未套用 3110 数值。完整来源、许可证与哈希见 [924b79f 包核对](qa-package-924b79f.md)及[结构化核对](qa-package-924b79f.json)。

全部正式运行结束后，独立审阅者重新读取解压目录的 3,015 文件与 ZIP 的 3,015 成员，集合、CRC、长度和 SHA-256 与初始包完全一致，零差异；17 份运行结果均显示包未变、launcher 退出 0、运行配置无残留。17 次包括保留的无效尝试，并非 17 次功能验收全部通过。ZIP 仍为 70,330,552 字节，哈希与上表相同；新 worktree HEAD 正确且干净。详见 [运行后整包复核](qa-package-924b79f-postrun.md)及[结构化结果](qa-package-924b79f-postrun.json)。

解压完整目录后双击 [Start-NeuroSaki.cmd](C:/Users/Link/Dev/ai-companion-dev-tools/builds/NeuroSaki-Desktop-924b79f/Start-NeuroSaki.cmd)，不要只移动 EXE；随包 Python/后台可直接使用。通用源码恢复和启动说明见 [Unity README](../../../../apps/unity/README.md)。

## R3：真实 Windows 缩放矩阵

| 实际系统缩放 | 1280×800 | 960×640 |
| --- | --- | --- |
| 125% | 已记录：DPI 120 / scale 125，实际输入及操作完成 | 已记录：DPI 120 / scale 125，实际输入与恢复完成 |
| 150% | 已记录：DPI 144 / scale 150，实际输入及操作完成 | 已记录：DPI 144 / scale 150，实际输入及操作完成 |

第一格已在正式包完成：真实系统显示器 2 由 100% 改为 125%，1280×800 下物理 n/i 出现拼音候选，Space 确认不误发，Shift+Return 后计数 2，输入“第二行”后 `你\n第二行` 计数 5；一次 Enter 发送、Escape 中断，历史仅一轮，音量经鼠标与物理 Left 为 80%→56%→46%。角色及必要控件、历史六个操作均可见，正常 Alt+F4 退出 0。三张 Player F8 PNG 已核对；OS 候选窗只由 root 现场观察，不在背缓冲图片中。详见 [实际 DPI 报告](dpi-final.md)及[矩阵与哈希](dpi-final.json)。

125% / 960×640 也已完成，同样实测 DPI 120 / scale 125。重启复用上格 userdata 恢复两条 Interrupted 记录与音量 46%，Ready 无自动重播；再次输入 `你\n小窗口`、单次发送并 Esc 中断，音量 46%→51%→Left 41%，历史 4 条和必要控件全部可见，正常关闭 0。两格均保留独立不可变历史快照。

150% / 1280×800 已在 Settings 真正切换缩放后完成，Player 记录 DPI 144 / scale 150 / HRESULT 0。独立新历史同样通过物理拼音候选确认、Shift+Enter 与计数 5、一轮 Enter 发送和 Escape 中断；音量 80%→53%→Left 43%，历史/角色/必要控件可见，退出 0。

150% / 960×640 最后一格也已完成：实际 DPI 144 / scale 150 / HRESULT 0；物理拼音候选确认无误发，`你\n小窗口` 两行计数 5，Enter 一次发送、Escape 中断，历史一轮、180224 / 192000 样本。音量 80%→46%→Left 36%，角色和必要控件、六个历史操作均可见，Alt+F4 退出 0。四格都保留独立历史快照和按 root 实际观察转录的记录；13 张 Player F8 图片已只读核对，候选窗没有伪称出现在其中。

四格结束后 root 经 Windows Settings 将显示器 2 恢复至原来的 100%（1920×1080），显示器 1 未改。恢复声明、源码干净及无 Player/8000 监听/运行配置残留的核对见 [最终环境记录](final-environment.json)。因 Settings 含账号信息，不保存其截图。窗口尺寸或缩放后的图片不能代替 OS 缩放，dev03/R1/R4 运行也不混入本矩阵；本轮仍为同机验证。

## R4：同一 Player 的同步视听演示

最终包 PID 42280 的实际 Player 自身渲染帧与该进程系统输出已同步采集，连续展示固定 WAV 有声/静音段、80%→20%、静音、恢复和停止后闭嘴，同次 QPC、振幅、口型、音频包与帧记录完整。只采指定 Player，不采其他应用或麦克风。

已交付 [连续同步 MP4](C:/Users/Link/Dev/ai-companion-dev-tools/evidence/desktop-r2-924b79f/audiovisual/mux-01/synchronized-evidence.mp4)：56.9367742 秒、13,541,940 字节，SHA-256 `8affcd16db61b31d877e0cc9211ceb9dde796bfc49c51a3c93e1cb132dc77cec`。732 张实际帧进入共同覆盖区间，约 12.845 fps，最大保持 83.106 ms；不声称稳定 15 fps。视频编码 PTS 最大误差 0.0664 ms，在实际容器一 tick 内；AAC 声音边界最大偏移 0.0756 ms。mute 的 23 帧及 Stop 后三秒的 38 帧 mouth 全零；该次单个 Stop 回环尾音 130.0535 ms，不替代旧 20 次分布。

独立核对包括同次身份、原始时序、程序记录、编码解码及有声/素材静音/mute/Stop 四张实际 PNG；**没有声明分析者完整人工听过或看过视频**，连续文件交 A0 播放复核。完整结果及局限见 [最终音画交付](audio-video-final.md)、[同次结构化证据](audio-video-final.json)、[编码复核](audio-video-encoding-final.json)。观看用 H.264/AAC 是有损派生文件；原始 float 音频仍是尾音量化依据，保留输出缓冲尾音且不人为平移。旧 3110 和合成校准仍仅为预检，不计作本次候选结果。

## 检查、沿用结论与边界

924b79f 正式 Unity 检查 History 32、UI 52、Session/Audio 53，共 **137 项断言通过**。最终 SHA 的独立 Python 检查为 **98 个不同用例通过**：新 worktree 97 passed / 1 skipped，唯一依赖闭包项因新目录无本地 `.venv`，在同 SHA 原仓补跑 1 passed，残余 skip 0；保留两项既有警告。详细日志、方法与哈希见新包核对报告。冻结前完整仓库入口和 dirty Player 结果另外保留于 [独立检查](independent-checks.md)，不冒称完整 `tools/check.py` 又在最终 SHA 全部重跑。新增检查不替代真实 Win32 窗口和最终包实测。

| 条目 | 沿用 A0 第一轮结论与限制 |
| --- | --- |
| F01 | 接受当前发布动作集的十分钟节点上界和稳定分段证据。保留退出期 65,812 字节原生 MemoryLeaks 标记及更广负载限制；不宣称零泄漏或 UA11 通过 |
| F02 | 只发布 neutral / idle / greeting；ArtMesh274/273/256/193/192 明确禁用。被排除路径不是已验证遮罩；新增效果必须复测 |
| F03 | 接受当前动作和已测窗口尺寸的可见范围。真实 OS DPI 由本轮 R3 单独补证，其他动作/效果不推广 |
| F04 | 第二机器仍待外部 owner；不新增为当前同机 U-G0 的阻塞门槛 |
| 既有停止回环 | 沿用 A0 重算的 20/20 有效、1e-9 FS 阈值下 P95 155.845 ms；它属于旧 b94af81 原始采样，不伪称本轮重录，也不是物理按键或扬声器声学延迟 |

所有阶段、文件与环境证据以新包实际结果填入 [verification.json](verification.json)，最终结论交 A0 独立复核。本开发交接不宣布 U-G0 通过；U-G1 / U-G2 未执行并按优先级后置。没有修改验收 worktree、冻结 Contracts/锁或用户原有 local-environment 未提交文件。
