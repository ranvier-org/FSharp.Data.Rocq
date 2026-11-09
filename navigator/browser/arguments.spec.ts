import type { Page } from "@playwright/test";
import { ARGS_PAGE, clientReady, expect, test } from "./support";

const KEY = "rocq-navigator-args";

const surface = (page: Page) => page.locator("dialog.settings");
const choice = (page: Page, label: string) => surface(page).getByRole("group", { name: "Arguments", exact: true }).getByRole("button", { name: label, exact: true });
const saved = (page: Page, value: string) => page.addInitScript(([key, chosen]) => localStorage.setItem(key, chosen), [KEY, value]);
const dataArgs = (page: Page) => page.evaluate(() => document.documentElement.getAttribute("data-args"));
const list = (page: Page, id: string) => page.locator(`[id="${id}"] > .args`);
const firstType = (page: Page, id: string) => list(page, id).locator(".args-type").first();
const panelBox = async (page: Page, id: string) => (await page.locator(`[id="${id}"] > .panel`).boundingBox())!;

const openSettings = async (page: Page) => {
  await page.locator(".pane-left [data-settings-open]").click();
  await expect(surface(page)).toBeVisible();
};

test.beforeEach(async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
});

const expectNames = async (page: Page) => {
  expect(await dataArgs(page)).toBeNull();
  await expect(firstType(page, "self_dirtying")).toBeHidden();
  const args = (await list(page, "self_dirtying").boundingBox())!;
  expect(args.y + args.height).toBeLessThanOrEqual((await panelBox(page, "self_dirtying")).y);
};

const expectTypes = async (page: Page) => {
  expect(await dataArgs(page)).toBe("types");
  await expect(firstType(page, "self_dirtying")).toBeVisible();
  const args = (await list(page, "self_dirtying").boundingBox())!;
  expect(args.y + args.height).toBeLessThanOrEqual((await panelBox(page, "self_dirtying")).y);
};

const expectPanel = async (page: Page) => {
  expect(await dataArgs(page)).toBe("panel");
  await expect(firstType(page, "self_dirtying")).toBeVisible();
  const args = (await list(page, "self_dirtying").boundingBox())!;
  const panel = await panelBox(page, "self_dirtying");
  expect(args.y).toBeCloseTo(panel.y + panel.height, 0);
  expect(args.x).toBeCloseTo(panel.x, 0);
  expect(args.width).toBeCloseTo(panel.width, 0);
};

