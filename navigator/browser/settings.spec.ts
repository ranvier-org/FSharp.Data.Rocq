import type { Browser, Page } from "@playwright/test";
import { clientReady, expect, GUARANTEES_PAGE, MODULE_PAGE, observe, OTHER_PAGE, test } from "./support";

const KEYS = {
  look: "rocq-navigator-look",
  mode: "rocq-navigator-mode",
  references: "rocq-navigator-references",
  referencesOpen: "rocq-navigator-references-open",
  definitions: "rocq-navigator-definitions",
  over: "rocq-navigator-definitions-over",
  proofs: "rocq-navigator-proofs",
  imports: "rocq-navigator-imports",
};

const surface = (page: Page) => page.locator("dialog.settings");
const paneTrigger = (page: Page) => page.locator(".pane-left [data-settings-open]");
const drawerTrigger = (page: Page) => page.locator(".drawer [data-settings-open]");

const group = (page: Page, name: string) => surface(page).getByRole("group", { name, exact: true });
const choice = (page: Page, name: string, label: string) => group(page, name).getByRole("button", { name: label, exact: true });
const rootAttribute = (page: Page, name: string) => page.evaluate((attribute) => document.documentElement.getAttribute(attribute), name);
const stored = (page: Page, key: string) => page.evaluate((name) => localStorage.getItem(name), key);

/** Opens the settings from whichever trigger the viewport shows. */
const openSettings = async (page: Page) => {
  if (await paneTrigger(page).isVisible()) {
    await paneTrigger(page).click();
  } else {
    await page.locator(".drawer > summary", { hasText: "Modules" }).click();
    await drawerTrigger(page).click();
  }

  await expect(surface(page)).toBeVisible();
};

const save = (page: Page, values: Record<string, string>) =>
  page.addInitScript((entries) => {
    for (const [key, value] of Object.entries(entries)) localStorage.setItem(key, value);
  }, values);

const box = (page: Page) => surface(page).evaluate((element) => {
  const rect = element.getBoundingClientRect();
  return {
    left: rect.left,
    right: rect.right,
    top: rect.top,
    bottom: rect.bottom,
    modal: element.matches(":modal"),
    surface: element.getAttribute("data-surface"),
    backdrop: getComputedStyle(element, "::backdrop").backgroundColor,
    width: document.documentElement.clientWidth,
    height: document.documentElement.clientHeight,
  };
});

const touchPage = async (browser: Browser, problems: string[], viewport: { width: number; height: number }) => {
  const context = await browser.newContext({ viewport, hasTouch: true, isMobile: true });
  const page = await context.newPage();
  observe(page, problems);
  return { context, page };
};

