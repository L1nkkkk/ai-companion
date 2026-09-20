# desktop-dev-04 实际 Player 故障与阶段取消复核

日期：2026-09-21。集成 owner 在完成输出回环测试并释放 8000 后授权执行；本轮只管理本次启动器创建的后台和 Player，未终止其他进程，未修改产品源码。结论为 **11 轮全部完成：9 类故障按预期失败并可停止恢复，已接受生成中取消 10 次、下载响应头到达后取消 10 次，无旧声音复活**。这不是完整 U-G0 / U-G1 / U-G2 通过结论。

## 实际环境与证据

Player：`C:/Users/Link/Dev/ai-companion-dev-tools/builds/desktop-dev-04/NeuroSaki.exe`。Unity 2022.3.62f3c1、Mono、D3D11、RTX 5060 Ti、1280×800、输出 48 kHz Stereo，DSP 1024 帧 × 4。后台使用本仓库 `.venv` 与已冻结 `unity_preview` fixture；没有云调用或麦克风采集。

证据根目录：`C:/Users/Link/Dev/ai-companion-dev-tools/evidence/desktop-faults-dev-04/`。每个场景均有独立 `runtime/`、`userdata/`、`player.log`、`player-result.json`、CSV 与截图。`batch-summary.json` 保存实际启动/退出时间、模式与启动器结果；`independent-review.json` 保存本次读取结果、历史交叉核对、关键文件 SHA-256 及程序集比较。每轮退出后对应 config.json 均已删除，最终复核 127.0.0.1:8000 无 listener 后才通知集成 owner 使用端口。

本轮沿用 `tools/unity/Start-Desktop.ps1`：故障模式每轮 20 秒，两个阶段取消模式各 35 秒。未减少 10 次的采样要求，未用等待时长冒充阶段到达。部分运行期间另有集成 owner 启动的 dev-05 离线 UI 手动测试；本报告不把这些短运行的帧时间当独占性能数据。

## 九类故障结果

以下每行实际 Player 都退出 0，`scriptedChecksCompleted=true`、`failures=[]`，真实播放启动数 0；Unity 日志 error/warning 数均为 0。这里的日志计数与有意触发的 Session.Error 是不同事项。每轮在截图之后调用停止并检查回到 Ready；未宣称已在同一故障后台上成功重试正常回复。

| Scenario | 实际历史 Error.Code | 保留文字 | 结果 |
|---|---|---|---|
| `tts_failure` | `provider_unavailable` | 完整 41 字符演示回复，Failed | passed |
| `partial_stream` | `protocol_error` | 已接收的 41 字符 delta，Failed | passed |
| `seq_gap` | `protocol_error` | 合法 delta，拒绝缺号完成事件 | passed |
| `wrong_ids` | `protocol_error` | 合法 delta，拒绝错误身份完成事件 | passed |
| `corrupt_audio` | `invalid_audio` | 完整文字，0/192000 样本，Failed | passed |
| `truncated_audio` | `invalid_audio` | 完整文字，0/192000 样本，Failed | passed |
| `expired_audio` | `resource_expired` | 完整文字，0/192000 样本，Failed | passed |
| `budget_exceeded` | `budget_exceeded` | 空回复、无 server turn，Failed | passed |
| `provider_timeout` | `provider_timeout` | 空回复、已有 server turn，Failed | passed |

实际持续时间为每轮 20.004–20.016 秒左右；所有助手记录都是 Failed，没有错误写成 Played。故障通过表示客户端正确处理显式 fixture 失败，不表示故障场景提供了真实云预算、云超时或收费证据。

## 已接受生成中取消 10 次

`slow_generation` + `-TestMode generation`，实际 35.006 秒。runner 每次等待当前 Session 已有 server turn、仍处 Thinking、FullText 为空后取消；这是 accepted 后的生成阶段，而非 SubmitText 后立即取消。

- result 的 acceptedGenerationStops=10，CSV 有 10 个 accepted-generation-cancel。
- 持久历史有 10 个不同 request_id、10 个 server turn_id；10 条助手均为 Interrupted、空文字、0/0 样本、无 Error.Code。
- 播放启动数 0、非零振幅行 0、errors=0、warnings=0、failures=[]。
- 最后一轮后仍观察超过 fixture 的 3 秒延迟，未出现旧输出。

## 已收到下载头后取消 10 次

`slow_download` + `-TestMode late-audio`，实际 35.015 秒。虽沿用 runner 的 late-audio 模式名，后台场景明确为 **slow_download**，没有用原 late_audio 冒充下载中。

Gateway 在成功校验 WAV HTTP response headers 后发出内部 AudioDownloadStarted(turnKey)，runner 等待该标记与本轮 generation 相同才取消；不是仅凭 Session.PreparingSpeech 状态猜测。

- result 的 downloadStops=10，CSV 有 10 个 download-cancel-command。
- 持久历史有 10 个不同 request_id、10 个 server turn_id；10 条助手均为 Interrupted，保留完整 41 字符文字，played=0、total=192000、无 Error.Code。
- 播放启动数 0、非零振幅行 0、errors=0、warnings=0、failures=[]。
- 运行时长超过最后一次取消后的 3 秒 body 延迟，未出现下载后旧声音复活。

**证据限制：** 当前 runner 的 Mark 方法取 PlaybackSnapshot；上述阶段尚未 Play，故两个阶段 CSV 的 request_id/generation 列为空。确切阶段由编译后的 runner 条件检查、10 次结果计数与持久历史的独立 ID/turn 交叉核对支持，但不是完整的逐事件身份轨迹。后续应在 Mark 中记录当前 Session.Operation。该限制已通知集成 owner，未改写原始 CSV 或补造 ID。

## 候选差异与可复用范围

本轮实际测试的是 dev-04。对随后 dev-05 的字节比较表明，下列程序集 SHA-256 **完全一致**：Audio、Avatar、Contracts、Foundation、History、Session、Transport。Composition 与 UI 不同，后续 Shift+Enter / 导出界面修复须在新 Player 单独验证，不能拿本批自动 Session 调用代替人工 UI 测试。

| dev-04 程序集 | SHA-256 |
|---|---|
| Companion.Audio.dll | `beffd0ef47781106701a56b20f0db477dbba17f99921c852cdb111be3841f66c` |
| Companion.Session.dll | `cb6bef21d277a0649ebd95abdb0ef5bad129d9475d89351261a236ea2d757696` |
| Companion.Transport.dll | `51d83b2219fd4658a36e4b6fef3164b6190ab418b699901f0ebb50dbfa2f949a` |
| Companion.Composition.dll | `4b62842ba49c32da5d43cd351834504765aa0c1f569c5f2c0de15d06cbb36f46` |
| Companion.UI.dll | `1072fe4b9d5c5186837c19db2716c9aece560994bf247f448e4cf2bb553f1192` |

未覆盖项仍保留：无/错令牌的实际 Player 入口、后台进程突然退出、相同 seq 的正向去重 Player 运行、真实云请求、ASR、IME/DPI/键盘矩阵、长期稳定性和正式 R1。原 `desktop-smoke-01` 失败与 `desktop-smoke-02` 100 秒结果继续见 [只读冒烟复核](qa-smoke.md)，本轮不隐去或重标以前的失败。实际输出尾音与播放中停止 P95 属于独立输出回环证据，本批未播放声音，不能与其合并计算。
