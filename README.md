# AI Companion

桌面语音聊天、直播弹幕互动，以及 Android / iOS 随身陪伴项目。当前可体验 **P01「伴星」桌面原型**：演示角色、预设文字聊天、系统语音、口型与打断、本机历史。保留服务端云模型接入口；真实 Live2D、直播和手机后台仍按原任务书推进。

正式私有仓库：[L1nkkkk/ai-companion](https://github.com/L1nkkkk/ai-companion)，主分支 `main`。其他机器通过 Git 克隆后按下方步骤安装。

## 从这里开始

- [总架构](docs/blueprint/ARCHITECTURE.md)、[任务卡](docs/blueprint/TASKS.md)、[任务台账](docs/blueprint/planning/tasks.json)
- [协作规则](AGENTS.md)、[接口说明](docs/blueprint/CONTRACTS.md)、[唯一契约目录](contracts/README.md)
- [T00 交接与验证](docs/reports/T00/README.md)、[工具与资源清单](docs/development/ENVIRONMENT.md)
- [第二台机器接手](docs/development/SECOND_MACHINE.md)、[GitHub 与并行开发](docs/development/COLLABORATION.md)
- [桌面原型使用说明](docs/development/DESKTOP_PROTOTYPE.md)、[桌面优先决策](docs/adr/0014-desktop-prototype-first.md)、[P01 交接](docs/reports/P01/README.md)

## 安装

安装 Node **24.19.0**、Python **3.12.10** 和 Git。工程工具为 pnpm **11.19.0**、uv **0.12.17**。Node 官网标准安装包附带 npm；若当前环境已提供固定版本 pnpm，直接使用它。

```text
npm install --global pnpm@11.19.0
python -m pip install --user uv==0.12.17
pnpm install --frozen-lockfile --ignore-scripts
uv sync --locked
uv run python tools/check.py
```

上述命令从仓库根目录运行。uv 若不在 PATH，可用 `python -m uv` 代替 `uv`。依赖下载不需要云端 AI 密钥。全部 Python 运行依赖由根 pyproject.toml 和 uv.lock 管理；tools/requirements-validation.txt 仅用于单独校验设计包。

## 运行开发预览

同时启动本机后台与桌面网页：

```text
pnpm dev:desktop
```

打开 [伴星桌面原型](http://127.0.0.1:5173)。Windows 也可双击 `start-desktop.cmd`。默认无需云端密钥，聊天使用明确标注的预设回应，角色为原创 SVG 演示形象。系统语音与云端配置见[使用说明](docs/development/DESKTOP_PROTOTYPE.md)。

后台提供 `/health/live` 与本机限定的 `/prototype/*`。`/health/ready`、`/v1/*`、账号和设备租约尚未实现；临时接口不是 R1 冻结契约。开发入口仅绑定本机，跨网手机测试由 T16 配置 HTTPS。

## 手机工程

```text
pnpm dev:mobile
pnpm --filter @ai-companion/mobile android
pnpm --filter @ai-companion/mobile ios
```

原生运行需要另外安装 Android 或 Apple 工具链，详见资源清单。`pnpm bundle:mobile` 只验证两端 JavaScript 能打包，不代表 Android APK、iOS App 或后台语音已验证。

## 协作原则

每个任务使用独立分支或 worktree。A0 管理架构、契约、根配置和锁文件；其他 agent 按任务卡修改目录。契约基线 1.0.0 由 `contracts/baseline.json` 记录，意外漂移会导致检查失败。原始音频、凭据、数据库和缓存不入库。

当前没有公开发布的项目许可证。官方 React Native 模板保留自身 MIT 许可，见 [第三方说明](THIRD_PARTY_NOTICES.md)。
