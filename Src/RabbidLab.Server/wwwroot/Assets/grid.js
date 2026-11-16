// Data grids, for every table: the one place columns come from. A page hands R.grid its columns and rows and the grid
// does the rest: a heading sorts (again turns it round), a heading's right edge drags to resize and a double-click fits
// it to what's in it, Columns picks what shows, and rows are Compact or Relaxed. Columns, widths and sort are kept in
// this browser for each grid; Compact or Relaxed is one setting for every list.
(() => {
  const R = window.RabbidLab;
  const { esc, svg } = R;
  const read = (k) => { try { return JSON.parse(localStorage.getItem(k) || 'null'); } catch { return null; } };
  const keep = (k, v) => { try { if (v == null) localStorage.removeItem(k); else localStorage.setItem(k, JSON.stringify(v)); } catch { /* not kept, then */ } };
  const DENSITY = 'rabbidlab.ad.density';
  const density = () => (read(DENSITY) === 'relaxed' ? 'relaxed' : 'compact');
  const GAP = 20; // between columns, with the line in the middle
  const of = (v) => (typeof v === 'function' ? v() : v);
  const blank = '<span class="muted">-</span>';

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

  // The Columns menu, one for every grid.
  const menu = document.createElement('div');
  menu.className = 'gear-menu col-menu';
  menu.setAttribute('popover', 'auto');
  document.body.append(menu);
  let menuFor = null;

  // o: { el, key, store, cols, sort, lead: { w, head, cell }, tail: { w, cell }, row(x, i), rowClass(x), empty, loading,
  // redraw, headButton }. el, key, cols, sort, lead, tail and empty can be functions, for a grid that shows different pages.
  // loading: true (or a function saying so) while the list is still being read, so it shows grey placeholder rows
  // under its headings and not what empty says.
  // headButton puts Columns at the end of the heading row, for a grid with no toolbar of its own. el is the table for
  // draw(); for a page with several tables sharing the columns (one per group), el holds them and table() makes each.
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
    g.shown = () => {
      const keys = read(name('cols'));
      return cols().filter((c) => c.fixed || (Array.isArray(keys) ? keys.includes(c.key) : c.on));
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
      const tail = of(o.tail)?.w || 8;
      const t = el();
      t.style.setProperty('--cols', `${lead ? `${lead.w}px ` : ''}${list.join(' ')} minmax(${tail}px, 1fr)`);
      t.style.setProperty('--minw', `${(lead ? lead.w + GAP : 0) + shown.reduce((n, c) => n + widthOf(c) + GAP, 0) + tail + 32}px`);
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
    // The heading and the rows, sorted: { rows (in the order shown), html }.
    const body = (list) => {
      const rows = g.sorted(list);
      const shown = g.shown();
      const s = g.sortBy();
      const lead = of(o.lead);
      const tail = of(o.tail);
      setTracks(shown);
      const head = shown.map((c) => `<div class="th-cell" data-grid-col="${c.key}">${c.nosort ? `<span class="th-sort still"><span class="lbl">${esc(c.label)}</span></span>`
        : `<button type="button" class="th-sort${s?.key === c.key ? ' on' : ''}" data-grid-sort="${c.key}" title="${esc(c.label)}"><span class="lbl">${esc(c.label)}</span>`
          + `${s?.key === c.key ? svg(s.dir > 0 ? 'chevron-up' : 'chevron-down', 'arr') : ''}</button>`}<i class="th-grip" data-grid-grip="${c.key}" data-tip="Drag to resize"></i></div>`).join('');
      const button = o.headButton ? `<button type="button" class="gcols" data-grid-cols data-tip="Columns" aria-label="Columns">${svg('columns')}</button>` : '';
      const html = `<div class="tr th">${lead ? `<div class="lead">${lead.head || ''}</div>` : ''}${head}<div class="tr-tail">${button}</div></div>`
        + (rows.length ? rows.map((x, i) => `<div class="tr${o.rowClass ? ` ${o.rowClass(x)}` : ''}"${o.row ? o.row(x, i) : ''}>${lead ? `<div class="lead">${lead.cell(x, i)}</div>` : ''}`
          + shown.map((c) => { const v = c.cell ? c.cell(x) : c.get(x); return `<div>${c.cell ? v : v === '' || v == null ? blank : `<span class="ell">${esc(v)}</span>`}</div>`; }).join('')
          + `<div class="tr-tail">${tail?.cell ? tail.cell(x, i) : ''}</div></div>`).join('')
          : `<div class="empty">${of(o.empty) || 'Nothing here.'}</div>`);
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
      return rows;
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
    // Columns: Compact or Relaxed, a tick for each column, and Reset.
    const drawMenu = () => {
      const on = g.shown().map((c) => c.key);
      const free = cols().filter((c) => !c.fixed);
      menu.innerHTML = `<div class="col-dens"><span>Rows</span><div class="seg">${[['compact', 'Compact'], ['relaxed', 'Relaxed']].map(([d, label]) => `<button type="button" data-density="${d}" aria-pressed="${density() === d}">${label}</button>`).join('')}</div></div>`
        + (free.length ? `<p class="cmd-head">Columns</p><div class="am-links col-list">${free.map((c) => `<label class="am-link col-check"><input type="checkbox" data-col="${c.key}"${on.includes(c.key) ? ' checked' : ''}><span>${esc(c.label)}</span></label>`).join('')}</div>` : '')
        + `<div class="am-links"><button type="button" class="am-link" data-col-reset>${svg('refresh')}<span>Reset columns, order, widths and sort</span></button></div>`;
    };
    g.menu = (button) => {
      if (menu.matches(':popover-open') && menuFor === g) { menu.hidePopover(); return; }
      menuFor = g;
      drawMenu();
      const r = button.getBoundingClientRect();
      menu.style.setProperty('left', '0px');
      menu.style.setProperty('top', '0px');
      menu.showPopover();
      menu.style.setProperty('left', `${Math.max(8, Math.min(r.right - menu.offsetWidth, innerWidth - menu.offsetWidth - 8))}px`);
      menu.style.setProperty('top', `${r.bottom + 6}px`);
    };
    g.menuChange = (e) => {
      const box = e.target.closest('[data-col]');
      if (!box) return;
      const keys = g.shown().filter((c) => !c.fixed).map((c) => c.key).filter((k) => k !== box.dataset.col);
      if (box.checked) keys.push(box.dataset.col);
      keep(name('cols'), keys);
      o.redraw();
    };
    g.menuClick = (e) => {
      const d = e.target.closest('[data-density]');
      if (d) {
        keep(DENSITY, d.dataset.density);
        drawMenu();
        o.redraw();
      } else if (e.target.closest('[data-col-reset]')) {
        ['cols', 'widths', 'sort', 'order'].forEach((w) => keep(name(w), null));
        drawMenu();
        o.redraw();
      }
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
        return el().querySelectorAll(`.tr > :nth-child(${nth})`);
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
      const th = e.target.closest('[data-grid-col]');
      if (th && el().contains(th)) pickUp(e, th);
    });
    // Fits a column to what's in it: every cell measured at its natural width.
    t0.addEventListener('dblclick', (e) => {
      const grip = e.target.closest('[data-grid-grip]');
      if (!grip) return;
      const shown = g.shown();
      const at = shown.findIndex((c) => c.key === grip.dataset.gridGrip);
      if (at < 0) return;
      const t = el();
      const wd = widths();
      t.classList.add('measuring');
      setTracks(shown, shown.map((c, i) => (i === at ? 'max-content' : `${widthOf(c, wd)}px`)));
      const nth = at + 1 + (of(o.lead) ? 1 : 0);
      const widest = Math.max(...[...t.querySelectorAll(`.tr > :nth-child(${nth})`)].map((cell) => cell.getBoundingClientRect().width));
      t.classList.remove('measuring');
      wd[grip.dataset.gridGrip] = Math.min(700, Math.max(60, Math.ceil(widest) + 4));
      keep(name('widths'), wd);
      setTracks(shown);
    });
    return g;
  };

  menu.addEventListener('change', (e) => menuFor?.menuChange(e));
  menu.addEventListener('click', (e) => menuFor?.menuClick(e));
  R.grid = grid;
})();
