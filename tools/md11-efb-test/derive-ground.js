'use strict';
// Derives the on-ground Services/State fixtures from the in-flight (locked) captures:
// GroundPage renders identical children; only the overlay and the pointer-events-none wrapper
// differ. Re-run after re-capturing the locked pages: node derive-ground.js
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
