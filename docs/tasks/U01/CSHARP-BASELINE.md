# U01 C# 协作边界冻结

日期：2026-09-20。A0 接受源码 `849c6c982d875f477d88933af4abea5a163d0fa6` 中的 `Companion.Contracts` 作为 U01 协作起点。[验收结论](../../reports/U01/acceptance/PR-003-round3-review.md)仅冻结接口、数据类型与以下接线决定，不宣称业务功能通过。

## 冻结对象与局部选择

以[固定源码](https://github.com/L1nkkkk/ai-companion/tree/849c6c982d875f477d88933af4abea5a163d0fa6/apps/unity/Assets/Companion/Runtime/Contracts)的六接口、成员签名、枚举、身份类型与 PCM 生命周期为准。程序集不依赖 Unity、Cubism、HTTP 库或 provider；根 `contracts/` 的正式 R1 契约保持原样。原接口提案中的线程、取消、历史状态和生产者校验要求继续有效。

A0 接受 Contracts README 所列十类局部选择，作如下限定：

| 范围 | 冻结的协作约定 |
|---|---|
| Session 与设置 | UI 仅持有 ISessionController；快照包含文字、播放、设置、音色、历史容量和最多 80 条记录。模式 Unknown 必须显式显示未就绪；TTS 的 System 与 Cloud/Fixture 分开 |
| 能力与限额 | 接受 typed capability/limits DTO；角色与音色各最多 128 项、设备最多 64 项。DTO 成员不是网络 JSON 拓扑；U01-03 先提交预览 schema/固定样例给 A0 与 U01-04 核对，不凭枚举名自行序列化 |
| 流事件与身份 | 固定 unity-preview/1、八种事件、Operation/Turn/Sequence；Transport 先校验原始协议和关联身份，再构造事件，不能用 DTO 默认值修补非法网络输入 |
| 历史与本地结果 | 接受版本化 HistoryTurn/ConversationHistory、StorageGeneration、StoreRevision、分页与导出 DTO。20 会话/每会话 80 条、2 MiB 单次导出保持；生产者验证结果标志与 Error 一致性、ID、容量和修订单调 |
| 实际音频 | Playback 采样时钟统一 Stopwatch ticks。ValidatedPcm 只由 Companion.Audio 构造，固定已校验 24 kHz/mono/PCM16、最长 120 秒。Play 接受转移所有权，拒绝由调用者释放；不能用完成生成代表完成播放 |
| 录音 | CapturedPcm 使用真实设备格式的交错 float，容器最长 30 秒/64 MiB；该内存上限不改变 16 kHz/mono/PCM16 WAV 的 1 MiB 上传限制。Session 拥有采集结果，Gateway 仅在异步上传期间借用 |
| 角色 | 接受参数/动作能力与 Applied/Unsupported/StaleOperation/Unavailable；最多 1024 参数、5 类 Emotion、256 动作。能力必须来自实际资源；动作/表情与当前操作绑定，缺能力明确降级 |
| null、错误与释放 | 接受已列可空语义和幂等 Dispose。构造器基本检查不代替生产者完整校验；取消以取消状态结束异步操作，其他异常在模块边界脱敏并映射到已有 PreviewError，不将原始异常或 provider 对象交给 UI |

## Transport → Audio 验证器注入

采用**由 Composition 注入验证委托**的方案，不为本次接线新增共享接口或改变现有 DownloadAudioAsync 签名。委托的 C# 类型为：

```csharp
Func<ReadOnlyMemory<byte>, AudioReadyDescriptor, CancellationToken, Task<ValidatedPcm>>
```

参数依次为完整且有界的 WAV 字节、原 audio.ready 描述和取消 token。Companion.Audio 提供实现，Companion.Transport 的 Gateway 构造时接收委托；Companion.Composition 引用双方并连接。Transport 的 asmdef 只需依赖 Contracts，不引用 Audio 实现，UI 仍不接触验证器。

1. Transport 校验当前 Operation/Turn、准确相对音频路径、loopback 地址、响应状态与下载上限。拒绝外部 URL、越界重定向、路径穿越和过期音频；下载失败不调用解码器，不重新计费生成。
2. Audio 校验 RIFF/WAVE 与全部合法 chunk、格式、长度、总样本和描述的一致性、8 MiB/120 秒边界，再通过现有 internal 构造器创建独立拥有样本的 ValidatedPcm。验证器不执行网络、文件或设备操作，不接受未验证字节作为 PCM。
3. WAV 输入内存由 Gateway 持有，直到 await 验证完成/取消/失败后才释放或归还池；Audio 不保留输入的借用视图。取消传入验证器并在返回前再次检查；若验证后操作已经失效，Gateway 释放返回的 PCM，禁止发布。
4. Gateway 成功返回后，PCM 由 Session 暂持；Audio.Play 接受后转移给播放器，拒绝/迟到/失败由仍持有者释放。错误由 U01-04 的边界适配转成脱敏 PreviewError；不能把验证失败包装成空音频成功。

U01-04 实现并测试委托与取消/所有权，U01-05 负责正式 Composition 接线。可用假验证器测试 Transport，但 G0 的 fixture 音频必须经过真实 Audio 验证器，UA03 使用真实输出振幅。需要新增公共异常或验证服务类型时提交小范围设计变更，禁止各模块自行添加同名协议。

## 放行方式

U01-01、02、03、04 可以按各自目录启动。它们共用这一份 Contracts；共享类型、Packages、ProjectSettings、总场景与 Composition 仍归集成 owner。U01-03/04 在固定预览样例达成一致后再做网络联调，UI/Avatar 可先对现有 typed 假服务开发。版本或共享签名变化仍由 A0 审阅消费者影响，不能由某个模块单独升级。
