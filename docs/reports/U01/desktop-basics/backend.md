# U01-03 桌面基础功能后台交接

日期：2026-09-21。开发分支：`agent/U01-desktop-basics`，源码提交由集成 owner 在本轮整体交付时登记。本报告是 U01-03 模块交接，不判 U-G0 / U-G1 / U-G2 通过。

最终产品提交为 `b94af81`，全 API 回归最新 **75 passed**（包括便携启动器 14 项），见 [便携运行时报告](portable-package.md) 与 `C:/Users/Link/Dev/ai-companion-dev-tools/evidence/desktop-final-check.log`。下文 56 / 58 / 70 是按实施阶段保留的结果，不是最终总数。

## 已交付

- 隔离的 `services/api/app/unity_preview`，保持根 contracts 和依赖锁不变；`app/main.py` 仅注册同一预览入口，未配置时全部预览接口拒绝访问。
- 与 U01-04 对齐的严格 snake_case wire JSON Schema、正常和故障固定样例、能力与限制、唯一请求/turn 身份。
- loopback + 短期 Bearer 令牌鉴权，拒绝浏览器 Origin/外部 Host，脱敏错误与默认禁止缓存。
- 明确 fixture 的固定文字、固定转写和随包原创 WAV；关闭朗读不会调用音频提供器；保留替换 TTS 的 `SpeechSynthesizer` 边界。
- 单活动生成/识别、取消先到 tombstone、5 分钟去重、512 条登记上限、取消与断流清理、迟到 provider 不发布。
- 严格 WAV chunk/格式/尺寸/时长检查；输出 8 MiB / 120 秒、输入 1 MiB / 30 秒；音频缓存 32 MiB / 5 分钟，取消释放、失效返回 410。
- 显式 cloud 不可用和 401 / 409 / 422 / 429 / 503 / 504 / 410 错误与故障样例。真实云 provider 与预算器未接入，没有假成功或收费重试。

## 验证与复现

环境：Windows、本仓库 `.venv`、Python 3.12.10；FastAPI 0.141.1、uvicorn 0.53.0、httpx 0.28.1、pytest 9.1.1、jsonschema 4.26.0，均使用既有锁定依赖。

模块命令 `.venv/Scripts/python.exe -m pytest services/api/tests/unity_preview -q`：**56 passed**。覆盖真实 FastAPI 路由、正常 schema、样本音频、不要朗读、额外/错误字段、无效 ID、字符/字节/上下文上限、鉴权/Origin/Host/客户端地址、缺云配置、重复提交、取消先到/幂等、完成后取消音频、过期与逐出、两种登记容量、各故障样例、空/静音/错误录音、无视取消的迟到 provider、下载准备期取消、真实 timeout、素材重生成。

其中三项在 **真实 TCP + uvicorn** 上执行：读取 accepted 后关闭 HTTP 流；通过独立 HTTP 请求取消正在生成的流；发送完整 ASR WAV 后直接断开 socket。三项均确认活动操作退出、任务标为取消、无残留音频。该证据不替代 Unity Player 播放停止延迟、Windows 输出回环、真实录音或 UI 操作证据。

冻结依赖产生两条既有弃用提示：Starlette TestClient 的 httpx 适配与 anyio BlockingPortal alias；没有为消除提示修改依赖锁。

全 API 回归 `.venv/Scripts/python.exe -m pytest services/api/tests -q`：**58 passed**，包含原基础 liveness / 未实现 R1 路由检查。新增后台/测试及入口的 Ruff 检查和格式检查通过。

启动及逐项故障复现见后台模块 `README.md`。故障由启动器 `-Scenario` 或后台 `U01_PREVIEW_SCENARIO` 选择，仅 fixture 生效。固定文件位于 `protocol/`；音频来源及 SHA-256 在 `fixtures/SOURCE.md`。

## 集成边界与剩余验收

启动配置是私有 JSON `{protocol,base_url,token}`，由 `U01_PREVIEW_CONFIG` 指定；后台入口 `python -m app.unity_preview` 固定绑定 127.0.0.1:8000、单 worker、关闭 access log。token 为 URL-safe 随机串，不能使用带 `+ / =` 的原始 Base64；已向集成 owner 指出并对齐。

当前 capabilities 的角色是 `mao`，音色是 `fixture-tone`；正常 WAV 为 192000 样本、384056 字节、8 秒。客户端仍需用真实 Audio 验证器下载校验，实际播放结束后才记录 played。静音或停止闭嘴由 Playback/Avatar 完成。

尚未由本模块证明：完整 U-G0/UA02–05 Player 验收、真实云对话/朗读/ASR、实际调用用量、桌面录音、输出停止 P95、长期稳定性、移动和直播。后续云接入需实现真正 provider、预算/用量记录与对应真实证据；本次只交桌面基础功能的演示后台。

