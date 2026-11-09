import type { Page } from "@playwright/test";
import { clientReady, expect, GUARANTEES_PAGE, LONG_PAGE, MODULE_PAGE, OTHER_PAGE, test } from "./support";

/** Where a jump lands its target, as a share of the viewport's height from its top. */
const LANDING_SHARE = 0.05;

/** A jump to a section, which holds it in view until the reader scrolls away. */
const scrollTo = (page: Page, id: string) =>
  page.evaluate((target) => {
    location.hash = encodeURIComponent(target);
  }, id);

const currentSection = (page: Page) => page.locator(".current-decl");

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

for (const height of [500, 700, 1300, 1600]) {
  test(`a jump in a ${height} px window marks a tightly packed target and shows its lists`, async ({ page }) => {
    await page.setViewportSize({ width: 1280, height });

    const expectTarget = async (id: string) => {
      await expect(outline(page, ".pane-right", id)).toHaveClass(/in-view/);
      await expect(page.locator(".pane-right a.in-view")).toHaveCount(1);

      const landed = await page.evaluate((target) => {
        const section = document.getElementById(target)!;
        const lists = (root: Element | null | undefined, name: string, inner: string) =>
          root?.querySelector(inner.replace("NAME", name))?.textContent ?? "";

        return {
          top: section.getBoundingClientRect().top,
          uses: [lists(section, "uses", '.ref-list[data-list="NAME"]'), lists(document, "uses", '.pane-right [data-slot="NAME"]')],
          usedBy: [lists(section, "used-by", '.ref-list[data-list="NAME"]'), lists(document, "used-by", '.pane-right [data-slot="NAME"]')],
        };
      }, id);

      expect(landed.top).toBeGreaterThan(0);
      expect(landed.top).toBeLessThanOrEqual(LANDING_SHARE * height);
      expect(landed.uses[1]).toBe(landed.uses[0]);
      expect(landed.usedBy[1]).toBe(landed.usedBy[0]);
    };

    // Each of these has a next section close below it.
    for (const id of ["shared", "shared~2", "sum_refl'"]) {
      await page.goto(`${MODULE_PAGE}#${id}`);
      await clientReady(page);
      await expectTarget(id);
    }

    await outline(page, ".pane-right", "first_use").click();
    await expectTarget("first_use");
    await outline(page, ".pane-right", "shared").click();
    await expectTarget("shared");
  });
}

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

