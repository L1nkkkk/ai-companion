# T04 派发记录

日期：2026-09-20。先完成 [T00 未完成项核对及并行条件](../../reports/T00/parallel-readiness.md)，再依用户指示派发实施。任务角色 A8；开发分支 `agent/T04-contract-mocks`，基线 `66b0e541a91aa727b33b3997b39bce178bbaee6a`。

执行 [原 T04 任务卡](../../blueprint/TASKS.md)，验收仍为 AC02、AC07。交付可独立运行的离线控制、音频、直播 mock，确定性模拟音频和明确角色占位，延迟/取消/旧代次/重复/断线注入，正反例和多语言序列化向量，自动测试与契约 CI。不得把静态正例数组直接当作合法会话时间线。

并行分工（同一任务 worktree、文件所有权互斥）：

- mock owner：`tests/mocks/server.py`、`tests/mocks/live.py`、资源及使用说明、服务自身测试。独立 FastAPI 服务，正式 `/v1` 控制与音频路径，测试管理入口 `/mock/*`；不改生产 `services/api`。
- 契约测试 owner：`tests/mocks/protocol.py`、`tests/mocks/receiver.py`、`tests/contract/`，定义校验器、参考消费者、冻结向量及跨语言测试；与 mock owner 明确函数接口。
- 开发集成 owner：`tools/`、`.github/workflows/contract.yml`、已登记的最小根依赖变更、台账及报告；独立启动实测并保留证据。
- 审阅 owner：只读复核契约一致性、故障测试实际覆盖和完成边界；发现问题交实现 owner 修复。

保留 R1 schema 与 U01 `unity-preview/1` 的界线。T04 不承诺生产认证、数据库持久化、完整 REST 实现、Unity 产品集成或真实 ASR/LLM/TTS/直播。任何未实现入口须明确失败，不能假装真实能力。
