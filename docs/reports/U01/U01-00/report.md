# U01-00 开发交接：工程候选已准备，Unity 6 安装受阻

日期：2026-09-20。角色：Unity 开发与集成 owner。建议状态：**awaiting_external**。此 PR 可审阅工程准备与资源证据；**U01-00 尚未完成，不放行依赖任务，不声明 UA01、UA02、U-G0 或 U01 通过。**

## 起点与范围

- 远端：`https://github.com/L1nkkkk/ai-companion.git`。
- 实际起点：`design/unity-desktop-taskbook` 的 `765c635327685b413c7f930558711db7d88df60c`。该提交包含 ADR15 与完整 U01 任务书；当时 main 仍为 `0bfc133f1bd4c7b95a48e14e045101cb4fb38fda`。
- 独立分支：`agent/U01-00-unity-foundation`；工作树为仓库同级 `U01-00-unity-foundation`。原始本地目录为空，先克隆正式仓库到 `repository`，再由上述设计提交建立独立 worktree。
- 代码与干净目录复核提交：`f64596ebcf7f372848915e01e27bf752ae3dc4e9`。最终 PR 提交 SHA 以 PR head 为准；可复核代码提交和各项测试在 [verification.json](verification.json) 中记录，报告更新提交不冒充 Unity 已测提交。
- 修改仅限 `apps/unity` 初始骨架、`assets/manifest`、`tools/unity`、必要 `.gitignore` 和本报告目录。历史 React/RN、根依赖锁、CI、冻结 `contracts/` 及设计台账未修改。

## 已实际完成

