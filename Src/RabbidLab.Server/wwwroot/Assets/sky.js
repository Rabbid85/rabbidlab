// The banner's sky ("Push this to dev", the sky window in the mock). For dark mode and for light mode, each person picks
// what every banner shows: the clock's time of day, one time of day for good, or a plain colour; and whether the holidays
// show on their dates. Kept in this browser, as the theme is. scene.js paints and draws what this says. The window opens
// from the chip at the top right of Overview's banner, and from Settings, Appearance; the banner changes behind it as you
// pick.
(() => {
  const KEY = 'rabbidlab.sky';
  const TIMES = [['clock', 'Follow the clock'], ['dawn', 'Dawn'], ['day', 'Day'], ['dusk', 'Dusk'], ['night', 'Night'], ['colour', 'Colour']];
  const COLOURS = ['#5900FF', '#3D7FE6', '#1FA3A8', '#2FB37A', '#E8542F', '#D6448E', '#1b1b2b'];
  const START = { dark: 'clock', light: 'clock', darkColour: COLOURS[0], lightColour: COLOURS[1], holidays: true };
  // Only what this file can show is taken from what was kept.
  const clean = (o) => ({
    dark: TIMES.some(([k]) => k === o?.dark) ? o.dark : START.dark,
    light: TIMES.some(([k]) => k === o?.light) ? o.light : START.light,
    darkColour: COLOURS.includes(o?.darkColour) ? o.darkColour : START.darkColour,
    lightColour: COLOURS.includes(o?.lightColour) ? o.lightColour : START.lightColour,
    holidays: o?.holidays !== false,
  });
  let st = { ...START };
  try {
    st = clean(JSON.parse(localStorage.getItem(KEY) || 'null'));
  } catch {
    // The usual sky, this visit.
  }
  const keep = () => {
    try {
      localStorage.setItem(KEY, JSON.stringify(st));
    } catch {
      // This visit only.
    }
    window.dispatchEvent(new Event('rabbidlab:sky'));
  };
  // Light mode is the Light theme; every other theme is dark. Read from the page, so a mode shown from the window counts.
  const mode = () => (document.documentElement.dataset.theme === 'light' ? 'light' : 'dark');

  // The holidays and their dates, each year. Christmas starts the day after Thanksgiving, the fourth Thursday in November.
  const thanksgiving = (y) => 1 + ((4 - new Date(y, 10, 1).getDay() + 7) % 7) + 21;
  const HOLIDAYS = [
    { key: 'halloween', name: 'Halloween', from: (y) => new Date(y, 9, 15), to: (y) => new Date(y, 9, 31) },
    { key: 'christmas', name: 'Christmas', from: (y) => new Date(y, 10, thanksgiving(y) + 1), to: (y) => new Date(y, 11, 26) },
  ];
  const today = () => { const d = new Date(); return new Date(d.getFullYear(), d.getMonth(), d.getDate()); };
  const onNow = () => { const d = today(); return HOLIDAYS.find((h) => d >= h.from(d.getFullYear()) && d <= h.to(d.getFullYear())) || null; };
  const next = () => {
    const d = today();
    return [d.getFullYear(), d.getFullYear() + 1].flatMap((y) => HOLIDAYS.map((h) => ({ name: h.name, from: h.from(y) }))).filter((h) => h.from > d).sort((a, b) => a.from - b.from)[0];
  };
  const day = (d) => d.toLocaleDateString([], { month: 'short', day: 'numeric' });
  // ?banner=halloween or ?banner=christmas shows one on any day.
  const asked = new URLSearchParams(location.search).get('banner');

  const esc = (s) => String(s).replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' })[c]);
  // data-sky-pick, not data-sky: the page itself carries data-sky (the time of day, scene.js), so a rule on that would match every button.
  const seg = (k, choices, on) => `<div class="seg" data-sky-pick="${k}">${choices.map(([v, l]) => `<button type="button" data-v="${v}" aria-pressed="${v === on}">${esc(l)}</button>`).join('')}</div>`;
  const swatches = (m) => (st[m] !== 'colour' ? '' : `<div class="sky-sw">${COLOURS.map((c) => `<button type="button" data-sky-colour="${m}" data-c="${c}" aria-label="${c}" aria-pressed="${st[`${m}Colour`] === c}"></button>`).join('')}</div>`);
  const note = () => {
    if (!st.holidays) return 'The banner stays the same all year.';
    const [on, then] = [onNow(), next()];
    return `${on ? `${on.name} is on now, until ${day(on.to(today().getFullYear()))}.` : 'No holiday is on now.'} Next: ${then.name}, ${day(then.from)}.`;
  };
  const form = () => `<div class="sky-row"><span>Showing</span>${seg('showing', [['dark', 'Dark mode'], ['light', 'Light mode']], mode())}</div>`
    + `<div class="sky-row"><span>Dark mode</span><div>${seg('dark', TIMES, st.dark)}${swatches('dark')}</div></div>`
    + `<div class="sky-row"><span>Light mode</span><div>${seg('light', TIMES, st.light)}${swatches('light')}</div></div>`
    + `<div class="sky-row"><span>Holidays</span><div><label class="sky-switch"><input type="checkbox" class="switch" data-sky-holidays ${st.holidays ? 'checked' : ''}><span>On their dates</span></label>`
    + `<p class="sky-note">${esc(note())}</p></div></div>`;

  let dialog = null;
  const draw = () => {
    dialog.querySelector('.sky-form').innerHTML = form();
    // The page allows no style attributes, so each swatch's colour is set here.
    dialog.querySelectorAll('[data-c]').forEach((b) => b.style.setProperty('--c', b.dataset.c));
  };
  // Showing looks at the other mode while the window is open; closing it puts the person's own theme back.
  const show = (m) => {
    const T = window.RabbidLabTheme;
    if (m === 'light') document.documentElement.dataset.theme = 'light';
    else if (T && T.shown !== 'light') { if (T.mine) T.set(T.picked); else T.follow(); return; }
    else delete document.documentElement.dataset.theme;
    window.dispatchEvent(new Event('rabbidlab:theme'));
  };
  const restore = () => { const T = window.RabbidLabTheme; if (!T) return; if (T.mine) T.set(T.picked); else T.follow(); };
  const open = () => {
    if (!dialog) {
      document.body.insertAdjacentHTML('beforeend', '<dialog class="dialog sky-dialog" id="sky-dialog" aria-labelledby="sky-title"><h2 id="sky-title">The banner\'s sky</h2>'
        + '<p class="dialog-lead">For every page\'s banner, in this browser. It changes behind this window as you pick.</p><div class="sky-form"></div>'
        + '<div class="dialog-foot"><button type="button" class="primary" data-sky-done>Done</button></div></dialog>');
      dialog = document.getElementById('sky-dialog');
      dialog.addEventListener('click', (e) => {
        const pick = e.target.closest('[data-sky-pick] > button');
        const colour = e.target.closest('[data-sky-colour]');
        if (pick) {
          const k = pick.parentElement.dataset.skyPick;
          if (k === 'showing') show(pick.dataset.v); else { st[k] = pick.dataset.v; keep(); }
        } else if (colour) {
          st[`${colour.dataset.skyColour}Colour`] = colour.dataset.c;
          keep();
        } else if (e.target.closest('[data-sky-done]')) {
          dialog.close();
          return;
        } else return;
        draw();
      });
      dialog.addEventListener('change', (e) => {
        if (!e.target.matches('[data-sky-holidays]')) return;
        st.holidays = e.target.checked;
        keep();
        draw();
      });
      dialog.addEventListener('close', restore);
    }
    draw();
    if (!dialog.open) dialog.showModal();
  };
  document.addEventListener('click', (e) => { if (e.target.closest('[data-sky-open]')) open(); });

  window.RabbidLabSky = {
    open,
    // What the banners show now: the time of day picked for this mode ('clock' to follow it), or a plain colour.
    now() {
      const m = mode();
      return { time: st[m] === 'colour' ? 'clock' : st[m], colour: st[m] === 'colour' ? st[`${m}Colour`] : null };
    },
    // The holiday the banners wear today: its key, or '' when none is on or this person turned holidays off.
    holiday() {
      if (HOLIDAYS.some((h) => h.key === asked)) return asked;
      return (st.holidays && onNow()?.key) || '';
    },
  };
})();
