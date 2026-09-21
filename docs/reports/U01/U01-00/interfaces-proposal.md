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
    LocalCommandResult SetVolume(float volume01);
    LocalCommandResult UpdateSettings(UpdateSettingsCommand command);
    Task<LocalResult<VoiceOptionsSnapshot>> RefreshVoiceOptionsAsync(
        CancellationToken cancellationToken);
    Task<LocalResult<MicrophoneDeviceList>> GetMicrophoneDevicesAsync(
        CancellationToken cancellationToken);
    Task<LocalResult<ConversationPage>> ListConversationsAsync(
        ConversationListQuery query, CancellationToken cancellationToken);
    Task<LocalResult<HistoryExport>> ExportConversationAsync(
        Guid conversationId, CancellationToken cancellationToken);
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

`SessionSnapshot` 至少包含 Phase、可空 Operation/Turn、Mode、Error、ConversationId、DraftRevision，以及供 UI 显示的完整/临时文字和音频状态；新增非空 `ClientSettingsSnapshot Settings`、`VoiceOptionsSnapshot VoiceOptions` 和 `HistoryCapacitySnapshot HistoryCapacity`。当前音量从 `Snapshot.Settings.Volume01` 读取，不能从滑块位置反推；设置、音色选项与容量变化沿用 SnapshotChanged。Mode 来自 capabilities / `turn.accepted`，不能由假服务或 UI 宣称为 cloud；chat/tts/asr 模式分别展示，避免系统 TTS 被标为云端 TTS。UI 不再维护另一个 Thinking/Speaking 状态机。

`DraftUpdate` 携带 DraftKey 与识别文本，只有仍有效的结果可进入编辑框；不会直接调用 SubmitText。Draft 更新仍须通过 Session 提交，不能由网络回调直接改输入框。

所有停止类操作先执行共同的同步本地清理段，再进行可等待的存储或网络操作：

1. Audio.Stop 撤销可播放操作、静音并清空队列；Avatar 立即归零口型并清理当前讲话动作。
2. Session 撤销旧 OperationKey，递增 generation，使已经排队的旧文本、音频、动作和完成事件失效。
3. 取消旧操作的 CancellationTokenSource，Abort 录音并释放设备；封存历史为 Interrupted/Failed 等真实状态。
4. 使用捕获的旧 OperationKey，以新的短时限 CancellationToken 尽力发送后台 cancel。不能复用已取消的业务 token，否则取消 HTTP 请求可能根本不发送。

失焦、鼠标移出按住说话按钮、Esc 和关闭窗口均不依赖 key-up 才释放录音。失焦后的播放策略可由桌面设置决定；关闭窗口的本地清理同步完成，不能依赖退出后某个异步 continuation 才停止设备。

### 3.1 R1 补齐：UI 入口与完整新增类型映射

以下对应 A0 的 U01-00-R1。它们是客户端本地类型，不增加 HTTP 字段；类型均位于同一 Contracts 命名空间，DTO 不引用 Unity、存储实现或平台设备句柄。表中列出本次新增/补全类型的全部公开数据成员，属性只读，构造时校验并复制集合/字节；构造参数按表中顺序采用同名 camelCase。`?` 表示可空引用或 Nullable 值；列表不可通过 DTO 反向修改。

