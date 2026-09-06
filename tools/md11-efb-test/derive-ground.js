'use strict';
// Derives the on-ground Services/State fixtures from the in-flight (locked) captures:
// GroundPage renders identical children; only the overlay and the inert wrapper differ.
// Re-run after re-capturing the locked pages: node derive-ground.js
//
// The wrapper swap is NOT a faithful render of the ground page. Live, GroundPage drops the
// wrapper entirely on the ground — it returns a bare Fragment, so there is no
// `pointer-events-auto` div at all; these fixtures keep one purely so the derived HTML stays
// structurally parallel to the capture it came from. Nothing in the reader keys on
// `pointer-events-auto` (only on `pointer-events-none`, and only where it wraps children), so
// the extra div changes no reading. Do not start keying a rule on it.
const fs = require('fs');
const path = require('path');
const F = path.join(__dirname, 'fixtures');
const OVERLAY = /<div class="absolute left-1\/2 top-1\/2[^"]*"[^>]*><h1[^>]*>This page cannot be used right now<\/h1><p[^>]*>Please try again when you're on the ground<\/p><\/div>/;
for (const [src, dst] of [['services-locked', 'services-ground'], ['state-locked', 'state-ground']]) {
  let h = fs.readFileSync(path.join(F, src + '.html'), 'utf8');
  if (!OVERLAY.test(h) || !h.includes('opacity-10 pointer-events-none')) throw new Error(src + ': overlay not found');
  h = h.replace(OVERLAY, '').replace('opacity-10 pointer-events-none', 'pointer-events-auto');
  fs.writeFileSync(path.join(F, dst + '.html'), '<!-- Derived from ' + src + '.html by derive-ground.js: the same page on the ground. -->\n' + h);
}
console.log('derived services-ground.html, state-ground.html');
