'use strict';
const { test } = require('node:test');
const assert = require('node:assert');
const { load } = require('./run');

// A press must reach the page's onClick EXACTLY ONCE. Until 2026-09-06 A.click dispatched a
// synthetic bubbling 'click' MouseEvent and THEN called el.click(), so React ran every handler
// twice per press. It stayed invisible because this EFB's handlers close over captured state (a
// door tile toggles to the state it captured at render, so twice lands where once does), but a
// handler reading live state or counting presses would have stepped twice for one press.
function counters(el, types) {
  const seen = {};
  for (const t of types) {
    seen[t] = 0;
    el.addEventListener(t, (function (k) { return function () { seen[k]++; }; })(t));
  }
  return seen;
}

const SEQUENCE = ['pointerdown', 'mousedown', 'pointerup', 'mouseup', 'click'];

test('one clickElement fires exactly one click and one mousedown on the target', () => {
  const { A, document } = load('services-ground');
  const gpu = JSON.parse(A.scrape()).elements.find(e => e.text === 'GPU: Disconnect');
  assert.ok(gpu && gpu.clickable, 'the GPU tile is read as a clickable button');

  const btn = document.querySelector('[data-md11-efb-idx="' + gpu.idx + '"]');
  assert.equal(btn.tagName, 'BUTTON');
  const seen = counters(btn, SEQUENCE);

  assert.equal(A.clickElement(gpu.idx), true);
  assert.equal(seen.click, 1, 'exactly one click event');
  assert.equal(seen.mousedown, 1, 'exactly one mousedown');
  assert.deepStrictEqual(seen, { pointerdown: 1, mousedown: 1, pointerup: 1, mouseup: 1, click: 1 });
});

test('picking a choice-group option presses that option button exactly once', () => {
  const { A, document } = load('options-general');
  const units = JSON.parse(A.scrape()).elements.find(e => e.text === 'Weight Units');
  assert.ok(units, 'the Weight Units dropdown is read');

  const group = document.querySelector('[data-md11-efb-idx="' + units.idx + '"]');
  const metric = [...group.children].find(b => b.textContent.trim() === 'Metric');
  const seen = counters(metric, SEQUENCE);

  assert.equal(A.setValue(units.idx, 'Metric'), true);
  assert.equal(seen.click, 1, 'exactly one click event');
  assert.equal(seen.mousedown, 1, 'exactly one mousedown');
});
