import type { Locator, Page } from "@playwright/test";
import { BASE, clientReady, expect, MODULE_PAGE, test } from "./support";

const DATA_FILE = "data/Fixture.Guide.symbols.json";

type Card = {
  key: string;
  name: string;
  qualified: string;
  module: string;
  href?: string;
  signature: string;
  docHtml?: string;
  mentions: number;
  external: boolean;
};

const cardOf = (page: Page) => page.locator('[data-island="cards"] .card');

const open = async (page: Page) => {
  await page.goto(MODULE_PAGE);
  await clientReady(page);
};

const symbols = async (page: Page): Promise<Card[]> => (await page.request.get(`${BASE}${DATA_FILE}`)).json();

const keyOf = async (page: Page, qualified: string) =>
  (await symbols(page)).find((card) => card.qualified === qualified)!.key;

/** The first visible reference in a declaration's statement. */
const statementRef = (page: Page, key: string) => page.locator(`article .decl pre.statement [data-ref="${key}"]`).first();

/** Adds a link to the article, for a symbol the fixture's page never refers to. */
const injectRef = async (page: Page, key: string) => {
  await page.evaluate((key) => {
    const link = document.createElement("a");
    link.href = "#";
    link.dataset.ref = key;
    link.textContent = `injected ${key}`;
    document.querySelector("article h1")!.append(link);
  }, key);

  return page.locator(`article h1 [data-ref="${key}"]`);
};

const box = async (locator: Locator) => (await locator.boundingBox())!;

test("hover opens after about 120 ms with type, doc, module and mention count", async ({ page }) => {
  await open(page);
  const reference = statementRef(page, await keyOf(page, "Fixture.Other.other_def"));
  const card = cardOf(page);
  await expect(card).toBeHidden();

  await page.evaluate(() => {
    const times = ((window as any).times = {} as { over?: number; shown?: number });
    document.addEventListener("pointerover", (event) => {
      if ((event.target as Element).closest("[data-ref]")) times.over ??= performance.now();
    });
    const element = document.querySelector('[data-island="cards"] .card')!;
    new MutationObserver(() => {
      if (!element.hasAttribute("hidden")) times.shown ??= performance.now();
    }).observe(element, { attributes: true, attributeFilter: ["hidden"] });
  });

  await reference.hover();
  await expect(card).toBeVisible();
  const { over, shown } = await page.evaluate(() => (window as any).times);
  expect(shown - over).toBeGreaterThan(90);
  expect(shown - over).toBeLessThan(400);

  await expect(card.locator(".card-name")).toHaveText("other_def");
  await expect(card.locator(".card-signature")).toHaveText("nat -> nat");
  await expect(card.locator(".card-doc")).toContainText("Doc with CRLF ends: µ.");
  await expect(card.locator(".card-meta")).toHaveText("Fixture.Other · mentioned by 3");
  await expect(card.locator(".card-quiet")).toBeHidden();
  await expect(card.locator(".card-name")).toHaveAttribute("href", `${BASE}Fixture.Other.html#other_def`);
});

test("the card stays open while the pointer is on it", async ({ page }) => {
  await open(page);
  const card = cardOf(page);
  await statementRef(page, await keyOf(page, "Fixture.Other.other_def")).hover();
  await expect(card).toBeVisible();

  await card.hover();
  await page.waitForTimeout(400);
  await expect(card).toBeVisible();

  await page.locator("article h1.page-title").hover();
  await expect(card).toBeHidden();
});

test("leaving a reference closes the card, and Esc closes a card opened by hover", async ({ page }) => {
  await open(page);
  const card = cardOf(page);
  const reference = statementRef(page, await keyOf(page, "Fixture.Other.other_def"));

  await reference.hover();
  await expect(card).toBeVisible();
  await page.locator(".topbar").hover();
  await expect(card).toBeHidden();

  await reference.hover();
  await expect(card).toBeVisible();
  await page.keyboard.press("Escape");
  await expect(card).toBeHidden();
});

test("keyboard focus opens it, Esc closes it, and Enter follows the link", async ({ page }) => {
  await open(page);
  const card = cardOf(page);
  const reference = statementRef(page, await keyOf(page, "Fixture.Other.other_def"));

  await reference.focus();
  await expect(card).toBeVisible();
  await expect(reference).toHaveAttribute("aria-describedby", (await card.getAttribute("id"))!);

  await page.keyboard.press("Escape");
  await expect(card).toBeHidden();
  await expect(reference).toBeFocused();
  await expect(reference).not.toHaveAttribute("aria-describedby");

  await page.keyboard.press("Enter");
  await expect(page).toHaveURL(new RegExp(`${BASE}Fixture\\.Other\\.html#other_def$`));
});

