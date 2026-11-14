// Hints, for every page: the one place a hint comes from. Give an element data-tip="A few words" and this shows it in the
// console's own box (.tipbox in console.css) a quarter of a second after the pointer stops on it, or at once on keyboard
// focus. A title anywhere is turned into a data-tip the first time the pointer meets it, so the browser's grey box never
// shows. Nothing else in the console draws hints.
(() => {
  const box = document.createElement('div');
  box.className = 'tipbox';
  box.setAttribute('role', 'tooltip');
  let tipFor = null;
  let timer = null;

  // The element with a hint at or above this one; a title becomes its data-tip here.
  const tipOf = (el) => {
    const t = el?.closest?.('[data-tip], [title]');
    if (t?.hasAttribute('title')) {
      const text = t.getAttribute('title');
      t.removeAttribute('title');
      if (text) {
        t.dataset.tip = text;
        if (!t.getAttribute('aria-label') && !t.textContent.trim()) t.setAttribute('aria-label', text);
      }
    }
    return t?.dataset.tip ? t : null;
  };
  const hide = () => {
    clearTimeout(timer);
    tipFor = null;
    box.classList.remove('show');
  };
  // A hint that only says what's already on screen stays hidden, unless that text is cut off.
  const clipped = (t) => [t, ...t.querySelectorAll('*')].some((e) => e.scrollWidth > e.clientWidth + 1);
  const repeats = (t) => (t.innerText ?? t.textContent ?? '').includes(t.dataset.tip.trim());
  const show = (t) => {
    if (repeats(t) && !clipped(t)) return;
    // Inside an open dialog or popover it has to be in there too, or the top layer covers it.
    const host = t.closest('dialog[open], :popover-open') || document.body;
    if (box.parentElement !== host) host.append(box);
    box.textContent = t.dataset.tip;
    const r = t.getBoundingClientRect();
    const w = box.offsetWidth;
    const h = box.offsetHeight;
    const up = r.bottom + 8 + h > innerHeight - 8;
    box.classList.toggle('up', up);
    box.style.setProperty('left', `${Math.max(8, Math.min(r.left + r.width / 2 - w / 2, innerWidth - w - 8))}px`);
    box.style.setProperty('top', `${up ? Math.max(8, r.top - h - 8) : r.bottom + 8}px`);
    box.classList.add('show');
  };
  const later = (t, wait) => {
    if (t === tipFor) return;
    hide();
    if (!t) return;
    tipFor = t;
    timer = setTimeout(() => { if (tipFor === t && t.isConnected) show(t); }, wait);
  };

  document.body.append(box);
  document.addEventListener('pointerover', (e) => later(tipOf(e.target), 250));
  document.addEventListener('focusin', (e) => later(e.target.matches?.(':focus-visible') ? tipOf(e.target) : null, 0));
  ['pointerdown', 'scroll', 'focusout', 'keydown'].forEach((ev) => document.addEventListener(ev, hide, true));
  window.addEventListener('blur', hide);
})();
