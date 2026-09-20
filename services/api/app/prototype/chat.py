"""Explicit demo responses and an opt-in, server-configured cloud adapter."""

import asyncio
import json
import os
from collections.abc import AsyncIterator
from dataclasses import dataclass
from urllib.parse import urlsplit

import httpx


@dataclass(frozen=True)
class ChatConfig:
    mode: str
    url: str
    model: str
    key: str

    @property
    def configured(self) -> bool:
        try:
            parsed = urlsplit(self.url)
            valid_url = (
                parsed.scheme == "https"
                or (parsed.scheme == "http" and parsed.hostname in {"127.0.0.1", "localhost"})
            ) and not (parsed.username or parsed.password or parsed.query or parsed.fragment)
            return bool(parsed.hostname and self.model.strip() and self.key.strip() and valid_url)
        except ValueError:
            return False


def get_config() -> ChatConfig:
    return ChatConfig(
        mode=os.getenv("AIC_CHAT_MODE", "demo"),
        url=os.getenv("AIC_CHAT_URL", ""),
        model=os.getenv("AIC_CHAT_MODEL", ""),
        key=os.getenv("AIC_CHAT_API_KEY", ""),
    )


def demo_reply(text: str, name: str) -> tuple[str, str]:
    if any(word in text for word in ("故事", "story")):
        return (
            "有一颗小星星，每天都担心自己不够亮。它问月亮：‘别人都那么耀眼，我有什么用呢？’\n\n"
            "月亮指着窗边一个还没睡的人：‘你看，他今晚一直在看你。’\n\n"
            "后来小星星明白了：不必照亮整个宇宙，也可以成为某个人眼里刚刚好的光。",
            "happy",
        )
    if any(word in text for word in ("累", "难过", "烦", "焦虑", "压力", "不好", "休息")):
        return (
            "辛苦啦。先把今天那些必须做到的事情放一放，我们停下来一小会儿。\n\n"
            "喝口水，松一松肩膀。如果愿意，可以告诉我今天最让你疲惫的那件事；"
            "也可以什么都不说，就在这里待一会儿。",
            "gentle",
        )
    if any(word in text for word in ("计划", "工作", "学习", "专注", "项目", "开发")):
        return (
            "我们把大事情拆小一点吧。\n\n"
            "① 选一件今天做完会让你轻松一点的事。\n"
            "② 给它十分钟，只做第一个能看见结果的小步骤。\n"
            "③ 结束后回来，给自己记下一点进展。\n\n"
            "你想先从哪件事开始？",
            "thinking",
        )
    if any(word in text for word in ("你好", "介绍", "是谁", "名字", "hello", "hi")):
        return (
            f"你好呀，我是{name}。很高兴能在这里和你见面。\n\n"
            "现在是演示模式，我会用预设的几种回应陪你试试聊天、声音和角色互动。"
            "等接上云端模型，我们就能聊得更自由啦。",
            "happy",
        )
    if any(word in text for word in ("谢谢", "喜欢", "开心", "太好了", "可爱")):
        return (
            "收到你的这句话啦，今天的小星星又亮了一点。\n\n要不要也留一个小小的好瞬间给自己？",
            "happy",
        )
    if any(word in text for word in ("记住", "记得", "记忆")):
        return (
            "这次聊天会保存在当前浏览器里，可以从左边的记录重新打开，也可以随时清除。"
            "原型还没有长期记忆和跨设备同步，所以我不会把它们说成已经实现。",
            "neutral",
        )
    return (
        "我听到啦。这版演示会使用预设回应，还不能像真正的云端模型一样理解任意问题。\n\n"
        "你可以试着跟我说‘今天有点累’，让我讲一个小故事，或者一起拆解今天的计划。"
        "设置里也有接入云端对话的说明。",
        "neutral",
    )


async def stream_reply(messages: list[dict[str, str]], name: str) -> AsyncIterator[dict]:
    config = get_config()
    if config.mode == "demo":
        reply, emotion = demo_reply(messages[-1]["content"].lower(), name)
        await asyncio.sleep(0.25)
        for offset in range(0, len(reply), 6):
            await asyncio.sleep(0.035)
            yield {"type": "delta", "text": reply[offset : offset + 6]}
        yield {"type": "done", "emotion": emotion, "mode": "demo"}
        return
    if config.mode != "cloud" or not config.configured:
        yield {"type": "error", "message": "云端服务尚未配置，请检查服务端设置，或切回演示模式。"}
        return
    system = (
        f"你是名叫{name}的温和、自然的虚拟伙伴。用简洁中文回应，通常不超过200字。"
        "你只能对话，不声称已经执行外部操作，不声称具有未提供的长期记忆。"
        "不要把你的人设设定描述为真实人的经历。"
    )
    body = {
        "model": config.model,
        "messages": [{"role": "system", "content": system}, *messages[-12:]],
        "stream": True,
        "max_tokens": 450,
    }
    emitted = 0
    finished = False
    try:
        async with asyncio.timeout(65):
            async with httpx.AsyncClient(
                timeout=httpx.Timeout(30, connect=8), follow_redirects=False
            ) as client:
                async with client.stream(
                    "POST",
                    config.url,
                    json=body,
                    headers={"Authorization": f"Bearer {config.key}"},
                ) as response:
                    response.raise_for_status()
                    async for line in response.aiter_lines():
                        if not line.startswith("data:"):
                            continue
                        data = line[5:].strip()
                        if data == "[DONE]":
                            finished = True
                            break
                        chunk = json.loads(data)
                        choices = chunk.get("choices", [])
                        if not choices:
                            continue
                        choice = choices[0]
                        text = choice.get("delta", {}).get("content")
                        if isinstance(text, str) and text:
                            text = text[: 2400 - emitted]
                            emitted += len(text)
                            yield {"type": "delta", "text": text}
                        if choice.get("finish_reason"):
                            finished = True
                        if emitted >= 2400:
                            finished = True
                            break
        if not finished or not emitted:
            yield {"type": "error", "message": "云端回复未完整返回，请稍后重试。"}
        else:
            yield {"type": "done", "emotion": "neutral", "mode": "cloud"}
    except (
        httpx.HTTPError,
        TimeoutError,
        ValueError,
        KeyError,
        TypeError,
        AttributeError,
        IndexError,
    ):
        yield {"type": "error", "message": "云端服务暂时不可用，请检查连接和服务端配置后重试。"}
