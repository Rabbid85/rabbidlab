// Data grids, for every table: the one place columns come from. A page hands R.grid its columns and rows and the grid
// does the rest: a heading sorts (again turns it round), its ⋮ menu sorts, pins it to the left or right, groups by it,
// hides it, fits every column or picks which show, a heading's right edge drags to resize and a double-click fits it to
// what's in it, the button at the end of the headings picks what shows, Group by gathers the rows under a heading for
// each value, and rows are Compact or Relaxed. Columns, widths, pins, sort and grouping are kept in this browser for
// each grid; Compact or Relaxed is one setting for every list.
(() => {
  const R = window.RabbidLab;
  const { esc, svg } = R;
  const read = (k) => { try { return JSON.parse(localStorage.getItem(k) || 'null'); } catch { return null; } };
  const keep = (k, v) => { try { if (v == null) localStorage.removeItem(k); else localStorage.setItem(k, JSON.stringify(v)); } catch { /* not kept, then */ } };
  const DENSITY = 'rabbidlab.ad.density';
  const density = () => (read(DENSITY) === 'relaxed' ? 'relaxed' : 'compact');
  const GAP = 20; // between columns, with the line in the middle
  const PAD = 16; // a row's own padding at each side (console.css .tr)
  const EXT = GAP / 2; // a pinned cell reaches half way into the gap at each side, so nothing shows through
  const of = (v) => (typeof v === 'function' ? v() : v);
  const blank = '<span class="muted">-</span>';
  // What a cell says, without its markup: for grouping, filtering and finding.
  const ENT = { '&amp;': '&', '&lt;': '<', '&gt;': '>', '&quot;': '"', '&#39;': "'" };
  const plain = (html) => String(html).replace(/<[^>]*>/g, '').replace(/&(amp|lt|gt|quot|#39);/g, (m) => ENT[m]).trim();
  // What a cell shows as plain words ('' for a blank one). A column whose cell says more than its value (Down 5 min) gives the value as word(x).
  const text = (c, x) => {
    const v = c.word ? c.word(x) : c.cell ? plain(c.cell(x)) : c.get(x);
    return v == null || v === '-' ? '' : String(v);
  };
  const tools = new WeakMap(); // a list bar's tools element -> the grid it's showing

  // While a heading is dragged: its label following the pointer, and a line down the table where it lands.
  const ghost = document.createElement('div');
  ghost.className = 'col-ghost';
  ghost.hidden = true;
  const line = document.createElement('div');
  line.className = 'col-line';
  line.hidden = true;
  document.body.append(ghost, line);
  const MOVE = 6; // pixels a press moves before it's a drag and not a click
  const EDGE = 48; // this near a table's side while dragging, it scrolls that way

  // o: { el, key, store, cols, sort, lead: { w, head, cell }, tail: { w, cell }, row(x, i), rowClass(x), empty, loading,
  // redraw, headButton }. el, key, cols, sort, lead, tail and empty can be functions, for a grid that shows different pages.
  // loading: true (or a function saying so) while the list is still being read, so it shows grey placeholder rows
  // under its headings and not what empty says.
  // Columns is the button at the end of the heading row (headButton: false leaves it out). el is the table for
  // draw(); for a page with several tables sharing the columns (one per group), el holds them and table() makes each.
  // A column: { key, label, w, get(x), cell(x), text(x), word(x), on, fixed, nosort, nogroup }.
  const grid = (o) => {
    const el = () => of(o.el);
    const name = (what) => `${o.store || 'rabbidlab.grid'}.${what}.${of(o.key)}`;
    // Every column, in the order it was dragged to (new ones keep their place at the end).
    const cols = () => {
      const all = of(o.cols) || [];
      const order = read(name('order'));
      if (!Array.isArray(order)) return all;
      const at = (c) => { const i = order.indexOf(c.key); return i < 0 ? 1000 + all.indexOf(c) : i; };
      return [...all].sort((a, b) => at(a) - at(b));
    };
    const g = {};
    g.cols = cols;
    g.text = text;
    // Group by: on or off, and the columns the rows are gathered by, outermost first.
    const groupSet = () => { const s = read(name('group')); return { on: !!s?.on, keys: Array.isArray(s?.keys) ? s.keys : [] }; };
    const groupCols = () => { const s = groupSet(); return s.on ? s.keys.map((k) => cols().find((c) => c.key === k && !c.nogroup)).filter(Boolean) : []; };
    const folded = new Set(); // the groups that are closed, until the page is opened again
    // Pinned columns: { key: 'left' | 'right' }. They show first or last, and stay put when the grid scrolls sideways.
    const pins = () => { const p = read(name('pins')); return p && typeof p === 'object' && !Array.isArray(p) ? p : {}; };
    g.shown = () => {
      const keys = read(name('cols'));
      const p = pins();
      const side = (c) => (p[c.key] === 'left' ? 0 : p[c.key] === 'right' ? 2 : 1);
      return cols().filter((c) => c.fixed || (Array.isArray(keys) ? keys.includes(c.key) : c.on)).sort((a, b) => side(a) - side(b));
    };
    g.sortBy = () => {
      const s = read(name('sort'));
      return s && cols().some((c) => c.key === s.key && !c.nosort) ? s : of(o.sort) || null;
    };
    const widths = () => read(name('widths')) || {};
    const widthOf = (c, wd = widths()) => wd[c.key] || c.w || 150;
    // The tracks: the lead (a tick), each column at its width in pixels, and the tail taking what's left.
    const setTracks = (shown, list = shown.map((c) => `${widthOf(c)}px`)) => {
      const lead = of(o.lead);
      const tail = Math.max(of(o.tail)?.w || 8, o.headButton === false ? 0 : 30); // room for Columns at the end
      const t = el();
      t.style.setProperty('--cols', `${lead ? `${lead.w}px ` : ''}${list.join(' ')} minmax(${tail}px, 1fr)`);
      t.style.setProperty('--minw', `${(lead ? lead.w + GAP : 0) + shown.reduce((n, c) => n + widthOf(c) + GAP, 0) + tail + 32}px`);
      // Where each pinned column stays: its outer edge's distance from its side (a sticky cell is held by its border box).
      const px = list.map((w) => parseFloat(w));
      if (px.some((w) => Number.isNaN(w))) return; // being measured
      const p = pins();
      let x = PAD + (lead ? lead.w + GAP : 0);
      shown.forEach((c, i) => { if (p[c.key] === 'left') t.style.setProperty(`--gpin${i}`, `${x === PAD ? 0 : x - EXT}px`); x += px[i] + GAP; });
      let r = PAD + tail + GAP;
      for (let i = shown.length - 1; i >= 0; i -= 1) if (p[shown[i].key] === 'right') { t.style.setProperty(`--gpin${i}`, `${r - EXT}px`); r += px[i] + GAP; }
    };
    // Blanks go last either way.
    g.sorted = (list) => {
      const s = g.sortBy();
      const c = s && cols().find((x) => x.key === s.key);
      if (!c) return list;
      const val = (x) => { const v = c.get(x); return typeof v === 'number' ? v : String(v ?? '').toLowerCase(); };
      return [...list].sort((a, b) => {
        const x = val(a);
        const y = val(b);
        if (x === '' || y === '') return x === y ? 0 : x === '' ? 1 : -1;
        return (x < y ? -1 : x > y ? 1 : 0) * s.dir;
      });
    };
    // The heading and the rows, sorted and grouped: { rows (in the order shown), html }.
    const body = (list) => {
      const sorted = g.sorted(list);
      const shown = g.shown();
      const s = g.sortBy();
      const lead = of(o.lead);
      const tail = of(o.tail);
      const by = groupCols();
      setTracks(shown);
      // Pinned cells: left ones first, with a line after the last; right ones last, with a line before the first. With
      // any pinned, the ticks (left) or the row's buttons (right) stay put beside them.
      const p = pins();
      const lefts = shown.filter((c) => p[c.key] === 'left').length;
      const rights = shown.filter((c) => p[c.key] === 'right').length;
      const pin = shown.map((c, i) => (i < lefts ? { cls: ` pin pin-l${i === lefts - 1 ? ' last' : ''}${i === 0 && !lead ? ' edge-l' : ''}`, at: ` style="left:var(--gpin${i})"` }
        : i >= shown.length - rights ? { cls: ` pin pin-r${i === shown.length - rights ? ' first' : ''}`, at: ` style="right:var(--gpin${i})"` } : { cls: '', at: '' }));
      const leadPin = lefts ? ' pin pin-l edge-l" style="left:0' : '';
      const tailPin = rights ? ' pin pin-r edge-r" style="right:0' : '';
      const head = shown.map((c, i) => `<div class="th-cell${pin[i].cls}" data-grid-col="${c.key}"${pin[i].at}>${c.nosort ? `<span class="th-sort still"><span class="lbl">${esc(c.label)}</span></span>`
        : `<button type="button" class="th-sort${s?.key === c.key ? ' on' : ''}" data-grid-sort="${c.key}" title="${esc(c.label)}"><span class="lbl">${esc(c.label)}</span>`
          + `${s?.key === c.key ? svg(s.dir > 0 ? 'arrow-up' : 'arrow-down', 'arr') : ''}</button>`}`
        + `<button type="button" class="th-menu" data-grid-menu="${c.key}" aria-label="${esc(c.label)}: sort, pin, group, hide">${svg('dots')}</button><i class="th-grip" data-grid-grip="${c.key}" data-tip="Drag to resize"></i></div>`).join('');
      const button = o.headButton === false ? '' : `<button type="button" class="gcols" data-grid-cols data-tip="Columns" aria-label="Columns">${svg('columns')}</button>`;
      // rows: in the order shown, so a row's number is its place in what draw() gives back, grouped or not.
      const rows = [];
      const row = (x) => {
        const i = rows.push(x) - 1;
        return `<div class="tr${o.rowClass ? ` ${o.rowClass(x)}` : ''}"${o.row ? o.row(x, i) : ''}>${lead ? `<div class="lead${leadPin}">${lead.cell(x, i)}</div>` : ''}`
          + shown.map((c, k) => { const v = c.cell ? c.cell(x) : c.get(x); return `<div${pin[k].cls ? ` class="${pin[k].cls.trim()}"${pin[k].at}` : ''}>${c.cell ? v : v === '' || v == null ? blank : `<span class="ell">${esc(v)}</span>`}</div>`; }).join('')
          + `<div class="tr-tail${tailPin}">${tail?.cell ? tail.cell(x, i) : ''}</div></div>`;
      };
      // A heading for each value of the column grouped by, then its rows (or the next column's groups) unless it's closed.
      const groups = (part, depth, path) => {
        if (depth >= by.length) return part.map(row).join('');
        const c = by[depth];
        const sets = new Map();
        part.forEach((x) => { const v = g.text(c, x) || 'None'; if (!sets.has(v)) sets.set(v, []); sets.get(v).push(x); });
        const rank = (v) => { const r = c.get(v); return typeof r === 'number' ? r : String(r ?? '').toLowerCase(); };
        return [...sets].sort((a, b) => { const x = rank(a[1][0]); const y = rank(b[1][0]); return x < y ? -1 : x > y ? 1 : 0; }).map(([v, xs]) => {
          const id = `${of(o.key)}${path}/${v}`;
          const shut = folded.has(id);
          if (shut) rows.push(...xs); // still part of the list: ticking everything counts them
          return `<div class="tr grp${shut ? ' shut' : ''}" data-grid-group="${esc(id)}" data-depth="${Math.min(depth, 3)}" tabindex="0"><div class="grp-cell">${svg('chevron-down', 'grp-chev')}`
            + `<span class="ell">${esc(c.label)}: ${esc(v)}</span><small>${xs.length.toLocaleString()}</small></div></div>${shut ? '' : groups(xs, depth + 1, `${path}/${v}`)}`;
        }).join('');
      };
      const html = `<div class="tr th">${lead ? `<div class="lead${leadPin}">${lead.head || ''}</div>` : ''}${head}<div class="tr-tail${tailPin}">${button}</div></div>`
        + (sorted.length ? groups(sorted, 0, '') : `<div class="empty">${of(o.empty) || 'Nothing here.'}</div>`);
      return { rows, html };
    };
    // Draws the table that el is, and gives back the rows in the order shown.
    g.draw = (list) => {
      const t = el();
      const { rows, html } = body(list);
      t.classList.add('dgrid');
      t.classList.toggle('compact', density() === 'compact');
      t.classList.toggle('relaxed', density() === 'relaxed');
      t.innerHTML = html;
      // Still being read: placeholder rows where the empty text would be.
      const none = !rows.length && of(o.loading) ? t.querySelector(':scope > .empty') : null;
      if (none) {
        none.className = 'grid-loading';
        if (t.clientWidth) none.style.width = `${t.clientWidth}px`; // as wide as what shows, however wide the columns are
        R.skeleton(none, 'rows', 8);
      }
      shownNow = rows.length;
      if (bar) drawTools();
      return rows;
    };
    // The grid's part of a list bar: Group by with its switch and what it groups by, how many rows show, and Compact
    // or Relaxed. A page gives the element once (again each draw, where several grids share one bar).
    let bar = null;
    let rowsBar = null; // where Compact or Relaxed goes, when not at the end of the bar
    let shownNow = 0;
    // How many rows show; with some ticked (o.picked), how many of them: 3/7 Items selected.
    const count = () => {
      const ticked = of(o.picked) || 0;
      return ticked ? `${ticked.toLocaleString()}/${shownNow.toLocaleString()} ${shownNow === 1 ? 'Item' : 'Items'} selected`
        : `${shownNow.toLocaleString()} ${shownNow === 1 ? 'Item' : 'Items'}`;
    };
    // The count again, after the ticks changed without the rows being drawn.
    g.recount = () => {
      const el = bar && tools.get(bar) === g ? bar.querySelector('.gcount') : null;
      if (el) el.textContent = count();
    };
    const drawTools = () => {
      if (tools.get(bar) !== g) return; // another grid has the bar now
      const s = groupSet();
      const by = s.keys.map((k) => cols().find((c) => c.key === k)?.label).filter(Boolean).join(', ');
      const rows = `<div class="vswitch">${[['compact', 'Compact rows', 'view-list'], ['relaxed', 'Relaxed rows', 'view-rows']].map(([d, tip, icon]) => `<button type="button" data-density="${d}" aria-pressed="${density() === d}" data-tip="${tip}" aria-label="${tip}">${svg(icon)}</button>`).join('')}</div>`;
      bar.innerHTML = `<label class="gby"><input type="checkbox" class="switch" data-grid-group-on${s.on && by ? ' checked' : ''}><span>Group by</span></label>`
        + `<button type="button" class="gby-pick" data-grid-group-pick>${esc(by || 'Pick a column')}${svg('chevron-down', 'chev')}</button>`
        + `<span class="gcount">${count()}</span>${rowsBar ? '' : `<span class="grow"></span>${rows}`}`;
      if (rowsBar) rowsBar.innerHTML = rows;
    };
    const pickGroup = (button) => {
      const s = groupSet();
      R.picker.open(button, { key: `group.${name('g')}`, items: cols().filter((c) => !c.fixed && !c.nogroup).map((c) => ({ key: c.key, label: c.label })), picked: new Set(s.keys),
        change: (picked) => { keep(name('group'), { on: picked.size > 0, keys: [...picked] }); o.redraw(); } });
    };
    // el: the list bar's part for this grid (Group by, the count). rows: where Compact or Relaxed goes instead of the
    // end of el, such as the command bar under the banner.
    g.tools = (el, rows = null) => {
      bar = el;
      rowsBar = rows;
      tools.set(el, g);
      if (rows) tools.set(rows, g);
      el.classList.add('gtools');
      for (const node of [el, rows].filter(Boolean)) {
        if (node.dataset.gridTools) continue;
        node.dataset.gridTools = '1';
        // One set of listeners for the element: they go to whichever grid has it.
        node.addEventListener('change', (e) => { if (e.target.matches('[data-grid-group-on]')) tools.get(node).groupOn(e.target); });
        node.addEventListener('click', (e) => {
          const d = e.target.closest('[data-density]');
          if (d) tools.get(node).density(d.dataset.density);
          else if (e.target.closest('[data-grid-group-pick]')) tools.get(node).pickGroup(e.target.closest('[data-grid-group-pick]'));
        });
      }
    };
    g.pickGroup = pickGroup;
    g.density = (d) => { keep(DENSITY, d); o.redraw(); };
    // The switch: off, or on with what was picked; with nothing picked yet it opens the picker.
    g.groupOn = (box) => {
      const s = groupSet();
      if (box.checked && !s.keys.length) {
        box.checked = false;
        const pick = bar.querySelector('[data-grid-group-pick]');
        pickGroup(pick.offsetParent ? pick : box.closest('.gby')); // under the chip when the pick is hidden
        return;
      }
      keep(name('group'), { on: box.checked, keys: s.keys });
      o.redraw();
    };
    // One of several tables inside el, as HTML (cls: its own classes).
    g.table = (list, cls = '') => `<div class="table dgrid ${density()}${cls ? ` ${cls}` : ''}">${body(list).html}</div>`;
    // The rows given, with the columns that show and in the order shown, as a CSV download. A column's text() is what
    // it exports as, when get() is only for sorting.
    g.csv = (list, file) => {
      const cell = (v) => {
        // A leading quote keeps Excel from running a value that starts like a formula.
        const s = (v === null || v === undefined ? '' : String(v)).replace(/^[=+\-@\t\r]/, "'$&");
        return /[",\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s;
      };
      const shown = g.shown();
      const text = [shown.map((c) => cell(c.label)).join(','), ...g.sorted(list).map((x) => shown.map((c) => cell(c.text ? c.text(x) : c.get(x))).join(','))].join('\r\n');
      const a = document.createElement('a');
      a.href = URL.createObjectURL(new Blob([`\uFEFF${text}`], { type: 'text/csv' }));
      a.download = `${file}.csv`;
      a.click();
      setTimeout(() => URL.revokeObjectURL(a.href), 5000);
    };
    // Columns: a tick for each column that can be hidden, and Reset (picker.js draws it).
    g.menu = (button) => {
      R.picker.open(button, { key: `cols.${name('g')}`, right: true, stars: false, clear: false,
        items: cols().filter((c) => !c.fixed).map((c) => ({ key: c.key, label: c.label })), picked: new Set(g.shown().filter((c) => !c.fixed).map((c) => c.key)),
        change: (picked) => { keep(name('cols'), [...picked]); o.redraw(); },
        foot: [{ label: 'Reset columns, order, widths and sort', icon: 'refresh', run: () => { ['cols', 'widths', 'sort', 'order', 'group', 'pins'].forEach((w) => keep(name(w), null)); o.redraw(); } }] });
    };
    // Fits columns to what's in them: every cell measured at its natural width.
    const fit = (keys) => {
      const shown = g.shown();
      const t = el();
      const wd = widths();
      const on = shown.map((c) => keys.includes(c.key));
      if (!on.some(Boolean)) return;
      t.classList.add('measuring');
      setTracks(shown, shown.map((c, i) => (on[i] ? 'max-content' : `${widthOf(c, wd)}px`)));
      const before = of(o.lead) ? 1 : 0;
      shown.forEach((c, i) => {
        if (!on[i]) return;
        const widest = Math.max(...[...t.querySelectorAll(`.tr:not(.grp) > :nth-child(${i + 1 + before})`)].map((cell) => cell.getBoundingClientRect().width));
        wd[c.key] = Math.min(700, Math.max(60, Math.ceil(widest) + 4));
      });
      t.classList.remove('measuring');
      keep(name('widths'), wd);
      setTracks(shown);
    };
    // A heading's ⋮: sort by it either way, pin it to a side, group by it, hide it, fit every column, pick which show.
    const colMenu = (button) => {
      const c = cols().find((x) => x.key === button.dataset.gridMenu);
      if (!c) return;
      const s = groupSet();
      const grouped = s.on && s.keys.length === 1 && s.keys[0] === c.key;
      const sort = (dir) => () => { keep(name('sort'), { key: c.key, dir }); o.redraw(); };
      const side = pins()[c.key];
      const pin = (to) => () => { const p = pins(); if (to) p[c.key] = to; else delete p[c.key]; keep(name('pins'), Object.keys(p).length ? p : null); o.redraw(); };
      const unpin = { label: 'Unpin', icon: 'pin-off', run: pin(null) };
      R.picker.menu(button, [
        ...(c.nosort ? [] : [{ label: 'Sort A to Z', icon: 'sort', run: sort(1) }, { label: 'Sort Z to A', icon: 'sort-za', run: sort(-1) }, '-']),
        side === 'left' ? unpin : { label: 'Pin to left', icon: 'pin', run: pin('left') },
        side === 'right' ? unpin : { label: 'Pin to right', icon: 'pin-right', run: pin('right') },
        '-',
        ...(c.fixed || c.nogroup ? [] : [grouped ? { label: 'Stop grouping', icon: 'list', run: () => { keep(name('group'), { on: false, keys: s.keys }); o.redraw(); } }
          : { label: 'Group by this column', icon: 'list', run: () => { keep(name('group'), { on: true, keys: [c.key] }); o.redraw(); } }]),
        ...(c.fixed ? [] : [{ label: 'Hide column', icon: 'eye-off', run: () => { keep(name('cols'), g.shown().filter((x) => !x.fixed && x.key !== c.key).map((x) => x.key)); o.redraw(); } }]),
        '-',
        { label: 'Autosize all columns', icon: 'fit', run: () => fit(g.shown().map((x) => x.key)) },
        { label: 'Choose columns', icon: 'columns', run: () => g.menu(button) },
      ], { key: `col.${name('g')}.${c.key}` });
    };

    // The heading: click to sort, drag a heading sideways to move its column, drag an edge to resize, double-click an
    // edge to fit. Listened for once on el, so a redraw keeps them.
    const t0 = el();
    let sizing = false; // an edge is being dragged
    let moving = null; // the column whose heading is being dragged
    // A column being resized or moved: a page that redraws on its own waits for it.
    g.busy = () => sizing || !!moving;
    t0.addEventListener('click', (e) => {
      const cb = e.target.closest('[data-grid-cols]');
      if (cb) { g.menu(cb); return; }
      const mb = e.target.closest('[data-grid-menu]');
      if (mb && el().contains(mb)) { colMenu(mb); return; }
      // A group's heading opens and closes it.
      const gr = e.target.closest('[data-grid-group]');
      if (gr && el().contains(gr)) {
        if (folded.has(gr.dataset.gridGroup)) folded.delete(gr.dataset.gridGroup);
        else folded.add(gr.dataset.gridGroup);
        o.redraw();
        return;
      }
      const b = e.target.closest('[data-grid-sort]');
      if (!b || !el().contains(b)) return;
      const s = g.sortBy();
      keep(name('sort'), { key: b.dataset.gridSort, dir: s?.key === b.dataset.gridSort ? -s.dir : 1 });
      o.redraw();
    });
    // The click the browser sends after a drag lets go isn't a sort.
    const eatClick = () => {
      const eat = (c) => { c.stopPropagation(); c.preventDefault(); };
      window.addEventListener('click', eat, { capture: true, once: true });
      setTimeout(() => window.removeEventListener('click', eat, true), 0);
    };
    // Every drag ends the same way: on letting go, on Esc (back as it was), or when the window loses focus.
    const follow = (move, end) => {
      const up = () => done(true);
      const cancel = () => done(false);
      const esc = (k) => { if (k.key === 'Escape') { k.preventDefault(); done(false); } };
      const done = (ok) => {
        window.removeEventListener('pointermove', move);
        window.removeEventListener('pointerup', up);
        ['pointercancel', 'blur'].forEach((ev) => window.removeEventListener(ev, cancel));
        window.removeEventListener('keydown', esc, true);
        end(ok);
      };
      window.addEventListener('pointermove', move);
      window.addEventListener('pointerup', up);
      ['pointercancel', 'blur'].forEach((ev) => window.addEventListener(ev, cancel));
      window.addEventListener('keydown', esc, true);
    };

    // Resizing: the edge line follows the pointer, and the width is kept once it's let go.
    const resize = (e, grip) => {
      e.preventDefault();
      const key = grip.dataset.gridGrip;
      const start = e.clientX;
      const from = grip.closest('.th-cell').getBoundingClientRect().width;
      const shown = g.shown();
      const wd = widths();
      const was = wd[key];
      let frame = 0;
      sizing = true;
      grip.classList.add('on');
      document.body.classList.add('col-dragging');
      follow((m) => {
        wd[key] = Math.max(60, Math.round(from + m.clientX - start));
        if (!frame) frame = requestAnimationFrame(() => { frame = 0; setTracks(shown, shown.map((c) => `${widthOf(c, wd)}px`)); });
      }, (ok) => {
        cancelAnimationFrame(frame);
        if (!ok) { if (was == null) delete wd[key]; else wd[key] = was; }
        if (ok) keep(name('widths'), wd);
        setTracks(shown, shown.map((c) => `${widthOf(c, wd)}px`));
        grip.classList.remove('on');
        document.body.classList.remove('col-dragging');
        sizing = false;
        eatClick();
      });
    };

    // Moving: a press on a heading that travels sideways picks the column up. Its label follows the pointer, the column
    // fades, and a line down the table shows where it lands; near a side the table scrolls. A press that doesn't
    // travel is a click, and sorts.
    const pickUp = (e, th) => {
      const key = th.dataset.gridCol;
      const row = th.parentElement;
      const table = th.closest('.table') || el();
      const x0 = e.clientX;
      const y0 = e.clientY;
      let on = false;
      let at = -1; // where it lands, among the headings shown
      let x = x0;
      let roll = 0;
      const heads = () => [...row.querySelectorAll(':scope > [data-grid-col]')];
      const from = () => heads().findIndex((h) => h.dataset.gridCol === key);
      const cells = () => {
        const nth = from() + 1 + (row.querySelector(':scope > .lead') ? 1 : 0);
        return el().querySelectorAll(`.tr:not(.grp) > :nth-child(${nth})`);
      };
      const place = () => {
        const hs = heads();
        at = hs.findIndex((h) => { const r = h.getBoundingClientRect(); return x < r.left + r.width / 2; });
        if (at < 0) at = hs.length;
        const f = from();
        // Next to where it is changes nothing: no line.
        if (at === f || at === f + 1) { line.hidden = true; return; }
        const box = table.getBoundingClientRect();
        const lx = at === 0 ? hs[0].getBoundingClientRect().left - GAP / 2 : hs[at - 1].getBoundingClientRect().right + GAP / 2;
        const top = Math.max(box.top, 0);
        line.style.cssText = `left:${Math.round(Math.min(Math.max(lx, box.left + 2), box.right - 2) - 1)}px;top:${top}px;height:${Math.max(0, Math.min(box.bottom, innerHeight) - top)}px`;
        line.hidden = false;
      };
      // Near the table's left or right side, it scrolls that way, faster the nearer.
      const scroll = () => {
        roll = 0;
        if (!on) return;
        const box = table.getBoundingClientRect();
        const near = x < box.left + EDGE ? -(box.left + EDGE - x) : x > box.right - EDGE ? x - (box.right - EDGE) : 0;
        if (!near || table.scrollWidth <= table.clientWidth) return;
        const before = table.scrollLeft;
        table.scrollLeft += Math.sign(near) * Math.min(24, 4 + Math.abs(near) / 2);
        if (table.scrollLeft !== before) place();
        roll = requestAnimationFrame(scroll);
      };
      follow((m) => {
        x = m.clientX;
        if (!on) {
          if (Math.abs(m.clientX - x0) < MOVE && Math.abs(m.clientY - y0) < MOVE) return;
          on = true;
          moving = key;
          ghost.innerHTML = `${svg('move')}<span>${esc(th.textContent.trim())}</span>`;
          ghost.hidden = false;
          cells().forEach((c) => c.classList.add('col-lift'));
          document.body.classList.add('col-moving');
        }
        ghost.style.transform = `translate(${m.clientX + 14}px, ${m.clientY + 12}px)`;
        place();
        if (!roll) roll = requestAnimationFrame(scroll);
      }, (ok) => {
        cancelAnimationFrame(roll);
        roll = 0;
        if (!on) return; // a click: it sorts
        eatClick();
        cells().forEach((c) => c.classList.remove('col-lift'));
        document.body.classList.remove('col-moving');
        ghost.hidden = true;
        line.hidden = true;
        moving = null;
        const hs = heads().map((h) => h.dataset.gridCol);
        const f = hs.indexOf(key);
        if (!ok || at < 0 || at === f || at === f + 1) return;
        // In front of the heading it was dropped before (hidden columns keep their places), or after the last one.
        const order = cols().map((c) => c.key).filter((k) => k !== key);
        const before = hs[at];
        order.splice(before ? order.indexOf(before) : order.indexOf(hs[hs.length - 1]) + 1, 0, key);
        keep(name('order'), order);
        o.redraw();
      });
    };

    t0.addEventListener('pointerdown', (e) => {
      if (e.button !== 0) return;
      const grip = e.target.closest('[data-grid-grip]');
      if (grip) { resize(e, grip); return; }
      if (e.target.closest('[data-grid-menu]')) return; // its ⋮ is a button, not a place to drag from
      const th = e.target.closest('[data-grid-col]');
      if (th && el().contains(th)) pickUp(e, th);
    });
    // A double-click on an edge fits that column.
    t0.addEventListener('dblclick', (e) => {
      const grip = e.target.closest('[data-grid-grip]');
      if (grip) fit([grip.dataset.gridGrip]);
    });
    return g;
  };

  // A list that isn't a table (Links' tiles) still has columns to filter by: the cols() and text() a grid gives.
  grid.columns = (cols) => ({ cols: () => of(cols) || [], text });

  R.grid = grid;
})();
