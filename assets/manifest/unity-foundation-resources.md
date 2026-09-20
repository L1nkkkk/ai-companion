# U01-00 官方资源与使用条件

核对及下载日期：2026-09-20。机器可读版本、下载地址、文件大小、SHA-256、Mao 全部资源及 `.meta` 哈希见 [unity-foundation.json](unity-foundation.json)。本文件记录实际取得的资源；Editor、URP 包版本与 Player 验证结果以 [U01-00 报告](../../docs/reports/U01/U01-00/) 为准。

## 固定资源

| 资源 | 精确版本与来源 | SHA-256 |
| --- | --- | --- |
| Cubism SDK for Unity | 官方正式 `5-r.5`，2026-04-02；[官方完整 unitypackage](https://cubism.live2d.com/sdk-unity/bin/CubismSdkForUnity-5-r.5.unitypackage)，20,780,340 字节 | `c9ac920b3a7359dc9ebe4ec0e9ad3c50367d615bdaeba3fc028141e90d45a0a1` |
| Cubism Core Windows x64 | 上述包内 `Plugins/Windows/x86_64/Live2DCubismCore.dll`；调用公开 `csmGetVersion()` 返回 `0x06000001`，即 `06.00.0001` | `d883c00d114fdf6cef61f439feb23e02d000fdf683e092803010470b80dfaf09` |
| Niziiro Mao | 上述包内 `Samples/Models/Mao`，包括原始 prefab、模型、纹理、物理、动作和表情；[官方模型说明](https://www.live2d.com/en/learn/sample/niziiro-mao/) | 目录及父 `.meta` 的 106 个文件树哈希：`3e843d81c19eb80c3afb0b7f337103bff26ff8e94a08bf1d28cb3c07a496d46c` |
| Noto Sans CJK SC Regular | 静态 OTF `2.004`；[官方固定提交文件](https://raw.githubusercontent.com/notofonts/noto-cjk/523d033d6cb47f4a80c58a35753646f5c3608a78/Sans/OTF/SimplifiedChinese/NotoSansCJKsc-Regular.otf)，16,437,364 字节 | `2c76254f6fc379fddfce0a7e84fb5385bb135d3e399294f6eeb6680d0365b74b` |
| Noto 字体许可 | 同一提交的 [SIL Open Font License 1.1](https://raw.githubusercontent.com/notofonts/noto-cjk/523d033d6cb47f4a80c58a35753646f5c3608a78/LICENSE) | `6a73f9541c2de74158c0e7cf6b0a58ef774f5a780bf191f2d7ec9cc53efe2bf2` |

模型没有另猜一个语义版本；完整包版本与逐文件哈希共同确定其身份。树哈希算法写在 JSON 中：按路径排序，以 `路径 + NUL + 小写文件哈希 + NUL + 十进制字节数 + LF` 的 UTF-8 字节计算 SHA-256。包括上游 `.meta`，不包括 Unity 后续生成的缓存。

## 版本建议与证据边界

SDK 的[固定 `5-r.5` README](https://github.com/Live2D/CubismUnityComponents/blob/c598e36eb2302a7e8a6177b2c8e52e9c1da21923/README.md)列出 Unity `6000.3.11f1` 与 `6000.0.71f1` 开发环境。因此 `6000.3.11f1` 是本任务 Unity 6.3 LTS 的有据可查候选。R5 以 URP 和自定义渲染 Pass 为基础，并要求 Input System；Built-in 与 HDRP 不属于此包支持范围。URP、Input System 的最终精确锁定值必须来自实际导入与构建，不能从此候选说明推定已经兼容。

下载地址来自[官方下载页](https://www.live2d.com/en/sdk/download/unity/)加载的[公开下载元数据](https://cubism.live2d.com/sdk-unity/js/download.js)，其中正式 URP 项为 `5-r.5`；没有使用 beta、第三方 Core 或网上转存模型。Components 官方 tag 提交为 `c598e36eb2302a7e8a6177b2c8e52e9c1da21923`。Core 版本同时经包内 CHANGELOG 和实际公开 API 返回值验证。

## Mao 参数与后续角色接线

模型入口：`Assets/Live2D/Cubism/Samples/Models/Mao/Mao.prefab`。JSON 中保留了包内全部资源哈希，不把其他随包角色默认为获选角色。

| 用途 | 参数 / 映射 | 读取到的范围与注意事项 |
| --- | --- | --- |
| 口型 | `ParamA` | `0..1`，默认 `0`；这是 model3 的 LipSync 组，**不是** `ParamMouthOpenY` |
| 眨眼 | `ParamEyeLOpen`、`ParamEyeROpen` | `0..1.2`，默认 `1` |
| 呼吸 | `ParamBreath` | `0..1`，默认 `0` |
| 视线 | `ParamEyeBallX`、`ParamEyeBallY` | `-1..1`，默认 `0` |
| 头部朝向 | `ParamAngleX`、`ParamAngleY` | `-30..30`，默认 `0` |
| 待机 | model3 `Idle` | `mtn_01`、`sample_01` 两个动作 |
| 点击候选 | model3 `TapBody` | 六个动作，后续 owner 选择实际招呼动作；本清单不把动作名称等同于语义验收 |
| 表情候选 | `exp_01` 至 `exp_08` | 八个表情；情绪标签映射由 U01-01 实际检查后决定 |

使用官方 Core 实际复活并初始化 Mao：MOC 一致性返回 `1`；读取到 132 个参数、262 个 drawable，其中 37 个 drawable 使用遮罩、10 个带反向遮罩标记。它适合覆盖本任务普通与反向遮罩检查，但上述读取结果**不是 GPU 材质或 Player 截图证据**。

## 各自适用的条件

Components 受 [Live2D Open Software License](https://www.live2d.com/eula/live2d-open-software-license-agreement_en.html)约束；Core 受单独的 [Live2D Proprietary Software License](https://www.live2d.com/eula/live2d-proprietary-software-license-agreement_en.html)约束。保留上游版权和许可。Core 包内 `RedistributableFiles.txt` 明确列出 Windows x64 DLL；作为应用一部分分发时仍须遵守该协议的分发条件。此记录不将整个 SDK 重发布到源码仓库；开发者从官方来源取得并核对哈希。

开发测试可继续进行；未来对外发行需要按发布主体核实一般用户 / 小规模企业条件及发行许可适用性，不能因为 SDK 免费下载就宣称任意商业发布免费。可导入任意模型的 expandable application 另有适用条件；本次固定单模型工程验证不代表已获得未来产品发行许可。没有购买许可或发起收费服务。

Mao 在[样例条款](https://www.live2d.com/en/learn/sample/model-terms/)中属于 Live2D original character，适用 [Free Material License Agreement](https://www.live2d.com/eula/live2d-free-material-license-agreement_en.html)。一般用户及符合条件的小规模企业可在其许可范围内用于创作；其他主体有内部评估、监督用途等限制。使用时保留官方角色身份和版权，不以损害角色形象的内容使用；不擅自把随包声音数据作为可自由复用配音。当前清单选取模型资源，不授权任何未来生成的对话内容或音频。

应随使用原始角色的应用说明保留下列指定声明：

> This content uses sample data owned and copyrighted by Live2D Inc. The sample data are utilized in accordance with terms and conditions set by Live2D Inc. This content itself is created at the author’s sole discretion.

字体为 `© 2014-2021 Adobe (http://www.adobe.com/).`，许可为 SIL OFL 1.1，可随软件嵌入和分发，同时保留版权和许可；不能单独售卖或换成其他许可。修改字体还须遵守保留名称规则。取得的是静态 CFF OTF，不是 CFF2 可变字体。字体 cmap 检查中 `中文字体测试你好，世界！` 全部有映射；这不等于 TMP 渲染、中文输入法或任意汉字覆盖都已经验证。

## 复核

先按 [资源恢复工具](../../tools/unity/restore_assets.py)从官方固定包恢复资源，保留原始 `.meta`。已阅读上述许可后运行其 `--accept-live2d-terms` 选项；本选项是记录获取/使用条件，不代替用户或发布主体的后续发行义务。

Windows 64 位 Python 3.11+ 可独立运行：

```powershell
python tools/unity/verify_native_assets.py --output .bootstrap/unity/native-assets-verification.json
```

`--project` 可指向另一干净目录中已恢复的 Unity 工程。该工具使用标准库，先核对 Core、MOC 与 model3 哈希，再通过官方公开 C API 检查一致性、枚举参数和遮罩，并分别驱动口型、两眼和呼吸，要求几何数据实际发生变化。它不访问网络、不使用云密钥，也不会把原生 Core 验证写成 Unity 构建成功。

字体源文件与许可可直接从上表固定链接恢复，下载后使用 `Get-FileHash -Algorithm SHA256` 核对。资源需要下载的干净工程必须在 Unity 首次导入前完成恢复，不能用作者机器绝对路径作为工程依赖。