| 类型 | 完整公开成员（C# 类型） | 生产者 → 消费者 / 约束 |
|---|---|---|
| `LocalCommandResult` | `bool Accepted`; `PreviewError? Error` | Session → UI；接受时 Error=null，拒绝时必须有 Error；本地命令不分配 OperationKey |
| `LocalResult<T>` | `bool Succeeded`; `T? Value`; `PreviewError? Error` | History/Session → 调用方；成功仅有非空 Value，失败仅有 Error；T 限引用类型，禁止同时有值和错误 |
| `UpdateSettingsCommand` | `bool AutoRead`; `string? VoiceId`; `bool ContinuePlaybackOnFocusLost`; `string? MicrophoneDeviceId` | UI → Session；整体替换这四项，音量独立经 SetVolume；null 麦克风表示尚未选择，禁止自动录音；VoiceId=null 表示后台默认音色 |
| `ClientSettingsSnapshot` | `int SchemaVersion`; `ulong Revision`; `float Volume01`; `bool AutoRead`; `string? VoiceId`; `bool ContinuePlaybackOnFocusLost`; `string? MicrophoneDeviceId`; `string PlaybackDeviceLabel`; `bool CanSelectPlaybackDevice`; `SettingsSaveState SaveState`; `PreviewError? SaveError` | Session → UI；SchemaVersion=1；Revision 在接受的设置变更后递增；SaveError 只在 Failed 时非空；运行值与是否已落盘分别表达 |
| `SettingsSaveState` | `Saved`, `Pending`, `Failed` | 设置持久化状态枚举；不冒充 SessionPhase 或网络错误 |
| `VoiceOption` | `string Id`; `string DisplayName` | Session 从 capabilities 公布音色映射 → UI；Id 非空且不透明，最多 256 Unicode 标量；DisplayName 最多 256，没有公布名称时显示原 ID，不编造音色 |
| `VoiceOptionsState` | `NotLoaded`, `Loading`, `Ready`, `Unavailable`, `Failed` | Ready 表示能力获取成功且 TTS configured/available；Unavailable 表示能力获取成功但 TTS 未配置或不可用；Failed 表示查询或校验失败 |
| `VoiceOptionsSnapshot` | `ulong Revision`; `VoiceOptionsState State`; `IReadOnlyList<VoiceOption> Items`; `PreviewError? Error` | Session → UI；最多 128 项且 ID 唯一，超限拒绝而非截断；仅 Ready 有可选项（允许空列表、仅默认音色），其余状态 Items 为空；Unavailable/Failed 必须有脱敏 Error，其余 Error=null；初始 NotLoaded；每次发布递增 Revision，不回绕 |
| `MicrophoneDevice` | `string Id`; `string DisplayName`; `bool IsDefault` | Capture → Session → UI；不透明 ID ≤256 Unicode 标量，名称 ≤128；仅代表枚举到的设备，不保证当前权限、连接或可录音 |
| `MicrophoneDeviceList` | `IReadOnlyList<MicrophoneDevice> Items`; `DateTimeOffset EnumeratedAtUtc` | Capture → Session → UI；最多 64 项，ID 唯一，默认标记最多一项；无设备为成功的空列表，枚举失败为 LocalResult 错误 |
| `ConversationListQuery` | `int PageSize`; `string? Cursor` | UI → Session → History；PageSize 为 1..20，Cursor=null 请求首页；游标是不透明本机分页标识，UTF-8 ≤512 字节 |
| `ConversationSummary` | `Guid ConversationId`; `string Title`; `DateTimeOffset CreatedAtUtc`; `DateTimeOffset UpdatedAtUtc`; `int MessageCount` | History → Session → UI；只包含索引摘要，无完整聊天/音频；Title ≤80 Unicode 标量，MessageCount 为当前保留消息数 0..80 |
| `ConversationPage` | `IReadOnlyList<ConversationSummary> Items`; `string? NextCursor`; `ulong StoreRevision` | History → Session → UI；Items.Count≤请求 PageSize，末页 NextCursor=null；空库为成功空页；StoreRevision 标识本页读取的索引修订 |
| `HistoryCapacitySnapshot` | `int ConversationCount`; `int MaxConversations`; `int MaxMessagesPerConversation`; `HistoryRetentionPolicy RetentionPolicy`; `ulong StoreRevision` | History 的索引/容量信息由 Session 汇入快照 → UI；初始提案上限 20 会话、每会话 80 条消息；不由 UI 遍历所有历史统计 |
| `HistoryRetentionPolicy` | `EvictOldestInactive` | 初始唯一策略：达到上限时删除最旧的非当前会话；单会话删除最旧完整消息组，避免保留孤立 assistant 回复；UI 明示清理策略 |
| `HistoryExport` | `int SchemaVersion`; `Guid ConversationId`; `ulong StoreRevision`; `DateTimeOffset ExportedAtUtc`; `string SuggestedFileName`; `string MediaType`; `ReadOnlyMemory<byte> Utf8Json` | History → Session → UI 本机另存为；SchemaVersion=1；MediaType=`application/json`；文件名只含安全 basename，例如 `conversation-{id:N}.json`；UTF-8 JSON ≤2 MiB，超限失败而非截断 |