1. 下载官方正式 Cubism Unity `5-r.5`，校验完整包、Core DLL、Mao 模型所有源文件与 `.meta`。没有使用 alpha/beta、第三方 Core 或来源不明的模型。
2. 下载官方固定提交的 Noto Sans CJK SC Regular `2.004` 与 OFL，记录来源、哈希和字符映射检查。详情见 [资源说明](../../../../assets/manifest/unity-foundation-resources.md) 与 [机器可读清单](../../../../assets/manifest/unity-foundation.json)。
3. 实际加载 Windows x64 Core：版本 `06.00.0001`；Mao 一致性检查成功，132 参数、262 drawable、37 使用遮罩、10 反向遮罩标记。驱动口型、左右眼、呼吸分别改变 13 / 13 / 13 / 156 个 drawable 的几何。见 [原生测试结果](native-assets-verification.json)。这不是 GPU/Unity 画面证据。
4. 建立带稳定 `.meta` 的 Unity 候选工程、最小场景、模块占位和两个 asmdef；提供官方资源恢复、编辑器准备、Windows x64 构建和真实 Player 证据采集入口。
5. 提交 [C# 接口与接线提案](interfaces-proposal.md)，等待 A0 审阅。没有改变正式 R1 契约，也没有提前实现完整聊天、云端、录音或音频状态机。
6. 执行仓库完整 `tools/check.py`：基础/冻结契约、蓝图、Python 格式检查与服务测试、TypeScript、历史网页构建、Android/iOS JS 打包通过。它们不证明 Unity 或手机原生构建通过。

## 精确版本建议及尚未冻结的部分

| 项目 | 建议 / 实际结果 |
| --- | --- |
| Unity | `6000.3.11f1` / `3000ef702840`；稳定 R5 README 的参考环境。下载/安装失败，尚非已验证版本 |
| SDK | 官方正式 `5-r.5`，SHA-256 `c9ac920b3a7359dc9ebe4ec0e9ad3c50367d615bdaeba3fc028141e90d45a0a1` |
| Core | 官方包内 Windows x64 `06.00.0001`，实际 C API 验证通过 |
| URP / Input System / uGUI | manifest 候选 `17.3.0` / `1.17.0` / `2.0.0`；尚未由 Editor 解析/编译 |
| 渲染设置候选 | 官方 Cubism URP renderer、HDR 关闭、Gamma、D3D11、Windows x64 Mono |
| 模型与字体 | 包内 Mao、Noto Sans CJK SC `2.004`；来源和许可分开登记 |

`ProjectVersion.txt` 和 `manifest.json` 是明确待验证的输入，不是最终冻结结论。没有手写 `packages-lock.json` 或伪造 Editor 导出的完整 ProjectSettings。取得 Editor 后必须运行准备、审阅生成文件、完成编译/构建/Player 验证，再交 A0 冻结。

## 安装尝试与真实缺口

见 [环境报告](environment-install.md)、[environment.json](environment.json) 及 [全球 Hub 安装日志](environment-evidence/hub-global-install-redacted.jsonl)。

本机现有 `2022.3.62f3c1` 可实际启动并成功更新许可证，Windows Standalone 文件存在。它不能证明 Unity 6 兼容。所需 `6000.3.11f1` 官方安装器请求重定向至 `download.unitychina.cn` 后返回 404；其他三个 6.3 LTS 补丁同样失败。随后取得签名有效的官方全球 Hub `3.14.5`，独立配置并真实执行安装请求，仍在来源校验阶段收到 404、进入 `download_failed`。没有替换现有编辑器/Hub或更改系统代理与安全设置。

需要补齐：**可取得的官方 Unity 6.3 Editor 安装器/安装目录及该版本可用许可证**。在此之前无法启动目标 Editor、安装其 Windows 模块、执行导入/编译或产出所要求的 Player。对当前网络的尝试已保存具体响应；不是根据 Local 模式推断不能开发。

## Windows 产物与验收状态

[build-manifest.json](build-manifest.json) 明确记录 `artifact=null`、`sha256=null`。**本次没有 U01-00 Windows `.exe` / ZIP，没有成功 Unity 构建日志，没有 Player 启动截图或录屏。** [构建前置检查](build-preflight.txt) 正确拒绝缺失的 Unity 6 与现有的 Unity 2022，不打开候选项目做降级迁移。

| 条目 | 本次结果 |
| --- | --- |
| UA01 | `awaiting_external`：资源取得和恢复可复核；Unity 6 干净工程导入、构建与包校验尚未执行 |
| UA02 | `awaiting_external`：Core/模型参数已真实运行；材质、遮罩、排序、10 分钟 Player 稳定性均未执行；完整眨眼/视线/招呼由 U01-01 补充 |
| UA03–UA12 | `not_run`：不借用基础仓库测试或原生 Core 探针宣称这些产品验收通过 |

## 从新目录复现和后续接线

[工程 README](../../../../apps/unity/README.md) 包含完整命令和模块表。先在新 worktree 检出本 PR 的代码提交，运行 `restore_assets.py`，再执行 `verify_native_assets.py`。上述步骤不要求 Editor；SDK/字体从官方固定 URL 恢复，哈希不符立即失败，不依赖作者绝对路径。源工程自己的 `.meta` 入 Git，下载资源的 `.meta` 从官方包恢复，缓存及构建输出被忽略。

当 Editor 可用后，用 PowerShell 7 运行 `Build-Windows.ps1 -UnityEditor <path> -PrepareOnly`。该操作通过 Editor 生成完整设置、管线资产、场景与包锁。审阅并提交生成文件后，运行不带 `-PrepareOnly` 的正式构建入口，避免反复重建场景而污染测试 SHA。构建成功才生成 ZIP 与完整文件哈希清单。然后对同一包运行 `Test-Player.ps1 -Seconds 600`，它将记录实际启动文件清单、日志、截图、帧时间与退出码。截图仍需人工/QA 检查遮罩与排序。

后续 agent 使用 A0 审阅后的精确提交；不能目前即把候选版本当成通过依赖。总场景、共享 Contracts、Packages、ProjectSettings 由集成 owner 单写；其他 owner 提交各自模块/子 prefab 并按 [接口提案](interfaces-proposal.md) 接线。Mao 口型参数是 `ParamA`，不是惯用的 `ParamMouthOpenY`。完整 UI 应采用 TMP 并单独测中文 IME；本候选场景只有一个 uGUI 只读范围标签。

## 未解决事项

- 目标 Unity 版本未安装；编辑器 C# 编译、URP 包解析、场景实际导入均待验证。静态审查发现的程序集名、绘制材质路径和动作控制器缺失问题已修正，但不等于编译通过。
- 没有可交付 Windows 程序，因此版本组合不能提交为最终兼容结论。
- 未测中文实际渲染、IME、DPI、完整角色交互、音频、停止延迟或任意云闭环。
- 尚未执行 A0 验收或更改设计台账；本 PR 保持草稿，恢复环境后由开发 owner 续作。

## 干净目录复核结果

在独立 detached worktree 和空下载缓存中重新从官方获取 SDK、字体及 OFL，恢复 1,582 个 SDK 资产条目，逐一复核 Mao 的 106 个源文件/`.meta`，核对 1,614 个唯一 GUID 与场景引用，原生模型探针再次通过；恢复后 Git 工作树保持干净。修改包哈希和路径穿越输入均被拒绝。见 [复现记录](clean-reproduction.json) 与 [原生复测](native-reproduction.json)。初次复核发现官方资源站拒绝 urllib 默认 User-Agent，已给下载器添加明确工具标识，并在空缓存完整下载复测通过。

仓库完整检查中的现存警告包括 Starlette/httpx、AnyIO 弃用提示，以及 RN feature flags exports 回退；均未修改冻结依赖来消除警告。
