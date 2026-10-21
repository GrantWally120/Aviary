const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.join(__dirname, '../../src/Aviary/BrowserTab.cs'), 'utf8');
const script = source.match(/private const string PageErrorScript = """\r?\n([\s\S]*?)\r?\n""";/)[1];
const stop = source.match(/private const string PageErrorStopScript = "([^"]+)";/)[1];
const listeners = new Map();
const messages = [];
const window = {
  addEventListener(name, callback) {
    if (!listeners.has(name)) listeners.set(name, new Set());
    listeners.get(name).add(callback);
  },
  removeEventListener(name, callback) { listeners.get(name)?.delete(callback); },
  chrome: { webview: { postMessage(message) { messages.push(JSON.parse(message)); } } }
};
const context = vm.createContext({ window, document: { querySelectorAll() { return [{ type: 'password', value: 'SYNTHETIC_FORM_SECRET' }]; } } });
const fire = (name, event) => { for (const fn of listeners.get(name) || []) fn(event); };
vm.runInContext(script, context);
vm.runInContext(script, context);
assert.equal(listeners.get('error').size, 1);
assert.equal(listeners.get('unhandledrejection').size, 1);
fire('error', { message: 'bad SYNTHETIC_FORM_SECRET', error: { stack: 'stack SYNTHETIC_FORM_SECRET' } });
assert.equal(messages.length, 1);
assert.ok(!JSON.stringify(messages).includes('SYNTHETIC_FORM_SECRET'));
vm.runInContext(stop, context);
assert.equal(listeners.get('error').size, 0);
assert.equal(listeners.get('unhandledrejection').size, 0);
assert.equal(window.__aviaryDiag, undefined);
fire('error', { message: 'should not be collected' });
assert.equal(messages.length, 1);
vm.runInContext(script, context);
assert.equal(listeners.get('error').size, 1);
fire('unhandledrejection', { reason: { message: 'bad SYNTHETIC_FORM_SECRET', stack: 'stack SYNTHETIC_FORM_SECRET' } });
assert.equal(messages.length, 2);
assert.ok(!JSON.stringify(messages).includes('SYNTHETIC_FORM_SECRET'));
for (let i = 0; i < 100; i++) fire('error', { message: 'synthetic error' });
assert.equal(messages.length, 61);
vm.runInContext(stop, context);
assert.equal(listeners.get('error').size, 0);
console.log('PASS diagnostics page script: duplicate prevention, typed-secret masking, stop/restart, 60-event limit');
