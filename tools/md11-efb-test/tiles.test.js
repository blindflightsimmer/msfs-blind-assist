'use strict';
const { test } = require('node:test');
const assert = require('node:assert');
const { load, scrape, lines } = require('./run');

test('Services tiles read their name with the action', () => {
  const els = scrape('services-ground');
  const btns = els.filter(e => e.kind === 'button').map(e => e.text);
  assert.deepStrictEqual(btns, ['Passenger 1L: Closed', 'Passenger 1R: Closed', 'Cargo Main: Closed', 'Cargo 1R: Closed',
    'Cargo 2R: Closed', 'Bulk Cargo: Closed', 'Nose Weight: Set', 'GPU: Connect', 'ASU: Connect', 'Wheel Chocks: Set']);
  assert.ok(!els.some(e => e.kind === 'static' && e.text === 'Passenger 1L'), 'the name is not read a second time');
  assert.ok(els.filter(e => e.kind === 'button').every(e => !e.disabled), 'on the ground nothing is dimmed');
});

test('State tiles: the load button, then the default marker or the set-default button', () => {
  const ls = lines(scrape('state-ground')).slice(7);
  assert.deepStrictEqual(ls, ['button|Cold and Dark', 'static|Cold and Dark is the default',
    'button|Ready to Start', 'button|Ready to Start: Set as default',
    'button|Ready to Fly', 'button|Ready to Fly: Set as default',
    'button|Load Last Save', 'button|Load Last Save: Set as default']);
});

test('a tile button is stamped on the EFB button itself', () => {
  const { A, document } = load('services-ground');
  const gpu = JSON.parse(A.scrape()).elements.find(e => e.text === 'GPU: Connect');
  const node = document.querySelector('[data-md11-efb-idx="' + gpu.idx + '"]');
  assert.equal(node.tagName, 'BUTTON');
  assert.equal(node.textContent.trim(), 'Connect');
});
