# 924b79f 全新构建与便携包独立核对

2026-09-21，preview_backend 独立只读复核。**最终候选 924b79f 的全新来源链及初始完整包检查通过，差异 0。** 本结论仅覆盖来源、构建、检查与包完整性；R1 实际导出矩阵、R3 系统缩放、R4 同步视听以及运行后再次核对另行记录，不据此宣布 U-G0 通过。复核者未启动 Player、后台或 Unity，未修改任何包文件或清单；产品启动和人工操作由 root 执行。

产品 SHA 为 `924b79f05b6c90983bdc5c18344aa125375fa57c`。便携目录为 `C:/Users/Link/Dev/ai-companion-dev-tools/builds/NeuroSaki-Desktop-924b79f`，双击入口为其中的 `Start-NeuroSaki.cmd`。旧 3110a56 已撤回，原来源核对、五次成功、一次阶段无效及一次真实失败仅属于旧候选，详见 [旧包退回记录](qa-package.md)；dev03 原生窗口结果仅为开发预检。

## 来源与构建

证据根为 `C:/Users/Link/Dev/ai-companion-dev-tools/evidence/desktop-r2-fresh-924b79f`，新 worktree 为 `C:/Users/Link/Dev/Neuro-Saki-U01-round2-924b79f`。下列时间为 2026-09-20 UTC：

| 时间 | 实际记录 |
| --- | --- |
| 21:25:44.596 | 创建前目标目录不存在；明确禁止复制 Library |
| 21:25:44.985 | 检出后 HEAD 为本次 SHA、git 状态干净，Library、SDK、Temp 均不存在 |
| 资源恢复 | 从固定缓存恢复 Cubism SDK 5-r.4.1，共 561 条资源；归档 SHA 为 `2777b69d4cd02fecd48dc0fe9871700c95943f6d141c18758027bd9aa2ed1de6`；原生 Core 5.1.0、Mao 一致性和参数变形检查通过 |
| 21:25:54.756 | 首次 Unity 导入前仍无 Library；HEAD 和干净状态一致 |
| 首次导入 | Build.log 第 89 行明确记录因 asset database 不存在而重建 Library |
| 21:26:56.900 | 构建结束后 HEAD 正确且工作区干净；本次产生 Library |

独立复核及测试前后再次读取 git HEAD/status，均一致且干净。资源缓存位置是 `C:/Users/Link/Dev/ai-companion-dev-tools/resources`；缓存文件哈希见 [独立检查](independent-checks.md)。没有复用旧 Library。

首次构建日志位于 `C:/Users/Link/Dev/ai-companion-dev-tools/builds/desktop-r2-924b79f-evidence/Build.log`，退出 0。Unity 2022.3.62f3c1 的 BuildReport 为 Succeeded，0 errors / 2 warnings，Player 大小 117847753 字节。最终检查日志位于 `builds/desktop-r2-finalcheck-924b79f-evidence/Check.log`，退出 0，History 32、UI 52、Session/Audio 53，共 **137 项断言通过**。保留原始警告，不把它们描述成零警告。

## 包完整性与来源比对

初始完整读取于 `2026-09-20T21:29:14.368391Z` 完成；结构化结果为 [qa-package-924b79f.json](qa-package-924b79f.json)。内外清单公共字段一致，`source_sha` 正确，`source_dirty=false`。

- 清单 3014 个文件，ZIP 3015 个成员（另含内清单），解压总大小 175180215 字节。目录和 ZIP 无额外或缺失文件、大小写重复名、越界路径或符号链接；每个成员完整读取，CRC、大小和 SHA-256 均匹配。
- 全部 226 个 Player 文件与本次 fresh build 清单及实际构建目录一致；43 个后台文件与最终源码一致。
- 2297 个 Python runtime 文件与固定 Python 3.12.10 standalone 来源一致；436 个依赖文件与冻结环境来源一致。13 个运行依赖的 dist-info 许可证均保留。
- 8 份资源与测试音频许可说明逐字节匹配来源，Python 自身许可证存在。
- launcher 与最终源码一致，指纹仍为 `6feae148…`，沿用既有原子 Windows Job Object 强退回收检查。此处没有重新启动进程做强退测试。强退可能留下 ACL 限制的旧配置，由下次持锁启动替换；不声称强退能执行 Python 清理。
- 文件清单不含运行配置、用户历史/设置、日志、数据库、Python 字节码或缓存。结论限于完整清单与来源匹配，不声称能够发现任意编码形式的秘密。

| 产物 | 字节数 / SHA-256 |
| --- | --- |
| ZIP | **70330552 字节**；`b14ca55facd93c55997968ad8d5fe07f3d0950809145b006bcc90a6d2f5e8036` |
| 包内 package-manifest.json | `b05472f60a49424f6c198f35980f472d0e2ff13f2116d0bc5c5885e411a8c934` |
| 外部 package-manifest.json | `9ac5564531e5fda585bf30642b3d3e9c073f719c83b2341a45bed180db724303` |
| launch_desktop.py | `6feae1488c22cadbb7205ef5192471ccba581f04eb98eb35cc571c9f0c368875` |
| 首次 Build.log | `1d915ab49977f637cb3e0b868e59c7b855a0f5f4e9c15f8bdf0ccd286739b9de` |
| 最终 Check.log | `bf19a6507449ffbeb1e797a46f3e1f9c20e4e05ccd08fbd7affee76ceaa3b572` |

## 冻结源码的独立 Python 检查

以原开发仓库现有 `.venv/Scripts/python.exe -B` 在最终新 worktree 执行 `-m pytest services/api/tests tools/unity/qa -q -p no:cacheprovider`，结果为 97 passed、1 skipped、2 个既有 warnings。唯一跳过项是依赖闭包/许可检查：它按仓库本地 `.venv` 查依赖，而全新 worktree 没有 `.venv`。确认原仓库也处于同一 924b79f SHA 且 API/工具无差异后，只补跑该项，1 passed。因此 **75 项 API + 23 项 QA，共 98 个不同测试通过，残余跳过 0**。没有安装依赖或改动新 worktree。另有报告封装的 3 项测试由 root 独立记录，不混入本次 98 项。

日志分别为 `C:/Users/Link/Dev/ai-companion-dev-tools/evidence/desktop-r2-924b79f-python-checks.log` 和 `desktop-r2-924b79f-dependency-check.log`，哈希及全部来源记录、依赖/许可证列表已写入结构化结果。该测试执行由 root 明确授权，可含短暂的隔离测试服务；不属于产品启动或人工验收。

## 最终运行后补证已完成

初始包审查时 root 正执行本候选 AV 与后续导出实测。全部运行完成后已再次读取目录全文件和 ZIP 全成员，CRC、长度、SHA-256、路径集合全部一致，差异 0；ZIP 仍为上述指纹。独立完成记录见 [运行后报告](qa-package-924b79f-postrun.md) 与 [结构化结果](qa-package-924b79f-postrun.json)。初始 JSON 保持原字节和原时点，因此其中的 pending 是历史状态，由后续记录明确完成。

17 份已结束运行记录均确认包未变、launcher 正常退出且配置清理；实际导出成功只计 [R1 报告](r1-final-924b79f.md) 中阶段正确的六格，无效尝试与额外自动退出脚本诊断保留原分类。第二机器仍待外部 owner，完整门禁由 A0 判定。
