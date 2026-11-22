// The style engine. Every look is made here once: Solid (each part as it always was), Smoked glass, Frosted glass and Dark,
// all flat (a fill, a blur and a thin edge, no sheen or bevel). Each part of the app takes one: windows, the side panel,
// menus and toasts (each person's, Settings, Appearance, Style and Toasts) and the sign-in card (the lab's, Sign-in page).
// A part's look goes on <html> as --sty-<part>-bg, -blur and -edge, plus data-sty-<part>; the part's own style rule reads
// them, falling back to its usual colours. Built from the theme's colours (--panel, --night, --text), so every theme turns
// it. Loaded first on every page, before brand.js and theme.js.
(() => {
  const LOOKS = {
    solid: null,
    glass: (a) => ({ bg: `color-mix(in srgb, var(--panel) ${a}%, transparent)`, blur: 'blur(16px) saturate(135%)', edge: 'color-mix(in srgb, var(--text) 10%, transparent)' }),
    frost: (a) => ({ bg: `color-mix(in srgb, color-mix(in srgb, var(--panel), #fff 12%) ${a}%, transparent)`, blur: 'blur(28px) saturate(180%)', edge: 'color-mix(in srgb, var(--text) 16%, transparent)' }),
    dark: () => ({ bg: 'color-mix(in srgb, var(--night) 88%, transparent)', blur: 'blur(10px) saturate(115%)', edge: 'color-mix(in srgb, var(--text) 8%, transparent)' }),
  };
  // Puts a look on a part: its name (win, panel, menus, toasts, signin), the look, and the glass opacity (20 to 95).
  const set = (part, look, glass = 65) => {
    const d = document.documentElement;
    const key = `sty${part[0].toUpperCase()}${part.slice(1)}`;
    const make = LOOKS[look];
    if (!make) {
      delete d.dataset[key];
      ['bg', 'blur', 'edge'].forEach((k) => d.style.removeProperty(`--sty-${part}-${k}`));
      return;
    }
    d.dataset[key] = look;
    Object.entries(make(Math.min(95, Math.max(20, Number(glass) || 65)))).forEach(([k, v]) => d.style.setProperty(`--sty-${part}-${k}`, v));
  };
  window.RabbidLabStyle = { LOOKS: Object.keys(LOOKS), set, seeThrough: (look) => look === 'glass' || look === 'frost' };
})();
