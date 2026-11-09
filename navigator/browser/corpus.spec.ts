import { readdirSync, statSync } from "node:fs";
import { join, resolve } from "node:path";
import { BASE, clientReady, expect, test } from "./support";

const site = resolve(process.env.NAVIGATOR_SITE ?? "../Rocq.Navigator.Site/output");

/** The module pages, largest first. */
const modulePages = readdirSync(site)
  .filter((file) => file.endsWith(".html") && file !== "index.html")
  .map((file) => ({ file, size: statSync(join(site, file)).size }))
  .sort((a, b) => b.size - a.size)
  .map((page) => page.file);

for (const file of modulePages.slice(0, 3)) {
  for (const foldsOpen of [false, true]) {
    for (const height of [500, 900]) {
      const state = foldsOpen ? "every fold open" : "folds closed";

      test(`${file}, ${height} px, ${state}: the marker and lists stay on the section at the line`, async ({ page }) => {
        test.setTimeout(300_000);
        await page.setViewportSize({ width: 1280, height });
        await page.goto(`${BASE}${file}`);
        await clientReady(page);

        const result = await page.evaluate(
          async ({ foldsOpen }) => {
            if (foldsOpen) {
              for (const details of document.querySelectorAll("article details")) (details as HTMLDetailsElement).open = true;
            }

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

            const view = document.documentElement.clientHeight;
            const scrollEnd = () => document.documentElement.scrollHeight - view;
            const links = new Set([...document.querySelectorAll('.pane-right .pane-item[href^="#"]')].map((link) => link.getAttribute("href")));
            const tallest = Math.max(...sections.map((section) => section.getBoundingClientRect().height));

            const problems: string[] = [];
            let positions = 0;

            const check = async (position: number) => {
              window.scrollTo(0, position);
              await frames();

              const line = view * (document.documentElement.scrollTop / scrollEnd());
              const tops = sections.map((section) => section.getBoundingClientRect().top);
              if (tops.some((value) => Math.abs(value - line) < 1)) return;

              let best = -1;
              tops.forEach((value, index) => {
                if (value <= line && (best < 0 || value >= tops[best])) best = index;
              });

              if (best < 0) return;
              positions += 1;

              const expected = sections[best];
              const href = `#${expected.id}`;

              if (links.has(href)) {
                const marked = document.querySelector(".pane-right .in-view")?.getAttribute("href");
                if (marked !== href) problems.push(`y=${Math.round(scrollY)}: ${marked} marked at ${href}`);
              }

              if (expected.matches(".decl")) {
                for (const name of ["uses", "used-by"]) {
                  const inline = expected.querySelector(`.ref-list[data-list="${name}"]`)?.textContent ?? "";
                  const shown = document.querySelector(`.pane-right [data-slot="${name}"]`)?.textContent ?? "";
                  if (inline !== shown) problems.push(`y=${Math.round(scrollY)}: ${name} of ${href} differs`);
                }
              }

              // The mark and the lists sit inside the pane's visible rectangle.
              const pane = document.querySelector(".pane-right")!.getBoundingClientRect();
              const pinned = document.querySelector(".pane-right .pinned")!.getBoundingClientRect();
              const mark = document.querySelector(".pane-right .in-view")?.getBoundingClientRect();

              if (pinned.top < pane.top || pinned.bottom > pane.bottom + 1 || pinned.height === 0) problems.push(`y=${Math.round(scrollY)}: the lists are out of the pane`);
              if (mark && (mark.top < pane.top || mark.bottom > pinned.top + 1)) problems.push(`y=${Math.round(scrollY)}: the mark is out of the pane`);
            };

            // A stride through the page, then each section's own top region and its middle.
            const end = document.documentElement.scrollHeight - document.documentElement.clientHeight;
            for (let position = 0; position < end; position += Math.max(97, end / 400)) await check(position);

            for (const section of sections.filter((_, index) => index % 5 === 0)) {
              const box = section.getBoundingClientRect();
              await check((box.top + scrollY + box.height / 2) * (scrollEnd() / (scrollEnd() + view)));
            }

            return { problems, positions, sections: sections.length, tallest: Math.round(tallest) };
          },
          { foldsOpen },
        );

        console.log(`${file} ${height} ${state}: ${result.sections} sections, ${result.positions} positions, tallest ${result.tallest} px`);
        expect(result.positions).toBeGreaterThan(50);
        expect(result.problems.slice(0, 20)).toEqual([]);
      });
    }
  }
}
