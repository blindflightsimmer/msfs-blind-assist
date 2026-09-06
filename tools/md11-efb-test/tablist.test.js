'use strict';
const { test } = require('node:test');
const assert = require('node:assert');
const { load, scrape } = require('./run');

test('the Options section strip reads in natural casing with the underlined section (selected)', () => {
  const strip = scrape('options-systems').filter(e => e.kind === 'tab').slice(7).map(e => e.text);
  assert.deepStrictEqual(strip, ['General', 'Systems (selected)', 'CAWS', 'Perf', 'Comms', 'Behavior']);
});

test('section tabs are clickable and stamped so a press reaches the EFB', () => {
  const { A, document } = load('options-general');
  const tab = JSON.parse(A.scrape()).elements.filter(e => e.kind === 'tab')[8];   // "Systems"
  assert.ok(tab.clickable);
  const node = document.querySelector('[data-md11-efb-idx="' + tab.idx + '"]');
  assert.equal(node.tagName, 'BUTTON');
  assert.equal(node.textContent.trim(), 'Systems');
});

test('a button the EFB styles uppercase reads in its natural casing ("Save", not "SAVE")', () => {
  const els = scrape('options-general');
  assert.ok(els.some(e => e.kind === 'button' && e.text === 'Save'));
  assert.ok(!els.some(e => e.text === 'SAVE'));
});

test('stale idx stamps from an earlier scrape are cleared before restamping', () => {
  const { A, document } = load('options-general');
  const root = document.getElementById('MSFS_REACT_MOUNT');
  const ghost = document.createElement('span');          // hidden: no data-vis
  ghost.setAttribute('data-md11-efb-idx', '3');
  root.insertBefore(ghost, root.firstChild);
  A.scrape();
  assert.equal(ghost.hasAttribute('data-md11-efb-idx'), false);
  assert.equal(document.querySelectorAll('[data-md11-efb-idx="3"]').length, 1);
});
