import type { Page } from "@playwright/test";
import { clientReady, expect, GUARANTEES_PAGE, MODULE_PAGE, OTHER_PAGE, test } from "./support";

const LOOK_KEY = "rocq-navigator-look";
const MODE_KEY = "rocq-navigator-mode";

const rootAttribute = (page: Page, name: string) =>
  page.evaluate((attribute) => document.documentElement.getAttribute(attribute), name);

const pageBackground = (page: Page) => page.evaluate(() => getComputedStyle(document.body).backgroundColor);

const choice = (page: Page, label: string) =>
  page.locator('[data-island="switch"]').getByRole("button", { name: label, exact: true });

const throwingStorage = () =>
  Object.defineProperty(window, "localStorage", {
    get() {
      throw new DOMException("denied", "SecurityError");
    },
  });

test("nothing stored lands on Family, following the system mode", async ({ page }) => {
  await page.goto(MODULE_PAGE);
  await clientReady(page);

  expect(await rootAttribute(page, "data-look")).toBe("family");
  await expect(choice(page, "Family")).toHaveAttribute("aria-pressed", "true");
  await expect(choice(page, "Journal")).toHaveAttribute("aria-pressed", "false");
  await expect(choice(page, "Auto")).toHaveAttribute("aria-pressed", "true");
});

test("assistive technology reads each set of choices as a named group", async ({ page }) => {
  await page.goto(MODULE_PAGE);

  const island = page.locator('[data-island="switch"]');
  await expect(island.getByRole("group", { name: "Look" }).getByRole("button")).toHaveCount(2);
  await expect(island.getByRole("group", { name: "Mode" }).getByRole("button")).toHaveCount(3);
});

test("a stored value outside the known set lands on Family", async ({ page }) => {
  await page.addInitScript((key) => localStorage.setItem(key, "<img>"), LOOK_KEY);
  await page.goto(MODULE_PAGE);
  await clientReady(page);

  expect(await rootAttribute(page, "data-look")).toBe("family");
  await expect(choice(page, "Family")).toHaveAttribute("aria-pressed", "true");
});

test("the switch persists across pages in both looks and modes", async ({ page }) => {
  await page.goto(MODULE_PAGE);
  await clientReady(page);

  const backgrounds = new Set<string>();

  for (const [look, lookLabel] of [
    ["family", "Family"],
    ["journal", "Journal"],
  ]) {
    for (const [mode, modeLabel] of [
      ["light", "Light"],
      ["dark", "Dark"],
    ]) {
      await choice(page, lookLabel).click();
      await choice(page, modeLabel).click();

      expect(await rootAttribute(page, "data-look")).toBe(look);
      expect(await rootAttribute(page, "data-mode")).toBe(mode);
      const background = await pageBackground(page);
      backgrounds.add(background);

      for (const next of [OTHER_PAGE, GUARANTEES_PAGE, MODULE_PAGE]) {
        await page.goto(next);
        await clientReady(page);

        expect(await rootAttribute(page, "data-look")).toBe(look);
        expect(await rootAttribute(page, "data-mode")).toBe(mode);
        expect(await pageBackground(page)).toBe(background);
        await expect(choice(page, lookLabel)).toHaveAttribute("aria-pressed", "true");
        await expect(choice(page, modeLabel)).toHaveAttribute("aria-pressed", "true");
      }
    }
  }

  expect(backgrounds.size).toBe(4);
});

test("Auto follows the system after an override", async ({ page }) => {
  await page.emulateMedia({ colorScheme: "dark" });
  await page.goto(MODULE_PAGE);
  await clientReady(page);
  const dark = await pageBackground(page);

  await choice(page, "Light").click();
  expect(await pageBackground(page)).not.toBe(dark);

  await choice(page, "Auto").click();
  expect(await pageBackground(page)).toBe(dark);

  await page.reload();
  await clientReady(page);
  expect(await pageBackground(page)).toBe(dark);
});

