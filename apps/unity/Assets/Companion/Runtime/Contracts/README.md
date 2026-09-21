# Companion.Contracts · U01 编译候选

Owner：U01-00 集成 owner。依据 [接口提案](../../../../../../docs/reports/U01/U01-00/interfaces-proposal.md)、[U01 协议](../../../../../../docs/tasks/U01/INTERFACES.md) 和 ADR16 迁移授权落地。**当前是待 A0 最终冻结的共享边界候选；编译成功不表示 Session、UI、音频或历史业务已实现。** 不修改仓库根目录冻结的 contracts，不增加 HTTP 路径或请求字段。

程序集为 Companion.Contracts，命名空间为 AICompanion.Preview.Contracts。目标 Unity 2022.3 / .NET Standard 2.1，语言只使用 C# 9 及更早特性。asmdef 无程序集引用且 noEngineReferences=true；不依赖 Unity、Cubism、HTTP 客户端、JSON 库、provider 或其他模块实现。autoReferenced=true 使工程导入会编译边界，具体消费者仍需在自身 asmdef 显式引用它。

## 文件与调用边界

| 文件 | 内容 |
|---|---|
| Common.cs | OperationKey / TurnKey / DraftKey / HistoryWriteToken 的值相等与哈希、枚举、PreviewError、LocalResult / LocalCommandResult、集合复制 |
| Interfaces.cs | Session、Gateway、Audio、Capture、Avatar、History 的全部提案签名；六个接口只公开类型化输入与结果 |
| SessionTypes.cs | SessionSnapshot、发送/草稿、设置、音色、设备 DTO；R1 增补的音量/设置/选项/历史/导出入口在 ISessionController |
| HistoryTypes.cs | 有界会话摘要页、写入身份与交付记录、导出内容、容量 |
| GatewayTypes.cs | 预览能力、提交上下文、音频描述、取消和转写结果；没有网络实现 |
| PreviewEvents.cs | 八种已公布 NDJSON 事件的封闭派生类型；不使用任意字典或 object payload |
| AudioTypes.cs / PcmBuffers.cs | 实际播放事件/采样、采集结果、带生命周期的 PCM 所有权 |
| AvatarTypes.cs | 模型参数/能力、关联操作、加载与动作结果；没有 Cubism 类型 |
| AssemblyInfo.cs | 仅 Companion.Audio 可构造 ValidatedPcm 的 friend assembly 声明 |

UI 只依赖 ISessionController。音量经 SetVolume；设置经 UpdateSettings；音色刷新经 RefreshVoiceOptionsAsync 并显示 Snapshot.VoiceOptions；麦克风枚举经 GetMicrophoneDevicesAsync；历史列表/导出分别经 ListConversationsAsync / ExportConversationAsync。Snapshot 同时包含 Settings、VoiceOptions、HistoryCapacity 与 Messages，UI 无需为了显示历史直接持有 HistoryStore。History 的 CapacityChanged 由 Session 转成主线程 SnapshotChanged。

本提交只有接口、DTO、不可变集合复制及缓冲生命周期。线程调度、取消、状态机、持久化、RIFF 验证、设备操作、能力获取和网络映射由后续 owner 实现；不能把接口方法存在当作功能通过。

## 不可变与所有权

- DTO 使用只读属性；构造函数复制所有集合。公开集合是 IReadOnlyList，实际返回只读包装，调用方改变输入数组不会改变 DTO。字符串本身不可变。
- HistoryExport 复制 UTF-8 内容，公开 ReadOnlyMemory，最大 2 MiB；对象不包含内部存储路径。保存对话框和路径安全检查属于 UI/导出适配，不能直接将不受信任的 SuggestedFileName 当路径。
- ValidatedPcm 是验证完成后的 24 kHz / mono / PCM16 样本所有者。构造器仅对 Companion.Audio 可见；它检查格式/120 秒形状，不实施完整 RIFF、资源路径、请求身份验证。Audio 的验证器完成那些验证后才可调用。Play 接受即把所有权交播放器，拒绝时调用方仍负责释放。
- CapturedPcm 复制设备实际格式的交错 float 样本；CaptureResult 成功后所有权交 Session。Gateway 在 TranscribeAsync 完成或取消前借用，Session 之后释放；重采样/编码为 16 kHz PCM16LE WAV 及 1 MiB 上传校验属于适配器。
- 两种 PCM 的 Dispose 幂等，清零并释放自身数组引用。获得的只读视图只在所有者存活期有效；释放后属性访问抛 ObjectDisposedException。禁止转移后继续读取或 Dispose，也不能把 ReadOnlyMemory 当作永久保存的音频副本。
- AudioLevelSample 是 readonly struct，传递实际增益后振幅不要求逐块分配对象/数组。MonotonicTicks 统一指进程内 Stopwatch.GetTimestamp()，频率为 Stopwatch.Frequency；时间字段本身不证明测量来自真实输出。
- nullable 值类型显式使用问号。为与现有 Unity 编译设置一致，未启用 nullable 引用分析；下文列出的引用 null 是有意义的，消费者必须检查结果标志。

## 本次补全、需 A0 最终冻结的局部选择

原提案未逐字段定义以下 DTO。本次只采用已描述语义，形成可编译候选；这些 C# 成员名**不是**另一个 wire schema，禁止直接按属性名序列化上线。

