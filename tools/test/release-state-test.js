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
check('completed alpha remains current', () => assert.equal(current(phases), 'AB5'));
check('alpha card initially selects the completed AB5', () => assert.equal(selected(phases[0]), 'AB5'));
check('beta remains planned', () => assert.ok(phases[1].builds.every(b => b.state === 'todo')));
check('planned beta card initially selects its first build', () => assert.equal(selected(phases[1]), 'BB1'));
check('a later active build takes precedence', () => {
  const future = JSON.parse(JSON.stringify(phases));
  future[1].builds[0].state = 'active';
  assert.equal(current(future), 'BB1');
});
check('M9 appears with its date in the actual Updates data', () => {
  assert.match(internal, /\{n:'M9', t:'Getting ready for Beta Build 1', when:'21 Oct'/);
  assert.ok(internal.includes('All sixteen milestones of The Long Run are finished.'));
});
console.log(passed + ' passed');
