# U01-00 · C# 模块接口与接线提案

状态：**提交 A0 设计审阅，尚未冻结为共享 API。** 本文件提供实际 C# 签名建议和生命周期约束，不代表这些模块已经实现或验收。依据为 [ADR15](../../../adr/0015-unity-client-and-design-only-a0.md)、[U01 任务书](../../../tasks/U01/TASKBOOK.md)、[U01 接口](../../../tasks/U01/INTERFACES.md)及[验收表](../../../tasks/U01/ACCEPTANCE.md)。本文不修改正式 `contracts/`、R1 协议或 U01 HTTP 字段。

U01-00 的最小场景只验证工程、SDK、资源导入和 Windows Player 渲染。以下接口供后续任务接线；不得因有接口类型、演示组件或最小 Player，就宣布 U-G0 或 U01 完成。A0 审阅后，由集成 owner 单独写入 `Assets/Companion/Runtime/Contracts/`，消费者使用同一版本。

## 1. 目录、程序集与所有权

| 程序集建议 / 目录（相对 `apps/unity/Assets/Companion/`） | 写入 owner | 依赖与交付边界 |
|---|---|---|
| `Companion.Contracts` / `Runtime/Contracts/` | 集成 owner，经 A0 评审 | 纯 C# 边界；不引用 UnityEngine、Cubism、HTTP 客户端或 provider SDK |
| `Companion.Avatar` / `Runtime/Avatar/`、`Prefabs/Avatar/` | U01-01 | 引用 Contracts 与 Cubism；交角色 prefab、参数能力清单及适配组件 |
| `Companion.UI` / `Runtime/UI/`、`Prefabs/UI/` | U01-02 | 引用 Contracts；只发 Session 命令并显示快照，不能调用 Transport、Audio 或 Cubism 实现 |
| `Companion.History` / `Runtime/Session/History/` | U01-02 | 引用 Contracts；独立程序集避免与 Session 主实现形成环依赖 |
| `Companion.Session` / `Runtime/Session/`（排除 History） | U01-04 | 引用 Contracts；唯一拥有会话状态、取消代数、草稿版本及当前操作 |
| `Companion.Transport` / `Runtime/Transport/` | U01-04 | 引用 Contracts；预览 HTTP、NDJSON、身份与协议校验；与 Audio 的关系通过接口注入 |
| `Companion.Audio` / `Runtime/Audio/` | U01-04 | 引用 Contracts；WAV 校验、播放、录音和有界采样，不依赖 Avatar 或 UI |
| `Companion.Composition` / `Runtime/Composition/`、`Scenes/` | U01-05 / 集成 owner | 唯一组合入口，注入上述接口并接线；其他 agent 交 prefab 与接线说明，不修改总场景 |
| `Packages/`、`ProjectSettings/`、共享 `.meta`、构建工具 | 集成 owner | 后续 agent 不自行升级 SDK/包或重写共享配置 |

每个模块保留自己的 `.asmdef` 和 `.meta`。模块 owner 可以在其获准目录内实现受控假服务来独立验证；假服务显式标记 fixture，不能作为真实后台或云能力证据。正式 R1 迁移时替换 Transport 及音频实现，不把 U01 `generation` 更名后当成 `session_epoch`。

## 2. 共同类型与线程规则

以下是签名草案；DTO 的不可变构造、相等比较、序列化映射和完整成员定义由共享 Contracts 落地提交提供。代码段并非可直接复制后编译的完整实现。公开命名空间建议为 `AICompanion.Preview.Contracts`。

