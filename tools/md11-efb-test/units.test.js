'use strict';
const { test } = require('node:test');
const assert = require('node:assert');
const { load, scrape } = require('./run');

test('a field carries the unit box beside it in its name', () => {
  const els = scrape('perf-landing');
  const fields = els.filter(e => e.controlType === 'text').map(e => e.text);
  for (const n of ['Temperature (°C)', 'Pressure (inHg)', 'Landing Weight (lb)']) assert.ok(fields.includes(n), n + ' in ' + fields.join('; '));
  for (const u of ['°C', 'inHg', 'lb']) assert.ok(!els.some(e => e.kind === 'static' && e.text === u), u + ' read as a loose line');
});

test('a row of plain spans reads as one line, with a space before the unit', () => {
  const statics = scrape('perf-landing').filter(e => e.kind === 'static').map(e => e.text);
  assert.ok(statics.includes('Slope 0.1%'), statics.join('; '));
  assert.ok(statics.includes('Headwind 0 KT'), statics.join('; '));
  for (const frag of ['Slope', 'Headwind', '0KT', '0.1%']) assert.ok(!statics.includes(frag), frag + ' read alone');
});

test('spaceUnit separates a trailing letter unit from a number, and nothing else', () => {
  const { A } = load('charts-signedout');
  const cases = { '0KT': '0 KT', '----ft': '---- ft', '7587ft': '7587 ft', '0.1%': '0.1%', 'N/A': 'N/A', 'RW06L': 'RW06L', '34000 ft': '34000 ft', 'MIN': 'MIN' };
  for (const [i, o] of Object.entries(cases)) assert.equal(A.spaceUnit(i), o, i);
});