test("names only is the default, above the code, with types out of the tab order", async ({ page }) => {
  await page.goto(ARGS_PAGE);
  await clientReady(page);
  await expectNames(page);
  await expect(list(page, "self_dirtying").locator(".args-row")).toHaveCount(2);
  await expect(list(page, "self_dirtying").locator(".args-name").first()).toHaveText("s");
  await expect(list(page, "self_dirtying").locator(".args-text").last()).toContainText("across two lines.");

  const link = list(page, "self_dirtying").locator(".args-type a").first();
  await expect(link).toHaveAttribute("href", /#state$/);
  expect(await link.evaluate((element) => (element as HTMLElement).checkVisibility())).toBe(false);

  await page.evaluate(() => {
    const before = document.querySelector<HTMLElement>('[id="self_dirtying"] > .doc')!;
    before.tabIndex = -1;
    before.focus();
  });
  await page.keyboard.press("Tab");
  expect(await page.evaluate(() => document.activeElement?.closest(".args") === null)).toBe(true);
});

test("each value applies live and survives a reload", async ({ page }) => {
  await page.goto(ARGS_PAGE);
  await clientReady(page);
  await openSettings(page);
  await expect(choice(page, "Names only")).toHaveAttribute("aria-pressed", "true");

  for (const [label, value, check] of [
    ["With types", "types", expectTypes],
    ["In the panel", "panel", expectPanel],
    ["Names only", "names", expectNames],
  ] as const) {
    await choice(page, label).click();
    await check(page);
    expect(await page.evaluate((key) => localStorage.getItem(key), KEY)).toBe(value);

    await page.reload();
    await clientReady(page);
    await check(page);
    await openSettings(page);
    await expect(choice(page, label)).toHaveAttribute("aria-pressed", "true");
  }
});

test("a type link shows and is focusable when the types show", async ({ page }) => {
  await saved(page, "types");
  await page.goto(ARGS_PAGE);
  await clientReady(page);
  const link = list(page, "self_dirtying").locator(".args-type a").first();
  await link.focus();
  await expect(link).toBeFocused();
  await expect(page.locator(".card")).toBeVisible();
});

test("the head script applies a saved value with the client held back", async ({ page }) => {
  await saved(page, "panel");
  await page.route("**/navigator.js", (route) => route.fulfill({ contentType: "text/javascript", body: "" }));
  await page.goto(ARGS_PAGE);
  await page.waitForLoadState("load");
  await expectPanel(page);
});

for (const value of ["types", "panel"]) {
  test(`a reload with ${value} saved shifts nothing`, async ({ page }) => {
    await page.addInitScript(() => {
      const shifts: number[] = [];
      (window as any).__shifts = shifts;
      new PerformanceObserver((list) => {
        for (const entry of list.getEntries()) shifts.push((entry as any).value);
      }).observe({ type: "layout-shift", buffered: true });
    });
    await saved(page, value);
    await page.goto(ARGS_PAGE);
    await clientReady(page);
    await page.reload();
    await clientReady(page);
    await page.waitForTimeout(300);
    expect(await page.evaluate(() => (window as any).__shifts)).toEqual([]);
  });
}

test("a bad stored value falls back to names only", async ({ page }) => {
  await saved(page, "<sideways>");
  await page.goto(ARGS_PAGE);
  await clientReady(page);
  await expectNames(page);
  await openSettings(page);
  await expect(choice(page, "Names only")).toHaveAttribute("aria-pressed", "true");
});

test("Reset clears the key and returns to names only", async ({ page }) => {
  await saved(page, "panel");
  await page.goto(ARGS_PAGE);
  await clientReady(page);
  await expectPanel(page);
  await openSettings(page);
  await page.evaluate((key) => localStorage.setItem(key, "panel"), KEY);
  await surface(page).getByRole("button", { name: "Reset to the site's defaults" }).click();
  expect(await page.evaluate((key) => localStorage.getItem(key), KEY)).toBeNull();
  expect(await dataArgs(page)).toBeNull();
  await expect(firstType(page, "self_dirtying")).toBeHidden();
});

test("hovering a row marks that binder's occurrences in the statement and clears", async ({ page }) => {
  await page.goto(ARGS_PAGE);
  await clientReady(page);

  const row = list(page, "self_dirtying").locator(".args-row").nth(1);
  const key = await row.getAttribute("data-binders");
  const hot = page.locator(".arg-hot");
  await expect(hot).toHaveCount(0);

  await row.locator(".args-text").hover();
  await expect(hot).not.toHaveCount(0);
  const marked = await hot.evaluateAll((spans) => spans.map((span) => [span.getAttribute("data-ref"), span.textContent, !!span.closest("pre.statement")]));
  for (const [ref, text, inStatement] of marked) {
    expect(ref).toBe(key);
    expect(text).toBe("n");
    expect(inStatement).toBe(true);
  }

  await row.locator(".args-name").hover();
  await expect(hot).not.toHaveCount(0);

  await page.locator(".topbar").hover();
  await expect(hot).toHaveCount(0);
});

test("a binder's card shows the entry and the type", async ({ page }) => {
  await page.goto(ARGS_PAGE);
  await clientReady(page);

  const key = await list(page, "self_dirtying").locator(".args-row").first().getAttribute("data-binders");
  await page.locator(`[id="self_dirtying"] pre.statement [data-ref="${key}"]`).first().hover();

  const card = page.locator(".card");
  await expect(card).toBeVisible();
  await expect(card.locator(".card-doc")).toHaveText("the state read.");
  await expect(card.locator(".card-signature")).toHaveText("state");
});

test("the list stays outside the statement's bytes", async ({ page }) => {
  await page.goto(ARGS_PAGE);
  await clientReady(page);
  expect(await page.locator("pre .args, .args pre").count()).toBe(0);
  expect(await page.locator('[id="self_dirtying"] pre.statement').textContent()).toBe("Definition self_dirtying (s : state) (n : nat) : Prop := s = n.");
});

test("the list stacks and scrolls nothing at 375 px, in every layout", async ({ page }) => {
  for (const value of ["names", "types", "panel"]) {
    await page.setViewportSize({ width: 375, height: 700 });
    await page.addInitScript(([key, chosen]) => localStorage.setItem(key, chosen), [KEY, value]);
    await page.goto(ARGS_PAGE);
    await clientReady(page);
    expect(await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth), value).toBe(0);

    const rows = list(page, "self_dirtying").locator(".args-row");
    const first = (await rows.first().boundingBox())!;
    const second = (await rows.nth(1).boundingBox())!;
    expect(second.y, value).toBeGreaterThanOrEqual(first.y + first.height);

    if (value !== "names") {
      const signature = (await rows.first().locator(".args-sig").boundingBox())!;
      const text = (await rows.first().locator(".args-text").boundingBox())!;
      expect(text.y, value).toBeGreaterThanOrEqual(signature.y + signature.height - 1);
    }
  }
});