```csharp
public readonly struct OperationKey
{
    public Guid RequestId { get; }
    public uint Generation { get; }
    public Guid ConversationId { get; }
}

public readonly struct TurnKey
{
    public OperationKey Operation { get; }
    public Guid TurnId { get; } // 仅在 turn.accepted 后由后台分配
}

public readonly struct DraftKey
{
    public OperationKey Operation { get; }
    public ulong DraftRevision { get; }
}

public enum SessionPhase
{
    Offline, Ready, Recording, Transcribing, Thinking,
    PreparingSpeech, Speaking, Stopping, Error
}
public enum PreviewMode { Unknown, Fixture, Cloud }
public enum DeliveryKind { Text, Audio }
public enum DeliveryState
{
    Generating, Generated, Displayed, Played, Interrupted, Failed
}
public enum Emotion { Neutral, Happy, Sad, Surprised, Thinking }
public enum StopReason
{
    User, Replaced, NewConversation, SelectConversation,
    DeleteHistory, WindowClosing, Failure
}
public enum PlaybackEndReason { Completed, Stopped, Failed }
```

- `RequestId` 是客户端新建的 UUID v4；一项操作只使用一个 ID。取消发送原操作的 ID 和原 generation，不发送停止后递增的新 generation。`ConversationId` 只关联本机历史，不能当作服务器账号或持久会话权限。
- `Generation` 由 Session 独占写入，每次开始和停止递增。达到 `uint.MaxValue` 前结束并重建运行态，不允许回绕。公共快照可用可空的 OperationKey / TurnKey 表达当前无活动请求，不用全零 UUID 冒充有效 ID。
- `DraftRevision` 是本地草稿保护，编辑输入、切换会话和开始新录音均递增；不会新增到 ASR 的 HTTP 请求体或请求头。ASR 结果需同时匹配 OperationKey 与保存的 DraftRevision。
- `PreviewError` 建议字段为 `Code`、脱敏的 `Message`、`Retryable`、可空 OperationKey；错误不可携带 token、原始录音、provider 请求体或栈到 UI。
- DTO 中字符串保留协议定义的 Unicode 限制；网络字节限制与字符数限制分别校验。枚举到线协议的映射显式定义，不依赖 `Enum.ToString()`。
- Session 命令和 UI/Avatar/Playback 的公开事件回调只在 Unity 主线程触发。Transport 的流事件处理器可以在工作线程执行，必须经过有界、可取消的主线程调度后进入 Session；排队和执行时均复核 OperationKey。
- 不在音频线程访问 GameObject、历史文件或网络，不在该线程逐块创建任意长度数组。音频与主线程之间采用固定容量队列/缓冲；停播的零振幅信号必须能够覆盖此前尚未消费的非零采样。

## 3. Session：命令与唯一状态快照

```csharp
public interface ISessionController : IDisposable
{
    SessionSnapshot Snapshot { get; }
    event Action<SessionSnapshot> SnapshotChanged;
    event Action<DraftUpdate> DraftUpdated;

    CommandReceipt SubmitText(SubmitTextCommand command);
    CommandReceipt BeginRecording(string deviceId);
    CommandReceipt EndRecording();
    void NotifyDraftEdited(string text);
    void Cancel(StopReason reason);
    Task NewConversationAsync(CancellationToken cancellationToken);
    Task SelectConversationAsync(Guid conversationId,
        CancellationToken cancellationToken);
    Task DeleteConversationAsync(Guid conversationId,
        CancellationToken cancellationToken);
    Task ClearHistoryAsync(CancellationToken cancellationToken);
}
```

`SubmitTextCommand` 只含用户文字、角色 ID、已公布音色 ID 和是否生成音频，不允许任意 endpoint、system prompt、文件路径或密钥。`CommandReceipt` 包含是否接受、OperationKey 和可选错误；同步命令完成校验和操作登记即返回，后台执行通过快照发布结果。连续双击同一尚未变更的发送操作不能各发一次 POST；用户明确的新发送才建立新操作并先停止旧操作。

`SessionSnapshot` 至少包含 Phase、可空 Operation/Turn、Mode、Error、ConversationId、DraftRevision，以及供 UI 显示的完整/临时文字和音频状态。Mode 来自 capabilities / `turn.accepted`，不能由假服务或 UI 宣称为 cloud；chat/tts/asr 模式分别展示，避免系统 TTS 被标为云端 TTS。UI 不再维护另一个 Thinking/Speaking 状态机。

