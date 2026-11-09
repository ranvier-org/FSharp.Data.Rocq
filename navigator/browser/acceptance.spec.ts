import { readdirSync, statSync } from "node:fs";
import { join, resolve } from "node:path";
import type { Page } from "@playwright/test";
import { BASE, clientReady, expect, test } from "./support";

const LOOKS = [
  ["family", "Family"],
  ["journal", "Journal"],
] as const;

const MODES = [
  ["system", "Auto"],
  ["light", "Light"],
  ["dark", "Dark"],
] as const;

type SearchEntry = { name: string; href: string };

const site = resolve(process.env.NAVIGATOR_SITE ?? "../Rocq.Navigator.Site/output");
const pages = readdirSync(site).filter((file) => file.endsWith(".html"));
const modulePages = pages.filter((file) => file !== "index.html");

/** The largest module page in the site: the fixture's Guide, or a real project's longest module. */
const LARGEST = modulePages
  .map((file) => ({ file, size: statSync(join(site, file)).size }))
  .sort((a, b) => b.size - a.size)[0].file;

const OTHER = modulePages.find((file) => file !== LARGEST)!;

const url = (file: string) => `${BASE}${file}`;

const remember = (page: Page, look: string, mode: string) =>
  page.addInitScript(
    ([look, mode]) => {
      localStorage.setItem("rocq-navigator-look", look);
      localStorage.setItem("rocq-navigator-mode", mode);
    },
    [look, mode],
  );

const openLargest = async (page: Page) => {
  await page.goto(url(LARGEST));
  await clientReady(page);
};

const card = (page: Page) => page.locator('[data-island="cards"] .card');
const firstReference = (page: Page) => page.locator("article pre.statement a[data-ref]").first();

const expectReadable = async (page: Page) => {
  const article = page.locator("article");
  await expect(article).toBeVisible();
  await expect(article.locator("h1.page-title")).not.toBeEmpty();
  const statement = article.locator("pre.statement").first();
  await expect(statement).toBeVisible();
  expect((await statement.innerText()).trim()).not.toBe("");
  await expect(article.locator("details.refs").first(), "the references are visible").toBeVisible();
};

const expectFoldOpens = async (page: Page) => {
  const fold = page.locator("article details.proof").first();
  await fold.locator("summary").click();
  await expect(fold.locator("pre")).toBeVisible();
};

/** Follows the first reference link in a statement and checks the browser shows its target. */
const expectLinkFollows = async (page: Page) => {
  const link = page.locator('article pre.statement a[href*="#"]').first();
  const href = decodeURIComponent((await link.getAttribute("href"))!);
  await link.click();
  await page.waitForURL((address) => decodeURIComponent(address.href).endsWith(href));
  const anchor = decodeURIComponent(new URL(page.url()).hash.slice(1));
  await expect(page.locator(`[id="${anchor}"]`)).toBeVisible();
};

const expectNoSideways = async (page: Page, label: string) => {
  const { scroll, client } = await page.evaluate(() => ({
    scroll: document.documentElement.scrollWidth,
    client: document.documentElement.clientWidth,
  }));

  expect(scroll, `${label}: ${scroll} wide in ${client}`).toBeLessThanOrEqual(client);
};

const expectEveryPageFits = async (page: Page) => {
  await page.setViewportSize({ width: 375, height: 700 });

  for (const file of pages) {
    await page.goto(url(file));
    await expectNoSideways(page, `${file}, closed`);

    await page.evaluate(() => {
      for (const details of document.querySelectorAll("details")) details.open = true;
    });

    await expectNoSideways(page, `${file}, every fold open`);
  }
};

