import { spawn } from "node:child_process";
import { existsSync } from "node:fs";
import { createServer } from "node:net";
import { createRequire } from "node:module";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const root = dirname(dirname(fileURLToPath(import.meta.url)));
const windows = process.platform === "win32";
const python = join(
  root,
  ".venv",
  windows ? "Scripts/python.exe" : "bin/python",
);
let vite;
try {
  const require = createRequire(join(root, "apps/web/package.json"));
  vite = join(dirname(require.resolve("vite/package.json")), "bin/vite.js");
} catch {
  /* Report missing dependencies below. */
}
if (!existsSync(python) || !vite || !existsSync(vite)) {
  console.error(
    "请先在仓库根目录运行 pnpm install --frozen-lockfile --ignore-scripts 和 uv sync --locked。",
  );
  process.exit(1);
}
if (existsSync(join(root, ".env"))) process.loadEnvFile(join(root, ".env"));
for (const port of [8000, 5173]) {
  await new Promise((resolve, reject) => {
    const server = createServer();
    server.once("error", () =>
      reject(new Error(`本机端口 ${port} 已被占用，请先关闭占用它的服务。`)),
    );
    server.listen(port, "127.0.0.1", () => server.close(resolve));
  });
}
const children = [];
let stopping = false;
function stop(code = 0) {
  if (stopping) return;
  stopping = true;
  for (const child of children) {
    if (!child.pid || child.exitCode !== null) continue;
    if (windows)
      spawn("taskkill.exe", ["/pid", String(child.pid), "/t", "/f"], {
        windowsHide: true,
        stdio: "ignore",
      });
    else child.kill("SIGTERM");
  }
  setTimeout(() => process.exit(code), 750);
}
function start(command, args, cwd) {
  const child = spawn(command, args, {
    cwd,
    env: process.env,
    stdio: "inherit",
    windowsHide: true,
  });
  children.push(child);
  child.on("error", (error) => {
    console.error(error.message);
    stop(1);
  });
  child.on("exit", (code) => {
    if (!stopping) stop(code ?? 1);
  });
}
process.on("SIGINT", () => stop());
process.on("SIGTERM", () => stop());
start(
  python,
  [
    "-m",
    "uvicorn",
    "app.main:app",
    "--app-dir",
    "services/api",
    "--host",
    "127.0.0.1",
    "--port",
    "8000",
  ],
  root,
);
start(
  process.execPath,
  [vite, "--host", "127.0.0.1", "--port", "5173", "--strictPort"],
  join(root, "apps/web"),
);
console.log(
  "\n伴星桌面原型：http://127.0.0.1:5173\n关闭此终端或按 Ctrl+C 结束服务。\n",
);