`DraftUpdate` 携带 DraftKey 与识别文本，只有仍有效的结果可进入编辑框；不会直接调用 SubmitText。Draft 更新仍须通过 Session 提交，不能由网络回调直接改输入框。

所有停止类操作先执行共同的同步本地清理段，再进行可等待的存储或网络操作：

1. Audio.Stop 撤销可播放操作、静音并清空队列；Avatar 立即归零口型并清理当前讲话动作。
2. Session 撤销旧 OperationKey，递增 generation，使已经排队的旧文本、音频、动作和完成事件失效。
3. 取消旧操作的 CancellationTokenSource，Abort 录音并释放设备；封存历史为 Interrupted/Failed 等真实状态。
4. 使用捕获的旧 OperationKey，以新的短时限 CancellationToken 尽力发送后台 cancel。不能复用已取消的业务 token，否则取消 HTTP 请求可能根本不发送。

失焦、鼠标移出按住说话按钮、Esc 和关闭窗口均不依赖 key-up 才释放录音。失焦后的播放策略可由桌面设置决定；关闭窗口的本地清理同步完成，不能依赖退出后某个异步 continuation 才停止设备。

## 4. Transport：带取消的 typed 结果

```csharp
public interface IConversationGateway : IDisposable
{
    Task<PreviewCapabilities> GetCapabilitiesAsync(
        CancellationToken cancellationToken);

    Task ConsumeTurnAsync(TurnSubmission request,
        Func<PreviewEvent, CancellationToken, Task> onEvent,
        CancellationToken cancellationToken);

    Task<CancelResult> CancelAsync(OperationKey operation,
        CancellationToken cancellationToken);

    Task<ValidatedPcm> DownloadAudioAsync(AudioReadyDescriptor audio,
        CancellationToken cancellationToken);

    Task<TranscriptionResult> TranscribeAsync(OperationKey operation,
        CapturedPcm recording, CancellationToken cancellationToken);
}
```

`TurnSubmission` 逐项对应 `POST /turns`，`PreviewEvent` 携带 protocol、OperationKey、TurnKey、seq 与明确的 payload 类型。类型至少包括 Accepted、TextDelta、TextCompleted、AudioReady、AudioSkipped、GenerationCompleted、Cancelled、Error；不能以一个任意字典绕过字段、序号或关联 ID 校验。

ConsumeTurnAsync 按读取顺序 await 每次 onEvent，形成有界背压。它完成仅表示流已解析并校验终态/EOF；不意味着播放器完成。第一条必须 accepted，之后 OperationKey/TurnId 恒定；重复 seq 内容相同可丢弃，内容不同或缺号导致协议错误。网络 read 边界不能代替 NDJSON 行边界；EOF 缺少合法终态也失败。

`AudioReadyDescriptor` 含 TurnKey、相对 Path、SampleRate、Channels、Codec、TotalSamples。下载器只接受当前 turn 的准确相对 WAV 路径，不接受远端 URL、重定向至非 loopback 或路径穿越。WAV 校验完整遍历 RIFF chunk，检查 PCM16LE / mono / 24000 Hz、样本数、8 MiB / 120 秒上限后才生成 ValidatedPcm；不假设 44 字节头。410 明确返回资源过期，不自动重新生成或计费。

`CapturedPcm` 保留实际设备采集格式；上传适配为 16 kHz / mono / PCM16LE，最大 30 秒 / 1 MiB。TranscriptionResult 保留 OperationKey、Text、Mode，由 Session 加上原 DraftKey 复核后发布。后台仍重新校验音频与大小。

GetCapabilitiesAsync、下载和转写均支持取消及独立超时；POST 不因超时或断流自动重试。`CancelResult` 必须对应原 RequestId。任何 fixture/cloud 模式切换必须显式配置，不能在云缺 key 时静默退回固定回答。