## 生成与下载中取消的后续联调补充

集成 owner 为区分真实取消阶段，追加两个显式 fixture 调度：`slow_generation` 在 accepted 后等待 3 秒再执行固定回复；`slow_download` 正常完成生成，GET WAV 先发送 200 响应头，等待 3 秒后重新读取资源。取消/过期时后者不发旧 WAV body；因为响应头已发出而结束空流，后续 GET 返回 410。原 `late_audio` 仍在 audio.ready 前等待，语义未改；无新增 wire 字段。

固定 NDJSON 和 manifest、README 均已登记来源为本模块的演示调度，不能当作真实模型/网络延迟。真实 TCP 新增三项测试验证 accepted 后立即取消、已收到下载头后的取消，以及正常下载 body 延迟，全部通过。当时包括便携工具测试的全 API 回归为 **70 passed**，Ruff 检查/格式通过；后台源码按集成 owner 要求冻结，Player 10+10 次测试另由集成证据登记。随后启动器强退处理增加测试，最终总数见本报告开头。

## 实际 Player 故障调度对照

以下是对当前后台、Gateway.Error / EventDecoder、WavValidator 和 Session.Fail 的只读映射，**不是实际 Player 故障测试结果**。所有请求开启朗读、角色 `mao`、音色 `fixture-tone`。`fault` 测试模式期望 `SessionPhase.Error`、精确 Error.Code，随后停止恢复 Ready；不能用它测试正常/重复去重/用户取消路径。

| 后台 Scenario | 期望 Session.Error.Code | 到达 Error 前的文字 | Player 测试参数 |
|---|---|---|---|
| `tts_failure` | `provider_unavailable` | 完整生成文字保留，未播放 | `-TestMode fault -ExpectedError provider_unavailable` |
| `partial_stream` | `protocol_error` | 已收到的 delta 保留；EOF 缺少终态 | `-TestMode fault -ExpectedError protocol_error` |
| `seq_gap` | `protocol_error` | 合法旧 delta 保留；拒绝缺号事件 | `-TestMode fault -ExpectedError protocol_error` |
| `wrong_ids` | `protocol_error` | 合法旧 delta 保留；不接受错误 ID 的完成事件 | `-TestMode fault -ExpectedError protocol_error` |
| `corrupt_audio` | `invalid_audio` | 完整文字保留；WAV 验证失败，不播放 | `-TestMode fault -ExpectedError invalid_audio` |
| `truncated_audio` | `invalid_audio` | 完整文字保留；WAV 长度校验失败，不播放 | `-TestMode fault -ExpectedError invalid_audio` |
| `expired_audio` | `resource_expired` | 完整文字保留；下载 HTTP 410，不重新生成 | `-TestMode fault -ExpectedError resource_expired` |
| `budget_exceeded` | `budget_exceeded` | accepted 前 HTTP 429，无回复文字 | `-TestMode fault -ExpectedError budget_exceeded` |
| `provider_timeout` | `provider_timeout` | accepted 后 error，无回复文字 | `-TestMode fault -ExpectedError provider_timeout` |
| `normal` | 无（正常终态 Ready） | 完整回复；实际播完才 Played | `-TestMode fixture` |
| `seq_duplicate` | 无（相同重复事件丢弃） | 不得重复追加文字或播放 | `-TestMode fixture`；不能作为期望错误用例 |
| `late_audio` | 无（未取消则正常） | 文字完成后、audio.ready 前等待 3 秒 | 不等同下载中；不使用 `fault` 模式 |
| `slow_generation` | 无（用户取消为 Ready/Interrupted） | accepted 后 3 秒等待；应在文字前取消 | `-TestMode generation`，应记 10 次 acceptedGenerationStops |
| `slow_download` | 无（用户取消为 Ready/Interrupted） | 完整文字可保留；GET 头已到，body 等待 | `-TestMode late-audio`，应记 10 次 downloadStops |

每个场景由 `Start-Desktop.ps1 -Scenario <值>` 注入独立后台，使用独立 `-RuntimeDirectory`、`-UserDataDirectory`、`-EvidenceDirectory`。故障建议单次 `-TestSeconds 22`；两个阶段取消模式建议各 `-TestSeconds 45`，由 runner 的关联阶段信号决定取消时点，不以等待秒数猜测阶段。固定 3 秒是故障调度来源，不代表实际服务性能。

补充边界：缺/错令牌通常在 capabilities 初始化阶段表现为 Offline + `unauthorized`；后台不在线表现为 Offline + `backend_unavailable`；cloud 未配置的 capabilities 显示不可用并给 `provider_unavailable`。这些不是当前 `Scenario` 枚举，不能用更换 ExpectedError 而宣称已覆盖。用户主动取消下载后，已返回 200 的旧响应会空 EOF，但 Session 已失效不会把其当作新 Error；单独再次 GET 资源才是 410。