`PreviewError` 沿用第 2 节的 `Code:string`、`Message:string`、`Retryable:bool`、`Operation:OperationKey?`；这些本地查询/设置错误的 Operation=null。代码段中的泛型接口不要求 Unity 序列化泛型 DTO；持久化适配由对应 owner 实现。

类型与入口的使用约束如下：

- **即时音量。** `SetVolume` 在主线程同步校验 0..1（拒绝 NaN/Infinity，拒绝越界而非悄悄钳制），调用 Audio.SetVolume，然后更新设置快照再返回 Accepted。离线和播放期间均可调用；不取消/重发当前 turn，不等待磁盘或网络。最新增益供下个输出块读取，口型仍使用增益后的真实振幅；设为 0 时覆盖尚未消费的非零振幅。每次调用不排队积累一个异步写入，设置保存只保留最新修订。
- **设置与暂不可用选择。** `UpdateSettings` 全量校验结构后一次接受或拒绝，不部分应用；设备/音色的选项成员校验只对与当前快照相比**实际变更的 ID**执行。新非空 VoiceId 必须在 VoiceOptions.State=Ready 的已公布列表中；新非空 MicrophoneDeviceId 必须在最近一次成功枚举中；null 始终允许表示默认音色/不选择麦克风。未变更的原 ID 可以保留，即使重启尚未枚举、设备拔出或音色暂不可用；这只保留偏好，不证明其可用，也不静默替换成其他 ID。因此只改 AutoRead 或失焦策略不会被原设备/音色阻塞。正在录音时改变 MicrophoneDeviceId（包括清空）仍返回 `device_busy`，原样保留则不拒绝。
- **使用时复核。** `BeginRecording(deviceId)` 必须等于快照已选 ID，并在实际开启时重新检查设备与权限；从未选择返回 `device_not_selected`，已选但拔出返回 `device_unavailable`。AutoRead/VoiceId 只影响后续提交，关 AutoRead 不代表当前播放已停止；当前静音/打断仍用 SetVolume(0)/Cancel。SubmitTextCommand 的 generate_audio 必须等于 Settings.AutoRead；有效 voice_id 为 AutoRead=true 时的 Settings.VoiceId，否则为 null，陈旧字段返回 `settings_changed`。需要朗读的提交还需 VoiceOptions=Ready 且非空 VoiceId 仍在选项中，否则返回 `voice_unavailable`，不发收费请求或自动回退；纯文字提交不被保留的失效音色阻塞。
- **音色入口。** UI 仅调用 Session.RefreshVoiceOptionsAsync 并订阅 Snapshot.VoiceOptions；Session 调用 Gateway.GetCapabilitiesAsync，将既有能力中的音色及 TTS 配置/可用信息映射为上述有界 DTO，不增加 HTTP 字段。刷新先发布 Loading；查询成功发布 Ready/Unavailable 并返回成功 LocalResult（不可用是已读到的能力状态），查询/格式错误发布 Failed 并返回失败 LocalResult。未就绪时禁用新音色选择，但可以显示 Settings.VoiceId 为“已保存、待确认/暂不可用”，允许用户保留或清空；默认音色 UI 项对应 null，不伪造服务端 ID。取消刷新时恢复刷新前的数据状态并发布新的 Revision，Task 按取消结束；销毁 Session 后不再发事件，迟到结果按本次刷新身份丢弃。启动检查和手工刷新共用同一有界入口，不生成对话或 TTS，也不让 UI 获取 Gateway 实例。
- **输出设备。** U01 当前提案使用系统默认播放设备，`PlaybackDeviceLabel` 显示实际可获知名称或明确“系统默认输出”，`CanSelectPlaybackDevice=false`。UI 不显示不可用的输出设备选择器，也不把字符串当作可选设备 ID。输入设备有上述明确枚举入口；若后续要求应用内选择输出设备，应另提对称枚举/切换 API 并实测后审阅，不暗中调用 Unity 或 Windows 设备服务绕过 Session。
- **持久化。** 音量/设置接受即改变运行值，设置文件与对话文件分开。Session 所有者负责设置保存适配和修订串行化，可合并尚未写出的修订；异步保存失败保留实际运行值并发布 SaveState=Failed/SaveError，不伪称已保存，不回滚成与实际输出不同的值。重新修改触发新保存；成功/失败仅在回报修订仍等于当前 Revision 时更新 SaveState，更旧结果不覆盖新状态。Revision 耗尽前拒绝继续变更并要求重建运行态，不能回绕。启动用已验证设置或显式默认值，首次运行不录音、不发送；具体默认音量及失焦策略由 A0 冻结时确认。
- **有界历史。** ListConversations 按 UpdatedAtUtc 降序、同时间按 ConversationId 固定次序排序，只读取有界索引摘要。第一页与后续页同一 StoreRevision；游标绑定版本、PageSize、最后排序键，任何索引变更使后续旧游标返回 `history_cursor_expired`，UI 清除旧列表后重查首页，不能合并不同版本。非法页长或游标为 `invalid_request`，不提供“0=全部”。列表读取和导出不切换当前会话、不修改草稿、不停止播放，也不触发云请求。
- **容量与清理。** 20/80 为满足任务书最低数量的初始上限提案，非已经实现的容量测试结论。新建会话到达上限时，由 Session 根据有界列表和自己的当前 ConversationId 选择最旧非当前项，先调用 History.DeleteConversationAsync，再 CreateAsync；Store 自身到达上限则拒绝 Create，不自行猜测当前会话。容量淘汰与手工删除复用第 7 节写入资格撤销，当前会话不被隐式淘汰；无法安全腾出容量返回 `history_capacity_exceeded`，底层 I/O 失败仍返回 `storage_unavailable`。HistoryTurn 的 user/assistant 为各一条消息，80 计消息而非 80 对；正在生成的组不得拆开淘汰。容量变化通过同一历史变更事件进入 Snapshot.HistoryCapacity。
- **导出。** ExportConversationAsync 接受明确的 ID（“当前会话”按钮在点击时捕获 Snapshot.ConversationId），输出一致修订的单会话记录，包含真实交付状态与格式版本；不包含 token、原录音、provider 密钥或设备私人句柄。已有活动请求无需停播；导出可以含 Generating/Interrupted 等当时真实状态。删除/清空与导出串行建立读取点：删除先完成则 `history_not_found`；导出先完成则已交付的副本不会被后续删除召回。上限超出返回 `export_too_large`，坏数据返回 `history_corrupt`，不生成貌似完整的部分文件。UI 的本机保存对话框只负责将该有界内容另存为用户选择的位置，不直接访问 HistoryStore，也不接收内部存储路径。
- **线程、取消、错误。** 所有新增 Session 入口从主线程调用；读取/保存工作不长期阻塞主线程，事件仍回到主线程。查询 Task 不承诺 continuation 线程，UI await 保留 Unity SynchronizationContext；退出/销毁页面时取消其 token 并丢弃迟到结果。取消以 Task 取消和 OperationCanceledException 表达，不返回“成功空列表”，不写入 SessionPhase.Error；检查启动前、读取中和结果发布前的取消。每个 Session 最多同时 1 次音色刷新、1 次设备枚举、1 次列表读取、1 次导出，多余同类调用返回 `local_busy`，不无界排队、不覆盖已有查询的加载状态；这不阻塞 SetVolume/Cancel。预期本地错误用 LocalResult/LocalCommandResult 的 PreviewError 表达，I/O 失败为 `storage_unavailable`，无权限为 `permission_denied`；未知程序缺陷才使 Task fault，记录脱敏诊断。设备拔出在 Begin 时返回 `device_unavailable`，不自动选其他输入录音。

