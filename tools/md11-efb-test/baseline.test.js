'use strict';
const { test } = require('node:test');
const assert = require('node:assert');
const { load, scrape } = require('./run');

test('the seven nav tabs come first, the active one marked (current page)', () => {
  const tabs = scrape('options-general').slice(0, 7);
  assert.deepStrictEqual(tabs.map(e => e.kind), Array(7).fill('tab'));
  assert.deepStrictEqual(tabs.map(e => e.text), ['Dispatch', 'Payload', 'Perf', 'Charts', 'Services', 'State', 'Options (current page)']);
  assert.ok(tabs.every(e => e.clickable));
});

test('the brightness slider is a range carrying its bounds', () => {
  const r = scrape('options-general').find(e => e.controlType === 'range');
  assert.equal(r.text, 'Screen Brightness');
  assert.equal(r.value, '100');
  assert.equal(r.min, 10);
  assert.equal(r.max, 100);
});

test('a second scrape with no DOM change answers unchanged:true', () => {
  const { A } = load('options-general');
  A.scrape();
  assert.deepStrictEqual(JSON.parse(A.scrape()), { ok: true, unchanged: true });
});

test('hidden text inside a button is not read (Services "Closed", never "Closed0")', () => {
  const els = scrape('services-locked');
  assert.ok(els.some(e => e.kind === 'button' && /Closed$/.test(e.text)));
  assert.ok(!els.some(e => /Closed0/.test(e.text)));
});

test('a field whose caption sits several wrappers up is still named (Perf "Runway")', () => {
  const els = scrape('perf-landing');
  assert.ok(els.some(e => /^Runway/.test(e.text)));
});

test('capture support: findRoot and isVisible exist for tools/coherent.ps1', () => {
  const { A, document } = load('charts-signedout');
  assert.equal(A.findRoot(), document.getElementById('MSFS_REACT_MOUNT'));
  assert.equal(A.isVisible(document.querySelector('h1')), true);
  assert.equal(A.isVisible(document.querySelector('.hidden') || document.createElement('i')), false);
});
