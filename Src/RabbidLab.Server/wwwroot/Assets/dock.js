// The dock: once a page's command bar scrolls out of sight, a pill at the top keeps its views, Refresh and Add in reach.
// A bar opts in with data-dock="id id id" (a .vswitch, icon commands, then the main command last, in the accent). The
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
    const ids = bar.dataset.dock.split(/\s+/);
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
        groups.push(`<button type="button" class="primary dock-main" data-for="${el.id}">${icon}<span>${label}</span></button>`);
      });
      if (icons) groups.push(icons);
      dock.innerHTML = groups.join('<i class="dock-sep"></i>');
    };
    dock.addEventListener('click', (e) => {
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
