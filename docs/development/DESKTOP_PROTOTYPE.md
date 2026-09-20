# 伴星桌面原型

当前无需 Mac、模型文件或云端密钥，可在这台 Windows 上体验角色与聊天。演示角色“小星”是原创 SVG 动画，聊天默认使用几种预设回应；界面始终显示演示标识。

## 打开

已安装依赖的电脑，在仓库根目录运行：

```text
pnpm dev:desktop
```

打开 [本机原型](http://127.0.0.1:5173)。Windows 也可双击根目录的 `start-desktop.cmd`。两个服务只监听本机；终端中按 Ctrl+C 结束。若端口 8000 或 5173 已占用，启动器会说明，不会关闭其他程序。

首次使用先按 [README](../../README.md) 安装固定依赖。启动器寻找当前仓库的 `.venv`，不依赖作者的绝对路径。macOS / Linux 可以启动文字界面，语音回退到浏览器音色；尚未进行实体 Mac 上的本版本测试。

## 体验顺序

1. 点击“小故事”“今天有点累”或输入“你好”，观察流式文字回复。
2. 默认自动朗读，Windows 使用已安装的中文系统音色。角色嘴型跟随真实音频音量；设置可修改音色、音量和语速，下次朗读生效。
3. 点击方形停止按钮或按 Esc，立即停止播放并取消剩余回复。发送新消息、开新聊天或切换记录也会停止旧回复。
4. 点击角色打招呼，切换晴日、薄荷和星夜，尝试沉浸模式。
5. 左侧记录可重新打开或删除聊天；聊天右上角可导出当前记录。设置可清除本机全部聊天，需点击确认。
6. 浏览器支持时，点击麦克风开始单次语音输入。识别结果留在输入框，确认后发送；没有权限或服务不可用会给出提示。网页不会自动开启麦克风。

记录保存在当前浏览器，最多 20 个会话、每个会话最近 80 条消息。使用不同浏览器或更换 `localhost` / `127.0.0.1` 地址会形成不同的存储空间。当前没有账号、长期记忆或跨设备同步。导出的文本文件由用户自行保管；网页删除不会删除已导出的文件。

## 接入真实云端对话

复制 `.env.example` 为根目录 `.env`，填写：

```dotenv
AIC_CHAT_MODE=cloud
AIC_CHAT_URL=你的完整HTTPS聊天接口地址
AIC_CHAT_MODEL=你的模型名称
AIC_CHAT_API_KEY=你的密钥
```

停止并重新运行 `pnpm dev:desktop`。使用支持流式 Chat Completions 格式的服务：POST 请求包含 `model`、`messages`、`stream` 和 `max_tokens`，响应使用 SSE 的 `choices[].delta.content`。适配器没有实现所有厂商特有参数；需要按实际 provider 验证。协议参考 [DeepSeek Chat Completions](https://api-docs.deepseek.com/api/create-chat-completion/)。

云端密钥只由本机后台读取，不进入网页或 Git。云端模式会发送人设与最近最多 6 条非空聊天；模型调用可能按服务商规则计费。完整 URL 不接受查询参数、嵌入凭据或非本机 HTTP。无效配置、超时、限流或服务错误会显示错误，不会静默切回预设回复。

本次没有配置或调用真实付费云模型，协议解析与失败处理使用隔离的模拟上游测试。界面显示“云端对话”意味着配置字段完整，不代表连通性已经测试成功，实际聊天结果会显示错误。

## 语音与资源

Windows 音色来自本机 [System.Speech.Synthesis](https://learn.microsoft.com/en-us/dotnet/api/system.speech.synthesis.speechsynthesizer.setoutputtowavestream?view=netframework-4.8.1)。服务把用户文字作为 JSON 标准输入交给固定脚本，不拼接成可执行命令；WAV 保留于内存，结束即释放。没有录音持久化逻辑。

其他系统使用浏览器 [SpeechSynthesis](https://developer.mozilla.org/en-US/docs/Web/API/SpeechSynthesis)；无法取得其波形时显示播放状态，不伪造音量口型。音色与可用性依赖实际浏览器和设备。

语音输入使用 [SpeechRecognition](https://developer.mozilla.org/en-US/docs/Web/API/SpeechRecognition)，浏览器可能把音频交给其在线识别服务。按钮与提示已说明此行为。自动化仅验证权限拒绝反馈，没有替用户开启实际麦克风，也没有完成中文识别质量验收。

角色与图标在源码中原创绘制，不需要下载外部美术资源。后续取得合法 Live2D 模型时按 T09 导入，不能把这个演示角色当成 Cubism 模型。

## 回归与交接

```text
uv run python tools/check.py
pnpm exec playwright install chromium
pnpm test:desktop
```

桌面测试使用独立的 Chromium 上下文；不读取个人浏览器数据。测试启动器在本机无服务时自动启动演示服务，已有服务必须是 demo 模式。CI 在 Linux 上额外运行浏览器流程；Windows 系统语音测试仅在有系统音色时执行，不在其他平台伪造成功。

[Playwright 本地服务](https://playwright.dev/docs/test-webserver)与[CI 运行说明](https://playwright.dev/docs/ci-intro)用于此回归配置。测试截图位于忽略的 `.tmp/desktop/`，失败 trace 位于忽略的 `test-results/`。

职责、迁移与契约边界见 [ADR14](../adr/0014-desktop-prototype-first.md)，本次实际证据见 [P01 交接报告](../reports/P01/README.md)。
