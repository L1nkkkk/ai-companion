# 工具版本与资源登记

登记日期：2026-09-20。机器可读版本在 [toolchain.json](../../toolchain.json)，JS 与 Python 依赖由 pnpm-lock.yaml / uv.lock 冻结。

**适用范围说明**：下表是 T00 导入时的环境与历史 React / RN 工具记录，不是 Unity 工程依赖。当前前台路线已改为 [Unity U01](../tasks/U01/TASKBOOK.md)；Editor、URP、Cubism、Core、字体与模型的确切版本尚待 U01-00 验证冻结。旧 Unity 2022.3 安装信息不能证明正式 R5 SDK 兼容；本设计会话没有安装或激活新 Editor。

| 项目 | 固定版本或状态 |
|---|---|
| 本机 | Windows 11，Core Ultra 7 265，约 64 GB，RTX 5060 8 GB |
| Node / pnpm | 24.19.0 / 11.19.0 |
| Python / uv | 3.12.10 / 0.12.17 |
| React / React Native / CLI | 19.2.3 / 0.87.1 / 20.2.0 |
| TypeScript / Vite | 6.0.3 / 8.3.0 |
| FastAPI / Uvicorn | 0.141.1 / 0.53.0 |
| Android | JDK 17；Gradle 9.4.1；AGP 9.2.1；Kotlin 2.2.0 |
| Android SDK | compile 37；target 36；minimum 29（Android 10）；Build Tools 37.0.0；NDK 27.1.12297006 |
| iOS | minimum 17.0；Xcode 26.3；Swift 5 语言模式 |
| Ruby / Bundler / CocoaPods / xcodeproj | 3.3.9 / 2.6.9 / 1.16.2 / 1.27.0 |
| Mac | 用户已确认：M4 MacBook Pro；尚未连接本任务，macOS 和 Xcode 实际版本待核实 |
| 真机 | Android 和 iPhone 型号、系统版本待登记 |
| 签名 | Apple 开发者与测试安装条件待确认；没有提交私钥或固定开发 Team |
| GitHub | 私有仓库 [L1nkkkk/ai-companion](https://github.com/L1nkkkk/ai-companion)，默认协作分支 main |
| AI / 直播 / Live2D | 密钥、额度、平台权限、正式模型与 Cubism Core 待提供 |

JDK 目前锁主版本 17；首次 Android 原生构建时还需记录发行商、补丁与 build。iOS 的 Gemfile.lock、Podfile.lock 需要在 Mac 上完成解析并提交，Windows 上没有伪造这些依赖锁。Xcode 26.3 要求 macOS 15.6 或更高的受支持版本；如手机系统要求更新 Xcode，由 A0 更新工具基线并记录验证。

移动 SDK、Gradle 和 Kotlin 数值取自固定 RN 模板与其 Gradle 插件，未凭经验混配。模板导入后按单仓库目录修正 Gradle、Metro 路径，最低系统版本按任务书设定。原生编译仍需要对应平台验证。

官方依据（2026-09-20 核对）：[React Native 版本](https://reactnative.dev/versions)、[官方模板](https://github.com/react-native-community/template)、[开发环境](https://reactnative.dev/docs/set-up-your-environment)、[Apple Xcode 支持表](https://developer.apple.com/xcode/system-requirements/)、[pnpm 配置](https://pnpm.io/settings)、[uv 锁与同步](https://docs.astral.sh/uv/concepts/projects/sync/)。
