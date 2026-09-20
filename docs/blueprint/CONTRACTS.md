# 共享接口契约

版本 1.0 · 契约负责人 A0 · 修改必须包含消费者与测试影响

本文件规定开发 agent 必须共同遵守的语义。机器可读定义分别是 contracts/openapi.json、contracts/client-event.schema.json 和 contracts/server-event.schema.json。fixtures 是正反例，不是已实现的服务。正式代码应由这些契约生成类型或进行自动校验，不在各端手工维护互相不同的字段集。

## 1 标识与基本约定

- 全部公开资源 ID 使用 UUID 字符串，时间使用带时区的 RFC 3339 UTC 字符串。
- session_id 标识一个个人或直播会话。mode 一经创建固定为 personal 或 broadcast。
- session_epoch 为正整数，表示当前会话执行代次。在输入或播放设备接管、运行进程恢复、主控制连接恢复导致旧执行上下文失效时提升；普通观察者重连不提升。
- turn_id 由服务端生成，标识一轮被接受的输入及其回应。一个 session 最多一个活动 turn。
- stream_id 标识一条音频流。输出每个语音片段有自己的 stream_id，关联 turn_id 和 segment_index。
- 客户端 event_id 同时作为幂等键，重试必须复用。同一会话保留最近 10 分钟或 1000 个已处理键。
- seq 是服务端在同一 epoch 内单调递增的控制事件序号；client_seq 是单个客户端控制连接内的递增序号，二者不混用。
- generation、播放队列、动画队列均必须检查 epoch 和 turn_id，不能只按消息到达顺序处理。
- R1 支持的情绪标签为 neutral、happy、sad、surprised、thinking。基础动作标签为 none、nod、shake_head、wave，最终还需通过角色能力清单校验。

## 2 认证与设备权限

网页使用 HttpOnly、Secure、SameSite cookie；改变状态的请求校验来源和 CSRF。手机使用短期 bearer access token，refresh token 只保存在 Keychain 或 Android 系统安全存储。R1 建议 access token 15 分钟、refresh token 7 天且轮换并支持撤销。后台续期由原生引擎负责，不能依赖手机 UI 的 JavaScript 定时器。

控制与音频 WSS 的连接票据通过已认证 REST 请求取得，有效期 30 秒、单次使用，并绑定 session、device、channel 和权限。浏览器无法自定义握手 Authorization 时，可使用短期票据查询参数；代理和日志必须隐藏它。长期 token 不出现在 URL。

设备角色分为 controller、input、speaker、observer。controller 可操作属于该用户的 session；input 可提交当前租约下的音频；speaker 可获取并播放当前会话输出并回报进度；observer 只接收被授权状态。角色可以组合，但输入和播放分别只有一个租约持有者。租约每 10 秒续期、30 秒失效，服务端拒绝过期设备的数据。

OBS 显示凭据是可撤销的、限定 broadcast session 的 speaker 和 observer 权限。显示页面用 URL fragment 中的一次性引导码兑换短期凭据，兑换后清除 fragment。引导码有效期60秒，显示授权本身采用 ttl_seconds 指定的期限并支持撤销；二者过期时间分别返回。兑换后用绑定显示角色的 HttpOnly 刷新 cookie 支持页面刷新，不把长期 token 写入页面地址。OBS 不能取得 controller 或 input 权限。显示凭据失效时停止播放并显示断开状态。

## 3 REST 范围

业务路径以 /v1 开头，健康检查使用 /health，JSON 使用 snake_case。正常对象响应为业务对象；错误统一为 code、message、retryable、request_id。除登录、刷新和一次性显示凭据兑换外，创建和动作型 POST 使用 Idempotency-Key；不能因网络重试创建重复会话或重复触发开播。

