// The settings audit: every row that is LIVE (no phase badge) must have a key something in
// Aviary actually reads - in the C# outside the page, or in the page's own script below SECTIONS.
//
// M6: Experimental's keys have a dot in them (exp.darkWebsites), so keys may too; and every
// Experimental row must be in Experiments.cs under the SAME name, with nothing there that the
// page does not show - the name appears in the question after a problem, and a switch with no
// row would be an experiment nobody can turn off.
const fs = require("fs");
const dir = "C:/Users/user/Documents/Claude CoWork Enclosed/Zoo/src/Aviary/";
const src = fs.readFileSync(dir + "InternalPages.cs", "utf8");

const start = src.indexOf("var SECTIONS");
const end = src.indexOf("function control(row)");
const body = src.slice(start, end);
const after = src.slice(end);
const esc = s => s.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");

const rows = body.split(/\{t:/).slice(1);
const live = [], gated = [];
for (const r of rows) {
  const head = r.split("ctl:")[0];
  const km = r.match(/key:'([A-Za-z0-9_.]+)'/);
  const title = (r.match(/^'([^']*)'/) || r.match(/^\s*([^,]*),/) || ["", "?"])[1];
  if (!km) continue;
  const isGated = /\bphase:\s*\d/.test(head) || /never:/.test(head);
  (isGated ? gated : live).push({ key: km[1], title });
}

const others = fs.readdirSync(dir).filter(f => f.endsWith(".cs") && f !== "InternalPages.cs")
  .map(f => fs.readFileSync(dir + f, "utf8")).join("\n");

const unread = [];
for (const row of live) {
  const inCs = new RegExp('"' + esc(row.key) + '"').test(others);
  const inJs = new RegExp("[^A-Za-z0-9_.]" + esc(row.key) + "[^A-Za-z0-9_.]").test(after);
  if (!inCs && !inJs) unread.push(row);
}

console.log("live keyed rows:  " + live.length);
console.log("gated keyed rows: " + gated.length);
console.log("live rows nothing reads: " + (unread.length ? "" : "none"));
for (const u of unread) console.log("   " + u.key + "   (" + u.title + ")");

// M6: Experimental rows against the registry.
const reg = fs.readFileSync(dir + "Experiments.cs", "utf8");
const registry = [...reg.matchAll(/new\("(exp\.[A-Za-z0-9_]+)",\s*"([^"]*)"/g)].map(m => ({ key: m[1], name: m[2] }));
const expRows = live.concat(gated).filter(r => r.key.startsWith("exp."));
const problems = [];
for (const row of expRows) {
  const e = registry.find(x => x.key === row.key);
  if (!e) problems.push(row.key + ": a row with no entry in Experiments.cs");
  else if (e.name !== row.title) problems.push(row.key + ": the row says '" + row.title + "', Experiments.cs says '" + e.name + "'");
}
for (const e of registry) {
  if (!expRows.some(r => r.key === e.key)) problems.push(e.key + ": in Experiments.cs with no row - nobody could turn it off");
}
console.log("experimental rows: " + expRows.length + " (registry " + registry.length + "), mismatches: " + (problems.length ? "" : "none"));
for (const p of problems) console.log("   " + p);
