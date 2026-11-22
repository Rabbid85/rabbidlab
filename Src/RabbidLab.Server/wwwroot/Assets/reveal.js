// Every password box in the console gets an eye that shows what's typed and hides it again, including boxes drawn
// later. Each one is hidden again when its dialog closes.
(() => {
  const icon = (shown) => `<svg aria-hidden="true"><use href="#i-${shown ? 'eye-off' : 'eye'}"/></svg>`;

  const add = (input) => {
    if (input.closest('.pw-wrap')) return;
    const wrap = document.createElement('span');
    wrap.className = 'pw-wrap';
    input.before(wrap);
    wrap.append(input);
    const eye = document.createElement('button');
    eye.type = 'button';
    eye.className = 'pw-eye';
    wrap.append(eye);
    show(wrap, false);
  };

  const show = (wrap, on) => {
    const input = wrap.querySelector('input');
    const eye = wrap.querySelector('.pw-eye');
    input.type = on ? 'text' : 'password';
    eye.innerHTML = icon(on);
    eye.dataset.tip = on ? 'Hide password' : 'Show password';
    eye.setAttribute('aria-label', on ? 'Hide password' : 'Show password');
    eye.setAttribute('aria-pressed', String(on));
  };

  const scan = (root) => {
    if (root.matches?.('input[type="password"]')) add(root);
    root.querySelectorAll?.('input[type="password"]').forEach(add);
  };

  document.addEventListener('click', (e) => {
    const eye = e.target.closest('.pw-eye');
    if (!eye) return;
    const wrap = eye.closest('.pw-wrap');
    show(wrap, wrap.querySelector('input').type === 'password');
    wrap.querySelector('input').focus();
  });
  // A dialog that closes hides what it showed (close doesn't bubble, so it's caught on the way down).
  document.addEventListener('close', (e) => {
    e.target.querySelectorAll?.('.pw-wrap').forEach((wrap) => show(wrap, false));
  }, true);

  scan(document);
  new MutationObserver((changes) => changes.forEach((c) => c.addedNodes.forEach((n) => n.nodeType === 1 && scan(n))))
    .observe(document.body, { childList: true, subtree: true });
})();