| 方法与路径 | 作用 | 关键约束 |
|---|---|---|
| POST /v1/auth/login | 已存在拥有者账号登录 | 无公开注册；限速；密码不记录日志 |
| POST /v1/auth/refresh | 轮换访问凭据 | 手机原生引擎可调用 |
| POST /v1/auth/logout | 撤销登录与刷新凭据 | 对应原生会话停止 |
| POST /v1/devices | 注册设备和能力 | 归属当前用户 |
| GET /v1/characters | 读取角色配置清单 | 输出已授权的模型与音色配置 |
| POST /v1/sessions | 创建 personal 或 broadcast 会话 | mode 不可后改 |
| GET /v1/sessions/{session_id} | 读取完整快照 | 校验所有权或显示权限 |
| POST /v1/sessions/{session_id}/attach | 加入或接管输入和播放 | 显式 takeover；返回角色与新 epoch |
| POST /v1/sessions/{session_id}/tickets | 取得指定通道的 WSS 票据 | 一次性，短时有效 |
| POST /v1/sessions/{session_id}/end | 结束会话 | 重复调用安全 |
| GET /v1/sessions/{session_id}/turns | 游标分页读取历史 | 保留实际播出和中断标记 |
| POST /v1/sessions/{session_id}/display-links | 为 OBS 创建限定显示入口 | 只允许 broadcast 和 controller |
| DELETE /v1/display-links/{display_link_id} | 撤销显示入口 | 断开对应显示连接 |
| POST /v1/auth/display-exchange | 兑换 OBS 一次性引导码 | 权限上限为 speaker 和 observer |
| GET /v1/memories | 读取指定角色和 scope 的记忆 | 缺省不跨 scope |
| POST /v1/memories | 新建明确确认的记忆 | 保存来源和 scope |
| PATCH /v1/memories/{memory_id} | 修改或确认候选记忆 | 校验 owner，记录修改时间 |
| DELETE /v1/memories/{memory_id} | 删除记忆及检索可见性 | 正在生成的相关上下文作废或重建 |
| DELETE /v1/sessions/{session_id} | 删除会话与派生数据 | 先结束活动会话，按来源处理摘要和记忆 |
| POST /v1/live/connections | 建立直播连接 | 先验证实际授权，不把 room_id 当凭据 |
| GET /v1/live/connections/{connection_id} | 读取连接及权限状态 | 支持 connected、degraded、blocked |
| DELETE /v1/live/connections/{connection_id} | 关闭连接 | 清理平台心跳与后台任务 |
| GET /v1/usage | 查询用量和费用估算 | 标注估算依据和币种 |
| GET /health/live | 进程存活 | 不返回秘密信息 |
| GET /health/ready | 服务就绪 | 检查必需依赖，不因可选 provider 故障全站失效 |

接口返回 401 表示凭据不可用，403 表示资源或角色权限不足，409 表示租约或状态冲突，422 表示不合法输入，429 表示额度或速率限制，503 表示暂不可用。业务 code 是稳定枚举；UI 不直接展示供应商堆栈。

## 4 控制通道

连接路径为 /v1/sessions/{session_id}/control。每条文本帧是一个 JSON 对象，最大 64 KiB。客户端和服务端 schema 分开校验。初版协议严格拒绝不认识的事件类型和必填字段缺失，未来通过版本协商扩展。

### 4.1 客户端事件

每条消息包含 schema_version、type、event_id、session_id、session_epoch、device_id、client_seq、timestamp 和 payload。相关 turn_id 放在 payload 中，客户端不能自己分配服务端 turn_id。

| 事件 | payload 的关键字段 | 行为 |
|---|---|---|
| input.text.submit | text、source | 提交个人文字或主播输入 |
| input.audio.start | stream_id、sample_rate、channels、codec | 开始一个输入语音段，收到 accepted 后才发数据 |
| input.audio.end | stream_id、reason、last_frame_seq、total_samples | 声明输入段边界，收齐对应音频后触发最终识别 |
| turn.cancel | turn_id、reason | 停止当前回复；重复取消返回同一终态 |
| session.mute | muted | 原生端先停止收音发送，服务端同步状态 |
| session.end | reason | 停止整个会话 |
| playback.progress | turn_id、stream_id、segment_index、played_samples、status | 报告真实播放进度；只有 speaker 可发送 |
| lease.renew | roles | 当前租约续期；不改变设备归属 |
| session.resync | last_epoch、last_seq | 请求快照与可恢复的控制事件 |
| live.select | live_event_id | controller 选择已经接收的弹幕 |
| live.auto_reply | enabled | 启停自动回应 |

### 4.2 服务端事件

每条消息包含 schema_version、type、event_id、session_id、session_epoch、seq、timestamp、turn_id 和 payload。会话级事件 turn_id 为 null，回复级事件为有效 UUID。状态快照可包含 active_turn_id。

