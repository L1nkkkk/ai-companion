# R1 离线契约与参考消费者测试

本目录执行 T04 的 AC02 / AC07 离线检查，使用仓库冻结的 R1 1.0 schema 和 AIC1 PCM 帧。没有修改 `contracts/`，没有把 U01 `unity-preview/1`、WAV 或 `generation` 混入 R1。

在仓库根目录安装冻结依赖后运行：

```powershell
uv run pytest tests/contract -o "pythonpath=tests services/api" -q
node tests/contract/vectors/verify-node.mjs
pwsh -NoProfile -File tests/contract/vectors/Verify-Aic1.ps1
```

统一检查入口由开发集成 owner 维护。独立 schema 静态检查仍可使用 `uv run python tools/validate_blueprint.py --root .`。

## 实际覆盖

| 文件 | 检查范围 |
| --- | --- |
| `test_schema.py` | schema 自校验；冻结的全部 27 个正例、8 个反例；所有必填 envelope 字段；UUID、带时区 RFC3339、版本、epoch；未知事件/字段；JSON 重复键、NaN、UTF-8、深嵌套和 64 KiB 限制 |
| `test_protocol.py` | 原始 `audio-header.json` 逐字节一致；64 字节头；标量小端与 UUID 网络字节；输入/输出身份、格式与采样率；偶数字节/长度/9600 上限；截断、额外字节、uint32 范围与偏移溢出；短末帧 |
| `test_receiver.py` | 音频先于元数据；500 ms 和全局 64 KiB 未识别音频上限；旧 epoch、错误 session、未知/终态 turn、控制去重；输入设备/accepted 约束、30 秒输入上限；重复/跳号/非连续偏移；输入与输出结束提前、总量和末帧边界；2 秒待播上限与显式背压；四阶段取消后文本/动作/音频；同 epoch 快照清理及 resync 恢复 |
| `test_vectors.py` / `vectors/` | Python、Node 和独立 C# 都编码并解码三个固定字节向量；非重复 UUID、UUID 混合端序陷阱、PCM 正负极值、uint32 最高值、短末帧；Node/C# 各拒绝 10 个独立坏帧 |

跨语言向量是按冻结的字节偏移表填写的常量，不在测试运行时调用 Python 编码器生成预期值。另有原契约固定头作独立对照。Node 检查为必需；C# 检查使用 PowerShell 7 的 .NET `Add-Type`，没有安装 PowerShell 的环境会明确跳过该项。本机 Windows 已真实执行 C#，没有跳过。

## 参考消费者接口与限制

`mocks.protocol` 提供 `AudioFrameHeader`、`AudioFrame`、`encode_frame`、`decode_frame`、`decode_control` 和 `validate_event`。协议失败抛出带诊断 `code` 的 `AudioProtocolError`；诊断码只属于测试，不扩充生产错误契约。编码器自动计算 `payload_bytes`；音频头没有 `session_id`，会话由连接绑定。

`mocks.receiver.PcmReceiver` 是一条已绑定会话的测试参考消费者。`receive_audio`、`receive_control` 返回接受/等待/拒绝结果；`cancel` 立即清队列，不等待服务端确认。宿主必须定时调用 `tick`，使无新网络消息时的等待也会到期。时钟可注入，从而不靠真实睡眠验证 499/500 ms 边界。

- 未识别音频按**全部流合计的完整帧字节**限制为 64 KiB；等待最多 500 ms。错误后请求 resync，并暂停接收，直到权威快照恢复；同 epoch 可以恢复，失败流不会重播。
- 输出匹配元数据后，PCM 待播量最多两秒。零字节帧另受 1,024 帧队列限额保护。超过限额显式停止损坏/过载流，不跳过中间帧继续拼接。
- 当前参考实例最多保留 64 个已知流；错误/文本/动作诊断采用有界队列。上述实例容量是测试实现的防护设置，不能当作新增的 R1 标准。
- 取消/终态 turn 身份按契约保留到 epoch 改变。`take_ready()` 仅将已匹配 PCM 交给**模拟 sink**，不播放声音，也不产生实际扬声器进度。
- 输入 `accept_input` 需要调用者已经确认设备租约；本消费者不实现真实认证、租约时钟、数据库、云服务或持久化。

这些测试不能证明正式 Unity/C# 或手机原生消费者已经接线，不能证明真实播放回调、200 ms 静音、ASR/LLM/TTS、真机后台或真实直播。C# 字节验证也不等于 Unity 产品程序集接入。服务端 mock 的会话与故障行为另由 `tests/mocks/` 的服务测试验证。

2026-09-20 本机结果：Python 3.12.10、Node 24.19.0、PowerShell 7 下执行本目录测试 **143 passed**；包含真实 Node 和 C# 子进程。后续修订请以最新统一检查及报告为准。
