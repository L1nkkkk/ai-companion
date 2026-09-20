import { useRef, useState } from "react";
import type { Emotion } from "../model";

export function Avatar({
  mouth,
  emotion,
  wave,
  name,
  onTouch,
}: {
  mouth: number;
  emotion: Emotion;
  wave: boolean;
  name: string;
  onTouch: () => void;
}) {
  const host = useRef<HTMLButtonElement>(null);
  const [look, setLook] = useState({ x: 0, y: 0 });
  return (
    <button
      ref={host}
      className={`avatar-touch ${wave ? "is-waving" : ""}`}
      aria-label={`和${name}打个招呼`}
      onClick={onTouch}
      onPointerMove={(event) => {
        const rect = host.current!.getBoundingClientRect();
        setLook({
          x: ((event.clientX - rect.left) / rect.width - 0.5) * 7,
          y: ((event.clientY - rect.top) / rect.height - 0.4) * 5,
        });
      }}
      onPointerLeave={() => setLook({ x: 0, y: 0 })}
    >
      <svg
        className="avatar-art"
        viewBox="0 0 380 600"
        role="img"
        aria-label={`${name}，银紫色头发的原创演示角色`}
      >
        <defs>
          <linearGradient id="hair" x2="0.8" y2="1">
            <stop stopColor="#d5d8ed" />
            <stop offset=".5" stopColor="#bec7e0" />
            <stop offset="1" stopColor="#a4accf" />
          </linearGradient>
          <linearGradient id="coat" x2=".6" y2="1">
            <stop stopColor="#fffdf6" />
            <stop offset="1" stopColor="#e2e9e9" />
          </linearGradient>
          <linearGradient id="iris" x2="0" y2="1">
            <stop stopColor="#47546d" />
            <stop offset="1" stopColor="#98b3b4" />
          </linearGradient>
          <radialGradient id="cheek">
            <stop stopColor="#eaaea6" stopOpacity=".55" />
            <stop offset="1" stopColor="#eaaea6" stopOpacity="0" />
          </radialGradient>
        </defs>
        <g
          className="avatar-breathe"
          strokeLinejoin="round"
          strokeLinecap="round"
        >
          <path
            d="M112 119C58 171 88 286 66 394 78 454 107 491 137 510L151 390 253 447C309 382 304 278 285 205 294 139 250 87 194 85Z"
            fill="url(#hair)"
            stroke="#99a6bf"
            strokeWidth="2"
          />
          <path
            d="M118 239C102 300 132 411 99 438M269 244c22 94-4 148-20 177"
            fill="none"
            stroke="#e3e7f4"
            strokeWidth="5"
            opacity=".75"
          />
          <path
            d="M136 484 130 595h51l8-111M215 484l-1 111h53l-19-111"
            fill="#333e51"
          />
          <path
            d="M134 482v66l44 4 10-67m24 0 3 66 43-5-16-64"
            fill="#aeb9c5"
          />
          <path
            d="M137 347c-7 63-21 108-24 151 28 26 116 29 163-1l-25-153Z"
            fill="#52677a"
            stroke="#40586c"
            strokeWidth="2"
          />
          <path
            d="m147 421-7 83m35-80-4 91m37-91 3 89m28-90 11 84"
            stroke="#7d93a4"
            strokeWidth="3"
          />
          <path
            d="M135 303c-23 2-48 27-52 61l-17 79c-3 15 6 26 19 26 16 0 20-18 24-30l37-85Z"
            fill="url(#coat)"
            stroke="#b5c0cb"
            strokeWidth="2"
          />
          <path
            d="m71 442-3 34c0 14 8 24 16 21 6-2 6-15 6-20 6 3 11-4 9-12l-3-20Z"
            fill="#f4dccc"
            stroke="#d6bfae"
            strokeWidth="1.5"
          />
          <g className="avatar-arm">
            <path
              d="M245 304c28 4 46 26 48 61l16 78c2 15-5 27-18 27-17 1-23-18-26-31l-31-84Z"
              fill="url(#coat)"
              stroke="#b5c0cb"
              strokeWidth="2"
            />
            <path
              d="m280 444 4 33c2 13 11 22 19 17 5-3 2-16 2-20 7 1 10-6 7-13l-6-19Z"
              fill="#f4dccc"
              stroke="#d6bfae"
              strokeWidth="1.5"
            />
            <path d="m278 431 28-4 3 22-27 5Z" fill="#a5bbc2" />
          </g>
          <path
            d="m133 302 39-15h42l37 17 5 134c-34 14-82 15-125-1Z"
            fill="#a7c8c5"
            stroke="#7eaaac"
            strokeWidth="2"
          />
          <path
            d="M172 273v32c5 24 34 25 43 0v-32"
            fill="#f2d9c7"
            stroke="#dbc2b1"
            strokeWidth="1.5"
          />
          <path
            d="m132 301 39-13-18 37 20 98-6 44-46-15Z"
            fill="url(#coat)"
            stroke="#b5c0cb"
            strokeWidth="2"
          />
          <path
            d="m216 289 39 14 13 150-44 15-8-47 21-97Z"
            fill="url(#coat)"
            stroke="#b5c0cb"
            strokeWidth="2"
          />
          <path
            d="m153 325 19-37 14 29-12 26Z M213 288l24 37-23 19-16-27Z"
            fill="#fdfcf5"
            stroke="#b9c8ce"
            strokeWidth="1.5"
          />
          <path
            d="m190 322-26 9 11 21 18-18 18 19 11-23-25-8Z"
            fill="#678c8e"
          />
          <circle cx="194" cy="328" r="7" fill="#e8d38f" />
          <path d="m188 342-5 37 13-10 8 9-4-36" fill="#668c8d" />
          <path d="m135 395 20 3m79-1 22-3" stroke="#b5c0cb" strokeWidth="2" />
          <circle cx="199" cy="401" r="3" fill="#eef4ee" />
          <circle cx="199" cy="423" r="3" fill="#eef4ee" />
          <g
            className="avatar-head"
            style={{ transform: `rotate(${look.x * 0.4}deg)` }}
          >
            <ellipse
              cx="191"
              cy="202"
              rx="75"
              ry="91"
              fill="#fae7d7"
              stroke="#d4bfba"
              strokeWidth="2"
            />
            <path
              d="M116 220c-14-14-19 17-8 27l14 4m145-29c16-12 18 18 6 26l-10 3"
              fill="#f7dfd0"
              stroke="#d4bfba"
              strokeWidth="1.5"
            />
            <ellipse cx="144" cy="239" rx="21" ry="12" fill="url(#cheek)" />
            <ellipse cx="241" cy="239" rx="21" ry="12" fill="url(#cheek)" />
            <path
              d="m133 189 25-4m64 0 25 5"
              fill="none"
              stroke="#a299aa"
              strokeWidth="3"
            />
            <g className="avatar-eyes">
              <path
                d="M126 209q18-18 38-2c-2 30-34 31-38 2Zm94-2q20-16 38 2c-4 29-36 28-38-2Z"
                fill="#fffdf9"
              />
              <g transform={`translate(${look.x},${look.y})`}>
                <ellipse cx="146" cy="218" rx="12" ry="18" fill="url(#iris)" />
                <ellipse cx="239" cy="218" rx="12" ry="18" fill="url(#iris)" />
                <ellipse cx="147" cy="215" rx="5" ry="10" fill="#394555" />
                <ellipse cx="239" cy="215" rx="5" ry="10" fill="#394555" />
                <circle cx="142" cy="209" r="5" fill="#fff" />
                <circle cx="235" cy="209" r="5" fill="#fff" />
                <circle cx="150" cy="228" r="2.5" fill="#e1efe6" />
                <circle cx="243" cy="228" r="2.5" fill="#e1efe6" />
              </g>
              <path
                d="M124 207q18-16 41-1m54 0q23-15 41 1M125 208l-5-5m139 5 5-5"
                fill="none"
                stroke="#666074"
                strokeWidth="4"
              />
            </g>
            <path
              d="m191 233-2 6 4 1"
              fill="none"
              stroke="#d7b9aa"
              strokeWidth="1.5"
            />
            {mouth > 0.04 ? (
              <ellipse
                cx="193"
                cy="260"
                rx={5 + mouth * 4}
                ry={2 + mouth * 10}
                fill="#925a66"
                stroke="#b97c7c"
                strokeWidth="1.2"
              />
            ) : (
              <path
                d={
                  emotion === "happy"
                    ? "M183 256q10 13 21 0"
                    : "M186 258q7 5 14-1"
                }
                fill="none"
                stroke="#bd8684"
                strokeWidth="2"
              />
            )}
            <path
              d="M106 211C73 160 112 90 159 85c67-26 116 14 127 66 5 22-3 62-16 74l-8-56-17-24c-10 22-21 32-26 36l-7-40c-12 27-35 44-54 47l6-43c-11 24-28 47-41 54l-1-43Z"
              fill="url(#hair)"
              stroke="#9eaac4"
              strokeWidth="2"
            />
            <path
              d="M117 140q29-49 70-45m31 10q30 10 42 40M133 146l-9 34m65-65-20 50m55-44 10 33"
              stroke="#ecedf8"
              strokeWidth="4"
              opacity=".65"
              fill="none"
            />
            <path
              d="M112 187c-14 38 0 99 17 120l8-16c-11-30-13-59-8-83m129-19c20 55 6 97-6 118l-11-15c13-28 8-63 4-84"
              fill="url(#hair)"
              stroke="#9eaac4"
              strokeWidth="2"
            />
            <path
              d="m253 151 4 9 10 1-8 6 2 10-8-6-9 5 3-10-7-7 10 1Z"
              fill="#f2d98e"
              stroke="#baa66a"
              strokeWidth="1.4"
            />
            <path d="m249 184 15-5" stroke="#f3e5b9" strokeWidth="4" />
            <circle
              cx="116"
              cy="232"
              r="15"
              fill="#e2e6ef"
              stroke="#9eaac4"
              strokeWidth="3"
            />
            <circle cx="116" cy="232" r="8" fill="#9cafba" />
            <circle
              cx="270"
              cy="233"
              r="14"
              fill="#e2e6ef"
              stroke="#9eaac4"
              strokeWidth="3"
            />
            <circle cx="270" cy="233" r="7" fill="#9cafba" />
          </g>
        </g>
      </svg>
    </button>
  );
}
