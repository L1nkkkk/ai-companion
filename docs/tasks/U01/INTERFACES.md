# U01 接口与取消约定

C# 协作起点已由 [CSHARP-BASELINE](CSHARP-BASELINE.md)冻结，包含 Transport→Audio 验证器注入。本文的预览协议、取消和数据约束保持有效；C# 成员名不自动定义 JSON 拓扑。

版本：`unity-preview/1`。设计 owner：A0。适用：U01 本机 Windows 原型。本文定义语义；实际 C# 类型、schema、服务实现由独立开发会话提交。

## 1. 与正式契约的关系

正式 R1 的 [CONTRACTS.md](../../blueprint/CONTRACTS.md) 及 `contracts/` 不变。本协议独立使用 `/preview/unity/v1` 前缀，不覆盖 `/v1`，不使用历史网页 `/prototype/chat` 作为未声明的依赖。它没有账号、租约、跨设备恢复或正式 PCM 帧；U01 的 `generation` 是本地请求取消代数，不冒充 R1 `session_epoch`。

U01-00 / U01-03 分别生成共享 C# 类型与可执行的预览协议样例，须与本文逐项对应。若开发中发现字段不足，先向 A0 提交变更建议，不能由客户端和后台私下形成另一份协议。

## 2. C# 模块边界

| 边界 | 输入/操作 | 输出/约束 |
|---|---|---|
| SessionController | SubmitText、BeginRecording、EndRecording、Cancel、NewConversation、SelectConversation、DeleteHistory | 唯一状态快照：phase、request_id、generation、turn_id、mode、error；UI 不自建第二状态机 |
| ConversationGateway | 能力检查、提交文字、取消请求、读取 WAV、发送录音；所有操作携带取消句柄 | typed 事件/结果；对外不暴露 provider 密钥与 Cubism 类型 |
| AudioPlayer | Play(validated PCM, request_id, generation)、Stop、SetVolume | PlayedSamples、PlaybackStarted/Ended、PostVolumeLevel；只有当前请求可以进入输出 |
| MicrophoneCapture | Begin(device)、End、Abort | 内存中的 PCM 与实际格式；最大 30 秒；End / Abort 必须释放设备 |
| AvatarPresenter | LoadCharacter、SetPlaybackState、SetAudioLevel、ApplyExpression、ApplyMotion、Suspend、Resume | 有界参数与能力清单；不支持动作返回降级结果；不自行发 AI 请求 |
| HistoryStore | Load、Append/Update、Export、DeleteConversation、ClearAll | 版本化记录，区分 generating、generated、displayed、played、interrupted、failed，并记录 text/audio 交付方式；不持有原始录音 |

公开回调携带关联 ID；网络线程不直接改 GameObject / UI。AudioPlayer 在音频线程与主线程间采用有界数据交换，不能每帧分配无限数组。音量口型经过短窗口 RMS / 平滑 / 限幅，静音或停止归零；表情与讲话动作以实际播放开始为准。

Session 另维护本地 `draft_revision`：用户编辑草稿、切换会话或开始新一轮录音时递增。提交 ASR 时保存其值；结果只有 request_id、generation、draft_revision 都仍匹配时才能更新输入框。该值属于客户端草稿保护，不需要冒充服务端会话版本。

## 3. 本机连接与配置

- 后台固定绑定 `127.0.0.1:8000`，Unity 默认连接该地址。U01 仅允许 loopback；不会通过修改 Host 或允许任意 Origin 变成公网服务。
- 开发启动器生成随机的短期本机访问令牌，保存在当前用户可读的运行配置中，Unity 从约定配置位置读取。所有预览接口使用 `Authorization: Bearer ...`；重启后旧令牌失效。获取步骤由 U01-00/03 冻结，不把值写进源码、参数命令行、日志或报告。
- 云端 endpoint、模型、key、TTS / ASR 配置和预算位于后台。Unity 只能选择后台已公布的模式与音色 ID；不得提交任意 URL、system prompt、脚本或文件路径。
- 后台通过环境显式选择 fixture / cloud。配置缺失、超时或额度耗尽返回错误，不静默换成预设回答。测试 fixture 默认不连外网。