test("the look is set before first paint", async ({ page }) => {
  await page.addInitScript(
    ([lookKey, modeKey]) => {
      localStorage.setItem(lookKey, "journal");
      localStorage.setItem(modeKey, "dark");
    },
    [LOOK_KEY, MODE_KEY],
  );

  // The bundle never runs and the stylesheet stays pending, so the only writer left is the head script.
  await page.route("**/assets/navigator.js", (route) => route.fulfill({ status: 200, contentType: "text/javascript", body: "" }));
  let release = () => {};
  const pending = new Promise<void>((resolve) => (release = resolve));
  await page.route("**/assets/navigator.css", async (route) => {
    await pending;
    await route.continue();
  });

  await page.goto(MODULE_PAGE, { waitUntil: "commit" });
  await page.waitForFunction(() => document.head !== null && document.querySelector('link[rel="stylesheet"]') !== null);

  expect(await page.evaluate(() => document.styleSheets.length)).toBe(0);
  expect(await rootAttribute(page, "data-look")).toBe("journal");
  expect(await rootAttribute(page, "data-mode")).toBe("dark");

  const order = await page.evaluate(() => {
    const children = [...document.head.children];
    return {
      script: children.findIndex((child) => child.tagName === "SCRIPT" && !child.hasAttribute("src")),
      stylesheet: children.findIndex((child) => child.matches('link[rel="stylesheet"]')),
      type: children.find((child) => child.tagName === "SCRIPT")?.getAttribute("type") ?? null,
    };
  });

  expect(order.script).toBeGreaterThanOrEqual(0);
  expect(order.script).toBeLessThan(order.stylesheet);
  expect(order.type).toBeNull();

  release();
});

test("with localStorage throwing, the page lands on Family", async ({ page }) => {
  await page.addInitScript(throwingStorage);
  await page.goto(MODULE_PAGE);
  await clientReady(page);

  expect(await rootAttribute(page, "data-look")).toBe("family");
  await expect(choice(page, "Family")).toHaveAttribute("aria-pressed", "true");
});

test("with localStorage throwing, the switch still changes this page and no error is logged", async ({ page }) => {
  await page.addInitScript(throwingStorage);
  await page.goto(MODULE_PAGE);
  await clientReady(page);

  await choice(page, "Journal").click();
  await choice(page, "Dark").click();

  expect(await rootAttribute(page, "data-look")).toBe("journal");
  expect(await rootAttribute(page, "data-mode")).toBe("dark");
  await expect(choice(page, "Journal")).toHaveAttribute("aria-pressed", "true");
});

test("no hydration mismatch is reported and the console has no errors", async ({ page }) => {
  await page.addInitScript(
    ([lookKey, modeKey]) => {
      localStorage.setItem(lookKey, "journal");
      localStorage.setItem(modeKey, "dark");
    },
    [LOOK_KEY, MODE_KEY],
  );

  for (const next of [MODULE_PAGE, OTHER_PAGE, GUARANTEES_PAGE]) {
    await page.goto(next);
    await clientReady(page);
    await expect(choice(page, "Journal")).toHaveAttribute("aria-pressed", "true");
  }
});

test("a hydration mismatch is reported, and the page stays readable", async ({ page, problems }) => {
  await page.route(`**${MODULE_PAGE}`, async (route) => {
    const response = await route.fetch();
    const body = await response.text();
    await route.fulfill({ response, body: body.replace(/<button[^>]*>Journal<\/button>/, "") });
  });

  await page.goto(MODULE_PAGE);
  await expect.poll(() => problems.some((problem) => problem.includes("Hydration"))).toBe(true);
  await expect(page.locator("article.page")).toBeVisible();
  await expect(page.locator("details.proof summary").first()).toBeVisible();

  problems.length = 0;
});

test("every statement sits in a pane, documented or not, and commands stay bare", async ({ page }) => {
  await page.goto(MODULE_PAGE);

  const paneOf = (selector: string) =>
    page.locator(selector).first().evaluate((element) => {
      const style = getComputedStyle(element);
      return {
        background: style.backgroundColor,
        border: style.borderTopWidth,
        padding: style.paddingTop,
        fontSize: style.fontSize,
        color: style.color,
      };
    });

  const documented = await paneOf(".decl.documented .panel");
  expect(documented.border).toBe("1px");
  expect(await paneOf(".decl.quiet .panel")).toEqual(documented);
  expect((await paneOf("pre.command")).border).toBe("0px");
});

