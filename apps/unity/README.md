# SAKI Windows 桌面开发预览

本工程当前交付桌面基础体验：左侧真实 Live2D Mao，右侧中文聊天、状态与停止操作，本机历史和独立设置。界面持续标明 **演示模式、固定回复、测试音频**；测试音频不对应回复正文。这一版用于检查桌面交互和本机流程，不包含真实云端 AI、ASR、云端 TTS、音色克隆、长期记忆、手机后台或直播接入。语音输入尚未开放，不会自动录音。

桌面入口为 `Assets/Companion/Scenes/Desktop.unity`。旧 `Foundation.unity`、`Build-Windows.ps1` 和 `Test-Player.ps1` 仍用于 U01-00 渲染验收，与当前聊天应用分开。版本选择依据 [ADR16](../../docs/adr/0016-unity-2022-r41-fallback-validation.md)，工作范围见 [桌面派工记录](../../docs/reports/U01/desktop-basics/WORK-ORDER.md)。

## 使用便携包

1. 完整解压交付 ZIP，保留 Player、`python`、`backend` 和许可文件的目录关系。
2. 双击 `Start-NeuroSaki.cmd`。包内提供固定 Python 运行环境，使用者无需另装 Python、PowerShell 或 Unity。
3. 输入文字后 Enter 发送，Shift+Enter 换行；点击“停止 / Esc”或按 Esc 停止当前操作。关闭窗口退出应用及本次启动的后台。
4. 点击角色打招呼；音量、静音、自动朗读和“设置”用于控制下一次回复。关闭自动朗读后只请求文字。
5. “本机历史”可打开、导出和删除会话，也可清除全部。删除需要界面确认；导出使用 Windows 另存为窗口。

最终包的位置、源版本与 SHA-256 以交付报告及 ZIP 旁的 `*-package-manifest.json` 为准；中间开发包不能替代最终包。启动失败会显示原因和本机日志位置。若已有另一实例或 8000 端口被占用，先关闭占用它的程序，再重新启动。

## 固定开发环境

| 项目 | 当前版本或设置 |
| --- | --- |
| Unity Editor | 2022.3.62f3c1 / 1623fc0bbb97 |
| Cubism SDK / Components | 官方 5-r.4.1 / ca8babb42333a2e4407aa72a78a12d7268294455 |
| Native Core | 同包 Windows x64 5.1.0 / 0x05010000 |
| 渲染与构建 | Built-in、Gamma、D3D11、Windows x64 Mono |
| UI | uGUI 1.0.0、TextMeshPro 3.0.6，依赖解析以 packages-lock.json 为准 |
| 资源 | 同包 Mao、固定 Noto Sans CJK SC Regular 2.004 |
| 源码工具 | PowerShell 7、Python 3.12.10、uv；有效 Unity 许可证与 Windows Mono 构建支持 |

R4_1 的开发环境说明与本机 c1 补丁号并不完全相同，本项目用记录的构建验证该精确版本。不要混用 R5 的材质、Prefab、Framework 或 Core。资源来源、许可与原始哈希见 [资源说明](../../assets/manifest/unity-foundation-resources.md) 和 [资源清单](../../assets/manifest/unity-foundation.json)。

## 从源码恢复与启动

以下命令在仓库根目录运行。先阅读资源许可，再恢复被 Git 忽略的官方 SDK、模型与字体：

```powershell
uv sync --locked
uv run python tools/unity/restore_assets.py --accept-live2d-terms
uv run python tools/unity/verify_native_assets.py --output .bootstrap/unity/native-result.json
```

恢复器拒绝已有的 `Assets/Live2D` 或其根 meta，避免混合 SDK。需要重新恢复时，使用新工作区，或先由开发者完整归档旧资源；不要拷贝其他工程的 Library。可通过恢复器的 `--sdk-package` 使用校验通过的官方本地包，通过 `--cache` 指定下载缓存。

使用固定 Editor 打开 `apps/unity`，完成冻结依赖解析后关闭 Editor。正常构建使用已有场景、设置、TMP 资源与字体资产；不要每次重新生成场景。将下面 Editor 路径替换为目标机器上同版本的位置：

```powershell
$desktopEditor = 'C:/Users/Link/Dev/ai-companion-dev-tools/Unity/2022.3.62f3c1/Editor/Unity.exe'
$desktopOutput = Join-Path (Get-Location) ('.bootstrap/desktop/build-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
pwsh -File tools/unity/Build-Desktop.ps1 -UnityEditor $desktopEditor -OutputDirectory $desktopOutput
pwsh -File tools/unity/Start-Desktop.ps1 -Player (Join-Path $desktopOutput 'NeuroSaki.exe')
```

构建输出完整 Player 目录、邻接 ZIP、`<output>-evidence/Build.log` 和 `build-manifest.json`，并记录最近产物至 `.bootstrap/desktop/latest-build.json`。已有最近构建时可直接运行：

```powershell
pwsh -File tools/unity/Start-Desktop.ps1
```

源码启动器默认使用根目录 `.venv/Scripts/python.exe`，可用 `-Python` 指定兼容解释器。启动器创建随机本机令牌，只绑定 `127.0.0.1:8000`，等待后台就绪后启动 Player；不连接外部模型或语音服务。关闭 Player 只回收本次创建的后台，不终止其他占端口进程。直接双击 `NeuroSaki.exe` 缺少本次启动的后台配置，应使用启动器。