## 4. HTTP 接口

| 方法与路径（均位于 `/preview/unity/v1` 下） | 输入 | 结果 |
|---|---|---|
| GET `/capabilities` | 本机访问令牌 | 协议版本、chat_mode、tts_mode、asr_mode、configured/available、字符 ID、音色清单及限制；绝不返回密钥 |
| POST `/turns` | 下述 JSON | UTF-8 NDJSON；逐行事件；一个请求只产生一个 server turn_id |
| POST `/requests/{request_id}/cancel` | JSON `{ "generation": uint32 }` | `{ request_id, cancelled: true }`；幂等；请求尚未接受时也登记取消标记 |
| GET `/turns/{turn_id}/audio.wav` | 同一本机令牌 | WAV PCM16LE / mono / 24000 Hz；超期 410，不自动重新计费合成 |
| POST `/transcriptions` | 原始 `audio/wav` 请求体；`X-Request-Id` UUID、`X-Generation` uint32 | JSON `{ request_id, generation, text, mode: "cloud"或"fixture" }`；结果只进入编辑框 |

`POST /turns` 字段：

| 字段 | 类型和上限 |
|---|---|
| protocol | 固定 `unity-preview/1` |
| request_id | 客户端 UUID v4；每次新操作生成，与取消共享 |
| generation | uint32，本进程每次开始/停止递增；溢出前结束并重建客户端运行态 |
| conversation_id | 客户端本机会话 UUID；用于关联，不代表服务器持久会话或账号权限 |
| character_id | `/capabilities` 公布的 ID，最多 64 字符 |
| text | 非空用户文本，最多 2000 Unicode 字符 |
| history | 最多 6 条历史 `{role: user或assistant, text}`；每条最多 2000 字符；含本次 text 总计不超过 12000 字符；assistant 仅包含音频模式 played 或文字模式 displayed 的完整回复 |
| generate_audio | boolean；关闭自动朗读时为 false，不触发 TTS 费用 |
| voice_id | 已公布 ID 或 null；系统默认音色由后台选择 |

模型输出最多 400 Unicode 字符；JSON 请求体最大 64 KiB。限制必须由后台校验，不能仅依靠输入框。拒绝额外字段、客户端自带 system 角色与未知字符/音色 ID。reply 超限或 provider 不支持参数时给出明确失败，不默默截断音频并宣称完整回复。

## 5. NDJSON 事件与完成语义

通用包络为 `{protocol, request_id, generation, conversation_id, turn_id, seq, type, payload}`。`turn_id` 是后台 UUID，`seq` 从 0 严格递增；第一条必须是 `turn.accepted`。行使用 UTF-8，以换行结束，不按网络 read 边界分割 JSON；单行上限 64 KiB，未知协议与非法字段导致当前请求失败。

| type | payload | 意义 |
|---|---|---|
| turn.accepted | `{mode: fixture或cloud}` | 后台已接受一次调用；后续事件保持相同关联 ID |
| text.delta | `{text}` | 追加草稿；不代表已朗读 |
| text.completed | `{text, emotion}` | 完整生成文字；emotion 为 neutral / happy / sad / surprised / thinking，不支持映射时降级 |
| audio.ready | `{path, sample_rate:24000, channels:1, codec:"pcm_s16le", total_samples}` | path 必须精确指向该 turn 的相对 `/preview/unity/v1/turns/{turn_id}/audio.wav`；可以读取校验后的音频 |
| audio.skipped | `{reason:"user_disabled"}` | 请求明确不要 TTS；不是声音生成失败的替代事件 |
| generation.completed | `{}` | LLM/TTS 生成已结束，仍可能尚未播放；UI 不能仅凭此事件显示已说完 |
| turn.cancelled | `{}` | 当前请求终态，不能再有可播放事件 |
| error | `{code, message, retryable}` | 当前请求失败并关闭流；错误信息脱敏、可操作 |

