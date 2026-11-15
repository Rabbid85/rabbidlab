// Filters, for every list that has one (Active Directory, Devices): conditions, each a field, a test and a value, and
// all of them or any. A page hands R.filter its fields ([group, [field…]] with key, label, type, get) and its rows; the
// Filter window, the tests and the filter in words are all here.
(() => {
  const R = window.RabbidLab;
  const { $, esc, svg } = R;
  const DAY = 86400000;

  // What each kind of field can be asked. Ops in NO_VALUE need nothing filled in.
  const OPS = {
    text: [['contains', 'contains'], ['notContains', 'does not contain'], ['is', 'is'], ['isNot', 'is not'], ['starts', 'starts with'], ['empty', 'is empty'], ['set', 'is not empty']],
    choice: [['in', 'is any of'], ['notIn', 'is none of'], ['empty', 'is empty'], ['set', 'is not empty']],
    member: [['in', 'is any of'], ['notIn', 'is none of']],
    ou: [['under', 'is in or under'], ['in', 'is directly in']],
    days: [['olderThan', 'more than'], ['within', 'in the last'], ['empty', 'never'], ['set', 'is set']],
    until: [['next', 'in the next'], ['past', 'has passed'], ['empty', 'never'], ['set', 'is set']],
    number: [['more', 'more than'], ['fewer', 'fewer than'], ['eq', 'exactly']],
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
          starts: (x) => lc(f.get(x)).startsWith(want), notContains: (x) => !lc(f.get(x)).includes(want) }[r.op] || ((x) => lc(f.get(x)).includes(want));
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
      case 'number': return r.op === 'more' ? (x) => num(f.get(x)) > n : r.op === 'fewer' ? (x) => num(f.get(x)) < n : (x) => num(f.get(x)) === n;
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

  R.filter = { open, test, count, words };
})();
