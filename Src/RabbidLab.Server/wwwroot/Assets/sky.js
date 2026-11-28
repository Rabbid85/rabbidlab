// The banner's sky ("Push this to dev", the sky window in the mock). For dark mode and for light mode, each person picks
// what every banner shows: the clock's time of day, one time of day for good, the drawing in one colour, or a solid colour
// with nothing moving ("Can change color here, like the actual background color, or to solid color which turns off the
// animations."); whether the holidays show on their dates; and whether the weather where the lab is shows. A colour shows
// neither. Kept in this browser, as the theme is. The lab's place,
// a US ZIP code, is the lab's own (Weather.cs), set here by whoever may change the lab's look. scene.js paints and draws
// what this says, weather.js the weather. The options are on a page, Settings, Banner (with the monsters', monsters.js),
// and in a window that opens from the time and the weather on Overview's banner; the banner changes behind it as you pick.
(() => {
  const KEY = 'rabbidlab.sky';
  const TIMES = [['clock', 'Follow the clock'], ['dawn', 'Dawn'], ['day', 'Day'], ['dusk', 'Dusk'], ['night', 'Night'], ['colour', 'Colour'], ['solid', 'Solid']];
  const COLOURS = { '#5900FF': 'Purple', '#2563EB': 'Blue', '#0D9488': 'Teal', '#16A34A': 'Green', '#DC2626': 'Red', '#EA580C': 'Orange', '#DB2777': 'Pink', '#475569': 'Slate' };
  const START = { dark: 'clock', light: 'clock', darkColour: '#5900FF', lightColour: '#2563EB', holidays: true, weather: true };
  // Only what this file can show is taken from what was kept.
  const clean = (o) => ({
    dark: TIMES.some(([k]) => k === o?.dark) ? o.dark : START.dark,
    light: TIMES.some(([k]) => k === o?.light) ? o.light : START.light,
    darkColour: Object.hasOwn(COLOURS, o?.darkColour ?? '') ? o.darkColour : START.darkColour,
    lightColour: Object.hasOwn(COLOURS, o?.lightColour ?? '') ? o.lightColour : START.lightColour,
    holidays: o?.holidays !== false,
    weather: o?.weather !== false,
  });
  let st = { ...START };
  try {
    st = clean(JSON.parse(localStorage.getItem(KEY) || 'null'));
  } catch {
    // The usual sky, this visit.
  }
  const changed = () => window.dispatchEvent(new Event('rabbidlab:sky'));
  const keep = () => {
    try {
      localStorage.setItem(KEY, JSON.stringify(st));
    } catch {
      // This visit only.
    }
    changed();
  };
  // Light mode is the Light theme; every other theme is dark. Read from the page, so a mode shown from the window counts.
  const mode = () => (document.documentElement.dataset.theme === 'light' ? 'light' : 'dark');
  // A colour in place of the times of day, for the mode the console is in: the drawing in it, or a solid one.
  const plain = () => ['colour', 'solid'].includes(st[mode()]);

  // The holidays and their dates, each year. Christmas is December 1 to 26 ("It's not even december1st, the fuck is this.",
  // of one that began the day after Thanksgiving).
  const HOLIDAYS = [
    { key: 'halloween', name: 'Halloween', from: (y) => new Date(y, 9, 15), to: (y) => new Date(y, 9, 31) },
    { key: 'christmas', name: 'Christmas', from: (y) => new Date(y, 11, 1), to: (y) => new Date(y, 11, 26) },
  ];
  const today = () => { const d = new Date(); return new Date(d.getFullYear(), d.getMonth(), d.getDate()); };
  const onNow = () => { const d = today(); return HOLIDAYS.find((h) => d >= h.from(d.getFullYear()) && d <= h.to(d.getFullYear())) || null; };
  const next = () => {
    const d = today();
    return [d.getFullYear(), d.getFullYear() + 1].flatMap((y) => HOLIDAYS.map((h) => ({ name: h.name, from: h.from(y) }))).filter((h) => h.from > d).sort((a, b) => a.from - b.from)[0];
  };
  const day = (d) => d.toLocaleDateString([], { month: 'short', day: 'numeric' });
  const query = new URLSearchParams(location.search);
  // ?banner=halloween or ?banner=christmas shows one on any day.
  const asked = query.get('banner');

  // The weather where the lab is, as the server last read it (/api/weather): { zip, reading, problem, checkedAt, every,
  // canSet }. Asked for when the page opens and every five minutes (due, at the foot); the server itself checks every fifteen.
  // ?weather=rain,heavy,most (the kind, how hard, how much cloud) shows one on any day, wherever the lab is.
  const KINDS = ['clear', 'rain', 'storm', 'snow', 'ice', 'wind', 'fog'];
  const [tryKind, tryAmount, tryCover] = (query.get('weather') || '').split(',');
  const tried = KINDS.includes(tryKind) ? { kind: tryKind, amount: ['light', 'mid', 'heavy'].includes(tryAmount) ? tryAmount : 'mid',
    cover: ['none', 'few', 'half', 'most', 'over'].includes(tryCover) ? tryCover : tryKind === 'clear' ? 'none' : 'most', tempC: null, windKph: null, says: '', place: '', at: new Date().toISOString() } : null;
  let wx = null;
  let asking = false;
  let askedAt = 0;
  // Where the lab is (the middle of its ZIP code area, from the server), for where the sun stands there: Follow the clock
  // goes by sunrise and sunset (scene.js). Kept in this browser with when it was learned, so the first banner drawn is
  // right; null when the lab has no ZIP code.
  const PLACE = 'rabbidlab.sky.place';
  let lab = { at: 0, spot: null };
  try {
    const kept = JSON.parse(localStorage.getItem(PLACE) || 'null');
    if (kept && (kept.spot === null || (Array.isArray(kept.spot) && kept.spot.length === 2 && kept.spot.every(Number.isFinite)))) lab = { at: Number(kept.at) || 0, spot: kept.spot };
  } catch {
    // Asked for again.
  }
  // The lab's place, from what the server just answered.
  const learn = () => {
    lab = { at: Date.now(), spot: Number.isFinite(wx.lat) && Number.isFinite(wx.lon) ? [wx.lat, wx.lon] : null };
    try { localStorage.setItem(PLACE, JSON.stringify(lab)); } catch { /* this visit only */ }
  };
  const headers = { 'X-Rabbid-Lab': '1', 'Content-Type': 'application/json', Accept: 'application/json' };
  const ask = async () => {
    if (asking) return;
    asking = true;
    askedAt = Date.now();
    try {
      const r = await fetch('/api/weather', { headers: { Accept: 'application/json' } });
      if (r.ok) {
        wx = await r.json();
        learn();
        changed();
        draw();
      }
    } catch {
      // No weather until the server answers.
    } finally {
      asking = false;
    }
  };
  // Fahrenheit and miles an hour where they are used (the weather service is the United States'), Celsius and km/h elsewhere.
  const imperial = (() => {
    try {
      const region = new Intl.Locale(navigator.language).maximize().region;
      return !region || ['US', 'LR', 'MM', 'BS', 'BZ', 'KY', 'PW'].includes(region);
    } catch {
      return true;
    }
  })();
  const temp = (c) => (c === null || c === undefined ? '' : imperial ? `${Math.round((c * 9) / 5 + 32)}°F` : `${Math.round(c)}°C`);
  const wind = (kph) => (kph === null || kph === undefined ? '' : imperial ? `${Math.round(kph / 1.609)} mph` : `${Math.round(kph)} km/h`);
  const NAMES = { clear: 'Clear', rain: 'Rain', storm: 'Thunderstorm', snow: 'Snow', ice: 'Ice', wind: 'Windy', fog: 'Fog' };
  // A reading in a few words: the weather service's own, or the kind's name, and the temperature.
  const says = (r) => [r.says || NAMES[r.kind] || '', temp(r.tempC)].filter(Boolean).join(', ');
  const reading = () => tried || wx?.reading || null;

  const esc = (s) => String(s).replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' })[c]);
  // data-sky-pick, not data-sky: the page itself carries data-sky (the time of day, scene.js), so a rule on that would match every button.
  const seg = (k, choices, on) => `<div class="seg" data-sky-pick="${k}">${choices.map(([v, l]) => `<button type="button" data-v="${v}" aria-pressed="${v === on}">${esc(l)}</button>`).join('')}</div>`;
  // Colour and Solid share the mode's colour. Under the swatches, what each one shows.
  const swatches = (m) => (!['colour', 'solid'].includes(st[m]) ? '' : `<div class="sky-sw">${Object.entries(COLOURS).map(([c, name]) => `<button type="button" data-sky-colour="${m}" data-c="${c}" data-tip="${name}" aria-label="${name}" aria-pressed="${st[`${m}Colour`] === c}"></button>`).join('')}</div>`
    + `<p class="sky-note">${st[m] === 'colour' ? 'The drawing in shades of this colour.' : 'One solid colour in place of the drawing. Nothing moves.'} Holidays and the weather are not shown.</p>`);
  const note = () => {
    if (!st.holidays) return 'The banner stays the same all year.';
    const [on, then] = [onNow(), next()];
    return `${on ? `${on.name} is on now, until ${day(on.to(today().getFullYear()))}.` : 'No holiday is on now.'} Next: ${then.name}, ${day(then.from)}.`;
  };
  // What was typed in the ZIP box and not saved yet, and what the server said when a save failed.
  let zipDraft = null;
  let zipProblem = '';
  let saving = false;
  const ago = (iso) => {
    const min = Math.max(0, Math.round((Date.now() - new Date(iso).getTime()) / 60000));
    return min < 1 ? 'just now' : min === 1 ? 'a minute ago' : min < 60 ? `${min} minutes ago` : min < 120 ? 'an hour ago' : `${Math.round(min / 60)} hours ago`;
  };
  // Under Current weather: the lab's ZIP code (a box for whoever may change the lab's look), and what was last read.
  const place = () => {
    if (!st.weather) return '';
    if (!wx) return '<p class="sky-note">The weather could not be read from the server.</p>';
    const r = wx.reading;
    const box = wx.canSet ? `<label class="sky-place"><span>ZIP code of the lab</span><span class="sky-zip"><input class="ds-input" data-sky-zip inputmode="numeric" maxlength="5" autocomplete="off" spellcheck="false" placeholder="60601" value="${esc(zipDraft ?? wx.zip)}">`
      + `<button type="button" class="ds-btn" data-sky-zip-save${saving ? ' disabled' : ''}>${saving ? 'Checking' : 'Save'}</button></span></label>` : '';
    const line = zipProblem ? zipProblem
      : !wx.zip ? (wx.canSet ? 'Enter the lab’s ZIP code to show the weather there. United States only.' : 'The lab’s ZIP code is not set yet. Someone who can change the lab’s appearance sets it here.')
        : r ? `${[says(r), r.windKph ? `wind ${wind(r.windKph)}` : ''].filter(Boolean).join(', ')}${r.place ? ` in ${r.place}` : ''}. Read ${ago(r.at)}; the server checks every ${wx.every} minutes.`
          : wx.problem || 'Waiting for the first reading.';
    return `${box}<p class="sky-note${zipProblem ? ' problem' : ''}">${esc(line)}</p><p class="sky-note">Weather from the US National Weather Service.</p>`;
  };
  // inPage: in Settings, Banner, which has no Showing row (the window's look at the other mode ends when it closes).
  const form = (inPage) => (inPage ? '' : `<div class="sky-row"><span>Showing</span>${seg('showing', [['dark', 'Dark mode'], ['light', 'Light mode']], mode())}</div>`)
    + `<div class="sky-row"><span>Dark mode</span><div>${seg('dark', TIMES, st.dark)}${swatches('dark')}</div></div>`
    + `<div class="sky-row"><span>Light mode</span><div>${seg('light', TIMES, st.light)}${swatches('light')}</div></div>`
    + `<div class="sky-row"><span>Holidays</span><div><label class="sky-switch"><input type="checkbox" class="switch" data-sky-holidays ${st.holidays ? 'checked' : ''}><span>On their dates</span></label>`
    + `<p class="sky-note">${esc(note())}</p></div></div>`
    + `<div class="sky-row"><span>Weather</span><div>${seg('weather', [['off', 'Off'], ['on', 'Current weather']], st.weather ? 'on' : 'off')}${place()}</div></div>`;

  let dialog = null;
  // Where the options are drawn: Settings, Banner (inPage), and the window once it has been opened.
  const places = [];
  const draw = () => places.forEach(({ el, inPage }) => {
    // The ZIP box keeps its place in the typing when the options are drawn again.
    const typing = el.contains(document.activeElement) && document.activeElement.matches('[data-sky-zip]') ? document.activeElement.selectionStart : null;
    el.innerHTML = form(inPage);
    // The page allows no style attributes, so each swatch's colour is set here.
    el.querySelectorAll('[data-c]').forEach((b) => b.style.setProperty('--c', b.dataset.c));
    if (typing !== null) { const z = el.querySelector('[data-sky-zip]'); z?.focus(); z?.setSelectionRange(typing, typing); }
  });
  // Showing looks at the other mode while the window is open; closing it puts the person's own theme back.
  const show = (m) => {
    const T = window.RabbidLabTheme;
    if (m === 'light') document.documentElement.dataset.theme = 'light';
    else if (T && T.shown !== 'light') { if (T.mine) T.set(T.picked); else T.follow(); return; }
    else delete document.documentElement.dataset.theme;
    window.dispatchEvent(new Event('rabbidlab:theme'));
  };
  const restore = () => { const T = window.RabbidLabTheme; if (!T) return; if (T.mine) T.set(T.picked); else T.follow(); };
  // The lab's ZIP code goes to the server, which looks the weather up at once and says what it read or why it couldn't.
  const saveZip = async () => {
    if (saving) return;
    [saving, zipProblem] = [true, ''];
    draw();
    try {
      const r = await fetch('/api/settings/weather', { method: 'POST', headers, body: JSON.stringify({ zip: (zipDraft ?? wx?.zip ?? '').trim() }) });
      const j = await r.json().catch(() => ({}));
      if (r.ok) { [wx, zipDraft] = [j, null]; learn(); }
      else zipProblem = j.error || 'The ZIP code could not be saved.';
    } catch {
      zipProblem = 'The server did not answer. Try again.';
    }
    saving = false;
    changed();
    draw();
  };
  // The options' own controls, wherever they are drawn. A pick is kept at once and every banner changes.
  const wire = (root) => {
    root.addEventListener('click', (e) => {
      const pick = e.target.closest('[data-sky-pick] > button');
      const colour = e.target.closest('[data-sky-colour]');
      if (pick) {
        const k = pick.parentElement.dataset.skyPick;
        if (k === 'showing') show(pick.dataset.v);
        else { st[k] = k === 'weather' ? pick.dataset.v === 'on' : pick.dataset.v; keep(); due(); }
      } else if (colour) {
        st[`${colour.dataset.skyColour}Colour`] = colour.dataset.c;
        keep();
      } else if (e.target.closest('[data-sky-zip-save]')) {
        saveZip();
        return;
      } else return;
      draw();
    });
    root.addEventListener('change', (e) => {
      if (!e.target.matches('[data-sky-holidays]')) return;
      st.holidays = e.target.checked;
      keep();
      draw();
    });
    root.addEventListener('input', (e) => { if (e.target.matches('[data-sky-zip]')) [zipDraft, zipProblem] = [e.target.value, '']; });
    root.addEventListener('keydown', (e) => { if (e.key === 'Enter' && e.target.matches('[data-sky-zip]')) { e.preventDefault(); saveZip(); } });
  };
  const open = () => {
    if (!dialog) {
      document.body.insertAdjacentHTML('beforeend', '<dialog class="dialog sky-dialog" id="sky-dialog" aria-labelledby="sky-title"><h2 id="sky-title">The banner\'s sky</h2>'
        + '<p class="dialog-lead">For every page\'s banner, in this browser. It changes behind this window as you pick. These options and the monsters are also in Settings, Banner.</p><div class="sky-form"></div>'
        + '<div class="dialog-foot"><button type="button" class="primary" data-sky-done>Done</button></div></dialog>');
      dialog = document.getElementById('sky-dialog');
      places.push({ el: dialog.querySelector('.sky-form'), inPage: false });
      wire(dialog);
      dialog.addEventListener('click', (e) => { if (e.target.closest('[data-sky-done]')) dialog.close(); });
      dialog.addEventListener('close', restore);
    }
    [zipDraft, zipProblem] = [null, ''];
    draw();
    if (!dialog.open) dialog.showModal();
    ask();
  };
  document.addEventListener('click', (e) => { if (e.target.closest('[data-sky-open]')) open(); });
  // Settings, Banner: the same options on a page.
  const inSettings = document.getElementById('set-sky');
  if (inSettings) {
    places.push({ el: inSettings, inPage: true });
    wire(inSettings);
    draw();
  }

  window.RabbidLabSky = {
    open,
    says,
    // Where the lab is, [latitude, longitude], or null when the server has no ZIP code for it.
    place: () => lab.spot,
    // For Overview's words (ovinfo.js): a temperature as a figure, the wind, and the hours to come while the weather shows.
    deg: (c) => (c === null || c === undefined ? '' : `${Math.round(imperial ? (c * 9) / 5 + 32 : c)}°`),
    wind,
    ahead: () => (!plain() && st.weather && !tried && wx?.reading ? wx.ahead || [] : []),
    // What the banners show now: the time of day picked for this mode ('clock' to follow it), and the weather where the
    // lab is when this person shows it and there is a reading; or the colour the drawing is in (colour) or the solid
    // colour in its place (solid), with the colour's name.
    now() {
      const m = mode();
      const c = st[`${m}Colour`];
      if (plain()) return { time: 'night', colour: st[m] === 'colour' ? c : null, solid: st[m] === 'solid' ? c : null, name: COLOURS[c], weather: null };
      return { time: st[m], colour: null, solid: null, weather: tried || (st.weather ? reading() : null) };
    },
    // The holiday the banners wear today: its key, or '' when none is on, this person turned holidays off, or a colour shows.
    holiday() {
      if (plain()) return '';
      if (HOLIDAYS.some((h) => h.key === asked)) return asked;
      return (st.holidays && onNow()?.key) || '';
    },
  };
  // The server is asked only while this person shows the weather (or has the window open, which shows the reading) and
  // the tab is in front; a tab that comes back in front asks at once if its reading is five minutes old.
  // A person who shows no weather still needs the lab's place for the sun: asked for once a day.
  const EVERY = 300000;
  const due = () => { if (!document.hidden && (st.weather || dialog?.open || Date.now() - lab.at > 86400000) && Date.now() - askedAt >= EVERY - 5000) ask(); };
  due();
  setInterval(due, EVERY);
  document.addEventListener('visibilitychange', due);
})();
