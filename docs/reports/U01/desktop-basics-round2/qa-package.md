# 3110a56 新目录构建与便携包独立核对

2026-09-21，preview_backend 只读检查。**3110a56 已因真实嵌套覆盖确认取消缺陷退回，不是最终交付候选。** 此前的新目录来源链及完整包核对通过、0 个差异这一历史结果仍成立，但只属于 3110a56；不能把它或下面的成功样本转记到修复后的源码。修复必须产生新 SHA、全新 worktree 和新包，再核对并实测。独立复核者未启动 Player、后台或 Unity，也未修改任何包、清单、构建源文件。

本报告审查并随后退回的产品提交为 `3110a56f30671161ddfb9e39998c9972d4f3a34a`，便携目录 `C:/Users/Link/Dev/ai-companion-dev-tools/builds/NeuroSaki-Desktop-3110a56`，ZIP 为同级 `NeuroSaki-Desktop-3110a56.zip`。包与证据保持原样以便复现。前轮 b94af81 和本轮 dirty 初测同样仅保留为历史证据。

## 全新来源链

检查 `C:/Users/Link/Dev/ai-companion-dev-tools/evidence/desktop-r2-fresh-3110a56` 原始记录及真实构建日志；下表为 2026-09-20 UTC（本机日期为 2026-09-21）：

| UTC 时间 | 证据 |
| --- | --- |
| 20:55:27.118 | 创建前目标 `C:/Users/Link/Dev/Neuro-Saki-U01-round2-3110a56` 不存在，记录禁止复制 Library |
| 20:55:27.666 | 新 worktree HEAD 为 3110a56、git 状态干净，Library / Temp / SDK 均不存在 |
| 恢复阶段 | 从固定资源缓存恢复 SDK 5-r.4.1，归档 SHA 与冻结清单一致，恢复 561 条资源；原生 Core 5.1.0 / Mao 一致性及实际参数变形检查通过 |
| 20:55:35.438 | 恢复后、首次 Unity 前仍无 Library，HEAD 与干净状态未变 |
| 20:55:40.081 | Library 在本次首次导入阶段创建；Build.log 第 89 行明确记录因不存在 asset database 而重建 |
| 20:56:59.675 | 构建完成后 HEAD 仍为 3110a56、git 状态干净 |

本次不是旧 worktree checkout 到新提交后复用 Library。独立复核时再次读取 HEAD 和 git status，仍一致且干净。资源缓存三份原始文件哈希见 [independent-checks.md](independent-checks.md)，本次来源记录与日志各自哈希保存在 [qa-package.json](qa-package.json)。

首次 `Build.log` 位于 `C:/Users/Link/Dev/ai-companion-dev-tools/builds/desktop-r2-3110a56-evidence/Build.log`，实际退出 0；BuildReport 为 Succeeded、0 errors / 2 warnings，Unity 2022.3.62f3c1、Mono / D3D11。日志保留自有未使用事件 CS0067 与原 SDK CS0162 等诊断，不为消除警告修改 SDK。最终 Check 位于 `builds/desktop-r2-finalcheck-3110a56-evidence/Check.log`，独立确认 History 32、UI 34、Session/Audio 53，共 **119 项断言通过**，退出 0。

## 完整包检查

外清单与内清单的公共字段完全一致，`source_sha=3110a56…`、`source_dirty=false`。逐一读取并重算目录文件、ZIP 内解压成员及来源文件：

- 清单包含 3014 个文件；ZIP 包含这些文件与内清单，共 3015 个成员。无额外/缺失文件、大小写重复名、越界路径或符号链接；全部成员完整读取，CRC、长度和 SHA-256 一致。总未压缩大小 175174218 字节。
- 全部 226 个 Player 文件与本次 fresh build 清单匹配；43 个后台文件与 3110a56 工作区源码逐文件匹配。
- 2297 个 Python runtime 文件与固定 Python 3.12.10 standalone 来源匹配，436 个依赖文件与冻结环境来源匹配。只含清单声明的 13 个运行依赖，每个 dist-info 均保留许可证。
- 8 份资源/测试音频许可说明与来源文件逐字节匹配；Python 自身许可保留。资源来源、Mao 使用条件和测试 WAV 来源随包保留。
- launcher 与冻结提交源码一致，仍为经过原子 Job Object 强退回收检查的 `6feae148…` 指纹。本次未重新运行强退用例或启动程序。
- 全量文件清单无运行 config/token 容器、用户历史/设置、日志、数据库、Python 字节码或缓存目录。此结论限于检查到的文件清单与来源一致性，不声称可以证明任意编码内容都没有秘密。

