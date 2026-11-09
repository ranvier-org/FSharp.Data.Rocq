import type { Page } from "@playwright/test";
import { clientReady, expect, MODULE_PAGE, runningAnimations, test } from "./support";

const FOLD = "details.proof";

/** Clicks a fold's summary and records its height each frame, until the inline height is gone and ten frames agree. */
const openAndSample = (page: Page) =>
  page.evaluate(
    (selector) =>
      new Promise<{ closed: number; heights: number[]; inline: string[] }>((resolve) => {
        const fold = document.querySelector<HTMLDetailsElement>(selector)!;
        const closed = fold.getBoundingClientRect().height;
        const heights: number[] = [];
        const inline: string[] = [];

        fold.querySelector("summary")!.click();

        const sample = () => {
          heights.push(fold.getBoundingClientRect().height);
          inline.push(fold.style.height);
          const last = heights.slice(-10);

          if (last.length === 10 && new Set(last).size === 1 && fold.style.height === "") {
            resolve({ closed, heights, inline });
          } else {
            requestAnimationFrame(sample);
          }
        };

        requestAnimationFrame(sample);
      }),
    FOLD,
  );

test("a fold's height runs through Anime to completion and leaves nothing inline", async ({ page }) => {
  await page.goto(MODULE_PAGE);
  await clientReady(page);

  const { closed, heights, inline } = await openAndSample(page);
  const open = heights[heights.length - 1];
  const between = new Set(heights.filter((height) => height > closed && height < open));

  expect(open).toBeGreaterThan(closed);
  expect(between.size).toBeGreaterThan(2);
  expect(inline.some((height) => height.endsWith("px"))).toBe(true);
  expect([...heights].sort((a, b) => a - b)).toEqual(heights);

  const fold = page.locator(FOLD).first();
  await expect(fold).toHaveJSProperty("open", true);
  expect(await fold.getAttribute("style")).toBeFalsy();
  await expect(fold.locator("pre")).toBeVisible();
});

test("a fold closes at once, also while it is opening", async ({ page }) => {
  await page.goto(MODULE_PAGE);
  await clientReady(page);

  const closed = await page
    .locator(FOLD)
    .first()
    .evaluate((fold: HTMLDetailsElement) => {
      const height = fold.getBoundingClientRect().height;
      const summary = fold.querySelector("summary")!;
      summary.click();
      summary.click();
      return { height, after: fold.getBoundingClientRect().height, open: fold.open };
    });

  expect(closed.open).toBe(false);
  expect(closed.after).toBe(closed.height);

  await page.waitForTimeout(400);
  expect(await page.locator(FOLD).first().getAttribute("style")).toBeFalsy();
});

test("the keyboard opens a fold", async ({ page }) => {
  await page.goto(MODULE_PAGE);
  await clientReady(page);

  await page.locator(`${FOLD} summary`).first().focus();
  await page.keyboard.press("Enter");

  await expect(page.locator(FOLD).first()).toHaveJSProperty("open", true);
  await expect(page.locator(`${FOLD} pre`).first()).toBeVisible();
});

test("the drawers stay out of the motion", async ({ page }) => {
  await page.setViewportSize({ width: 600, height: 800 });
  await page.goto(MODULE_PAGE);
  await clientReady(page);

  const inline = await page
    .locator("details.drawer")
    .first()
    .evaluate((drawer: HTMLDetailsElement) => {
      drawer.querySelector("summary")!.click();
      return drawer.style.height;
    });

  expect(inline).toBe("");
  expect(await runningAnimations(page)).toBe(0);
});

test("the target settles after a jump", async ({ page }) => {
  await page.goto(`${MODULE_PAGE}#sum_refl`);

  const running = await page
    .locator(".decl:target > pre.statement")
    .evaluate((statement) => statement.getAnimations().map((animation) => (animation as CSSAnimation).animationName));

  expect(running).toEqual(["rocq-settle"]);
});

test("a card appears with motion", async ({ page }) => {
  await page.goto(MODULE_PAGE);

  const running = await page.evaluate(() => {
    const card = document.createElement("div");
    card.className = "card";
    document.body.append(card);
    return card.getAnimations().map((animation) => (animation as CSSAnimation).animationName);
  });

  expect(running).toEqual(["rocq-card-in"]);
});

test("reduced motion makes every animated property instant", async ({ page }) => {
  await page.emulateMedia({ reducedMotion: "reduce" });
  await page.goto(`${MODULE_PAGE}#sum_refl`);
  await clientReady(page);

  await page.evaluate(() => {
    const card = document.createElement("div");
    card.className = "card";
    document.body.append(card);
  });

  await page.locator("details.proof summary").first().click();

  await expect(page.locator(FOLD).first()).toHaveJSProperty("open", true);
  expect(await runningAnimations(page)).toBe(0);
});

test("a change to reduced motion after load applies to the next fold", async ({ page }) => {
  await page.goto(MODULE_PAGE);
  await clientReady(page);
  await page.emulateMedia({ reducedMotion: "reduce" });

  await page.locator("details.proof summary").first().click();

  await expect(page.locator(FOLD).first()).toHaveJSProperty("open", true);
  expect(await runningAnimations(page)).toBe(0);
});

test.describe("with JavaScript off", () => {
  test.use({ javaScriptEnabled: false });

  test("a fold still opens", async ({ page }) => {
    await page.goto(MODULE_PAGE);
    await page.locator(`${FOLD} summary`).first().click();
    await expect(page.locator(`${FOLD} pre`).first()).toBeVisible();
  });
});
