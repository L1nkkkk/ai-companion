# GitHub 与多个 agent 协作

正式私有仓库为 [L1nkkkk/ai-companion](https://github.com/L1nkkkk/ai-companion)，主分支 main。本地 origin 使用 HTTPS 地址。

## 在另一台机器克隆

先在该机器登录具有仓库权限的 GitHub 账号，然后：

```text
git clone https://github.com/L1nkkkk/ai-companion.git
cd ai-companion
```

若从 bundle 克隆，先用 `git remote set-url origin https://github.com/L1nkkkk/ai-companion.git` 替换已有 origin，然后 `git fetch origin`。身份验证由本机 Git / GitHub 登录处理，不把令牌写进 URL 或项目文件。推送会触发 Foundation 工作流，实际结果见 [Actions](https://github.com/L1nkkkk/ai-companion/actions)。工作流包含 Windows、Linux、macOS 的静态检查、契约检查、服务测试、网页构建和手机 JS 打包，原生编译与真机行为另行验收。

## 分配工作目录

先在仓库根运行 `git lfs install --local`。当前没有 LFS 资源；后续取得合法模型素材时由 A0/A3 处理资源清单、忽略规则和 LFS 配额，不把 SDK 缓存加入版本管理。

```text
git worktree add ../ai-companion-T04 -b agent/T04-contract-mocks main
git worktree add ../ai-companion-T01 -b agent/T01-ios-spike main
```

在每个 worktree 单独安装依赖，领取任务卡和起点提交。一个分支只分配给一个写入 agent。文件职责见 [AGENT_PLAYBOOK](../blueprint/AGENT_PLAYBOOK.md)；任务更新由 A0 写入 [台账](../blueprint/planning/tasks.json)。

共享文件由 A0 维护：根 package.json、pnpm-workspace.yaml、pnpm-lock.yaml、pyproject.toml、uv.lock、contracts、toolchain.json、CI，以及影响两端的原生工程配置。某个任务需要新依赖时，先给 A0 说明理由和影响。

完成时提交代码、检查输出和 docs/reports/Txx 交接报告。A0 复核契约和依赖后集成；资源不齐如实保留 awaiting_external 或 review。不要把“能编译”作为手机后台或直播权限的证据。