本机运行配置位置提案为 `%LOCALAPPDATA%/AICompanion/UnityPreview/runtime.json`，仅当前用户可读，含配置版本、固定 loopback 地址、协议和短期令牌；launcher 每次重启刷新令牌。具体字段和位置须由 U01-00/03 与 A0 共同确认，当前最小场景无需创建或消费该文件。令牌不进入源码、启动参数、报告或日志；客户端拒绝除 `127.0.0.1:8000` 外的服务地址。

## 5. Audio：输出资格、真实播放与采样

```csharp
public interface IAudioPlayer : IDisposable
{
    PlaybackSnapshot Snapshot { get; }
    event Action<PlaybackStarted> PlaybackStarted;
    event Action<PlaybackEnded> PlaybackEnded;
    event Action<PlaybackProgress> ProgressChanged;
    event Action<AudioLevelSample> PostVolumeLevel;

    void Arm(OperationKey operation);
    PlayResult Play(ValidatedPcm pcm, TurnKey turn);
    void Stop(StopReason reason);
    void SetVolume(float volume01);
}

public interface IMicrophoneCapture : IDisposable
{
    Task<IReadOnlyList<MicrophoneDevice>> GetDevicesAsync(
        CancellationToken cancellationToken);
    Task<CaptureStarted> BeginAsync(DraftKey draft, string deviceId,
        CancellationToken cancellationToken);
    Task<CaptureResult> EndAsync(DraftKey draft,
        CancellationToken cancellationToken);
    void Abort();
}
```

Arm 仅由 Session 为当前操作调用。Play 同时检查 TurnKey.Operation 等于已授权操作、PCM 已校验及没有其他活动播放；失败返回显式 PlayResult，不暗中覆盖或叠播。ValidatedPcm 是只能由音频校验器构造的不可变有界缓冲：Play 成功即转移所有权给播放器，播放终止后释放；Play 拒绝时仍由调用方释放。CancellationToken 只是清理手段，不能替代进入设备前的身份检查。

Stop 是同步本机操作，不访问网络，不等待 provider；先撤销 Arm，再静音、清空待播 PCM，发布零振幅并结束当前输出。其结束通知排入主线程事件队列，不同步重入尚未完成清理的 Session；Session 撤销代数后才消费该通知。旧 Play 请求即使随后完成下载也被拒绝。音量范围有限且拒绝 NaN/Infinity，0 表示静音。完成、失败、Stop、Dispose 都释放占用和缓冲，Stopped 不生成 Completed。

PlaybackStarted/Ended/Progress 和 AudioLevelSample 均携带 TurnKey。Started 以实际输出路径开始消费样本为依据，不在调用 AudioSource.Play、收到 text.completed 或下载完成时提前发出。Ended 原因区分 Completed / Stopped / Failed；每个被接受的播放只有一个结束事件。Progress / Snapshot 含 PlayedSamples、TotalSamples、源采样率与单调时钟时间戳；PlayedSamples 表示输出路径已消费的源样本数，不能用下载字节数、计划调度样本数或墙上时间估算。

AudioLevelSample 建议包含 TurnKey、Level01、SampleStart、SampleCount、MonotonicTicks。Level01 来自播放器实际输出块，在应用音量/静音增益后计算短窗口 RMS，再平滑和限幅；数据不取自原始 WAV 整体能量、文本长度或动画计时器。后台/音频线程只写有界交换缓冲，主线程转发最新有效样本。停止时立即归零，不能等待平滑衰减；静音片段也必须发送零值。

上述测量边界位于应用输出路径，操作系统音量、混音和设备缓冲仍可能改变最终可听结果。UA03 要用带系统输出的录屏核对，UA05 要用同一单调时钟记录停止输入与最后非零输出块，注明系统缓冲；具备 loopback 时另测可听尾音。调用 Stop 的日志本身不证明 P95≤200 ms。

Begin / End / Abort 覆盖设备拔出、权限、空录音和超时。End 在成功、失败或取消时都释放麦克风；Abort 同步阻止后续采集并释放，默认不写原始音频到磁盘。CaptureResult 携带 DraftKey 和真实格式，供 Session 在 ASR 前后复核身份。最多一个输入设备、一个播放设备；更换设备需显式动作。

