import type { Page } from "@playwright/test";
import { clientReady, expect, LONG_PAGE, MODULE_PAGE, test } from "./support";

const REFERENCES_KEY = "rocq-navigator-references";

const place = (page: Page, value: string) => page.addInitScript(([key, placement]) => localStorage.setItem(key, placement), [REFERENCES_KEY, value]);

const jump = (page: Page, id: string) =>
  page.evaluate((target) => {
    location.hash = encodeURIComponent(target);
  }, id);

const pinned = (page: Page) => page.locator(".pane-right .pinned");
const chips = (page: Page) => page.locator(".refs-chip");
const chipDialog = (page: Page) => page.locator(".chip-dialog");

/** The first declaration with a body fold and a chip, with its chip and the column its caret and line count fill. */
const foldedDeclaration = async (page: Page) => {
  const id = await page.locator(".decl:has(> .panel > details.body):has(.refs-chip)[id]").first().getAttribute("id");
  expect(id).toBeTruthy();
  return { id: id!, chip: page.locator(`[id="${id}"] .refs-chip`), summary: page.locator(`[id="${id}"] > .panel > details.body > summary`) };
};

/** The rectangle of the caret and the line count at the right of a fold's first row. */
const caretArea = (page: Page, id: string) =>
  page.locator(`[id="${id}"] > .panel > details.body > summary`).evaluate((summary) => {
    const box = summary.getBoundingClientRect();
    const caret = getComputedStyle(summary, "::before");
    const count = getComputedStyle(summary, "::after");
    const width =
      parseFloat(caret.width) + parseFloat(caret.paddingLeft) + parseFloat(caret.paddingRight) +
      parseFloat(count.width) + parseFloat(count.paddingLeft) + parseFloat(count.paddingRight);

    return { x: box.right - width, y: box.top, width, height: 30 };
  });

const overlaps = (
  a: { x: number; y: number; width: number; height: number },
  b: { x: number; y: number; width: number; height: number },
) => a.x < b.x + b.width && b.x < a.x + a.width && a.y < b.y + b.height && b.y < a.y + a.height;