for (const [look, lookLabel] of LOOKS) {
  for (const [mode, modeLabel] of MODES) {
    test.describe(`${look}, mode ${mode}`, () => {
      test("a card opens by hover and by keyboard", async ({ page }) => {
        await remember(page, look, mode);
        await openLargest(page);

        await firstReference(page).hover();
        await expect(card(page)).toBeVisible();
        await expect(card(page).locator(".card-name")).not.toBeEmpty();

        await page.locator(".topbar").hover();
        await expect(card(page)).toBeHidden();

        await firstReference(page).focus();
        await expect(card(page)).toBeVisible();
        await page.keyboard.press("Escape");
        await expect(card(page)).toBeHidden();
      });

      test("search finds a declaration and navigates to it", async ({ page }) => {
        await remember(page, look, mode);
        await openLargest(page);

        const entries: SearchEntry[] = await (await page.request.get(`${BASE}data/search.json`)).json();
        const uses = (name: string) => entries.filter((entry) => entry.name === name).length;
        const target = entries.find(
          (entry) => /^[a-z_]{6,}$/.test(entry.name) && !entry.href.includes(LARGEST) && uses(entry.name) === 1,
        )!;

        await page.keyboard.press("Control+k");
        await page.getByRole("dialog", { name: "Search declarations" }).getByRole("combobox").fill(target.name);
        await expect(page.getByRole("option").first()).toContainText(target.name);
        await page.keyboard.press("Enter");
        await page.waitForURL((address) => address.href.endsWith(target.href));
        await expect(page.locator(`[id="${target.href.split("#")[1]}"]`)).toBeVisible();
      });

      test("a fold opens", async ({ page }) => {
        await remember(page, look, mode);
        await openLargest(page);
        await expectFoldOpens(page);
      });

      test("the switch persists across every kind of page", async ({ page }) => {
        await page.goto(url(OTHER));
        await clientReady(page);
        const island = page.locator('[data-island="settings"]');
        await page.locator(".pane-left [data-settings-open]").click();
        await island.getByRole("button", { name: lookLabel, exact: true }).click();
        await island.getByRole("button", { name: modeLabel, exact: true }).click();

        for (const file of [LARGEST, "index.html", OTHER]) {
          await page.goto(url(file));
          await clientReady(page);
          const root = await page.evaluate(() => ({ ...document.documentElement.dataset }));
          expect(root.look).toBe(look);
          expect(root.mode).toBe(mode);
          await page.locator(".pane-left [data-settings-open]").click();
          await expect(page.locator('[data-island="settings"]').getByRole("button", { name: lookLabel, exact: true }))
            .toHaveAttribute("aria-pressed", "true");
        }
      });

      test("375 px: no horizontal page scroll on every page", async ({ page }) => {
        test.setTimeout(180_000);
        await remember(page, look, mode);
        await expectEveryPageFits(page);
      });

      test("under the subpath base URL, CSS, JS, fonts and data all load", async ({ page }) => {
        const loaded: string[] = [];

        page.on("response", (response) => {
          if (response.ok()) loaded.push(new URL(response.url()).pathname);
        });

        await remember(page, look, mode);
        await openLargest(page);
        await page.evaluate(() => document.fonts.ready);
        await firstReference(page).hover();
        await expect(card(page)).toBeVisible();
        await page.keyboard.press("Control+k");
        await expect.poll(() => loaded.some((path) => path.endsWith("/data/search.json"))).toBe(true);

        expect(loaded.filter((path) => !path.startsWith(BASE))).toEqual([]);

        for (const ending of ["/navigator.css", "/navigator.js", ".woff2", ".symbols.json"]) {
          expect(loaded.some((path) => path.endsWith(ending)), ending).toBe(true);
        }
      });

      test("a failing bundle leaves a fully readable page", async ({ page, problems }) => {
        await remember(page, look, mode);
        await page.route("**/navigator.js", (route) => route.abort());
        await page.goto(url(LARGEST));

        await expectReadable(page);
        await expectFoldOpens(page);
        await expectLinkFollows(page);
        await page.waitForLoadState("load");
        expect(await page.evaluate(() => document.documentElement.dataset.client)).toBeUndefined();

        // Chromium reports each aborted request as a console error, and a page cannot silence it.
        const report = /^error: Failed to load resource: net::ERR_FAILED$/;
        await expect.poll(() => problems.filter((problem) => report.test(problem)).length).toBeGreaterThan(0);
        const others = problems.filter((problem) => !report.test(problem));
        problems.length = 0;
        problems.push(...others);
      });
    });
  }
}

