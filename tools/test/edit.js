// Usage: node edit.js <edits.js>   where edits.js does:  module.exports = [ [file, [[find, replace], ...]], ... ]
// Matches with the FILE's own line endings, skips pairs already applied, reports misses.
const fs = require("fs");
const path = require("path");
const edits = require(path.resolve(process.argv[2]));
let missing = 0;
for (const [file, pairs] of edits) {
  let s = fs.readFileSync(file, "utf8");
  const crlf = (s.match(/\r\n/g) || []).length > (s.match(/(?<!\r)\n/g) || []).length;
  const norm = t => crlf ? t.replace(/\r?\n/g, "\r\n") : t.replace(/\r\n/g, "\n");
  for (let [a, b] of pairs) {
    a = norm(a); b = norm(b);
    if (s.includes(b)) { console.log("already: " + file + " :: " + a.split(/\r?\n/)[0].trim().slice(0, 60)); continue; }
    if (!s.includes(a)) { console.log("MISSING: " + file + " :: " + a.split(/\r?\n/)[0].trim().slice(0, 80)); missing++; continue; }
    s = s.replace(a, () => b);
  }
  if (crlf) s = s.replace(/(?<!\r)\n/g, "\r\n");
  fs.writeFileSync(file, s);
}
if (missing) { process.exitCode = 1; }
console.log("edits done, missing=" + missing);
