# 技术依据与核实范围

核实日期为 2026 年 9 月 20 日。文档中的自定义架构、协议、性能目标和任务分工是本项目设计；下列官方资料支撑平台能力和限制。正式实现时应再次核对目标系统与 SDK 版本。

<a id="s01"></a>
## S01 苹果实时活动

[Displaying live data with Live Activities](https://developer.apple.com/documentation/activitykit/displaying-live-data-with-live-activities)

[ActivityKit](https://developer.apple.com/documentation/ActivityKit)

官方文档说明灵动岛与锁屏的呈现、实时活动自己的沙箱和更新方式，单次活动最长活跃8小时。实时活动的状态展示不能代替 App 音频后台执行。本文通过官方 Markdown 版本核对了正文。

<a id="s02"></a>
## S02 iOS 音频会话

[AVAudioSession playAndRecord](https://developer.apple.com/documentation/avfaudio/avaudiosession/category-swift.struct/playandrecord)

支持录音和播放，可配置锁屏与后台音频；需要录音授权。它并不证明本项目能在所有设备上无限持续监听。具体中断、恢复和长期使用必须真机验证。

<a id="s03"></a>
## S03 Android 前台服务

[Foreground service types](https://developer.android.com/develop/background-work/services/fgs/service-types)

[Restrictions on starting a foreground service from the background](https://developer.android.com/develop/background-work/services/fgs/restrictions-bg-start)

microphone 类型支持在符合条件时继续后台收音，并有麦克风授权和启动时机限制。mediaPlayback 用于后台播放。实现按目标版本检查，不依靠后台任意启动录音。

<a id="s04"></a>
## S04 iOS 构建环境

[Xcode SDKs and system requirements](https://developer.apple.com/xcode/system-requirements)

Xcode 的支持环境为 macOS，具体版本组合由构建时的稳定版本决定。本项目需要另行确认 Mac、签名和真机条件。

<a id="s05"></a>
## S05 Live2D 支持平台

[Cubism SDK Platform support status](https://docs.live2d.com/en/cubism-sdk-manual/platform/)

[官方 Web 示例](https://github.com/Live2D/CubismWebSamples)

官方列出移动浏览器支持并提供 Web 示例。Web SDK 的 Core 需按官方 SDK 获取说明准备。嵌入式 WKWebView 和 Android WebView 的表现仍需本项目验证。

<a id="s06"></a>
## S06 Live2D 口型

[Lip sync](https://docs.live2d.com/en/cubism-sdk-manual/lipsync/)

官方支持以音量驱动嘴部参数。本项目采用播放端实际输出振幅，并将复杂音素口型留作后续改进。

<a id="s07"></a>
## S07 OBS 网页来源

[Browser Source](https://obsproject.com/kb/browser-source)

OBS 可加载网页或本地文件作为画面来源，支持透明背景设置。实际音频路由及重复播放由项目测试确认。

<a id="s08"></a>
## S08 B 站官方入口

[哔哩哔哩直播开放平台](https://open-live.bilibili.com/)

[直播开放文档入口](https://open-live.bilibili.com/document/849b924b-b421-8586-3e5e-765a72ec3840)

确认存在官方平台与文档入口。当前访问的文档正文未被静态抓取完整呈现，因此本任务书不声称已核实最新全部准入条件，也没有核实项目账号权限。T03 必须在实际账号和可读取文档上完成验证。

<a id="s09"></a>
## S09 抖音官方接入类型

[直播玩法开放能力概述](https://developer.open-douyin.com/docs/resource/zh-CN/interaction/introduction/introduction/capabilitieslist)

[直播玩法文档指引](https://developer.open-douyin.com/docs/resource/zh-CN/interaction/introduction/userGuide/userguide)

[直播互动工具接入抖音云指南](https://developer.open-douyin.com/docs/resource/zh-CN/live-interactive-tools/development/douyin-cloud/live-interaction-guide-dycloud)

官方存在不同接入产品。直播玩法文档包含提案评估及能力申请；能力表中的部分评论能力针对特定指令；直播互动工具的网络请求路径涉及抖音云。不能据此断言所有抖音接入都能读取完整普通评论，也不能把某一产品条件当成全部方案条件。

<a id="s10"></a>
## S10 React Native 原生能力

[Native Platform](https://reactnative.dev/docs/native-platform)

官方提供原生模块和原生组件机制，可连接 Swift、Kotlin 等实现。此条只记录 T00 历史 RN 起点的依据；当前 Unity 路线按 ADR15 和 U01 资料设计，不继续据此扩展 RN 客户端。

<a id="s11"></a>
## S11 FastAPI 实时接口

[WebSockets](https://fastapi.tiangolo.com/advanced/websockets/)

用于服务端连接与断线处理的基础能力。业务状态机、租约、取消与恢复由本项目实现，框架本身不自动提供这些保证。

<a id="s12"></a>
## S12 Git 多目录协作

[git worktree](https://git-scm.com/docs/git-worktree)

[GitHub 大文件管理](https://docs.github.com/en/repositories/working-with-files/managing-large-files/about-large-files-on-github)

worktree 用于隔离并行任务。大模型、美术工程和贴图按大小采用 Git LFS；普通 GitHub 提交存在单文件大小限制。缓存、密钥和运行数据库不提交。

<a id="s13"></a>
## S13 Unity 与 Cubism SDK for Unity

当前 Unity LTS、SDK R5/URP、Core 获取和官方模型样例依据见 [U01 官方来源](../tasks/U01/SOURCES.md)。这些资料支持技术路线，不代表已经完成本机导入、Windows 构建或手机真机验证。
