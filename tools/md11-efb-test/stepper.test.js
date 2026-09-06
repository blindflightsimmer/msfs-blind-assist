'use strict';
const { test } = require('node:test');
const assert = require('node:assert');
const { load, scrape, lines } = require('./run');

test('a stepper whose option list is readable is one dropdown with the full list', () => {
  const els = scrape('perf-landing');
  const sels = els.filter(e => e.controlType === 'select').map(e => [e.text, e.options, e.value]);
  assert.deepStrictEqual(sels.slice(0, 2), [
    ['Runway', ['RW06L', 'RW06R', 'RW07L', 'RW07R', 'RW24L', 'RW24R', 'RW25L', 'RW25R'], 'RW06L'],
    ['Runway Condition', ['DRY', 'WET', 'CONTAMINATED'], 'DRY']]);
  assert.ok(sels.some(s => s[0] === 'Autobrake' && s[2] === 'MIN' && s[1].join() === 'MIN,MED,MAX'));
  assert.ok(sels.some(s => s[0] === 'Reversers' && s[2] === 'ALL' && s[1].join() === 'ALL,WING,TAIL,NONE'));
  assert.ok(!els.some(e => /previous|next/.test(e.text)), 'no arrow buttons remain');
  assert.ok(!els.some(e => e.controlType === 'text' && e.text === 'Runway'), 'the locked field is not an edit box');
});

test('without a readable option list the stepper reads its value and keeps the two arrow buttons', () => {
  const ls = lines(scrape('perf-stepper-fallback', { autoVis: true, nav: 'Perf' })).slice(7);
  assert.deepStrictEqual(ls, ['static|Runway: RW06L', 'button|Runway previous (now RW06L)', 'button|Runway next (now RW06L)']);
});

function wire(input, opts, counts) {
  // The EFB applies a press on the NEXT tick (React 18 flushes after the event, live-verified
  // 2026-09-05: the DOM read inline after a press still showed the old value). Emulate that.
  let i = opts.indexOf(input.value);
  const [up, down] = Array.from(input.parentElement.getElementsByTagName('button'));
  down.addEventListener('mousedown', () => { counts.down++; queueMicrotask(() => { i = Math.min(i + 1, opts.length - 1); input.value = opts[i]; }); });
  up.addEventListener('mousedown', () => { counts.up++; queueMicrotask(() => { i = Math.max(i - 1, 0); input.value = opts[i]; }); });
}

test('picking a later value presses the down arrow until the field shows it, then back up', async () => {
  const { A, document } = load('perf-landing');
  A.STEP_DELAY_MS = 0;
  const sel = JSON.parse(A.scrape()).elements.find(e => e.controlType === 'select' && e.text === 'Autobrake');
  const input = document.querySelector('[data-md11-efb-idx="' + sel.idx + '"]');
  assert.equal(input.tagName, 'INPUT');
  const counts = { up: 0, down: 0 };
  wire(input, ['MIN', 'MED', 'MAX'], counts);
  const ok = await new Promise(res => assert.equal(A.setValue(String(sel.idx), 'MAX', res), true));
  assert.equal(ok, true);
  assert.deepStrictEqual(counts, { up: 0, down: 2 });
  assert.equal(input.value, 'MAX');
  const back = await new Promise(res => A.setValue(String(sel.idx), 'MIN', res));
  assert.equal(back, true);
  assert.deepStrictEqual(counts, { up: 2, down: 2 });
  assert.equal(input.value, 'MIN');
});

test('a press that does not move the field stops the walk instead of spinning', async () => {
  const { A, document } = load('perf-landing');
  A.STEP_DELAY_MS = 0;
  const sel = JSON.parse(A.scrape()).elements.find(e => e.controlType === 'select' && e.text === 'Reversers');
  const input = document.querySelector('[data-md11-efb-idx="' + sel.idx + '"]');
  let presses = 0;
  for (const b of input.parentElement.getElementsByTagName('button')) b.addEventListener('mousedown', () => presses++);
  const ok = await new Promise(res => A.setValue(String(sel.idx), 'NONE', res));
  assert.equal(ok, false);
  assert.equal(presses, 1);
});

test('an unknown value is refused before any press', () => {
  const { A, document } = load('perf-landing');
  const sel = JSON.parse(A.scrape()).elements.find(e => e.controlType === 'select' && e.text === 'Runway');
  const input = document.querySelector('[data-md11-efb-idx="' + sel.idx + '"]');
  let presses = 0;
  for (const b of input.parentElement.getElementsByTagName('button')) b.addEventListener('mousedown', () => presses++);
  assert.equal(A.setValue(String(sel.idx), 'RW99X'), false);
  assert.equal(presses, 0);
});