test.describe("the surface follows the viewport", () => {
  test("a fine pointer above 900 px gets a popover rising from the footer", async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto(MODULE_PAGE);
    await clientReady(page);
    await openSettings(page);

    const placed = await box(page);
    expect(placed.surface).toBe("popover");
    expect(placed.modal).toBe(false);

    const footer = (await page.locator(".pane-foot").boundingBox())!;
    expect(placed.bottom).toBeLessThanOrEqual(footer.y + 1);
    expect(placed.left).toBeLessThan(footer.x + footer.width);
    expect(placed.top).toBeGreaterThanOrEqual(0);
  });

  test("a click outside the popover closes it and leaves the page working", async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto(MODULE_PAGE);
    await clientReady(page);
    await openSettings(page);

    await page.locator(".topbar .crumb").click();
    await expect(surface(page)).toBeHidden();
  });

  test("Esc closes the popover and focus returns to the trigger", async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto(MODULE_PAGE);
    await clientReady(page);
    await openSettings(page);

    expect(await surface(page).evaluate((element) => element.contains(document.activeElement))).toBe(true);
    await page.keyboard.press("Escape");
    await expect(surface(page)).toBeHidden();
    await expect(paneTrigger(page)).toBeFocused();
  });

  test("the trigger toggles the popover", async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto(MODULE_PAGE);
    await clientReady(page);
    await openSettings(page);
    await paneTrigger(page).click();
    await expect(surface(page)).toBeHidden();
  });

  test("a fine pointer from 601 to 900 px gets a centred dialog, from the drawer's button", async ({ page }) => {
    await page.setViewportSize({ width: 800, height: 700 });
    await page.goto(MODULE_PAGE);
    await clientReady(page);
    await openSettings(page);

    const placed = await box(page);
    expect(placed.surface).toBe("dialog");
    expect(placed.modal).toBe(true);
    expect(Math.abs(placed.left - (placed.width - placed.right))).toBeLessThanOrEqual(1);

    await page.keyboard.press("Escape");
    await expect(surface(page)).toBeHidden();
    await expect(drawerTrigger(page)).toBeFocused();
  });

  test("at most 600 px gets a centred dialog with a dimmed backdrop", async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 700 });
    await page.goto(MODULE_PAGE);
    await clientReady(page);
    await openSettings(page);

    const placed = await box(page);
    expect(placed.surface).toBe("dialog");
    expect(placed.modal).toBe(true);
    expect(placed.backdrop).toBe("rgba(0, 0, 0, 0.4)");
    expect(placed.left).toBeGreaterThanOrEqual(0);
    expect(placed.right).toBeLessThanOrEqual(placed.width);
    expect(await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)).toBe(0);
  });

  for (const viewport of [{ width: 820, height: 1180 }, { width: 1280, height: 800 }]) {
    test(`a coarse pointer at ${viewport.width} px gets a side panel, and a tap outside closes it`, async ({ browser, problems }) => {
      const { context, page } = await touchPage(browser, problems, viewport);
      await page.goto(MODULE_PAGE);
      await clientReady(page);
      expect(await page.evaluate(() => matchMedia("(pointer: coarse)").matches)).toBe(true);
      await openSettings(page);

      const placed = await box(page);
      expect(placed.surface).toBe("panel");
      expect(placed.modal).toBe(true);
      expect(placed.backdrop).toBe("rgba(0, 0, 0, 0.15)");
      expect(placed.top).toBe(0);
      expect(Math.round(placed.bottom)).toBe(placed.height);
      expect(Math.round(placed.right)).toBe(placed.width);

      // A tap on the panel's blank space, below its last control, leaves it open.
      const last = (await surface(page).locator(".settings-note").boundingBox())!;
      expect(last.y + last.height + 20, "the panel has blank space").toBeLessThan(placed.bottom);
      await page.touchscreen.tap(placed.right - 20, placed.bottom - 10);
      await page.waitForTimeout(100);
      await expect(surface(page)).toBeVisible();

      await page.touchscreen.tap(10, 300);
      await expect(surface(page)).toBeHidden();
      await context.close();
    });
  }

  test("a coarse pointer at most 600 px gets the dialog", async ({ browser, problems }) => {
    const { context, page } = await touchPage(browser, problems, { width: 500, height: 800 });
    await page.goto(MODULE_PAGE);
    await clientReady(page);
    await openSettings(page);
    expect((await box(page)).surface).toBe("dialog");
    await context.close();
  });

  test("a resize across a boundary re-picks the surface", async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto(MODULE_PAGE);
    await clientReady(page);
    await openSettings(page);
    expect((await box(page)).surface).toBe("popover");

    await page.setViewportSize({ width: 800, height: 700 });

    await expect.poll(async () => (await box(page)).surface).toBe("dialog");
    expect((await box(page)).modal).toBe(true);
    await expect(surface(page)).toBeVisible();

    await page.setViewportSize({ width: 1440, height: 900 });
    await expect.poll(async () => (await box(page)).surface).toBe("popover");
    expect((await box(page)).modal).toBe(false);
  });

  test("Esc after a resize hides the opener returns focus to the visible trigger", async ({ page }) => {
    await page.setViewportSize({ width: 800, height: 700 });
    await page.goto(MODULE_PAGE);
    await clientReady(page);
    await openSettings(page);
    await expect(drawerTrigger(page)).toBeVisible();

    await page.setViewportSize({ width: 1440, height: 900 });
    await expect.poll(async () => (await box(page)).surface).toBe("popover");
    await page.keyboard.press("Escape");
    await expect(surface(page)).toBeHidden();
    await expect(paneTrigger(page)).toBeFocused();
  });

  test("Esc after a resize into a closed drawer returns focus to the drawer's summary", async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto(MODULE_PAGE);
    await clientReady(page);
    await openSettings(page);

    await page.setViewportSize({ width: 800, height: 700 });
    await expect.poll(async () => (await box(page)).surface).toBe("dialog");
    await page.keyboard.press("Escape");
    await expect(surface(page)).toBeHidden();
    await expect(page.locator(".drawer > summary", { hasText: "Modules" })).toBeFocused();
  });
});