| 事件 | payload 的关键字段 | 行为 |
|---|---|---|
| session.snapshot | state、mode、muted、active_turn_id、input_device_id、speaker_device_id | 权威状态，用于首次连接和恢复 |
| input.audio.accepted | stream_id | 允许当前设备开始发送指定输入流 |
| input.transcript | text、is_final、stream_id | 临时文本或最终识别 |
| turn.started | source、input_text | 该轮正式被调度器接受 |
| assistant.text.delta | text | 临时生成文本；不能标记为已经播出 |
| assistant.segment | segment_index、stream_id、text、emotion、gesture、sample_rate、channels、codec | 不可变待播片段及对应音频流 |
| assistant.audio.end | stream_id、segment_index、total_samples | 该片段音频数据结束；不表示已经播放完 |
| turn.generation.completed | finish_reason | 生成与合成全部结束；等待播放回报 |
| turn.completed | spoken_text、completion_reason | 依据播放回报完成；中断情况用 cancelled |
| turn.cancelled | reason、spoken_text | 终止该轮并封存实际播出记录 |
| avatar.state | emotion、gesture、segment_index | 片段级表现；迟到和旧轮次状态丢弃 |
| live.queue | size、dropped_total、selected_event_id | 队列与选择状态 |
| connection.state | state、reason | degraded、reconnecting、connected 等状态 |
| error | code、message、retryable、related_event_id | 可关联失败请求 |
| command.ack | related_event_id、status、result_turn_id、lease_expires_at | 指令确认与幂等结果；非租约指令的到期时间为 null |
| turn.failed | code、message、spoken_text、retryable | 执行失败的回复终态，与请求级 error 区分 |

assistant.segment 必须早于该 stream 的任何输出二进制数据，且先通过控制通道发出。跨通道无法保证实际到达先后，客户端最多暂存 500 ms 或 64 KiB 未识别音频，等待对应 segment；超过限制请求 resync 并停止该流。禁止无元数据直接播放。

指令验证并执行状态改变后返回 command.ack；重复 event_id 返回 duplicate 及同一结果。输入文本入库后可以先确认入队，result_turn_id 在尚未调度时为 null。session.snapshot 在状态改变及恢复时都可发送。lease.renew 的确认包含新的 lease_expires_at。

输入结束和输出结束标记可能先于最后的二进制帧到达。接收方按声明的末帧序号或 total_samples 等待收齐，最多额外等待 500 ms；超时将该流标记为不完整并停止，不默默截断后假装成功。空输入流的 last_frame_seq 为 null、total_samples 为 0，正常输入流两者必须与实际累计值一致。

## 5 音频通道与二进制帧

连接路径为 /v1/sessions/{session_id}/audio。一帧二进制 WebSocket message 包含 64 字节固定头加 PCM payload。字段统一小端；UUID 使用 RFC 4122 原始 16 字节网络表示，而非平台 Guid 的混合端序。首版仅支持 codec pcm_s16le，单声道。所有整数都做长度和范围校验。

| 偏移 | 长度 | 内容 |
|---|---|---|
| 0 | 4 | ASCII AIC1 |
| 4 | 1 | 协议版本 1 |
| 5 | 1 | kind，1 为输入，2 为输出 |
| 6 | 1 | channels，固定 1 |
| 7 | 1 | flags，R1 固定 0 |
| 8 | 4 | session_epoch，uint32 |
| 12 | 4 | frame_seq，流内从 0 递增，uint32 |
| 16 | 4 | sample_rate，输入 16000，输出 24000 |
| 20 | 4 | payload_bytes，实际音频字节数 |
| 24 | 16 | stream_id |
| 40 | 16 | turn_id，输入为全零，输出为有效 UUID |
| 56 | 4 | segment_index，输入固定 0 |
| 60 | 4 | offset_samples，流内累计样本偏移 |
| 64 | 可变 | PCM16LE payload，必须为偶数字节 |

正常帧为 100 ms：输入 3200 字节，输出 4800 字节，末帧可以更短。最大 payload 为 9600 字节。单个输入语音段最长 30 秒，起音缓冲建议 300 ms、句末静音阈值初值 700 ms，最终阈值由中文测试调优。

输入只接受当前 input 设备、当前 epoch、已经 accepted 的 stream。输出只发给 speaker 租约持有者。输出音频不默认广播到所有观察者。frame_seq 或 offset_samples 不连续时报告流损坏，不悄悄拼接出错音频。帧序号溢出前结束并重建流。

单个播放端待播音频上限为 2 秒，服务端未播合成缓冲上限为 10 秒。超过上限暂停上游消费或合成；无法施加背压的 provider 则取消剩余生成并给出明确状态。不得静默丢弃音频中间帧后继续播。对每条控制消息也设置大小、超时和频率限制。

## 6 状态与取消语义

