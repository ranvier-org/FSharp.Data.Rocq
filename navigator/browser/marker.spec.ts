import type { Page } from "@playwright/test";
import { clientReady, expect, GUARANTEES_PAGE, MODULE_PAGE, test } from "./support";

const scrollTo = (page: Page, id: string) =>
  page.evaluate((target) => document.getElementById(target)!.scrollIntoView({ block: "start" }), id);

const outline = (page: Page, copy: string, id: string) => page.locator(`${copy} a.pane-item[href="#${id}"]`);

/** The name of each declaration with a Uses list and a Used by list. */
const declarationWithBothLists = (page: Page) =>
  page.evaluate(() => {
    const both = [...document.querySelectorAll(".decl[id]")].filter(
      (decl) =>
        decl.querySelector('.ref-list[data-list="uses"]') !== null && decl.querySelector('.ref-list[data-list="used-by"]') !== null,
    );
    return both[0].id;
  });

test.beforeEach(async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 500 });
});

test("the marker follows the section in view in the outline", async ({ page }) => {
  await page.goto(MODULE_PAGE);
  await clientReady(page);

  await scrollTo(page, "first_use");
  await expect(outline(page, ".pane-right", "first_use")).toHaveClass(/in-view/);
  await expect(outline(page, ".pane-right", "first_use")).toHaveAttribute("aria-current", "location");
  await expect(outline(page, ".drawer", "first_use")).toHaveClass(/in-view/);

  await scrollTo(page, "sum_zero");
  await expect(outline(page, ".pane-right", "sum_zero")).toHaveClass(/in-view/);
  await expect(outline(page, ".pane-right", "first_use")).not.toHaveClass(/in-view/);
  await expect(outline(page, ".pane-right", "first_use")).not.toHaveAttribute("aria-current", "location");
  await expect(page.locator(".pane-right a.in-view")).toHaveCount(1);
});

test("a jump marks its target", async ({ page }) => {
  await page.goto(`${MODULE_PAGE}#sum_zero`);
  await clientReady(page);

  await expect(outline(page, ".pane-right", "sum_zero")).toHaveClass(/in-view/);
});

test("the marker follows the Guarantees page", async ({ page }) => {
  await page.goto(GUARANTEES_PAGE);
  await clientReady(page);

  await scrollTo(page, "group-falsified");
  await expect(outline(page, ".pane-right", "group-falsified")).toHaveClass(/in-view/);
});

test("the last declaration can reach the marker", async ({ page }) => {
  await page.goto(MODULE_PAGE);
  await clientReady(page);

  const last = await page.evaluate(() => [...document.querySelectorAll(".decl[id]")].pop()!.id);
  await page.evaluate(() => window.scrollTo(0, document.documentElement.scrollHeight));

  await expect(outline(page, ".pane-right", last)).toHaveClass(/in-view/);
});

test("wide screens move the current declaration's lists to the right pane and hide the inline ones", async ({ page }) => {
  await page.goto(MODULE_PAGE);
  await clientReady(page);

  const id = await declarationWithBothLists(page);
  await scrollTo(page, id);

  for (const slot of ["uses", "used-by"]) {
    const inline = await page.locator(`[id="${id}"] .ref-list[data-list="${slot}"]`).first().textContent();
    await expect(page.locator(`.pane-right [data-slot="${slot}"]`)).toHaveText(inline!);
    await expect(page.locator(`.pane-right [data-slot="${slot}"]`)).toBeVisible();
    await expect(page.locator(`.drawer [data-slot="${slot}"]`)).toBeEmpty();
  }

  await expect(page.locator(".decl details.refs").first()).toBeHidden();
});

test("a declaration with one list clears the other slot", async ({ page }) => {
  await page.goto(MODULE_PAGE);
  await clientReady(page);

  const id = await declarationWithBothLists(page);
  await scrollTo(page, id);
  await expect(page.locator('.pane-right [data-slot="uses"] .ref-list')).toHaveCount(1);

  const usedByOnly = await page.evaluate(
    () =>
      [...document.querySelectorAll(".decl[id]")].find(
        (decl) =>
          decl.querySelector('.ref-list[data-list="uses"]') === null && decl.querySelector('.ref-list[data-list="used-by"]') !== null,
      )?.id,
  );

  expect(usedByOnly).toBeTruthy();
  await scrollTo(page, usedByOnly!);

  await expect(page.locator('.pane-right [data-slot="uses"] .ref-list')).toHaveCount(0);
  await expect(page.locator('.pane-right [data-slot="used-by"] .ref-list')).toHaveCount(1);
});

test("narrow screens keep the lists inline and the drawer's slots empty", async ({ page }) => {
  await page.setViewportSize({ width: 600, height: 500 });
  await page.goto(MODULE_PAGE);
  await clientReady(page);

  const id = await declarationWithBothLists(page);
  await scrollTo(page, id);

  await expect(page.locator(`[id="${id}"] details.refs`)).toBeVisible();
  await expect(page.locator('.drawer [data-slot="uses"]')).toBeEmpty();
  await expect(page.locator('.drawer [data-slot="used-by"]')).toBeEmpty();
  await expect(page.locator(".pane-right")).toBeHidden();
});

test("a resize from wide to narrow brings the inline lists back", async ({ page }) => {
  await page.goto(MODULE_PAGE);
  await clientReady(page);

  const id = await declarationWithBothLists(page);
  await scrollTo(page, id);
  await expect(page.locator(`[id="${id}"] details.refs`)).toBeHidden();

  await page.setViewportSize({ width: 600, height: 500 });
  await expect(page.locator(`[id="${id}"] details.refs`)).toBeVisible();
});

test.describe("with JavaScript off", () => {
  test.use({ javaScriptEnabled: false });

  test("wide screens keep the inline lists", async ({ page }) => {
    await page.goto(MODULE_PAGE);
    await expect(page.locator(".decl details.refs").first()).toBeVisible();
  });
});