test.describe("triggers", () => {
  test("appear with a client at 24 px or taller", async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto(MODULE_PAGE);
    await clientReady(page);
    await expect(paneTrigger(page)).toBeVisible();
    expect((await paneTrigger(page).boundingBox())!.height).toBeGreaterThanOrEqual(24);
  });

  test("the footer sits at the bottom of the left pane", async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 500 });
    await page.goto(MODULE_PAGE);
    await clientReady(page);

    const footer = (await page.locator(".pane-foot").boundingBox())!;
    expect(Math.round(footer.y + footer.height)).toBe(500);
  });

  test("a failed client hides them and they keep their space", async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.route("**/navigator.js", (route) => route.fulfill({ contentType: "text/javascript", body: "" }));
    await page.goto(MODULE_PAGE);
    await page.waitForLoadState("load");

    await expect(paneTrigger(page)).toBeHidden();
    expect((await paneTrigger(page).boundingBox())!.height).toBeGreaterThan(0);
  });

  test("the top bar has no switch", async ({ page }) => {
    await page.goto(MODULE_PAGE);
    await clientReady(page);
    await expect(page.locator(".topbar [aria-pressed]")).toHaveCount(0);
    await expect(page.locator('.topbar [data-island="settings"]')).toHaveCount(0);
  });
});

