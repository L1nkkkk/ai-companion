import { useCallback, useEffect, useRef, useState } from "react";
import { Avatar } from "./components/Avatar";
import { Icon } from "./components/Icon";
import { VoicePlayer } from "./audio";
import {
  loadWorkspace,
  newConversation,
  speechChunks,
  storageKey,
  streamChat,
} from "./model";
import type { Capabilities, Emotion, Message, Phase } from "./model";
import "./workspace.css";

type Run = {
  controller: AbortController;
  conversationId: string;
  messageId: string;
  serial: number;
  replay?: boolean;
};
type Recognition = {
  lang: string;
  continuous: boolean;
  interimResults: boolean;
  onresult:
    | ((event: {
        results: ArrayLike<ArrayLike<{ transcript: string }>>;
      }) => void)
    | null;
  onerror: ((event: { error: string }) => void) | null;
  onend: (() => void) | null;
  start(): void;
  abort(): void;
};
const speechWindow = window as typeof window & {
  SpeechRecognition?: new () => Recognition;
  webkitSpeechRecognition?: new () => Recognition;
};
const RecognitionConstructor =
  speechWindow.SpeechRecognition ?? speechWindow.webkitSpeechRecognition;
const prompts = [
  { icon: "leaf" as const, text: "今天有点累", note: "让心情慢下来" },
  { icon: "sparkles" as const, text: "讲个小故事", note: "给日常一点想象" },
  { icon: "sun" as const, text: "一起做个计划", note: "从一个小步骤开始" },
];
const phaseLabels: Record<Phase, string> = {
  idle: "在这里，陪着你",
  thinking: "让我想一想…",
  responding: "正在回复你…",
  preparing: "准备开口…",
  speaking: "想说的话，说给你听",
  listening: "正在听你说话…",
};
const timeLabel = (time: number) =>
  new Date(time).toLocaleTimeString("zh-CN", {
    hour: "2-digit",
    minute: "2-digit",
  });