for (const colorScheme of ["light", "dark"] as const) {
  test.describe(`JavaScript off, ${colorScheme}`, () => {
    test.use({ javaScriptEnabled: false, colorScheme });

    test("JavaScript off: a module page reads, folds and links", async ({ page }) => {
      await page.goto(url(LARGEST));
      await expectReadable(page);
      await expectFoldOpens(page);
      await expectLinkFollows(page);
    });

    test("JavaScript off: 375 px has no horizontal page scroll on every page", async ({ page }) => {
      test.setTimeout(180_000);
      await expectEveryPageFits(page);
    });
  });
}

for (const [look] of LOOKS) {
  test(`375 px in ${look}: a long code name or word in comment prose wraps`, async ({ page }) => {
    await remember(page, look, "system");
    await page.setViewportSize({ width: 375, height: 700 });
    await page.goto(url(LARGEST));

    await page.evaluate(() => {
      const long = "a_declaration_name_too_long_to_fit_on_one_line_of_a_phone";
      const prose = document.createElement("div");
      prose.className = "doc";
      prose.innerHTML = `<p><a class="ref" href="#"><code>${long}</code></a></p><p>${long}</p>`;
      document.querySelector("article")!.append(prose);
    });

    await expectNoSideways(page, look);
  });
}

test("without hydration markers, as default HTML minifying leaves a page, it stays readable", async ({ page, problems }) => {
  const original = await (await page.request.get(url(LARGEST))).text();
  const stripped = original.replace(/<!--[\s\S]*?-->/g, "");
  expect(stripped.length).toBeLessThan(original.length);

  await page.route(`**${url(LARGEST)}`, (route) =>
    route.fulfill({ body: stripped, contentType: "text/html; charset=utf-8" }),
  );

  const exceptions: string[] = [];
  page.on("pageerror", (error) => exceptions.push(error.message));

  await page.goto(url(LARGEST));
  await page.waitForLoadState("load");
  await page.evaluate(() => document.fonts.ready);

  await expectReadable(page);

  const sameArticle = await page.evaluate((html) => {
    const parsed = new DOMParser().parseFromString(html, "text/html");
    return parsed.querySelector("article")!.textContent === document.querySelector("article")!.textContent;
  }, original);

  expect(sameArticle, "the article's text is unchanged").toBe(true);
  await expectFoldOpens(page);
  expect(exceptions).toEqual([]);

  // Each island reports a failed hydration and stays inert. Any other problem still fails the test.
  await expect.poll(() => problems.filter((problem) => problem.includes("Hydration")).length).toBeGreaterThan(0);
  const others = problems.filter((problem) => !problem.includes("Hydration"));
  problems.length = 0;
  problems.push(...others);

  await page.unroute(`**${url(LARGEST)}`);
  await expectLinkFollows(page);
  problems.length = 0;
});

test("a link to a primed or repeated name lands on its declaration", async ({ page }) => {
  test.setTimeout(300_000);
  let checked = 0;

  for (const file of modulePages) {
    await page.goto(url(file));

    const anchors = await page.evaluate(() =>
      [...document.querySelectorAll("article section.decl[id]")].map((section) => section.id).filter((id) => /['~]/.test(id)),
    );

    for (const anchor of anchors) {
      await page.goto(url(file));
      const link = page.locator(`a[href$="#${anchor}"]`).first();

      if ((await link.count()) > 0) {
        await link.evaluate((element: HTMLAnchorElement) => element.click());
      } else {
        await page.goto(`${url(file)}#${anchor}`);
      }

      await page.waitForFunction((anchor) => document.querySelector(":target")?.id === anchor, anchor);
      const top = await page.evaluate(() => document.querySelector(":target")!.getBoundingClientRect().top);
      expect(top, anchor).toBeGreaterThanOrEqual(0);
      expect(top, anchor).toBeLessThan(page.viewportSize()!.height);
      await expect(page.locator(`section.decl[id="${anchor}"] pre.statement`).first()).toContainText(anchor.split("~")[0]);
      checked += 1;
    }
  }

  expect(checked).toBeGreaterThan(0);
  console.log(`primed or repeated anchors checked: ${checked}`);
});
