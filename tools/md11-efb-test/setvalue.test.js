'use strict';
const { test } = require('node:test');
const assert = require('node:assert');
const { load } = require('./run');

test('typing into a field commits like a keyboard: input, change, then blur/focusout for the EFB\'s onBlur', () => {
  const { A, document } = load('perf-landing');
  const wind = JSON.parse(A.scrape()).elements.find(e => e.controlType === 'text' && e.text === 'Wind');
  const input = document.querySelector('[data-md11-efb-idx="' + wind.idx + '"]');
  const seen = [];
  for (const ev of ['input', 'change', 'blur', 'focusout']) input.addEventListener(ev, () => seen.push(ev + ':' + input.value));
  assert.equal(A.setValue(String(wind.idx), '270/10'), true);
  assert.deepStrictEqual(seen, ['input:270/10', 'change:270/10', 'blur:270/10', 'focusout:270/10']);
});
