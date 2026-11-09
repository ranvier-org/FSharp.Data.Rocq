import { resolve } from "node:path";
import { defineConfig, devices } from "@playwright/test";

// A site directory built with NAVIGATOR_BASE_URL=/sub/path/. `npm run site` builds the demo site this default names.
const site = resolve(process.env.NAVIGATOR_SITE ?? "../Rocq.Navigator.Site/output");
const port = Number(process.env.NAVIGATOR_PORT ?? 4173);

// With NAVIGATOR_CORPUS set, the site is a real project's: the specs that name fixture declarations stay out.
const corpus = Boolean(process.env.NAVIGATOR_CORPUS);

export default defineConfig({
  testDir: ".",
  testMatch: corpus ? ["acceptance.spec.ts", "corpus.spec.ts"] : "*.spec.ts",
  testIgnore: corpus ? [] : "corpus.spec.ts",
  outputDir: "test-results",
  fullyParallel: true,
  forbidOnly: true,
  reporter: "list",
  use: {
    ...devices["Desktop Chrome"],
    baseURL: `http://127.0.0.1:${port}`,
  },
  webServer: {
    command: "node serve.mjs",
    url: `http://127.0.0.1:${port}/sub/path/`,
    reuseExistingServer: false,
    env: { NAVIGATOR_SITE: site, PORT: String(port) },
  },
});
