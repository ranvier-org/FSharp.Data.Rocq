import { expect, test as base, type Page } from "@playwright/test";

export const BASE = "/sub/path/";
export const MODULE_PAGE = `${BASE}Fixture.Guide.html`;
export const OTHER_PAGE = `${BASE}Fixture.Other.html`;
export const GUARANTEES_PAGE = `${BASE}index.html`;

type Observed = { problems: string[] };

/** Every test fails on a console error, an uncaught exception, a hydration report, or a request that misses the site. */
export const test = base.extend<Observed>({
  problems: [
    async ({ page }, use) => {
      const problems: string[] = [];

      page.on("console", (message) => {
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
