# 924b79f 最终运行后完整包复核

2026-09-21，preview_backend 独立只读复核。**全部最终运行结束后，目录全部文件及 ZIP 全部成员再次核对通过，差异 0；交付包与初始审查时完全相同。** 未启动 Player、服务或 Unity，未修改包、清单或最终构建源文件，也未停止任何进程。结构化结果见 [qa-package-924b79f-postrun.json](qa-package-924b79f-postrun.json)。

初始审查 [qa-package-924b79f.json](qa-package-924b79f.json) 保留 `2026-09-20T21:29:14.368391Z` 的原始时点及“运行后待复核”字段，文件字节未改，SHA-256 仍为 `9ee037b2676443de1949082b0374000f5cd259815b77300b0ed53dd8e9471ce2`。本报告补足该待办，不把初始结果改写为当时已完成实测。

## 最终交付指纹

| 对象 | 核对结果 |
| --- | --- |
| 产品 SHA | `924b79f05b6c90983bdc5c18344aa125375fa57c` |
| ZIP | `C:/Users/Link/Dev/ai-companion-dev-tools/builds/NeuroSaki-Desktop-924b79f.zip` |
| ZIP 大小 | **70330552 字节** |
| ZIP SHA-256 | `b14ca55facd93c55997968ad8d5fe07f3d0950809145b006bcc90a6d2f5e8036` |
| 内清单 SHA-256 | `b05472f60a49424f6c198f35980f472d0e2ff13f2116d0bc5c5885e411a8c934` |
| 外清单 SHA-256 | `9ac5564531e5fda585bf30642b3d3e9c073f719c83b2341a45bed180db724303` |
| launcher SHA-256 | `6feae1488c22cadbb7205ef5192471ccba581f04eb98eb35cc571c9f0c368875` |
| 文件数 | 内清单 3014 项；目录与 ZIP 各 3015 个文件，另含清单自身 |
| ZIP 解压字节数 | 175180215 |

重新读取目录每个文件的长度与 SHA-256；重新解压读取 ZIP 每个成员，校验 CRC、长度、SHA-256 和完整路径集合。内外清单公共内容完全一致，路径无越界、大小写重复、额外/缺失文件或符号链接。ZIP 整体指纹与初始值一致；许可证、226 个 Player 文件、43 个后台源文件、Python 与依赖来源比对的初始结论因此继续适用于相同字节。包内没有引入用户历史、运行配置、日志、数据库或字节码缓存。

最终 fresh worktree `C:/Users/Link/Dev/Neuro-Saki-U01-round2-924b79f` 的 HEAD 再次确认为上述产品 SHA，git 状态干净。来源恢复/首次导入/137 项 Unity 检查及 98 个不同 API/QA 测试的证据见 [初始独立报告](qa-package-924b79f.md)，此次未重复运行测试或构建。

## 运行清理

读取最终证据根的 17 份已结束 run-result：每份退出后包身份均与初始一致、launcher 退出 0、运行配置已清除。逐一确认各自 runtime 目录无 config.json 或 config-*.pending，正式用户默认 runtime 同样无此类配置。该清理结论不把两次无效导出尝试或 deadline 正常退出例的脚本诊断变成验收成功；R1 分类完整保留在 [R1 独立报告](r1-final-924b79f.md)。

扫描前只读查询进程可执行路径：本包路径下进程为 0；8000 端口 Listen 数为 0。没有读取进程命令行，也没有结束进程。root 已声明四格真实 DPI 操作均正常退出，并在 Windows Settings 恢复 display2 原来的 100%；该实际系统操作归 UI owner 观察，本包复核者不冒称亲自操作或独立验证系统设置。

本复核完成 R2 来源与最终同包完整性证据。第二机器仍待外部 owner，R3/R4 分别引用其专属记录，完整 U-G0 最终结论交 A0；不宣称云端语音、手机/直播或后置门禁已完成。
