# U01 本机桌面预览后台

本模块实现 `unity-preview/1` 与 `/preview/unity/v1`，用于桌面基础功能。根 `contracts/` 和正式 R1 `/v1` 不变。fixture 回复、转写与测试音频始终明确标为演示；不代表真实 LLM、ASR、TTS 或云端验收。

## 启动与配置

集成启动器为仓库 `tools/unity/Start-Desktop.ps1`。它生成本次运行的随机令牌、限制配置文件权限，启动固定 `127.0.0.1:8000` 的单后台进程，并在 Player 退出后关闭该进程。令牌不作为命令行参数、不写日志、不提交源码。

手动开发入口：设置 `PYTHONPATH` 为 `services/api` 的绝对路径，将 `U01_PREVIEW_CONFIG` 指向当前用户私有配置，然后执行 `.venv/Scripts/python.exe -m app.unity_preview`。工厂入口是 `app.unity_preview:create_app_from_environment`，不应启用多 worker 或公网绑定。

私有 JSON 形状为 `{ "protocol": "unity-preview/1", "base_url": "http://127.0.0.1:8000", "token": "运行时生成的 URL-safe 随机令牌" }`。令牌必须为 32–256 个 ASCII 字母、数字、下划线或连字符。默认配置位置由启动器约定为 `%LOCALAPPDATA%/NeuroSaki/preview-runtime/config.json`。后台只读取 `U01_PREVIEW_CONFIG`，不会猜测其他令牌来源。

`U01_PREVIEW_MODE` 默认为 `fixture`。`cloud` 模式如实返回 `configured=false` / `available=false`，请求返回 503 `provider_unavailable`；本轮未接入云 provider，不会偷偷回退到 fixture。`SpeechSynthesizer` 是待后续 TTS 实现替换的后台边界。

所有预览路由要求 Bearer 令牌、loopback 客户端与 loopback Host；拒绝带 Origin 的浏览器请求，防止把本机预览变成网页可调用服务。无 CORS 放行、外部音频 URL、原始录音文件或聊天持久化。

## 固定联调样例

`protocol/` 保存请求、能力、事件、错误、取消、转写与运行配置的严格 JSON Schema，以及正常/故障 JSON 和 NDJSON；`protocol/manifest.json` 指明负例语义。JSON Schema 之外仍必须检查关联身份、序号、事件顺序、上下文总字符数和 WAV 描述一致性。`seq_duplicate.ndjson` 是相同事件重复，客户端应去重；`seq_gap`、`wrong_ids`、`partial_stream` 必须失败。

`fixtures/demo-tone.wav` 是随包原创测试信号，来源、音频时间表与哈希见 `fixtures/SOURCE.md`。它不是文字朗读。`normal.ndjson` 的请求/会话/turn UUID 固定，真实服务为每次新请求生成独立 turn UUID；测试消费时须用同一固定样例请求身份。

故障仅由 fixture 环境变量 `U01_PREVIEW_SCENARIO` 选择，不增加网络协议字段、不把用户聊天内容当作控制指令。修改后重启后台/启动器，下一次输入任意文字即可触发：

| 值 | 可复现行为 |
|---|---|
| `normal` | 明确演示文字，下载 8 秒测试 WAV；关闭朗读则不调用音频提供器 |
| `tts_failure` | 先发完整文字，再发 error；无 audio.skipped、无 generation.completed |
| `partial_stream` | accepted / delta 后 EOF，客户端必须识别不完整流 |
| `seq_duplicate` / `seq_gap` | 完全相同的重复序号 / 缺失序号 |
| `wrong_ids` | text.completed 携带错误 request_id，客户端不得消费 |
| `late_audio` | 文字后音频延迟 3 秒；期间停止必须终止等待且无迟到音频 |
| `slow_generation` | accepted 已发送后，固定等待 3 秒才开始 LLM fixture 回复；供已接受生成中取消测试 |
| `slow_download` | 正常生成；GET WAV 先返回 HTTP 响应头，固定等待 3 秒才发 body；等待后重新检查资源，取消/过期则结束空流，不返回旧 WAV |
| `corrupt_audio` / `truncated_audio` | HTTP 下载返回故意损坏 / 截断的 WAV，仅供验证拒绝路径 |
| `expired_audio` | 发 audio.ready 后获取返回 410，不自动重新生成 |
| `budget_exceeded` | 接受前返回 429，没有生成调用 |
| `provider_timeout` | accepted 后返回显式演示超时 error |

其他复现：不带/错误令牌得到 401；相同 request_id 再次提交得到 409 `duplicate_request`；先调用 cancel 再提交得到 409 `request_cancelled`；空 WAV 为 `invalid_audio`，全静音 WAV 为 `no_speech`。fixture ASR 校验真实输入格式后仍只返回固定演示转写，不能计入真实识别准确率。

延迟场景是显式 fixture 调度，来源为本模块的固定 3 秒等待，不是模型或网络实测延迟。`late_audio` 始终在 audio.ready 之前等待；它不代表下载中。`slow_download` 若已返回 200 响应头后被取消，不能再改 HTTP 状态码，因此不发旧音频并关闭流；后续重新 GET 才返回 410。客户端仍须立即本地停止/关闭下载，禁止自动重新生成。

## 有界生命周期

单令牌最多一个活动生成或识别；新请求取消旧操作。停止、断流和断开的 ASR 请求都会清理消费任务，不发布旧 provider 结果。取消先到的 tombstone 和去重登记分别最多 512 条、保留 5 分钟；未过期项不为新请求让位，容量满返回 429。已接受的相同 ID 不再次调用 provider。

WAV 保留最多 5 分钟、总缓存 32 MiB；取消立即释放对应音频，缓存超额逐出最旧项，获取失效资源为 410。LLM / TTS / ASR / 总操作时限分别为 45 / 30 / 30 / 90 秒。录音输入只在请求内存中存在。fixture 不访问网络、不消耗云预算；预算不足场景是显式故障样例，不宣称已实现真实云账单预算器。

测试：`.venv/Scripts/python.exe -m pytest services/api/tests/unity_preview -q`。其中真实 TCP 测试自动使用临时 loopback 端口，验证生成断流、取消与 ASR 断开清理；不占用集成端口 8000。源码入口本身仍固定 8000。