for (const [minHeight, height] of [[500, 500], [500, 700], [900, 500], [900, 700]]) {
  test(`declarations ${minHeight} px tall in a ${height} px viewport keep the marker and the lists on the section at the line`, async ({ page }) => {
    await page.setViewportSize({ width: 1280, height });
    await page.goto(MODULE_PAGE);
    await clientReady(page);
    await page.addStyleTag({ content: `.decl { min-height: ${minHeight}px; }` });

    const result = await page.evaluate(async () => {
      const frames = async () => {
        await new Promise(requestAnimationFrame);
        await new Promise(requestAnimationFrame);
      };

      await frames();

      const sections = [
        ...new Set([
          ...document.querySelectorAll(".decl[id]"),
          ...[...document.querySelectorAll('.pane-right .pane-item[href^="#"]')].map((link) =>
            document.getElementById(link.getAttribute("href")!.slice(1)),
          ),
        ]),
      ].filter((section): section is Element => section !== null);

      const top = (section: Element) => section.getBoundingClientRect().top;
      const declarations = sections.filter((section) => section.matches(".decl"));
      const view = document.documentElement.clientHeight;
      const scrollEnd = document.documentElement.scrollHeight - view;
      const steps = Array.from({ length: Math.ceil(scrollEnd / 37) }, (_, index) => index * 37);

      // Each position puts the line inside the gap after a declaration. The line moves with the scroll position.
      const gaps = declarations.slice(0, -1).map((declaration, index) => {
        const bottom = declaration.getBoundingClientRect().bottom + scrollY;
        const next = declarations[index + 1].getBoundingClientRect().top + scrollY;
        return ((bottom + next) / 2) * (scrollEnd / (scrollEnd + view));
      });

      const problems: string[] = [];
      let inGaps = 0;

      const check = async (position: number, isGap: boolean) => {
        window.scrollTo(0, position);
        await frames();

        const line = view * (document.documentElement.scrollTop / scrollEnd);
        const tops = sections.map(top);

        // A top within a pixel of the line can land on either side of it.
        if (tops.some((value) => Math.abs(value - line) < 1)) return;

        const above = sections.filter((_, index) => tops[index] <= line);
        if (above.length === 0) return;
        if (isGap) inGaps++;

        const expected = above.reduce((best, section) => (top(section) >= top(best) ? section : best));
        const href = `#${expected.id}`;
        const links = [...document.querySelectorAll('.pane-right .pane-item[href^="#"]')];

        if (expected.matches(".decl") && document.querySelector(".current-decl")?.id !== expected.id) problems.push(`y=${Math.round(scrollY)}: not on ${expected.id}`);

        if (links.some((link) => link.getAttribute("href") === href)) {
          const marked = document.querySelector(".pane-right .in-view")?.getAttribute("href");
          if (marked !== href) problems.push(`y=${Math.round(scrollY)}: ${marked} marked at ${href}`);
        }

        if (expected.matches(".decl")) {
          for (const name of ["uses", "used-by"]) {
            const inline = expected.querySelector(`.ref-list[data-list="${name}"]`)?.textContent ?? "";
            const shown = document.querySelector(`.pane-right [data-slot="${name}"]`)?.textContent ?? "";
            if (inline !== shown) problems.push(`y=${Math.round(scrollY)}: ${name} of ${href} shows "${shown}"`);
          }
        }
      };

      for (const position of steps) await check(position, false);
      for (const position of gaps) await check(position, true);

      return { problems, positions: steps.length, inGaps };
    });

    expect(result.positions).toBeGreaterThan(50);
    expect(result.inGaps).toBeGreaterThan(5);
    expect(result.problems).toEqual([]);
  });
}

test.describe("the reading position runs from the view's top to its bottom", () => {
  test("at the start the first section is marked, with another starting just below the top", async ({ page }) => {
    await page.goto(LONG_PAGE);
    await clientReady(page);
    await page.addStyleTag({ content: ".page-title, .topbar, .doc, .prose { display: none; } .decl { margin: 2px 0; }" });
    await page.evaluate(() => window.scrollTo(0, 0));

    const sections = page.locator("article .decl[id], article h2[id], article h3[id]");
    await expect(currentSection(page)).toHaveAttribute("id", (await sections.first().getAttribute("id"))!);
    expect((await sections.nth(1).boundingBox())!.y, "another section starts in view").toBeLessThan(200);
  });

  test("halfway, the marked section holds the viewport's middle", async ({ page }) => {
    await page.goto(LONG_PAGE);
    await clientReady(page);

    await page.evaluate(() => {
      const root = document.documentElement;
      window.scrollTo(0, (root.scrollHeight - root.clientHeight) / 2);
    });

    await expect
      .poll(async () => {
        const middle = await page.evaluate(() => document.documentElement.clientHeight / 2);
        const marked = (await currentSection(page).boundingBox())!;
        const next = await currentSection(page).evaluate((section) => section.nextElementSibling?.getBoundingClientRect().top ?? Infinity);
        return marked.y <= middle + 1 && next > middle - 1;
      })
      .toBe(true);
  });

  test("at the end the last section is marked", async ({ page }) => {
    await page.goto(LONG_PAGE);
    await clientReady(page);
    await page.evaluate(() => window.scrollTo(0, document.documentElement.scrollHeight));

    const last = await page.evaluate(() => [...document.querySelectorAll(".decl[id]")].pop()!.id);
    await expect(currentSection(page)).toHaveAttribute("id", last);
  });

  test("a jump to a short mid-page section marks it, and it stays marked with no further scroll", async ({ page }) => {
    await page.goto(LONG_PAGE);
    await clientReady(page);
    await scrollTo(page, "long_15");

    await expect(currentSection(page)).toHaveAttribute("id", "long_15");
    await page.waitForTimeout(200);
    await expect(currentSection(page)).toHaveAttribute("id", "long_15");
    await expect(outline(page, ".pane-right", "long_15")).toHaveClass(/in-view/);
  });

  test("scrolling away after a jump moves the mark", async ({ page }) => {
    await page.goto(LONG_PAGE);
    await clientReady(page);
    await scrollTo(page, "long_15");
    await expect(currentSection(page)).toHaveAttribute("id", "long_15");

    await page.evaluate(() => window.scrollBy(0, 240));
    await expect(currentSection(page)).not.toHaveAttribute("id", "long_15");
  });

  test("a page with no scroll marks its first section", async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 3000 });
    await page.goto(OTHER_PAGE);
    await clientReady(page);
    await page.addStyleTag({ content: ":root .page::after { display: none !important; } .center { padding-bottom: 0 !important; }" });

    const fits = await page.evaluate(() => document.documentElement.scrollHeight === document.documentElement.clientHeight);
    expect(fits).toBe(true);

    const first = await page.locator("article .decl[id], article h1[id], article h2[id], article h3[id]").first().getAttribute("id");
    await expect(currentSection(page)).toHaveAttribute("id", first!);
  });
});

