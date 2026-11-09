import type { Page } from "@playwright/test";
import { BASE, clientReady, expect, MODULE_PAGE, OTHER_PAGE, test } from "./support";

const SEARCH_URL = `**${BASE}data/search.json`;

const dialog = (page: Page) => page.getByRole("dialog", { name: "Search declarations" });
const field = (page: Page) => dialog(page).getByRole("combobox");
const options = (page: Page) => dialog(page).getByRole("option");

const entry = (name: string, href: string, summary = "") => ({
  name,
  qualified: `Fixture.Guide.${name}`,
  module: "Fixture.Guide",
  kind: "lemma",
  href,
  summary,
});

const serve = (page: Page, entries: object[]) =>
  page.route(SEARCH_URL, (route) => route.fulfill({ json: entries }));

const openByShortcut = async (page: Page) => {
  await page.goto(MODULE_PAGE);
  await clientReady(page);
  await page.keyboard.press("Control+k");
  await expect(dialog(page)).toBeVisible();
};

test("Ctrl+K and / open search, but / typed in an input does not", async ({ page }) => {
  await page.goto(MODULE_PAGE);
  await clientReady(page);

  await page.keyboard.press("Control+k");
  await expect(dialog(page)).toBeVisible();
  await expect(field(page)).toBeFocused();

  await page.keyboard.press("Escape");
  await expect(dialog(page)).toBeHidden();

  await page.keyboard.press("/");
  await expect(dialog(page)).toBeVisible();
  await expect(field(page)).toHaveValue("");

  await page.keyboard.type("a/b");
  await expect(field(page)).toHaveValue("a/b");
  await page.keyboard.press("Escape");

  await page.evaluate(() => {
    const input = document.createElement("input");
    input.id = "other-input";
    document.body.prepend(input);
  });
  await page.locator("#other-input").focus();
  await page.keyboard.type("x/y");
  await expect(page.locator("#other-input")).toHaveValue("x/y");
  await expect(dialog(page)).toBeHidden();
});

test("typing finds a declaration, and Enter navigates to its anchor", async ({ page }) => {
  const requests: string[] = [];
  page.on("request", (request) => requests.push(new URL(request.url()).pathname));

  await openByShortcut(page);
  await field(page).fill("other_lem");
  await expect(options(page)).toHaveCount(1);
  await expect(options(page).first()).toContainText("other_lemma");
  await expect(options(page).first().getByRole("link")).toHaveAttribute("href", `${BASE}Fixture.Other.html#other_lemma`);
  expect(requests).toContain(`${BASE}data/search.json`);

  await page.keyboard.press("Enter");
  await expect(page).toHaveURL((url) => url.pathname === OTHER_PAGE && url.hash === "#other_lemma");
});

test("arrow keys move the selection, and Esc closes and returns focus", async ({ page }) => {
  await page.goto(MODULE_PAGE);
  await clientReady(page);

  const trigger = page.getByRole("button", { name: /Search/ });
  await trigger.click();
  await expect(dialog(page)).toBeVisible();
  await field(page).fill("other");
  await expect(options(page)).toHaveCount(2);
  await expect(options(page).nth(0)).toHaveAttribute("aria-selected", "true");
  await expect(field(page)).toHaveAttribute("aria-activedescendant", "search-option-0");

  await page.keyboard.press("ArrowDown");
  await expect(options(page).nth(1)).toHaveAttribute("aria-selected", "true");
  await expect(options(page).nth(0)).toHaveAttribute("aria-selected", "false");
  await expect(field(page)).toHaveAttribute("aria-activedescendant", "search-option-1");

  await page.keyboard.press("ArrowDown");
  await expect(options(page).nth(1)).toHaveAttribute("aria-selected", "true");
  await page.keyboard.press("ArrowUp");
  await expect(options(page).nth(0)).toHaveAttribute("aria-selected", "true");

  await expect(dialog(page).getByRole("listbox")).toBeVisible();
  await page.keyboard.press("Escape");
  await expect(dialog(page)).toBeHidden();
  await expect(trigger).toBeFocused();
});

test("when search.json fails to load, the dialog says so", async ({ page, problems }) => {
  await page.route(SEARCH_URL, (route) => route.abort());
  await openByShortcut(page);

  await expect(dialog(page)).toContainText("could not be loaded");
  await field(page).fill("other");
  await expect(options(page)).toHaveCount(0);

  // The browser reports the aborted request itself; the page logs nothing of its own.
  const own = problems.filter((problem) => !problem.includes("Failed to load resource"));
  expect(own).toEqual([]);
  problems.length = 0;
});

test("names and summaries show as text, never as markup", async ({ page }) => {
  await serve(page, [entry("<img src=x onerror=alert(1)>", `${BASE}Fixture.Guide.html#a`, "<b>bold</b> & more")]);
  await openByShortcut(page);
  await field(page).fill("<img");

  await expect(options(page)).toHaveCount(1);
  await expect(options(page).first()).toContainText("<img src=x onerror=alert(1)>");
  await expect(options(page).first()).toContainText("<b>bold</b> & more");
  await expect(dialog(page).locator("img, b")).toHaveCount(0);
});

test("duplicate hrefs in search.json show one row and leave the dialog working", async ({ page }) => {
  const href = `${BASE}Fixture.Guide.html#twin`;
  await serve(page, [entry("twin", href), entry("twin", href, "again"), entry("twin2", `${BASE}Fixture.Guide.html#twin2`)]);
  await openByShortcut(page);
  await field(page).fill("twin");

  await expect(options(page)).toHaveCount(2);
  await field(page).fill("twin2");
  await expect(options(page)).toHaveCount(1);
  await field(page).fill("zzzz");
  await expect(dialog(page)).toContainText("No matches");
});

test("the dialog follows the look and mode tokens", async ({ page }) => {
  await openByShortcut(page);
  await page.keyboard.press("Escape");

  const backgrounds = [];
  for (const [look, mode] of [["family", "light"], ["family", "dark"], ["journal", "light"], ["journal", "dark"]]) {
    await page.evaluate(([l, m]) => {
      document.documentElement.setAttribute("data-look", l);
      document.documentElement.setAttribute("data-mode", m);
    }, [look, mode]);
    await page.keyboard.press("Control+k");
    await expect(dialog(page)).toBeVisible();
    const colors = await dialog(page).evaluate((element) => ({
      dialog: getComputedStyle(element).backgroundColor,
      page: getComputedStyle(document.body).backgroundColor,
    }));
    expect(colors.dialog).toBe(colors.page);
    backgrounds.push(colors.dialog);
    await page.keyboard.press("Escape");
    await expect(dialog(page)).toBeHidden();
  }
  expect(new Set(backgrounds).size).toBeGreaterThan(1);
});
