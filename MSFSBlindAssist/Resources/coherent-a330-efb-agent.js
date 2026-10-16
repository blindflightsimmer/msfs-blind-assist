// iniBuilds A300-600R EFB in-page agent — installed at runtime into the EFB's Coherent GT view
// ("VCockpit18 - iniEfbA300", window.__MSFSBA_A330_EFB). No Community mod, no HTML patching.
//
// The EFB is msfs-sdk FSComponent markup with Bootstrap classes: plain buttons, inputs, divs, NO
// React. Every page is mounted at once under #iniEFB > #renderer, one wrapper per page; the page on
// screen is the wrapper carrying class "visiblePage" (the rest "hiddenPage"). The handlers are plain
// el.onclick / el.onkeyup / el.onmousemove properties, so el.click() and dispatched events reach
// them exactly as the touch screen does.
//
// ES5 ONLY. Coherent GT is Chromium 49: var (no const/let), no arrow functions, no template
// literals, no String.includes / Array.from / Object.assign.
//
// Contract, identical to coherent-pmdg-efb-agent.js / coherent-md11-efb-agent.js so the shared
// FbwEfbForm consumes it unchanged:
//   scrape()            -> JSON string {ok, unchanged?, page, elements:[...]}
//   clickElement(idx)   -> press the element carrying that stamped idx
//   setValue(idx, text) -> type into it / pick a choice / move a slider
// Element: {idx,text,value,controlType,kind,clickable,level,live,disabled,options,min,max,step,key,announceChange}
//   kind: 'button' | 'heading' (+level) | 'static' | 'alert'
//   controlType: 'text' | 'checkbox' | 'select' | 'range'
//
// key / announceChange follow the MD-11 EFB rules exactly (CLAUDE.md, TFDi MD-11 invariants):
//   key            stamped on every element whose LABEL carries changing state, so the shell patches
//                  the node in place instead of rebuilding it under the pilot's focus:
//                    'door:<id>'  'slide:<id>'  'equip:<id>'  'toggle:<id>'  'runway:<id>'
//                    'readout:<id>'  'field:<id>'  'choice:<name>'
//   announceChange ONLY on a control whose own label carries its own NEW state after the press —
//                  a door ("Door 1L: open"), a slide arm, a ground-equipment attach/remove, a
//                  perf-page cycle toggle ("Runway surface: WET"). Never on a navigation tile, a
//                  plain action button, or a control the screen reader has just read.
//
// What cannot be made accessible (said once on the page, never silently dropped):
//   - the enroute map (OpenLayers canvas), the Navigraph chart image, the checklist (an image),
//     the brake/tyre wear indicators (colour only), the weight-and-balance hold diagram.
(function () {
  var A = {};
  A.INSTALLED = 'MSFSBA_A330_EFB_INSTALLED';
  A.ATTR = 'data-a330-efb-idx';
  A.CLAIM = 'data-a330-efb-label';

  // ---------------------------------------------------------------------------------
  // names — the EFB carries almost no accessible names, so the reader brings its own
  // ---------------------------------------------------------------------------------

  // Page wrapper -> spoken page name, keyed by the id of the wrapper's content root.
  A.PAGES = {
    dashboard: 'Dashboard', OFPViewer: 'OFP', groundViewer: 'Ground', panelState: 'Panel State',
    payloadViewer: 'Payload', takeOffPerfViewer: 'Take Off', options: 'Options'
  };

  A.DOORS = {
    left_door_1: 'Door 1L', right_door_1: 'Door 1R', left_door_2: 'Door 2L', right_door_2: 'Door 2R',
    left_door_3: 'Door 3L', right_door_3: 'Door 3R', left_door_4: 'Door 4L', right_door_4: 'Door 4R',
    left_door_cargo: 'Upper cargo door', front_cargo: 'Forward cargo door', rear_cargo: 'Aft cargo door',
    rear_bulk: 'Bulk cargo door'
  };

  A.SLIDES = {
    door1Larmed: 'Door 1L slide', door1Rarmed: 'Door 1R slide', door2Larmed: 'Door 2L slide',
    door2Rarmed: 'Door 2R slide', door3Larmed: 'Door 3L slide', door3Rarmed: 'Door 3R slide',
    door4Larmed: 'Door 4L slide', door4Rarmed: 'Door 4R slide'
  };

  // Attach/Remove buttons: the EFB renders the ACTION ("Attach GPU" / "Remove GPU"); the reader
  // speaks the STATE that action implies ("GPU: not attached").
  // Ground equipment: the button names the ACTION ("Toggle GPU"); the reader speaks the STATE from the
  // aircraft's own L:vars (read with SimVar in the page — the EFB's own JS reads the same ones).
  A.EQUIP_STATE = {
    groundEquip_GPU: { name: 'GPU', lvar: 'L:INI_GPU_AVAIL', on: 'connected', off: 'not connected' },
    groundEquip_CHOCKS: { name: 'Chocks', lvar: 'L:INI_CHOCKS_ENABLED', on: 'placed', off: 'not placed' },
    groundEquip_COVERS: { name: 'Covers', lvar: 'L:INI_COVERS_ENABLED', on: 'on', off: 'off' }
  };
  A.EQUIP = {};

  // Perf-page cycle toggles (input type=button whose VALUE is the setting; a press steps it).
  A.TOGGLES = {
    input_surface: 'Runway surface', input_flaps: 'Flaps/slats', input_aircond: 'Air conditioning',
    input_antiice: 'Anti-ice', input_toga: 'Force TOGA',
    land_input_surface: 'Runway condition', land_input_flaps: 'Configuration', land_input_brake: 'Brakes'
  };

  A.RUNWAY_PICKERS = { show_runway_select: 'Runway', land_show_runway_select: 'Runway' };

  // One key per element id: an object literal keeps only the LAST duplicate, and an A300 leftover
  // 'zfw' entry once renamed the A330's cargo slider "Zero fuel weight". Units come from the
  // page's own unit box (kg or lb, as the EFB is set), never from the name.
  A.FIELDS = {
    pax: 'Passengers', paxText: 'Passengers', zfw: 'Cargo', zfwText: 'Cargo', fuel: 'Fuel', fuelText: 'Fuel',
    zfwDirect: 'Zero fuel weight', zfwDirectText: 'Zero fuel weight', enterZFWText: 'Zero fuel weight',
    paxLights: 'Cabin lights', wxsearchquery: 'METAR airport ICAO', simbrief_ident: 'SimBrief pilot ID',
    weight1: 'Weight units', roa1: 'Throttle 1 reverse on axis', roa2: 'Throttle 2 reverse on axis',
    input_pax: 'Passengers', input_payload: 'Load', weight_fuel: 'Fuel',
    weight_freight_fwd: 'Forward cargo', weight_freight_mid: 'Mid cargo', weight_freight_aft: 'Aft cargo',
    output_maczfw: 'MAC at zero fuel weight', output_maccg: 'MAC at gross weight',
    output_zfw: 'Zero fuel weight', output_tow: 'Takeoff weight', output_payload: 'Payload',
    output_blkfuel: 'Block fuel',
    output_tora: 'TORA', output_heading: 'Runway heading', input_wind: 'Wind (direction/speed)',
    input_temp: 'Outside air temperature', input_alt: 'Altimeter', input_weight: 'Takeoff weight',
    output_flex: 'Flex temperature', output_v1: 'V1', output_vr: 'VR', output_v2: 'V2',
    output_mtow: 'Maximum takeoff weight (performance)', output_gwcg: 'Gross weight CG',
    output_ths: 'Stabilizer trim (THS)',
    land_output_tora: 'Runway length', land_output_heading: 'Runway heading',
    land_input_wind: 'Wind (direction/speed)', land_input_temp: 'Temperature',
    land_input_weight: 'Landing weight', land_input_vls: 'Extra speed',
    land_output_dist: 'Landing distance', land_output_fdist: 'Factored landing distance',
    simbrief: 'SimBrief pilot ID', hoppie: 'Hoppie logon code',
    fuelload: 'Fuel load (kg)', towatch: 'Variable to watch',
    towatch_type: 'Value type', ng_search: 'Airport ICAO',
    brightness: 'Screen brightness', lights_cargo: 'Cargo lighting', fuelLoad: 'Fuel load'
  };

  A.BUTTONS = {
    simbriefDlButtonDash: 'Import from SimBrief', simbriefDlButtonOFP: 'Download OFP from SimBrief', simbriefDlButton: 'Import from SimBrief',
    refreshDeparture: 'Update departure weather', refreshArrival: 'Update arrival weather', wxsearch: 'Search METAR',
    configs_CloseAll: 'Close all doors', configs_LoadingConfig: 'Doors to loading configuration',
    groundEquip_JETWAY: 'Toggle jetway', groundEquip_Catering: 'Toggle catering', groundEquip_Luggage: 'Toggle luggage',
    startLoad_instant: 'Load instantly', startLoad_fast: 'Load fast (up to 5 minutes)', startLoad_real: 'Load realistically (up to 40 minutes)',
    enterZFW: 'Enter zero fuel weight', hideEnterZFW: 'Close zero fuel weight entry', enterZFWSet: 'Set zero fuel weight',
    applyLoadButton: 'Apply load', button_scrolldown: 'Scroll down', button_scrollup: 'Scroll up', button_scrolltop: 'Scroll to top',
    setCalibration: 'Start guided throttle calibration', resetCalibration: 'Reset throttle calibration', hideThrottle: 'Close',
    pb_left: 'Pushback left', pb_aft: 'Pushback straight', pb_right: 'Pushback right', pb_stop: 'Pushback stop',
    refreshMetar: 'Refresh METARs', filter_pinned: 'Pinned charts', ng_search_button: 'Search',
    hide_runway_select: 'Cancel', land_hide_runway_select: 'Cancel',
    'toggle-maintenance': 'Maintenance timers', syncMetar: 'Sync conditions from METAR',
    land_syncMetar: 'Sync conditions from METAR', calculate: 'Calculate', land_calculate: 'Calculate',
    send: 'Send to FMS'
  };

  // Value boxes on My Flight that carry no caption of their own.
  A.STATICS = { dep_icao: 'Departure', depcit: 'Departure city', arr_icao: 'Arrival', arrcit: 'Arrival city',
                'payload-name': 'Load type' };

  A.GAUGES = { APU_OIL_val: 'APU oil', ENG1_OIL_val: 'Engine 1 oil', ENG2_OIL_val: 'Engine 2 oil',
              HYD_BLU_val: 'Blue hydraulic quantity', HYD_GRE_val: 'Green hydraulic quantity',
              HYD_YEL_val: 'Yellow hydraulic quantity' };

  A.UNITS = { 'M': 'm', 'hpa': 'hPa', 'Kts': 'knots', '°': 'degrees', 'kg': 'kg', 'lb': 'lb', 'lbs': 'lb' };

  // Panel states (the gear menu). The confirm box does not say WHICH state it confirms — that
  // lives only in the EFB's JS — so the reader remembers the one the pilot just pressed.
  A.PANEL_STATES = {
    panelstate_0: 'Cold and Dark', panelstate_1: 'Ready for Takeoff',
    panelstate_2: 'On APU', panelstate_3: 'On GPU'
  };
  A._pendingPanel = '';

  A.MAINT_MODES = { maint_disabled: 'DISABLED', maint_real: 'REALISTIC', maint_fast: 'FAST' };

  // ---------------------------------------------------------------------------------
  // helpers
  // ---------------------------------------------------------------------------------

  A.hasClass = function (el, c) {
    if (!el || !el.className || typeof el.className !== 'string') return false;
    return (' ' + el.className + ' ').indexOf(' ' + c + ' ') >= 0;
  };

  A.clean = function (s) { return String(s == null ? '' : s).replace(/\s+/g, ' ').trim(); };

  // Text runs joined with a SPACE: textContent glues "WEIGHT AND BALANCE [kg]" to the
  // "Passenger Flight" box inside the same heading, and "LIGHTING" to its "CARGO" span.
  A.txt = function (el) {
    if (!el) return '';
    var parts = [];
    (function w(n) {
      for (var i = 0; i < n.childNodes.length; i++) {
        var c = n.childNodes[i];
        if (c.nodeType === 3) parts.push(c.textContent);
        else if (c.nodeType === 1 && c.tagName !== 'SCRIPT' && c.tagName !== 'STYLE') { parts.push(' '); w(c); parts.push(' '); }
      }
    })(el);
    return A.clean(parts.join('')).replace(/\[ /g, '[').replace(/ \]/g, ']');
  };

  A.ownText = function (el) {
    var s = '';
    for (var i = 0; i < el.childNodes.length; i++) {
      var n = el.childNodes[i];
      if (n.nodeType === 3) s += n.textContent + ' ';
    }
    return A.clean(s);
  };

  // Visibility by STYLE only, never by geometry: the page wrappers lay out absolutely and the
  // weights sub-pages are slid off-screen, so rectangles say nothing useful here.
  A.isHidden = function (el) {
    if (!el || el.nodeType !== 1) return true;
    if (A.hasClass(el, 'hiddenPage')) return true;
    var st = null;
    try { st = window.getComputedStyle(el); } catch (e) { return false; }
    if (!st) return false;
    return st.display === 'none' || st.visibility === 'hidden';
  };

  A.shownChain = function (el) {
    for (var n = el; n && n.nodeType === 1; n = n.parentElement) if (A.isHidden(n)) return false;
    return true;
  };

  A.root = function () { return document.getElementById('iniEFB') || document.body; };
  // tools/coherent.ps1 `capture` looks for these two on whichever agent is installed.
  A.findRoot = function () { return A.root(); };
  A.isVisible = function (el) { return !!el && !A.isHidden(el); };

  A.byId = function (id) { return document.getElementById(id); };

  A.unit = function (input) {
    var n = input.nextElementSibling;
    if (!n || !A.hasClass(n, 'input-group-text')) return '';
    var u = A.txt(n);
    if (A.UNITS.hasOwnProperty(u)) return A.UNITS[u];
    return u.replace(/�/g, '°');
  };

  // The caption of a field: the label cell in its Bootstrap row (".flight-value-title" on the
  // perf pages, ".ini-input-label" on Weight and Balance). Used only when the id map has nothing.
  A.rowLabelEl = function (input) {
    for (var n = input.parentElement, up = 0; n && up < 5; n = n.parentElement, up++) {
      if (!A.hasClass(n, 'row')) continue;
      for (var i = 0; i < n.children.length; i++) {
        var c = n.children[i];
        if (A.hasClass(c, 'flight-value-title') || A.hasClass(c, 'ini-input-label')) return c;
      }
      return null;
    }
    return null;
  };

  A.fieldName = function (el) {
    if (el.id && A.FIELDS[el.id]) return A.FIELDS[el.id];
    var l = A.rowLabelEl(el);
    if (l) return A.txt(l);
    return el.getAttribute('placeholder') || el.id || 'Field';
  };

  // Claim captions (label cells, unit boxes, a choice group's heading) BEFORE the walk: the caption
  // is reached before its control in DOM order, so it must be known as claimed up front or every
  // field reads its caption twice.
  A.markClaims = function () {
    var was = A.root().querySelectorAll('[' + A.CLAIM + ']');
    for (var w = 0; w < was.length; w++) was[w].removeAttribute(A.CLAIM);
    var ins = A.root().querySelectorAll('input');
    for (var i = 0; i < ins.length; i++) {
      var l = A.rowLabelEl(ins[i]);
      if (l) l.setAttribute(A.CLAIM, '1');
      var u = ins[i].nextElementSibling;
      if (u && A.hasClass(u, 'input-group-text')) u.setAttribute(A.CLAIM, '1');
    }
    var rg = A.root().querySelectorAll('input[type="range"]');
    for (var r = 0; r < rg.length; r++) {
      if (A.FIELDS[rg[r].id]) continue;
      var col = rg[r].parentElement, cap = col ? col.previousElementSibling : null;
      if (cap && A.txt(cap)) cap.setAttribute(A.CLAIM, '1');
    }
    var gs = A.root().querySelectorAll('div.btn-group');
    for (var g = 0; g < gs.length; g++) {
      if (!A.isChoiceGroup(gs[g])) continue;
      var h = A.groupHeadingEl(gs[g]);
      if (h) h.setAttribute(A.CLAIM, '1');
    }
  };

  // ---------------------------------------------------------------------------------
  // element records
  // ---------------------------------------------------------------------------------

  A._idx = 0;
  A.stamp = function (node) { node.setAttribute(A.ATTR, String(++A._idx)); return A._idx; };

  A.el = function (node, f) {
    var o = { idx: node ? A.stamp(node) : ++A._idx, text: '', value: '', controlType: '', kind: '',
              clickable: false, level: 0, live: '', disabled: false, options: null };
    for (var k in f) if (f.hasOwnProperty(k)) o[k] = f[k];
    return o;
  };

  A.button = function (node, text, extra) {
    var f = { kind: 'button', clickable: true, text: text, disabled: !!node.disabled };
    if (extra) for (var k in extra) if (extra.hasOwnProperty(k)) f[k] = extra[k];
    return A.el(node, f);
  };

  A.readout = function (el) {
    var name = A.fieldName(el), unit = A.unit(el);
    var v = A.clean(el.value);
    if (!v) v = el.getAttribute('placeholder') || '';
    if (!v) v = '(empty)';
    var dashed = /^[-.\s]*$/.test(v);
    return A.el(el, { kind: 'static', text: name + ': ' + v + (unit && !dashed && v !== '(empty)' ? ' ' + unit : ''),
                      key: 'readout:' + (el.id || name) });
  };

  // ---------------------------------------------------------------------------------
  // choice groups — Bootstrap radio button groups (Settings, Throttle Calibration)
  // ---------------------------------------------------------------------------------

  A.isChoiceGroup = function (el) {
    if (!el || el.tagName !== 'DIV' || !A.hasClass(el, 'btn-group')) return false;
    var rs = el.querySelectorAll('input[type="radio"]');
    return rs.length >= 2;
  };

  A.radioLabel = function (r) {
    if (!r.id) return '';
    var l = document.querySelector('label[for="' + r.id + '"]');
    return l ? A.txt(l) : '';
  };

  // The group's caption is the H2 just before the row that holds it ("IRS Align Time").
  A.groupHeadingEl = function (g) {
    for (var n = g, up = 0; n && up < 5; n = n.parentElement, up++) {
      var p = n.previousElementSibling;
      if (p && /^H[1-6]$/.test(p.tagName)) return p;
      if (p) return null;
    }
    return null;
  };

  A.choiceRecord = function (g) {
    var rs = g.querySelectorAll('input[type="radio"]'), opts = [], cur = '', dis = true;
    for (var i = 0; i < rs.length; i++) {
      var t = A.radioLabel(rs[i]) || rs[i].id;
      opts.push(t);
      if (rs[i].checked) cur = t;
      if (!rs[i].disabled) dis = false;
    }
    var h = A.groupHeadingEl(g);
    var name = h ? A.txt(h) : (rs[0].getAttribute('name') || 'Option');
    return A.el(g, { controlType: 'select', text: name, value: cur, options: opts, disabled: dis,
                     key: 'choice:' + (rs[0].getAttribute('name') || name) });
  };

  // ---------------------------------------------------------------------------------
  // control classification
  // ---------------------------------------------------------------------------------

  A.isControl = function (el) { var t = el.tagName; return t === 'BUTTON' || t === 'INPUT' || t === 'SELECT' || t === 'TEXTAREA'; };

  A.unitText = function (s) {
    return String(s).replace(/(\d)'(\d)/g, '$1,$2').replace(/Kgs\b/g, ' kg').replace(/Lbs\b/g, ' lb');
  };

  A.controlFor = function (el) {
    var id = el.id || '';

    if (el.tagName === 'BUTTON') {
      if (A.DOORS[id]) {
        var st = A.hasClass(el, 'door-transit') ? 'moving' : (A.hasClass(el, 'door-open') ? 'open' : 'closed');
        return A.button(el, A.DOORS[id] + ': ' + st, { key: 'door:' + id, announceChange: true });
      }
      if (A.SLIDES[id]) {
        return A.button(el, A.SLIDES[id] + ': ' + (A.hasClass(el, 'armed') ? 'armed' : 'disarmed'),
                        { key: 'slide:' + id, announceChange: true });
      }
      if (A.EQUIP[id]) {
        var on = /^remove/i.test(A.txt(el));
        return A.button(el, A.EQUIP[id] + ': ' + (on ? 'attached' : 'not attached'),
                        { key: 'equip:' + id, announceChange: true });
      }
      if (A.PANEL_STATES[id]) return A.button(el, A.PANEL_STATES[id]);
      if (A.MAINT_MODES[id]) {
        // "(selected)" is a state suffix the shell already strips from its reconcile key.
        var cur = A.txt(A.byId('maint_state')).toUpperCase();
        return A.button(el, A.txt(el) + (cur === A.MAINT_MODES[id] ? ' (selected)' : ''));
      }
      if (A.hasClass(el, 'payload-button')) {
        var sp = el.getElementsByTagName('span');
        if (sp.length >= 2) {
          var detail = A.unitText(A.txt(sp[0])), nm = A.txt(sp[1]);
          return A.button(el, nm + ' (' + detail + ')');
        }
      }
      if (A.hasClass(el, 'rwy-select') || A.hasClass(el, 'land-rwy-select')) {
        var small = el.getElementsByTagName('small')[0];
        var rn = A.ownText(el).replace(/^RWY\s*/i, 'Runway ');
        var len = small ? A.txt(small).replace(/^\[|\]$/g, '').replace(/(\d)(m|ft)\b/g, '$1 $2') : '';
        return A.button(el, rn + (len ? ', ' + len : ''));
      }
      if (id === 'dep_button') return A.button(el, 'Departure charts: ' + A.txt(el));
      if (id === 'arr_button') return A.button(el, 'Arrival charts: ' + A.txt(el));
      var bt = A.BUTTONS[id] || A.txt(el) || el.getAttribute('title') || '';
      if (/^[<>]$/.test(bt)) bt = 'Toggle sidebar';
      if (!bt) return null;                       // truly anonymous icon button — drop it (PMDG-EFB rule)
      return A.button(el, bt);
    }

    if (el.tagName !== 'INPUT') return null;
    var ty = String(el.getAttribute('type') || el.type || 'text').toLowerCase();

    if (ty === 'button' || ty === 'submit') {
      if (A.TOGGLES[id]) return A.button(el, A.TOGGLES[id] + ': ' + A.clean(el.value), { key: 'toggle:' + id, announceChange: true });
      if (A.RUNWAY_PICKERS[id]) {
        // A press opens the runway list; it does not change this label, so no announceChange.
        var cur = A.clean(el.value).replace(/^RWY\s*/i, '');
        return A.button(el, 'Runway: ' + (cur || 'none') + ', choose', { key: 'runway:' + id });
      }
      return A.button(el, A.BUTTONS[id] || A.clean(el.value) || id || 'Button');
    }

    if (ty === 'radio') return null;              // read through its choice group
    if (ty === 'checkbox') {
      var cl = el.id ? document.querySelector('label[for="' + el.id + '"]') : null;
      if (!cl && el.nextElementSibling && el.nextElementSibling.tagName === 'LABEL') cl = el.nextElementSibling;
      if (cl) cl.setAttribute(A.CLAIM, '1');
      return A.el(el, { controlType: 'checkbox', text: (cl ? A.txt(cl) : '') || A.FIELDS[id] || id || 'Option',
                        value: el.checked ? 'true' : 'false', disabled: !!el.disabled });
    }
    if (ty === 'range') {
      var rname = A.FIELDS[id] || A.rangeName(el);
      if (id === 'lights_cargo') {
        // The same slider is the CABIN lights on a passenger airframe; the EFB says which in #lighting_title.
        var lt = A.txt(A.byId('lighting_title'));
        if (lt) rname = lt.charAt(0) + lt.slice(1).toLowerCase() + ' lighting';
      }
      var rf = { controlType: 'range', text: rname, value: String(el.value),
                 disabled: !!el.disabled };
      if (el.min !== '') rf.min = Number(el.min);
      if (el.max !== '') rf.max = Number(el.max);
      if (el.step !== '') rf.step = Number(el.step);
      return A.el(el, rf);
    }

    // A disabled or read-only box is a read-out, never an edit field.
    if (el.disabled || el.readOnly || el.hasAttribute('readonly')) return A.readout(el);

    var name = A.fieldName(el), unit = A.unit(el);
    if (unit) name += ' (' + unit + ')';
    if (ty === 'password') {
      // Never put a logon code on screen or in a log: say whether one is stored.
      return A.el(el, { controlType: 'text', text: name + (el.value ? ', set' : ', not set'), value: '',
                        key: 'field:' + (id || name) });
    }
    return A.el(el, { controlType: 'text', text: name, value: String(el.value == null ? '' : el.value) });
  };

  // Audio volume sliders sit beside a <p> caption in the previous Bootstrap column.
  A.rangeName = function (el) {
    var col = el.parentElement;
    var p = col ? col.previousElementSibling : null;
    if (p) {
      var t = A.txt(p);
      if (t) return t + ' volume';
    }
    return el.id || 'Slider';
  };

  // ---------------------------------------------------------------------------------
  // building blocks — tried before the generic rules; the first match owns its subtree
  // ---------------------------------------------------------------------------------

  A.BLOCKS = [];
  A.block = function (name, match, emit) { A.BLOCKS.push({ name: name, match: match, emit: emit }); };
  A.runBlocks = function (el, els) {
    for (var i = 0; i < A.BLOCKS.length; i++) if (A.BLOCKS[i].match(el)) { A.BLOCKS[i].emit(el, els, A._pageName); return true; }
    return false;
  };

  A.UNREADABLE = 'This is a picture and cannot be read.';

  // A330 side bar: one clickable DIV per page (onclick), the open page carries class "active".
  A.block('sidebar',
    function (el) { return el.id === 'side-bar'; },
    function (el, els) {
      var bs = el.querySelectorAll('.sidebar-button');
      for (var i = 0; i < bs.length; i++) {
        var title = bs[i].querySelector('.sidebar-button-title');
        var name = A.txt(title || bs[i]);
        if (!name) continue;
        var on = A.hasClass(bs[i], 'active');
        els.push(A.button(bs[i], name + (on ? ' (current page)' : ''), { key: 'nav:' + name }));
      }
    });

  // A330 door buttons: state from the door-open / door-transit classes, else the fill gradient's percent.
  A.doorState = function (el) {
    if (A.hasClass(el, 'door-transit')) return 'moving';
    if (A.hasClass(el, 'door-open')) return 'open';
    var m = /(\d+(?:\.\d+)?)%\s*\)?\s*$/.exec(String(el.style.backgroundImage || ''));
    var pct = m ? parseFloat(m[1]) : 0;
    return pct >= 99 ? 'open' : pct > 0 ? 'moving' : 'closed';
  };
  A.block('a330door',
    function (el) { return el.tagName === 'BUTTON' && !!A.DOORS[el.id]; },
    function (el, els) {
      if (A.isHidden(el)) return;
      els.push(A.button(el, A.DOORS[el.id] + ': ' + A.doorState(el), { key: 'door:' + el.id, announceChange: true }));
    });

  A.equipState = function (id) {
    var s = A.EQUIP_STATE[id];
    try { return SimVar.GetSimVarValue(s.lvar, 'number') > 0.5 ? s.on : s.off; } catch (e) { return ''; }
  };
  A.block('a330equip',
    function (el) { return el.tagName === 'BUTTON' && !!A.EQUIP_STATE[el.id]; },
    function (el, els) {
      if (A.isHidden(el)) return;
      var s = A.EQUIP_STATE[el.id], st = A.equipState(el.id);
      var label = A.txt(el) || s.name;
      if (/cover/i.test(label)) label = label.replace(/^\s+|\s+$/g, '');
      els.push(A.button(el, st ? (s.name === 'Covers' ? label : s.name) + ': ' + st : label, { key: 'equip:' + el.id + ':' + label, announceChange: true }));
    });

  // Dashboard tile: a clickable DIV with rel="<page>".
  A.block('tile',
    function (el) { return A.hasClass(el, 'menu-row-item') && A.hasClass(el, 'is-button'); },
    function (el, els) {
      var t = A.txt(el.querySelector('.home-button-text')) || A.txt(el) || el.getAttribute('rel') || 'Page';
      els.push(A.button(el, t, { disabled: A.hasClass(el, 'disabled') }));
    });

  // The door column has no caption of its own.
  A.block('doors',
    function (el) { return el.id === 'equip-doors'; },
    function (el, els, pageName) {
      els.push(A.el(null, { kind: 'heading', level: 2, text: 'Doors and slides' }));
      for (var i = 0; i < el.children.length; i++) A.walkInto(el.children[i], els, pageName);
    });

  A.block('choice', A.isChoiceGroup, function (el, els) { els.push(A.choiceRecord(el)); });

  // The colour legend under the door buttons ("Red - Armed // Green - Disarmed"): the reader puts
  // the state in each label instead, so the legend is noise.
  A.block('slide-legend',
    function (el) { return /^H[1-6]$/.test(el.tagName) && el.querySelector('span.text-danger') && el.querySelector('span.text-success'); },
    function () {});

  // A heading paired with a value box ("DEP TIME" / "-", "METAR EGNT" / the report): one line.
  A.block('pair',
    function (el) {
      return el.tagName === 'DIV' && el.children.length === 2 && /^H[1-6]$/.test(el.children[0].tagName) &&
             el.children[1].tagName === 'DIV' && !!el.children[1].id && el.children[1].children.length === 0 &&
             A.txt(el.children[1]) !== '';      // an empty box is a drawing (the throttle track), not a value
    },
    function (el, els) {
      var h = A.txt(el.children[0]), v = A.txt(el.children[1]) || '-';
      els.push(A.el(el.children[1], { kind: 'static', text: h + ': ' + v, key: 'readout:' + el.children[1].id }));
    });

  // Perf diagrams: the V-speed / distance graphic is drawn, but its message box is text.
  A.block('perf-diagram',
    function (el) { return el.id === 'diagram'; },
    function (el, els) {
      var codes = el.querySelector('#codes, #land_codes');
      if (codes && !A.isHidden(codes)) {
        els.push(A.el(codes, { kind: 'static', text: 'Performance messages: ' + (A.txt(codes) || '-'),
                               key: 'readout:' + codes.id }));
      }
    });

  A.block('enroute-map',
    function (el) { return el.id === 'enroute'; },
    function (el, els) { els.push(A.el(null, { kind: 'static', text: 'The enroute map is a picture and cannot be read.' })); });

  A.block('chart-image',
    function (el) { return el.id === 'chart_image_box'; },
    function (el, els) { els.push(A.el(null, { kind: 'static', text: 'Charts are pictures and cannot be read.' })); });

  // Chart zoom and pan buttons only move that picture.
  A.block('chart-controls',
    function (el) { return el.id === 'chart_ctrl_zoom' || el.id === 'chart_ctrl_pan'; },
    function () {});

  A.block('wear-indicators',
    function (el) { return el.id === 'Brake_Wear_Indicator_LFI'; },
    function (el, els) { els.push(A.el(null, { kind: 'static', text: 'Brake and tyre wear are shown as colours only and cannot be read.' })); });

  A.block('wear-indicator',
    function (el) { return A.hasClass(el, 'brake-wear-indicator') || A.hasClass(el, 'tire-wear-indicator'); },
    function () {});

  // Oil / hydraulic level gauges: name, a bar, then the value.
  A.block('level-gauge',
    function (el) {
      return el.tagName === 'DIV' && el.children.length === 3 && el.children[1].className &&
             A.hasClass(el.children[1], 'condition-bar');
    },
    function (el, els) {
      var v = el.children[2];
      var nm = A.GAUGES[v.id] || A.txt(el.children[0]);
      els.push(A.el(v, { kind: 'static', text: nm + ': ' + (A.txt(v) || '-'), key: 'readout:' + (v.id || nm) }));
    });

  // Throttle-calibration position marks: graphic labels beside the lever track.
  A.block('throttle-marks',
    function (el) { return A.hasClass(el, 'indicator'); },
    function () {});

  // The hold diagram on Weight and Balance draws the loaded hold, no text.
  A.block('weight-image',
    function (el) { return A.hasClass(el, 'weight-image'); },
    function () {});

  // ---------------------------------------------------------------------------------
  // the walk
  // ---------------------------------------------------------------------------------

  A.headingLevel = function (el) {
    var t = el.tagName;
    if (t === 'H1' || t === 'H5') return 2;
    if (t === 'H2' || t === 'H3' || t === 'H4') return 3;
    if (t === 'H6') return 4;
    return 0;
  };

  A.INLINE = { SPAN: 1, SMALL: 1, B: 1, I: 1, STRONG: 1, EM: 1, BR: 1, A: 1 };
  A.onlyInline = function (el) {
    for (var i = 0; i < el.children.length; i++) if (!A.INLINE[el.children[i].tagName]) return false;
    return true;
  };

  A.walkInto = function (root, els, pageName) {
    A._pageName = pageName;
    function walk(el) {
      if (!el || el.nodeType !== 1) return;
      var tag = el.tagName;
      if (tag === 'SCRIPT' || tag === 'STYLE' || tag === 'IMG' || tag === 'svg' || tag === 'SVG' || tag === 'CANVAS' || tag === 'HR') return;
      if (A.isHidden(el)) return;
      if (A.runBlocks(el, els)) return;

      if (A.isControl(el)) {
        var c = A.controlFor(el);
        if (c && (c.text || c.value || c.controlType)) els.push(c);
        return;
      }

      var lvl = A.headingLevel(el);
      if (lvl > 0) {
        if (el.hasAttribute(A.CLAIM)) return;
        // Controls inside a heading (the perf cards' SYNC button, the runway list's Cancel) are
        // still controls: read the heading's own text, then walk its children.
        var hasCtrl = el.querySelector('button, input, div');
        var ht = hasCtrl ? A.ownText(el) : A.txt(el);
        // A page's own title repeated as its first heading ("SETTINGS" under Settings) reads twice;
        // the same words as a SECTION heading further down (Ground Equipment's card) are kept.
        var last = els.length ? els[els.length - 1] : null;
        var dup = last && last.kind === 'heading' && last.level === 1 && ht.toLowerCase() === String(pageName).toLowerCase();
        if (ht && !dup) els.push(A.el(el, { kind: 'heading', text: ht, level: lvl }));
        if (hasCtrl) for (var h = 0; h < el.children.length; h++) walk(el.children[h]);
        return;
      }

      if (el.hasAttribute(A.CLAIM)) return;

      // A paragraph or a box of inline runs is ONE line ("Maintenance system is currently set to:
      // REALISTIC"), not one line per span.
      if (A.onlyInline(el) && !el.querySelector('button, input')) {
        var t = A.txt(el);
        if (el.id && A.STATICS[el.id]) t = A.STATICS[el.id] + ': ' + (t || '-');
        if (t && !(t.length <= 1 && !/[0-9A-Za-z]/.test(t))) {
          els.push(A.el(el, { kind: 'static', text: t, key: el.id ? 'readout:' + el.id : undefined }));
        }
        return;
      }

      var own = A.ownText(el);
      if (own) els.push(A.el(el, { kind: 'static', text: own }));
      for (var k = 0; k < el.children.length; k++) walk(el.children[k]);
    }
    walk(root);
  };

  // ---------------------------------------------------------------------------------
  // the screen in front of the pilot
  // ---------------------------------------------------------------------------------

  A.currentPageWrapper = function () {
    var r = A.byId('renderer');
    if (!r) return null;
    for (var i = 0; i < r.children.length; i++) if (A.hasClass(r.children[i], 'visiblePage')) return r.children[i];
    return null;
  };

  A.pageNameOf = function (wrap) {
    if (!wrap) return '';
    for (var i = 0; i < wrap.children.length; i++) {
      var c = wrap.children[i];
      if (c.id && A.PAGES[c.id]) return A.PAGES[c.id];
      if (A.hasClass(c, 'page-title')) {
        var t = A.txt(c);
        return t ? t.charAt(0) + t.slice(1).toLowerCase() + ' Map' : 'Map';
      }
    }
    return 'Page';
  };

  A.currentPage = function () {
    if (A.overlayShown('powered-off')) return 'Powered off';
    if (A.overlayShown('confirm-box')) return 'Confirm Panel State';
    if (A.overlayShown('control-box')) return 'EFB Menu';
    return A.pageNameOf(A.currentPageWrapper());
  };

  A.overlayShown = function (id) { var e = A.byId(id); return !!e && A.shownChain(e); };

  // A notie toast ("Panel State Set - Cold and Dark") is appended straight to <body>.
  A.toasts = function (els) {
    var ts = document.querySelectorAll('.notie-container');
    for (var i = 0; i < ts.length; i++) {
      var inner = ts[i].querySelector('.notie-textbox-inner');
      var msg = A.txt(inner || ts[i]);
      if (msg) els.push(A.el(inner || ts[i], { kind: 'alert', live: 'assertive', text: msg }));
      var bs = ts[i].querySelectorAll('.notie-button');
      for (var b = 0; b < bs.length; b++) els.push(A.button(bs[b], A.txt(bs[b]) || 'OK'));
    }
  };

  A.headerControls = function (els) {
    var sb = A.byId('side-bar');
    if (sb && A.shownChain(sb)) {
      els.push(A.el(null, { kind: 'heading', level: 2, text: 'Pages' }));
      A.runBlocks(sb, els);
    }
    var home = A.byId('menu-home-button');
    if (home && A.shownChain(home)) els.push(A.button(home, 'Home'));
    var maint = A.byId('toggle-maintenance');
    if (maint && A.shownChain(maint)) els.push(A.button(maint, 'Maintenance timers'));
    var gear = A.byId('control-box-button-icon');
    if (gear && A.shownChain(gear)) els.push(A.button(gear, 'EFB menu (power, lock, brightness, panel states)'));
  };

  // The clock and the maintenance timers close the page, so they never stand between the pilot and
  // the page's own content.
  A.footer = function (els, pageName) {
    var times = A.root().querySelectorAll('#right-menu-bar .time');
    for (var i = 0; i < times.length; i++) {
      var small = times[i].querySelector('.time-small');
      var label = small ? A.txt(small) : '';
      var val = A.ownText(times[i]);
      if (!val) continue;
      if (/^\d{4}$/.test(val)) val = val.slice(0, 2) + ':' + val.slice(2);
      var name = /zulu/i.test(label) ? 'Zulu time' : (/local/i.test(label) ? 'Local time' : 'Time');
      els.push(A.el(times[i], { kind: 'static', text: name + ': ' + val, key: 'readout:time-' + i }));
    }
    var timers = A.byId('timerContainer');
    if (timers && A.shownChain(timers)) {
      els.push(A.el(null, { kind: 'heading', level: 2, text: 'Maintenance timers' }));
      A.walkInto(timers, els, pageName);
    }
  };

  // A full-screen overlay inside a page (runway list, OFP viewer, checklist): shown modally, since
  // it covers the page — reading the page behind it would offer buttons the pilot cannot reach.
  A.pageOverlay = function (wrap) {
    var ids = ['runwayPanel', 'land_runwayPanel', 'ofp_viewer', 'checklist_viewer'];
    for (var i = 0; i < ids.length; i++) {
      var e = A.byId(ids[i]);
      if (e && wrap.contains(e) && A.shownChain(e)) return e;
    }
    return null;
  };

  A.weightsSubPage = function (wrap) {
    var p2 = A.byId('page2');
    if (!p2 || !wrap.contains(p2)) return null;
    var left = String(p2.style.left || '');
    return /^0(px|vw|%)?$/.test(left) ? 'page2' : 'page1';
  };

  A.collectPage = function (wrap, els, pageName) {
    if (A.byId('lockscreen') && wrap.contains(A.byId('lockscreen'))) {
      els.push(A.el(null, { kind: 'static', text: 'The EFB is locked.' }));
      var un = A.byId('lockscreen').querySelector('.home-button');
      if (un) els.push(A.button(un, 'Unlock'));
      return;
    }

    var ov = A.pageOverlay(wrap);
    if (ov) {
      if (ov.id === 'ofp_viewer') {
        els.push(A.el(null, { kind: 'heading', level: 2, text: 'Flight plan (OFP)' }));
        var pre = ov.querySelector('pre');
        var lines = pre ? String(pre.textContent || '').split(/\r?\n/) : [];
        for (var l = 0; l < lines.length; l++) {
          var ln = A.clean(lines[l]);
          if (ln) els.push(A.el(null, { kind: 'static', text: ln }));
        }
        var cl = ov.querySelector('button');
        if (cl) els.push(A.button(cl, 'Close'));
        return;
      }
      if (ov.id === 'checklist_viewer') {
        els.push(A.el(null, { kind: 'heading', level: 2, text: 'Checklist' }));
        els.push(A.el(null, { kind: 'static', text: 'The checklist is a picture and cannot be read.' }));
        var cc = ov.querySelector('button');
        if (cc) els.push(A.button(cc, 'Close'));
        return;
      }
      A.walkInto(ov, els, pageName);              // the runway list
      return;
    }

    var sub = A.weightsSubPage(wrap);
    if (sub) {
      var sw1 = A.byId('switcher_page1'), sw2 = A.byId('switcher_page2');
      if (sub === 'page1') {
        if (sw2 && A.shownChain(sw2)) els.push(A.button(sw2, 'Show weight and balance'));
        A.walkInto(A.byId('page1'), els, pageName);
      } else {
        if (sw1 && A.shownChain(sw1)) els.push(A.button(sw1, 'Show payload selection'));
        A.walkInto(A.byId('page2'), els, pageName);
      }
      return;
    }

    for (var k = 0; k < wrap.children.length; k++) {
      var c = wrap.children[k];
      if (c.id && /-bg$/.test(c.id)) continue;      // background art
      if (A.hasClass(c, 'page-title')) continue;    // already the page heading
      A.walkInto(c, els, pageName);
    }
  };

  A.collect = function () {
    var els = [];
    A._idx = 0;
    var stale = document.querySelectorAll('[' + A.ATTR + ']');
    for (var s = 0; s < stale.length; s++) stale[s].removeAttribute(A.ATTR);
    A.markClaims();

    var page = A.currentPage();
    A.toasts(els);

    if (A.overlayShown('powered-off')) {
      els.push(A.el(null, { kind: 'static', text: 'The EFB is powered off.' }));
      els.push(A.button(A.byId('powered-off'), 'Power on'));
      return els;
    }

    if (A.overlayShown('confirm-box')) {
      var cb = A.byId('confirm-box');
      els.push(A.el(null, { kind: 'heading', level: 1, text: 'Confirm New Panel State' }));
      if (A._pendingPanel) els.push(A.el(null, { kind: 'static', text: 'Load panel state: ' + A._pendingPanel + '?' }));
      var bs = cb.querySelectorAll('button');
      for (var b = 0; b < bs.length; b++) if (!A.isHidden(bs[b])) els.push(A.button(bs[b], A.txt(bs[b])));
      return els;
    }

    if (A.overlayShown('control-box')) {
      els.push(A.el(null, { kind: 'heading', level: 1, text: 'EFB Menu' }));
      A.walkInto(A.byId('control-box'), els, page);
      var gear = A.byId('control-box-button-icon');
      if (gear) els.push(A.button(gear, 'Close menu'));
      return els;
    }

    A.headerControls(els);
    var wrap = A.currentPageWrapper();
    if (!wrap) {
      els.push(A.el(null, { kind: 'static', text: 'No EFB page is showing.' }));
      return els;
    }
    els.push(A.el(null, { kind: 'heading', level: 1, text: page }));
    A.collectPage(wrap, els, page);
    A.footer(els, page);

    var off = A.byId('inputDisabled');
    if (off && A.shownChain(off)) els.push(A.el(off, { kind: 'alert', live: 'assertive', text: A.txt(off) }));
    return els;
  };

  // ---------------------------------------------------------------------------------
  // scrape, with the dirty gate
  // ---------------------------------------------------------------------------------

  A._everScraped = false;
  A._dirty = true;
  A._pollCount = 0;
  A._obs = null;
  A._sig = '';
  A.OBSERVER_OPTS = { childList: true, subtree: true, characterData: true, attributes: true };
  A.FORCE_FULL_EVERY = 10;

  // The EFB writes most of its values through the .value / .checked PROPERTIES, which no
  // MutationObserver sees (a calculated V1 would sit stale until the forced full scrape). So the
  // gate also compares a cheap signature of every input's live value.
  A.valueSig = function () {
    var ins = A.root().querySelectorAll('input'), s = '';
    for (var i = 0; i < ins.length; i++) s += (ins[i].checked ? '1' : '0') + ins[i].value + '\u001f';
    return s;
  };

  // SELF-TRIGGER TRAP (MD-11 lesson): collect() stamps and clears attributes inside the observed
  // subtree, so the observer is disconnected for exactly its duration.
  A.scrape = function () {
    try {
      A._pollCount++;
      var sig = A.valueSig();
      if (sig !== A._sig) { A._sig = sig; A._dirty = true; }
      var forceFull = !A._everScraped || (A._pollCount % A.FORCE_FULL_EVERY === 0);
      if (!A._dirty && !forceFull) return JSON.stringify({ ok: true, unchanged: true });
      A._dirty = false;
      A._everScraped = true;

      var els;
      A.disconnect();
      try { els = A.collect(); }
      finally { A.observe(); }
      return JSON.stringify({ ok: true, page: A.currentPage(), elements: els });
    } catch (e) {
      return JSON.stringify({ ok: false, error: String(e && e.message ? e.message : e) });
    }
  };

  // ---------------------------------------------------------------------------------
  // input
  // ---------------------------------------------------------------------------------

  A.find = function (idx) { return document.querySelector('[' + A.ATTR + '="' + idx + '"]'); };

  // Coherent GT exposes some event constructors (PointerEvent) whose instances dispatchEvent then
  // rejects ("must be an instance of Event", seen live 2026-10-07), so anything that is not a real
  // Event falls back to a plain one — and a failed preamble event never blocks the click itself.
  A.fire = function (el, type, Ctor) {
    var ev = null;
    try { if (Ctor) ev = new Ctor(type, { bubbles: true, cancelable: true, view: window }); } catch (e) { ev = null; }
    if (!ev || !(ev instanceof window.Event)) { ev = document.createEvent('Event'); ev.initEvent(type, true, true); }
    try { el.dispatchEvent(ev); } catch (e2) {}
  };

  // ONE click event (the MD-11 lesson: a synthetic 'click' beside el.click() runs onclick twice,
  // and every handler here is a toggle — twice is no press at all).
  A.click = function (el) {
    A.fire(el, 'pointerdown', window.PointerEvent || window.MouseEvent);
    A.fire(el, 'mousedown', window.MouseEvent);
    A.fire(el, 'pointerup', window.PointerEvent || window.MouseEvent);
    A.fire(el, 'mouseup', window.MouseEvent);
    if (typeof el.click === 'function') el.click();
    else A.fire(el, 'click', window.MouseEvent);
  };

  A.clickElement = function (idx) {
    var el = A.find(idx);
    if (!el) return false;
    if (el.disabled || A.hasClass(el, 'disabled')) return false;   // greyed out: refuse, never claim a press
    if (A.isChoiceGroup(el)) return false;                          // a dropdown is set, not pressed
    try {
      if (A.PANEL_STATES[el.id]) A._pendingPanel = A.PANEL_STATES[el.id];
      A.click(el);
      A._dirty = true;
      return true;
    } catch (e) { return false; }
  };

  A.setValue = function (idx, text) {
    var el = A.find(idx);
    if (!el) return false;
    try {
      var want = A.clean(text);
      if (A.isChoiceGroup(el)) {
        var rs = el.querySelectorAll('input[type="radio"]');
        for (var i = 0; i < rs.length; i++) {
          if ((A.radioLabel(rs[i]) || rs[i].id) !== want) continue;
          if (rs[i].disabled) return false;
          if (rs[i].checked) return true;           // already there: nothing to press
          A.click(rs[i]);                           // the EFB's onclick on the radio commits it
          A._dirty = true;
          return true;
        }
        return false;
      }
      if (el.tagName !== 'INPUT') return false;
      var ty = String(el.getAttribute('type') || el.type || 'text').toLowerCase();
      if (el.disabled || el.readOnly || el.hasAttribute('readonly')) return false;
      if (ty === 'button' || ty === 'submit' || ty === 'radio') return false;
      if (ty === 'checkbox') {
        var on = String(text).toLowerCase() === 'true';
        if (el.checked === on) return true;
        A.click(el);
        A._dirty = true;
        return true;
      }

      // Never focus(): an EFB input's onfocus grabs the sim keyboard (FOCUS_INPUT_FIELD) and pops
      // the on-screen keyboard. Write the value, then fire what the page listens to.
      el.value = String(text);
      if (ty === 'range') {
        // The sliders commit on onmousemove (brightness, cargo lights, volumes), not change.
        A.fire(el, 'input', window.Event);
        A.fire(el, 'change', window.Event);
        A.fire(el, 'mousemove', window.MouseEvent);
        A.fire(el, 'mouseup', window.MouseEvent);
      } else {
        A.fire(el, 'input', window.Event);
        A.fire(el, 'change', window.Event);
        // LOAD-BEARING: SimBrief ID, Hoppie code and the Weight and Balance boxes save ONLY on
        // onkeyup. Without it the text shows and is never committed.
        A.fire(el, 'keyup', window.KeyboardEvent || window.Event);
        A.fire(el, 'blur', window.FocusEvent || window.Event);
      }
      A._dirty = true;
      return true;
    } catch (e) { return false; }
  };

  // ---------------------------------------------------------------------------------
  // install
  // ---------------------------------------------------------------------------------

  // Measured live (2026-10-07, two seconds parked on the Home page): the ONLY mutations were the
  // maintenance-timer spinner (#maint-spin, style.transform every 250 ms), the enroute map's aircraft
  // icon (#position-icon img, style ~4 Hz) and the hidden Development page's variable watcher
  // (#watcherList, rebuilt every few hundred ms). Counted, they kept every poll dirty. So:
  //   - an attribute change on an IMG is a picture moving, never text the reader shows;
  //   - anything inside a page that is not on screen is read when that page is shown (its wrapper's
  //     class change to visiblePage is itself a record that counts).
  A.inHiddenPage = function (n) {
    for (; n && n.nodeType === 1; n = n.parentElement) if (A.hasClass(n, 'hiddenPage')) return true;
    return false;
  };
  A.relevant = function (records) {
    for (var i = 0; i < records.length; i++) {
      var r = records[i], t = r.target;
      if (t && t.nodeType !== 1) t = t.parentElement;           // characterData: the text node's element
      if (r.type === 'attributes' && t && t.tagName === 'IMG') continue;
      if (t && A.inHiddenPage(t)) continue;
      return true;
    }
    return false;
  };

  A.observe = function () {
    if (!A._obs) return;
    try {
      A._obs.observe(A.root(), A.OBSERVER_OPTS);
      // notie toasts are appended directly to <body>.
      if (document.body && A.root() !== document.body) A._obs.observe(document.body, { childList: true });
    } catch (e) {}
  };
  A.disconnect = function () { if (A._obs) { try { A._obs.disconnect(); } catch (e) {} } };

  try {
    if (window.__MSFSBA_A330_EFB_OBS && typeof window.__MSFSBA_A330_EFB_OBS.disconnect === 'function') {
      window.__MSFSBA_A330_EFB_OBS.disconnect();
    }
  } catch (e) {}

  try {
    A._obs = new MutationObserver(function (records) { if (A.relevant(records)) A._dirty = true; });
    A.observe();
    window.__MSFSBA_A330_EFB_OBS = A._obs;
  } catch (e) {
    // No observer -> never gate. Slower, but a stale screen is far worse than a busy one.
    A._obs = null;
    A._dirty = true;
    A.scrape = (function (inner) {
      return function () { A._dirty = true; return inner.apply(A, arguments); };
    })(A.scrape);
  }

  window.__MSFSBA_A330_EFB = A;
  return A.INSTALLED;
})();