test.describe("controls", () => {
  test.beforeEach(async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 900 });
  });

  test("each control is a labelled group, with buttons at 24 px or taller", async ({ page }) => {
    await page.goto(MODULE_PAGE);
    await clientReady(page);
    await openSettings(page);

    for (const name of ["Look", "Mode", "Definitions", "Proofs", "Imports", "References"]) {
      await expect(group(page, name)).toBeVisible();
    }

    for (const button of await surface(page).getByRole("button").all()) {
      expect((await button.boundingBox())!.height).toBeGreaterThanOrEqual(24);
    }
  });

  test("proofs apply live and survive a reload", async ({ page }) => {
    await page.goto(MODULE_PAGE);
    await clientReady(page);
    const proof = page.locator("article details.proof").first();

    await openSettings(page);
    await expect(choice(page, "Proofs", "Closed")).toHaveAttribute("aria-pressed", "true");
    await choice(page, "Proofs", "Open").click();
    await expect(proof).toHaveJSProperty("open", true);
    expect(await stored(page, KEYS.proofs)).toBe("open");

    await page.reload();
    await clientReady(page);
    await expect(proof).toHaveJSProperty("open", true);
    await openSettings(page);
    await expect(choice(page, "Proofs", "Open")).toHaveAttribute("aria-pressed", "true");

    await choice(page, "Proofs", "Closed").click();
    await expect(proof).toHaveJSProperty("open", false);
  });

  test("the pressed choice still writes: a proof opened by hand closes on Closed", async ({ page }) => {
    await page.goto(MODULE_PAGE);
    await clientReady(page);
    const proof = page.locator("article details.proof").first();
    await proof.evaluate((fold) => ((fold as HTMLDetailsElement).open = true));

    await openSettings(page);
    await expect(choice(page, "Proofs", "Closed")).toHaveAttribute("aria-pressed", "true");
    await choice(page, "Proofs", "Closed").click();
    await expect(proof).toHaveJSProperty("open", false);
  });

  test("a fold the reader or a jump opened stays open while the page starts", async ({ page }) => {
    await save(page, { [KEYS.proofs]: "closed" });
    let release = () => {};
    const held = new Promise<void>((resolve) => (release = resolve));
    await page.route("**/navigator.js", async (route) => {
      await held;
      await route.continue();
    });
    await page.goto(MODULE_PAGE, { waitUntil: "commit" });
    // The page's inline script writes the saved fold states while the document parses, and the held module runs after parsing.
    await page.waitForFunction(() => document.readyState !== "loading");
    await page.locator("article details.proof").first().evaluate((fold) => ((fold as HTMLDetailsElement).open = true));
    await expect(page.locator("html")).not.toHaveAttribute("data-client", "ready");
    release();
    await clientReady(page);
    await page.waitForTimeout(100);
    await expect(page.locator("article details.proof").first()).toHaveJSProperty("open", true);
  });

  test("imports apply live and survive a reload", async ({ page }) => {
    await page.goto(MODULE_PAGE);
    await clientReady(page);
    const imports = page.locator("article details.imports");

    await openSettings(page);
    await choice(page, "Imports", "Open").click();
    await expect(imports).toHaveJSProperty("open", true);

    await page.reload();
    await clientReady(page);
    await expect(imports).toHaveJSProperty("open", true);
  });

  test("definitions open, close and close long ones, live and across a reload", async ({ page }) => {
    await page.goto(MODULE_PAGE);
    await clientReady(page);

    const states = () =>
      page.locator("article details.body").evaluateAll((folds) =>
        folds.map((fold) => ({
          open: (fold as HTMLDetailsElement).open,
          lines: parseInt(fold.querySelector("summary")!.getAttribute("data-lines")!, 10),
        })),
      );

    await openSettings(page);
    const lines = surface(page).locator("input");
    await expect(lines).toBeHidden();
    for (const label of ["Open", "Closed", "Close long ones"]) {
      await expect(choice(page, "Definitions", label)).toHaveAttribute("aria-pressed", "false");
    }

    await choice(page, "Definitions", "Closed").click();
    await expect.poll(async () => (await states()).every((fold) => !fold.open)).toBe(true);
    await expect(lines).toBeHidden();

    await choice(page, "Definitions", "Open").click();
    await expect.poll(async () => (await states()).every((fold) => fold.open)).toBe(true);

    await choice(page, "Definitions", "Close long ones").click();
    await expect(lines).toBeVisible();
    await lines.fill("2");

    const expectLimit = async (limit: number) => {
      const found = await states();
      expect(found.length).toBeGreaterThan(0);
      expect(found.every((fold) => fold.open === fold.lines <= limit)).toBe(true);
    };

    await expect.poll(() => stored(page, KEYS.over)).toBe("2");
    await expectLimit(2);
    await lines.fill("1");
    await expectLimit(1);

    await page.reload();
    await clientReady(page);
    await expectLimit(1);
    await openSettings(page);
    await expect(lines).toHaveValue("1");
    await expect(lines).toBeVisible();
  });

  test("a line count being typed leaves the last valid value", async ({ page }) => {
    await page.goto(MODULE_PAGE);
    await clientReady(page);
    await openSettings(page);
    await choice(page, "Definitions", "Close long ones").click();

    const lines = surface(page).locator("input");
    await lines.fill("3");
    await lines.fill("");
    expect(await stored(page, KEYS.over)).toBe("3");
  });

  test("the references placement applies live and survives a reload", async ({ page }) => {
    await page.goto(MODULE_PAGE);
    await clientReady(page);
    await openSettings(page);

    await expect(choice(page, "References", "Pinned")).toHaveAttribute("aria-pressed", "true");
    await expect(page.locator(".pane-right .pinned")).toBeVisible();
    await expect(group(page, "References open")).toBeHidden();

    await choice(page, "References", "Badges").click();
    expect(await rootAttribute(page, "data-lists")).toBe("badges");
    await expect(page.locator(".pane-right .pinned")).toBeHidden();
    await expect(page.locator(".refs-chip").first()).toBeVisible();
    await expect(page.locator("details.refs").first()).toBeHidden();
    await expect(group(page, "References open")).toBeHidden();

    await page.reload();
    await clientReady(page);
    expect(await rootAttribute(page, "data-lists")).toBe("badges");

    await openSettings(page);
    await choice(page, "References", "Under definitions").click();
    expect(await rootAttribute(page, "data-lists")).toBeNull();
    await expect(page.locator("details.refs").first()).toBeVisible();
    await expect(page.locator(".refs-chip").first()).toBeHidden();
    await expect(group(page, "References open")).toBeVisible();

    await choice(page, "References open", "Open").click();
    await expect(page.locator("details.refs").first()).toHaveJSProperty("open", true);

    await page.reload();
    await clientReady(page);
    expect(await rootAttribute(page, "data-lists")).toBeNull();
    await expect(page.locator("details.refs").first()).toHaveJSProperty("open", true);
  });

  test("below 900 px the references row is hidden and the folds stay inline", async ({ page }) => {
    await save(page, { [KEYS.references]: "badges" });
    await page.setViewportSize({ width: 800, height: 700 });
    await page.goto(MODULE_PAGE);
    await clientReady(page);
    await openSettings(page);

    await expect(group(page, "References")).toBeHidden();
    await expect(page.locator("details.refs").first()).toBeVisible();
    await expect(page.locator(".refs-chip").first()).toBeHidden();
  });

  test("below 900 px the references open row shows and applies, whatever placement is saved", async ({ page }) => {
    await save(page, { [KEYS.references]: "pane" });
    await page.setViewportSize({ width: 800, height: 700 });
    await page.goto(MODULE_PAGE);
    await clientReady(page);
    await openSettings(page);

    await expect(group(page, "References open")).toBeVisible();
    await choice(page, "References open", "Open").click();
    await expect(page.locator("details.refs").first()).toHaveJSProperty("open", true);
  });

  test("the look and mode keep their keys, and all four pairs open without error", async ({ page }) => {
    await page.goto(MODULE_PAGE);
    await clientReady(page);
    await openSettings(page);

    for (const [look, lookLabel] of [["family", "Family"], ["journal", "Journal"]]) {
      for (const [mode, modeLabel] of [["light", "Light"], ["dark", "Dark"]]) {
        await choice(page, "Look", lookLabel).click();
        await choice(page, "Mode", modeLabel).click();
        expect(await rootAttribute(page, "data-look")).toBe(look);
        expect(await rootAttribute(page, "data-mode")).toBe(mode);
        expect(await stored(page, KEYS.look)).toBe(look);
        expect(await stored(page, KEYS.mode)).toBe(mode);
        await expect(surface(page)).toBeVisible();
      }
    }
  });

  test("settings persist across pages", async ({ page }) => {
    await save(page, { [KEYS.proofs]: "open" });
    for (const next of [OTHER_PAGE, MODULE_PAGE]) {
      await page.goto(next);
      await clientReady(page);
      const proofs = page.locator("article details.proof");
      for (const proof of await proofs.all()) await expect(proof).toHaveJSProperty("open", true);
    }
    await page.goto(GUARANTEES_PAGE);
    await clientReady(page);
    await expect(paneTrigger(page)).toBeVisible();
  });
});