test("an external reference shows its qualified name and 'external', with no link", async ({ page }) => {
  await open(page);
  const card = cardOf(page);
  await statementRef(page, await keyOf(page, "Corelib.Init.Datatypes.nat")).hover();

  await expect(card.locator(".card-name")).toHaveText("Corelib.Init.Datatypes.nat");
  await expect(card.locator(".card-meta")).toContainText("external");
  await expect(card.locator("a[href]")).toHaveCount(0);
  await expect(card.locator(".card-quiet")).toBeHidden();
});

test("a binder shows its inferred type", async ({ page }) => {
  await open(page);
  const card = cardOf(page);
  const binder = page.locator('article .decl[id="sum_refl"] pre.statement [data-ref^="b"]').first();
  await binder.hover();

  await expect(card.locator(".card-name")).toHaveText("a");
  await expect(card.locator(".card-signature")).toHaveText("nat");
  await expect(card.locator("a[href]")).toHaveCount(0);
});

test("a doc-less symbol shows 'No documentation' quietly", async ({ page }) => {
  await open(page);
  const card = cardOf(page);
  await page.locator('.decl[id="rests_on_axiom"] details.proof summary').click();
  await page.locator(`.decl[id="rests_on_axiom"] details.proof [data-ref="${await keyOf(page, "Fixture.Guide.fixture_axiom")}"]`).hover();

  const quiet = card.locator(".card-quiet");
  await expect(quiet).toHaveText("No documentation");
  await expect(card.locator(".card-doc")).toBeHidden();

  const colours = await quiet.evaluate((element) => ({
    quiet: getComputedStyle(element).color,
    ink: getComputedStyle(element.closest(".card")!).color,
  }));

  expect(colours.quiet).not.toBe(colours.ink);
});

test("an Admitted theorem's card shows no passing badge", async ({ page }) => {
  await open(page);
  const card = cardOf(page);

  const proved = await injectRef(page, await keyOf(page, "Fixture.Guide.sum_refl"));
  await proved.hover();
  await expect(card.locator(".card-name")).toHaveText("sum_refl");
  await expect(card.locator(".badge.pass")).toBeVisible();
  await expect(card.locator(".badge.pass")).toHaveText("✓ No axioms");

  await page.locator(".topbar").hover();
  await expect(card).toBeHidden();

  const admitted = await injectRef(page, await keyOf(page, "Fixture.Guide.open_goal"));
  await admitted.hover();
  await expect(card.locator(".card-name")).toHaveText("open_goal");
  await expect(card.locator(".card-signature")).toHaveText("∀ n : nat, n = n + 0");
  await expect(card.locator(".badge")).toBeHidden();
});

test("a theorem resting on an axiom names it", async ({ page }) => {
  await open(page);
  const card = cardOf(page);
  await (await injectRef(page, await keyOf(page, "Fixture.Guide.rests_on_axiom"))).hover();

  await expect(card.locator(".badge.axioms")).toHaveText("Rests on fixture_axiom");
  await expect(card.locator(".badge.pass")).toHaveCount(0);
});

test("when the data file fails to load, the card shows the name and the link still works", async ({ page, problems }) => {
  let requests = 0;

  await page.route("**/*.symbols.json", (route) => {
    requests += 1;
    return route.abort();
  });

  await open(page);
  const card = cardOf(page);
  const key = (await keyOf(page, "Fixture.Other.other_def"));
  const reference = statementRef(page, key);
  await reference.hover();

  await expect(card.locator(".card-name")).toHaveText("other_def");
  await expect(card.locator(".card-name")).toHaveAttribute("href", `${BASE}Fixture.Other.html#other_def`);
  await expect(card.locator(".card-quiet")).toBeHidden();

  // Chromium itself reports an aborted request as a console error, and a page cannot silence it. Exactly that one
  // message is expected. Anything the page logs stays in `problems` and fails the test.
  const browserReport = /^error: Failed to load resource: net::ERR_FAILED$/;
  await expect.poll(() => problems.filter((problem) => browserReport.test(problem)).length).toBe(1);
  const others = problems.filter((problem) => !browserReport.test(problem));
  problems.length = 0;
  problems.push(...others);

  await page.locator(".topbar").hover();
  await expect(card).toBeHidden();
  await reference.hover();
  await expect(card.locator(".card-name")).toHaveText("other_def");
  expect(requests).toBe(1);

  await card.locator(".card-name").click();
  await expect(page).toHaveURL(new RegExp(`${BASE}Fixture\\.Other\\.html#other_def$`));
});

