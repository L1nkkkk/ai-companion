# 桌面基础功能第二轮返工登记

用户于 2026-09-21 要求继续完成 `Neuro-Saki-U01-acceptance/docs/reports/U01/acceptance/desktop-basics-round1-review.md`。本会话继续承担开发与集成，不替代 A0 审阅，也不修改验收工作区。

起点：产品 `b94af81d73101d684b856bd0942de778ea74074a`、交接 `9852efd901e2c65b48321af6f97703821644a740`；分支 `agent/U01-desktop-basics`。

| Owner | 本轮范围 |
|---|---|
| desktop_ui / U01-02 | R1 异步原生导出、UI 生命周期、对应 UI 检查 |
| preview_backend / U01-04、06 | 导出等待历史写入失败的最小 Session 修复及检查；独立评审 R1 和 R2 包 |
| session_audio / U01-06 | R4 指定进程音频与实际渲染帧的 QPC 同步验证、离线编码工具 |
| root / U01-05、06 | Composition 接线与明确开启的 QA 记录；最终 SHA 全新工作目录恢复/构建/打包；真实导出、DPI 和音画操作及交接 |

SDK、模型、依赖、正式 contracts、共享 C# Contracts 保持冻结。root 单写 Composition、构建接线和共享配置；各 agent 不并行操作桌面或启动 Player。测试仅使用隔离目录、自有固定文字和测试音频；记录仅限本次 Player 画面及指定 PID 输出，不采麦克风或其他应用。

先修 R1，冻结最终产品提交后再直接建立新的工作目录完成 R2，不复制任何既有 Library。随后在该候选完成 R3 的实际系统缩放矩阵、R4 同步视听及相关取消/退出复测。系统缩放测试结束恢复原值；F04 仍为外部机器待办，不增加为本轮同机门槛。最终门槛结论由 A0 给出。
