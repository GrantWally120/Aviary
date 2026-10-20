// Summarise actual local diagnostics; never substitutes historical numbers for this run.
const fs = require('node:fs');
const path = require('node:path');
const profile = process.argv[2] || 'm7-benchmark';
if (!/^[a-zA-Z0-9_-]+$/.test(profile)) throw Error('Use a scratch profile name');
const dir = path.join(__dirname, 'profiles', profile, 'user.data.improvementcollection');
function rows(file) {
  return fs.readFileSync(path.join(dir, file), 'utf8').trim().split(/\r?\n/)
    .filter(Boolean).map(line => JSON.parse(line));
}
const events = rows('events.jsonl');
const modes = new Map(events.filter(r => r.k === 'session' && r.name === 'start')
  .map(r => [r.s, r.settings.quik === 'true' ? 'quik' : 'regular']));
const perf = rows('perf.jsonl');
const starts = perf.filter(r => r.k === 'startup');
function median(values) {
  values = [...values].sort((a,b) => a-b);
  const n = values.length;
  return n ? (values[Math.floor((n-1)/2)] + values[Math.floor(n/2)]) / 2 : null;
}
const summary = {};
for (const mode of ['regular','quik']) {
  const times = starts.filter(r => modes.get(r.s) === mode).map(r => r.totalMs);
  summary[mode] = {count:times.length, times, medianMs:median(times)};
}
summary.fasterPercent = summary.regular.medianMs && summary.quik.medianMs
  ? +(100 * (1-summary.quik.medianMs/summary.regular.medianMs)).toFixed(2) : null;
summary.newTabs = {};
for (const ready of [false, true]) {
  const times = perf.filter(r => r.k === 'tab-open' && r.ready === ready).map(r => r.ms);
  summary.newTabs[ready ? 'prepared' : 'ordinary'] = {
    count:times.length, times, medianMs:median(times),
    minMs:times.length ? Math.min(...times) : null,
    maxMs:times.length ? Math.max(...times) : null,
  };
}
console.log(JSON.stringify(summary, null, 2));