## 6. Avatar：只呈现，不拥有 AI 或音频

```csharp
public interface IAvatarPresenter : IDisposable
{
    AvatarCapabilities Capabilities { get; }
    Task<AvatarLoadResult> LoadCharacterAsync(Guid loadRequestId,
        string characterId, CancellationToken cancellationToken);
    void BindOperation(OperationKey? operation);
    void SetPlaybackState(TurnKey turn, AvatarPlaybackState state);
    void SetAudioLevel(AudioLevelSample sample);
    AvatarActionResult ApplyExpression(AvatarActionContext context,
        Emotion emotion);
    AvatarActionResult ApplyMotion(AvatarActionContext context,
        string motionId);
    void Suspend();
    void Resume();
}
```

AvatarLoadResult 返回 loadRequestId、资源标识、能力和失败信息；最新加载操作替换旧加载后，旧结果不能替换场景中的模型。BindOperation(null)、Suspend、Dispose 必须立即闭嘴并清理旧动作；BindOperation(newKey) 撤销此前所有回调资格。SetAudioLevel/SetPlaybackState/动作检查相同 OperationKey；一个旧 Level 样本不能使停止后的模型重新张嘴。

AvatarActionContext 含 OperationKey 和可空 TurnId。本地“招呼”由 Session 分配本地 OperationKey，不伪造服务端 turn；它只使用模型能力清单公布的 motionId。云回复的 emotion 先由 Session 缓存，到对应 PlaybackStarted 时再调用 ApplyExpression / 讲话动作；generate_audio=false 的完整显示路径可应用非讲话表情，但不伪造 Speaking。U01 HTTP 目前没有任意 motion 事件，此提案不向线协议增加动作字段。

AvatarCapabilities 列明模型实际参数、是否支持口型/眨眼/呼吸/视线、支持表情与 motionId；AvatarActionResult 区分 Applied / Unsupported / StaleOperation / Unavailable，并说明中性降级。缺参数只记录一次诊断。Avatar 不发 AI 请求、不下载服务 WAV、不再播放一份 Cubism motion 音频，也不从 Time.time 或 TTS 事件合成“口型”。

## 7. History：交付状态与防止删除后复活

```csharp
public interface IHistoryStore : IDisposable
{
    Task<ConversationHistory> CreateAsync(Guid conversationId,
        CancellationToken cancellationToken);
    Task<ConversationHistory> LoadAsync(Guid conversationId,
        CancellationToken cancellationToken);
    Task<HistoryWriteResult> AppendOrUpdateAsync(
        HistoryWriteToken writeToken, HistoryTurn turn,
        CancellationToken cancellationToken);
    Task<HistoryExport> ExportAsync(Guid conversationId,
        CancellationToken cancellationToken);
    Task DeleteConversationAsync(Guid conversationId,
        CancellationToken cancellationToken);
    Task ClearAllAsync(CancellationToken cancellationToken);
}
```

ConversationHistory 含 SchemaVersion、本机会话 ID、HistoryWriteToken 和版本化记录。HistoryTurn 保留 OperationKey / 可空 TurnId、角色、文本、DeliveryKind、DeliveryState、PlayedSamples/TotalSamples、更新时间及必要模式信息；不存原始录音、Bearer 令牌或 provider 密钥。历史配置与对话分文件存于应用数据目录，支持原子写、损坏恢复和既定容量。

HistoryWriteToken 是本地存储修订号（会话 ID + 存储代数）的封装，不是 R1 的设备租约。Delete/Clear 先撤销旧写入资格，再排队执行持久化删除；AppendOrUpdate 在真正提交写入时仍检查 token，防止“检查通过 → 异步删除 → 旧写回”复活数据。只有 CreateAsync 能显式新建会话；对已删除 ID 的迟到 upsert 拒绝，不隐式重建。所有写操作串行化，Session 也先停止当前操作。

