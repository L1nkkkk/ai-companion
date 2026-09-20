import type { CSSProperties } from "react";

export type IconName =
  | "star"
  | "chat"
  | "plus"
  | "history"
  | "settings"
  | "mic"
  | "volume"
  | "muted"
  | "stop"
  | "send"
  | "close"
  | "sun"
  | "moon"
  | "leaf"
  | "arrow"
  | "download"
  | "trash"
  | "expand"
  | "check"
  | "headphones"
  | "sparkles";
const paths: Record<IconName, string[]> = {
  star: ["M12 2.5 14.6 9.4 21.5 12 14.6 14.6 12 21.5 9.4 14.6 2.5 12 9.4 9.4Z"],
  chat: [
    "M21 11.5a8.5 8.5 0 0 1-8.5 8.5H4l-2 2V11.5a9.5 9.5 0 1 1 19 0Z",
    "M7 10h10M7 14h6",
  ],
  plus: ["M12 5v14M5 12h14"],
  history: ["M3 11a9 9 0 1 1 2 6", "M3 4v7h7", "M12 7v5l3 2"],
  settings: [
    "M9 3h6l1 3 3 1 2 5-2 5-3 1-1 3H9l-1-3-3-1-2-5 2-5 3-1Z",
    "M15.5 12a3.5 3.5 0 1 1-7 0 3.5 3.5 0 0 1 7 0",
  ],
  mic: [
    "M9 5a3 3 0 0 1 6 0v7a3 3 0 0 1-6 0Z",
    "M5 10v2a7 7 0 0 0 14 0v-2M12 19v3M8 22h8",
  ],
  volume: ["M11 4 6 8H2v8h4l5 4Z", "M15 8a6 6 0 0 1 0 8M18 5a10 10 0 0 1 0 14"],
  muted: ["M11 4 6 8H2v8h4l5 4Z", "m16 9 6 6m0-6-6 6"],
  stop: ["M5 5h14v14H5Z"],
  send: ["M12 20V4m-7 7 7-7 7 7"],
  close: ["m6 6 12 12M18 6 6 18"],
  sun: [
    "M16 12a4 4 0 1 1-8 0 4 4 0 0 1 8 0",
    "M12 2v2m0 16v2M2 12h2m16 0h2M5 5l1.5 1.5m11 11L19 19M5 19l1.5-1.5m11-11L19 5",
  ],
  moon: ["M21 13A9 9 0 0 1 11 3a9 9 0 1 0 10 10Z"],
  leaf: ["M20 3C8 1 3 6 4 13c1 6 11 9 15 0 1-3 1-7 1-10Z", "M4 21 16 8"],
  arrow: ["M5 12h14m-6-6 6 6-6 6"],
  download: ["M12 3v12m-5-5 5 5 5-5M4 16v5h16v-5"],
  trash: ["M3 6h18M9 6V3h6v3M5 6l1 15h12l1-15M10 10v7m4-7v7"],
  expand: ["M9 3H3v6m12-6h6v6M3 15v6h6m12-6v6h-6"],
  check: ["m5 12 4 4L19 6"],
  headphones: [
    "M4 14v-3a8 8 0 0 1 16 0v3M4 12h3v8H4a2 2 0 0 1-2-2v-4a2 2 0 0 1 2-2Zm16 0h-3v8h3a2 2 0 0 0 2-2v-4a2 2 0 0 0-2-2Z",
  ],
  sparkles: [
    "m9 3 2 6 6 2-6 2-2 6-2-6-6-2 6-2Z",
    "m19 14 1 3 3 1-3 1-1 3-1-3-3-1 3-1Z",
  ],
};
export function Icon({
  name,
  size = 20,
  style,
}: {
  name: IconName;
  size?: number;
  style?: CSSProperties;
}) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.65"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      style={style}
    >
      {paths[name].map((path, index) => (
        <path d={path} key={index} />
      ))}
    </svg>
  );
}
