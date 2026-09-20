# T01、T02、T03 并行可行性准备

日期：2026-09-20。范围：根据仓库与本机资源盘点，整理可立即执行的准备步骤、外部缺口和原验收矩阵。本文没有交付新的 spike 工程，没有执行手机原生构建、真实手机后台或真实直播接入，也不把 T00/T01/T02/T03 或完整 G0 标为完成。

依据：[T00 原报告](README.md)、[离线并行放行](parallel-readiness.md)、[现有任务卡](../../blueprint/TASKS.md)、[总架构](../../blueprint/ARCHITECTURE.md)、[正式契约](../../blueprint/CONTRACTS.md)、[完整验收表](../../blueprint/ACCEPTANCE.md) 和 [ADR15](../../adr/0015-unity-client-and-design-only-a0.md)。总架构允许真实资源验证与 mock 开发并行；本次桌面优先级见 [DESKTOP-PRIORITIES](../../development/DESKTOP-PRIORITIES.md)。

## 已知资源与阻塞

下表是本次本机只读盘点结论，不能用旧报告中另一台电脑的信息替代。未发现表示本次检查未取得证据，不表示用户没有该设备或账号。

| 资源 | 本次已知事实 | 影响与下一步 |
|---|---|---|
| Windows/Unity/Python | 当前 Windows Unity 与 Python 环境已完成实装、依赖、基础后台、干净 Player 构建及 600 秒渲染验证；原 worktree 本地报告位于 `C:\Users\Link\Dev\Neuro-Saki\docs\reports\U01\local-environment\README.md` | 可继续桌面与离线契约工作；不推导 Android/iOS 支持 |
| Java | 默认 Oracle JDK 8u461 位于 `C:\Program Files\Java\jdk-1.8`；另有 JDK 21.0.2，位于 `C:\Program Files\Java\jdk-21` | 未发现 JDK 17；现存 8/21 不能直接声称满足历史移动基线。T02 owner 应为实际实验工程核定匹配版本与路径，隔离准备，不覆盖其他项目全局配置 |
| Android SDK / Unity Android | 未发现完整 Android SDK、sdkmanager 或当前 Unity Android 模块 | Android 原生编译尚未验证；MuMu 自带 adb 不能代替完整工具链，也不能证明 Unity Android 可构建 |
| adb / 手机连接 | MuMu adb 36.0.0：`C:\Program Files\Netease\MuMu\nx_main\adb.exe`；本次 devices 列表为空，PnP 检查未发现手机 | 没有可用于此次真机验证的连接证据；不能断言用户没有手机。需用户实际接入拟测设备并完成设备端授权 |
| Mac / iPhone | 只有“用户持有 M4 MacBook Pro”的记录；没有本次 macOS/Xcode、签名、iPhone 型号或实际安装记录 | T01 的 Mac 构建、签名安装与真机矩阵待外部资源到位 |
| 直播 | 未找到实际账号授权、可用事件权限或真实事件接收证据；本次未读取凭据 | T03 的真实连接待指定账号、已授权测试直播间和能力确认。不能把文档、mock 或账号存在当作可接入证明 |

历史 [工具登记](../../development/ENVIRONMENT.md) 中的 RN/Gradle/CocoaPods 等版本属于旧工程起点；不能因表中写有版本就标为本机已安装。ADR15 以后，旧 RN 依赖锁与 Unity + Swift/Kotlin 实际工程的适用关系由对应 owner 登记，不为了消除旧待办继续扩建历史 RN 客户端。

## 独立目录与可运行准备

| 任务 | 原任务允许修改目录 | 当前可以准备 | 资源到位后才执行 |
|---|---|---|---|
| T01 / A5 | `spikes/ios-audio/`、`docs/reports/T01/` | 整理设备/系统/签名清单、测试音频与场景表、日志脱敏格式；在 Mac 上只读盘点工具 | 独立原生音频实验、签名安装、真实 iPhone 锁屏/后台/系统入口与中断测试 |
| T02 / A6 | `spikes/android-audio/`、`docs/reports/T02/` | 复查 Java/SDK/adb、明确实验所需工具版本及设备连接步骤、准备场景表和日志格式 | 完整工具链构建、授权安装、真实手机麦克风前台服务/通知/后台生命周期测试 |
| T03 / A7 | **`spikes/live-access/`、`docs/reports/T03/`** | 登记所需账号/直播间/能力项，准备脱敏事件字段和连接/断线测试表；使用明确标为 mock 的数据练习解析 | 使用真实授权接收指定测试直播间事件，验证实际能力、心跳与恢复并保存脱敏证据 |

这些是原任务的授权目录边界；列出准备步骤不等于本次已经创建或运行对应实验。T03 不自行进入 `services/api/app/live/` 扩建正式集成，后者按 T14 等后续任务处理。共享 schema、锁文件、原生公共配置或迁移仍需登记 owner 并审阅影响。

Windows 上可直接复查现有资源，以下命令不安装 SDK、不连接云服务、不结束其他进程：

```powershell
Get-Command java -ErrorAction SilentlyContinue | Select-Object Source
& 'C:\Program Files\Java\jdk-1.8\bin\java.exe' -version
& 'C:\Program Files\Java\jdk-21\bin\java.exe' -version
Get-ChildItem Env:ANDROID_HOME,Env:ANDROID_SDK_ROOT -ErrorAction SilentlyContinue
& 'C:\Program Files\Netease\MuMu\nx_main\adb.exe' version
& 'C:\Program Files\Netease\MuMu\nx_main\adb.exe' devices -l
Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue |
    Where-Object { $_.FriendlyName -match 'Android|ADB|iPhone' } |
    Select-Object Status,Class,FriendlyName
```

