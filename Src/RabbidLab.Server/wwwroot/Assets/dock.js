// The dock: once a page's command bar scrolls out of sight, a pill at the top keeps its views, Refresh and Add in reach.
// A bar opts in with data-dock="id id id" (a .vswitch, icon commands, then the main command last, in the accent). The
// main command can name others after a + (ln-add+ln-newgroup): it becomes a split button whose arrow lists them. The
// dock's buttons press the bar's own, so whatever the page does on a click happens the same way.
(() => {
  const main = document.querySelector('.main');
  const docks = [];
  const make = (bar) => {
    const dock = document.createElement('div');
    dock.className = 'dock';
    dock.setAttribute('role', 'toolbar');
    dock.setAttribute('aria-label', `${bar.getAttribute('aria-label') || 'Page'} tools`);
    dock.hidden = true;
    document.body.append(dock);
    const menu = document.createElement('div');
    menu.className = 'gear-menu dock-menu';
    menu.setAttribute('role', 'menu');
    menu.popover = 'auto';
    document.body.append(menu);
    const ids = bar.dataset.dock.split(/\s+/);
    const [mainId, ...more] = ids.pop().split('+');
    ids.push(mainId);
    // Drawn each time it shows, so a role that can't use a command, or a view picked since, is as the bar has it.
    const draw = () => {
      const parts = ids.map((id) => document.getElementById(id)).filter((el) => el && !el.hidden && !el.classList.contains('cant'));
      // Groups with a thin line between: the views, the icon commands, then the main command.
      const groups = [];
      let icons = '';
      parts.forEach((el, i) => {
        const icon = el.querySelector('svg')?.outerHTML || '';
        if (el.classList.contains('vswitch')) {
          groups.push(`<div class="vswitch">${[...el.querySelectorAll('button')].map((b) => `<button type="button" data-for="${el.id}" data-view="${b.dataset.view}" aria-pressed="${b.getAttribute('aria-pressed') || 'false'}" aria-label="${b.getAttribute('aria-label')}" data-tip="${b.getAttribute('aria-label')}">${b.querySelector('svg').outerHTML}</button>`).join('')}</div>`);
          return;
        }
        const label = el.querySelector('span')?.textContent || el.getAttribute('aria-label') || '';
        if (i < parts.length - 1) {
          icons += `<button type="button" class="dock-btn" data-for="${el.id}" aria-label="${label}" data-tip="${label}">${icon}</button>`;
          return;
        }
        if (icons) groups.push(icons);
        icons = '';
        const button = `<button type="button" class="primary dock-main" data-for="${el.id}">${icon}<span>${label}</span></button>`;
        const extra = more.map((id) => document.getElementById(id)).filter((x) => x && !x.hidden && !x.classList.contains('cant'));
        menu.innerHTML = `<div class="am-links">${extra.map((x) => `<button type="button" class="am-link" role="menuitem" data-for="${x.id}">${x.querySelector('svg')?.outerHTML || ''}${x.querySelector('span')?.textContent || ''}</button>`).join('')}</div>`;
        groups.push(extra.length ? `<div class="dock-pair">${button}<button type="button" class="primary dock-arrow" aria-haspopup="menu" aria-expanded="false" aria-label="More options" data-tip="More options"><svg class="chev"><use href="#i-chevron-down"/></svg></button></div>` : button);
      });
      if (icons) groups.push(icons);
      dock.innerHTML = groups.join('<i class="dock-sep"></i>');
    };
    // The arrow opens the others under it, lined up with its right edge.
    const arrow = () => dock.querySelector('.dock-arrow');
    menu.addEventListener('toggle', (e) => arrow()?.setAttribute('aria-expanded', String(e.newState === 'open')));
    menu.addEventListener('click', (e) => {
      const b = e.target.closest('[data-for]');
      if (!b) return;
      menu.hidePopover();
      document.getElementById(b.dataset.for)?.click();
    });
    dock.addEventListener('click', (e) => {
      if (e.target.closest('.dock-arrow')) {
        menu.togglePopover();
        if (menu.matches(':popover-open')) {
          const r = arrow().getBoundingClientRect();
          Object.assign(menu.style, { position: 'fixed', margin: '0', inset: 'auto', top: `${r.bottom + 8}px`, left: `${Math.max(8, r.right - menu.offsetWidth)}px` });
        }
        return;
      }
      const b = e.target.closest('[data-for]');
      if (!b) return;
      const own = document.getElementById(b.dataset.for);
      (b.dataset.view ? own.querySelector(`[data-view="${b.dataset.view}"]`) : own)?.click();
      if (b.dataset.view) dock.querySelectorAll('[data-view]').forEach((x) => x.setAttribute('aria-pressed', String(x === b)));
    });
    const place = () => {
      const r = main.getBoundingClientRect();
      dock.style.left = `${r.left + r.width / 2}px`;
    };
    // Shown while the bar is on a page that's open and has gone up past the top bar.
    const top = () => parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--top-h')) || 52;
    const update = () => {
      const r = bar.getBoundingClientRect();
      const show = bar.offsetParent !== null && r.bottom < top();
      if (show && dock.hidden) {
        draw();
        place();
      }
      dock.hidden = !show;
    };
    docks.push({ update, place });
  };
  document.querySelectorAll('.cmdbar[data-dock]').forEach(make);
  if (!docks.length) return;
  let queued = 0;
  const all = () => {
    if (queued) return;
    queued = requestAnimationFrame(() => {
      queued = 0;
      docks.forEach((d) => d.update());
    });
  };
  document.addEventListener('scroll', all, { passive: true, capture: true });
  window.addEventListener('resize', () => docks.forEach((d) => d.place()));
  document.addEventListener('rabbidlab:show', all);
  window.addEventListener('hashchange', all);
})();