test.describe("Reset", () => {
  test("clears every saved key and restores the server's state with no reload", async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 900 });
    await save(page, {
      [KEYS.look]: "journal",
      [KEYS.mode]: "dark",
      [KEYS.references]: "under",
      [KEYS.referencesOpen]: "open",
      [KEYS.definitions]: "long",
      [KEYS.over]: "2",
      [KEYS.proofs]: "open",
      [KEYS.imports]: "open",
    });
    await page.goto(MODULE_PAGE);
    await clientReady(page);
    await page.evaluate(() => ((window as any).__sameDocument = true));

    await openSettings(page);
    const lines = surface(page).locator("input");
    await lines.fill("3");

    await surface(page).getByRole("button", { name: "Reset to the site's defaults" }).click();

    expect(await page.evaluate(() => (window as any).__sameDocument)).toBe(true);

    for (const key of Object.values(KEYS)) expect(await stored(page, key)).toBeNull();

    expect(await rootAttribute(page, "data-look")).toBe("family");
    expect(await rootAttribute(page, "data-mode")).toBe("system");
    expect(await rootAttribute(page, "data-lists")).toBe("pane");

    const open = (selector: string) =>
      page.locator(selector).evaluateAll((folds) => folds.filter((fold) => (fold as HTMLDetailsElement).open).length);
    expect(await open("article details.proof")).toBe(0);
    expect(await open("article details.imports")).toBe(0);
    expect(await open("article details.refs")).toBe(0);
    const bodies = await page.locator("article details.body").count();
    expect(bodies).toBeGreaterThan(0);
    expect(await open("article details.body")).toBe(bodies);

    await expect(choice(page, "Look", "Family")).toHaveAttribute("aria-pressed", "true");
    await expect(choice(page, "References", "Pinned")).toHaveAttribute("aria-pressed", "true");
    await expect(lines).toBeHidden();
    await expect(lines).toHaveValue("40");
    await expect(surface(page)).toBeVisible();
  });

  test("a fold the reader opened by hand closes too", async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto(MODULE_PAGE);
    await clientReady(page);

    const proof = page.locator("article details.proof").first();
    await proof.evaluate((fold) => ((fold as HTMLDetailsElement).open = true));

    await openSettings(page);
    await surface(page).getByRole("button", { name: "Reset to the site's defaults" }).click();
    await expect(proof).toHaveJSProperty("open", false);
  });

  test("restores a fold the site's limit closes", async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 900 });
    await save(page, { [KEYS.definitions]: "open" });
    await page.goto(MODULE_PAGE);
    await clientReady(page);
    await page.evaluate(() => document.body.setAttribute("data-collapse-over", "1"));

    const fold = page.locator("article details.body").first();
    await expect(fold).toHaveJSProperty("open", true);
    await openSettings(page);
    await surface(page).getByRole("button", { name: "Reset to the site's defaults" }).click();
    await expect(fold).toHaveJSProperty("open", false);
  });
});

