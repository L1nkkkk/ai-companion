# U01 官方依据与版本边界

核对日期：2026-09-20。以下支持技术选型，不等于本机运行证据。网页与 master 会变化；U01-00 取得包后必须登记固定版本、来源与哈希。

| 编号 | 官方来源 | 本任务使用的事实 / 限制 |
|---|---|---|
| U-S01 | [Unity 6 支持说明](https://unity.com/releases/unity-6/support) | 官方列出 Unity 6.3 LTS。选择该版本族作为候选；没有把网页“最新”当作已锁定补丁 |
| U-S02 | [Cubism Unity 官方 README](https://github.com/Live2D/CubismUnityComponents/blob/master/README.md) | 提供 C# / Unity 组件；当前文档列出 6000.3.11f1、6000.0.71f1 开发环境，要求 URP 与 Input System；这不是对任意补丁或本机环境的保证 |
| U-S03 | [Cubism Unity 官方变更记录](https://github.com/Live2D/CubismUnityComponents/blob/master/CHANGELOG.md) | 查到正式 `5-r.5`，日期 2026-04-02；R5 改动需按版本处理，不能复用旧渲染配置并假定兼容 |
| U-S04 | [R4_1 之前版本与 R5 的差异](https://docs.live2d.com/en/cubism-sdk-manual/differences-from-before-unity-r4_1/) | 渲染管线、遮罩和绘制排序发生变化，开发任务必须检查 Player 中的实际材质/遮罩结果 |
| U-S05 | [URP 迁移说明](https://docs.live2d.com/en/cubism-sdk-manual/migration-from-birp-to-urp/) | 文档含 beta 时期说明，需与最终包 README/CHANGELOG 交叉核对；不能把 Built-in、URP、HDRP 配置混用 |
| U-S06 | [Cubism SDK for Unity 手册](https://docs.live2d.com/en/cubism-sdk-manual/cubism-sdk-for-unity/) 与 [官方下载](https://www.live2d.com/en/sdk/download/unity/) | Core 不在官方 GitHub 源码仓库分发，需要按官方 SDK 包获取。Framework、Core 的来源和适用条件分别记录 |
| U-S07 | [官方模型样例入口](https://www.live2d.com/en/learn/sample/) | 可用于选取演示资源；具体模型条件与能力由 U01-00/01 核实，不宣称所有样例均可任意再分发 |
| U-S08 | [Unity Application.runInBackground](https://docs.unity3d.com/ScriptReference/Application-runInBackground.html) | Android 真正进入后台时仍会暂停，iOS 忽略此设置；不能用桌面失焦继续运行证明手机后台语音可用 |
| U-S09 | [Cubism R4_1 固定提交 README](https://github.com/Live2D/CubismUnityComponents/blob/ca8babb42333a2e4407aa72a78a12d7268294455/README.md) | `5-r.4.1` 对应此提交，列出 `2022.3.61f1` / `6000.0.49f1` 开发环境；本机 `2022.3.62f3c1` 的短时实验依据另见 [ADR16](../../adr/0016-unity-2022-r41-fallback-validation.md)，不能冒充官方同版测试 |
| U-S10 | [R4_1 官方完整包](https://cubism.live2d.com/sdk-unity/bin/CubismSdkForUnity-5-r.4.1.unitypackage) | 开发 owner 获取并登记包哈希、Core 实际版本与资源条件；下载可达不等于导入或 Player 验证通过 |
| U-S11 | [正式 R5 固定 tag README](https://github.com/Live2D/CubismUnityComponents/blob/5-r.5/README.md) | 交叉核对 U-S04/U-S05 的 beta 时期描述：正式 R5 要求 URP，不支持 BiRP/HDRP；不能用 R4_1 / BiRP 成功替代 R5 测试 |

本任务的模块分工、临时协议、超时、缓存上限与验收目标是项目设计决定，不是 Unity 或 Live2D 官方提供的完整 AI 伙伴架构。手机后台和灵动岛继续依赖原有 [平台依据](../../blueprint/SOURCES.md)，本次未执行手机原生验证。
