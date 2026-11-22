// The skeleton engine: grey pulsing placeholders the shape of what's loading, until a first read is back. Every page,
// tab, panel and window that waits on a first read uses it (R.skeleton); something the user started uses the flask (R.busy).
//   R.skeleton(el, kind, count, options)  draws kind in el; kind '' takes it away again
//   R.skeleton.scan(root)                 draws every [data-skel="kind:count"] inside root, for HTML drawn as a string
//   R.skeleton.html(kind, count)          the placeholders as a string, to put inside HTML being built
//   R.skeleton.inline('num' | 'text')     one placeholder inside a line, for a number or a few words
// Kinds: rows, tiles, grid (lined up under a grid's headings), panel, facts, timeline, form, lines.
(() => {
  const W = [45, 60, 30, 75];
  const bar = (i, k = 0) => `<i class="sk w${W[(i + k) % 4]}"></i>`;
  const lines = (i, n) => `<span class="sk-lines">${Array.from({ length: n }, (_, k) => bar(i, k)).join('')}</span>`;
  const row = (i) => `<div class="sk-row"><i class="sk sq"></i>${lines(i, 2)}<i class="sk end"></i></div>`;
  const gridWidths = (o) => (o.widths?.length ? o.widths : [36, ...Array.from({ length: o.cols || 5 }, () => 0)]);
  const facts = (n) =>`<div class="sk-facts">${Array.from({ length: n }, (_, i) => `<span>${bar(i, 2)}${bar(i, 1)}</span>`).join('')}</div>`;
  const SHAPES = {
    rows: (n) => Array.from({ length: n }, (_, i) => row(i)).join(''),
    tiles: (n) => `<i class="sk sk-head"></i><div class="sk-tiles">${Array.from({ length: n }, (_, i) => `<div class="sk-tile"><i class="sk sq"></i>${lines(i, 3)}</div>`).join('')}</div>`,
    // widths: the grid's heading cells, so each placeholder sits under its column; lead is a tick column first, tail the
    // Columns button's cell last. Without widths, cols even columns after a tick column.
    grid: (n, o) => {
      const ws = gridWidths(o);
      const lead = !o.widths?.length || !!o.lead;
      const cell = (i, k) => (k === 0 && lead ? '<span><i class="sk tick"></i></span>' : k === ws.length - 1 && o.tail ? '<span></span>' : `<span>${bar(i, k)}</span>`);
      return Array.from({ length: n }, (_, i) => `<div class="sk-grow">${ws.map((_w, k) => cell(i, k)).join('')}</div>`).join('');
    },
    panel: (n) => `<div class="sk-phead"><i class="sk sq"></i>${lines(0, 2)}</div><div class="sk-chips"><i class="sk"></i><i class="sk"></i><i class="sk"></i></div>`
      + `${facts(4)}<div class="sk-chips tabs"><i class="sk"></i><i class="sk"></i><i class="sk"></i></div>${SHAPES.rows(n)}`,
    facts,
    timeline: (n) => `<div class="sk-steps">${Array.from({ length: n }, (_, i) => `<div class="sk-step"><i class="sk dot"></i>${lines(i, 3)}</div>`).join('')}</div>`,
    form: (n) => Array.from({ length: n }, (_, i) => `<div class="sk-field">${bar(i, 2)}<i class="sk box"></i></div>`).join(''),
    lines: (n) => `<div class="sk-para">${Array.from({ length: n }, (_, i) => bar(i, 3)).join('')}</div>`,
  };

  const clear = (el) => {
    if (!el) return;
    if (el.firstElementChild?.classList.contains('sk-wrap')) el.replaceChildren();
    el.removeAttribute('aria-busy');
  };
  const skeleton = (el, kind = 'rows', count = 6, options = {}) => {
    if (!el) return;
    if (!kind) { clear(el); return; }
    const shape = SHAPES[kind] || SHAPES.rows;
    el.innerHTML = `<div class="sk-wrap" data-sk="${kind}" aria-hidden="true">${shape(count, options)}</div>`;
    // The page's CSP refuses style attributes in markup, so the grid's column widths are set here.
    if (kind === 'grid') el.firstElementChild.style.setProperty('--sk-cols', gridWidths(options).map((w) => (w ? `${w}px` : 'minmax(0, 1fr)')).join(' '));
    el.setAttribute('aria-busy', 'true');
  };
  skeleton.clear = clear;
  skeleton.scan = (root = document) => root.querySelectorAll('[data-skel]').forEach((el) => {
    const [kind, count] = el.dataset.skel.split(':');
    skeleton(el, kind, Number(count) || 6);
  });
  // For HTML built as a string; the grid kind needs skeleton(el, …) instead, for its widths.
  skeleton.html = (kind = 'rows', count = 6, options = {}) => `<div class="sk-wrap" data-sk="${kind}" aria-hidden="true">${(SHAPES[kind] || SHAPES.rows)(count, options)}</div>`;
  skeleton.inline = (kind = 'text') => `<i class="sk sk-${kind === 'num' ? 'num' : 'text'}"></i>`;
  skeleton.kinds = Object.keys(SHAPES);
  window.RabbidSkeleton = skeleton;
})();