test.describe("pinned", () => {
  test.beforeEach(async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 900 });
  });

  test("is the default, in view at the bottom of the right pane, with the definition's name", async ({ page }) => {
    await page.goto(MODULE_PAGE);
    await clientReady(page);
    expect(await page.evaluate(() => document.documentElement.getAttribute("data-lists"))).toBe("pane");

    await jump(page, "first_use");
    await expect(pinned(page).locator(".lists-name")).toHaveText("first_use");
    await expect(pinned(page).locator(".lists-head small")).toHaveText("This definition");

    const pane = (await page.locator(".pane-right").boundingBox())!;
    const region = (await pinned(page).boundingBox())!;
    expect(Math.round(region.y + region.height)).toBe(Math.round(pane.y + pane.height));
    expect(region.height).toBeLessThanOrEqual(900 * 0.46 + 1);
  });

  test("the section in view carries the same bar inside its panel", async ({ page }) => {
    await page.emulateMedia({ reducedMotion: "reduce" });
    await page.goto(MODULE_PAGE);
    await clientReady(page);
    await jump(page, "first_use");

    const decl = page.locator('[id="first_use"]');
    await expect(decl).toHaveClass(/current-decl/);
    await expect(page.locator(".current-decl")).toHaveCount(1);
    expect(await decl.locator("> .panel").evaluate((panel) => getComputedStyle(panel).boxShadow)).toContain("3px 0px 0px 0px");
  });

  test("a definition with no lists shows one quiet line", async ({ page }) => {
    await page.goto(MODULE_PAGE);
    await clientReady(page);

    const bare = await page.evaluate(() => [...document.querySelectorAll(".decl[id]")].find((decl) => decl.querySelector(".ref-list") === null)?.id);
    expect(bare, "the fixture has a definition with no lists").toBeTruthy();
    await jump(page, bare!);

    await expect(pinned(page).locator(".lists-empty")).toHaveText("No uses or used-by entries.");
    await expect(pinned(page).locator("[data-slot] .ref-list")).toHaveCount(0);
  });

  test("a heading after a jump shows no stale name, and a heading after its definition keeps it", async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 500 });
    await page.goto(MODULE_PAGE);
    await clientReady(page);

    const headings = await page.locator("article h2[id], article h3[id]").evaluateAll((elements) => elements.map((element) => element.id));
    expect(headings.length).toBeGreaterThan(0);
    await jump(page, headings[0]);
    await expect(page.locator(`[id="${headings[0]}"]`)).toHaveClass(/current-decl/);

    const owner = await page.evaluate((id) => {
      let element = document.getElementById(id)!.previousElementSibling;
      while (element && !element.matches(".decl")) element = element.previousElementSibling;
      return element && element.getBoundingClientRect().bottom > 0 ? element.id : "";
    }, headings[0]);

    await expect(pinned(page).locator(".lists-name")).toHaveText(owner);
    if (owner === "") await expect(pinned(page).locator(".lists-empty")).toHaveText("No definition in view.");
  });

  test("a reader scrolling the lists keeps their position through a section change, and the pane stays put", async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 500 });
    await page.goto(MODULE_PAGE);
    await clientReady(page);
    await page.addStyleTag({ content: ".pane-right .pinned { max-height: 60px !important; }" });

    await jump(page, "first_use");
    await expect(pinned(page).locator(".lists-name")).toHaveText("first_use");

    const scrolled = await pinned(page).evaluate((region) => {
      region.scrollTop = 40;
      return region.scrollTop;
    });
    expect(scrolled, "the lists are taller than the region").toBeGreaterThan(0);

    const pane = page.locator(".pane-right");
    const before = await pane.evaluate((element) => element.getBoundingClientRect().bottom);

    await page.evaluate(() => window.scrollBy(0, 300));
    await expect(pinned(page).locator(".lists-name")).not.toHaveText("first_use");

    const after = await pinned(page).evaluate((region) => ({ top: region.scrollTop, max: region.scrollHeight - region.clientHeight }));
    expect(after.top).toBe(Math.min(scrolled, after.max));

    const region = (await pinned(page).boundingBox())!;
    expect(Math.round(region.y + region.height)).toBe(Math.round(before));
  });

  test("the wheel over the lists stops at their end and leaves the page where it was", async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 500 });
    await page.goto(MODULE_PAGE);
    await clientReady(page);
    await page.addStyleTag({ content: ".pane-right .pinned { max-height: 60px !important; }" });
    await jump(page, "first_use");
    await expect(pinned(page).locator(".lists-name")).toHaveText("first_use");

    const position = await page.evaluate(() => window.scrollY);
    const paneTop = await page.locator(".pane-right").evaluate((element) => element.scrollTop);
    const region = (await pinned(page).boundingBox())!;

    await page.mouse.move(region.x + 20, region.y + 20);
    await page.mouse.wheel(0, 2000);
    await page.waitForTimeout(200);

    expect(await page.evaluate(() => window.scrollY)).toBe(position);
    expect(await page.locator(".pane-right").evaluate((element) => element.scrollTop)).toBe(paneTop);
    await expect(pinned(page).locator(".lists-name")).toHaveText("first_use");
  });

  test("the outline can always reach above the lists", async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 500 });
    await page.goto(LONG_PAGE);
    await clientReady(page);

    const last = await page.locator(".pane-right a.pane-item").last().getAttribute("href");
    await jump(page, last!.slice(1));
    await expect(page.locator(".pane-right a.in-view")).toHaveAttribute("href", last!);

    const mark = (await page.locator(".pane-right a.in-view").boundingBox())!;
    const region = (await pinned(page).boundingBox())!;
    expect(mark.y + mark.height).toBeLessThanOrEqual(region.y + 1);
  });
});