生成、显示、下载、播放分别登记：generation.completed 仅表示生成结束；音频只有实际 Completed 才标记 Played；中途停止是 Interrupted。文字模式完整显示且收到 generation.completed 才标记 Displayed。TTS 失败保留已生成文字并记为 Failed，附语音失败原因，不能写成 AudioSkipped 或 Played。上下文只选择音频 Played 或文字 Displayed 的完整 assistant 回复；Interrupted 不整篇回送到下一轮。

## 8. 组合入口与接线顺序

1. Composition 加载资源清单与角色 prefab，建立明确的演示/fixture 标记。首次启动不录音、不发付费调用。
2. 注入 IHistoryStore、IMicrophoneCapture、IAudioPlayer、IConversationGateway、IAvatarPresenter 到 Session；UI 只拿 ISessionController。
3. Session 接受新操作时停止旧操作，分配新 OperationKey，Arm Audio 并 BindOperation Avatar。网络事件回到主线程后再次验 ID、generation、会话和终态。
4. text.completed 缓存完整文字和 emotion；audio.ready 完成下载和严格校验后，由 Session 再次验资格并调用 Play。下载器不直接播放。
5. PlaybackStarted → Session 更新 Speaking → Avatar 表情与讲话动作；PostVolumeLevel → Session/组合层校验身份 → Avatar.SetAudioLevel；PlaybackEnded → Session 决定 Ready/错误及 History 终态。
6. 若播放先结束而 generation.completed 尚未到达，保留实际播放结果，但整体请求直到生成终态校验通过才成功完成；若生成先结束，则等实际播放完成。EOF 或关联错误按协议失败处理，不能提前显示“已经说完”。
7. 所有停止入口复用同一同步清理路径，撤销后先挡住旧事件，再发尽力取消；卸载时解绑事件并 Dispose。模块 prefab 不自行寻找其他模块单例来绕过注入。

## 9. A0 需审阅的提案与验收边界

请求 A0 确认上述 API 形状、PCM 所有权、事件线程、HistoryWriteToken、主线程有界调度以及本机运行配置候选路径。它们是实现层提案，不要求变更 U01 HTTP 字段；如审阅认为需要新增线协议字段，必须另行同步 INTERFACES、U01-03 schema/fixtures 和消费者测试后再实施。

| 验证点 | U01-00 可以提供的证据 | 后续仍需完成 |
|---|---|---|
| UA01 | 固定候选版本、SDK/Core/模型/字体来源与哈希、最小源码/Player/构建日志、干净工作目录构建 | A0 对同 SHA 证据审阅；未执行的复现不能标为 passed |
| UA02 | `.model3.json` 对应真实 Player 画面、材质/遮罩/排序与运行日志；已执行的时长与行为逐项登记 | 10 分钟稳定性、眨眼/呼吸/视线/招呼及完整能力降级由 U01-01 / QA 补齐；最小场景截图不等于整项通过 |
| UA03/UA05 | 本文约束实际输出采样、先静音和迟到数据隔离 | U01-04 音频实现、U01-01 口型接线；含声音录屏、20 次播放停止与生成/下载各 10 次取消及 P95 |
| UA04/UA07 | 模块边界和历史语义建议 | U01-02 中文 IME、DPI、键盘、历史持久化、坏数据和删除竞争实测 |
| U-G0 | U01-00 提供后续共同工程起点 | UA01/02/03/04/05/12 全部所需证据及阶段集成、QA、A0 验收；不由 U01-00 单独宣布完成 |
| U-G1/U-G2 | 无需云密钥即可完成工程与提案 | 真云、真实麦克风、ASR、闭环与时延另外验收；缺资源时明确 not_run / awaiting_external |

后续 agent 开工必须引用 A0 通过的 U01-00 精确提交、资源与包锁及最终接口修订；不能只引用本文的草案名。本文自身只证明边界已提出，不能用它替代可执行接口、模块实现或实际 Player 证据。
