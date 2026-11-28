// Overview's words on its banner: one line across it, with the lab's name and its own line at the left and the date, the
// time and the weather where the lab is at the right. The weather opens the next hours on glass; the time opens the
// banner's settings (sky.js). The line is at the top or at the bottom, as each person sets it there, with the quote of the
// day at the other end of the picture ("Should be able to do at the top, and on the bottom."). With no weather to show
// (no ZIP code set, turned off, or a colour in place of the picture) the name, the date and the time stand alone. The
// weather is the server's reading and its hourly forecast (Weather.cs); nothing here asks for anything.
(() => {
  const R = window.RabbidLab;
  const Sky = window.RabbidLabSky;
  const Quotes = window.RabbidLabQuotes;
  const { $, esc, where } = R;
  // The weather's pictures: a cloud with what falls from it, the sun and the moon.
  const CLOUD = '<path d="M7.2 15.5a4 4 0 0 1 .5-7.96 5.2 5.2 0 0 1 9.9 1.3 3.4 3.4 0 0 1-.4 6.66Z"/>';
  const ic = (body) => `<svg class="ovw-ic" viewBox="0 0 24 24" aria-hidden="true">${body}</svg>`;
  const ICONS = {
    rain: ic(`${CLOUD}<path d="M8.6 18.2l-.9 2.3M12.4 18.2l-.9 2.3M16.2 18.2l-.9 2.3"/>`),
    storm: ic(`${CLOUD}<path d="M12.9 17.2l-2.3 2.6h2.9l-2.1 2.6"/>`),
    snow: ic(`${CLOUD}<path d="M8.4 18.6v.01M12 18.6v.01M15.6 18.6v.01M10.2 21.4v.01M13.8 21.4v.01"/>`),
    ice: ic(`${CLOUD}<path d="M8.6 18.2l-.9 2.3M12 18.6v.01M12 21.4v.01M16.2 18.2l-.9 2.3"/>`),
    fog: ic(`${CLOUD}<path d="M6.2 18.6h11.6M8.4 21.4h7.2"/>`),
    wind: ic('<path d="M3 9.5h10.2a2.7 2.7 0 1 0-2.6-3.4M3 14h15.3a2.6 2.6 0 1 1-2.5 3.3M3 18.5h7.2"/>'),
    cloud: ic(CLOUD),
    sun: ic('<circle cx="12" cy="12" r="4.2"/><path d="M12 2.6v2.2M12 19.2v2.2M2.6 12h2.2M19.2 12h2.2M5.35 5.35l1.55 1.55M17.1 17.1l1.55 1.55M5.35 18.65l1.55-1.55M17.1 6.9l1.55-1.55"/>'),
    moon: ic('<path d="M20.4 12.8A8.6 8.6 0 1 1 11.2 3.6a6.8 6.8 0 0 0 9.2 9.2Z"/>'),
  };
  // A clear sky is the sun by day and the moon by night; under cloud with nothing falling, a cloud.
  const icon = (kind, day, cover) => ICONS[kind === 'clear' ? (['most', 'over'].includes(cover) ? 'cloud' : day ? 'sun' : 'moon') : kind] || ICONS.cloud;
  const NAMES = { clear: 'Clear', rain: 'Rain', storm: 'Thunderstorm', snow: 'Snow', ice: 'Ice', wind: 'Windy', fog: 'Fog' };

  const hero = $('hero');
  const wxButton = $('ovw-wx');
  const pop = $('ovw-pop');
  // The next hours, on glass under the weather (over it when the line is at the bottom).
  const open = (on) => { pop.hidden = !on; wxButton.setAttribute('aria-expanded', String(on)); };
  // Today's quote: one of the kind the lab set for the sign-in page, with the lab's own, the same one all day.
  const quote = () => {
    const brand = window.RabbidLabBrand?.now || {};
    const all = Quotes.list(brand.quoteKind, brand.quotes || []);
    return all[Quotes.pick(all, 'day')] || null;
  };
  // An element's words, changed only when they differ.
  const say = (id, text) => { const el = $(id); if (el.textContent !== text) el.textContent = text; };

  const draw = () => {
    const now = new Date();
    say('ovw-time', now.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' }));
    say('ovw-date', now.toLocaleDateString([], { weekday: 'long', month: 'long', day: 'numeric' }));
    const set = Sky.words();
    const said = set.quote ? quote() : null;
    hero.dataset.line = set.line;
    hero.dataset.quote = said ? 'on' : 'off';
    $('ovw-quote').hidden = !said;
    if (said) {
      say('ovw-quote-text', said[0]);
      say('ovw-quote-by', said[1] || '');
    }
    const wx = Sky.now().weather;
    const light = document.documentElement.dataset.sky !== 'night';
    wxButton.hidden = !wx;
    if (!wx) {
      open(false);
      return;
    }
    const picture = icon(wx.kind, light, wx.cover);
    if ($('ovw-icon').innerHTML !== picture) $('ovw-icon').innerHTML = picture;
    say('ovw-temp', Sky.deg(wx.tempC));
    say('ovw-says', wx.says || NAMES[wx.kind] || '');
    const more = [wx.place, wx.windKph ? `Wind ${Sky.wind(wx.windKph)}` : ''].filter(Boolean).join(' · ');
    say('ovw-more', more);
    $('ovw-more').hidden = !more;
    $('ovw-hours').innerHTML = Sky.ahead().slice(0, 5).map((h) => `<div><small>${esc(new Date(h.at).toLocaleTimeString([], { hour: 'numeric' }))}</small>${icon(h.kind, h.day)}`
      + `<span>${esc(NAMES[h.kind] || '')}</span><b>${esc(Sky.deg(h.tempC))}</b></div>`).join('');
  };

  // The weather opens and closes the hours; so do a click anywhere else, Esc, and the button to the settings.
  wxButton.addEventListener('click', () => open(pop.hidden && !wxButton.hidden));
  document.addEventListener('click', (e) => { if (!pop.hidden && (!e.target.closest('#ovw-wx, #ovw-pop') || e.target.closest('#ovw-pop [data-sky-open]'))) open(false); });
  document.addEventListener('keydown', (e) => { if (e.key === 'Escape' && !pop.hidden) { open(false); wxButton.focus(); } });

  // The clock turns with the minute, while Overview shows and the tab is in front; it catches up when either is back.
  let timer = 0;
  const tick = () => {
    clearTimeout(timer);
    if (where()[0] !== 'overview' || document.hidden) {
      open(false);
      return;
    }
    draw();
    timer = setTimeout(tick, 60000 - (Date.now() % 60000) + 50);
  };
  ['rabbidlab:show', 'visibilitychange'].forEach((t) => document.addEventListener(t, tick));
  // The sky changed (a pick in its window, a new reading, day turning to night), or the lab's quotes did: the words follow.
  ['rabbidlab:sky', 'rabbidlab:theme', 'rabbidlab:brand'].forEach((t) => window.addEventListener(t, () => { if (where()[0] === 'overview') draw(); }));
  document.addEventListener('rabbidlab:scene', () => { if (where()[0] === 'overview') draw(); });
  tick();
})();