test.describe("badges", () => {
  test.beforeEach(async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 900 });
    await place(page, "badges");
    await page.goto(MODULE_PAGE);
    await clientReady(page);
  });

  test("a chip blends with its panel at 10 px and sits on every definition with lists", async ({ page }) => {
    const style = await chips(page).first().evaluate((chip) => {
      const own = getComputedStyle(chip);
      const panel = getComputedStyle(chip.closest(".panel")!);
      return {
        size: own.fontSize,
        height: chip.getBoundingClientRect().height,
        background: own.backgroundColor,
        panelBackground: panel.backgroundColor,
        border: own.borderTopColor,
        panelBorder: panel.borderTopColor,
      };
    });

    expect(style.size).toBe("10px");
    expect(style.height).toBeLessThanOrEqual(16);
    expect(style.background).toBe(style.panelBackground);
    expect(style.border).toBe(style.panelBorder);

    const mismatched = await page.evaluate(
      () => [...document.querySelectorAll(".decl[id]")].filter((decl) => (decl.querySelector(".ref-list") !== null) !== (decl.querySelector(".refs-chip") !== null)).length,
    );
    expect(mismatched).toBe(0);
  });

  test("no chip's rectangle meets another definition's panel", async ({ page }) => {
    const touching = await page.evaluate(() => {
      const panels = [...document.querySelectorAll(".decl > .panel")];
      const found: string[] = [];

      for (const chip of document.querySelectorAll(".refs-chip")) {
        const a = chip.getBoundingClientRect();
        const own = chip.closest(".panel");

        for (const panel of panels) {
          if (panel === own) continue;
          const b = panel.getBoundingClientRect();
          if (a.left < b.right && b.left < a.right && a.top < b.bottom && b.top < a.bottom) found.push(chip.closest(".decl")!.id);
        }
      }

      return found;
    });

    expect(touching).toEqual([]);
  });

  test("neither the chip nor its dialog covers the caret, and the caret stays clickable", async ({ page }) => {
    const { id, chip, summary } = await foldedDeclaration(page);
    await chip.scrollIntoViewIfNeeded();
    await chip.hover();
    await expect(chipDialog(page)).toBeVisible();

    const caret = await caretArea(page, id);
    expect(caret.width).toBeGreaterThan(10);
    expect(overlaps((await chip.boundingBox())!, caret)).toBe(false);
    expect(overlaps((await chipDialog(page).boundingBox())!, caret)).toBe(false);

    const covered = await page.evaluate(({ x, y }) => document.elementFromPoint(x, y)?.closest("summary") !== null, {
      x: caret.x + caret.width - 12,
      y: caret.y + 18,
    });
    expect(covered, "the caret's point belongs to its summary").toBe(true);

    const fold = page.locator(`[id="${id}"] > .panel > details.body`);
    const open = await fold.evaluate((element) => (element as HTMLDetailsElement).open);
    const box = (await summary.boundingBox())!;
    await page.mouse.click(box.x + box.width - 12, box.y + 18);
    await expect(fold).toHaveJSProperty("open", !open);
  });

  test("hover opens it with the name and lists, and the pointer on it keeps it open", async ({ page }) => {
    const { id, chip } = await foldedDeclaration(page);
    await chip.scrollIntoViewIfNeeded();
    await chip.hover();

    await expect(chipDialog(page)).toBeVisible();
    const name = await page.locator(`[id="${id}"]`).evaluate((decl) => decl.getAttribute("data-name") ?? decl.id);
    await expect(chipDialog(page).locator(".lists-name")).toHaveText(name);

    const inline = await page.locator(`[id="${id}"] details.refs .ref-list`).first().textContent();
    expect(await chipDialog(page).locator(".ref-list").first().textContent()).toBe(inline);

    const box = (await chipDialog(page).boundingBox())!;
    await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2, { steps: 4 });
    await page.waitForTimeout(300);
    await expect(chipDialog(page)).toBeVisible();

    await page.locator(".topbar").hover();
    await expect(chipDialog(page)).toBeHidden();
  });

  test("a reference inside it opens a card without closing it, and Esc closes the card first", async ({ page }) => {
    const { chip } = await foldedDeclaration(page);
    await chip.scrollIntoViewIfNeeded();
    await chip.hover();
    await expect(chipDialog(page)).toBeVisible();

    const reference = chipDialog(page).locator("[data-ref]").first();
    await reference.hover();
    const card = page.locator(".card");
    await expect(card).toBeVisible();
    await page.waitForTimeout(250);
    await expect(chipDialog(page)).toBeVisible();

    await page.keyboard.press("Escape");
    await expect(card).toBeHidden();
    await expect(chipDialog(page)).toBeVisible();

    await page.keyboard.press("Escape");
    await expect(chipDialog(page)).toBeHidden();
  });

  test("keyboard focus opens it, Enter moves into it, and Esc returns to the chip", async ({ page }) => {
    const { chip } = await foldedDeclaration(page);
    await chip.scrollIntoViewIfNeeded();

    await chip.focus();
    await page.keyboard.press("Shift+Tab");
    await page.keyboard.press("Tab");
    await expect(chip).toBeFocused();
    await expect(chipDialog(page)).toBeVisible();
    await expect(chip).toHaveAttribute("aria-expanded", "true");

    await page.keyboard.press("Enter");
    await expect.poll(() => page.evaluate(() => document.querySelector(".chip-dialog")!.contains(document.activeElement))).toBe(true);

    // The focused link's card closes first.
    await expect(page.locator(".card")).toBeVisible();
    await page.keyboard.press("Escape");
    await expect(page.locator(".card")).toBeHidden();
    await expect(chipDialog(page)).toBeVisible();

    await page.keyboard.press("Escape");
    await expect(chipDialog(page)).toBeHidden();
    await expect(chip).toBeFocused();
    await expect(chip).not.toHaveAttribute("aria-expanded");
  });

  test("Tab past the dialog's last link returns to the chip", async ({ page }) => {
    const { chip } = await foldedDeclaration(page);
    await chip.scrollIntoViewIfNeeded();
    await chip.focus();
    await page.keyboard.press("Enter");
    await expect.poll(() => page.evaluate(() => document.querySelector(".chip-dialog")!.contains(document.activeElement))).toBe(true);

    for (let index = 0; index < 40; index++) {
      if (!(await chipDialog(page).isVisible())) break;
      await page.keyboard.press("Tab");
      await page.waitForTimeout(10);
    }

    await expect(chipDialog(page)).toBeHidden();
    await expect(chip).toBeFocused();
  });

  test("a click outside closes it", async ({ page }) => {
    const { chip } = await foldedDeclaration(page);
    await chip.scrollIntoViewIfNeeded();
    await chip.click();
    await expect(chipDialog(page)).toBeVisible();
    await page.locator(".topbar .crumb").click();
    await expect(chipDialog(page)).toBeHidden();
  });

  test("a chip lies outside the pre, so copied statements stay exact", async ({ page }) => {
    expect(await page.locator("pre .refs-chip").count()).toBe(0);
    expect(await chips(page).count()).toBeGreaterThan(0);
  });
});

