'use strict';
// Tests for the SHIPPING EFB shell — the PageHtml verbatim string inside
// MSFSBlindAssist/Forms/FBWA380/FbwEfbForm.cs, the one document every EFB window (the flyPad, both
// PMDG tablets and the MD-11 EFB) actually renders. The string is extracted from the C# source,
// loaded under jsdom and driven through window.__render exactly as the form does, so a change to
// the shipping script is what these tests see. flypad-shell.html (reconcile.test.js) is a
// hand-maintained reconcile spec with no live region and no click announce; it cannot stand in
// for the announce behaviour pinned here.

const test = require('node:test');
const assert = require('node:assert');
const fs = require('fs');
const path = require('path');
const { JSDOM } = require('jsdom');

const FORM = path.join(__dirname, '..', '..', 'MSFSBlindAssist', 'Forms', 'FBWA380', 'FbwEfbForm.cs');

// Pull the PageHtml verbatim string out of the C# source: "" is the verbatim escape for one
// quote, and the two {{...}} placeholders are what BuildPageHtml fills at runtime.
function shippingShellHtml() {
  const src = fs.readFileSync(FORM, 'utf8');
  const marker = 'PageHtml = @"';
  const start = src.indexOf(marker);
  assert.ok(start >= 0, 'PageHtml verbatim string not found in FbwEfbForm.cs');
  const open = start + marker.length;
  const close = src.indexOf('</html>";', open);
  assert.ok(close > open, 'PageHtml verbatim string has no </html>"; terminator');
  return src.slice(open, close + '</html>'.length)
    .replace(/""/g, '"')
    .replace(/\{\{TITLE\}\}/g, 'EFB')
    .replace(/\{\{NOUN\}\}/g, 'EFB');
}

function loadShell() {
  const html = shippingShellHtml();
  const dom = new JSDOM(html, { runScripts: 'outside-only' });
  const { window } = dom;
  const posted = [];
  window.chrome = { webview: { postMessage(m) { posted.push(m); }, addEventListener() {}, removeEventListener() {} } };
  const scripts = [...html.matchAll(/<script>([\s\S]*?)<\/script>/g)].map((m) => m[1]);
  assert.strictEqual(scripts.length, 1, 'expected exactly one inline <script> in PageHtml');
  // runScripts:'outside-only' gives window.eval the jsdom window's OWN realm, so the shell's bare
  // `document`/`window`/`setTimeout` resolve there. The MD-11 reader harness (tools/md11-efb-test/
  // run.js) documents the other case — a jsdom built WITHOUT runScripts hands back Node's own eval,
  // where those bare identifiers resolve against the Node global instead — so the Node globals are
  // pointed at this window too. Harmless when the realm eval is in force, and the difference stops
  // being a silent nothing-defined failure if a jsdom upgrade changes which one applies.
  global.window = window;
  global.document = window.document;
  window.eval(scripts[0]);
  assert.strictEqual(typeof window.__render, 'function', 'shell did not define window.__render');
  return {
    window,
    posted,
    render(page, items) { window.__render({ page, items }); },
    buttonByLabel(prefix) {
      return [...window.document.querySelectorAll('#list button')].find((b) => b.textContent.indexOf(prefix) === 0);
    },
    // What the aria-live region holds — the audible channel. announce() clears the region and
    // writes the text 30 ms later, so the read waits past that timer.
    async spoken() { await new Promise((r) => setTimeout(r, 80)); return window.document.getElementById('live').textContent; },
  };
}

// Items in the shape BuildRenderJson posts.
const btn = (idx, text, extra) => Object.assign({ idx, kind: 'button', controlType: '', text, clickable: true, level: 0, live: '', disabled: false }, extra || {});
const tab = (idx, text) => ({ idx, kind: 'tab', controlType: '', text, clickable: true, level: 0, live: '', disabled: false });

test('a control flagged announceChange speaks its new label after the pilot\'s own press', async () => {
  const s = loadShell();
  s.render('Perf', [btn(5, 'Runway next (now 06L)', { announceChange: true })]);
  assert.strictEqual(await s.spoken(), 'EFB page: Perf');
  const b = s.buttonByLabel('Runway next');
  assert.ok(b, 'stepper button not rendered');
  b.focus(); b.click();
  assert.strictEqual(await s.spoken(), 'Activating Runway next (now 06L)');
  assert.deepStrictEqual(s.posted, [JSON.stringify({ type: 'click', idx: '5' })]);
  // The next scrape shows the field one step on: patched in place (focus kept) and spoken.
  s.render('Perf', [btn(5, 'Runway next (now 06R)', { announceChange: true })]);
  assert.strictEqual(s.buttonByLabel('Runway next'), b, 'stepper button was rebuilt instead of patched');
  assert.strictEqual(b.textContent, 'Runway next (now 06R)', 'visible label did not update');
  assert.strictEqual(await s.spoken(), 'Runway next (now 06R)');
});

// The MD-11 door/GPU/chocks tiles carry their state after a colon, and the flip a press produces is
// the OUTCOME the pilot asked for — the same class as the stepper, and flagged the same way.
test('a flagged tile speaks the state its own press produced', async () => {
  const s = loadShell();
  s.render('Services', [btn(9, 'Passenger 1L: Closed', { announceChange: true })]);
  assert.strictEqual(await s.spoken(), 'EFB page: Services');
  const door = s.buttonByLabel('Passenger 1L');
  door.focus(); door.click();
  assert.strictEqual(await s.spoken(), 'Activating Passenger 1L: Closed');
  s.render('Services', [btn(9, 'Passenger 1L: Open', { announceChange: true })]);
  assert.strictEqual(s.buttonByLabel('Passenger 1L'), door, 'tile was rebuilt instead of patched');
  assert.strictEqual(await s.spoken(), 'Passenger 1L: Open');
});

test('a control without the flag stays silent when its label changes after a press', async () => {
  const s = loadShell();
  // The PMDG tablet's nav bar: the pressed tab gains ' (current page)' on the next scrape.
  s.render('Dashboard', [tab(1, 'Dashboard (current page)'), tab(2, 'Performance'), btn(7, 'Baggage')]);
  assert.strictEqual(await s.spoken(), 'EFB page: Dashboard');
  const perf = s.buttonByLabel('Performance');
  perf.focus(); perf.click();
  assert.strictEqual(await s.spoken(), 'Activating Performance');
  s.render('Dashboard', [tab(1, 'Dashboard'), tab(2, 'Performance (current page)'), btn(7, 'Baggage')]);
  assert.strictEqual(s.buttonByLabel('Performance'), perf, 'tab was rebuilt instead of patched');
  assert.strictEqual(perf.textContent, 'Performance (current page)', 'visible label did not update');
  assert.strictEqual(await s.spoken(), 'Activating Performance', 'the press echo "(current page)" was announced');
  // The flyPad's service tiles: the pressed tile gains ' (called)'.
  const bag = s.buttonByLabel('Baggage');
  bag.focus(); bag.click();
  assert.strictEqual(await s.spoken(), 'Activating Baggage');
  s.render('Dashboard', [tab(1, 'Dashboard'), tab(2, 'Performance (current page)'), btn(7, 'Baggage (called)')]);
  assert.strictEqual(s.buttonByLabel('Baggage'), bag, 'tile was rebuilt instead of patched');
  assert.strictEqual(bag.textContent, 'Baggage (called)', 'visible label did not update');
  assert.strictEqual(await s.spoken(), 'Activating Baggage', 'the press echo "(called)" was announced');
});