test("the pane scrolls the mark into view, and the page holds its position", async ({ page }) => {
  await page.goto(LONG_PAGE);
  await clientReady(page);

  const pane = page.locator(".pane-right");
  const heights = await pane.evaluate((element) => ({ scroll: element.scrollHeight, client: element.clientHeight }));
  expect(heights.scroll, "the outline is taller than the pane").toBeGreaterThan(heights.client);

  for (const id of ["long_30", "long_15", "long_01", "long_25"]) {
    await scrollTo(page, id);
    const position = await page.evaluate(() => window.scrollY);
    await expect(outline(page, ".pane-right", id)).toHaveClass(/in-view/);

    const inside = await page.evaluate((target) => {
      const pane = document.querySelector(".pane-right")!.getBoundingClientRect();
      const mark = document.querySelector(`.pane-right a.pane-item[href="#${target}"]`)!.getBoundingClientRect();
      const pinned = document.querySelector(".pane-right .pinned")!.getBoundingClientRect();
      return mark.top >= pane.top && mark.bottom <= pinned.top;
    }, id);

    expect(inside, `${id} sits in the pane, above the pinned lists`).toBe(true);
    expect(await page.evaluate(() => window.scrollY)).toBe(position);
  }
});

test("the mark stays above the pinned lists when new lists change their height", async ({ page }) => {
  await page.goto(MODULE_PAGE);
  await clientReady(page);
  await page.addStyleTag({ content: '.pinned [data-slot="used-by"]:not(:empty) { min-height: 150px; }' });

  const ids = await page.locator('.pane-right a.pane-item[href^="#"]').evaluateAll((links) => links.map((link) => link.getAttribute("href")!.slice(1)));
  expect(ids.length).toBeGreaterThan(10);

  for (const id of ids) {
    await scrollTo(page, id);
    await expect(outline(page, ".pane-right", id)).toHaveClass(/in-view/);

    const placed = await page.evaluate(() => ({
      pane: document.querySelector(".pane-right")!.getBoundingClientRect().top,
      mark: document.querySelector(".pane-right .in-view")!.getBoundingClientRect().toJSON(),
      pinned: document.querySelector(".pane-right .pinned")!.getBoundingClientRect().top,
    }));

    expect(placed.mark.top, `${id} below the pane's top`).toBeGreaterThanOrEqual(placed.pane);
    expect(placed.mark.bottom, `${id} above the lists`).toBeLessThanOrEqual(placed.pinned + 1);
  }
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
