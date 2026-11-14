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

  // The Columns menu, one for every grid.
  const menu = document.createElement('div');
  menu.className = 'gear-menu col-menu';
  menu.setAttribute('popover', 'auto');
  document.body.append(menu);
  let menuFor = null;

  // o: { el, key, store, cols, sort, lead: { w, head, cell }, tail: { w, cell }, row(x, i), rowClass(x), empty, redraw,
  // headButton }. el, key, cols, sort, lead, tail and empty can be functions, for a grid that shows different pages.
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
      const head = shown.map((c) => `<div class="th-cell" draggable="true" data-grid-col="${c.key}">${c.nosort ? `<span class="th-sort still"><span class="lbl">${esc(c.label)}</span></span>`
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
      return rows;
    };
    // One of several tables inside el, as HTML (cls: its own classes).
    g.table = (list, cls = '') => `<div class="table dgrid ${density()}${cls ? ` ${cls}` : ''}">${body(list).html}</div>`;
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

    // The heading: sort, drag an edge, double-click an edge, drag a heading onto another. Listened for once on el, so a
    // redraw keeps them.
    const t0 = el();
    let sizing = false; // an edge is being dragged, so the heading doesn't move too
    let moving = null; // the column whose heading is being dragged
    const unmark = () => t0.querySelectorAll('.drop-left, .drop-right').forEach((c) => c.classList.remove('drop-left', 'drop-right'));
    t0.addEventListener('dragstart', (e) => {
      const th = e.target.closest?.('[data-grid-col]');
      if (!th) return;
      if (sizing) { e.preventDefault(); return; }
      moving = th.dataset.gridCol;
      e.dataTransfer.effectAllowed = 'move';
      e.dataTransfer.setData('text/plain', moving);
    });
    t0.addEventListener('dragover', (e) => {
      const th = e.target.closest('[data-grid-col]');
      if (!moving || !th) return;
      e.preventDefault();
      unmark();
      if (th.dataset.gridCol === moving) return;
      const box = th.getBoundingClientRect();
      th.classList.add(e.clientX < box.left + box.width / 2 ? 'drop-left' : 'drop-right');
    });
    t0.addEventListener('drop', (e) => {
      const th = e.target.closest('[data-grid-col]');
      if (!moving || !th || th.dataset.gridCol === moving) return;
      e.preventDefault();
      const order = cols().map((c) => c.key).filter((k) => k !== moving);
      order.splice(order.indexOf(th.dataset.gridCol) + (th.classList.contains('drop-right') ? 1 : 0), 0, moving);
      keep(name('order'), order);
      moving = null;
      o.redraw();
    });
    t0.addEventListener('dragend', () => { moving = null; unmark(); });
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
    t0.addEventListener('pointerdown', (e) => {
      const grip = e.target.closest('[data-grid-grip]');
      if (!grip) return;
      e.preventDefault();
      const key = grip.dataset.gridGrip;
      const start = e.clientX;
      const from = grip.closest('.th-cell').getBoundingClientRect().width;
      const shown = g.shown();
      const wd = widths();
      sizing = true;
      document.body.classList.add('col-dragging');
      const move = (m) => {
        wd[key] = Math.max(60, Math.round(from + m.clientX - start));
        keep(name('widths'), wd);
        setTracks(shown);
      };
      const up = () => {
        window.removeEventListener('pointermove', move);
        ['pointerup', 'pointercancel', 'blur'].forEach((ev) => window.removeEventListener(ev, up));
        document.body.classList.remove('col-dragging');
        sizing = false;
      };
      window.addEventListener('pointermove', move);
      ['pointerup', 'pointercancel', 'blur'].forEach((ev) => window.addEventListener(ev, up));
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