### 3.2 UI 与模块调用样例（接线草案）

下例中的 UI 只持有 ISessionController。`ShowError`、`RenderSettings`、`RenderVoiceOptions`、`RenderConversationRows`、`ReplaceDeviceOptions`、`OfferLocalSaveDialog` 是 UI 自身显示/本机另存为函数，不是新的共享服务接口；后者收到 HistoryExport 才允许用户选择导出目的地。DTO 构造遵守上表字段顺序；代码说明调用关系，不表示已落地的 Unity 组件。

```csharp
// Composition 仅把 session 注入 UI；订阅后立即绘制当前权威值。
void Bind(ISessionController session)
{
    session.SnapshotChanged += OnSnapshot; // 页面销毁时解除订阅
    OnSnapshot(session.Snapshot);
}
void OnSnapshot(SessionSnapshot snapshot)
{
    RenderSettings(snapshot.Settings, snapshot.HistoryCapacity);
    RenderVoiceOptions(snapshot.VoiceOptions, snapshot.Settings.VoiceId);
    // 更新滑块使用 SetValueWithoutNotify，避免事件反馈循环。
}
void OnVolumeChanged(ISessionController session, float value)
{
    LocalCommandResult result = session.SetVolume(value);
    if (!result.Accepted) ShowError(result.Error);
    RenderSettings(session.Snapshot.Settings, session.Snapshot.HistoryCapacity);
}

async Task RefreshVoicesAsync(ISessionController session, CancellationToken pageToken)
{
    LocalResult<VoiceOptionsSnapshot> result =
        await session.RefreshVoiceOptionsAsync(pageToken);
    pageToken.ThrowIfCancellationRequested();
    if (!result.Succeeded) ShowError(result.Error);
    // Loading/Ready/Unavailable/Failed 都由 OnSnapshot 绘制；不调用 Gateway。
}
void ChooseVoice(ISessionController session, string? chosenId)
{
    ClientSettingsSnapshot s = session.Snapshot.Settings;
    LocalCommandResult result = session.UpdateSettings(new UpdateSettingsCommand(
        s.AutoRead, chosenId, s.ContinuePlaybackOnFocusLost, s.MicrophoneDeviceId));
    if (!result.Accepted) ShowError(result.Error);
}
void SetAutoRead(ISessionController session, bool enabled)
{
    ClientSettingsSnapshot s = session.Snapshot.Settings;
    LocalCommandResult result = session.UpdateSettings(new UpdateSettingsCommand(
        enabled, s.VoiceId, s.ContinuePlaybackOnFocusLost, s.MicrophoneDeviceId));
    if (!result.Accepted) ShowError(result.Error);
    // 原麦克风/音色 ID 未改变：即使暂不可用，也可接受本次开关修改。
}

async Task RefreshDevicesAsync(ISessionController session, CancellationToken pageToken)
{
    LocalResult<MicrophoneDeviceList> result =
        await session.GetMicrophoneDevicesAsync(pageToken);
    pageToken.ThrowIfCancellationRequested();
    if (!result.Succeeded) { ShowError(result.Error); return; }
    ReplaceDeviceOptions(result.Value.Items); // 不自动开始采集
}
void ChooseMicrophone(ISessionController session, string chosenId)
{
    ClientSettingsSnapshot s = session.Snapshot.Settings;
    LocalCommandResult result = session.UpdateSettings(new UpdateSettingsCommand(
        s.AutoRead, s.VoiceId, s.ContinuePlaybackOnFocusLost, chosenId));
    if (!result.Accepted) ShowError(result.Error);
    // 随后的按住说话用 session.BeginRecording(session.Snapshot.Settings.MicrophoneDeviceId)。
}

// 返回 NextCursor 交给“下一页”；首次传 null，不无限循环加载全量历史。
async Task<string?> LoadConversationPageAsync(ISessionController session,
    string? cursor, CancellationToken pageToken)
{
    LocalResult<ConversationPage> result = await session.ListConversationsAsync(
        new ConversationListQuery(20, cursor), pageToken);
    pageToken.ThrowIfCancellationRequested();
    if (!result.Succeeded)
    {
        ShowError(result.Error); // history_cursor_expired 时丢弃旧页，用户刷新传 null
        return null;
    }
    RenderConversationRows(result.Value.Items, result.Value.StoreRevision);
    return result.Value.NextCursor;
}
async Task ExportCurrentAsync(ISessionController session, CancellationToken pageToken)
{
    Guid target = session.Snapshot.ConversationId; // 点击时捕获，不在 await 后重新取
    LocalResult<HistoryExport> result =
        await session.ExportConversationAsync(target, pageToken);
    pageToken.ThrowIfCancellationRequested();
    if (!result.Succeeded) { ShowError(result.Error); return; }
    OfferLocalSaveDialog(result.Value); // 文件名、MIME、有界 UTF-8 内容来自导出 DTO
}
```

