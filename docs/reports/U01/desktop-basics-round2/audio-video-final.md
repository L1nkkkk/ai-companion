# R4 最终候选：连续同步视听演示

最终候选 `924b79f05b6c90983bdc5c18344aa125375fa57c` 的实际 Player 同步视听文件已交付。独立核对同次 PID、native QPC、画面/声音范围、逐阶段振幅、实际关键帧与编码后的时序，记录一致；本报告不声称分析人完整人工听过电影，也不替 A0 宣布正式 R4 / U-G0 验收结论。

**[播放/下载同步 MP4](C:/Users/Link/Dev/ai-companion-dev-tools/evidence/desktop-r2-924b79f/audiovisual/mux-01/synchronized-evidence.mp4)**，约 56.94 秒，13,541,940 字节。SHA-256：`8affcd16db61b31d877e0cc9211ceb9dde796bfc49c51a3c93e1cb132dc77cec`。

## 同一候选与采集范围

原始证据目录：`C:/Users/Link/Dev/ai-companion-dev-tools/evidence/desktop-r2-924b79f/audiovisual/`。本次由集成 owner 启动唯一 Player PID **42280** 和仅包含该进程树的 WASAPI 回环，音频在该 Player 退出后结束。帧来源是此 Player 自身 backbuffer，不是全桌面截图；没有其他应用画面、全局音频或麦克风采集。独立分析只读取完成后的文件并离线编码，没有另启采集、Player 或 UI。

`frame-source.json`、音频摘要、启动结果与包运行前后清单中的 SHA / PID 相互一致，运行前后包未变；包清单 SHA-256 为 `b05472f60a49424f6c198f35980f472d0e2ff13f2116d0bc5c5885e411a8c934`。实际环境为 Windows Player / Unity 2022.3.62f3c1，1280×800，系统 DPI 120（125%）；本轮没有其他 UI 输入或 resize。Player 持续 70.0165764 秒，2 次实际开始播放、1 次完整完成、1 次主动停止，脚本完成、errors/warnings/failures 均为零。源码从全新工作目录构建的证据由 R2 独立报告承接。

## 连续演示与独立核对

MP4 的零点来自两路共同覆盖中的第一张实际帧，不是任意拖动音频后的对齐点。以下定位使用同次事件 native QPC 换算，便于 A0 连续播放时复核：

| MP4 中的约略时间 | 同次事件与实际结果 |
| --- | --- |
| 11.935 s | 提交第一轮完整 fixture 演示 |
| 13.132–21.116 s | 第一轮实际播放，结束记录 192000/192000 Completed；固定 WAV 的三段有声及间隔静音完整覆盖 |
| 23.129 s | 提交第二轮音量演示 |
| 24.282 s | 第二轮开始，音量 80% |
| 25.088 s | 降为 20%；非零 RMS 中位数由 0.0879005 降为 0.021763，比值 **0.247587** |
| 25.794 s | mute；42 条该音量音频采样全零，直到恢复期间的 23 张实际帧 mouth 值全零 |
| 27.601 s | 恢复 80%，实际音频振幅和帧 mouth 恢复非零 |
| 28.307 s | Stop；首张实采闭嘴帧在命令后 **18.510 ms**，随后三秒 38 帧 mouth 值全零、UI 为 Ready / Interrupted |
| 31.315 s 之后 | 三秒停止观察完成，视频仍连续保留静止后的角色与 UI 状态 |

第一轮振幅按实际 source sample_start / 24000 对照固定 WAV：0–0.45、2.1–2.9、5.1–5.9、7.6–7.9 秒窗口全部为零；0.6–1.9、3.1–4.9、6.1–7.4 秒窗口全部非零，没有使用服务端进度模拟振幅。20% 切换后口型有原实现的有声平滑，未宣称瞬间跳到四分之一；mute 与 Stop 的实际帧 mouth 值为零。

已查看原始 `frame-000218.png`（有声）、`frame-000243.png`（素材静音）、`frame-000373.png`（mute）和 `frame-000405.png`（Stop）：图像正向，包含真实 Mao 与中文 UI / fixture 标记；mute 帧显示 0% 和“恢复”，播放仍未结束；Stop 帧显示准备状态及已中断。数值、画面和事件身份一致。该工作是数值与选定实际帧复核，**没有将其说成完整人工听看验收**；连续 MP4 已提供给 A0 播放。

该轮唯一 Stop 的原始 float32 回环在声明的 `1e-9 FS` 阈值下，最后信号位于命令后 **130.0535 ms**。这只补充本轮尾音观察，不与生成/下载取消混算、不代替上一轮已独立接受的 20 次停止分布。视频里保留实际 Windows 缓冲尾音，没有为提前闭嘴画面删音或移动音轨。

