# T04 离线 mock

这是 **MOCK / 离线协议测试服务**，不是产品后台、真实 AI、真实语音识别或直播平台。使用冻结 R1 `schema_version=1.0`、64 字节 AIC1 头和 PCM16LE，不是 U01 `unity-preview/1` 的整段 WAV 接口。不会调用外网、读取密钥、启动数据库、采集麦克风或自动播放声音。

## 启动与验证

从仓库根目录、固定开发环境运行：

```powershell
uv sync --locked
uv run python tools/run_mock.py
# 默认 http://127.0.0.1:8765；另一个终端：
uv run python tools/smoke_mock.py
uv run python -m pytest -o "pythonpath=tests services/api" tests/mocks
```

启动器固定绑定回环地址、一个 worker；`Ctrl+C` 结束。服务只保存当前进程内存，重启后必须重新 bootstrap，不声称具备持久化恢复。不要暴露到公网：`/mock/*` 是无生产认证的本机测试控制面，可以创建身份、授予角色、注入故障。

`GET /health/live` 返回 `{"status":"ok","mode":"mock","scope":"T04 offline R1 fixtures"}`，只说明 mock 进程存活。

## 最小客户端顺序

1. `POST /mock/sessions`，JSON `{"mode":"personal","epoch":1}`。得到 `session_id`、`device_id`、`session_epoch`、`control_ticket`、`audio_ticket`、`control_path`、`audio_path`。默认设备有 controller/input/speaker/observer 角色。
2. 分别连接 `ws://127.0.0.1:8765{control_path}?ticket={control_ticket}` 和相应 audio URL。票据有效 30 秒、单次使用，绑定会话/设备/通道。控制连接立即收到 `session.snapshot`；observer 没有音频权限。
3. 按下列格式发送控制帧。用实际返回值替换 UUID；新意图生成新 event_id，重试复用；`client_seq` 每个新控制连接从 1 递增，timestamp 使用 UTC。

```json
{
  "schema_version": "1.0",
  "type": "input.text.submit",
  "event_id": "33333333-3333-4333-8333-333333333333",
  "session_id": "11111111-1111-4111-8111-111111111111",
  "session_epoch": 1,
  "device_id": "22222222-2222-4222-8222-222222222222",
  "client_seq": 1,
  "timestamp": "2026-09-20T00:00:00Z",
  "payload": {"text": "你好", "source": "user"}
}
```

4. 正常回复包含 `turn.started`、`command.ack`、文字/片段/角色事件、三个二进制音频帧、`assistant.audio.end`、`turn.generation.completed`。故障注入可改变到达时机。生成完成仍保留活动 turn，`spoken_text` 为空，不能当作已经播放。
5. speaker 测试消费者发送 `playback.progress`，包含匹配的 turn/stream、`segment_index=0`、累计 `played_samples`、`status`。只有 `status=completed` 且累计 7200 样本才在生成结束后发 `turn.completed`。`playing` 即使报满样本也不会完成。测试的播放回报是合成输入，不证明扬声器真实播放。
6. `turn.cancel` 随时从同一控制连接提交。生成使用独立异步任务，延迟不阻塞取消；输出前检查 turn/epoch，终态阻止待发送旧文字、音频、动作。部分播放不会假称整句已播出。

输入顺序为 `input.audio.start` → `input.audio.accepted` → audio 通道 kind=1、16000 Hz PCM → `input.audio.end`。结束标记可先于末帧到达，最多等待 500 ms；序号/样本数不一致会报错。输入最多 30 秒，仅累计样本计数，不存储上传录音。转写固定标记 `[MOCK ASR]`。可以只连 control 测取消；实际发送输出时无 speaker audio 会取消，不会假装完成。

## 测试控制面

以下路由仅属于 T04，不加入生产 `contracts/`：

