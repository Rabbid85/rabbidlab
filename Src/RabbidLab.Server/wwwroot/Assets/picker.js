// Pickers and small menus, for every list: the one place a pop-up list comes from. R.picker.open shows things to tick,
// with a find box and Clear all, and on each row a star (keeps it on top) or a number (how many rows have it).
// R.picker.menu shows a plain menu. There's one box; it opens under the button that asked and closes on a click elsewhere.
(() => {
  const R = window.RabbidLab;
  const { esc, svg } = R;
  const read = (k) => { try { return JSON.parse(localStorage.getItem(k) || 'null'); } catch { return null; } };
  const keep = (k, v) => { try { localStorage.setItem(k, JSON.stringify(v)); } catch { /* not kept, then */ } };

  const box = document.createElement('div');
  box.className = 'gear-menu xpick';
  box.setAttribute('popover', 'auto');
  document.body.append(box);
  let now = null; // what's open: { key, click(e), change(e), input(e) }
  let shut = { key: '', at: 0 }; // what closed last, so the click on its own button doesn't open it again
  box.addEventListener('beforetoggle', (e) => {
    if (e.newState !== 'closed') return;
    shut = { key: now?.key || '', at: performance.now() };
    now = null;
  });
  ['click', 'change', 'input'].forEach((ev) => box.addEventListener(ev, (e) => now?.[ev]?.(e)));

  // Under the button, kept inside the window; above it when there's no room below.
  const place = (anchor, right) => {
    const r = anchor.getBoundingClientRect();
    box.style.setProperty('left', '0px');
    box.style.setProperty('top', '0px');
    box.showPopover();
    const w = box.offsetWidth;
    const h = box.offsetHeight;
    const y = r.bottom + 6 + h > innerHeight - 8 && r.top - h - 6 > 8 ? r.top - h - 6 : r.bottom + 6;
    box.style.setProperty('left', `${Math.max(8, Math.min(right ? r.right - w : r.left, innerWidth - w - 8))}px`);
    box.style.setProperty('top', `${y}px`);
  };
  // False when this is the click that just closed it.
  const start = (key) => {
    if (box.matches(':popover-open')) {
      const same = now?.key === key;
      box.hidePopover();
      if (same) return false;
    } else if (shut.key === key && performance.now() - shut.at < 350) return false;
    return true;
  };

  // o: { key, items: [{ key, label, n }], picked (a Set, changed in place), change(picked), stars (true), starKey,
  //      clear (true), foot: [{ label, icon, run }], right }. An item with n shows the number where its star would be.
  const open = (anchor, o) => {
    if (!start(o.key)) return;
    const starKey = `rabbidlab.pick.star.${o.starKey || o.key}`;
    const fav = new Set(read(starKey) || []);
    const stars = o.stars !== false;
    box.className = 'gear-menu xpick xpick-list';
    box.innerHTML = `<label class="xpick-find">${svg('search')}<input type="search" placeholder="Search" aria-label="Search" autocomplete="off" spellcheck="false"></label>`
      + `${o.clear === false ? '' : '<button type="button" class="xpick-clear">Clear all</button>'}<div class="xpick-rows"></div>`
      + (o.foot?.length ? `<div class="am-links">${o.foot.map((f, i) => `<button type="button" class="am-link" data-pick-foot="${i}">${f.icon ? svg(f.icon) : ''}<span>${esc(f.label)}</span></button>`).join('')}</div>` : '');
    const find = box.querySelector('input');
    const rows = box.querySelector('.xpick-rows');
    const draw = () => {
      const q = find.value.trim().toLowerCase();
      const shown = o.items.filter((it) => it.label.toLowerCase().includes(q)).sort((a, b) => fav.has(b.key) - fav.has(a.key));
      rows.innerHTML = shown.length ? shown.map((it) => `<label class="xpick-row"><input type="checkbox" data-pick="${esc(it.key)}"${o.picked.has(it.key) ? ' checked' : ''}><span class="ell">${esc(it.label)}</span>`
        + (it.n != null ? `<small class="xpick-n${it.n ? '' : ' zero'}">${it.n.toLocaleString()}</small>`
          : stars ? `<button type="button" class="xpick-star${fav.has(it.key) ? ' on' : ''}" data-pick-star="${esc(it.key)}" aria-label="Keep on top">★</button>` : '')
        + '</label>').join('') : '<p class="xpick-none">Nothing matches.</p>';
    };
    now = {
      key: o.key,
      input: (e) => { if (e.target === find) draw(); },
      change: (e) => {
        const k = e.target.dataset.pick;
        if (k === undefined) return;
        if (e.target.checked) o.picked.add(k);
        else o.picked.delete(k);
        o.change(o.picked);
      },
      click: (e) => {
        const star = e.target.closest('[data-pick-star]');
        if (star) {
          e.preventDefault();
          const k = star.dataset.pickStar;
          if (fav.has(k)) fav.delete(k);
          else fav.add(k);
          keep(starKey, [...fav]);
          draw();
        } else if (e.target.closest('.xpick-clear')) {
          o.picked.clear();
          o.change(o.picked);
          draw();
        } else if (e.target.closest('[data-pick-foot]')) {
          const f = o.foot[Number(e.target.closest('[data-pick-foot]').dataset.pickFoot)];
          box.hidePopover();
          f.run();
        }
      },
    };
    draw();
    place(anchor, o.right);
    find.focus();
  };

  // items: { label, icon, tip, on, off, danger, run, edit, remove } (edit puts a pencil beside it, remove a ×),
  // { head }, or '-' for a line.
  const menu = (anchor, items, o = {}) => {
    if (!start(o.key)) return;
    const list = items.filter(Boolean);
    const groups = [[]];
    list.forEach((it, i) => (it === '-' ? groups.push([]) : groups[groups.length - 1].push([it, i])));
    box.className = 'gear-menu xpick xpick-menu';
    box.innerHTML = groups.filter((g) => g.length).map((g) => `<div class="am-links">${g.map(([it, i]) => (it.head ? `<p class="cmd-head">${esc(it.head)}</p>`
      : `<div class="xpick-item"><button type="button" class="am-link${it.on ? ' on' : ''}${it.danger ? ' out' : ''}" data-pick-item="${i}"${it.off ? ' disabled' : ''}${it.tip ? ` data-tip="${esc(it.tip)}"` : ''}>${it.icon ? svg(it.icon) : ''}<span class="ell">${esc(it.label)}</span></button>`
        + `${it.edit ? `<button type="button" class="at-more xpick-x" data-pick-edit="${i}" data-tip="Rename" aria-label="Rename ${esc(it.label)}">${svg('edit')}</button>` : ''}`
        + `${it.remove ? `<button type="button" class="at-more xpick-x" data-pick-remove="${i}" data-tip="Remove" aria-label="Remove ${esc(it.label)}">${svg('x')}</button>` : ''}</div>`)).join('')}</div>`).join('');
    now = {
      key: o.key,
      click: (e) => {
        const b = e.target.closest('[data-pick-remove], [data-pick-edit], [data-pick-item]');
        if (!b) return;
        const it = list[Number(b.dataset.pickRemove ?? b.dataset.pickEdit ?? b.dataset.pickItem)];
        box.hidePopover();
        if (b.dataset.pickRemove !== undefined) it.remove();
        else if (b.dataset.pickEdit !== undefined) it.edit();
        else it.run?.();
      },
    };
    place(anchor, o.right);
  };

  R.picker = { open, menu, close: () => { if (box.matches(':popover-open')) box.hidePopover(); } };
})();
