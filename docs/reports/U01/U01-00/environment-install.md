> 历史记录：下文描述 R5/Unity 6.3 阶段。2026-09-20 已依 ADR16 完成 R4_1 正式迁移和 600 秒基础验证；当前状态见 [report.md](report.md)。R1/R2 已由 A0 提交 5286a56 关闭。

# U01-00 本机 Unity 环境实查

检查日期：2026-09-20。任务起点：`765c635327685b413c7f930558711db7d88df60c`。本报告只记录开发机环境，不代表 U01-00、U-G0 或 U01 验收通过。

## 结论

当前机器只有已发现的 Unity `2022.3.62f3c1`。该编辑器在独立临时目录中实际完成了 batchmode 空工程创建，日志记录许可证更新成功及退出码 `0`；已安装 Windows Standalone 模块，目录包含 Windows x64 Mono 开发版和非开发版运行时。

候选 Unity `6000.3.11f1` 尚未安装。官方安装器请求在当前网络被重定向到 `download.unitychina.cn`，最终返回 HTTP `404`。相同方式检查 `6000.3.22f1`、`6000.3.23f1`、`6000.3.24f1`，结果相同。之后实际取得并运行了官方全球版 Hub `3.14.5`，其 CLI 安装同一 Editor 也在来源校验阶段返回 HTTP `404`、进入 `download_failed`。**Unity 6 许可证可用性、导入、URP / Cubism 兼容性和 Windows Player 构建均未由本报告验证。** 旧编辑器的许可结果不能推断为 Unity 6 可用。

下一步需要在本机提供可使用的官方 Unity 6.3 LTS 编辑器或官方安装器，并确保对应版本具备适用的许可证。优先保留 SDK README 参考候选 `6000.3.11f1`；若采用其他确切 6.3 LTS 补丁，必须重新记录版本并完成 SDK 导入、Windows 构建和运行验证后再提交冻结建议。不能用当前 2022 编辑器或团结引擎替代候选组合并宣称验证完成。

## 实查环境

| 项目 | 结果 |
|---|---|
| 操作系统 | Microsoft Windows 11 专业版，`10.0.26200`，64 位 |
| CPU | Intel Core Ultra 7 265，20 核 / 20 逻辑处理器 |
| 内存 | 63.19 GiB 可见物理内存 |
| GPU | NVIDIA GeForce RTX 5060，驱动 `32.0.15.9571`；另有 Intel Graphics `32.0.101.6629` |
| C 盘可用空间 | 检查时约 1,212 GiB |
| Unity Hub | 已安装 `3.3.3-c7`，文件产品版本 `3.3.3.0` |
| 现有 Editor | `C:\Program Files\Unity\Hub\Editor\2022.3.62f3c1\Editor\Unity.exe` |
| 现有模块 | AndroidPlayer、WindowsStandaloneSupport；Windows 模块包含 win64 Mono / IL2CPP variation 目录，IL2CPP 工具链未验证 |
| 现有许可 | 许可文件存在；没有复制或展示文件内容；Editor 实际启动显示 `IsPro: 0`，许可证更新成功 |

## Unity 6.3 候选来源与下载尝试

