# 机器可读接口与样例

本目录为 1.0.0 接口基线，冻结清单见 baseline.json。REST 定义在 openapi.json，客户端和服务端事件分别有 JSON Schema 2020-12 定义。样例数据均为虚构测试值；当前业务实现状态见根 README。

- [REST 契约](openapi.json)
- [客户端事件](client-event.schema.json)
- [服务端事件](server-event.schema.json)
- [客户端正例](examples/client-valid.json)
- [服务端正例](examples/server-valid.json)
- [事件反例](examples/invalid-events.json)
- [REST 正例](examples/rest-valid.json)
- [音频头示例](examples/audio-header.json)

正例数组是逐种事件的独立样例，不是可以原样重放的会话时间线。反例必须被拒绝。schema 验证结构，权限、先后关系、租约、取消和播放语义必须另按 [CONTRACTS.md](../docs/blueprint/CONTRACTS.md) 实现和测试。

openapi.json 只描述 REST。两个 WebSocket 通道的认证、控制消息和二进制音频协议在 CONTRACTS.md。修改契约时同时修改文档、schema、样例、mock 和消费者。

使用包内 tools/validate_blueprint.py 检查结构与相互引用；工程阶段另外建立真正的跨端契约和端到端测试。