export default function App() {
  const [initial] = useState(loadWorkspace);
  const [conversations, setConversations] = useState(initial.conversations);
  const [preferences, setPreferences] = useState(initial.preferences);
  const [currentId, setCurrentId] = useState(initial.conversations[0]!.id);
  const [draft, setDraft] = useState("");
  const [phase, setPhase] = useState<Phase>("idle");
  const [emotion, setEmotion] = useState<Emotion>("neutral");
  const [mouth, setMouth] = useState(0);
  const [wave, setWave] = useState(false);
  const [focus, setFocus] = useState(false);
  const [drawer, setDrawer] = useState<"history" | "settings" | null>(null);
  const [notice, setNotice] = useState("");
  const [clearConfirm, setClearConfirm] = useState(false);
  const [cloudHelp, setCloudHelp] = useState(false);
  const [capabilities, setCapabilities] = useState<Capabilities | null>(null);
  const [connection, setConnection] = useState<
    "connecting" | "connected" | "offline"
  >("connecting");
  const [browserVoices, setBrowserVoices] = useState<
    { name: string; language: string }[]
  >([]);
  const [retry, setRetry] = useState(0);
  const player = useRef(new VoicePlayer());
  const active = useRef<Run | null>(null);
  const recognition = useRef<Recognition | null>(null);
  const serial = useRef(0);
  const transcriptEnd = useRef<HTMLDivElement>(null);
  const input = useRef<HTMLTextAreaElement>(null);
  const waveTimer = useRef<number>(0);
  const modal = useRef<HTMLElement>(null);
  const conversation = (conversations.find((item) => item.id === currentId) ??
    conversations[0])!;
  const busy = phase !== "idle";
  const name = preferences.name.trim() || "小星";
  const voices = capabilities?.voices.length
    ? capabilities.voices
    : browserVoices;
  const modeLabel =
    connection === "connecting"
      ? "正在连接"
      : connection === "offline"
        ? "服务未连接"
        : capabilities?.mode === "cloud" && capabilities.cloud_configured
          ? "云端对话"
          : capabilities?.mode === "demo"
            ? "演示模式 · 预设回应"
            : "云端待配置";

  const patchMessage = useCallback(
    (conversationId: string, messageId: string, patch: Partial<Message>) => {
      setConversations((items) =>
        items.map((item) =>
          item.id === conversationId
            ? {
                ...item,
                messages: item.messages.map((message) =>
                  message.id === messageId ? { ...message, ...patch } : message,
                ),
              }
            : item,
        ),
      );
    },
    [],
  );

  const stop = useCallback(() => {
    // Silence locally before cancelling any network operation. A serial fences late results.
    player.current.stop();
    const run = active.current;
    active.current = null;
    serial.current += 1;
    if (run) {
      run.controller.abort();
      if (!run.replay)
        setConversations((items) =>
          items.map((item) =>
            item.id === run.conversationId
              ? {
                  ...item,
                  messages: item.messages.map((message) =>
                    message.id === run.messageId && message.status === "pending"
                      ? { ...message, status: "stopped" }
                      : message,
                  ),
                }
              : item,
          ),
        );
    }
    const mic = recognition.current;
    recognition.current = null;
    if (mic) {
      mic.onresult = null;
      mic.onerror = null;
      mic.onend = null;
      mic.abort();
    }
    setPhase("idle");
    setMouth(0);
    setEmotion("neutral");
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    let alive = true;
    setConnection("connecting");
    const timer = window.setTimeout(() => controller.abort(), 12000);
    fetch("/prototype/capabilities", { signal: controller.signal })
      .then(async (response) => {
        if (!response.ok) throw new Error("Unavailable");
        const value = (await response.json()) as Capabilities;
        if (
          !Array.isArray(value.voices) ||
          !["demo", "cloud", "unconfigured"].includes(value.mode)
        )
          throw new Error("Invalid capabilities");
        if (alive) {
          setCapabilities(value);
          setConnection("connected");
        }
      })
      .catch(() => {
        if (alive) setConnection("offline");
      })
      .finally(() => window.clearTimeout(timer));
    return () => {
      alive = false;
      controller.abort();
      window.clearTimeout(timer);
    };
  }, [retry]);

  useEffect(() => {
    const synth = window.speechSynthesis;
    if (!synth) return;
    const update = () =>
      setBrowserVoices(
        synth
          .getVoices()
          .map((voice) => ({ name: voice.name, language: voice.lang })),
      );
    update();
    synth.addEventListener("voiceschanged", update);
    return () => synth.removeEventListener("voiceschanged", update);
  }, []);

  useEffect(() => {
    const save = () => {
      try {
        localStorage.setItem(
          storageKey,
          JSON.stringify({
            preferences,
            conversations: conversations
              .slice(0, 20)
              .map((item) => ({ ...item, messages: item.messages.slice(-80) })),
          }),
        );
      } catch {
        setNotice("浏览器存储空间不足，这次聊天可能无法保存。可以先导出记录。");
      }
    };
    // Commit completed turns immediately; flush partial text on reload/navigation.
    const pending = conversations.some((item) =>
      item.messages.some((message) => message.status === "pending"),
    );
    const timer = pending ? window.setTimeout(save, 200) : undefined;
    if (!pending) save();
    window.addEventListener("pagehide", save);
    return () => {
      window.clearTimeout(timer);
      window.removeEventListener("pagehide", save);
    };
  }, [preferences, conversations]);
  useEffect(() => {
    transcriptEnd.current?.scrollIntoView({ block: "nearest" });
  }, [conversation.messages, phase]);
  useEffect(() => {
    if (!notice) return;
    const timer = window.setTimeout(() => setNotice(""), 5500);
    return () => window.clearTimeout(timer);
  }, [notice]);
  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        setDrawer(null);
        setFocus(false);
        stop();
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [stop]);
  useEffect(() => {
    if (!drawer) return;
    setClearConfirm(false);
    const previous = document.activeElement as HTMLElement | null;
    modal.current?.querySelector<HTMLButtonElement>("button")?.focus();
    return () => previous?.focus();
  }, [drawer]);
  useEffect(
    () => () => {
      active.current?.controller.abort();
      if (recognition.current) {
        recognition.current.onend = null;
        recognition.current.onerror = null;
        recognition.current.onresult = null;
        recognition.current.abort();
      }
      player.current.dispose();
      window.clearTimeout(waveTimer.current);
    },
    [],
  );

  const validRun = (run: Run) =>
    active.current?.serial === run.serial && !run.controller.signal.aborted;
  const say = async (text: string, run: Run) => {
    for (const part of speechChunks(text)) {
      if (!validRun(run)) return;
      setPhase("preparing");
      const options = {
        signal: run.controller.signal,
        volume: preferences.volume,
        speed: preferences.speed,
        onStart: () => {
          if (validRun(run)) setPhase("speaking");
        },
        onLevel: (value: number) => {
          if (validRun(run)) setMouth(value);
        },
      };
      if (capabilities?.voices.length) {
        const response = await fetch("/prototype/speech", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          signal: run.controller.signal,
          body: JSON.stringify({
            text: part,
            voice: capabilities.voices.some(
              (item) => item.name === preferences.voice,
            )
              ? preferences.voice
              : "",
          }),
        });
        if (!response.ok)
          throw new Error("暂时无法播放系统语音，文字回复已保留。");
        const bytes = await response.arrayBuffer();
        if (validRun(run)) await player.current.playWave(bytes, options);
      } else await player.current.playBrowser(part, preferences.voice, options);
    }
  };

  const send = async (value = draft) => {
    const text = value.trim().slice(0, 2000);
    if (!text) return;
    if (connection !== "connected") {
      setNotice("请先连接桌面服务，再开始聊天。");
      return;
    }
    stop();
    if (preferences.autoSpeak) player.current.unlock();
    const user: Message = {
      id: crypto.randomUUID(),
      role: "user",
      text,
      status: "complete",
      time: Date.now(),
    };
    const answer: Message = {
      id: crypto.randomUUID(),
      role: "assistant",
      text: "",
      status: "pending",
      time: Date.now(),
    };
    const run: Run = {
      controller: new AbortController(),
      conversationId: conversation.id,
      messageId: answer.id,
      serial: ++serial.current,
    };
    active.current = run;
    const context = [
      ...conversation.messages.filter((item) => item.status !== "pending"),
      user,
    ];
    setConversations((items) =>
      items.map((item) =>
        item.id === conversation.id
          ? {
              ...item,
              title: item.messages.length ? item.title : text.slice(0, 22),
              messages: [...item.messages, user, answer].slice(-80),
            }
          : item,
      ),
    );
    setDraft("");
    setPhase("thinking");
    setEmotion("thinking");
    let reply = "";
    try {
      const nextEmotion = await streamChat(
        context,
        name,
        run.controller.signal,
        (delta) => {
          if (!validRun(run)) return;
          reply += delta;
          patchMessage(run.conversationId, run.messageId, { text: reply });
          setPhase("responding");
        },
      );
      if (!validRun(run)) return;
      patchMessage(run.conversationId, run.messageId, { status: "complete" });
      setEmotion(nextEmotion);
      if (preferences.autoSpeak) {
        try {
          await say(reply, run);
        } catch (error) {
          if (validRun(run))
            setNotice(
              error instanceof Error
                ? error.message
                : "语音未能播放，文字已保留。",
            );
        }
      }
    } catch (error) {
      if (!validRun(run)) return;
      patchMessage(run.conversationId, run.messageId, { status: "error" });
      setNotice(
        error instanceof Error ? error.message : "回复未完成，请稍后再试。",
      );
    } finally {
      if (validRun(run)) {
        active.current = null;
        setPhase("idle");
        setMouth(0);
      }
    }
  };

  const replay = async (message: Message) => {
    stop();
    player.current.unlock();
    const run: Run = {
      controller: new AbortController(),
      conversationId: conversation.id,
      messageId: message.id,
      serial: ++serial.current,
      replay: true,
    };
    active.current = run;
    try {
      await say(message.text, run);
    } catch (error) {
      if (validRun(run))
        setNotice(error instanceof Error ? error.message : "语音未能播放。");
    } finally {
      if (validRun(run)) {
        active.current = null;
        setPhase("idle");
        setMouth(0);
      }
    }
  };
  const startNew = () => {
    stop();
    const next = newConversation();
    setConversations((items) =>
      [next, ...items.filter((item) => item.messages.length)].slice(0, 20),
    );
    setCurrentId(next.id);
    setDraft("");
    setDrawer(null);
    setFocus(false);
    input.current?.focus();
  };
  const removeConversation = (id: string) => {
    if (id === currentId) stop();
    const remaining = conversations.filter((item) => item.id !== id);
    if (!remaining.length) remaining.push(newConversation());
    setConversations(remaining);
    if (id === currentId) {
      setCurrentId(remaining[0]!.id);
      setDraft("");
    }
  };
  const clearHistory = () => {
    stop();
    const next = newConversation();
    setConversations([next]);
    setCurrentId(next.id);
    setDraft("");
    setClearConfirm(false);
    setNotice("本机聊天记录已清除。");
  };
  const exportChat = () => {
    const text = [
      `伴星 · ${conversation.title}`,
      ...conversation.messages.map(
        (item) =>
          `\n${item.role === "user" ? "我" : name} · ${new Date(item.time).toLocaleString("zh-CN")}\n${item.text}${item.status === "stopped" ? "\n[已打断]" : ""}`,
      ),
    ].join("\n");
    const url = URL.createObjectURL(
      new Blob([text], { type: "text/plain;charset=utf-8" }),
    );
    const link = document.createElement("a");
    link.href = url;
    link.download = `伴星聊天-${new Date().toISOString().slice(0, 10)}.txt`;
    link.click();
    window.setTimeout(() => URL.revokeObjectURL(url), 1000);
  };
  const greet = () => {
    setWave(true);
    setEmotion("happy");
    window.clearTimeout(waveTimer.current);
    waveTimer.current = window.setTimeout(() => {
      setWave(false);
      if (!active.current) setEmotion("neutral");
    }, 2200);
  };
  const toggleMic = () => {
    if (phase === "listening") {
      stop();
      return;
    }
    if (!RecognitionConstructor) {
      setNotice("这个浏览器暂不支持语音输入，可以先打字聊天。");
      return;
    }
    stop();
    const mic = new RecognitionConstructor();
    recognition.current = mic;
    mic.lang = "zh-CN";
    mic.continuous = false;
    mic.interimResults = true;
    const prefix = draft.trim() ? `${draft.trim()} ` : "";
    mic.onresult = (event) => {
      if (recognition.current === mic)
        setDraft(
          (
            prefix +
            Array.from(event.results)
              .map((result) => result[0]?.transcript ?? "")
              .join("")
          ).slice(0, 2000),
        );
    };
    mic.onerror = (event) => {
      if (recognition.current !== mic) return;
      setNotice(
        event.error === "not-allowed" || event.error === "service-not-allowed"
          ? "麦克风权限未开启，可以继续打字聊天。"
          : event.error === "no-speech"
            ? "还没有听到声音，再试一次吧。"
            : "语音输入暂时不可用，请检查麦克风和网络。",
      );
      recognition.current = null;
      setPhase("idle");
    };
    mic.onend = () => {
      if (recognition.current === mic) {
        recognition.current = null;
        setPhase("idle");
        input.current?.focus();
      }
    };
    try {
      mic.start();
      setPhase("listening");
      setNotice(
        "语音将转成输入框中的文字，确认后再发送。浏览器可能使用在线识别服务。",
      );
    } catch {
      recognition.current = null;
      setPhase("idle");
      setNotice("无法启动麦克风，请检查浏览器权限。");
    }
  };
  const setAutoSpeak = (enabled: boolean) => {
    if (!enabled) stop();
    setPreferences((value) => ({ ...value, autoSpeak: enabled }));
  };

  return (
    <div className={`workspace ${focus ? "focus-mode" : ""}`}>
      <aside className="rail" aria-label="主导航">
        <a
          className="brand"
          href="#"
          aria-label="伴星首页"
          onClick={(event) => {
            event.preventDefault();
            setDrawer(null);
            setFocus(false);
          }}
        >
          <Icon name="star" size={29} />
          <span>伴星</span>
        </a>
        <nav>
          <button
            className={!drawer ? "rail-item selected" : "rail-item"}
            title="聊天"
            onClick={() => setDrawer(null)}
          >
            <Icon name="chat" />
            <span>聊天</span>
          </button>
          <button
            className="rail-item"
            aria-label="新的聊天"
            title="新的聊天"
            onClick={startNew}
          >
            <Icon name="plus" />
            <span>新聊天</span>
          </button>
          <button
            className={
              drawer === "history" ? "rail-item selected" : "rail-item"
            }
            aria-label="聊天记录"
            title="聊天记录"
            onClick={() => setDrawer("history")}
          >
            <Icon name="history" />
            <span>记录</span>
          </button>
        </nav>
        <div className="rail-bottom">
          <button
            className={
              drawer === "settings" ? "rail-item selected" : "rail-item"
            }
            aria-label="偏好设置"
            title="偏好设置"
            onClick={() => setDrawer("settings")}
          >
            <Icon name="settings" />
            <span>设置</span>
          </button>
          <div className="user-avatar" title="本机空间">
            我
          </div>
        </div>
      </aside>
      <main className="main-body">
        <header className="topbar">
          <div className="breadcrumb">
            我的空间<span>/</span>
            <strong>陪伴时刻</strong>
          </div>
          <div className="connection-area">
            <span className="preview-label">DESKTOP PREVIEW</span>
            <button
              className={`mode-badge ${connection === "offline" ? "offline" : ""}`}
              onClick={() =>
                connection === "offline"
                  ? setRetry((value) => value + 1)
                  : setDrawer("settings")
              }
              title={connection === "offline" ? "点击重新连接" : "查看对话设置"}
            >
              <i />
              {modeLabel}
            </button>
          </div>
        </header>
        <section className="page-heading">
          <div>
            <p className="eyebrow">A LITTLE COMPANY, A BETTER DAY</p>
            <h1>
              今天，也想陪在你身边<span>。</span>
            </h1>
          </div>
          <div className="today">
            <Icon name="sun" size={17} />
            {new Date().toLocaleDateString("zh-CN", {
              month: "long",
              day: "numeric",
              weekday: "long",
            })}
          </div>
        </section>
        <div className="main-grid">
          <section
            className={`character-stage scene-${preferences.scene}`}
            aria-label="角色互动区"
          >
            <div className="stage-heading">
              <div>
                <div className="character-name">
                  {name}
                  <span>XIAOXING</span>
                </div>
                <p>给日常一点温柔的回声</p>
              </div>
              <button
                className="icon-button glass"
                aria-label={focus ? "退出沉浸模式" : "沉浸模式"}
                title={focus ? "退出沉浸模式 · Esc" : "沉浸模式"}
                onClick={() => setFocus((value) => !value)}
              >
                <Icon name={focus ? "close" : "expand"} size={18} />
              </button>
            </div>
            <div className="scene-art" aria-hidden="true">
              <div className="sun-halo" />
              <div className="arch-window">
                <i />
                <i />
                <i />
              </div>
              <div className="orbit orbit-one" />
              <div className="orbit orbit-two" />
              <span className="floating-star star-one">✧</span>
              <span className="floating-star star-two">✧</span>
              <span className="scene-dot dot-one" />
              <span className="scene-dot dot-two" />
              <div className="stage-shadow" />
            </div>
            <div
              className="avatar-position"
              data-mouth-level={mouth.toFixed(3)}
            >
              <Avatar
                mouth={mouth}
                emotion={emotion}
                wave={wave}
                name={name}
                onTouch={greet}
              />
            </div>
            {wave && <div className="hello-bubble">嘿，我在呢 ♡</div>}
            <div
              className={`stage-caption ${busy ? "active" : ""}`}
              role="status"
            >
              <span className="sound-bars" aria-hidden="true">
                {[0.6, 1, 0.7, 0.9, 0.5].map((scale, index) => (
                  <i
                    key={index}
                    style={{ height: `${4 + mouth * scale * 20}px` }}
                  />
                ))}
              </span>
              {phaseLabels[phase]}
            </div>
            <div className="stage-bottom">
              <div className="scene-row">
                <div className="scene-picker" aria-label="场景选择">
                  {(
                    [
                      { id: "morning", label: "晴日", icon: "sun" },
                      { id: "mint", label: "薄荷", icon: "leaf" },
                      { id: "night", label: "星夜", icon: "moon" },
                    ] as const
                  ).map((scene) => (
                    <button
                      key={scene.id}
                      aria-pressed={preferences.scene === scene.id}
                      onClick={() =>
                        setPreferences((value) => ({
                          ...value,
                          scene: scene.id,
                        }))
                      }
                    >
                      <Icon name={scene.icon} size={14} />
                      {scene.label}
                    </button>
                  ))}
                </div>
                <span className="demo-label">演示角色 · 非 Live2D</span>
              </div>
              <div className="stage-controls">
                <button
                  className="control-circle"
                  aria-label={
                    preferences.autoSpeak ? "关闭自动朗读" : "开启自动朗读"
                  }
                  title={
                    preferences.autoSpeak ? "自动朗读已开启" : "自动朗读已关闭"
                  }
                  onClick={() => setAutoSpeak(!preferences.autoSpeak)}
                >
                  <Icon
                    name={preferences.autoSpeak ? "volume" : "muted"}
                    size={19}
                  />
                </button>
                <button
                  className={`talk-button ${phase === "listening" ? "listening" : ""}`}
                  onClick={toggleMic}
                  disabled={!RecognitionConstructor}
                  title={
                    RecognitionConstructor
                      ? "点击开始语音输入，可能使用浏览器在线识别服务"
                      : "当前浏览器不支持语音输入，请使用右侧文字聊天"
                  }
                >
                  <Icon name="mic" size={18} />
                  {phase === "listening" ? "结束语音输入" : "跟我说说话"}
                </button>
                <button
                  className="control-circle stop-button"
                  aria-label="打断回复"
                  title="打断回复 · Esc"
                  disabled={!busy}
                  onClick={stop}
                >
                  <Icon name="stop" size={16} />
                </button>
              </div>
            </div>
          </section>
          <section className="chat-panel" aria-label="聊天面板">
            <header className="chat-heading">
              <div className="mini-avatar">
                <Icon name="star" size={21} />
              </div>
              <div>
                <h2>和{name}聊聊天</h2>
                <p>
                  <i />
                  {connection === "connected"
                    ? "每一句话，都有人回应"
                    : "等待桌面服务连接"}
                </p>
              </div>
              <button
                className="icon-button export-button"
                aria-label="导出当前聊天"
                title="导出当前聊天"
                disabled={!conversation.messages.length}
                onClick={exportChat}
              >
                <Icon name="download" size={17} />
              </button>
            </header>
            <div
              className="transcript"
              role="log"
              aria-label="聊天记录"
              aria-live="polite"
            >
              <div className="date-divider">
                <span>
                  {new Date(conversation.created).toLocaleDateString("zh-CN", {
                    month: "long",
                    day: "numeric",
                  })}
                </span>
              </div>
              {!conversation.messages.length && (
                <div className="welcome">
                  <div className="welcome-icon">
                    <Icon name="sparkles" size={27} />
                  </div>
                  <h3>嗨，欢迎回来。</h3>
                  <p>
                    今天过得怎么样？
                    <br />
                    开心的、小小的、说不清的，都可以聊聊。
                  </p>
                  <div className="suggestions">
                    {prompts.map((prompt) => (
                      <button
                        key={prompt.text}
                        onClick={() => void send(prompt.text)}
                        disabled={connection !== "connected"}
                      >
                        <span className="suggestion-icon">
                          <Icon name={prompt.icon} size={19} />
                        </span>
                        <span>
                          <strong>{prompt.text}</strong>
                          <small>{prompt.note}</small>
                        </span>
                        <Icon name="arrow" size={15} />
                      </button>
                    ))}
                  </div>
                  <div className="welcome-note">
                    <span />
                    从一句简单的问候开始
                    <span />
                  </div>
                </div>
              )}
              {conversation.messages.map((message) => (
                <article
                  className={`message message-${message.role}`}
                  key={message.id}
                >
                  <div className="message-meta">
                    <span>{message.role === "assistant" ? name : "我"}</span>
                    <time>{timeLabel(message.time)}</time>
                  </div>
                  <div
                    className={`message-bubble ${message.status === "error" ? "has-error" : ""}`}
                  >
                    {message.text ||
                      (message.status === "pending" ? (
                        <span className="typing-dots">
                          <i />
                          <i />
                          <i />
                        </span>
                      ) : message.status === "stopped" ? (
                        "这条回复已打断。"
                      ) : (
                        "这次没能收到回复，请重试。"
                      ))}
                    {message.status === "pending" && message.text && (
                      <span className="cursor" />
                    )}
                  </div>
                  {message.role === "assistant" && (
                    <div className="message-actions">
                      {message.status === "stopped" && <span>已打断</span>}
                      {message.status === "error" && <span>未完成</span>}
                      {message.text && message.status !== "pending" && (
                        <button
                          className="read-button"
                          aria-label="朗读这条回复"
                          onClick={() => void replay(message)}
                          title="朗读这条回复"
                        >
                          <Icon name="volume" size={14} />
                        </button>
                      )}
                    </div>
                  )}
                </article>
              ))}
              <div ref={transcriptEnd} />
            </div>
            <form
              className="composer"
              onSubmit={(event) => {
                event.preventDefault();
                void send();
              }}
            >
              <div className="input-box">
                <textarea
                  ref={input}
                  value={draft}
                  onChange={(event) => setDraft(event.target.value)}
                  onKeyDown={(event) => {
                    if (
                      event.key === "Enter" &&
                      !event.shiftKey &&
                      !event.nativeEvent.isComposing
                    ) {
                      event.preventDefault();
                      void send();
                    }
                  }}
                  aria-label="聊天内容"
                  placeholder={`和${name}说点什么…`}
                  maxLength={2000}
                  rows={2}
                />
                <div className="input-tools">
                  <button
                    type="button"
                    className={`icon-button ${phase === "listening" ? "mic-active" : ""}`}
                    aria-label={
                      phase === "listening" ? "停止语音输入" : "语音输入"
                    }
                    disabled={!RecognitionConstructor}
                    onClick={toggleMic}
                  >
                    <Icon name="mic" size={18} />
                  </button>
                  <span>Enter 发送 · Shift + Enter 换行</span>
                  <button
                    className="send-button"
                    type="submit"
                    aria-label="发送消息"
                    disabled={!draft.trim() || connection !== "connected"}
                  >
                    <Icon name="send" size={18} />
                  </button>
                </div>
              </div>
              <p className="privacy-note">聊天仅保存在当前浏览器，可随时清除</p>
            </form>
          </section>
        </div>
        <footer className="workspace-footer">
          <span>
            <span className="little-star">✧</span> 伴星 HOSHI
          </span>
          <span>把平凡的每一天，过得有一点星光。</span>
          <button onClick={() => setDrawer("settings")}>
            偏好设置 <Icon name="arrow" size={13} />
          </button>
        </footer>
      </main>
      {drawer && (
        <div
          className="drawer-backdrop"
          onMouseDown={(event) => {
            if (event.target === event.currentTarget) setDrawer(null);
          }}
        >
          <section
            ref={modal}
            className="drawer"
            role="dialog"
            aria-modal="true"
            aria-labelledby="drawer-title"
            onKeyDown={(event) => {
              if (event.key !== "Tab") return;
              const elements = modal.current?.querySelectorAll<HTMLElement>(
                "button:not(:disabled), input, select, a[href]",
              );
              if (!elements?.length) return;
              const first = elements[0]!,
                last = elements[elements.length - 1]!;
              if (event.shiftKey && document.activeElement === first) {
                event.preventDefault();
                last.focus();
              } else if (!event.shiftKey && document.activeElement === last) {
                event.preventDefault();
                first.focus();
              }
            }}
          >
            <header className="drawer-heading">
              <div>
                <p className="eyebrow">YOUR LITTLE SPACE</p>
                <h2 id="drawer-title">
                  {drawer === "settings" ? "让陪伴更合心意" : "一起说过的话"}
                </h2>
              </div>
              <button
                className="icon-button"
                aria-label="关闭面板"
                onClick={() => setDrawer(null)}
              >
                <Icon name="close" />
              </button>
            </header>
            {drawer === "history" ? (
              <>
                <p className="drawer-description">
                  保留最近 20 次聊天，每次最多 80 条消息。记录只在当前浏览器中。
                </p>
                <button className="new-chat-button" onClick={startNew}>
                  <Icon name="plus" size={17} />
                  开始新的聊天
                </button>
                <div className="history-list">
                  {conversations.map((item) => (
                    <div
                      className={`history-item ${item.id === currentId ? "current" : ""}`}
                      key={item.id}
                    >
                      <button
                        onClick={() => {
                          stop();
                          setCurrentId(item.id);
                          setDraft("");
                          setDrawer(null);
                        }}
                      >
                        <strong>{item.title}</strong>
                        <span>
                          {new Date(item.created).toLocaleDateString("zh-CN")} ·{" "}
                          {item.messages.length} 条消息
                        </span>
                      </button>
                      <button
                        className="icon-button"
                        aria-label={`删除聊天：${item.title}`}
                        onClick={() => removeConversation(item.id)}
                      >
                        <Icon name="trash" size={16} />
                      </button>
                    </div>
                  ))}
                </div>
              </>
            ) : (
              <>
                <section className="settings-group">
                  <h3>角色</h3>
                  <label className="field-label" htmlFor="character-name">
                    怎么称呼她
                  </label>
                  <input
                    className="text-input"
                    id="character-name"
                    value={preferences.name}
                    maxLength={20}
                    onChange={(event) =>
                      setPreferences((value) => ({
                        ...value,
                        name: event.target.value,
                      }))
                    }
                    onBlur={() => {
                      if (!preferences.name.trim())
                        setPreferences((value) => ({ ...value, name: "小星" }));
                    }}
                  />
                  <p className="field-help">
                    原创演示角色，之后可以替换为你的 Live2D 模型。
                  </p>
                </section>
                <section className="settings-group">
                  <h3>声音</h3>
                  <div className="setting-row">
                    <div>
                      <strong>自动朗读回复</strong>
                      <p>也可以随时点击消息旁的声音按钮</p>
                    </div>
                    <button
                      className={`switch ${preferences.autoSpeak ? "on" : ""}`}
                      role="switch"
                      aria-label="自动朗读回复"
                      aria-checked={preferences.autoSpeak}
                      onClick={() => setAutoSpeak(!preferences.autoSpeak)}
                    >
                      <i />
                    </button>
                  </div>
                  <label className="field-label" htmlFor="voice">
                    朗读音色
                  </label>
                  <select
                    id="voice"
                    value={
                      voices.some((item) => item.name === preferences.voice)
                        ? preferences.voice
                        : ""
                    }
                    onChange={(event) =>
                      setPreferences((value) => ({
                        ...value,
                        voice: event.target.value,
                      }))
                    }
                  >
                    <option value="">默认中文音色</option>
                    {voices.map((voice) => (
                      <option key={voice.name} value={voice.name}>
                        {voice.name} · {voice.language}
                      </option>
                    ))}
                  </select>
                  <p className="field-help">
                    {capabilities?.voices.length
                      ? "使用本机系统语音，口型跟随实际播放的声音。"
                      : "使用浏览器语音，音色因设备而异；此模式不驱动口型。"}
                  </p>
                  <label className="range-label" htmlFor="volume">
                    音量<span>{Math.round(preferences.volume * 100)}%</span>
                  </label>
                  <input
                    id="volume"
                    type="range"
                    min="0"
                    max="1"
                    step="0.05"
                    value={preferences.volume}
                    onChange={(event) =>
                      setPreferences((value) => ({
                        ...value,
                        volume: Number(event.target.value),
                      }))
                    }
                  />
                  <div className="field-label">语速</div>
                  <div className="segmented">
                    {[
                      { value: 0.9, label: "舒缓" },
                      { value: 1, label: "自然" },
                      { value: 1.15, label: "轻快" },
                    ].map((speed) => (
                      <button
                        key={speed.value}
                        aria-pressed={preferences.speed === speed.value}
                        onClick={() =>
                          setPreferences((value) => ({
                            ...value,
                            speed: speed.value,
                          }))
                        }
                      >
                        {speed.label}
                      </button>
                    ))}
                  </div>
                  <p className="field-help">
                    音量、音色和语速在下次朗读时生效。
                  </p>
                </section>
                <section className="settings-group">
                  <h3>对话服务</h3>
                  <div className="service-card">
                    <span className="service-mark">
                      <Icon name="sparkles" size={20} />
                    </span>
                    <div>
                      <strong>{modeLabel}</strong>
                      <p>
                        {capabilities?.mode === "demo"
                          ? "预设的几种回应，无需云端密钥。"
                          : capabilities?.mode === "cloud" &&
                              capabilities.cloud_configured
                            ? capabilities.model
                            : "检查服务端配置后重新连接。"}
                      </p>
                    </div>
                  </div>
                  <button
                    className="text-button"
                    onClick={() => setCloudHelp((value) => !value)}
                  >
                    {cloudHelp ? "收起接入说明" : "如何接入真正的 AI 对话"}
                    <Icon name="arrow" size={14} />
                  </button>
                  {cloudHelp && (
                    <div className="cloud-help">
                      在项目的 <code>.env</code> 中填写兼容 Chat Completions
                      的服务配置，重启桌面服务即可。
                      <pre>
                        AIC_CHAT_MODE=cloud{"\n"}AIC_CHAT_URL=完整接口地址{"\n"}
                        AIC_CHAT_MODEL=模型名称{"\n"}AIC_CHAT_API_KEY=你的密钥
                      </pre>
                      密钥仅由本机后台使用。云端模式会将最近几条聊天发送给你配置的服务商，并可能产生费用。
                    </div>
                  )}
                </section>
                <section className="settings-group">
                  <h3>本机记录</h3>
                  <p className="field-help">
                    聊天保存在当前浏览器；不跨设备同步。可在聊天右上角导出。
                  </p>
                  {clearConfirm ? (
                    <div className="clear-confirm">
                      <p>清除这个浏览器中所有聊天记录？此操作无法撤销。</p>
                      <div>
                        <button
                          className="danger-button"
                          onClick={clearHistory}
                        >
                          确认清除全部
                        </button>
                        <button
                          className="text-button"
                          onClick={() => setClearConfirm(false)}
                        >
                          保留记录
                        </button>
                      </div>
                    </div>
                  ) : (
                    <button
                      className="text-button danger-text"
                      onClick={() => setClearConfirm(true)}
                    >
                      <Icon name="trash" size={15} />
                      清除全部聊天记录
                    </button>
                  )}
                </section>
                <p className="version-note">
                  伴星 · 桌面原型 0.1
                  <br />
                  先从一段小小的对话开始。
                </p>
              </>
            )}
          </section>
        </div>
      )}
      {notice && (
        <div className="toast" role="alert">
          <span>{notice}</span>
          <button aria-label="关闭提示" onClick={() => setNotice("")}>
            <Icon name="close" size={16} />
          </button>
        </div>
      )}
    </div>
  );
}