候选来自 [Unity 官方发布说明](https://unity.com/releases/editor/whats-new/6000.3.11f1) 及 [官方 release API](https://services.api.unity.com/unity/editor/release/v1/releases?version=6000.3.11f1&limit=1)。API 元数据如下；这不是对已下载文件的校验结果。

| 字段 | 官方元数据 |
|---|---|
| 版本 | `6000.3.11f1`，LTS，2026-03-11 发布 |
| Revision | `3000ef702840` |
| Windows x64 安装器 | [UnitySetup64-6000.3.11f1.exe](https://download.unity3d.com/download_unity/3000ef702840/Windows64EditorInstaller/UnitySetup64-6000.3.11f1.exe) |
| 下载大小 | `4,183,040,280` 字节 |
| 安装大小 | `8,253,123,770` 字节（发布 API 估计） |
| 发布方 MD5 | `74af0e8736260889664604154e2c8995`（API 表示为 `md5-dK8OhzYmCIlmRgQVTiyJlQ==`） |
| 本机安装器 SHA-256 | 未取得完整安装器，不能计算 |

实际尝试：

1. 官方 HTTPS 安装器：HTTP 302 至同路径的 `download.unitychina.cn`，随后 HTTP 404。
2. 官方 beta 分发域相同 revision：重定向后 HTTP 404。
3. 官方 HTTP 地址，以及附 `download=1` / 独立缓存查询参数的 HTTPS 地址：仍重定向后 HTTP 404。
4. 官方 `unity3d.com/download_unity/...` 入口：重定向至 `unity.com` 后在 20 秒限制内未取得响应。
5. 官方 API 所列 `6000.3.22f1` / `1c726e1fb402`、`6000.3.23f1` / `09d2ecc7fb28`、`6000.3.24f1` / `4e7b9b5b6244` Windows 安装器：均为区域 CDN 重定向后 HTTP 404。
6. 现有 Hub 的两种 headless help 调用未返回可用帮助，其中一项报告 Licensing SDK logging callback 未注册；未通过该 Hub 安装新编辑器。
7. 通过机器上现成的本机代理端口执行同一官方 HTTPS 请求，响应仍为相同区域重定向。没有修改系统代理、网络路由、安全设置或账户凭据。
8. 官方 Download Assistant `UnityDownloadAssistant-6000.3.11f1.exe` 同样重定向后 HTTP 404。

## 实际全球 Hub 安装尝试

用户再次明确要求安装后，继续采用官方独立工具路线：

- 通用 [Hub 下载地址](https://public-cdn.cloud.unity3d.com/hub/prod/UnityHubSetup.exe) 实際下载 `132,149,984` 字节，产品版本 `3.3.6`，Authenticode 有效，签名主体为优三缔科技（上海）有限公司。SHA-256：`400944d2b1dc799da105862a027cd6f0f123dd4d0c3a3f0855d0147b65a3e922`。这是地区版 Hub 下载结果，未安装或替换现有 Hub。
- 从 [Unity 官方兼容版本文档](https://docs.unity.com/en-us/hub/install-legacy-hub) 的 [固定 3.14.5 下载链接](https://public-cdn.cloud.unity3d.com/hub/3.14.5/UnityHubSetup.exe) 取得全球版 Hub 安装器。大小 `159,101,280` 字节，产品版本 `3.14.5`，Authenticode 有效，签名主体 `Unity Technologies SF`。SHA-256：`697bb600791d712ecbd210c268ab5ed4b126110ed5c68239a61314cb0cfec3f9`。
- 使用现有 Unity 随附 7-Zip 解压官方安装器中的应用到 `../U01-00-tools/hub-3.14.5/`，以独立 `--user-data-dir` 运行。没有替换现有 Hub，也没有更改系统默认安装路径。
- 全球 Hub 的 headless help 成功；将其独立配置的 Editor 安装目录设为 `../U01-00-tools/UnityEditors` 后，真正运行了如下安装请求。

```powershell
$hub = '<workspace-parent>\U01-00-tools\hub-3.14.5\Unity Hub.exe'
# 本机实际通过 Start-Process 调用时，单个 --headless 参数生效。
& $hub --user-data-dir='<workspace-parent>\U01-00-tools\hub-probe-userdata' --headless install --version 6000.3.11f1 --changeset 3000ef702840 --errors
```

`2026-09-20T08:39:18Z` UTC 起的 Hub 原始日志确认识别 `6000.3.11f1` / `3000ef702840`，加入下载队列并进入 `download_validation`，随后对官方 Editor URL 的 `Source Availablity Check` 得到 HTTP `404`，转入 `download_failed`。该进程未自动退出，记录失败后只结束了本次创建的隔离 Hub 进程。脱敏原始记录随本报告提交为 `environment-evidence/hub-global-install-redacted.jsonl`。这次安装没有下载到 Editor 有效载荷。

这些是当前机器的可复现网络结果，不构成对地区许可或法律的判断。下载失败时未采用非官方重打包安装器、未改动现有编辑器、未读取输出或上传完整许可证。

## 现有编辑器的运行探针

仅为区分“本机可以运行 Unity”与“所需 Unity 6 尚未取得”，运行了独立的 2022 环境探针。目录位于工作仓库之外：`../U01-00-tools/license-probe-2022`。没有用它迁移或创建正式 Unity 6 工程。

```powershell
$editor = 'C:\Program Files\Unity\Hub\Editor\2022.3.62f3c1\Editor\Unity.exe'
$probe = '<workspace-parent>\U01-00-tools\license-probe-2022'
$log = '<workspace-parent>\U01-00-evidence\license-probe-2022.private.log'
Start-Process -FilePath $editor -ArgumentList ('-batchmode -nographics -quit -createProject "' + $probe + '" -logFile "' + $log + '"') -WindowStyle Hidden -Wait
```

日志包含许可证客户端验证警告，随后记录 `Successfully updated license`、`IsPro: 0`、`Exiting batchmode successfully now!` 和退出码 `0`。因此证据仅支持该旧编辑器本次启动和创建空工程成功。

附加空工程 Windows x64 构建探针退出 `1`：`'' is an incorrect path for a scene file. BuildPlayer expects paths relative to the project folder.` 原因是该环境探针没有保存可构建场景。没有据此宣称 Windows Player 构建成功，也没有继续用旧版本搭建替代产品工程。

## 证据与隐私

结构化摘要为本目录 `environment.json`。可公开的许可运行摘录、全球 Hub 安装记录及 HTTP 响应头随本报告提交在 `environment-evidence/`；其哈希见结构化摘要。更完整的原始和经过筛选的证据在仓库同级 `../U01-00-evidence/`：

- `release-api.json`：候选精确版本的官方 API 原始响应。
- `releases-current.json`：官方最近发布列表；用于选择实测的其他 6.3 LTS 补丁。
- `download-6000.3.*-http-headers.txt`：安装器请求的 HTTP 响应头，不含账户数据。
- `environment-license-probe-redacted.txt`：只保留许可更新、运行模式和退出结果的筛选摘录。
- `hub-global-install-redacted.jsonl`：全球 Hub 实际识别版本、来源校验 404 和进入失败状态的记录，不含身份或许可信息。
- `license-probe-2022.private.log`、`license-probe-2022-build.private.log`：原始本机日志，可能包含机器/账户标识，**不入 Git、不附公开分发包**。

本报告未安装 Unity 6，未更换许可证，也没有验证 Unity 6 的 Windows Mono 构建模块。后续安装成功后，应补充新编辑器精确路径、安装器 SHA-256、实际许可探针和正式项目构建证据，保留这次失败尝试供排障。

## 首轮审阅后的补充核查

2026-09-20 再次检查本机已知 Unity 安装目录、隔离工具目录和下载目录，没有发现新增官方 Unity 6.3 Editor 或安装器。Hub 还登记了一个现有自定义 `2022.2.8f1_e73d2c1eec46` 编辑器；实读其文件版本确认属于 Unity 2022，未启动或用于 U01 工程。原有官方 `2022.3.62f3c1` 同样不能替代所需环境。

父任务于 09:00–09:02 UTC 读取官方发布 API 的最近五个 6.3 版本，新增一次此前未试的 `6000.3.21f1` / `c02631ffc030` Windows x64 Editor HEAD 核查。该官方 EXE 仍从 `download.unity3d.com` 以 302 重定向至 `download.unitychina.cn` 后返回 404；该版本元数据没有提供第二个 Windows x64 Editor 下载项。未重复已失败的补丁请求，也未更改系统网络。此范围内仍没有可用安装来源，不能推断所有官方分发渠道都不可用。

公开证据为 [筛选后的官方元数据](review-fixes/unity-6000.3.21f1-metadata-excerpt.json) 与 [完整筛选响应头](review-fixes/unity-6000.3.21f1-official-head.txt)。元数据摘录记录原始完整 API 文件哈希，原文件保存在仓库同级 `U01-00-evidence/parent-continuation/`。其他平台 Editor 和构建模块没有被当作 Windows x64 Editor 使用。工程候选版本保持原样，未因一次失败的补丁查询改成未经测试的新版本。