session 状态为 idle、listening、thinking、speaking、muted、interrupted、reconnecting、ended。turn 状态为 queued、generating、synthesizing、playing、completed、cancelled、failed；生成与播放可重叠，实现可用独立子状态，但对外终态只有一个。

终态 turn 不得再次输出可播放片段。cancel 到达后，即使 provider 无法真正停止计算，服务端仍关闭消费通路并丢弃迟到结果。客户端保留取消 turn 集合直到 epoch 改变，以阻止旧帧重新进入播放队列。

输出播放事件不只用于界面。服务端根据 playback.progress 记录哪些片段已经播完，截断未播内容，再用于下一轮上下文。音频中途停止时，R1 可以保守地只记录完全播完的片段，保留部分播放的时间标记，不能声称精确知道每一个字的播出位置。

同一 speaker 在 30 秒租约过期后离线，服务端取消活动 turn 并暂停自动发言。OBS 刷新页面必须先取得新快照和租约；不重播旧轮次。数据持久化成功后才确认输入已接受，避免重连产生重复用户消息。

## 7 断线与恢复

控制心跳建议 10 秒，超时 30 秒。原生引擎按 1、2、4、8 秒增加间隔重试，最高 15 秒并加入抖动；尊重服务器限速。重连先刷新凭据并取得新票据，再请求 snapshot。没有取得当前 epoch 和租约之前不发送麦克风音频。

同一 epoch 可以补发保留窗口内的控制事件，客户端按 event_id 去重；已经播放的音频不补发。进程重启或主执行连接恢复后会取消旧 turn、提升 epoch并返回完整 snapshot。UI 清空临时文本与待播内容，再订阅新事件。显式结束或权限撤销后不自动重连。

## 8 云服务适配器

ASRAdapter 接收标准音频段及取消信号，输出临时和最终识别、用量和错误。DialogueAdapter 接收经过 scope 过滤的上下文，输出文字增量及可封存的 SpeechSegment。TTSAdapter 接收封存文本、音色和语速，输出标准音频块和总样本数。

每个适配器提供 capabilities、health、timeout、cancel、usage 五项能力。不能取消的 provider 必须在 capabilities 中声明，调用端依然保证停止消费。所有供应商异常转换为 provider_unavailable、provider_timeout、provider_rate_limited、budget_exceeded 等稳定错误。

供应商支持结构化流时可输出句级情绪；不支持时由轻量规则产生有限标签并默认 neutral，不为每个字或每帧另调大模型。先拿到完整短句再调用 TTS，已经进入播放的片段不修改。

## 9 角色清单与原生桥接

CharacterManifest 至少包含 id、version、display_name、model_entry、asset_base、checksum、license_reference、lip_sync_parameters、expression_map、motion_map 和 voice_profile_id。远程素材下载校验 checksum，模型加载失败显示明确占位状态。用户导入的路径在客户端沙箱或服务端资源目录内解析。

原生语音接口由 packages/mobile-bridge 统一定义：startSession、mute、cancelTurn、endSession、getSnapshot 和 subscribeState。startSession 必须从允许启动麦克风的前台交互触发。原生引擎拥有连接状态、凭据续期与取消集合。

viewer 桥接只有 loadCharacter、setPlaybackState、setAudioLevel、applyExpression、applyMotion、suspend 和 resume。audio level 来源于原生实际播放回调，前台最多每秒 30 次；后台完全停止转发。每条桥接消息带 schema_version、session_epoch 和对应 turn_id，不允许任意 JavaScript 代码字符串作为业务命令。

## 10 直播事件适配

LiveEvent 统一包含 platform、connection_id、platform_event_id、received_at、occurred_at、type、viewer_id、display_name、text 和 metadata。type 首版为 comment、connection、optional_gift。平台不提供可靠 event_id 时，适配器使用连接、用户、消息时间与正文生成去重键，并说明碰撞风险。

LiveAdapter 暴露 connect、disconnect、events、capabilities、health。capabilities 必须说明能否获得普通评论、特殊指令评论、用户标识与礼物信息，不能把缺字段伪造成完整能力。模拟适配器标记 platform=mock，界面显示模拟状态，验收不能算真实直播接入。

## 11 契约变更

新增事件或字段需要修改版本与正反例，所有消费者都更新后才合并。允许向后兼容的增量应在协议能力协商后启用；R1 不依赖客户端“猜着忽略”。破坏性修改提升主版本并安排迁移。schema、固定样例、文档、mock 和双方代码必须同时更新。