设备未显示时，检查连接、设备端开发/调试设置及授权状态；空列表不是一次真机测试。只把已连接且已授权的目标设备用于测试，报告保留型号/系统与脱敏设备标识，不公开完整硬件序列号。

Mac 资源可用时，在该 Mac 的终端执行只读盘点并记录输出摘要：

```sh
sw_vers
uname -m
xcode-select -p
xcodebuild -version
xcrun xctrace list devices
```

另由该机器使用者确认可用签名和目标 iPhone 安装条件，仅记录“已具备/缺少、类型、实测安装结果”，不把证书私钥、完整设备标识或账号令牌写入仓库。命令失败就保留具体失败；尚无 Mac 访问条件时不填写版本或签名成功。

T03 当前无账号证据，准备记录应列出平台、授权主体、测试直播间、获准事件类型、使用的接口路径、心跳/断线行为和测试窗口。账号登录、授权及直播间操作需要实际账号使用者完成相应交互。先按原任务验证 B 站；不可行时记录准确卡点，再对用户允许的其他官方接入类型作比较。本记录不重新推断平台当前开放条件、不读取现有凭据、不向平台或他人发送消息。

离线控制/音频/直播异常可使用 T04 的明确 mock 交付物准备；正式启动与测试命令以 T04 实际交付说明为准，不假定未交付服务存在。现有 `uv run python tools/check.py` 可复查仓库基础状态，但其通过不是 T01/T02/T03 实测。

## 原标准与测试矩阵

原 T01/T02 任务卡分别要求先做 **30 分钟**的最小真机实验；完整验收 AC10/AC11 为 **60 分钟**。两者是早期可行性证据和完整验收的不同范围，不能用 30 分钟通过替换 60 分钟条件。下表保持原数值及范围。

| 任务 / 验收 | 必须准备和真实执行的矩阵 | 完整通过条件与证据边界 |
|---|---|---|
| T01 / AC10 | 前台主动授权/开始，锁屏，切换应用，前后台恢复，静音/恢复，输入输出路由，结束；记录收音上传、播放及状态 | 真实 iPhone 60 分钟，至少 20 轮、两次前后台切换、一次锁屏、一次静音恢复、一次网络切换；关键音频/网络/续期由原生模块执行，不能依赖暂停的 Unity/JS |
| T02 / AC11 | 可见界面启动麦克风前台服务，通知状态/控制，锁屏、切换应用、静音、权限撤销、系统结束服务及下次启动状态；记录厂商电池管理 | 主力 Android 真机 60 分钟，覆盖同一 20 轮/切换矩阵，并补一个不同厂商或系统版本的回归；模拟器、adb 连接或安装空壳不能替代 |
| T01/T02 / AC12 | 支持灵动岛的 iPhone 真机、无灵动岛锁屏布局、Android 持续通知；逐个触发静音/停止并核对真实音频 | 系统入口状态与引擎一致，操作真实生效。无灵动岛布局可先在模拟器核查 UI，后台语音仍需真实 iPhone |
| T01/T02 / AC13 | 来电/系统音频中断、蓝牙与耳机拔出、权限撤销、15 分钟令牌过期、续期成功/失败 | 原生引擎控制录音、播放、连接和续期；失效时不假装继续监听，恢复不重播旧音频；每种情况留时间与状态证据 |
| T03 / AC15 | 指定已授权直播间真实评论、权限/事件类型、连续连接、断线重连、主播选消息、暂停自动回应与显式停止 | 至少一个已授权平台真实评论进入系统并被角色回应；真实测试至少连续 30 分钟。最小 spike 只收到事件时仅证明接入可行，不提前宣称整项 AC15 完成 |

其他相关原门槛继续保留：手机两小时混合使用报告流量/耗电/温度/断线属于 AC23；安装包与 iOS 签名测试版本属于 AC24；原验收记录的灵动岛 8 小时时限由活动结束事件验证清理和状态恢复，若未做实际 8 小时长测，应明确未测。不能从本准备记录推导这些已通过。

音频测试要用真实设备生命周期数据确认：静音后停止上传，结束后释放录音/播放/连接；停止先本机静音，再通知后台；旧 epoch/turn 的音频、字幕与动作不能复活。固定音频、回声服务或 provider mock 可以隔离云服务影响，但必须标明用途，不能据此声称真实云效果通过。禁止伪造通话、循环静音音频或无限重建系统活动掩盖缺口。

直播 mock 必须 `platform=mock` 且显式显示模拟；普通评论、指令评论、用户标识、礼物各项能力分别记录，缺字段或权限即为不支持/未验证。真实测试只在获准测试房间进行，样例需脱敏，不附 key、token 或完整私人日志。

## 证据交接与下一步

每个 owner 在各自报告目录记录：完整源码 SHA、允许/实际修改目录、操作系统与工具版本、设备型号/系统、构建/签名条件、测试模式、开始结束时间、样本和操作时序、实际结果、失败复现、证据位置及 SHA-256。构建退出 0、模拟器截图、连接到 adb、读到平台文档均只能证明对应步骤，不能合并成真机后台或平台接入成功。

本次准备状态：T01 等待 Mac/签名/iPhone 实际条件；T02 已盘点现有 Java/adb，缺完整已验证工具链及已连接授权真机；T03 等待实际授权与测试房间。三项资源准备可并行，离线 T04 和桌面 U01 可以继续。资源到位后按矩阵执行原任务，不减少样本/时长，不将外部缺口从 T00/G0 台账中自动移除。