| 产物 | 字节数 / SHA-256 |
| --- | --- |
| 3110a56 ZIP（已退回） | 70327532 字节；`ddcf9dccf2eb4db0882e3fab2ccf5d1623f24c77ff99c5e2035940e013e2a8ac` |
| 包内 package-manifest.json | `3fa75186a14341c56d98884285184b08809f5da701acb323cb981f5e1aaa1c1e` |
| 外部 package-manifest.json | `09f7648949b6dd0bd445983534bb1581bd208f1a487a039ed02984a2f141da9f` |
| launch_desktop.py | `6feae1488c22cadbb7205ef5192471ccba581f04eb98eb35cc571c9f0c368875` |
| 首次 Build.log | `b6921e1ede4cd50fdc9a39a07de22b1f846a3b2ab94d2d0973a8a5b6ea9cef6d` |
| 最终 Check.log | `e38617d738876b22e0b893a1120ee76dedf2b6e66afdd56be84e276e93dc7adf` |

结构化独立结果为 [qa-package.json](qa-package.json)，本次初始结果 SHA-256 为 `bb3cf6b60f30dfa3671e7b70536274c4b0bedb9d8b68b6e45185ab9bac0340f9`。里面逐项保存来源记录哈希、依赖与许可证、检查范围和空错误清单。

## 同包实测发现阻断，3110a56 退回

独立读取 `C:/Users/Link/Dev/ai-companion-dev-tools/evidence/desktop-r2-final/export-generation-cancel-delayed` 后确认，这次是有效的 **Thinking** 导出：Stopping 时采样 0/0，随后 Stopped 和 DialogOpen，没有错过指定阶段。root 在保存窗口输入同次 case 的 `overwrite-guard.json` 并打开覆盖确认，再切回 Player 按 Escape；原生窗口消失，但事件永远没有 Cancelled / Finished。全程主循环继续更新，不能把“窗口已消失”当作 `GetSaveFileNameW` 已返回或 STA 已结束。

正常 Alt+F4 后，Player.log 出现 `DESKTOP_CLOSE_TIMEOUT: local history or export cleanup did not finish within two seconds.`，随后有 Unity shutdown 日志，但进程仍未退出。root 核验程序路径后只强制结束自己启动的 PID 27640；`forced-cleanup.json` 保留时间 2026-09-20T21:09:20.8063266Z 和路径。独立复核未结束任何进程。

最终 `analysis-after-cleanup.json` 为 **failed**、`invalid_reasons=[]`，缺失原生导出终态和脚本完成；wrapper 返回 1 / `player_exit`。需区分：强制清理之后 `runtime_config_exists=false`、`package_unchanged=true`，不是遗留凭据文件；分析器合并的“launcher 未正常退出并清理”检查因退出不正常而失败。覆盖目标内容未被修改，原/后 SHA-256 都为 `853e008d6daeda9d5f3a6925da05e44c5add808de8fe20028d6db1ab4be3103a`。

| 3110a56 实际 case | 保留状态 |
| --- | --- |
| export-playback-save | 自动记录检查通过，Interrupted；保留人工观察 |
| export-playback-cancel | 自动记录检查通过；保留人工观察 |
| export-idle-save | 自动记录检查通过，Played；保留人工观察 |
| export-idle-cancel | 自动记录检查通过；保留人工观察 |
| export-generation-save-delayed | 显式 fixture 延迟环境，自动记录检查通过；保留人工观察 |
| export-generation-cancel | 第一次三秒窗口错过，invalid_attempt；不能计作生成中成功或产品失败 |
| export-generation-cancel-delayed | 有效 Thinking、覆盖确认取消与正常退出失败；本次退回依据 |

失败分析文件 SHA-256 为 `91b3315682e3772f4029c2a849165045197829249f7c2a0ae3158be7b18a3969`，强制清理记录为 `01026895768b2b07f60305507a32f9e307e51ace370544e71064f75abe3bc128`。全部 case 分析文件的独立哈希及候选退回状态另见 [qa-package-disposition.json](qa-package-disposition.json)；初始 `qa-package.json` 保持不覆盖。

修复审查方向是由原生 STA hook 串行处理取消，每次仅关闭最深一层提示，在保存窗重新可用且无子提示后才投递 IDCANCEL，而非同时向多个窗口投关闭。微软明确建议 hook 用 PostMessage / WM_COMMAND / IDCANCEL 结束 common dialog，禁止在 hook 中调用 EndDialog；是否解决本机复现仍须真实重跑，不能由静态方案推断通过。[Microsoft OFNHookProc](https://learn.microsoft.com/en-us/windows/win32/api/commdlg/nc-commdlg-lpofnhookproc)

此阶段没有执行全包重扫，以免影响 root 正在运行的 AV 预检。第二机器仍为外部待办；下一候选来源及完整 gate 结论等待新 SHA 和 A0 审查。
