import { defineConfig } from "@playwright/test";

export default defineConfig({
  testDir: "./tests/desktop",
  timeout: 30000,
  expect: { timeout: 8000 },
  fullyParallel: false,
  workers: 1,
  reporter: "list",
  outputDir: "test-results",
  use: {
    baseURL: "http://127.0.0.1:5173",
    viewport: { width: 1440, height: 960 },
    headless: true,
    trace: "retain-on-failure",
  },
  webServer: {
    command: "node tools/dev-desktop.mjs",
    url: "http://127.0.0.1:5173/health/live",
    reuseExistingServer: !process.env.CI,
    timeout: 45000,
    env: { AIC_CHAT_MODE: "demo" },
  },
});