test.describe("at 375 px", () => {
  test.use({ viewport: { width: 375, height: 700 }, hasTouch: true });

  test("at 375 px a tap opens the card", async ({ page }) => {
    await open(page);
    const card = cardOf(page);
    const reference = statementRef(page, await keyOf(page, "Fixture.Other.other_def"));
    const url = page.url();

    await reference.tap();
    await expect(card).toBeVisible();
    await expect(card.locator(".card-name")).toHaveText("other_def");
    await expect(card.locator(".card-meta")).toHaveText("Fixture.Other · mentioned by 3");
    await page.waitForTimeout(300);
    await expect(card).toBeVisible();
    expect(page.url()).toBe(url);

    await page.locator("article h1.page-title").tap();
    await expect(card).toBeHidden();

    const binder = page.locator('article .decl[id="sum_refl"] pre.statement [data-ref^="b"]').first();
    await binder.tap();
    await expect(card.locator(".card-name")).toHaveText("a");

    await reference.tap();
    await expect(card.locator(".card-name")).toHaveText("other_def");
    await reference.tap();
    await expect(page).toHaveURL(new RegExp(`${BASE}Fixture\\.Other\\.html#other_def$`));
  });
});

for (const viewport of [
  { width: 1280, height: 720 },
  { width: 375, height: 600 },
  { width: 800, height: 280 },
]) {
  test(`the card stays inside the viewport near every edge at ${viewport.width} by ${viewport.height}`, async ({ page }) => {
    await page.setViewportSize(viewport);
    await open(page);
    const card = cardOf(page);

    // Marks the visible reference closest to each edge after scrolling a candidate to the top, the middle and the bottom.
    const mark = (block: ScrollLogicalPosition, edge: "left" | "right") =>
      page.evaluate(
        ({ block, edge }) => {
          document.querySelector("[data-edge]")?.removeAttribute("data-edge");
          const statements = [...document.querySelectorAll("article .decl pre.statement")];
          const statement = block === "start" ? statements[statements.length - 3] : statements[2];
          statement.scrollIntoView({ block, behavior: "instant" });

          const inView = [...document.querySelectorAll("article .decl pre.statement [data-ref]")].filter((element) => {
            const rect = element.getBoundingClientRect();
            return rect.top >= 0 && rect.bottom <= innerHeight && rect.height > 0;
          });

          const measure = (element: Element) => element.getBoundingClientRect()[edge];
          const best = inView.reduce((a, b) => ((edge === "left" ? measure(b) < measure(a) : measure(b) > measure(a)) ? b : a));
          best.setAttribute("data-edge", "");
        },
        { block, edge },
      );

    for (const block of ["start", "center", "end"] as const) {
      for (const edge of ["left", "right"] as const) {
        await mark(block, edge);
        await page.locator("[data-edge]").hover();
        await expect(card).toBeVisible();
        await expect(card.locator(".card-meta")).not.toBeEmpty();

        await expect
          .poll(async () => {
            const rect = await box(card);
            return (
              rect.x >= 0 &&
              rect.y >= 0 &&
              rect.x + rect.width <= viewport.width &&
              rect.y + rect.height <= viewport.height
            );
          })
          .toBe(true);

        const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
        expect(overflow).toBe(0);

        await page.keyboard.press("Escape");
        await expect(card).toBeHidden();
      }
    }
  });
}

test("every reference in a statement shows the card the build wrote for its key", async ({ page }) => {
  await open(page);
  const card = cardOf(page);
  const cards = new Map((await symbols(page)).map((item) => [item.key, item]));

  const keys = await page
    .locator("article .decl pre.statement [data-ref]")
    .evaluateAll((elements) => [...new Set(elements.map((element) => element.getAttribute("data-ref")!))]);

  expect(keys.length).toBeGreaterThan(5);

  for (const key of keys) {
    const expected = cards.get(key)!;
    await statementRef(page, key).hover();
    await expect(card.locator(".card-name")).toHaveText(expected.external ? expected.qualified : expected.name);
    await expect(card.locator(".card-meta")).toContainText(`mentioned by ${expected.mentions}`);
  }
});

