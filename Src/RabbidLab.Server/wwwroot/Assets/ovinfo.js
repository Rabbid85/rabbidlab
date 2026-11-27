// Overview's words on its banner: the time and the date, the weather where the lab is with the next five hours under it,
// and the lab's name with its line. The time and the weather open the sky's window (sky.js), so the banner has no chip.
// With no weather to show (no ZIP code set, turned off, or a colour in place of the picture) the time and the name stand
// alone. The weather is the server's reading and its hourly forecast (Weather.cs); nothing here asks for anything.
(() => {
  const R = window.RabbidLab;
  const Sky = window.RabbidLabSky;
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

  const draw = () => {
    const now = new Date();
    const [, hm, ap = ''] = now.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' }).match(/^(.*?)\s*([AP]M)?$/i);
    $('ovw-time').textContent = hm;
    $('ovw-ap').textContent = ap;
    $('ovw-date').textContent = now.toLocaleDateString([], { weekday: 'long', month: 'long', day: 'numeric' });
    const sky = Sky.now();
    const wx = sky.weather;
    const light = document.documentElement.dataset.sky !== 'night';
    $('ovw-wx').hidden = !wx;
    if (wx) {
      $('ovw-temp').innerHTML = `${esc(Sky.deg(wx.tempC))}${icon(wx.kind, light, wx.cover)}`;
      $('ovw-says').textContent = wx.says || { clear: 'Clear', rain: 'Rain', storm: 'Thunderstorm', snow: 'Snow', ice: 'Ice', wind: 'Windy', fog: 'Fog' }[wx.kind] || '';
      const more = [wx.place, wx.windKph ? `Wind ${Sky.wind(wx.windKph)}` : ''].filter(Boolean).join(' · ');
      $('ovw-more').textContent = more;
      $('ovw-more').hidden = !more;
    }
    const hours = wx ? Sky.ahead().slice(0, 5) : [];
    $('ovw-hours').hidden = !hours.length;
    $('ovw-hours').innerHTML = hours.map((h) => `<span><small>${esc(new Date(h.at).toLocaleTimeString([], { hour: 'numeric' }))}</small>${icon(h.kind, h.day)}<b>${esc(Sky.deg(h.tempC))}</b></span>`).join('');
  };

  // The clock turns with the minute, while Overview shows and the tab is in front; it catches up when either is back.
  let timer = 0;
  const tick = () => {
    clearTimeout(timer);
    if (where()[0] !== 'overview' || document.hidden) return;
    draw();
    timer = setTimeout(tick, 60000 - (Date.now() % 60000) + 50);
  };
  ['rabbidlab:show', 'visibilitychange'].forEach((t) => document.addEventListener(t, tick));
  // The sky changed (a pick in its window, a new reading, day turning to night): the words follow.
  ['rabbidlab:sky', 'rabbidlab:theme'].forEach((t) => window.addEventListener(t, () => { if (where()[0] === 'overview') draw(); }));
  document.addEventListener('rabbidlab:scene', () => { if (where()[0] === 'overview') draw(); });
  tick();
})();
