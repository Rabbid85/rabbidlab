// Filters, for every list that has one (Active Directory, Devices): conditions, each a field, a test and a value, and
// all of them or any. A page hands R.filter its fields ([group, [field…]] with key, label, type, get) and its rows; the
// Filter window, the tests and the filter in words are all here.
(() => {
  const R = window.RabbidLab;
  const { $, esc, svg } = R;
  const DAY = 86400000;

  // What each kind of field can be asked. Ops in NO_VALUE need nothing filled in.
  const OPS = {
    text: [['contains', 'contains'], ['notContains', 'does not contain'], ['is', 'is'], ['isNot', 'is not'], ['starts', 'starts with'], ['ends', 'ends with'], ['empty', 'is empty'], ['set', 'is not empty']],
    choice: [['in', 'is any of'], ['notIn', 'is none of'], ['empty', 'is empty'], ['set', 'is not empty']],
    member: [['in', 'is any of'], ['notIn', 'is none of']],
    ou: [['under', 'is in or under'], ['in', 'is directly in']],
    days: [['olderThan', 'more than'], ['within', 'in the last'], ['empty', 'never'], ['set', 'is set']],
    until: [['next', 'in the next'], ['past', 'has passed'], ['empty', 'never'], ['set', 'is set']],
    number: [['more', 'more than'], ['fewer', 'fewer than'], ['eq', 'exactly'], ['atLeast', 'at least'], ['atMost', 'at most']],
    yes: [['is', 'is'], ['isNot', 'is not']],
  };
  const NO_VALUE = ['empty', 'set', 'past'];
  const fieldOf = (fields, key) => (fields || []).flatMap(([, items]) => items).find((f) => f.key === key);
  const listOf = (v) => (Array.isArray(v) ? v : v === undefined || v === null || v === '' ? [] : [v]);
  const lc = (s) => String(s ?? '').toLowerCase();
  const same = (a, b) => lc(a) === lc(b);
  const dayDiff = (iso) => (Date.now() - new Date(iso)) / DAY; // days ago (negative: to come)
  const num = (v) => (v === undefined || v === null || v === '' ? NaN : Number(v));

  // A test for one row from a condition; one not filled in yet lets everything through.
  const ruleTest = (fields, r) => {
    const f = fieldOf(fields, r.field);
    if (!f) return () => true;
    const values = listOf(r.value);
    const one = values[0];
    if (!NO_VALUE.includes(r.op) && (one === undefined || one === '')) return () => true;
    const n = Number(one);
    switch (f.type) {
      case 'text': {
        const want = lc(one);
        return { empty: (x) => !f.get(x), set: (x) => !!f.get(x), is: (x) => lc(f.get(x)) === want, isNot: (x) => lc(f.get(x)) !== want,
          starts: (x) => lc(f.get(x)).startsWith(want), ends: (x) => lc(f.get(x)).endsWith(want), notContains: (x) => !lc(f.get(x)).includes(want) }[r.op] || ((x) => lc(f.get(x)).includes(want));
      }
      case 'choice': {
        const want = new Set(values.map(lc));
        return { empty: (x) => !f.get(x), set: (x) => !!f.get(x), notIn: (x) => !want.has(lc(f.get(x))) }[r.op] || ((x) => want.has(lc(f.get(x))));
      }
      case 'member': {
        const want = new Set(values.map(lc));
        const has = (x) => f.get(x).some((g) => want.has(lc(g)));
        return r.op === 'notIn' ? (x) => !has(x) : has;
      }
      case 'ou': return r.op === 'in' ? (x) => values.some((p) => same(f.get(x), p)) : (x) => values.some((p) => same(f.get(x), p) || lc(f.get(x)).startsWith(`${lc(p)} / `));
      case 'days':
        if (r.op === 'empty') return (x) => !f.get(x);
        if (r.op === 'set') return (x) => !!f.get(x);
        if (r.op === 'within') return (x) => !!f.get(x) && dayDiff(f.get(x)) <= n;
        return (x) => (f.get(x) ? dayDiff(f.get(x)) > n : !!f.never);
      case 'until':
        if (r.op === 'empty') return (x) => !f.get(x);
        if (r.op === 'set') return (x) => !!f.get(x);
        if (r.op === 'past') return (x) => !!f.get(x) && dayDiff(f.get(x)) > 0;
        return (x) => !!f.get(x) && dayDiff(f.get(x)) <= 0 && -dayDiff(f.get(x)) <= n;
      case 'number': return { more: (x) => num(f.get(x)) > n, fewer: (x) => num(f.get(x)) < n, atLeast: (x) => num(f.get(x)) >= n, atMost: (x) => num(f.get(x)) <= n }[r.op] || ((x) => num(f.get(x)) === n);
      case 'yes': return (x) => (!!f.get(x) === (one !== false && one !== 'false')) === (r.op !== 'isNot');
      default: return () => true;
    }
  };
  const ruleFilled = (fields, r) => { const f = fieldOf(fields, r.field); return !!f && (NO_VALUE.includes(r.op) || listOf(r.value).some((v) => v !== '')); };
  // How many conditions are in use.
  const count = (fields, flt) => (flt?.rules || []).filter((r) => ruleFilled(fields, r)).length;
  // One test for a row from the whole filter: every condition, or any.
  const test = (fields, flt) => {
    const tests = (flt?.rules || []).filter((r) => ruleFilled(fields, r)).map((r) => ruleTest(fields, r));
    if (!tests.length) return () => true;
    return flt.join === 'any' ? (x) => tests.some((t) => t(x)) : (x) => tests.every((t) => t(x));
  };
  // A filter in words, for the line over the list.
  const words = (fields, flt) => (flt?.rules || []).filter((r) => ruleFilled(fields, r)).map((r) => {
    const f = fieldOf(fields, r.field);
    const op = (OPS[f.type].find(([k]) => k === r.op) || ['', ''])[1];
    const v = listOf(r.value);
    if (f.type === 'yes') return `${f.label} ${op} ${v[0] === false || v[0] === 'false' ? 'no' : 'yes'}`;
    if (NO_VALUE.includes(r.op)) return `${f.label} ${op}`;
    if (f.type === 'days') return `${f.label} ${op} ${v[0]} days${r.op === 'olderThan' ? ' ago' : ''}`;
    if (f.type === 'until') return `${f.label} ${op} ${v[0]} days`;
    return `${f.label} ${op} ${v.join(', ')}`;
  }).join(flt?.join === 'any' ? ' or ' : '; ');

  // --- The Filter window -------------------------------------------------------------------------------------------
  let draft = null; // { fields, rows, noun, apply, join, rules } while it's open
  const blankRule = () => ({ field: '', op: '', value: '' });
  // What a choice can be: the values the list holds, for the box's suggestions.
  const choicesFor = (f) => {
    const all = draft.rows().flatMap((x) => listOf(f.get(x))).filter((v) => v !== undefined && v !== null && v !== '').map(String);
    return [...new Set(all)].sort((a, b) => a.localeCompare(b));
  };
  const valueHtml = (r, f, i) => {
    const v = listOf(r.value);
    if (f.type === 'yes') {
      const no = v[0] === false || v[0] === 'false';
      const yes = v[0] === true || v[0] === 'true';
      return `<select data-r-val="${i}" aria-label="Value">${yes || no ? '' : '<option value="" selected disabled>Pick one</option>'}`
        + `<option value="true"${yes ? ' selected' : ''}>Yes</option><option value="false"${no ? ' selected' : ''}>No</option></select>`;
    }
    if (NO_VALUE.includes(r.op)) return '<span class="flt-none"></span>';
    if (f.type === 'days' || f.type === 'until' || f.type === 'number') {
      const after = f.type === 'number' ? '' : f.type === 'days' && r.op === 'olderThan' ? `days ago${f.never ? ', or never' : ''}` : 'days';
      const range = f.type === 'number' ? '' : 'min="0" max="3650" ';
      return `<span class="flt-num">${R.stepper(`${range}data-r-val="${i}" value="${esc(v[0] ?? '')}" aria-label="Number"`)}${after ? `<span>${after}</span>` : ''}</span>`;
    }
    if (f.type === 'text') return `<input type="text" data-r-val="${i}" value="${esc(v[0] ?? '')}" aria-label="Value" placeholder="Text" autocomplete="off" spellcheck="false">`;
    // Choices, OUs and groups: picked values as chips, and a box to add more from what the list holds.
    return `<span class="flt-tokens">${v.map((x, j) => `<span class="gchip">${esc(x)}<button type="button" class="chip-x" data-r-unpick="${i}" data-j="${j}" aria-label="Remove ${esc(x)}">${svg('x')}</button></span>`).join('')}`
      + `<input type="text" list="flt-dl-${i}" data-r-pick="${i}" placeholder="${v.length ? 'Add another' : 'Type or pick'}" autocomplete="off" spellcheck="false" aria-label="Add a value">`
      + `<datalist id="flt-dl-${i}">${choicesFor(f).filter((c) => !v.includes(c)).map((c) => `<option value="${esc(c)}">`).join('')}</datalist></span>`;
  };
  const drawDraft = () => {
    $('flt-join').value = draft.join;
    $('flt-rules').innerHTML = draft.rules.map((r, i) => {
      const f = fieldOf(draft.fields, r.field);
      return `<div class="flt-row"><select data-r-field="${i}" aria-label="Field">${f ? '' : '<option value="" selected disabled>Pick a field</option>'}`
        + `${draft.fields.map(([group, items]) => `<optgroup label="${esc(group)}">`
          + `${items.map((x) => `<option value="${esc(x.key)}"${x.key === f?.key ? ' selected' : ''}>${esc(x.label)}</option>`).join('')}</optgroup>`).join('')}</select>`
        + (f ? `<select data-r-op="${i}" aria-label="Test">${OPS[f.type].map(([k, label]) => `<option value="${k}"${k === r.op ? ' selected' : ''}>${esc(label)}</option>`).join('')}</select>`
          : `<select data-r-op="${i}" aria-label="Test" disabled><option value="" selected>Test</option></select>`)
        + `<div class="flt-val">${f ? valueHtml(r, f, i) : '<span class="flt-none"></span>'}</div>`
        + `<button type="button" class="at-more flt-x" data-r-del="${i}" data-tip="Remove" aria-label="Remove this condition">${svg('x')}</button></div>`;
    }).join('') || '<p class="muted flt-empty">No conditions. Add one to narrow the list.</p>';
    drawDraftCount();
  };
  // How many match as the conditions stand, updated as they're typed.
  const drawDraftCount = () => {
    const base = draft.rows();
    const n = base.filter(test(draft.fields, draft)).length;
    $('flt-count').textContent = `${n.toLocaleString()} of ${base.length.toLocaleString()} ${draft.noun} match`;
  };
  // o: { title, fields, rows() (what the filter narrows), noun (plural), now (the filter in use), apply(filter or null) }
  const open = (o) => {
    draft = { ...o, join: o.now?.join || 'all', rules: o.now?.rules?.length ? JSON.parse(JSON.stringify(o.now.rules)) : [blankRule()] };
    $('flt-title').textContent = o.title;
    drawDraft();
    $('flt-dialog').showModal();
    $('flt-rules').querySelector('[data-r-val], [data-r-pick], [data-r-field]')?.focus();
  };
  const addPick = (i, value) => {
    const r = draft.rules[i];
    const v = String(value || '').trim();
    if (!v || listOf(r.value).some((x) => same(x, v))) return;
    r.value = [...listOf(r.value), v];
    drawDraft();
    $('flt-rules').querySelector(`[data-r-pick="${i}"]`)?.focus();
  };
  $('flt-rules').addEventListener('change', (e) => {
    const t = e.target;
    if (t.dataset.rField !== undefined) {
      const f = fieldOf(draft.fields, t.value);
      draft.rules[Number(t.dataset.rField)] = { field: f.key, op: OPS[f.type][0][0], value: '' };
      drawDraft();
    } else if (t.dataset.rOp !== undefined) {
      draft.rules[Number(t.dataset.rOp)].op = t.value;
      drawDraft();
    } else if (t.dataset.rVal !== undefined) {
      draft.rules[Number(t.dataset.rVal)].value = t.tagName === 'SELECT' ? t.value === 'true' : t.value;
      if (t.tagName === 'SELECT') t.querySelector('option[value=""]')?.remove(); // Pick one goes once picked
      drawDraftCount();
    } else if (t.dataset.rPick !== undefined) {
      addPick(Number(t.dataset.rPick), t.value);
    }
  });
  $('flt-rules').addEventListener('input', (e) => {
    const t = e.target;
    if (t.dataset.rVal !== undefined && t.tagName === 'INPUT') {
      draft.rules[Number(t.dataset.rVal)].value = t.value;
      drawDraftCount();
    }
    // A suggestion picked from the list adds itself straight away.
    if (t.dataset.rPick !== undefined && [...(t.list?.options || [])].some((o) => o.value === t.value)) addPick(Number(t.dataset.rPick), t.value);
  });
  $('flt-rules').addEventListener('keydown', (e) => {
    if (e.key !== 'Enter') return;
    const t = e.target;
    e.preventDefault();
    if (t.dataset.rPick !== undefined) addPick(Number(t.dataset.rPick), t.value);
    else $('flt-apply').click();
  });
  $('flt-rules').addEventListener('click', (e) => {
    const del = e.target.closest('[data-r-del]');
    if (del) {
      draft.rules.splice(Number(del.dataset.rDel), 1);
      drawDraft();
      return;
    }
    const un = e.target.closest('[data-r-unpick]');
    if (un) {
      const r = draft.rules[Number(un.dataset.rUnpick)];
      r.value = listOf(r.value).filter((_, j) => j !== Number(un.dataset.j));
      drawDraft();
    }
  });
  $('flt-join').addEventListener('change', () => { draft.join = $('flt-join').value; drawDraftCount(); });
  $('flt-add').addEventListener('click', () => {
    draft.rules.push(blankRule());
    drawDraft();
    $('flt-rules').querySelectorAll('[data-r-field]')[draft.rules.length - 1]?.focus();
  });
  $('flt-cancel').addEventListener('click', () => $('flt-dialog').close());
  $('flt-clear').addEventListener('click', () => {
    draft.rules = [];
    drawDraft();
  });
  $('flt-apply').addEventListener('click', () => {
    const kept = draft.rules.filter((r) => ruleFilled(draft.fields, r));
    $('flt-dialog').close();
    draft.apply(kept.length ? { join: draft.join, rules: kept } : null);
  });

  // --- The filter bar --------------------------------------------------------------------------------------------
  // Over a list: Load filter (the ones saved, and the conditions window above for anything a chip can't say), a search
  // of one column or all of them, a chip for each column worth filtering by, and Add/remove filters. A chip lists that
  // column's values with how many rows have each. What's picked stays until it's cleared or the page is opened again;
  // which chips show and the saved filters are kept in this browser.
  const read = (k) => { try { return JSON.parse(localStorage.getItem(k) || 'null'); } catch { return null; } };
  const keep = (k, v) => { try { if (v == null) localStorage.removeItem(k); else localStorage.setItem(k, JSON.stringify(v)); } catch { /* not kept, then */ } };
  const of = (v) => (typeof v === 'function' ? v() : v);
  // The search box's tests follow the column picked: words for text, more or less for a number, days for a date, yes
  // or no for a column that is one or the other. Ops in QUICK_BARE need nothing typed, so the box hides.
  const QUICK = {
    any: [['contains', 'contains'], ['notContains', 'does not contain'], ['is', 'is'], ['starts', 'starts with']],
    text: OPS.text,
    number: [['more', 'more than'], ['fewer', 'less than'], ['eq', 'exactly'], ['atLeast', 'at least'], ['atMost', 'at most']],
    days: [['within', 'in the last'], ['olderThan', 'more than'], ['empty', 'never'], ['set', 'is set']],
    until: [['next', 'in the next'], ['past', 'has passed'], ['empty', 'never'], ['set', 'is set']],
    yes: [['yes', 'is yes'], ['no', 'is no']],
  };
  const QUICK_BARE = ['empty', 'set', 'past', 'yes', 'no'];
  const QUICK_BOX = { number: 'A number', days: 'Days', until: 'Days' }; // what the box asks for, where it isn't words
  const UNIT = { within: ' days', olderThan: ' days ago', next: ' days' };
  const opFor = (kind, op) => (QUICK[kind].some(([k]) => k === op) ? op : QUICK[kind][0][0]);
  const HIT = { contains: (t, q) => t.includes(q), notContains: (t, q) => !t.includes(q), is: (t, q) => t === q, isNot: (t, q) => t !== q,
    starts: (t, q) => t.startsWith(q), ends: (t, q) => t.endsWith(q), empty: (t) => !t, set: (t) => !!t };
  const NUMBERISH = /^[\d.,\s]+(%|gb|mb|ms|min|h|days?|hours?|minutes?)?$/i; // 3, 89%, 16 GB: not worth a chip
  const NONE = 'None'; // a row with nothing in that column
  const picked = {}; // what's filtered on each list, by its name

  // o: { el, store, key, grid() (the list's grid, for its columns), rows() (what the bar narrows: the open tab and
  //      view), noun (plural), find(x) (the words Any column looks in; every column when not given), fields() (for the
  //      conditions window; without them it's left out), redraw }
  const bar = (o) => {
    const el = o.el;
    const name = () => `${o.store || 'rabbidlab.grid'}.filter.${of(o.key)}`;
    const st = () => (picked[name()] ||= { chips: {}, col: '*', op: 'contains', text: '', adv: null });
    const grid = () => o.grid();
    const colOf = (k) => grid().cols().find((c) => c.key === k);
    const fields = () => of(o.fields) || null;
    const b = {};
    let typing = null;
    // What the column picked holds: the page's field type when it has one, or else what its values look like.
    const kindOf = (c) => {
      if (!c) return 'any';
      const f = fields() && fieldOf(fields(), c.key);
      if (f) return QUICK[f.type] ? f.type : 'text';
      let n = 0;
      const seen = { number: 0, date: 0, future: 0, yes: 0 };
      for (const x of o.rows()) {
        const v = c.get(x);
        if (v === null || v === undefined || v === '') continue;
        n += 1;
        const when = typeof v === 'number' && v > 1e11 ? v : typeof v === 'string' && /^\d{4}-\d\d-\d\dT/.test(v) ? Date.parse(v) : NaN;
        if (typeof v === 'boolean') seen.yes += 1;
        else if (!Number.isNaN(when)) { seen.date += 1; if (when > Date.now()) seen.future += 1; } else if (typeof v === 'number' && Number.isFinite(v)) seen.number += 1;
        if (n >= 200) break;
      }
      if (!n) return 'text';
      if (seen.yes === n) return 'yes';
      if (seen.number === n) return 'number';
      if (seen.date === n) return seen.future > n / 2 ? 'until' : 'days';
      return 'text';
    };
    const searchCol = () => { const s = st(); return s.col === '*' ? null : colOf(s.col); };

    // One test for a row from everything picked; skip leaves one chip out, to count what each of its values would give.
    b.test = (skip) => {
      const s = st();
      const g = grid();
      const tests = [];
      const q = s.text.trim().toLowerCase();
      const c = searchCol();
      const kind = kindOf(c);
      const op = opFor(kind, s.op);
      if (q || (c && QUICK_BARE.includes(op))) {
        const hit = HIT[op] || HIT.contains;
        if (kind === 'yes') tests.push((x) => !!c.get(x) === (op === 'yes'));
        else if (kind !== 'text' && kind !== 'any') {
          const f = fields() && fieldOf(fields(), c.key);
          tests.push(ruleTest([['', [{ key: c.key, type: kind, get: c.get, never: !!f?.never }]]], { field: c.key, op, value: q }));
        } else if (c) tests.push((x) => hit(g.text(c, x).toLowerCase(), q));
        else {
          const all = g.cols();
          const texts = (x) => (o.find ? o.find(x).map((t) => String(t ?? '')) : all.map((col) => g.text(col, x))).map((t) => t.toLowerCase());
          tests.push(op === 'notContains' ? (x) => !texts(x).some((t) => t.includes(q)) : (x) => texts(x).some((t) => hit(t, q)));
        }
      }
      Object.entries(s.chips).forEach(([k, set]) => { const c = colOf(k); if (c && set.size && k !== skip) tests.push((x) => set.has(g.text(c, x) || NONE)); });
      if (s.adv && fields()) tests.push(test(fields(), s.adv));
      return tests.length ? (x) => tests.every((t) => t(x)) : () => true;
    };
    // Whether anything narrows the list.
    const bareSearch = () => { const c = searchCol(); return !!c && QUICK_BARE.includes(opFor(kindOf(c), st().op)); };
    b.any = () => { const s = st(); return !!s.text.trim() || bareSearch() || Object.values(s.chips).some((set) => set.size) || !!(s.adv && fields()); };
    // What's picked, in words: a saved filter's name.
    const say = () => {
      const s = st();
      const parts = Object.entries(s.chips).filter(([k, set]) => set.size && colOf(k)).map(([k, set]) => `${colOf(k).label}: ${[...set].join(', ')}`);
      const c = searchCol();
      const kind = kindOf(c);
      const op = opFor(kind, s.op);
      const word = QUICK[kind].find(([k]) => k === op)[1];
      if (bareSearch()) parts.push(`${c.label} ${word}`);
      else if (s.text.trim()) parts.push(`${c?.label || 'Any column'} ${word} ${kind === 'text' || kind === 'any' ? `"${s.text.trim()}"` : `${s.text.trim()}${UNIT[op] || ''}`}`);
      if (s.adv && fields()) parts.push(words(fields(), s.adv));
      return parts.join(' · ');
    };
    // The columns worth a chip: a handful of values that rows share (not a name each), and not numbers or times. A
    // column can ask (filter: true) or refuse (nofilter).
    const candidates = () => {
      const rows = o.rows();
      const g = grid();
      const most = Math.min(40, Math.max(2, rows.length * 0.6));
      return g.cols().filter((c) => {
        if (c.nofilter) return false;
        if (c.filter) return true;
        const seen = new Set();
        for (const x of rows) {
          const raw = c.get(x);
          if (typeof raw === 'number' && raw > 1e11) return false; // a time
          const v = g.text(c, x);
          if (v) seen.add(v);
          if (seen.size > most) return false;
        }
        return seen.size > 1 && ![...seen].every((v) => NUMBERISH.test(v));
      });
    };
    // The chips that show: the ones chosen under Add/remove filters, or the first few worth having.
    const chipKeys = (cand = candidates()) => {
      const chosen = read(`${name()}.chips`);
      if (Array.isArray(chosen)) return chosen.filter(colOf);
      return [...cand.filter((c) => c.filter), ...cand.filter((c) => !c.filter)].slice(0, 5).map((c) => c.key);
    };
    const change = () => { o.redraw(); };

    b.draw = () => {
      const s = st();
      const g = grid();
      if (!el.dataset.fbar) {
        el.dataset.fbar = '1';
        el.classList.add('fbar');
        el.innerHTML = `<button type="button" class="tlink" data-fb="load">Load filter${svg('chevron-down', 'chev')}</button>`
          + `<button type="button" class="gcols" data-fb="save" data-tip="Save this filter" aria-label="Save this filter">${svg('save')}</button>`
          + '<span class="fcombo"><select data-fb="col" aria-label="Column"></select><select data-fb="op" aria-label="Test"></select>'
          + '<input type="search" data-fb="text" autocomplete="off" spellcheck="false"></span>'
          + `<span class="fchips"></span><button type="button" class="fchip" data-fb="more">${svg('plus')}Add/remove filters</button><span class="grow"></span>`
          + '<button type="button" class="tlink" data-fb="clear" hidden>Clear</button>'
          + `<button type="button" class="gcols" data-fb="wide" data-tip="Show every filter" aria-label="Show every filter" aria-expanded="false">${svg('chevron-down')}</button>`
          + '<div class="fwide" hidden></div>';
        // Typing draws once it pauses: a list can be 1,000 long.
        el.addEventListener('input', (e) => {
          // A column's own find box hides the values that don't match, there and then.
          if (e.target.dataset.fwq !== undefined) {
            const c = e.target.closest('.fw-col');
            const q = e.target.value.trim().toLowerCase();
            finds[c.dataset.k] = e.target.value;
            c.querySelectorAll('.xpick-row').forEach((r) => { r.hidden = !!q && !r.textContent.toLowerCase().includes(q); });
            return;
          }
          if (e.target.dataset.fb !== 'text') return;
          st().text = e.target.value;
          clearTimeout(typing);
          typing = setTimeout(change, 150);
        });
        el.addEventListener('change', (e) => {
          // A value ticked in the opened-out bar.
          if (e.target.dataset.fw !== undefined) {
            const set = (st().chips[e.target.closest('.fw-col').dataset.k] ||= new Set());
            if (e.target.checked) set.add(e.target.dataset.fw);
            else set.delete(e.target.dataset.fw);
            change();
            return;
          }
          const f = e.target.dataset.fb;
          if (f !== 'col' && f !== 'op') return;
          const s = st();
          const was = kindOf(searchCol());
          s[f] = e.target.value;
          // Another kind of column has other tests, and what was typed for the last one means nothing to it.
          const kind = kindOf(searchCol());
          s.op = opFor(kind, s.op);
          if (kind !== was) {
            s.text = '';
            el.querySelector('[data-fb="text"]').value = '';
          }
          change();
        });
        el.addEventListener('click', (e) => {
          const t = e.target.closest('[data-fb]');
          if (!t) return;
          const act = { load, save, more, clear: b.clear, adv: advanced, chip: () => values(t), wide: wideToggle, wfind: wideFind }[t.dataset.fb];
          if (act) act(t);
        });
      }
      // The columns to search in follow the list that shows.
      const col = el.querySelector('[data-fb="col"]');
      const sig = `${name()}|${g.cols().map((c) => c.key).join(',')}`;
      if (el.dataset.cols !== sig) {
        el.dataset.cols = sig;
        if (s.col !== '*' && !colOf(s.col)) s.col = '*';
        col.innerHTML = `<option value="*">Any column</option>${g.cols().map((c) => `<option value="${esc(c.key)}">${esc(c.label)}</option>`).join('')}`;
      }
      col.value = s.col;
      // The tests for that kind of column; a number or days are typed, yes or no and never need nothing typed.
      const kind = kindOf(searchCol());
      s.op = opFor(kind, s.op);
      const opBox = el.querySelector('[data-fb="op"]');
      if (opBox.dataset.kind !== kind) {
        opBox.dataset.kind = kind;
        opBox.innerHTML = QUICK[kind].map(([k, w]) => `<option value="${k}">${esc(w)}</option>`).join('');
      }
      opBox.value = s.op;
      const box = el.querySelector('[data-fb="text"]');
      const noun = of(o.noun) || 'this list';
      box.hidden = QUICK_BARE.includes(s.op);
      box.inputMode = QUICK_BOX[kind] ? 'decimal' : 'search';
      box.placeholder = QUICK_BOX[kind] || `Search ${noun}`;
      box.setAttribute('aria-label', QUICK_BOX[kind] || `Search ${noun}`);
      if (box.value !== s.text && document.activeElement !== box) box.value = s.text;
      const advOn = s.adv && fields();
      const keys = chipKeys();
      drawWide(keys);
      el.querySelector('.fchips').innerHTML = keys.map((k) => {
        const sel = [...(s.chips[k] || [])];
        return `<button type="button" class="fchip${sel.length ? ' set' : ''}" data-fb="chip" data-k="${esc(k)}"><span>${esc(colOf(k).label)}</span>`
          + `${sel.length ? `<b class="ell">${esc(sel.length === 1 ? sel[0] : `${sel.length} picked`)}</b>` : ''}${svg('chevron-down', 'chev')}</button>`;
      }).join('') + (advOn ? `<button type="button" class="fchip set" data-fb="adv" data-tip="${esc(words(fields(), s.adv))}">${svg('filter')}<span>Advanced</span><b>${count(fields(), s.adv)} ${count(fields(), s.adv) === 1 ? 'condition' : 'conditions'}</b></button>` : '');
      el.querySelector('[data-fb="clear"]').hidden = !b.any();
    };

    // A filter's values, each with how many rows have it once everything else picked is applied.
    const valuesOf = (k) => {
      const c = colOf(k);
      const g = grid();
      const set = (st().chips[k] ||= new Set());
      const n = new Map();
      const rank = new Map();
      o.rows().filter(b.test(k)).forEach((x) => { const v = g.text(c, x) || NONE; n.set(v, (n.get(v) || 0) + 1); if (!rank.has(v)) rank.set(v, c.get(x)); });
      // In the column's own order when it sorts by something other than its words (a status), else by name; None last.
      const ranked = [...rank.values()].every((r) => typeof r === 'number');
      const order = (a, z) => (a === NONE) - (z === NONE) || (ranked && rank.has(a) && rank.has(z) ? rank.get(a) - rank.get(z) : 0) || a.localeCompare(z, undefined, { numeric: true, sensitivity: 'base' });
      return [...new Set([...n.keys(), ...set])].sort(order).map((v) => ({ key: v, label: v, n: n.get(v) || 0 }));
    };
    // A chip opens its values in the picker.
    const values = (button) => {
      const k = button.dataset.k;
      R.picker.open(button, { key: `chip.${name()}.${k}`, stars: false, items: valuesOf(k), picked: st().chips[k], change });
    };
    // The bar opened out (the caret at its end): every filter side by side with its values and counts, to tick in place.
    const wideKey = `${o.store || 'rabbidlab.grid'}.filter.wide`;
    const finds = {}; // what's typed in a column's own find box, by filter
    const drawWide = (keys) => {
      const wide = el.querySelector('.fwide');
      const open = !!read(wideKey);
      el.querySelector('[data-fb="wide"]').setAttribute('aria-expanded', String(open));
      wide.hidden = !open;
      if (!open) return;
      // Where each column was scrolled to, put back after the redraw.
      const left = wide.scrollLeft;
      const tops = Object.fromEntries([...wide.querySelectorAll('.fw-col')].map((c) => [c.dataset.k, c.querySelector('.fw-rows').scrollTop]));
      wide.innerHTML = keys.map((k) => {
        const q = finds[k];
        const set = st().chips[k] || new Set();
        const rows = valuesOf(k).map((v) => `<label class="xpick-row"${q && !v.label.toLowerCase().includes(q.toLowerCase()) ? ' hidden' : ''}><input type="checkbox" data-fw="${esc(v.key)}"${set.has(v.key) ? ' checked' : ''}>`
          + `<span class="ell">${esc(v.label)}</span><small class="xpick-n${v.n ? '' : ' zero'}">${v.n.toLocaleString()}</small></label>`).join('');
        return `<div class="fw-col" data-k="${esc(k)}"><div class="fw-head">${q == null ? `<span class="ell">${esc(colOf(k).label)}</span>`
          : `<input type="search" data-fwq value="${esc(q)}" placeholder="Find in ${esc(colOf(k).label)}" aria-label="Find in ${esc(colOf(k).label)}" autocomplete="off" spellcheck="false">`}`
          + `<button type="button" class="gcols" data-fb="wfind" data-tip="${q == null ? 'Find a value' : 'Close'}" aria-label="Find a value">${svg(q == null ? 'search' : 'x')}</button></div>`
          + `<div class="fw-rows">${rows || '<p class="xpick-none">Nothing to pick.</p>'}</div></div>`;
      }).join('') || '<p class="xpick-none">No filters show. Add some with Add/remove filters.</p>';
      wide.scrollLeft = left;
      wide.querySelectorAll('.fw-col').forEach((c) => { c.querySelector('.fw-rows').scrollTop = tops[c.dataset.k] || 0; });
    };
    const wideToggle = () => {
      keep(wideKey, read(wideKey) ? null : true);
      b.draw();
    };
    // A column's magnifier: a box to find a value in it, or back to its name.
    const wideFind = (button) => {
      const k = button.closest('.fw-col').dataset.k;
      finds[k] = finds[k] == null ? '' : null;
      b.draw();
      el.querySelector(`.fw-col[data-k="${CSS.escape(k)}"] [data-fwq]`)?.focus();
    };
    // Add/remove filters: which columns have a chip.
    const more = (button) => {
      const cand = candidates();
      const shown = chipKeys(cand);
      const on = new Set(shown);
      R.picker.open(button, { key: `chips.${name()}`, picked: on,
        items: grid().cols().filter((c) => cand.includes(c) || on.has(c.key)).map((c) => ({ key: c.key, label: c.label })),
        change: () => {
          keep(`${name()}.chips`, [...on]);
          Object.keys(st().chips).forEach((k) => { if (!on.has(k)) delete st().chips[k]; });
          change();
        } });
    };
    // Load filter: the ones saved in this browser, and the conditions window.
    const load = (button) => {
      const saved = read(`${name()}.saved`) || [];
      R.picker.menu(button, [{ head: 'Saved filters' },
        ...(saved.length ? saved.map((f, i) => ({ label: f.name, icon: 'filter', run: () => use(f), remove: () => { saved.splice(i, 1); keep(`${name()}.saved`, saved.length ? saved : null); } }))
          : [{ label: 'Nothing saved yet', off: true }]),
        ...(fields() ? ['-', { label: 'Advanced filter…', icon: 'columns', on: !!st().adv, run: advanced }] : [])], { key: `load.${name()}` });
    };
    const use = (f) => {
      const s = st();
      s.chips = Object.fromEntries(Object.entries(f.chips || {}).filter(([k]) => colOf(k)).map(([k, v]) => [k, new Set(v)]));
      s.col = f.col && colOf(f.col) ? f.col : '*';
      s.op = f.op || 'contains'; // drawing puts it right for the column's kind
      s.text = f.text || '';
      s.adv = f.adv || null;
      // Its chips show, whatever was chosen under Add/remove filters.
      const shown = chipKeys();
      const missing = Object.keys(s.chips).filter((k) => !shown.includes(k));
      if (missing.length) keep(`${name()}.chips`, [...shown, ...missing]);
      el.querySelector('[data-fb="text"]').value = s.text;
      change();
    };
    const save = () => {
      const s = st();
      const text = say();
      if (!text) { R.toast('Nothing to save.', 'Pick a filter or search first.'); return; }
      const saved = (read(`${name()}.saved`) || []).filter((f) => f.name !== text);
      saved.push({ name: text, chips: Object.fromEntries(Object.entries(s.chips).filter(([, set]) => set.size).map(([k, set]) => [k, [...set]])), col: s.col, op: s.op, text: s.text.trim(), adv: s.adv });
      keep(`${name()}.saved`, saved);
      R.toast('Filter saved.', 'It is under Load filter.');
    };
    // The conditions window, for what a chip can't say: older than, does not contain, any of these or all.
    const advanced = () => {
      const noun = of(o.noun) || 'rows';
      open({ title: `Filter ${noun}`, fields: fields(), noun, now: st().adv, rows: () => o.rows(), apply: (flt) => { st().adv = flt; change(); } });
    };
    b.clear = () => {
      const s = st();
      s.chips = {};
      s.text = '';
      s.adv = null;
      s.col = '*'; // a column with a test that needs nothing typed (is yes, never) would still filter
      s.op = 'contains';
      if (el.dataset.fbar) el.querySelector('[data-fb="text"]').value = '';
      change();
    };
    // What's picked in one chip, such as the groups on Links (a new link starts in the one group picked).
    b.picked = (k) => [...(st().chips[k] || [])];
    // A value that was renamed (a group) stays picked under its new name. The page draws itself after.
    b.rename = (k, from, to) => {
      const set = st().chips[k];
      if (set?.delete(from)) set.add(to);
    };
    // The search box emptied without drawing, for a page that's about to draw anyway.
    b.forget = () => {
      st().text = '';
      if (el.dataset.fbar) el.querySelector('[data-fb="text"]').value = '';
    };
    return b;
  };

  R.filter = { open, test, count, words, bar };
})();
