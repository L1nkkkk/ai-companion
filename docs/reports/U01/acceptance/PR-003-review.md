# A0 审阅 PR #3：工程准备可保留，U01-00 尚未通过

日期：2026-09-20。审阅者：A0 设计与验收会话。对象：[PR #3](https://github.com/L1nkkkk/ai-companion/pull/3)，head `5de03d46a048d1b976680aecc1e7c21962951b06`，开发起点 `765c635327685b413c7f930558711db7d88df60c`。资源复核代码为 `f64596ebcf7f372848915e01e27bf752ae3dc4e9`。

**结论：U01-00 为 `awaiting_external`，同时有两项开发续作要求。UA01、UA02 未通过，U-G0 和 U01 未完成，暂不放行依赖 U01-00 的实现任务。** 可以保留 PR 作为工程准备草稿；本记录不表示批准合并或冻结 Unity 版本组合。

## 1. 本次实际检查及证据边界

| 检查 | A0 本次结果 | 限制 |
|---|---|---|
| GitHub / 本地提交 | PR head 与开发工作树一致；69 个改动文件处于获准目录；工作树干净 | 结论只针对上述 SHA |
| 受保护范围 | 冻结契约、旧客户端、根依赖锁、CI 与原设计台账未改 | 不代表新增 Unity 代码已编译 |
| 资源文件 | 本地 Mao 的 106 个文件/元数据、字体及 OFL 哈希匹配提交清单 | A0 没有重新下载或导入 SDK，也未替用户作资源许可承诺 |
| 环境证据 | 6 份已提交的脱敏证据，其 Git blob SHA-256 与清单一致 | 完整私人许可日志未读取、未上传 |
| 安装器可达性 | A0 对官方安装器执行一次 HEAD，区域重定向后仍为 HTTP 404 | 未下载载荷、未安装 Editor；不能据此推断所有网络或其他版本均不可用 |
| 仓库 CI | head 的 [Foundation 工作流](https://github.com/L1nkkkk/ai-companion/actions/runs/35500611249) 在 Windows、macOS、Linux 全部成功 | 工作流执行已有仓库检查，不执行 Unity Editor 或 Player 构建 |
| 原生 Core、恢复与 GUID | 已阅读开发者报告及结果；复核代码到 PR head 只有 5 份报告变更 | A0 本次未重新运行 Core 探针或完整恢复；它们也不能证明实际画面正确 |
| Unity 产物 | `build-manifest.json` 的 artifact、sha256、built_source_sha 均为 null，player_started=false；没有 packages-lock | 未执行 Unity 导入、编译、构建、Player 画面或稳定性验收 |

开发原始材料：[交接报告](https://github.com/L1nkkkk/ai-companion/blob/5de03d46a048d1b976680aecc1e7c21962951b06/docs/reports/U01/U01-00/report.md)、[环境报告](https://github.com/L1nkkkk/ai-companion/blob/5de03d46a048d1b976680aecc1e7c21962951b06/docs/reports/U01/U01-00/environment-install.md)、[构建清单](https://github.com/L1nkkkk/ai-companion/blob/5de03d46a048d1b976680aecc1e7c21962951b06/docs/reports/U01/U01-00/build-manifest.json)。本次结构化记录见 [PR-003-verification.json](PR-003-verification.json)。

## 2. 环境阻塞与版本决定

环境记录包含真实的全球 Hub 3.14.5 安装尝试，失败发生在官方 Editor 下载来源校验，而不是 Local 会话模式。旧 Unity 2022 编辑器能启动，不等于目标 Unity 6 已具备条件。

首要解除条件：开发 owner 取得官方 Unity 6.3 LTS Windows Editor、所需 Windows 构建能力，并实际验证该 Editor 的许可证与启动。可以使用官方安装器或已经安装的官方 Editor 目录；记录来源、版本和哈希。用户若已取得相应资源，向原开发会话提供路径即可继续。

`6000.3.11f1` 是候选参考，不是唯一允许补丁。允许开发 owner 提交其他可获取的正式 6.3 LTS 补丁建议，附官方来源与 SDK 兼容依据；统一更新候选版本涉及的脚本、工程和报告，然后实际验证，再交 A0 冻结。不能只改版本字符串就宣称兼容，也不以 Unity 2022 或团结引擎的成功替代当前任务验证。

官方依据：[Unity 6000.3.11f1 发布页](https://unity.com/releases/editor/whats-new/6000.3.11f1)、[Cubism Unity README](https://github.com/Live2D/CubismUnityComponents/blob/master/README.md)。本次未找到已验证可用的替代下载结果；不要求反复重试已有相同 404 结果。

## 3. 两项需要原开发 owner 补齐的问题

### U01-00-R1 · P2：补全 UI 可调用的接口

[接口提案的 ISessionController](https://github.com/L1nkkkk/ai-companion/blob/5de03d46a048d1b976680aecc1e7c21962951b06/docs/reports/U01/U01-00/interfaces-proposal.md#L78) 要求 UI 只依赖该边界，但没有即时调整音量、列出本机会话或导出会话的入口。音量只出现在 IAudioPlayer，导出只出现在 IHistoryStore，会话列表在两层都未定义。按当前签名，UI owner 必须越过约定边界或自行增加不同接口才能完成既定功能，不能直接作为多人共享 API 冻结。

通过条件：在 Session 对 UI 的边界补充即时音量命令、有界历史列表查询、会话导出，以及必要的当前音量/设置快照；HistoryStore 对应提供有界列表查询。涉及设备选择时，同样给 UI 明确的设备枚举入口。提交完整类型映射与模块调用样例，经设计复核后由集成 owner 落地共享 Contracts。此项为接口设计补齐，不要求 U01-00 提前实现聊天或全部历史逻辑。

其余提案方向可沿用：OperationKey/TurnKey/DraftKey、先本机停播、旧结果隔离、实际输出振幅、PCM 所有权、历史写入代数、模块依赖方向，以及本机 runtime.json 候选位置。当前属于设计方向认可，完整共享 API 仍未冻结。

### U01-00-R2 · P2：给 Player 验证增加外部超时

[Test-Player.ps1 第 29 行](https://github.com/L1nkkkk/ai-companion/blob/5de03d46a048d1b976680aecc1e7c21962951b06/tools/unity/Test-Player.ps1#L29) 使用无限期 `WaitForExit()`。`-Seconds` 只交给 Player，由其 Update 自行退出；若初始化或主线程卡死，测试脚本会永久等待，无法输出这次稳定性测试的失败结果。

通过条件：测试脚本使用独立于 Player 主循环的墙钟超时（测试时长加明确宽限），超时后保留日志，记录 timeout / 非成功状态，只结束本次启动的测试进程，并返回非零。用能退出与不会退出的受控进程各验证一次，不得操作其他 Unity 或用户进程。正常 Player 的图片、结果及文件哈希校验仍保留。

## 4. 下一步与放行条件

1. 继续原 U01-00 开发会话，保持 PR #3；先补接口与测试工具的问题，同时解决 Editor 的实际取得条件。
2. 目标 Editor 可用后，由开发 owner 完成导入与 C# 编译、包解析，生成并审阅真实 packages-lock 和 ProjectSettings。
3. 提交准备完成的工程，以干净提交实际构建 Windows x64，交付完整 ZIP、源码 SHA、产物 SHA-256 与构建日志。
4. 启动同一包，提交真实模型画面、材质/遮罩/排序检查和运行时证据；明确哪些 UA02 行为由后续角色任务补齐，不以最小示例代表整项通过。
5. 提交修复与证据供 QA/A0 复核。U01-00 通过后，才从同一已批准提交并行安排 U01-01 角色、U01-02 UI/历史、U01-03 后台、U01-04 会话/音频。

等待 Editor 期间，可以继续官方资料研究和样例设计；不把待验证版本散发成各 agent 的已冻结开发基线。

## 5. 给原开发会话的续作提示

```text
继续 U01-00 和 PR #3。A0 对 5de03d46a048d1b976680aecc1e7c21962951b06 的结论为 awaiting_external，尚未放行后续实现。
先读取 design/unity-desktop-taskbook 分支的 docs/reports/U01/acceptance/PR-003-review.md。
完成 U01-00-R1 的 UI 音量/历史列表/导出接口补齐，以及 U01-00-R2 的 Player 外部超时与失败留证。
检查是否已获得可用的官方 Unity 6.3 Editor 或安装器路径；6000.3.11f1 是候选，可提出其他正式 6.3 补丁及依据，但需要统一修改候选配置并实测后交 A0 冻结。
若环境仍不可用，完成上述独立工作并准确列出外部资源缺口，不重复宣称任务完成。
环境可用后补齐 Editor 导入/编译、真实包锁、干净提交 Windows 构建、完整 ZIP 与 SHA-256、同包实际启动和画面/运行证据，更新原 PR 再交 A0。
```

A0 本次只读审阅实现与已有材料，更新设计文档和台账；未安装 SDK/Editor、修代码、代打包或自动启动新的开发会话。
