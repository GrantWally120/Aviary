// Release regression: the real roadmap selectors must keep the last completed build
// current when nothing is active, rather than promoting a planned beta.
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const root = path.resolve(__dirname, '../..');
const landing = fs.readFileSync(path.join(root, 'Aviary.html'), 'utf8');
const buildInfo = fs.readFileSync(path.join(root, 'src/Aviary/BuildInfo.cs'), 'utf8');
const internal = fs.readFileSync(path.join(root, 'src/Aviary/InternalPages.cs'), 'utf8');
const phasesSource = landing.match(/const PHASES=(\[[\s\S]*?\n\]);/)[1];
const currentSelector = landing.match(/^const phaseNow=.*\r?\nconst buildNow=.*$/m)[0];
const cardSelector = landing.match(/  const active=p\.builds\.findIndex[^\n]*\n  const start=[^\n]*\n  pick\([^\n]*/)[0];
const phases = vm.runInNewContext('(' + phasesSource + ')');
const ab5 = phases.flatMap(p => p.builds).find(b => b.code === 'AB5');
const constant = name => Number(buildInfo.match(new RegExp('public const int ' + name + ' = (\\d+);'))[1]);
let passed = 0;
function check(name, fn) { fn(); passed++; console.log('PASS ' + name); }
function current(data) {
  return vm.runInNewContext(currentSelector + '\nbuildNow.code', {PHASES:data});
}
function selected(phase) {
  let index;
  vm.runInNewContext(cardSelector, {p:phase, pick:i => { index=i; }});
  return phase.builds[index].code;
}
check('release counters agree', () => {
  if (constant('PhaseNumber') === 5) {
    assert.equal(constant('PhaseShipped'), 5);
    assert.equal(constant('MilestonesDone'), constant('Milestones'));
  }
});
check('all sixteen AB5 milestones are dated', () => {
  assert.equal(ab5.state, 'done');
  assert.equal(ab5.milestones.length, 16);
  assert.ok(ab5.milestones.every(m => m.when));
});
// Beta Prep 1 (25 Oct 2026) is the current build, so it is what the landing page's hero, What's new and badge follow.
const bp1 = phases.flatMap(p => p.builds).find(b => b.code === 'BP1');
check('Beta Prep 1 is the current build on the landing page', () => {
  assert.equal(current(phases), 'BP1');
  assert.equal(bp1.state, 'done');
  assert.deepEqual(Array.from(phases[1].builds, b => b.code), ['BP1', 'BB1', 'BB2']);
});
check('alpha card initially selects the completed AB5', () => assert.equal(selected(phases[0]), 'AB5'));
check('Beta Build 1 and 2 remain planned', () => assert.ok(phases[1].builds.filter(b => b.code !== 'BP1').every(b => b.state === 'todo')));
check('the beta card initially selects the current build, Beta Prep 1', () => assert.equal(selected(phases[1]), 'BP1'));
check('a later active build takes precedence', () => {
  const future = JSON.parse(JSON.stringify(phases));
  future[1].builds.find(b => b.code === 'BB1').state = 'active';
  assert.equal(current(future), 'BB1');
});
check('a later prep takes over as the current build', () => {
  const future = JSON.parse(JSON.stringify(phases));
  future[1].builds.splice(1, 0, {code:'BP2', title:'Beta Prep 2', state:'done', when:'1 Nov 2026', bits:[]});
  assert.equal(current(future), 'BP2');
});
check('M9 appears with its date in the actual Updates data', () => {
  assert.match(internal, /\{n:'M9', t:'Getting ready for Beta Build 1', when:'21 Oct'/);
  assert.ok(internal.includes('All sixteen milestones of The Long Run are finished.'));
});
// Beta Prep 1 (25 Oct 2026) is a literal update that REPLACES Alpha Build 5 as the build you are on, not a note inside it.
const updatesSource = internal.slice(internal.indexOf('var UPDATES = ['), internal.indexOf('\n];', internal.indexOf('var UPDATES = [')) + 3);
const updates = vm.runInNewContext(updatesSource + '\nUPDATES');
const ab5Entry = updates.find(u => u.phase === 5 && !u.prep);
const prep1Entry = updates.find(u => u.phase === 5 && u.prep === 1);
check('Beta Prep 1 is the current build: its own entry, name, code and version', () => {
  assert.equal(constant('PrepNumber'), 1);
  assert.match(buildInfo, /public const string Version = "0\.5\.1";/);
  assert.match(buildInfo, /PrepNameOf\(int prep\) => "Beta Prep " \+ prep/);
  assert.match(buildInfo, /PrepCodeOf\(int prep\) => "BP" \+ prep/);
  assert.ok(prep1Entry, 'no Updates entry with prep:1');
  assert.equal(updates.indexOf(prep1Entry), updates.indexOf(ab5Entry) + 1);
  assert.ok(prep1Entry.short && prep1Entry.summary && prep1Entry.notes.length >= 5);
});
check('Beta Prep 1 opens like Alpha Build 5: a pressable card listing its changes, each dated and explained', () => {
  const items = prep1Entry.milestones;
  assert.equal(prep1Entry.unit, 'changes');
  assert.equal(items.length, 8);
  for (const m of items) {
    assert.ok(m.n && m.t && m.d, 'every change has a label, a title and a description');
    assert.match(m.when, /^2[1-4] Oct$/);
    assert.ok(m.d.length > 120, m.t + ' needs a real description');
  }
  const days = items.map(m => Number(m.when.slice(0, 2)));
  assert.deepEqual(Array.from(days), Array.from(days).sort((a, b) => a - b));
  // The card is the same <details> hero as Alpha Build 5's, and a prep counts all its items as done.
  assert.match(internal, /var done = CURRENT\.prep \? ms\.length : Math\.max\(0, Math\.min\(BUILD\.milestonesDone \| 0, ms\.length\)\);/);
  assert.match(internal, /var heroTag = ms\.length \? 'details' : 'div';/);
  assert.equal(ab5Entry.unit, undefined);
});
check('Alpha Build 5 is left whole under Earlier builds: 16 milestones, dated 21 Oct 2026, no Beta Prep notes', () => {
  assert.equal(ab5Entry.when, '21 Oct 2026');
  assert.equal(ab5Entry.milestones.length, 16);
  assert.ok(ab5Entry.notes.every(g => !/Beta Prep/.test(g.h)));
  assert.equal(constant('PhaseNumber'), 5);
  assert.equal(constant('PhaseShipped'), 5);
  assert.equal(constant('Milestones'), 16);
  assert.equal(constant('MilestonesDone'), 16);
});
check('the dates are the verified ones (AB5 21 Oct 2026; the prep made 25 Oct 2026, its changes 21 to 24 Oct)', () => {
  assert.equal(ab5.when, '21 Oct 2026');
  assert.equal(prep1Entry.when, '25 Oct 2026');
  const headings = prep1Entry.notes.map(g => g.h);
  assert.deepEqual(Array.from(headings, h => h.replace(/ — .*$/, '')), ['About this update', 'Prism 2, the look', 'Privacy and security', 'Everyday use', 'Under the hood', 'The Collection', 'Not in this update']);
  for (const h of headings.slice(0, -1)) assert.match(h, / — (\d{1,2} (to \d{1,2} )?October 2026)$/);
  assert.ok(headings.includes('About this update — 25 October 2026'));
  assert.ok(headings.includes('Prism 2, the look — 23 October 2026'));
  assert.ok(headings.includes('Everyday use — 22 October 2026'));
  assert.ok(headings.includes('The Collection — 24 October 2026'));
});
check('the landing page tells the same Beta Prep 1 story as the app: version, day, and the eight changes with the app\'s titles and dates', () => {
  assert.equal(bp1.version, buildInfo.match(/public const string Version = "([^"]+)";/)[1]);
  assert.equal(ab5.version, '0.5.0');
  assert.equal(bp1.when, prep1Entry.when);
  assert.equal(bp1.unit, 'changes');
  assert.equal(bp1.milestones.length, prep1Entry.milestones.length);
  bp1.milestones.forEach((m, i) => {
    const a = prep1Entry.milestones[i];
    assert.equal(m.n, a.n);
    assert.equal(m.t, a.t);
    assert.equal(m.when, a.when);
    assert.ok(m.d.length > 60 && m.d.length < a.d.length, m.t + ': the landing line is a short version of the app\'s account');
  });
  assert.ok(bp1.blurb && bp1.notIn && bp1.desc && bp1.bits.length >= 6);
});
console.log(passed + ' passed');
