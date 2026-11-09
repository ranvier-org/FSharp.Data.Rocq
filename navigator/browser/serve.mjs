// Serves NAVIGATOR_SITE at /sub/path/ and answers 404 everywhere else, so a URL that ignores the base URL fails.
import { createReadStream, statSync } from "node:fs";
import { createServer } from "node:http";
import { extname, join, normalize, sep } from "node:path";

const site = process.env.NAVIGATOR_SITE;
const port = Number(process.env.PORT ?? 4173);
const base = "/sub/path/";

if (!site) {
  console.error("NAVIGATOR_SITE names the directory of a site built with NAVIGATOR_BASE_URL=/sub/path/.");
  process.exit(2);
}

const types = {
  ".html": "text/html; charset=utf-8",
  ".css": "text/css; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".json": "application/json; charset=utf-8",
  ".woff2": "font/woff2",
  ".txt": "text/plain; charset=utf-8",
};

const fileOf = (pathname) => {
  if (!pathname.startsWith(base)) return null;
  const relative = normalize(decodeURIComponent(pathname.slice(base.length)) || "index.html");
  if (relative.startsWith("..") || relative.includes(`..${sep}`)) return null;
  const path = join(site, relative);

  try {
    const stat = statSync(path);
    if (stat.isFile()) return path;
    if (stat.isDirectory() && statSync(join(path, "index.html")).isFile()) return join(path, "index.html");
  } catch {}

  return null;
};

createServer((request, response) => {
  const file = fileOf(new URL(request.url, "http://localhost").pathname);

  if (!file) {
    response.writeHead(404);
    response.end();
    return;
  }

  response.writeHead(200, { "content-type": types[extname(file)] ?? "application/octet-stream" });
  createReadStream(file).pipe(response);
}).listen(port, "127.0.0.1");
