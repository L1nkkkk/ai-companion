# U01-00：Unity 2022 / R4_1 正式迁移交接

日期：2026-09-20。当前为开发验证阶段，最终版本、共享边界冻结及依赖放行由 A0 决定。依据设计提交 `000f2b7776abb9a87e63337eb6df4b7b0fcd5fa7` 的 [ADR16](../../../adr/0016-unity-2022-r41-fallback-validation.md)，正式候选已切换为 Unity `2022.3.62f3c1 / 1623fc0bbb97` + 完整 Cubism `5-r.4.1` / Core `5.1.0` + Built-in，Windows x64 / Mono / D3D11。原 PR #3 与任务分支继续使用，R1/R2 已关闭且行为保留。

## 本轮交付

- 成套替换官方 SDK、Core、shader、材质、Mao 和清单；旧 R5 资源隔离在项目 Assets 之外，不参与导入或打包。原候选和安装失败记录在 Git 历史与环境报告中保留。
- 真正由 Editor 生成的 ProjectSettings、场景、包锁及稳定 meta；uGUI 1.0.0、TMP 3.0.6。正式构建使用已提交场景，不自动重新生成。
- 按已审提案落实纯 C# Contracts：Session/UI 入口、不可变 DTO、typed events、操作身份与 PCM 所有权；无业务实现、Unity、SDK 或 HTTP 依赖。未细化字段的候选选择在其 README 中列出，仍交 A0 冻结。
- 实际参数/动作渲染 fixture：60 秒 19 阶段，使用 Core 图形数据、SDK 动作回调、真实截图、帧间隔和 Windows 内存计量。直接参数口型不是实际音频驱动，测试组件不是完整 AvatarPresenter。

## 已执行与正式复现

隔离实验已成功构建并运行 30 秒，记录见 [实验结果](fallback-r41/experiment-feasibility.json)。正式工程当前已完成整体导入/编译、场景生成及开发预检：完整 70 秒动作序列无错误，参数变化有实际几何响应，动作开始/结束回调齐全。另修正 Unity Mono 通用进程接口返回零内存的问题，改用 Windows GetProcessMemoryInfo，35 秒预检得到真实非零工作集/私有内存数据。

上述预检包含未提交修改，不冒充干净源码验证。下一步从本轮固定源码提交建立全新工作目录，空缓存恢复官方资源，不复制实验 Library，导入构建后对同一 Windows 包运行至少 600 秒。最终源码 SHA、产物 SHA-256、日志、图片、行为覆盖与性能结果将单独登记在本目录的正式验证证据中；未取得这些结果前不宣布通过。

## 复现和接线

[工程 README](../../../../apps/unity/README.md) 提供精确 Editor 获取入口、资源恢复、构建、Player 测试和模块所有权说明。[资源说明](../../../../assets/manifest/unity-foundation-resources.md) 区分 SDK 原包字节、恢复排除项及 Editor 重序列化。[接口提案](interfaces-proposal.md) 与 [实际 Contracts](../../../../apps/unity/Assets/Companion/Runtime/Contracts/README.md) 共同供后续 owner 接线。

[此前 verification.json](verification.json)、原生复现和环境报告保留为 R5 阶段历史，在本轮正式验证后更新当前索引；其中的旧测试 SHA 不代表 R4_1 已测源码。UA01/UA02 的基础子项按新证据交 A0 复核，完整 UA02、真实音频口型、UI/IME/历史/录音/云闭环以及 U-G0 不由此次工程迁移单独宣布通过。
