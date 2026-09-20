# M4 MacBook Pro 接手清单

从 GitHub 克隆正式私有仓库。不要复制 node_modules、.venv 或开发机绝对路径。

```text
git clone https://github.com/L1nkkkk/ai-companion.git
cd ai-companion
git switch main
```

也可以先用 `git clone ai-companion-T00.bundle ai-companion` 离线恢复，再按 COLLABORATION.md 把 origin 切到正式仓库并获取最新提交。安装 README 中的固定 Node、Python、pnpm、uv 后执行：

```text
pnpm install --frozen-lockfile --ignore-scripts
uv sync --locked
uv run python tools/check.py
```

再按 README 启动网页与后台，确认页面能显示“基础服务已连接”。保存操作系统、工具版本、提交号和实际输出到 `docs/reports/T00/mac-reproduction.md`。这是 AC01 要求的第二台机器证据；本机临时目录的干净克隆只能提供部分证据。

## iOS 工具链准备

先核对 [资源清单](ENVIRONMENT.md) 与 Mac 系统、手机系统的兼容性。确认 Xcode 26.3、Ruby 3.3.9 和 Bundler 2.6.9 已安装并选用正确版本，然后：

```text
cd apps/mobile
bundle _2.6.9_ install
cd ios
bundle _2.6.9_ exec pod install
```

首次依赖解析后将 `apps/mobile/Gemfile.lock`、`apps/mobile/ios/Podfile.lock` 交 A0 审阅并提交；之后使用锁定解析。打开生成的 AICompanion.xcworkspace，配置个人开发团队，先在模拟器构建，再在已授权 iPhone 上安装。不要提交 Xcode 自动生成的本机 Node 路径、证书或签名私钥。

后台音频、灵动岛与真机 30 分钟验证属于 T01；成功启动空白 App 不能替代它们。Android SDK 可在 Windows 或 Mac 准备，按资源清单安装准确版本后再执行 T02。
