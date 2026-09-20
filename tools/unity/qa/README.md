# U01 进程限定音频回环 QA

`ProcessLoopback.cpp` 是独立 Windows QA 工具，使用官方 `ActivateAudioInterfaceAsync` 的 `VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK` 和 `PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE`，只包含指定 Player 及其子进程的输出。代码没有系统端点枚举、排除进程树模式或麦克风接口，激活失败直接失败，不会退回全局音频回环。

运行前必须由集成 owner 指定当前 fixture Player 的确切 PID、可执行文件完整路径、验证窗口和输出文件。**未获指定前只能构建和执行 `--self-test`。** 目标路径必须与活进程的真实镜像路径一致、文件名必须是 `NeuroSaki.exe`；持有进程句柄并在目标退出时结束采集。必须使用隔离测试数据与明确演示音频，不得用于私人对话或其他应用。

构建不安装任何依赖，使用本机现有 Visual Studio 2022 C++ 工具与固定 Windows SDK `10.0.26100.0`：

```powershell
./tools/unity/qa/Build-ProcessLoopback.ps1 -OutputDirectory C:/absolute/new/tool-output
```

获准后运行（PID 与路径必须替换为本次批准值）：

```text
ProcessLoopback.exe --pid PID --seconds 30 --output C:\absolute\new\fixture-output.wav --expected-exe C:\approved\player\NeuroSaki.exe --fixture-only
```

时长限 1–120 秒，输出文件必须不存在。输出 float32/stereo/48 kHz WAV、逐包 CSV 和摘要 JSON，使用浮点格式避免将 PCM16 转换产生的 ±1 LSB 抖动当成静音尾音。CSV 保留 WASAPI 原始设备位置、包起点的 QPC（100 ns 单位）、读取时原始 QPC tick、RMS、峰值、首末非零帧和原始状态 flags；摘要记录 QPC frequency。Unity Mono 的 `Stopwatch` 与 Win32 QPC 可能有不同计时原点，必须在 Player 停止操作旁另外记录 Win32 `QueryPerformanceCounter` 及 frequency 后比较。记录 discontinuity 或 timestamp-error 的样本须单列，不能默认为精确时延证据。

本工具记录的是 Windows 进程音频回环，不能单独证明物理扬声器/耳机的声学输出尾音。要比较停止尾音，应使用同一轮 Player 的 stop-command 原始 QPC tick 与包时间戳换算后的最后非零帧；报告实际捕获格式、时间戳错误/断流和回环测量边界。

停止测量工具只分析已有文件，不发起录音：

```text
python tools/unity/qa/analyze_process_loopback.py --wav ABS/audio.wav --stops ABS/stop-measurements.csv --output ABS/stop-analysis.json
python -m unittest discover -s tools/unity/qa -p "test_*.py" -v
```

分析器要求独立记录的 `stop_qpc_ticks / qpc_frequency`，拒绝凭 Mono `stop_ticks` 猜时间原点。每次停止必须覆盖前 250 ms、后 500 ms，窗口内不得播放下一轮新的声音；缺捕获覆盖、时间戳异常、断流或缺少 50 ms 静音尾段的样本均单列 invalid，不以零延迟填充。默认以 float 精度的零为静音，显式改变阈值时记录阈值。仅当至少 20 个有效样本且 P95≤200 ms 才把**这一项程序触发停止的回环子检查**记为通过，不能据此宣布整个 UA05 或人手操作延迟通过。

官方依据（2026-09-21 查阅）：

- [Microsoft Application loopback audio capture sample](https://learn.microsoft.com/en-us/samples/microsoft/windows-classic-samples/applicationloopbackaudio-sample/)：进程树包含模式、其他进程音频隔离与最低系统版本。
- [PROCESS_LOOPBACK_MODE](https://learn.microsoft.com/en-us/windows/win32/api/audioclientactivationparams/ne-audioclientactivationparams-process_loopback_mode)：固定使用 include 模式，最低 Windows build 20348。
- [IActivateAudioInterfaceCompletionHandler](https://learn.microsoft.com/en-us/windows/win32/api/mmdeviceapi/nn-mmdeviceapi-iactivateaudiointerfacecompletionhandler)：异步回调使用 WRL FtmBase 提供自由线程封送。
- [IAudioCaptureClient::GetBuffer](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudiocaptureclient-getbuffer)：QPC 的 100 ns 单位、数据包读取释放规则和时间戳错误标志。

构建与无录音自检证明工具可编译及基本参数/统计正确；只有指定窗口实际运行后的 WAV/CSV/JSON 才是回环证据。
