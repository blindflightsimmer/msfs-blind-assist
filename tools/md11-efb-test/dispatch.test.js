'use strict';
const { test } = require('node:test');
const assert = require('node:assert');
const { scrape, lines } = require('./run');

test('the Dispatch flight header reads as labelled lines, then the fuel grid, then the six actions', () => {
  assert.deepStrictEqual(lines(scrape('dispatch')).slice(7), [
    'static|Flight BVI2GP', 'static|Aircraft TFDi MD-11F GE (TFDI-MD11)',
    'static|From KMEM', 'static|Block time 03:50 (air 03:22)', 'static|Cruise 34000 ft (CI 20)', 'static|To KLAX',
    'static|Departure 23:35 UTC', 'static|Arrival 3:25 UTC', 'static|Alternate KONT',
    'static|Route CHLDR5 ANSWA DCT LIT DCT FUZ J4 WLVRN DCT ESTWD HLYWD1',
    'static|Block Fuel: 77347 lbs', 'static|Enroute Fuel: 59634 lbs', 'static|Contingency Fuel: 4408 lbs', 'static|Taxi Fuel: 2000 lbs',
    'static|Extra + ETOPS Fuel: 0 lbs', 'static|Alternate Fuel: 7738 lbs', 'static|Reserve Fuel: 3567 lbs', 'static|Estimated ZFW: 430583 lbs',
    'static|Estimated TOW: 505930 lbs', 'static|Estimated LW: 446296 lbs',
    'button|Send flight plan to FMC', 'button|Set payload & fuel', 'button|View departure airport charts',
    'button|View arrival airport charts', 'button|Reload flight plan', 'button|View OFP']);
});

test('the header labelling is Dispatch-only: the same shapes on another page read as before', () => {
  // The Perf page has no such rows, but a stray h2+div pair there must not become "Flight …".
  assert.ok(!lines(scrape('perf-landing')).some(l => /^static\|(Flight|From|To|Route) /.test(l)));
});