test("name and signature are text, and the doc is HTML", async ({ page }) => {
  const key = "s0";

  await page.route(`**/${DATA_FILE}`, async (route) => {
    const cards: Card[] = await (await route.fetch()).json();
    const edited = cards.map((item) =>
      item.key === key
        ? { ...item, name: "<b>a < b</b>", signature: "<img src=x onerror=alert(1)>", docHtml: "<p><em>rendered</em></p>" }
        : item,
    );
    await route.fulfill({ json: edited });
  });

  await open(page);
  const card = cardOf(page);
  await statementRef(page, key).hover();

  await expect(card.locator(".card-name")).toHaveText("<b>a < b</b>");
  await expect(card.locator(".card-signature")).toHaveText("<img src=x onerror=alert(1)>");
  await expect(card.locator(".card-name b, .card-signature img")).toHaveCount(0);
  await expect(card.locator(".card-doc em")).toHaveText("rendered");
});

test("the data file is fetched once, on first hover, under the base URL", async ({ page }) => {
  const requests: string[] = [];

  page.on("request", (request) => {
    if (request.url().includes(".symbols.json")) requests.push(new URL(request.url()).pathname);
  });

  await open(page);
  const card = cardOf(page);
  expect(requests).toEqual([]);

  await statementRef(page, await keyOf(page, "Fixture.Other.other_def")).hover();
  await expect(card.locator(".card-meta")).not.toBeEmpty();
  await statementRef(page, await keyOf(page, "Corelib.Init.Datatypes.nat")).hover();
  await expect(card.locator(".card-name")).toHaveText("Corelib.Init.Datatypes.nat");

  // `symbols` itself reads the file through the API context, which `page.on` does not see.
  expect(requests).toEqual([`${BASE}${DATA_FILE}`]);
});

test("a reference list copied to the right pane stays an ordinary link", async ({ page }) => {
  await page.setViewportSize({ width: 1400, height: 800 });
  await open(page);
  const card = cardOf(page);

  await page.locator('.decl[id="plain_doc"]').scrollIntoViewIfNeeded();
  await page.evaluate(() => {
    const target = document.querySelector('.decl[id="plain_doc"]')!;
    window.scrollTo({ top: target.getBoundingClientRect().top + scrollY - 10, behavior: "instant" });
  });

  const copy = page.locator(".pane-right [data-slot] [data-ref]").first();
  await expect(copy).toBeVisible();
  await copy.hover();
  await page.waitForTimeout(300);
  await expect(card).toBeHidden();
});

test("the card takes each look's tokens and appears with motion", async ({ page }) => {
  await open(page);
  const card = cardOf(page);
  await statementRef(page, await keyOf(page, "Fixture.Other.other_def")).hover();
  await expect(card).toBeVisible();

  const backgrounds = await page.evaluate(() => {
    const root = document.documentElement;
    const element = document.querySelector('[data-island="cards"] .card')!;
    const read = (look: string, mode: string) => {
      root.dataset.look = look;
      root.dataset.mode = mode;
      return getComputedStyle(element).backgroundColor;
    };

    return [read("family", "light"), read("family", "dark"), read("journal", "light"), read("journal", "dark")];
  });

  expect(new Set(backgrounds).size).toBe(4);
});

test("a real card runs the appearance animation", async ({ page }) => {
  await open(page);
  await page.evaluate(() => {
    const element = document.querySelector('[data-island="cards"] .card')!;
    new MutationObserver(() => {
      if (!element.hasAttribute("hidden")) {
        (window as any).running ??= element.getAnimations().map((animation) => (animation as CSSAnimation).animationName);
      }
    }).observe(element, { attributes: true, attributeFilter: ["hidden"] });
  });

  await statementRef(page, await keyOf(page, "Fixture.Other.other_def")).hover();
  await expect(cardOf(page)).toBeVisible();
  expect(await page.evaluate(() => (window as any).running)).toEqual(["rocq-card-in"]);
  await expect(cardOf(page)).toHaveCSS("animation-name", "rocq-card-in");
});

test("under reduced motion a real card appears with nothing running", async ({ page }) => {
  await page.emulateMedia({ reducedMotion: "reduce" });
  await open(page);
  await statementRef(page, await keyOf(page, "Fixture.Other.other_def")).hover();
  const card = cardOf(page);
  await expect(card).toBeVisible();
  expect(await card.evaluate((element) => element.getAnimations().length)).toBe(0);
});