test.describe("badges by touch", () => {
  test.use({ hasTouch: true });

  test("a tap opens it and a tap outside closes it", async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 900 });
    await place(page, "badges");
    await page.goto(MODULE_PAGE);
    await clientReady(page);

    const { chip } = await foldedDeclaration(page);
    await chip.scrollIntoViewIfNeeded();
    await chip.tap();
    await expect(chipDialog(page)).toBeVisible();

    await page.locator(".topbar .crumb").tap();
    await expect(chipDialog(page)).toBeHidden();
  });
});

test.describe("under definitions", () => {
  test("the inline folds show, and neither the pinned region nor the chips do", async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 900 });
    await place(page, "under");
    await page.goto(MODULE_PAGE);
    await clientReady(page);

    await expect(page.locator(".decl details.refs").first()).toBeVisible();
    await expect(pinned(page)).toBeHidden();
    await expect(chips(page).first()).toBeHidden();
  });
});

test.describe("the gap around a References line", () => {
  /** For each declaration whose references fold is displayed: its gaps to the panel above and the next declaration below. */
  const refsGaps = (page: Page) =>
    page.evaluate(() =>
      [...document.querySelectorAll(".decl")].flatMap((decl) => {
        const refs = decl.querySelector<HTMLDetailsElement>(":scope > details.refs");
        let next = decl.nextElementSibling;
        while (next && !next.matches(".decl")) next = next.nextElementSibling;
        if (!refs || !next || getComputedStyle(refs).display === "none") return [];

        return [
          {
            id: decl.id,
            quiet: decl.matches(".quiet"),
            open: refs.open,
            above: refs.querySelector("summary")!.getBoundingClientRect().top - decl.querySelector(":scope > .panel")!.getBoundingClientRect().bottom,
            below: next.getBoundingClientRect().top - refs.getBoundingClientRect().bottom,
          },
        ];
      }),
    );

  const expectTight = async (page: Page, open: boolean) => {
    const gaps = await refsGaps(page);
    expect(gaps.some((gap) => gap.quiet), "a quiet declaration with references").toBe(true);
    expect(gaps.some((gap) => !gap.quiet), "a documented declaration with references").toBe(true);

    for (const gap of gaps) {
      expect(gap.open, gap.id).toBe(open);
      expect(gap.above, `${gap.id}: panel to summary`).toBeGreaterThanOrEqual(0);
      expect(gap.above, `${gap.id}: panel to summary`).toBeLessThanOrEqual(4);
      expect(gap.below, `${gap.id}: fold to the next declaration`).toBeGreaterThanOrEqual(14);
      expect(gap.below, `${gap.id}: fold to the next declaration`).toBeGreaterThanOrEqual(3 * gap.above);
    }
  };

  for (const width of [1440, 390]) {
    for (const open of [false, true]) {
      test(`under definitions at ${width} px, ${open ? "open" : "closed"}`, async ({ page }) => {
        await page.setViewportSize({ width, height: 900 });
        await place(page, "under");
        if (open) await page.addInitScript(() => localStorage.setItem("rocq-navigator-references-open", "open"));
        await page.goto(MODULE_PAGE);
        await clientReady(page);
        await expectTight(page, open);
      });
    }
  }

  test("below 900 px with the lists pinned on wider screens", async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 900 });
    await page.goto(MODULE_PAGE);
    await clientReady(page);
    expect(await page.evaluate(() => document.documentElement.getAttribute("data-lists"))).toBe("pane");
    await expectTight(page, false);
  });

  test("after a failed client restores the folds", async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.route("**/navigator.js", (route) => route.fulfill({ contentType: "text/javascript", body: "" }));
    await page.goto(MODULE_PAGE);
    await page.waitForLoadState("load");
    await expectTight(page, false);
  });

  for (const [placement, quiet] of [["pane", "3.2px"], ["badges", "16px"]]) {
    test(`${placement} keeps the spacing of a declaration whose fold is hidden`, async ({ page }) => {
      await page.setViewportSize({ width: 1440, height: 900 });
      await place(page, placement);
      await page.goto(MODULE_PAGE);
      await clientReady(page);

      const margins = await page.evaluate(() => {
        const read = (selector: string) =>
          [...new Set([...document.querySelectorAll(selector)].map((element) => getComputedStyle(element).marginBottom))];
        return { quiet: read(".decl.quiet:has(> details.refs)"), panel: read(".decl:has(> details.refs) > .panel") };
      });

      expect(margins).toEqual({ quiet: [quiet], panel: ["8px"] });
    });
  }

  test("under definitions leaves a declaration with no references as it was", async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 900 });
    await place(page, "under");
    await page.goto(LONG_PAGE);
    await clientReady(page);

    const margins = await page.evaluate(() => {
      const quiet = document.querySelector(".decl.quiet:not(:has(> details.refs))")!;
      return { decl: getComputedStyle(quiet).marginBottom, panel: getComputedStyle(quiet.querySelector(":scope > .panel")!).marginBottom };
    });
    expect(margins).toEqual({ decl: "3.2px", panel: "8px" });
  });
});