test.describe("storage", () => {
  const throwingStorage = () =>
    Object.defineProperty(window, "localStorage", {
      get() {
        throw new DOMException("denied", "SecurityError");
      },
    });

  test("blocked storage falls back to defaults, the controls still apply, and nothing is logged", async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.addInitScript(throwingStorage);
    await page.goto(MODULE_PAGE);
    await clientReady(page);

    expect(await rootAttribute(page, "data-lists")).toBe("pane");
    await openSettings(page);
    await expect(choice(page, "Proofs", "Closed")).toHaveAttribute("aria-pressed", "true");

    await choice(page, "Proofs", "Open").click();
    await expect(page.locator("article details.proof").first()).toHaveJSProperty("open", true);
    await choice(page, "References", "Badges").click();
    expect(await rootAttribute(page, "data-lists")).toBe("badges");
    await surface(page).getByRole("button", { name: "Reset to the site's defaults" }).click();
    expect(await rootAttribute(page, "data-lists")).toBe("pane");
  });

  test("bad stored values fall back to defaults", async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 900 });
    await save(page, {
      [KEYS.references]: "<img>",
      [KEYS.referencesOpen]: "maybe",
      [KEYS.definitions]: "sideways",
      [KEYS.over]: "abc",
      [KEYS.proofs]: "1",
      [KEYS.imports]: "",
    });
    await page.goto(MODULE_PAGE);
    await clientReady(page);

    expect(await rootAttribute(page, "data-lists")).toBe("pane");
    await expect(page.locator("article details.proof").first()).toHaveJSProperty("open", false);
    const bodies = page.locator("article details.body");
    expect(await bodies.evaluateAll((folds) => folds.every((fold) => (fold as HTMLDetailsElement).open))).toBe(true);

    await openSettings(page);
    await expect(choice(page, "Proofs", "Closed")).toHaveAttribute("aria-pressed", "true");
    await expect(choice(page, "References", "Pinned")).toHaveAttribute("aria-pressed", "true");
    await choice(page, "Definitions", "Close long ones").click();
    await expect(surface(page).locator("input")).toHaveValue("40");
  });

  test("a bad line count with Close long ones saved uses the default limit", async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 900 });
    await save(page, { [KEYS.definitions]: "long", [KEYS.over]: "-1" });
    await page.goto(MODULE_PAGE);
    await clientReady(page);
    const bodies = page.locator("article details.body");
    expect(await bodies.evaluateAll((folds) => folds.every((fold) => (fold as HTMLDetailsElement).open))).toBe(true);
  });
});