| 方法与路径 | 作用 |
|---|---|
| `POST /mock/sessions` | 创建 personal/broadcast 会话；epoch≥2 可用于旧代次测试 |
| `GET /mock/sessions/{id}` | mock 快照、turn 终态、计数、直播队列；不返回上传音频 |
| `POST /mock/sessions/{id}/devices` | `{"roles":["observer"]}` 添加设备；input/speaker 已占用时必须显式 `takeover:true`，取消旧 turn 并提高 epoch |
| `POST /mock/sessions/{id}/tickets` | `{"device_id":"UUID","channel":"control"}` 或 audio，取得新的单次票据 |
| `POST /mock/sessions/{id}/faults` | 替换下一轮配置，缺省字段恢复默认值，不改变已启动生成 |
| `POST /mock/sessions/{id}/disconnect` | 断开会话、取消活动 turn；新主控制连接提高 epoch、返回快照，不重播音频 |
| `POST /mock/sessions/{id}/live` | 仅 broadcast；注入 `{"text":"模拟评论","event_id":"可选稳定ID"}`，事件始终 `platform=mock` |
| `GET /mock/assets/character-placeholder.svg` | 自制、明确标记的静态占位，没有 Live2D、动作或嘴型能力 |

所有 `/v1/*` **HTTP REST** 路由返回 501。只实现 `/v1/sessions/{id}/control`、`/audio` 两个 WebSocket 测试通道，不伪装生产登录、refresh、REST 建会话、历史数据库或模型服务。

故障配置示例：

```json
{
  "generation_delay_ms": 1000,
  "control_delay_ms": 300,
  "audio_delay_ms": 100,
  "frame_interval_ms": 5,
  "duplicate_events": true,
  "stale_epoch": false,
  "duplicate_audio_frames": false,
  "disconnect_after_frames": null
}
```

- generation/control/audio 延迟 0–5000 ms，frame 间隔 0–1000 ms。独立通道交付延迟模拟“音频先到、元数据后到”；正常零延迟路径先发 segment。消费者按 R1 有界等待元数据。
- `duplicate_events` 重复同一个 `assistant.text.delta`，event_id/seq 相同，消费者应去重。
- `stale_epoch` 将音频帧头改为当前 epoch−1，要求 epoch≥2；这是主动产生的坏测试数据，消费者必须拒绝。
- `duplicate_audio_frames` 重复同一二进制帧，消费者应拒绝重复序号。
- `disconnect_after_frames=1..3` 在对应帧后断线。恢复先取新票据，新主控制连接收到新 epoch；普通 observer 重连不提高 epoch。
- `turn.cancel` 就是取消注入入口，覆盖生成中、元数据后音频前、播放中，不另设假取消开关。

## 约束与边界

严格校验原 schema、UUID、带时区时间、类型、额外字段和 64 KiB 上限；拒绝重复 JSON 键、非有限数值、二进制控制帧。票据绑定设备，控制/输入/播放分别检查角色。input/speaker 各只有一个持有者，租约 30 秒；`lease.renew` 续期，过期停止对应通路并清理快照。这是本机测试身份，不代表生产跨账号授权完成。

静音停止服务端接受输入，已有输出可继续；结束取消 turn 并清理输入。event_id 窗口最多 1000 条/10 分钟，相同意图返回 duplicate 及原 result_turn_id，冲突失败。client_seq 在新控制连接重置，幂等窗口保留。uint32 epoch 耗尽时结束会话、要求新建，不能溢出。

会话最多 32 个、每会话 16 个设备、100 条 turn 诊断、1000 个输入 stream；每设备每秒最多 100 条控制指令。控制广播并行发送，每次出站发送/关闭有 100 ms 测试超时，移除卡住的连接；音频发送也有界。阻塞 socket 单测证明不会无限占用会话锁，不作为真实慢网性能测量。

每轮只有 300 ms 音频，低于 2 秒待播/10 秒未播缓冲限制，不实现无限生成或生产背压算法。PCM 由 `server.PCM` 的整数公式生成：24 kHz、16-bit、小端、单声道、500 Hz 方波、幅值 ±1200，共 7200 样本，三个 100 ms 帧。它不是 TTS 或录音；SVG 也是自制占位，无外部 SDK/模型。

直播 mock 支持普通评论、合成 viewer ID，不宣称礼物或特殊指令能力；队列≤50、30 秒过期、去重。`live.select` 选队列事件；`live.auto_reply` 仅对开关启用期间、新到且当前空闲的评论启动固定回复，忙时保留队列，不实现生产优先级或自动排空调度。

未覆盖：生产认证、refresh、票据 REST、持久化成功再确认、完整 REST、数据库恢复、全部供应商错误、生产心跳、真实音频回调、Unity 接线、移动后台、Live2D、真实云服务和官方直播授权。断线恢复只针对同进程仍保留的 mock 会话；真实进程重启丢失会话。T04 不能代替 AC10–AC15 真机/真实平台证据。