构建脚本拒绝其他 Editor 版本和非空输出目录；每次 Editor 调用有 30 分钟外部期限。源码有未提交改动时 manifest 标记 `sourceDirty=true`，这不是干净提交或第二台机器验收证据。完整原始日志可能包含机器或许可信息，仅发布检查过的证据。

仅当 TMP Essentials 缺失或需要重新生成桌面场景时，由集成 owner 使用以下维护入口。ImportText 需要先完成锁定包的解析；Prepare 会修改场景、设置和字体资产，生成后必须审阅差异：

```powershell
pwsh -File tools/unity/Build-Desktop.ps1 -UnityEditor $desktopEditor -Stage ImportText
pwsh -File tools/unity/Build-Desktop.ps1 -UnityEditor $desktopEditor -Stage Prepare
```

## 制作便携包

先从本次源码构建完整 Player，然后运行：

```powershell
$portableOutput = Join-Path (Get-Location) ('.bootstrap/desktop/portable-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
.venv/Scripts/python.exe tools/unity/package_desktop.py --player-directory $desktopOutput --output $portableOutput
```

打包器默认读取本机固定的 `C:/Users/Link/Dev/ai-companion-dev-tools/toolchain/python/cpython-3.12.10-windows-x86_64-none`；其他机器可通过 `--python-runtime` 和 `--site-packages` 指定同版本运行时与冻结依赖。输出目录、ZIP 及外部 manifest 必须尚不存在，不覆盖已有交付。

包内保留运行依赖的 dist-info 与许可证、Python 许可、Live2D/Core/Mao 和字体资源说明。运行配置、令牌、缓存与日志不进入包。包内 `package-manifest.json` 记录文件哈希，外部 manifest 另记录 ZIP 哈希与包内 manifest 哈希。详细打包边界和测试见 [便携包报告](../../docs/reports/U01/desktop-basics/portable-package.md)。

## 本机数据

默认位置为当前用户的 `%LOCALAPPDATA%/NeuroSaki`：

| 文件 | 用途 |
| --- | --- |
| `desktop/history/conversations.v1.json` | 本机历史，最多 20 个会话、每会话 80 条记录 |
| `desktop/settings.json` | 音量、自动朗读、音色与失焦策略，独立于历史 |
| `preview-runtime/config.json` | 启动期私有鉴权配置，关闭后清理；不要分享 |
| `preview-runtime/launcher.log` | 便携启动器的固定事件码诊断日志 |

重启恢复最近会话；未完成的回复标为中断，不自动补播。删除历史不清除设置。已导出的 JSON 由使用者自行管理，清除本机历史不会删除外部导出文件。原始录音不保存，当前版本也未开启麦克风。

## 自检与桌面验收

不新增 Test Framework 包，使用 Editor-only 自检入口：

```powershell
pwsh -File tools/unity/Build-Desktop.ps1 -UnityEditor $desktopEditor -Stage Check -CheckMethod Companion.Foundation.Editor.DesktopBuild.CheckModules
```

带自动测试序列的 Player 运行应使用独立数据目录，避免覆盖日常会话：

```powershell
$desktopQa = Join-Path (Get-Location) ('.bootstrap/desktop/qa-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
pwsh -File tools/unity/Start-Desktop.ps1 -Player (Join-Path $desktopOutput 'NeuroSaki.exe') -EvidenceDirectory (Join-Path $desktopQa 'evidence') -UserDataDirectory (Join-Path $desktopQa 'user-data') -TestSeconds 600 -Width 1280 -Height 800
```

源码自检、固定回复流程、实际 Windows 操作、真实音频输出和 OS DPI 是不同证据。通过内部停止回调不能直接宣称扬声器停止延迟已通过。中文 IME 候选、长按快捷键、125% / 150% DPI、设备切换、长时间运行及完整 U-G0/U-G1/U-G2 仍以逐项验收报告为准。

当前交接证据见 [UI 与历史](../../docs/reports/U01/desktop-basics/ui-history.md)、[Session 与音频](../../docs/reports/U01/desktop-basics/session-audio.md)、[后台](../../docs/reports/U01/desktop-basics/backend.md) 及同目录总报告；本 README 不宣告完整桌面门禁通过。

## 模块边界

| 目录 | 所属与约束 |
| --- | --- |
| Runtime/Contracts | 冻结接口；变更需统一评审消费者 |
| Runtime/UI、Runtime/Session/History、Tests/UI、Tests/History | U01-02；UI 只调用 Session 契约 |
| Runtime/Session（除 History）、Transport、Audio | Session / 音频 owner；身份、取消与实际播放 |
| Runtime/Avatar、Prefabs/Avatar | Avatar owner；Cubism 适配、动作和真实振幅口型 |
| Runtime/Composition、Editor、Scenes、Settings、Packages、ProjectSettings | 集成 owner；接线与共享配置 |
| Runtime/Foundation | 保留 U01-00 渲染与能力证据 |

开发 owner 保留 metas，在各自边界内工作。正式 R1 contracts、历史 React/RN 工程和用户现有本机环境记录不因桌面基础交付而改写。