test("proofs are the footer of the statement's pane", async ({ page }) => {
  await page.goto(MODULE_PAGE);

  const shape = await page.locator('.decl[id="sum_refl"] .panel').evaluate((pane) => {
    const rect = pane.getBoundingClientRect();
    const footer = pane.lastElementChild!;
    const box = footer.getBoundingClientRect();
    return {
      matches: footer.matches("details.proof"),
      left: box.left - rect.left,
      right: rect.right - box.right,
      bottom: rect.bottom - box.bottom,
      rule: getComputedStyle(footer).borderTopWidth,
      statementBorder: getComputedStyle(pane.querySelector("pre.statement")!).borderTopWidth,
    };
  });

  // Each gap is the pane's own 1px border, so the footer spans the pane edge to edge.
  expect(shape).toEqual({ matches: true, left: 1, right: 1, bottom: 1, rule: "1px", statementBorder: "0px" });
});

test("a declaration's admitted proof keeps its warning in the footer", async ({ page }) => {
  await page.goto(MODULE_PAGE);
  await expect(page.locator('.decl[id="open_goal"] .panel > details.proof.warning summary')).toContainText("Not proved");
});

test("a collapsible code body folds, and shows its first line while folded", async ({ page }) => {
  await page.goto(MODULE_PAGE);
  await clientReady(page);

  const body = page.locator('.decl[id="sum_refl"] .panel > details.body');
  const statement = body.locator("pre.statement");
  const peek = body.locator(".peek");

  await expect(body).toHaveJSProperty("open", true);
  await expect(statement).toBeVisible();
  await expect(peek).toBeHidden();

  await body.locator("summary").click();

  await expect(body).toHaveJSProperty("open", false);
  await expect(statement).toBeHidden();
  await expect(peek).toHaveText("Theorem sum_refl (a b : nat) : a + b = a + b.");
  await expect(page.locator('.decl[id="sum_refl"] .panel > details.proof summary')).toBeVisible();
});

test.describe("with JavaScript off", () => {
  test.use({ javaScriptEnabled: false });

  test("a collapsible code body still folds", async ({ page }) => {
    await page.goto(MODULE_PAGE);
    const body = page.locator('.decl[id="sum_refl"] .panel > details.body');
    await body.locator("summary").click();
    await expect(body.locator("pre.statement")).toBeHidden();
  });
});

for (const viewport of [{ width: 1280, height: 720 }, { width: 375, height: 700 }]) {
  test(`a route change paints its final layout at ${viewport.width} px`, async ({ page }) => {
    await page.setViewportSize(viewport);
    await page.addInitScript(() => {
      const shifts: number[] = [];
      (window as any).__shifts = shifts;
      new PerformanceObserver((list) => {
        for (const entry of list.getEntries()) shifts.push((entry as any).value);
      }).observe({ type: "layout-shift", buffered: true });
    });

    await page.goto(OTHER_PAGE);
    await clientReady(page);
    await page.goto(MODULE_PAGE);
    await clientReady(page);
    await page.waitForTimeout(300);

    expect(await page.evaluate(() => (window as any).__shifts)).toEqual([]);
  });
}

test("the inline lists are hidden on wide screens before the client runs", async ({ page }) => {
  await page.route("**/navigator.js", (route) => route.fulfill({ contentType: "text/javascript", body: "" }));
  await page.goto(MODULE_PAGE);

  expect(await rootAttribute(page, "data-lists")).toBe("pane");
  await expect(page.locator(".decl details.refs").first()).toBeHidden();
});

test("the default look's fonts are requested from the document head", async ({ page }) => {
  await page.goto(MODULE_PAGE);
  const fonts = await page.locator('head link[rel="preload"][as="font"]').evaluateAll((links) =>
    links.map((link) => link.getAttribute("href")!.split("/").pop()),
  );

  expect(fonts).toEqual(["Inter-wght.woff2", "JetBrainsMono-Regular.woff2", "JetBrainsMono-SemiBold.woff2"]);
});

test("the Journal look's serif is requested before the stylesheet applies", async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem("rocq-navigator-look", "journal"));
  await page.goto(MODULE_PAGE);

  await expect(page.locator('head link[rel="preload"][href$="SourceSerif4-wght.woff2"]')).toHaveCount(1);
});

