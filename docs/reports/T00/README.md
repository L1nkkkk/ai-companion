# T00 交接报告

负责人：A0。日期：2026-09-20。状态：本地交付已就绪；任务台账为 awaiting_external，等待第二台机器复现及原生工具链补录后关闭 T00。

## 范围

建立正式独立仓库；导入任务书和唯一契约；准备网页、服务端及双端原生工程；固定工具与依赖；提供协作规则、CI 和第二台机器操作步骤。本任务不实现语音聊天或真实直播。

## 验证记录

起点提交：`eab4a5f`。先在开发目录运行，再从该提交克隆到开发目录之外的新目录，单独安装固定依赖和创建虚拟环境，执行同一检查入口。安装复用了本机下载缓存，未复制原 node_modules、.venv 或未提交源码。

| 检查 | 实际结果 |
|---|---|
| `pnpm install --frozen-lockfile --ignore-scripts` | 开发目录及独立克隆通过 |
| `uv sync --locked` | 新虚拟环境安装通过，锁文件未变化 |
| `uv run python tools/check.py` | 开发目录及独立克隆通过 |
| 规范与样例 | 27 个控制事件正例、8 个反例、8 个 REST 正例、25 个 REST 操作、64 字节音频头检查通过 |
| Python | 格式与静态检查通过；2 个真实 HTTP 路由测试通过 |
| TypeScript | 5 个 workspace 的类型检查通过 |
| 网页 | Vite 正式构建通过 |
| 手机 | Android / iOS JavaScript bundle 均成功；CLI 能正确识别两端原生工程 |
| 实际进程联通 | 临时启动 API 和 Vite，网页、直接健康检查、网页代理健康检查均 HTTP 200；随后停止测试进程 |
| Git | 工作文件与缓存分离；没有提交密钥、签名文件、node_modules、.venv 或运行数据 |

结构检查结果见 [blueprint-check.json](blueprint-check.json)，独立克隆完整检查输出见 [clean-check.txt](clean-check.txt)，测试环境与范围见 [verification.json](verification.json)。绝对个人目录已在保存的日志中替换为占位符。

已观察到但未隐藏的依赖警告：Starlette 对 HTTPX 和 BlockingPortal 别名的弃用提示；React Native 内部 feature flags 导出的回退解析提示。当前检查退出码为 0，两端 bundle 均生成；这些提示不能用于推断原生运行正常。后续依赖调整由 A0 统一处理。

## 验收边界

- AC01：本机独立干净克隆验证通过；**第二台真实开发机仍待验证**。下一步在 M4 MacBook Pro 按 SECOND_MACHINE.md 操作并提交报告。
- AC02：本阶段 schema、固定样例与 HTTP 健康检查契约通过；T04 继续建立客户端、服务端消费者及有状态协议行为测试。
- GitHub Actions：已提交三系统工作流，但未上传仓库，**没有声称远程 CI 已运行**。
- 原生编译：本机尚无 JDK / Android SDK，且没有连接 Mac；未产出 APK 或 IPA，未执行原生编译及真机测试。
- iOS 仍需在 Mac 上生成并提交 Gemfile.lock / Podfile.lock，登记 Xcode 与签名；Android 需补录 JDK 发行商与补丁。

其他任务可以阅读当前冻结契约和准备各自实现；T00 未全量验收前不把依赖状态自动改为 done，也不把 G0 宣布完成。

## 外部资源

- M4 MacBook Pro：用户已确认持有；系统和 Xcode 版本待现场登记。
- Android / iPhone：型号、系统、签名与实际安装尚未验证。
- GitHub：账号连接已核实，远程仓库尚未创建或推送。
- 云端服务、直播权限、Live2D 模型：后续任务登记。
