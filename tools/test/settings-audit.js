// The settings audit: every row that is LIVE (no phase badge) must have a key something in
// Aviary actually reads - in the C# outside the page, or in the page's own script below SECTIONS.
const fs = require("fs");
const dir = "C:/Users/user/Documents/Claude CoWork Enclosed/Zoo/src/Aviary/";
const src = fs.readFileSync(dir + "InternalPages.cs", "utf8");

const start = src.indexOf("var SECTIONS");
const end = src.indexOf("function control(row)");
const body = src.slice(start, end);
const after = src.slice(end);

const rows = body.split(/\{t:/).slice(1);
const live = [], gated = [];
for (const r of rows) {
  const head = r.split("ctl:")[0];
  const km = r.match(/key:'([A-Za-z0-9_]+)'/);
  const title = (r.match(/^'([^']*)'/) || r.match(/^\s*([^,]*),/) || ["", "?"])[1];
  if (!km) continue;
  const isGated = /\bphase:\s*\d/.test(head) || /never:/.test(head);
  (isGated ? gated : live).push({ key: km[1], title });
}

const others = fs.readdirSync(dir).filter(f => f.endsWith(".cs") && f !== "InternalPages.cs")
  .map(f => fs.readFileSync(dir + f, "utf8")).join("\n");

const unread = [];
for (const row of live) {
  const inCs = new RegExp('"' + row.key + '"').test(others);
  const inJs = new RegExp("[^A-Za-z0-9_]" + row.key + "[^A-Za-z0-9_]").test(after);
  if (!inCs && !inJs) unread.push(row);
}

console.log("live keyed rows:  " + live.length);
console.log("gated keyed rows: " + gated.length);
console.log("live rows nothing reads: " + (unread.length ? "" : "none"));
for (const u of unread) console.log("   " + u.key + "   (" + u.title + ")");
