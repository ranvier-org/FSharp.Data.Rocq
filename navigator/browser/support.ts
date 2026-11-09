import { expect, test as base, type Page } from "@playwright/test";

export const BASE = "/sub/path/";
export const MODULE_PAGE = `${BASE}Fixture.Guide.html`;
export const OTHER_PAGE = `${BASE}Fixture.Other.html`;
export const GUARANTEES_PAGE = `${BASE}index.html`;
export const LONG_PAGE = `${BASE}Fixture.Long.html`;
export const ARGS_PAGE = `${BASE}Fixture.Args.html`;

type Observed = { problems: string[] };

/** Collects a page's console errors, uncaught exceptions, hydration reports and requests that miss the site. */
export const observe = (page: Page, problems: string[]) => {
  page.on("console", (message) => {
    // Windows out of socket buffers on the machine running the tests, on any request.
    if (message.text().includes("net::ERR_NO_BUFFER_SPACE")) return;

    if (message.type() === "error" || message.text().includes("Hydration")) {
      problems.push(`${message.type()}: ${message.text()}`);
    }
  });

  page.on("pageerror", (error) => problems.push(`exception: ${error.message}`));

  page.on("response", (response) => {
    if (response.status() >= 400) {
      problems.push(`${response.status()}: ${response.url()}`);
    }
  });
};

/** Every test fails on what `observe` collects. */
export const test = base.extend<Observed>({
  page: async ({ page }, use) => {
    const goto = page.goto.bind(page);

    page.goto = (...args) =>
      goto(...args).catch((error) => {
        if (!String(error).includes("net::ERR_NO_BUFFER_SPACE")) throw error;
        return goto(...args);
      });

    await use(page);
  },
  problems: [
    async ({ page }, use) => {
      const problems: string[] = [];
      observe(page, problems);
      await use(problems);
      expect(problems).toEqual([]);
    },
    { auto: true },
  ],
});

export { expect };

/** Waits until the client bundle has started every island. */
export const clientReady = (page: Page) =>
  page.waitForFunction(() => document.documentElement.dataset.client === "ready");

/** Counts what can move on the page: running animations, non-zero transitions and animations, and inline fold heights. */
export const runningAnimations = (page: Page) =>
  page.evaluate(() => {
    const moving = (element: Element, pseudo?: string) => {
      const style = getComputedStyle(element, pseudo);
      const nonZero = (durations: string) => durations.split(",").some((duration) => parseFloat(duration) > 0);
      return nonZero(style.transitionDuration) || nonZero(style.animationDuration);
    };

    const elements = [...document.querySelectorAll("*")];

    return (
      document.getAnimations().length +
      elements.filter((element) => moving(element) || moving(element, "::before")).length +
      elements.filter((element) => element instanceof HTMLElement && element.style.height !== "").length
    );
  });