## 同步精度、实际帧率与编码边界

使用冻结 `av-evidence-tool-04/EncodeAvEvidence.exe` 离线 H.264/AAC 编码；SHA-256 `29da45f61001302f5c3781d141fcbf2f175bad17baedc320f9c927ed78f4406d`。方法与已保留的技术校准、旧候选失败尝试见 [方案和预检报告](audio-video.md)。本次没有修改冻结工具或产品源码。

| 项目 | 结果 |
| --- | --- |
| 原始 Player PNG | 772 张，captured 完成、0 readback errors；dropped=0 只表示未丢弃已提出的采集请求 |
| 两路共同覆盖 | 56.9367742 s；录音启动前的 40 张帧排除并记账，732 张实采帧进入视频；不合成捕获前的声音 |
| 编码前后帧数 | 732 / 732，无插帧或人为恒定帧率重定时 |
| 实际采集速度 | **12.844709 fps**；帧间隔 P50 77.7614 ms、P95 79.6073 ms、最大保持 83.106 ms；不声称稳定 15 fps |
| 主线程采集成本 | readback P95 2.6067 ms；读回和 PNG 写盘最大 34.5679 ms |
| Player 在采集负载下 | 帧时间 P95 28.6 ms、最大 52.4327 ms，无超过 1 秒帧；不代替无采集负载的稳定性测试 |
| MP4 实际 track timescale | video=15000、audio=48000，直接读取 `mdhd` 校验 |
| 编码视频误差 | 最大 PTS 差 **0.0664 ms**，duration 差 0.0640 ms；在一个实际 video tick（约 0.066667 ms）内 |
| AAC 音频区域 | 五段实际信号区域均保留，边界最大偏移 **0.0756 ms**；末端编码补齐约 1.9035 ms，原始信号未平移 |
| WASAPI 原始音频 | 6703 包、67.03 秒采样；0 discontinuity flags、0 timestamp-error flags |

原录音退出前另有 **10.9162 ms** 包 QPC 缺口，位置在视频结束约十秒之后，完整记入 alignment JSON；它不与编码区间相交，没有被填音。编码区间内按既定规则没有超过 2 ms 的包时间缺口。帧间隔只保持上一张真实帧，没有推算或重新渲染角色状态。

视频时间是 Unity EndOfFrame 读回前的 native QPC 观察点，不是 DWM 或物理显示器扫描时刻；音频来自 Windows 指定进程输出，不是麦克风声学录制。MP4 的 YUV420/AAC 是观看用的有损派生文件，原始 PNG、float WAV 和 QPC CSV 未改动。编码后的声音边界检查使用 PCM16 幅值 100 与短于 10 ms 的正弦过零合并规则，不能拿它替代 raw WAV 停止分析的 `1e-9 FS` 阈值。

## 复核文件

- [紧凑同次身份、阶段和哈希记录](audio-video-final.json)
- [独立 MP4 解码后的时间与声音边界检查](audio-video-encoding-final.json)
- [原始逐帧索引](C:/Users/Link/Dev/ai-companion-dev-tools/evidence/desktop-r2-924b79f/audiovisual/video-frames/frames.csv)
- [同次播放及音量事件](C:/Users/Link/Dev/ai-companion-dev-tools/evidence/desktop-r2-924b79f/audiovisual/events.csv)与[音量后振幅](C:/Users/Link/Dev/ai-companion-dev-tools/evidence/desktop-r2-924b79f/audiovisual/output-levels.csv)
- [原始限定进程 float WAV](C:/Users/Link/Dev/ai-companion-dev-tools/evidence/desktop-r2-924b79f/audiovisual/audio.wav)与[逐包 QPC](C:/Users/Link/Dev/ai-companion-dev-tools/evidence/desktop-r2-924b79f/audiovisual/audio.wav.packets.csv)
- [全部输入和 PNG 哈希、共同覆盖及实际帧率](C:/Users/Link/Dev/ai-companion-dev-tools/evidence/desktop-r2-924b79f/audiovisual/mux-01/alignment.json)

原始 WAV SHA-256：`3d83944e9f60f7efe889e494e3467bc0143ea7d2623e23bfd498e6aac04dcfad`；帧 CSV SHA-256：`065a14d64508161cc3394b557a85051bce3b9632f760ea35a058b706946b49df`。更多文件哈希见紧凑 JSON。3110a56 预检、其原始失败尝试及全部证据原样保留，没有改称最终候选。
