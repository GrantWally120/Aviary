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
// Beta Prep 2 (21 Nov 2026) is the current build, so it is what the landing page's hero, What's new and badge follow.
// Beta Prep 1 (25 Oct 2026) stays before it, finished.
const bp1 = phases.flatMap(p => p.builds).find(b => b.code === 'BP1');
const bp2 = phases.flatMap(p => p.builds).find(b => b.code === 'BP2');
check('Beta Prep 2 is the current build on the landing page, after a finished Beta Prep 1', () => {
  assert.equal(current(phases), 'BP2');
  assert.equal(bp1.state, 'done');
  assert.equal(bp2.state, 'done');
  assert.deepEqual(Array.from(phases[1].builds, b => b.code), ['BP1', 'BP2', 'BB1', 'BB2']);
});
check('alpha card initially selects the completed AB5', () => assert.equal(selected(phases[0]), 'AB5'));
check('Beta Build 1 and 2 remain planned', () => assert.ok(phases[1].builds.filter(b => !b.code.startsWith('BP')).every(b => b.state === 'todo')));
check('the beta card initially selects the current build, Beta Prep 2', () => assert.equal(selected(phases[1]), 'BP2'));
check('a later active build takes precedence', () => {
  const future = JSON.parse(JSON.stringify(phases));
  future[1].builds.find(b => b.code === 'BB1').state = 'active';
  assert.equal(current(future), 'BB1');
});
check('a later prep takes over as the current build', () => {
  const future = JSON.parse(JSON.stringify(phases));
  future[1].builds.splice(2, 0, {code:'BP3', title:'Beta Prep 3', state:'done', when:'1 Dec 2026', bits:[]});
  assert.equal(current(future), 'BP3');
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
const prep2Entry = updates.find(u => u.phase === 5 && u.prep === 2);
const version = buildInfo.match(/public const string Version = "([^"]+)";/)[1];
check('Beta Prep 2 is the current build: its own entry after Beta Prep 1, name, code and version', () => {
  assert.equal(constant('PrepNumber'), 2);
  assert.equal(version, '0.5.2');
  assert.ok(prep2Entry, 'no Updates entry with prep:2');
  assert.equal(updates.indexOf(prep2Entry), updates.indexOf(prep1Entry) + 1);
  assert.equal(prep2Entry.when, '21 Nov 2026');
  assert.ok(prep2Entry.short && prep2Entry.summary && prep2Entry.notes.length >= 5);
});
check('Beta Prep 2 opens like the builds before it: nine changes, each dated 21 Nov and explained', () => {
  const items = prep2Entry.milestones;
  assert.equal(prep2Entry.unit, 'changes');
  assert.equal(items.length, 9);
  for (const m of items) {
    assert.ok(m.n && m.t && m.d, 'every change has a label, a title and a description');
    assert.equal(m.when, '21 Nov');
    assert.ok(m.d.length > 120, m.t + ' needs a real description');
  }
  const headings = prep2Entry.notes.map(g => g.h);
  assert.equal(headings[0], 'About this update — 21 November 2026');
  assert.equal(headings[headings.length - 1], 'Not in this update');
  for (const h of headings.slice(0, -1)) assert.match(h, / — 21 November 2026$/);
});
check('the landing page tells the same Beta Prep 2 story as the app: version, day, and the nine changes with the app\'s titles and dates', () => {
  assert.equal(bp2.version, version);
  assert.equal(bp2.when, prep2Entry.when);
  assert.equal(bp2.unit, 'changes');
  assert.equal(bp2.milestones.length, prep2Entry.milestones.length);
  bp2.milestones.forEach((m, i) => {
    const a = prep2Entry.milestones[i];
    assert.equal(m.n, a.n);
    assert.equal(m.t, a.t);
    assert.equal(m.when, a.when);
    assert.ok(m.d.length > 60 && m.d.length < a.d.length, m.t + ': the landing line is a short version of the app\'s account');
  });
  assert.ok(bp2.blurb && bp2.notIn && bp2.desc && bp2.bits.length >= 6);
});
// THE RULE (Grant, 21 Nov 2026): the Privacy Policy is read against every build, and changed in the same
// build as anything that starts to store or send something. A build cannot be made on an unread policy.
const policySource = fs.readFileSync(path.join(root, 'src/Aviary/PrivacyPolicy.cs'), 'utf8');
const legalSource = fs.readFileSync(path.join(root, 'src/Aviary/LegalTexts.cs'), 'utf8');
const policyRevision = policySource.match(/public const string Revision = "([^"]+)";/)[1];
check('the Privacy Policy was read against the version being built', () => {
  const reviewed = policySource.match(/public const string ReviewedForVersion = "([^"]+)";/)[1];
  assert.equal(reviewed, version, 'PrivacyPolicy.ReviewedForVersion must be this build: read the policy against it, then set it');
});
check('the Privacy Policy lists its changes, the newest dated its revision, and the published copy says the same', () => {
  const changes = policySource.slice(policySource.indexOf('public static readonly string[] Changes'));
  const newest = changes.match(/"([^"]+)"/)[1];
  assert.ok(newest.startsWith(policyRevision + ','), 'the newest entry of PrivacyPolicy.Changes must start with the revision date');
  const published = fs.readFileSync(path.join(root, 'PRIVACY.md'), 'utf8');
  assert.ok(published.includes('Revision: ' + policyRevision));
  assert.ok(published.includes('read against Aviary ' + version));
});
check('the Terms of Use and the Copyright notice are published at the revisions the app asks people to agree to', () => {
  const termsRevision = legalSource.match(/public const string TermsRevision = "([^"]+)";/)[1];
  const copyrightRevision = legalSource.match(/public const string CopyrightRevision = "([^"]+)";/)[1];
  assert.ok(fs.readFileSync(path.join(root, 'TERMS.md'), 'utf8').includes('Revision: ' + termsRevision));
  assert.ok(fs.readFileSync(path.join(root, 'COPYRIGHT.md'), 'utf8').includes('Revision: ' + copyrightRevision));
  // The screen compares the revision, so the Terms cannot change without everybody being asked again.
  assert.match(legalSource, /string\.Equals\(AgreedRevision, LegalTexts\.TermsRevision, StringComparison\.Ordinal\)/);
});
check('the look is called Prism 2 wherever people read it, and an old saved name still opens it', () => {
  const start = fs.readFileSync(path.join(root, 'src/Aviary/StartPage.cs'), 'utf8');
  const looks = fs.readFileSync(path.join(root, 'src/Aviary/Looks.cs'), 'utf8');
  // The only places the old words may remain: the note that says it was renamed, in the app and on the landing page.
  const strip = text => text.replace(/Liquid Glass is called Prism 2/g, '').replace(/A saved Liquid Glass opens as Prism 2/g, '')
    .replace(/"Liquid Glass" is "Prism 2"/g, '');
  assert.ok(!/Liquid Glass/.test(strip(internal)), 'InternalPages.cs still says Liquid Glass');
  assert.ok(!/Liquid Glass/.test(strip(landing)), 'Aviary.html still says Liquid Glass');
  assert.ok(!/Liquid Glass/.test(start));
  assert.match(looks, /FormerGlassName = "Liquid Glass"/);
  assert.match(internal, /key:'look', opts:\['Classic','Prism 2'\]/);
});
check('Beta Prep 1 is left whole under Earlier builds: its own entry, name and code', () => {
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
  assert.equal(bp1.version, '0.5.1');
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
