export type Emotion = "neutral" | "happy" | "gentle" | "thinking";
export type Phase =
  | "idle"
  | "thinking"
  | "responding"
  | "preparing"
  | "speaking"
  | "listening";
export type Message = {
  id: string;
  role: "user" | "assistant";
  text: string;
  status: "complete" | "pending" | "stopped" | "error";
  time: number;
};
export type Conversation = {
  id: string;
  title: string;
  created: number;
  messages: Message[];
};
export type Preferences = {
  name: string;
  autoSpeak: boolean;
  volume: number;
  speed: number;
  voice: string;
  scene: "morning" | "mint" | "night";
};
export type Capabilities = {
  mode: "demo" | "cloud" | "unconfigured";
  cloud_configured: boolean;
  model: string;
  voices: { name: string; language: string }[];
  avatar: string;
};
export const defaults: Preferences = {
  name: "小星",
  autoSpeak: true,
  volume: 0.8,
  speed: 1,
  voice: "",
  scene: "morning",
};
export const storageKey = "hoshi.desktop.v1";

export function newConversation(): Conversation {
  return {
    id: crypto.randomUUID(),
    title: "新的聊天",
    created: Date.now(),
    messages: [],
  };
}

export function loadWorkspace(): {
  conversations: Conversation[];
  preferences: Preferences;
} {
  try {
    const raw: unknown = JSON.parse(localStorage.getItem(storageKey) ?? "null");
    if (!raw || typeof raw !== "object") throw new Error("Empty workspace");
    const data = raw as Record<string, unknown>;
    const values = Array.isArray(data.conversations) ? data.conversations : [];
    const conversations: Conversation[] = values
      .slice(0, 20)
      .flatMap((entry: unknown) => {
        if (!entry || typeof entry !== "object") return [];
        const item = entry as Record<string, unknown>;
        if (
          typeof item.id !== "string" ||
          typeof item.title !== "string" ||
          !Array.isArray(item.messages)
        )
          return [];
        const messages = item.messages
          .slice(-80)
          .flatMap((entry: unknown): Message[] => {
            if (!entry || typeof entry !== "object") return [];
            const message = entry as Record<string, unknown>;
            if (
              typeof message.id !== "string" ||
              typeof message.text !== "string" ||
              !["user", "assistant"].includes(String(message.role))
            )
              return [];
            return [
              {
                id: message.id,
                text: message.text.slice(0, 2400),
                role: message.role as Message["role"],
                time:
                  typeof message.time === "number" ? message.time : Date.now(),
                status:
                  message.status === "pending"
                    ? "stopped"
                    : ["complete", "stopped", "error"].includes(
                          String(message.status),
                        )
                      ? (message.status as Message["status"])
                      : "complete",
              },
            ];
          });
        return [
          {
            id: item.id,
            title: item.title.slice(0, 40),
            created:
              typeof item.created === "number" ? item.created : Date.now(),
            messages,
          },
        ];
      });
    const stored =
      data.preferences && typeof data.preferences === "object"
        ? (data.preferences as Record<string, unknown>)
        : {};
    const preferences: Preferences = {
      ...defaults,
      name:
        typeof stored.name === "string" && stored.name.trim()
          ? stored.name.slice(0, 20)
          : defaults.name,
      autoSpeak:
        typeof stored.autoSpeak === "boolean"
          ? stored.autoSpeak
          : defaults.autoSpeak,
      volume:
        typeof stored.volume === "number"
          ? Math.min(1, Math.max(0, stored.volume))
          : defaults.volume,
      speed:
        typeof stored.speed === "number"
          ? Math.min(1.3, Math.max(0.8, stored.speed))
          : defaults.speed,
      voice: typeof stored.voice === "string" ? stored.voice : "",
      scene: ["morning", "mint", "night"].includes(String(stored.scene))
        ? (stored.scene as Preferences["scene"])
        : "morning",
    };
    return {
      conversations: conversations.length ? conversations : [newConversation()],
      preferences,
    };
  } catch {
    return { conversations: [newConversation()], preferences: { ...defaults } };
  }
}

export async function streamChat(
  messages: Message[],
  name: string,
  signal: AbortSignal,
  onDelta: (text: string) => void,
): Promise<Emotion> {
  const context = messages
    .filter((item) => item.text && item.status !== "error")
    .slice(-6)
    .map((item) => ({ role: item.role, content: item.text.slice(0, 2400) }));
  const response = await fetch("/prototype/chat", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ messages: context, character_name: name }),
    signal,
  });
  if (!response.ok || !response.body)
    throw new Error("暂时连接不上对话服务，请确认桌面服务已启动。");
  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let buffer = "";
  let completed = false;
  let emotion: Emotion = "neutral";
  const accept = (line: string) => {
    if (!line.trim()) return;
    const event = JSON.parse(line) as Record<string, unknown>;
    if (event.type === "error")
      throw new Error(
        typeof event.message === "string"
          ? event.message
          : "回复未完成，请重试。",
      );
    if (event.type === "delta" && typeof event.text === "string")
      onDelta(event.text);
    if (event.type === "done") {
      completed = true;
      if (
        ["neutral", "happy", "gentle", "thinking"].includes(
          String(event.emotion),
        )
      )
        emotion = event.emotion as Emotion;
    }
  };
  try {
    while (true) {
      const { value, done } = await reader.read();
      buffer += decoder.decode(value, { stream: !done });
      if (buffer.length > 65536) throw new Error("回复格式不正确，请重试。");
      const lines = buffer.split("\n");
      buffer = lines.pop() ?? "";
      lines.forEach(accept);
      if (done) {
        accept(buffer);
        break;
      }
    }
    if (!completed) throw new Error("连接中断了，已保留收到的文字。");
    return emotion;
  } finally {
    await reader.cancel().catch(() => undefined);
  }
}

export function speechChunks(text: string): string[] {
  const chunks: string[] = [];
  let remaining = text.replace(/[*#`]/g, "").trim();
  while (remaining.length > 400) {
    const part = remaining.slice(0, 400);
    const boundary = Math.max(
      part.lastIndexOf("。"),
      part.lastIndexOf("！"),
      part.lastIndexOf("？"),
      part.lastIndexOf("\n"),
    );
    const cut = boundary > 80 ? boundary + 1 : 400;
    chunks.push(remaining.slice(0, cut));
    remaining = remaining.slice(cut);
  }
  if (remaining) chunks.push(remaining);
  return chunks;
}
