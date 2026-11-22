// The page area's scroll bar: a thin thumb drawn over the right edge of .main, so the page never gets narrower or wider
// when it comes or goes, and banners run right to the edge. The browser's own bar is hidden there (console.css); the
// scrolling itself is still the browser's: wheel, keys and touch work as ever.
(() => {
  const main = document.querySelector('.main');
  if (!main) return;
  const bar = document.createElement('div');
  bar.className = 'sbar';
  bar.hidden = true;
  bar.setAttribute('aria-hidden', 'true');
  bar.innerHTML = '<i></i>';
  document.body.append(bar);
  const thumb = bar.firstElementChild;

  let queued = 0;
  const place = () => {
    queued = 0;
    const view = main.clientHeight;
    const all = main.scrollHeight;
    bar.hidden = all <= view + 1;
    if (bar.hidden) return;
    const r = main.getBoundingClientRect();
    const track = r.height - 6;
    const h = Math.max(32, (view / all) * track);
    bar.style.top = `${r.top}px`;
    bar.style.height = `${r.height}px`;
    bar.style.left = `${r.right - 12}px`;
    thumb.style.height = `${h}px`;
    thumb.style.transform = `translateY(${(main.scrollTop / (all - view)) * (track - h)}px)`;
  };
  const soon = () => { queued ||= requestAnimationFrame(place); };
  main.addEventListener('scroll', soon, { passive: true });
  addEventListener('resize', soon);
  // A page opening, or growing as its data comes in.
  const sizes = new ResizeObserver(soon);
  sizes.observe(main);
  [...main.children].forEach((el) => sizes.observe(el));
  document.addEventListener('rabbidlab:show', soon);

  // Dragging the thumb moves the page with it; a click above or below it moves a screen.
  thumb.addEventListener('pointerdown', (e) => {
    e.preventDefault();
    e.stopPropagation();
    thumb.setPointerCapture(e.pointerId);
    bar.classList.add('drag');
    const y0 = e.clientY;
    const top0 = main.scrollTop;
    const per = (main.scrollHeight - main.clientHeight) / Math.max(1, bar.clientHeight - 6 - thumb.offsetHeight);
    const move = (m) => { main.scrollTop = top0 + (m.clientY - y0) * per; };
    const up = () => {
      bar.classList.remove('drag');
      thumb.removeEventListener('pointermove', move);
    };
    thumb.addEventListener('pointermove', move);
    thumb.addEventListener('pointerup', up, { once: true });
    thumb.addEventListener('pointercancel', up, { once: true });
  });
  bar.addEventListener('pointerdown', (e) => {
    if (e.target !== bar) return;
    const above = e.clientY < thumb.getBoundingClientRect().top;
    main.scrollBy({ top: (above ? -0.9 : 0.9) * main.clientHeight, behavior: 'smooth' });
  });
  soon();
})();