页面事件处理器统一捕获其生命周期 token 造成的 OperationCanceledException，不对已销毁页面显示错误；其他异常经 UI 的统一脱敏错误入口显示。本例省略该宿主事件处理器，不建议 async void 无异常处理。下一页、切换和删除用行中的 ConversationId；切换与删除仍调用本节已有 Session API。

| UI 入口 | Session 内部调用 | 返回/通知路径 |
|---|---|---|
| SetVolume(v) | 校验 → IAudioPlayer.SetVolume(v) → 更新设置修订、异步保存 | LocalCommandResult；SnapshotChanged.Settings；PostVolumeLevel 仍驱动 Avatar |
| UpdateSettings(command) | 校验能力/设备/录音状态 → 应用本机偏好 → 异步保存 | LocalCommandResult；SnapshotChanged.Settings |
| RefreshVoiceOptionsAsync(ct) | IConversationGateway.GetCapabilitiesAsync(ct) → 校验有界音色列表/可用状态 | LocalResult&lt;VoiceOptionsSnapshot&gt;；SnapshotChanged.VoiceOptions；UI 不直接查询 Gateway |
| GetMicrophoneDevicesAsync(ct) | IMicrophoneCapture.GetDevicesAsync(ct) → 校验上限并保存本次有效 ID 集合 | 相同 LocalResult&lt;MicrophoneDeviceList&gt;；不隐式录音 |
| ListConversationsAsync(query, ct) | IHistoryStore.ListAsync(query, ct) | 相同 LocalResult&lt;ConversationPage&gt;；不绕经 UI 自行读取历史目录 |
| ExportConversationAsync(id, ct) | IHistoryStore.ExportAsync(id, ct) | 相同 LocalResult&lt;HistoryExport&gt; → UI 本机另存为 |
| 初始化/历史变更 | IHistoryStore.Capacity + CapacityChanged | Session 在主线程汇入 Snapshot.HistoryCapacity；不让 UI 订阅 HistoryStore |

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
    Task<LocalResult<MicrophoneDeviceList>> GetDevicesAsync(
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

GetDevicesAsync 不启动采集，也不以“没有授权”为由触发录音。Capture 对返回的 MicrophoneDevice 数量、ID/名称长度施加第 3.1 节上限；超过上限返回明确 `device_limit_exceeded`，不悄悄截去设备。该底层方法与 Session 入口共用 LocalResult、MicrophoneDeviceList 及 Task 取消语义；预期设备错误用失败结果，未知异常不能冒充空列表。Session 保留最后一次成功枚举的有效 ID 集合并转交结果；枚举失败不清空实际选中的设置，但开始采集仍需重新验证。UI 不持有 IMicrophoneCapture 实例。

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
    HistoryCapacitySnapshot Capacity { get; }
    event Action<HistoryCapacitySnapshot> CapacityChanged;
    Task<LocalResult<ConversationPage>> ListAsync(
        ConversationListQuery query, CancellationToken cancellationToken);
    Task<ConversationHistory> CreateAsync(Guid conversationId,
        CancellationToken cancellationToken);
    Task<ConversationHistory> LoadAsync(Guid conversationId,
        CancellationToken cancellationToken);
    Task<HistoryWriteResult> AppendOrUpdateAsync(
        HistoryWriteToken writeToken, HistoryTurn turn,
        CancellationToken cancellationToken);
    Task<LocalResult<HistoryExport>> ExportAsync(Guid conversationId,
        CancellationToken cancellationToken);
    Task DeleteConversationAsync(Guid conversationId,
        CancellationToken cancellationToken);
    Task ClearAllAsync(CancellationToken cancellationToken);
}
```

ConversationHistory 含 SchemaVersion、本机会话 ID、HistoryWriteToken 和版本化记录。HistoryTurn 保留 OperationKey / 可空 TurnId、角色、文本、DeliveryKind、DeliveryState、PlayedSamples/TotalSamples、更新时间及必要模式信息；不存原始录音、Bearer 令牌或 provider 密钥。历史配置与对话分文件存于应用数据目录，支持原子写、损坏恢复和既定容量。

ListAsync / ExportAsync 使用第 3.1 节完整 DTO 与取消/错误规则，Store 自身也校验页长、游标、输出字节数，不能只相信 Session 已校验。Capacity 是初始化完成后的不可变索引快照；Composition 在接通 UI 前完成 History 初始化（失败交 Session 显示 storage_unavailable），不能以未读取磁盘的“空库”覆盖已有历史。每次建立新索引修订后更新 Capacity 并发送 CapacityChanged；Store 可以在工作线程通知，Session 只通过有界主线程调度合并最新修订后发布 SnapshotChanged。UI 不订阅 Store 事件。

读取分页、导出、删除和写入通过有界存储执行器建立明确的先后顺序，读取采用同一修订快照，不长时间占用主线程。History 索引修订在任一影响列表排序/摘要/容量的变更后递增；StoreRevision 耗尽前拒绝继续使用旧实例，不能回绕。List/Export 的排队也支持取消，失败保留有效文件；页面 token 取消不撤销已经提交的历史写入，不改变 HistoryWriteToken。DTO 是读取点的快照，UI 在手工删除/清空成功后丢弃旧列表再取首页，已取得的旧 DTO 不构成重建会话或重新写入的资格。

HistoryWriteToken 是本地存储修订号（会话 ID + 存储代数）的封装，不是 R1 的设备租约。Delete/Clear 先撤销旧写入资格，再排队执行持久化删除；AppendOrUpdate 在真正提交写入时仍检查 token，防止“检查通过 → 异步删除 → 旧写回”复活数据。只有 CreateAsync 能显式新建会话；对已删除 ID 的迟到 upsert 拒绝，不隐式重建。所有写操作串行化，Session 也先停止当前操作。

生成、显示、下载、播放分别登记：generation.completed 仅表示生成结束；音频只有实际 Completed 才标记 Played；中途停止是 Interrupted。文字模式完整显示且收到 generation.completed 才标记 Displayed。TTS 失败保留已生成文字并记为 Failed，附语音失败原因，不能写成 AudioSkipped 或 Played。上下文只选择音频 Played 或文字 Displayed 的完整 assistant 回复；Interrupted 不整篇回送到下一轮。

## 8. 组合入口与接线顺序

1. Composition 加载资源清单与角色 prefab，建立明确的演示/fixture 标记。首次启动不录音、不发付费调用。
2. 注入 IHistoryStore、IMicrophoneCapture、IAudioPlayer、IConversationGateway、IAvatarPresenter 到 Session，完成设置/历史初始化后向 UI 提供初始快照；UI 只拿 ISessionController。音量、设备枚举、列表与导出接线见第 3.2 节；设置持久化适配由 Session owner 管理，对话存储归 History owner，两者不形成程序集互相引用。
3. Session 接受新操作时停止旧操作，分配新 OperationKey，Arm Audio 并 BindOperation Avatar。网络事件回到主线程后再次验 ID、generation、会话和终态。
4. text.completed 缓存完整文字和 emotion；audio.ready 完成下载和严格校验后，由 Session 再次验资格并调用 Play。下载器不直接播放。
5. PlaybackStarted → Session 更新 Speaking → Avatar 表情与讲话动作；PostVolumeLevel → Session/组合层校验身份 → Avatar.SetAudioLevel；PlaybackEnded → Session 决定 Ready/错误及 History 终态。
6. 若播放先结束而 generation.completed 尚未到达，保留实际播放结果，但整体请求直到生成终态校验通过才成功完成；若生成先结束，则等实际播放完成。EOF 或关联错误按协议失败处理，不能提前显示“已经说完”。
7. 所有停止入口复用同一同步清理路径，撤销后先挡住旧事件，再发尽力取消；卸载时解绑事件并 Dispose。模块 prefab 不自行寻找其他模块单例来绕过注入。

## 9. A0 需审阅的提案与验收边界

请求 A0 确认上述 API 形状、PCM 所有权、事件线程、HistoryWriteToken、主线程有界调度以及本机运行配置候选路径。它们是实现层提案，不要求变更 U01 HTTP 字段；如审阅认为需要新增线协议字段，必须另行同步 INTERFACES、U01-03 schema/fixtures 和消费者测试后再实施。

U01-00-R1 本次文档自查覆盖：SetVolume → Audio → 设置快照/增益后口型；音色刷新 → Gateway.capabilities → Session.VoiceOptions → UI 选择；ListConversations → History.List → 有界摘要页；ExportConversation → History.Export → 有界导出 DTO；设备枚举 → Capture → Session 选择校验；History.CapacityChanged → Session 快照。补充核对重启未枚举/拔出设备/音色失效时，只改 AutoRead 仍可保留原 ID，而真正录音/朗读前重新校验。新增类型的成员、生产者/消费者、线程、取消、容量和错误见第 3.1 节，具体 UI 调用见第 3.2 节。这是接口连通性审阅，不声称已编译共享 Contracts 或实现上述业务；仍须 A0 设计复核后由集成 owner 落地。

| 验证点 | U01-00 可以提供的证据 | 后续仍需完成 |
|---|---|---|
| UA01 | 固定候选版本、SDK/Core/模型/字体来源与哈希、最小源码/Player/构建日志、干净工作目录构建 | A0 对同 SHA 证据审阅；未执行的复现不能标为 passed |
| UA02 | `.model3.json` 对应真实 Player 画面、材质/遮罩/排序与运行日志；已执行的时长与行为逐项登记 | 10 分钟稳定性、眨眼/呼吸/视线/招呼及完整能力降级由 U01-01 / QA 补齐；最小场景截图不等于整项通过 |
| UA03/UA05 | 本文约束实际输出采样、先静音和迟到数据隔离 | U01-04 音频实现、U01-01 口型接线；含声音录屏、20 次播放停止与生成/下载各 10 次取消及 P95 |
| UA04/UA07 | 模块边界和历史语义建议 | U01-02 中文 IME、DPI、键盘、历史持久化、坏数据和删除竞争实测 |
| U-G0 | U01-00 提供后续共同工程起点 | UA01/02/03/04/05/12 全部所需证据及阶段集成、QA、A0 验收；不由 U01-00 单独宣布完成 |
| U-G1/U-G2 | 无需云密钥即可完成工程与提案 | 真云、真实麦克风、ASR、闭环与时延另外验收；缺资源时明确 not_run / awaiting_external |

后续 agent 开工必须引用 A0 通过的 U01-00 精确提交、资源与包锁及最终接口修订；不能只引用本文的草案名。本文自身只证明边界已提出，不能用它替代可执行接口、模块实现或实际 Player 证据。
