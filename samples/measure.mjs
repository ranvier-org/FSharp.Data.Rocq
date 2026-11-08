// A plain JavaScript reader, standing in for a browser: JSON.parse, byte offsets and UTF-16 slicing.
// Usage: node measure.mjs <out> [name]
import { readFileSync } from "node:fs";
import { join } from "node:path";

const out = process.argv[2];
const read = (p) => readFileSync(join(out, p), "utf8");

function time(fn, runs = 31) {
  const once = () => {
    const t = performance.now();
    fn();
    return performance.now() - t;
  };
  const first = once();
  const rest = Array.from({ length: runs }, once).sort((a, b) => a - b);
  return `${first.toFixed(2)} | ${rest[runs >> 1].toFixed(2)}`;
}

const index = JSON.parse(read("index.json"));
const xref = JSON.parse(read("xref.json"));
const large = index.files.reduce((a, b) => (a.bytes > b.bytes ? a : b));

function whoUses(node) {
  const { from, where, count } = xref.edges;
  const hits = [];
  for (let e = xref.toOffsets[node]; e < xref.toOffsets[node + 1]; e++) {
    hits.push([from[e] >= 0 ? index.declarations.id[from[e]] : index.files[~from[e]].module, where[e], count[e]]);
  }
  return hits;
}

const qualified = index.symbols.qualified;
let busiest = 0;
for (let n = 0; n < qualified.length; n++) {
  if (xref.toOffsets[n + 1] - xref.toOffsets[n] > xref.toOffsets[busiest + 1] - xref.toOffsets[busiest]) busiest = n;
}

console.log(`Largest file: ${large.module}.\n`);
console.log("| Operation (node) | First (ms) | Median (ms) |\n|---|---|---|");
console.log(`| JSON.parse index.json | ${time(() => JSON.parse(read("index.json")))} |`);
console.log(`| JSON.parse xref.json | ${time(() => JSON.parse(read("xref.json")))} |`);
for (const layer of ["outline", "refs", "tokens"]) {
  console.log(`| JSON.parse largest ${layer} | ${time(() => JSON.parse(read(large.shards[layer].path)))} |`);
}
console.log(`| who-uses on parsed data (${whoUses(busiest).length} mentions) | ${time(() => whoUses(busiest))} |`);

// Rebuild the source from tokens, and slice a declaration by converting its byte offsets through the token table.
const tokens = JSON.parse(read(large.shards.tokens.path));
const outline = JSON.parse(read(large.shards.outline.path));
const { start, u16, source } = tokens;

function byteToUtf16(offset) {
  let lo = 0;
  let hi = start.length - 1;
  while (lo < hi) {
    const mid = (lo + hi + 1) >> 1;
    if (start[mid] <= offset) lo = mid;
    else hi = mid - 1;
  }
  if (start[lo] !== offset) {
    if (offset === tokens.bytes) return tokens.utf16Length;
    throw new Error(`offset ${offset} is between tokens`);
  }
  return u16[lo];
}

const rebuilt = u16.map((s, i) => source.slice(s, i + 1 < u16.length ? u16[i + 1] : source.length)).join("");
if (rebuilt !== source) throw new Error("tokens do not rebuild the source");

const encoder = new TextEncoder();
const decoder = new TextDecoder();
const bytes = encoder.encode(source);
for (const d of outline.declarations) {
  const [s, e] = d.sentenceSpan;
  const viaBytes = decoder.decode(bytes.subarray(s, e));
  const viaUtf16 = source.slice(byteToUtf16(s), byteToUtf16(e));
  if (viaBytes !== viaUtf16) throw new Error(`slices differ for ${d.id}`);
}
console.log(`\nRebuilt ${large.module} from ${start.length} tokens, and sliced ${outline.declarations.length} declarations both ways.`);
