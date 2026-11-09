// Builds the demo site from the fixture project under /sub/path/, into navigator/Rocq.Navigator.Site/output.
// Written in Node because Git Bash rewrites an environment value such as /sub/path/ into a Windows path.
import { spawnSync } from "node:child_process";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const browser = dirname(fileURLToPath(import.meta.url));
const navigator = resolve(browser, "..");
const fixtures = resolve(navigator, "Rocq.Navigator.Tests/fixtures");

const { status } = spawnSync(
  "dotnet",
  ["run", "--project", resolve(navigator, "Rocq.Navigator.Site/Rocq.Navigator.Site.fsproj"), "--", "build"],
  {
    cwd: navigator,
    stdio: "inherit",
    env: {
      ...process.env,
      NAVIGATOR_BASE_URL: "/sub/path/",
      NAVIGATOR_PROJECT: resolve(fixtures, "_CoqProject"),
      NAVIGATOR_CONFIG: resolve(fixtures, "navigator.json"),
    },
  },
);

process.exit(status ?? 1);
