'use strict';
const { test } = require('node:test');
const assert = require('node:assert');
const { scrape } = require('./run');

const isControl = e => e.controlType === 'text' || e.controlType === 'select' || e.controlType === 'range' || e.controlType === 'checkbox' || e.kind === 'button' || e.kind === 'link';

test('a page locked in flight explains itself first, then reads its content with every control dimmed', () => {
  for (const fx of ['payload-locked', 'services-locked', 'state-locked']) {
    const els = scrape(fx).slice(7);
    assert.equal(els[0].kind, 'heading', fx); assert.equal(els[0].text, 'This page cannot be used right now', fx);
    assert.equal(els[1].text, "Please try again when you're on the ground", fx);
    const ctrls = els.slice(2).filter(isControl);
    assert.ok(ctrls.length > 0, fx + ': content still read');
    assert.ok(ctrls.every(e => e.disabled), fx + ': ' + JSON.stringify(ctrls.filter(e => !e.disabled).map(e => e.text)));
  }
});

test('the nav tabs above a locked page are never dimmed', () => {
  assert.ok(scrape('services-locked').slice(0, 7).every(e => !e.disabled));
});

test('on the ground the same controls are live', () => {
  const ctrls = scrape('services-ground').slice(7).filter(isControl);
  assert.ok(ctrls.length > 0);
  assert.ok(ctrls.every(e => !e.disabled));
});
