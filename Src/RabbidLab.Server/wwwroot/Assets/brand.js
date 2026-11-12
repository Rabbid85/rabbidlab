// The lab's own look, on the console and the sign-in page: the accent colour everywhere it shows (buttons, bars, the flask,
// the logo's dot, the browser tab's icon), and the lab's icon in the tab. The look last seen is kept in this browser and put
// on before the page draws, so a reload or an update never starts in Rabbid Lab's orange; the page then gives the server's.
(() => {
  // Each accent: its name, a light shade for lines and a main one for buttons.
  const SWATCHES = [['Ember', '#FF8A3D', '#E8542F'], ['Amber', '#F7CF7E', '#E0A33A'], ['Green', '#7FE3B5', '#2FB37A'], ['Teal', '#6FDCDC', '#1FA3A8'],
    ['Blue', '#8DBBFF', '#3D7FE6'], ['Indigo', '#9EA3FF', '#5B62E8'], ['Violet', '#C79BF0', '#8E52D6'], ['Pink', '#F68CC6', '#D6448E'], ['Red', '#FF4D4D', '#E02424']];
  const KEY = 'rabbidlab.brand';
  const lighter = (hex, by) => `#${[1, 3, 5].map((i) => { const c = parseInt(hex.slice(i, i + 2), 16); return Math.round(c + (255 - c) * by).toString(16).padStart(2, '0'); }).join('')}`;
  // Which swatch an accent is (-1: a colour of the person's own, 0: none set, so Ember).
  const swatch = (accent) => {
    if (!accent) return 0;
    const a = accent.toLowerCase();
    if (a === '#e0405a') return 8; // the red before 1.1.21 was pinker: a lab that picked it gets the red that's there now
    return SWATCHES.findIndex((s) => s[2].toLowerCase() === a);
  };
  // An accent's light and main shades; nothing for Rabbid Lab's own.
  const shades = (accent) => {
    if (!/^#[0-9a-f]{6}$/i.test(accent || '')) return [];
    const i = swatch(accent);
    return i >= 0 ? [SWATCHES[i][1], SWATCHES[i][2]] : [lighter(accent, 0.15), accent];
  };
  let now = { name: null, icon: null, accent: null };
  try {
    now = { ...now, ...JSON.parse(localStorage.getItem(KEY) || '{}') };
  } catch {
    // Rabbid Lab's own look until the page says otherwise.
  }
  const colours = () => {
    const [light, main] = shades(now.accent);
    const set = { '--accent': main, '--accent-line': light, '--accent-hover': light, '--accent-soft': light,
      '--ember': main && `linear-gradient(90deg, ${light}, ${main})`, '--foam': light && lighter(light, 0.5) };
    for (const [k, v] of Object.entries(set)) {
      if (v) document.documentElement.style.setProperty(k, v);
      else document.documentElement.style.removeProperty(k);
    }
  };
  // The tab's icon: the lab's own, or Rabbid Lab's with its dot in the accent.
  let asked = 0;
  const tabIcon = async () => {
    const link = document.querySelector('link[rel="icon"]');
    if (!link) return;
    const [, main] = shades(now.accent);
    const mine = ++asked;
    let href = now.icon || 'favicon.svg';
    if (!now.icon && main) {
      try {
        const svg = await (await fetch('favicon.svg')).text();
        href = `data:image/svg+xml,${encodeURIComponent(svg.replace(/#FF6A45/gi, main))}`;
      } catch {
        // The plain one will do.
      }
    }
    if (mine === asked) link.href = href;
  };
  const apply = (brand) => {
    now = { name: brand?.name || null, icon: brand?.icon || null, accent: brand?.accent || null };
    try {
      localStorage.setItem(KEY, JSON.stringify(now));
    } catch {
      // Too big to keep, or no storage: it's asked for again next time.
    }
    colours();
    tabIcon();
    return now;
  };
  colours();
  tabIcon();
  window.RabbidLabBrand = { SWATCHES, swatch, shades, apply, get now() { return now; } };
})();
