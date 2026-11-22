// The theme (Settings, Appearance), just for this browser: Dark is the style sheets as they are; Light, Carbon, Mocha and
// Slate come from themes.css (Themes.cs), picked by data-theme on <html> before the page draws. System follows Windows.
(() => {
  const KEY = 'rabbidlab.theme';
  // Each one's name, and the colours its tile in Settings is drawn in: the page, the side, a card.
  const THEMES = [['system', 'System'], ['dark', 'Dark', '#0B0C14', '#0D0E17', '#1A1C28'], ['light', 'Light', '#EFEFF3', '#E4E6EC', '#FFFFFF'],
    ['carbon', 'Carbon', '#1B1B1B', '#1E1E1E', '#333333'], ['mocha', 'Mocha', '#201B17', '#231D19', '#3D342D'], ['slate', 'Slate', '#171B20', '#191E24', '#2C333D']];
  const light = matchMedia('(prefers-color-scheme: light)');
  // Someone who hasn't picked one (or picked System in Appearance) gets the lab's (brand.js, look.theme), else Dark.
  const lab = () => {
    const t = window.RabbidLabBrand?.now?.look?.theme;
    return THEMES.some(([k]) => k === t) ? t : 'dark';
  };
  let mine = null;
  try {
    mine = localStorage.getItem(KEY);
  } catch {
    // The lab's, this visit.
  }
  if (!THEMES.some(([k]) => k === mine)) mine = null;
  let picked = mine || lab();
  const shown = () => (picked === 'system' ? (light.matches ? 'light' : 'dark') : picked);
  const apply = () => {
    const t = shown();
    if (t === 'dark') delete document.documentElement.dataset.theme;
    else document.documentElement.dataset.theme = t;
    // The browser's own bars (on a phone, say) in the theme's page colour.
    document.querySelector('meta[name="theme-color"]')?.setAttribute('content', THEMES.find(([k]) => k === t)[2]);
    window.dispatchEvent(new Event('rabbidlab:theme'));
  };
  light.addEventListener('change', () => { if (picked === 'system') apply(); });
  // The lab's look came from the server: someone following it gets its theme.
  window.addEventListener('rabbidlab:brand', () => {
    if (mine || picked === lab()) return;
    picked = lab();
    apply();
  });
  apply();
  window.RabbidLabTheme = {
    THEMES,
    get picked() { return picked; },
    get shown() { return shown(); },
    // True when this person picked one themselves; false when it's the lab's.
    get mine() { return !!mine; },
    set(t) {
      picked = THEMES.some(([k]) => k === t) ? t : 'dark';
      mine = picked;
      try {
        localStorage.setItem(KEY, picked);
      } catch {
        // This visit only.
      }
      apply();
    },
    // Back to the lab's (System in Appearance).
    follow() {
      mine = null;
      try {
        localStorage.removeItem(KEY);
      } catch {
        // This visit only.
      }
      picked = lab();
      apply();
    },
  };
})();