正常顺序：accepted → delta* → text.completed → audio.ready 或 audio.skipped → generation.completed → EOF。失败/取消可以在 accepted 后任一阶段结束，之后没有 completed。前置鉴权/校验失败直接返回非 2xx JSON。TTS 失败可以保留生成文字，但必须发 error 并显示“语音失败”，不能改成 audio.skipped 假装用户关闭朗读。

客户端把生成完成、音频下载完成和播放完成分开。只有实际播放器结束事件才标记 `played`；中途停止标记 `interrupted`。`generate_audio=false` 时，完整文本显示且 generation.completed 到达后标记 `displayed`，可进入文本上下文，不伪造已播状态。EOF 缺少终态、seq 断裂、错误关联 ID、畸形数据视为不完整请求，停止其输出并保留已有文字。识别到重复 seq 可丢弃相同已处理事件；同 seq 不同内容视为协议错误。

## 6. WAV、识别与资源上限

输出 WAV 最长 120 秒、文件最大 8 MiB；校验 RIFF/WAVE、fmt、data、codec、通道、采样率、样本长度与声明 total_samples，遍历合法额外 chunk，不能假设所有 WAV 都是固定 44 字节头。拒绝未知压缩格式、不完整文件和格式伪装。U01 允许完整下载后播放；不因此声称实现 R1 分段流式 TTS。

麦克风先按硬件实际格式采集，由客户端适配为 16 kHz / mono / PCM16LE WAV。最多 30 秒，上传最大 1 MiB；后台重新解析时长与样本数据，不只信请求头。内存缓冲在结束、取消或失败后释放，默认不写音频文件。没有语音、设备撤销、空 WAV、超时均有单独错误码。结果必须匹配仍有效的 request_id / generation 才写入草稿。

建议冻结的初始上限：LLM 45 秒、TTS 30 秒、识别 30 秒、单 turn 总时限 90 秒；单行读取空闲 30 秒；音频获取 15 秒。服务持有 WAV 最多 5 分钟、总量最多 32 MiB，超限清除过期或最旧完成项；取消请求立即释放其音频。客户端收到 410 显示资源已过期，不自动重新请求 LLM。

每个本机令牌最多一个活动生成/识别操作；新请求先将旧请求置为取消并断开消费。相同 request_id 在 5 分钟去重窗口内不再次调用 provider：返回 409 与 `duplicate_request`；客户端不自动重试 POST。取消标记和去重条目各有 512 条上限；未过期取消标记不能为腾空间提前丢弃，容量满时拒绝新请求并返回 429。

取消接口允许先到，保留 tombstone；稍后到达同 ID 的提交直接返回 409 `request_cancelled`。断开流或取消时清理后台消费/合成任务；即使 provider 计算无法真正停止，也不再发布旧结果。只有用户重新点击才发起新的收费操作。

## 7. 统一错误与最小联调样例

非流响应错误形状为 `{code, message, retryable, request_id}`，无法解析 ID 时为 null。至少覆盖 `unauthorized`(401)、`invalid_request`(422)、`duplicate_request`/`request_cancelled`(409)、`rate_limited`/`budget_exceeded`(429)、`provider_unavailable`(503)、`provider_timeout`(504)、`invalid_audio`(422)、`resource_expired`(410)、`cancelled`。NDJSON 内沿用相同 code，已返回 200 后不能伪造第二个 HTTP 状态。

U01-03 提交固定样例：正常文字+WAV、不要音频、云缺配置、ASR 成功/空录音、取消先到、流半途断开、seq 重复/缺失、音频延迟到达、TTS 失败后保留文字、损坏 WAV、过期资源与预算不足。U01-04 / QA 用同一组样例验消费行为。fixture 必须确定性、无收费调用，并显示明确模式。

本协议的本机令牌、request_id、generation 和 WAV 只解决 U01 的局部边界。未来对接正式 R1 必须另行实现账号、权限、租约、正式 turn 分配、二进制帧、播放回报和重连，不通过字段重命名宣称完成。
