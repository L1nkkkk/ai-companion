# R4：同一 Player 的同步视听证据

Owner：U01-06 集成录制；U01-04 提供限定进程回环及离线同步工具。当前状态：**工具已通过独立技术验证，最终候选实际同步录制尚未执行，R4 暂不宣布通过。** 本文件在获得最终同一 SHA 的实际录制后补充结果。

## 采集范围与职责

已阅读 computer-use 的 SKILL、guidance 和 confirmations。原生 Windows 应用操作只通过其 `sky` API；该 API 没有连续录像入口，因此本轮不另写外部窗口截图、桌面捕获或 UI 自动化工具。采用集成 owner 明确授权的应用内 QA 路径：Composition 的 opt-in `DesktopVideoEvidence` 在实际 Windows Player 的 `WaitForEndOfFrame` 保存自身 backbuffer PNG，包含真实 UI 与角色画面。它不抓取其他窗口、桌面或系统通知。

声音沿用 `ProcessLoopback` 的官方 `INCLUDE_TARGET_PROCESS_TREE` 模式，仅采集 owner 明确指定的同一 `NeuroSaki.exe` PID 及其子进程树；要求镜像路径核对、fixture 标志和限定窗口。全局 loopback 与麦克风始终禁用，不存在失败回退。未获得确切 PID 和时间窗口之前不启动实际采集。

