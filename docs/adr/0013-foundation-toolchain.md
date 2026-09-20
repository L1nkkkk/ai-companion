# ADR-0013：T00 正式仓库与工具基线

日期：2026-09-20。负责人：A0。状态：本地工程基线采用，原生编译待平台证据。

创建独立 ai-companion 仓库，导入设计包到 docs/blueprint；contracts 和 tools 只在根目录保留一份。固定 Node 24.19.0、pnpm 11.19.0、Python 3.12.10、uv 0.12.17。JS 采用一个 workspace 锁，Python 采用一个 uv 锁。具体依赖和原生构建数值见 toolchain.json。

手机使用官方 React Native Community Template 0.87.1（RN 0.87.1），保留 Swift / Kotlin 工程。单仓库使用 pnpm 的 hoisted 布局，Metro 监视仓库根，Gradle 显式引用根 node_modules。pnpm 11 的工程配置放 pnpm-workspace.yaml。网页与手机固定同一个 React 19.2.3。默认 Hermes 和新架构。

模板中的 Android 最低 SDK 24 改为 29，iOS 部署目标改为 17.0，与任务书保持一致。删除模板附带的调试 keystore，使用 Android 本机默认 debug 签名；release 保持未签名。调整 Ruby 顶层依赖到固定的 CocoaPods 1.16.2 / xcodeproj 1.27.0，以避免模板旧上界与当前 CocoaPods 不兼容。真正的 Gemfile.lock / Podfile.lock 在 Mac 验证时提交。

接口语义和 schema 沿用 1.0.0，不在 T00 实现未授权的业务范围。契约清单记录规范与样例的 SHA-256，文本换行统一后计算；变更须更新 ADR、版本、样例及清单。冻结不是锁死设计，而是阻止无记录的分歧。

T00 本地检查不使用 provider 密钥、不启动数据库、不下载 Live2D Core。网页提供工程预览；API 仅提供进程存活检查。完整 mock 和行为契约由 T04 实现。

现有 M4 MacBook Pro 已登记，尚未连接或运行。AC01 的第二机器验证、移动原生构建和全部 G0 真机任务仍需实际证据。工具更新由 A0 记录理由并重复相关验证，不由 agent 自行追踪 latest。
