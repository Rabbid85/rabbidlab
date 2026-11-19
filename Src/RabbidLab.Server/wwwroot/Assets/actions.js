// Actions menus, for every list: one column of plain words under its Actions button, related actions in a sub-menu
// (›), a note at the top when something won't apply to every row ticked, and the risky ones last in red. One menu for
// the whole page; R.actions.open(button, { items, pick, note }) fills it.
//   items: [{ act, label, hint, danger, disabled, sub: [items] }] and '-' for a line between.
(() => {
  const R = window.RabbidLab;
  const { esc, svg } = R;
  let menu = null;
  let now = null; // { button, items, pick } while it's open
  let shut = { button: null, at: 0 }; // a click on the button that shut it (outside the menu) doesn't open it again

  const row = (e, path) => (e === '-' ? '<hr>'
    : `<button type="button" role="menuitem" class="xact-item${e.danger ? ' out' : ''}" data-path="${path}"${e.disabled ? ' disabled' : ''}${e.sub ? ' aria-haspopup="menu"' : ''}>`
      + `<span>${esc(e.label)}</span>${e.hint ? `<small>${esc(e.hint)}</small>` : ''}${e.sub ? svg('right', 'xact-more') : ''}</button>`);
  const at = (path) => path.split('.').reduce((list, i, n, all) => (n === all.length - 1 ? list[Number(i)] : list[Number(i)].sub), now.items);

  const make = () => {
    menu = document.createElement('div');
    menu.className = 'gear-menu xact';
    menu.id = 'act-menu';
    menu.setAttribute('popover', '');
    menu.setAttribute('role', 'menu');
    document.body.append(menu);
    const sub = () => menu.querySelector('.xact-sub');
    // A sub-menu opens beside its row: to the right, or to the left where there's no room.
    const openSub = (b) => {
      menu.querySelectorAll('.xact-item.on').forEach((x) => x.classList.remove('on'));
      sub()?.remove();
      const e = at(b.dataset.path);
      if (!e?.sub) return;
      b.classList.add('on');
      const box = document.createElement('div');
      box.className = 'xact-sub';
      box.setAttribute('role', 'menu');
      box.innerHTML = e.sub.map((x, i) => row(x, `${b.dataset.path}.${i}`)).join('');
      menu.append(box);
      const m = menu.getBoundingClientRect();
      const r = b.getBoundingClientRect();
      const right = m.right + 4 + box.offsetWidth <= innerWidth - 8;
      box.style.cssText = `top:${Math.min(r.top - m.top - 7, innerHeight - m.top - box.offsetHeight - 8)}px;${right ? 'left:calc(100% + 4px)' : 'right:calc(100% + 4px)'}`;
    };
    menu.addEventListener('pointerover', (e) => {
      const b = e.target.closest('.xact-item');
      if (b && !b.closest('.xact-sub')) openSub(b);
    });
    menu.addEventListener('click', (e) => {
      const b = e.target.closest('.xact-item');
      if (!b || b.disabled) return;
      const item = at(b.dataset.path);
      if (item.sub) {
        openSub(b);
        sub()?.querySelector('.xact-item:not(:disabled)')?.focus();
        return;
      }
      const pick = now.pick;
      menu.hidePopover();
      pick(item.act, item);
    });
    // The arrow keys move through the rows; right opens a sub-menu, left goes back.
    menu.addEventListener('keydown', (e) => {
      const here = document.activeElement?.closest('.xact-item');
      const list = here?.closest('.xact-sub') || menu;
      const rows = [...list.querySelectorAll(':scope > .xact-item:not(:disabled)')];
      const i = rows.indexOf(here);
      if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
        e.preventDefault();
        rows[(i + (e.key === 'ArrowDown' ? 1 : rows.length - 1)) % rows.length]?.focus();
      } else if (e.key === 'ArrowRight' && here?.getAttribute('aria-haspopup')) {
        e.preventDefault();
        openSub(here);
        sub()?.querySelector('.xact-item:not(:disabled)')?.focus();
      } else if (e.key === 'ArrowLeft' && list !== menu) {
        e.preventDefault();
        menu.querySelector('.xact-item.on')?.focus();
        sub()?.remove();
      }
    });
    menu.addEventListener('toggle', (e) => {
      if (e.newState === 'open') return;
      now?.button.setAttribute('aria-expanded', 'false');
      shut = { button: now?.button, at: Date.now() };
      now = null;
    });
  };

  R.actions = {
    open: (button, { items, pick, note = '' }) => {
      if (!menu) make();
      // The button again shuts it: the press outside the menu already did, just before this click.
      if (shut.button === button && Date.now() - shut.at < 400) return;
      if (now?.button === button && menu.matches(':popover-open')) {
        menu.hidePopover();
        return;
      }
      if (menu.matches(':popover-open')) menu.hidePopover();
      const list = items.filter((e, i, all) => e !== '-' || (i > 0 && i < all.length - 1 && all[i - 1] !== '-'));
      now = { button, items: list, pick };
      menu.innerHTML = (note ? `<p class="xact-note">${svg('alert')}<span>${esc(note)}</span></p>` : '')
        + (list.length ? list.map((e, i) => row(e, String(i))).join('') : '<p class="xact-none">Nothing to do with these.</p>');
      // Under the button, its left edge with the button's; moved in where the window ends, and above when there's more room there.
      menu.style.cssText = 'position:fixed;inset:auto;margin:0;left:0;top:0';
      menu.showPopover();
      button.setAttribute('aria-expanded', 'true');
      const r = button.getBoundingClientRect();
      const w = menu.offsetWidth;
      const h = menu.offsetHeight;
      menu.style.left = `${Math.max(8, Math.min(r.left, innerWidth - w - 8))}px`;
      menu.style.top = `${r.bottom + 6 + h > innerHeight - 8 && r.top > innerHeight - r.bottom ? Math.max(8, r.top - 6 - h) : r.bottom + 6}px`;
    },
    close: () => menu?.hidePopover(),
  };
})();
