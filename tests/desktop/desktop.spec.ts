import { test, expect, type Page } from "@playwright/test";
import { mkdirSync } from "node:fs";

const storageKey = "hoshi.desktop.v1";
async function open(page: Page, speak = false) {
  await page.addInitScript(
    ({ key, speak }) => {
      if (!localStorage.getItem(key))
        localStorage.setItem(
          key,
          JSON.stringify({
            conversations: [],
            preferences: { autoSpeak: speak },
          }),
        );
    },
    { key: storageKey, speak },
  );
  await page.goto("/");
  await expect(
    page.getByRole("button", { name: "演示模式 · 预设回应" }),
  ).toBeVisible();
}

test("desktop welcome, original avatar and scene preference", async ({
  page,
}) => {
  const errors: string[] = [];
  page.on("pageerror", (error) => errors.push(error.message));
  await open(page);
  await expect(
    page.getByRole("heading", { name: "今天，也想陪在你身边。" }),
  ).toBeVisible();
  await page
    .getByRole("button", { name: "和小星打个招呼", exact: true })
    .click();
  await expect(page.getByText("嘿，我在呢 ♡")).toBeVisible();
  await page.getByRole("button", { name: "星夜", exact: true }).click();
  await expect
    .poll(() =>
      page.evaluate(
        (key) => JSON.parse(localStorage.getItem(key)!).preferences.scene,
        storageKey,
      ),
    )
    .toBe("night");
  await page.reload();
  await expect(
    page.getByRole("button", { name: "星夜", exact: true }),
  ).toHaveAttribute("aria-pressed", "true");
  await page.getByRole("button", { name: "晴日", exact: true }).click();
  mkdirSync(".tmp/desktop", { recursive: true });
  await page.screenshot({
    path: ".tmp/desktop/welcome.png",
    fullPage: true,
    animations: "disabled",
  });
  expect(errors).toEqual([]);
});

test("real API chat, local history, export and clearing", async ({ page }) => {
  await open(page);
  await page.getByRole("button", { name: /今天有点累/ }).click();
  await expect(page.locator(".message-assistant")).toContainText("辛苦啦");
  await expect(
    page.getByRole("button", { name: "朗读这条回复" }),
  ).toBeVisible();
  await expect(page.getByRole("button", { name: "打断回复" })).toBeDisabled();
  const download = page.waitForEvent("download");
  await page.getByRole("button", { name: "导出当前聊天" }).click();
  expect((await download).suggestedFilename()).toContain("伴星聊天");
  await expect
    .poll(() =>
      page.evaluate(
        (key) =>
          JSON.parse(localStorage.getItem(key)!).conversations[0].messages
            .length,
        storageKey,
      ),
    )
    .toBe(2);
  await page.reload();
  await expect(page.locator(".message-assistant")).toContainText("辛苦啦");
  await page.getByRole("button", { name: "新的聊天", exact: true }).click();
  await expect(
    page.getByRole("heading", { name: "嗨，欢迎回来。" }),
  ).toBeVisible();
  await page.getByRole("button", { name: "聊天记录", exact: true }).click();
  await page.getByRole("button", { name: /今天有点累.*2 条消息/ }).click();
  await expect(page.locator(".message-assistant")).toContainText("辛苦啦");
  await page
    .getByRole("button", { name: "偏好设置", exact: true })
    .first()
    .click();
  await page
    .getByRole("button", { name: "清除全部聊天记录", exact: true })
    .click();
  await page.getByRole("button", { name: "确认清除全部" }).click();
  await page.getByRole("button", { name: "关闭面板" }).click();
  await expect(page.locator(".message")).toHaveCount(0);
  await expect
    .poll(() =>
      page.evaluate(
        (key) =>
          JSON.parse(localStorage.getItem(key)!).conversations[0].messages
            .length,
        storageKey,
      ),
    )
    .toBe(0);
});

test("stop and new conversation fence late streamed text", async ({ page }) => {
  await open(page);
  await page.getByRole("button", { name: /讲个小故事/ }).click();
  await expect(
    page.locator(".message-assistant .message-bubble"),
  ).toContainText("小星星");
  await page.getByRole("button", { name: "打断回复" }).click();
  await expect(page.getByText("已打断", { exact: true })).toBeVisible();
  const stopped = await page
    .locator(".message-assistant .message-bubble")
    .textContent();
  await page.getByRole("button", { name: "新的聊天", exact: true }).click();
  await page.getByRole("button", { name: /一起做个计划/ }).click();
  await expect(page.locator(".message-assistant")).toContainText("十分钟");
  await expect(
    page.getByRole("button", { name: "朗读这条回复" }),
  ).toBeVisible();
  expect(await page.locator(".message-assistant").textContent()).not.toContain(
    "月亮",
  );
  await page.getByRole("button", { name: "聊天记录", exact: true }).click();
  await page.getByRole("button", { name: /讲个小故事.*2 条消息/ }).click();
  await expect(page.locator(".message-assistant .message-bubble")).toHaveText(
    stopped!,
  );
});