1. SessionSnapshot 用 DraftText、FullText、PartialText、Playback 和最多 80 条 Messages 表达现有 UI 状态；Mode 为聊天模式，另有 TtsMode / AsrMode。SpeechMode.System 只用于本机状态区别系统语音，不能被映射成 cloud；最终 capabilities 的 tts_mode 到枚举映射由 U01-03 与 A0 确认。
2. PreviewCapabilities 用 Chat/Asr 的 ServiceCapability 和 Tts 的 SpeechCapability 表达 mode/configured/available；另含 CharacterIds、Voices、PreviewLimits。字符清单本地容量候选 128。PreviewLimits 的十项值对应任务书已有字符、消息、字节和时长限制；具体 capabilities JSON 字段拓扑并未在任务书冻结，本 DTO 不自创拓扑。
3. PreviewEvent 固定协议为 unity-preview/1，Sequence 为 uint；Operation 从 Turn.Operation 派生。Transport 必须先验证原始 protocol、ID、seq 和字段后构造 DTO，不能先归一化错误值再声称合法。Type 显式使用八个现有事件名称；AudioSkipReason.UserDisabled 映射 user_disabled；其余枚举映射也必须显式编写。
4. HistoryTurn 用 Operation + 可空 TurnId + MessageRole 标识同一次输入/回复的两类记录；每条记录的格式版本由 ConversationHistory.SchemaVersion 提供，模式和失败原因随记录保存。ConversationHistory 同时保留 Title、CreatedAtUtc、UpdatedAtUtc，支撑摘要列表；HistoryWriteResult 返回 Accepted、StoreRevision、Error。这些是本地存储候选字段，不要求变更预览 HTTP。
5. PlaybackSnapshot 和 Progress 含真实样本进度/时钟；Started 含 Turn、TotalSamples、SampleRate、MonotonicTicks；Ended 再含 Reason、PlayedSamples、Error。PlaybackSnapshot.Volume01 是播放器实际应用增益供 Session 对齐，不建立第二套 UI 设置状态。
6. CaptureStarted 成功时 Error=null，携带 Draft、DeviceId、实际 SampleRate/Channels、MonotonicTicks；失败携带 Error，不得消费其设备/格式值。CaptureResult 成功仅有 Recording，失败仅有 Error。AvatarLoadResult 同理只含 Capabilities 或 Error。没有成功录音内容时不得返回空 PCM 假装成功。
7. AvatarParameter 为 ID/min/max/default；AvatarCapabilities 列出实际参数、四项支持标志、表达式与动作 ID。局部防御容量候选为 1024 参数、5 种既定 Emotion、256 动作；AvatarActionStatus 区分 Applied/Unsupported/StaleOperation/Unavailable，UsedNeutralFallback 表示是否中性降级。AvatarPlaybackState 为 Idle/Speaking/Stopped/Failed，最终状态映射随 U01-01/04 接线审阅。
8. CapturedPcm 的设备原始格式尚未在提案定型，本次使用 interleaved float、30 秒和 64 MiB 内存上限；64 MiB 是客户端原始采样容器候选上限，**不改变** 1 MiB 上传限制。有效 float 范围、重采样与权限/设备错误由 Audio 实现验证。
9. ValidatedPcm 的 internal 构造通过 Companion.Audio friend assembly 限定。Gateway 下载后如何注入 Audio 验证器尚需集成 owner/A0 在后续接线时确认；本次没有为了可编译而新增未提案的验证服务接口，也不允许 Transport 私自调用构造器跳过验证。
10. CommandReceipt 接受时必须有 Operation；拒绝时必须有 Error，Operation 可空。LocalResult 成功仅有非空 Value，失败仅有 Error。LocalCommandResult 成功 Error=null。其他结果的 Accepted/Status 与 Error 一致性仍由生产者负责；单纯 new DTO 不证明业务成功。

可空引用约定：VoiceId=null 表示后台默认音色，MicrophoneDeviceId=null 表示未选择；Cursor/NextCursor=null 为首页/末页；结果的 Error、Value、Recording、Capabilities 按上述互斥规则；SessionSnapshot.Error 和 HistoryTurn.Error 在无错误时为空；ClientSettingsSnapshot.SaveError 仅保存失败时存在。其余快照、集合和文字使用非空对象/空字符串，不用 null 代表“还没加载”；音色加载状态有专门枚举。

构造器只做基本 null、集合容量、分页/PCM 形状与部分结果互斥检查。Unicode 字符数、ID 唯一性、枚举合法性、Revision 单调、有效 ID、声音和录音权限、导出 MIME/安全文件名，以及每页不超过请求 PageSize 等完整语义仍必须由生产者按提案验证。default(OperationKey) 在 C# 无法禁止，不能作为有效操作；无活动操作使用 OperationKey?=null。

## 编译与后续验证

开发已用本机 Unity 2022.3.62f3c1 的 Roslyn 和 Editor/Data/NetStandard/ref/2.1.0 引用，仅编译本目录到临时输出目录；无业务服务、模型或 Editor 运行依赖。根集成 owner 仍须以登记源码提交执行真实 Unity 导入、整体构建和 Player 检查；本目录不提供额外常驻进程或网络调用。

后续实现需按提案测试：设置保留暂不可用的原设备/音色 ID、增益与实际输出/口型、分页游标失效、删除旧写入资格、导出快照、取消/迟到事件、PCM 所有权。接口编译不能替代这些行为测试或 A0 的最终冻结。