test.describe("before first paint", () => {
  const firstPaintSettings = { [KEYS.definitions]: "closed", [KEYS.proofs]: "open", [KEYS.imports]: "open" };

  test("fold states apply with the client held back", async ({ page }) => {
    await save(page, firstPaintSettings);
    await page.route("**/navigator.js", (route) => route.fulfill({ contentType: "text/javascript", body: "" }));
    await page.goto(MODULE_PAGE);

    expect(await page.locator("article details.body").evaluateAll((folds) => folds.every((fold) => !(fold as HTMLDetailsElement).open))).toBe(true);
    await expect(page.locator("article details.proof").first()).toHaveJSProperty("open", true);
    await expect(page.locator("article details.imports")).toHaveJSProperty("open", true);
  });

  test("the placement is on the root before the stylesheet applies", async ({ page }) => {
    await save(page, { [KEYS.references]: "badges" });
    let release = () => {};
    const held = new Promise<void>((resolve) => (release = resolve));
    await page.route("**/navigator.js", async (route) => {
      await held;
      await route.fulfill({ contentType: "text/javascript", body: "" });
    });

    await page.goto(MODULE_PAGE, { waitUntil: "commit" });
    await page.locator("article").waitFor({ state: "attached" });
    expect(await rootAttribute(page, "data-lists")).toBe("badges");
    release();
  });

  test("a reload with definitions saved closed moves no fold after first paint", async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.addInitScript(() => {
      const shifts: number[] = [];
      (window as any).__shifts = shifts;
      new PerformanceObserver((list) => {
        for (const entry of list.getEntries()) shifts.push((entry as any).value);
      }).observe({ type: "layout-shift", buffered: true });
    });
    await save(page, { [KEYS.definitions]: "closed" });

    await page.goto(MODULE_PAGE);
    await clientReady(page);
    await page.reload();
    await clientReady(page);
    await page.waitForTimeout(300);

    const folds = page.locator("article details.body");
    expect(await folds.count()).toBeGreaterThan(0);
    expect(await folds.evaluateAll((all) => all.every((fold) => !(fold as HTMLDetailsElement).open))).toBe(true);
    expect(await page.evaluate(() => (window as any).__shifts)).toEqual([]);
  });

  for (const placement of ["pane", "badges", "under"]) {
    test(`a client that fails with ${placement} saved restores the inline lists`, async ({ page }) => {
      await page.setViewportSize({ width: 1440, height: 900 });
      await save(page, { [KEYS.references]: placement });
      await page.route("**/navigator.js", (route) => route.fulfill({ contentType: "text/javascript", body: "" }));
      await page.goto(MODULE_PAGE);
      await page.waitForLoadState("load");

      expect(await rootAttribute(page, "data-lists")).toBeNull();
      await expect(page.locator(".decl details.refs").first()).toBeVisible();
      await expect(page.locator(".refs-chip").first()).toBeHidden();
      await expect(page.locator(".pane-right .pinned")).toBeHidden();
    });
  }
});

