# U01 桌面基础功能开发登记

用户于 2026-09-21 要求完成 `C:/Users/Link/Dev/Neuro-Saki-U01-plan/docs/development/DESKTOP-PRIORITIES.md`。
本会话领取实现及集成任务，不是 A0 设计会话。共同源码起点 `66b0e541a91aa727b33b3997b39bce178bbaee6a`；开发分支 `agent/U01-desktop-basics`。

本轮交付固定文字 / WAV 的桌面基础体验。真实云 LLM/TTS/ASR、声音模型工作及长期记忆按原文后置。根 R1 contracts、冻结 C# Contracts、Unity/SDK/依赖版本不变。

| 开发 owner | 目录与任务 |
|---|---|
| preview_backend | U01-03，unity_preview、服务测试；登记授权服务入口 app/main.py |
| desktop_ui | U01-02，Runtime/UI、Session/History、自有测试 |
| session_audio | U01-04，Session（排除 History）、Transport、Audio、自有测试 |
| root | U01-01 / U01-05，Avatar、Composition、总场景、Editor、构建/启动/验收工具、自有测试与交接 |

共享的 Unity 总场景、ProjectSettings、Editor 和工具由 root 单写。依赖锁及正式 contracts 未授权更改；共享 C# 签名无变更。本地环境原有未提交报告保持原样；用户指定的规划 worktree 保持原样。各模块完成后由集成 owner 构建 Windows Player 并记录实测；A0 最终验收与外部条件不能由本轮开发报告代替。
