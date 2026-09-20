# AI Companion

桌面语音聊天、直播弹幕互动，以及 Android / iOS 随身陪伴项目。当前客户端方向为 **Unity / C# + Live2D，先做 Windows 原型**。U01-00 的回退实验已真实构建并运行角色，A0 已接受进入正式工程迁移，尚未完成版本冻结与完整验收。首轮两项返工保持关闭，进度见 [A0 回退初评与下一步](docs/reports/U01/acceptance/R4_1-initial-assessment.md)。本分支维护设计与验收文档；现有 React / React Native 文件是 T00 历史工程起点。

用户已允许更换版本，[ADR16](docs/adr/0016-unity-2022-r41-fallback-validation.md)选择 Unity 2022.3 / Cubism R4_1 / BiRP。原开发 owner 下一步迁移正式工程、从干净提交构建并完成 600 秒及角色基础行为验证；后续模块实现尚未放行。

从 [U01 Unity 桌面原型任务书](docs/tasks/U01/TASKBOOK.md) 开始，当前先由原开发会话续作 U01-00；[派工提示](docs/tasks/U01/DISPATCH.md) 随验收状态使用。A0 当前会话只负责设计和验收，编码与打包由其他开发会话承担，规则见 [ADR15](docs/adr/0015-unity-client-and-design-only-a0.md)。历史 [P01 网页草稿](https://github.com/L1nkkkk/ai-companion/pull/1) 仅作交互参考，不是 Unity 交付。

正式私有仓库：[L1nkkkk/ai-companion](https://github.com/L1nkkkk/ai-companion)，主分支 `main`。其他机器通过 Git 克隆后按下方步骤安装。

## 从这里开始

- [Unity 原型任务书](docs/tasks/U01/TASKBOOK.md)、[接口](docs/tasks/U01/INTERFACES.md)、[验收](docs/tasks/U01/ACCEPTANCE.md)、[派工提示](docs/tasks/U01/DISPATCH.md)

- [总架构](docs/blueprint/ARCHITECTURE.md)、[任务卡](docs/blueprint/TASKS.md)、[任务台账](docs/blueprint/planning/tasks.json)
- [协作规则](AGENTS.md)、[接口说明](docs/blueprint/CONTRACTS.md)、[唯一契约目录](contracts/README.md)
- [T00 交接与验证](docs/reports/T00/README.md)、[工具与资源清单](docs/development/ENVIRONMENT.md)
- [第二台机器接手](docs/development/SECOND_MACHINE.md)、[GitHub 与并行开发](docs/development/COLLABORATION.md)

## 已有 T00 工具安装

以下固定工具用于现有后台与历史工程检查。Unity、渲染管线、Cubism、模型及资源锁定另由 U01-00 验证，不以这些命令代替 Unity 安装或构建。

安装 Node **24.19.0**、Python **3.12.10** 和 Git。工程工具为 pnpm **11.19.0**、uv **0.12.17**。Node 官网标准安装包附带 npm；若当前环境已提供固定版本 pnpm，直接使用它。

```text
npm install --global pnpm@11.19.0
python -m pip install --user uv==0.12.17
pnpm install --frozen-lockfile --ignore-scripts
uv sync --locked
uv run python tools/check.py
```

上述命令从仓库根目录运行。uv 若不在 PATH，可用 `python -m uv` 代替 `uv`。依赖下载不需要云端 AI 密钥。全部 Python 运行依赖由根 pyproject.toml 和 uv.lock 管理；tools/requirements-validation.txt 仅用于单独校验设计包。

## 运行历史 T00 工程预览

在第一个终端启动后台：

```text
uv run uvicorn app.main:app --app-dir services/api --host 127.0.0.1 --port 8000
```

在第二个终端启动网页：

```text
pnpm dev:web
```

打开终端显示的本地地址。网页显示基础服务连接状态。后台目前只提供 `/health/live`；`/health/ready` 和 `/v1/*` 尚未实现，不会假装业务已就绪。开发入口仅绑定本机，跨网手机测试由 T16 配置 HTTPS。

## 历史 T00 手机工程（暂停扩展）

下列命令只用于复查已导入的 RN 工程；新的客户端不按此路线继续扩展。Unity 手机客户端与原生后台桥接在桌面阶段之后另行派工。

```text
pnpm dev:mobile
pnpm --filter @ai-companion/mobile android
pnpm --filter @ai-companion/mobile ios
```

原生运行需要另外安装 Android 或 Apple 工具链，详见资源清单。`pnpm bundle:mobile` 只验证两端 JavaScript 能打包，不代表 Android APK、iOS App 或后台语音已验证。

## 协作原则

每个任务使用独立分支或 worktree。A0 管理架构、契约、根配置和锁文件；其他 agent 按任务卡修改目录。契约基线 1.0.0 由 `contracts/baseline.json` 记录，意外漂移会导致检查失败。原始音频、凭据、数据库和缓存不入库。

当前没有公开发布的项目许可证。官方 React Native 模板保留自身 MIT 许可，见 [第三方说明](THIRD_PARTY_NOTICES.md)。
