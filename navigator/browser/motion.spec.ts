import type { Page } from "@playwright/test";
import { clientReady, expect, MODULE_PAGE, runningAnimations, test } from "./support";

const FOLD = "details.proof";

/** The first definition line's fold, and the keyword on its summary. */
const definitionLine = (page: Page) => {
  const fold = page.locator("article .panel > details.body").first();
  return { fold, word: fold.locator("summary .kw").first() };
};

const settled = (page: Page) => expect.poll(() => runningAnimations(page)).toBeLessThan(1);

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

/** Clicks an open fold's summary and records its height each frame, until it has closed and ten frames agree. */
const closeAndSample = (page: Page) =>
  page.evaluate(
    (selector) =>
      new Promise<{ heights: number[]; openAfterClick: boolean }>((resolve) => {
        const fold = document.querySelector<HTMLDetailsElement>(selector)!;
        const heights: number[] = [fold.getBoundingClientRect().height];
        fold.querySelector("summary")!.click();
        const openAfterClick = fold.open;

        const sample = () => {
          heights.push(fold.getBoundingClientRect().height);
          const last = heights.slice(-10);

          if (last.length === 10 && new Set(last).size === 1 && !fold.open) {
            resolve({ heights, openAfterClick });
          } else {
            requestAnimationFrame(sample);
          }
        };

        requestAnimationFrame(sample);
      }),
    FOLD,
  );

test("a fold closes through the same motion and leaves nothing inline", async ({ page }) => {
  await page.goto(MODULE_PAGE);
  await clientReady(page);
  await openAndSample(page);

  const { heights, openAfterClick } = await closeAndSample(page);
  const open = heights[0];
  const closed = heights[heights.length - 1];
  const between = new Set(heights.filter((height) => height > closed && height < open));

  expect(openAfterClick).toBe(true);
  expect(between.size).toBeGreaterThan(2);
  expect([...heights].sort((a, b) => b - a)).toEqual(heights);

  const fold = page.locator(FOLD).first();
  await expect(fold).toHaveJSProperty("open", false);
  expect(await fold.getAttribute("style")).toBeFalsy();
  expect(await fold.getAttribute("data-closing")).toBeNull();
});

test("a second click while a fold is opening sends it back closed", async ({ page }) => {
  await page.goto(MODULE_PAGE);
  await clientReady(page);

  const height = await page
    .locator(FOLD)
    .first()
    .evaluate((fold: HTMLDetailsElement) => {
      const height = fold.getBoundingClientRect().height;
      const summary = fold.querySelector("summary")!;
      summary.click();
      summary.click();
      return height;
    });

  const fold = page.locator(FOLD).first();
  await expect(fold).toHaveJSProperty("open", false);
  await expect.poll(() => fold.getAttribute("style")).toBeFalsy();
  expect(await fold.evaluate((element) => element.getBoundingClientRect().height)).toBe(height);
});

test("a second click while a fold is closing sends it back open", async ({ page }) => {
  await page.goto(MODULE_PAGE);
  await clientReady(page);
  await openAndSample(page);

  const fold = page.locator(FOLD).first();
  await fold.evaluate((element: HTMLDetailsElement) => {
    const summary = element.querySelector("summary")!;
    summary.click();
    summary.click();
  });

  await expect.poll(() => fold.getAttribute("style")).toBeFalsy();
  await expect(fold).toHaveJSProperty("open", true);
  expect(await fold.getAttribute("data-closing")).toBeNull();
  await expect(fold.locator("pre")).toBeVisible();
});

test("reduced motion closes a fold at once", async ({ page }) => {
  await page.emulateMedia({ reducedMotion: "reduce" });
  await page.goto(MODULE_PAGE);
  await clientReady(page);

  const summary = page.locator(`${FOLD} summary`).first();
  await summary.click();
  await summary.click();

  await expect(page.locator(FOLD).first()).toHaveJSProperty("open", false);
  expect(await runningAnimations(page)).toBe(0);
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
    .locator(".decl:target > .panel")
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

test("the keyboard toggles a fold while a selection lies in its summary", async ({ page }) => {
  await page.goto(MODULE_PAGE);
  await clientReady(page);
  const { fold, word } = definitionLine(page);

  await word.evaluate((element) => {
    element.closest("summary")!.focus();
    const range = document.createRange();
    range.selectNodeContents(element);
    getSelection()!.removeAllRanges();
    getSelection()!.addRange(range);
  });

  expect(await page.evaluate(() => getSelection()!.toString().length)).toBeGreaterThan(0);
  await page.keyboard.press("Enter");
  await expect(fold).toHaveJSProperty("open", false);
});

test("a double click selects a word on a definition line and leaves the fold open", async ({ page }) => {
  await page.goto(MODULE_PAGE);
  await clientReady(page);
  const { fold, word } = definitionLine(page);

  await word.dblclick();
  await page.waitForTimeout(400);

  await expect(fold).toHaveJSProperty("open", true);
  expect(await page.evaluate(() => getSelection()!.toString().trim().length)).toBeGreaterThan(0);
});