test("native Windows audio drives mouth and stops locally", async ({
  page,
  request,
}) => {
  const capabilities = await (
    await request.get("/prototype/capabilities")
  ).json();
  test.skip(
    !capabilities.voices.length,
    "Native Windows voice requires a Windows host with installed voices.",
  );
  await open(page, true);
  await page.getByRole("textbox", { name: "聊天内容" }).fill("你好");
  await page.getByRole("button", { name: "发送消息", exact: true }).click();
  await expect(
    page.getByText("想说的话，说给你听", { exact: true }),
  ).toBeVisible({ timeout: 20000 });
  await expect
    .poll(async () =>
      Number(
        await page.locator(".avatar-position").getAttribute("data-mouth-level"),
      ),
    )
    .toBeGreaterThan(0.04);
  await page.getByRole("button", { name: "打断回复" }).click();
  await expect(page.locator(".avatar-position")).toHaveAttribute(
    "data-mouth-level",
    "0.000",
  );
  await expect(page.getByRole("button", { name: "打断回复" })).toBeDisabled();
  await page.getByRole("button", { name: "新的聊天", exact: true }).click();
  await expect(
    page.getByRole("heading", { name: "嗨，欢迎回来。" }),
  ).toBeVisible();
});

test("unresponsive browser voice exits preparation with feedback", async ({
  page,
}) => {
  await page.clock.install();
  await page.route("**/prototype/capabilities", (route) =>
    route.fulfill({
      json: {
        mode: "demo",
        cloud_configured: false,
        model: "",
        voices: [],
        avatar: "svg-demo",
      },
    }),
  );
  await page.addInitScript(() => {
    Object.defineProperty(window, "speechSynthesis", {
      configurable: true,
      value: {
        getVoices: () => [],
        speak: () => {},
        cancel: () => {},
        addEventListener: () => {},
        removeEventListener: () => {},
      },
    });
  });
  await open(page, true);
  await page.getByRole("button", { name: /今天有点累/ }).click();
  await expect(page.getByText("准备开口…", { exact: true })).toBeVisible();
  await page.clock.fastForward(10001);
  await expect(page.getByRole("alert")).toContainText("浏览器语音没有响应");
  await expect(page.getByRole("button", { name: "打断回复" })).toBeDisabled();
  await expect(page.locator(".avatar-position")).toHaveAttribute(
    "data-mouth-level",
    "0.000",
  );
});

test("offline service and microphone denial have honest feedback", async ({
  page,
}) => {
  await page.route("**/prototype/capabilities", (route) => route.abort());
  await page.addInitScript(() => {
    class DeniedRecognition {
      onerror: ((event: { error: string }) => void) | null = null;
      start() {
        setTimeout(() => this.onerror?.({ error: "not-allowed" }), 10);
      }
      abort() {}
    }
    Object.defineProperty(window, "SpeechRecognition", {
      value: DeniedRecognition,
      configurable: true,
    });
  });
  await page.goto("/");
  await expect(page.getByRole("button", { name: "服务未连接" })).toBeVisible();
  await page.getByRole("textbox", { name: "聊天内容" }).fill("测试");
  await expect(
    page.getByRole("button", { name: "发送消息", exact: true }),
  ).toBeDisabled();
  await page.getByRole("button", { name: "跟我说说话", exact: true }).click();
  await expect(page.getByRole("alert")).toContainText("麦克风权限未开启");
  await expect(page.getByRole("button", { name: "打断回复" })).toBeDisabled();
  await expect(page.locator(".message")).toHaveCount(0);
});

test("settings, focus mode and narrow layout remain usable", async ({
  page,
}) => {
  await open(page);
  await page
    .getByRole("button", { name: "偏好设置", exact: true })
    .first()
    .click();
  await page.getByRole("textbox", { name: "怎么称呼她" }).fill("小月");
  await page.getByRole("button", { name: "轻快", exact: true }).click();
  await page.getByRole("button", { name: "关闭面板" }).click();
  await expect(
    page.getByRole("heading", { name: "和小月聊聊天" }),
  ).toBeVisible();
  await page.getByRole("button", { name: "沉浸模式", exact: true }).click();
  await expect(page.getByRole("region", { name: "聊天面板" })).toBeHidden();
  await page.keyboard.press("Escape");
  await expect(page.getByRole("region", { name: "聊天面板" })).toBeVisible();
  await page.setViewportSize({ width: 390, height: 844 });
  await expect(page.getByRole("textbox", { name: "聊天内容" })).toBeVisible();
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= window.innerWidth,
    ),
  ).toBe(true);
  await page.screenshot({
    path: ".tmp/desktop/narrow.png",
    fullPage: true,
    animations: "disabled",
  });
});