程序内部渲染帧与进程音频回环是两条独立实际数据流；不以素材帧、重放角色状态、源 WAV 或人工对齐后的声音代替实际播放。截图 API 的完成帧边界遵循 [Unity ScreenCapture 文档](https://docs.unity.com/en-us/engine/6000.7/script-reference/unityengine/screencapture/capturescreenshotintorendertexture)；实际实现使用固定版本中已有的同步 `CaptureScreenshotAsTexture`，同步 PNG 成本另行计量。

## 共同时间基准与文件约定

`video-frames/frame-source.json` 记录 `unity_player_backbuffer_only`、PID、fixture、完整 source SHA、native QPC frequency、目标帧率和时间边界。`frames.csv` 每行保存：

```text
frame_index,file,capture_qpc_ticks,qpc_frequency,readback_done_qpc_ticks,write_done_qpc_ticks,width,height,request_id,generation,turn_id,mouth,volume,phase,status
```

帧时间取 `WaitForEndOfFrame` 后紧邻本帧读回前的 Win32 QPC；读回和 PNG 写盘结束另存时间，不能拿写盘结束时间代替帧时间。`capture-end.json` 记录结束 QPC、完成标记、实际帧数、丢弃请求和错误数。同步采集没有排队丢帧不代表达成目标 15 fps；实际帧间隔和保持时长必须从 CSV 计算。

音频每包保留 WASAPI 的 QPC 100 ns 时间戳、实际采样数与 flags。两路都换算到 native QPC 100 ns，剪裁到共同覆盖区间，以第一张纳入的实际帧作为视频零点。每个视频 sample 的 PTS 与 duration 来自相邻实际帧时间，不按照序号猜固定帧率，不插值或补造角色帧。连续帧之间保持上一张实采帧，间隔过大必须在报告中显露。音频只按相同 QPC 区间裁切，不因看起来不同步而平移，不制造录音启动前的声音。

该时间基准对应 Unity 完成渲染时的观察点和 Windows 进程输出，不是 DWM 最终呈现、物理显示器、手指输入或扬声器声学时间。源录音可包含 Windows 输出缓冲后的真实尾音，不能为了让停止画面“看起来正确”而将它删掉。

## 离线编码与独立复核

现有 `JiJiDown/OrderEXE/ffmpeg.exe` 为精简 4.1.2，仅支持有限重封装和 MP3，不具备所需图片/WAV解码、视频/AAC编码；没有安装新 FFmpeg、SDK 或产品依赖。独立 `EncodeAvEvidence.cpp` 使用已有 Visual Studio 2022 与 Windows SDK `10.0.26100.0`，通过 Windows Media Foundation sink writer 编码 H.264/AAC。它只有已有文件的读取、编码、解码检查功能，不包含屏幕、窗口、端点或麦克风采集 API。依据为 [Microsoft Sink Writer 编码指南](https://learn.microsoft.com/en-us/windows/win32/medfound/tutorial--using-the-sink-writer-to-encode-video)和[时间戳保留规则](https://learn.microsoft.com/en-us/windows/win32/medfound/time-stamps-and-durations)。

`mux_qpc_evidence.py` 校验同 PID/范围、固定偶数尺寸、完整结束清单、单调 QPC、帧文件边界、WAV/逐包长度与状态；时间戳错误、断流或超过 2 ms 的音频包时间缺口明确失败。输出 PNG 哈希清单、原始文件哈希、实际帧率/间隔、读回及写盘耗时、共同覆盖裁剪范围，再调用离线编码器。原始 float32 WAV、PNG、CSV 不改动；MP4 使用 YUV420 BT.601 与从 PCM16 编码的 AAC，是便于观看的有损派生文件，不能替代 raw WAV 的尾音定量证据。

编码完成后重新打开 MP4，独立读取视频 sample PTS/duration，并解码 AAC。数量必须与输入帧一致、视频时间差不得超过 1 μs；另用声明的 PCM16 幅值阈值 100、短于 10 ms 的正弦过零间隔合并规则核对声音起止区域，超过 30 ms 的编解码偏移拒绝标为已同步。这是编解码核对阈值，不代替原始 float 回环的 `1e-9 FS` 停止测量阈值。实际检查结果与 AAC 补齐尾段均保留；不以静音填补缺失录音。

## 当前技术验证（不是产品验收证据）

独立生成 64×64 黑/白帧和合成正弦，模拟 1.95 秒内 17 个不等间隔的 QPC 帧；没有启动 Player、麦克风、系统音频或窗口捕获。

| 校准项 | 结果 |
| --- | --- |
| 不等间隔帧数 / 解码后帧数 | 17 / 17 |
| 最大 PTS 差 / duration 差 | 0 / 0（100 ns 单位） |
| 预定音频区间 | 0.95–1.45 s |
| 声音起点偏移 / 最后信号偏移 | 0 / −0.0001 ms |
| AAC 解码结束 | 1.9626666 s，输入为 1.95 s；编码器补齐 12.6666 ms |
| 同步分析脚本 | 7 项测试通过，另有既存回环分析 5 项 |

校准目录：`C:/Users/Link/Dev/ai-companion-dev-tools/evidence/av-encoder-calibration-01/mux-tool04/`。技术验证使用 `av-evidence-tool-04/EncodeAvEvidence.exe`；其 `tool-build.json` 保存构建工具和哈希。校准证明编码工具能够保留输入时间，不能证明最终角色口型/声音已通过 R4。

## 最终候选录制步骤

1. owner 从最终修复 SHA 的全新构建启动隔离 fixture Player，启用 audiovis QA；明确 PID、EXE 完整路径和时间窗后启动唯一限定进程回环。Player 预留 15 秒准备时间。
2. 连续记录第一轮完整固定 WAV 的有声/静音段；两秒间隔后第二轮展示 0.8→0.2→mute→恢复 0.8→Stop，并保留停止后至少三秒。不要切换窗口尺寸或混入其他应用画面。保留同次 events、音量后 RMS 与逐帧 mouth/QPC。
3. 两路结束并正常写完清单之后运行下面的离线命令。人工完整观看生成 MP4，核对中文 UI、角色画面、静音与停止闭嘴；同步用动作事件 QPC/振幅/逐帧值定位片段，记录实际观测与任何延迟、掉帧或边界。
4. 补充实际候选 SHA、PID、时间窗、MP4和原始数据哈希、编码后检查及观看结论，交 A0 独立复核；现阶段不提前宣布缺口关闭。

```powershell
./tools/unity/qa/Build-AvEvidence.ps1 -OutputDirectory C:/absolute/new/tool-directory
./.venv/Scripts/python.exe tools/unity/qa/mux_qpc_evidence.py --frames C:/absolute/evidence/video-frames --wav C:/absolute/evidence/audio.wav --output C:/absolute/new/mux-directory --encoder C:/absolute/tool-directory/EncodeAvEvidence.exe
./.venv/Scripts/python.exe -m unittest discover -s tools/unity/qa -p 'test_*.py' -v
```

工具不申请或推断捕获授权，上述离线命令只消费已有文件。本次没有修改 SDK、共享 Contracts 或产品模块；Composition 捕获及演示序列由集成 owner 实施。