test.describe("fold targets", () => {
  const closed = { [KEYS.definitions]: "closed" };

  test("a hash on load opens the target's closed fold", async ({ page }) => {
    await save(page, closed);
    await page.goto(MODULE_PAGE);
    await clientReady(page);
    const target = await page.locator(".decl:has(> .panel > details.body)[id]").first().getAttribute("id");
    expect(target).toBeTruthy();

    await page.goto(`${MODULE_PAGE}#${target}`);
    await clientReady(page);
    await expect(page.locator(`[id="${target}"] > .panel > details.body`)).toHaveJSProperty("open", true);

    const others = await page
      .locator(`.decl:not([id="${target}"]) > .panel > details.body`)
      .evaluateAll((folds) => folds.filter((fold) => (fold as HTMLDetailsElement).open).length);
    expect(others).toBe(0);
  });

  test("an in-page link opens its target's closed fold", async ({ page }) => {
    await save(page, closed);
    await page.goto(MODULE_PAGE);
    await clientReady(page);
    const target = await page.locator(".decl:has(> .panel > details.body)[id]").first().getAttribute("id");
    const fold = page.locator(`[id="${target}"] > .panel > details.body`);
    await expect(fold).toHaveJSProperty("open", false);

    await page.evaluate((id) => {
      const link = document.createElement("a");
      link.href = `#${id}`;
      link.textContent = "to the definition";
      document.querySelector("article")!.prepend(link);
      link.click();
    }, target);
    await expect(fold).toHaveJSProperty("open", true);
  });

  test("a modified click on a link to the current hash leaves the fold closed", async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 900 });
    await save(page, closed);
    await page.goto(MODULE_PAGE);
    await clientReady(page);
    const target = await page.locator(".decl:has(> .panel > details.body)[id]").first().getAttribute("id");
    const fold = page.locator(`[id="${target}"] > .panel > details.body`);
    await page.evaluate((id) => {
      const anchor = document.createElement("a");
      anchor.className = "to-target";
      anchor.href = `#${id}`;
      anchor.textContent = "to the definition";
      anchor.style.cssText = "position: fixed; top: 0; left: 300px; z-index: 99";
      document.body.append(anchor);
    }, target);
    const link = page.locator("a.to-target");

    await link.click();
    await expect(fold).toHaveJSProperty("open", true);
    await fold.evaluate((element) => ((element as HTMLDetailsElement).open = false));
    await link.click({ modifiers: ["ControlOrMeta"] });
    await page.waitForTimeout(100);
    await expect(fold).toHaveJSProperty("open", false);
  });

  test("a second click on a link to the current hash opens the fold again", async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 900 });
    await save(page, closed);
    await page.goto(MODULE_PAGE);
    await clientReady(page);
    const target = await page.locator(".decl:has(> .panel > details.body)[id]").first().getAttribute("id");
    const fold = page.locator(`[id="${target}"] > .panel > details.body`);
    await page.evaluate((id) => {
      const anchor = document.createElement("a");
      anchor.className = "to-target";
      anchor.href = `#${id}`;
      anchor.textContent = "to the definition";
      anchor.style.cssText = "position: fixed; top: 0; left: 300px; z-index: 99";
      document.body.append(anchor);
    }, target);
    const link = page.locator("a.to-target");

    await link.click();
    await expect(fold).toHaveJSProperty("open", true);

    await fold.evaluate((element) => ((element as HTMLDetailsElement).open = false));
    await link.click();
    await expect(fold).toHaveJSProperty("open", true);
  });
});
