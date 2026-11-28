// The small parts every page draws the same way, and the words they say the same way. One copy, here, on RabbidLab
// beside what console.js gives (dates are there, with when): a page takes what it needs and never has its own.
(() => {
  const R = window.RabbidLab;
  const { esc, svg, toast, closeX } = R;
  const COLOURS = R.palette.slice(0, 9); // the grey at the end is left to roles

  // The side panel: its head (a picture, a name, a line under it), a section with its heading, a strip of label and
  // value pairs, a list of facts, a quick action, and the line that says a list is empty.
  const head = (icon, title, sub) => `<div class="dw-head"><span class="pic dw-pic">${svg(icon)}</span><div class="dw-name"><h2 id="dw-title">${esc(title)}</h2><p>${esc(sub)}</p></div>${closeX}</div>`;
  const section = (title, body, right = '') => `<section class="dw-sec"><div class="dw-sec-head"><h3>${title}</h3>${right}</div>${body}</section>`;
  const strip = (pairs) => `<div class="dw-strip">${pairs.map(([l, v]) => `<div><span class="l">${esc(l)}</span><span class="v">${v}</span></div>`).join('')}</div>`;
  const facts = (rows) => `<ul class="does">${rows.filter(Boolean).map(([k, v]) => `<li><b>${esc(k)}</b><span>${v}</span></li>`).join('')}</ul>`;
  const qa = (attr, icon, label) => `<button type="button" class="dw-qa" ${attr}>${svg(icon)}<span>${esc(label)}</span></button>`;
  const none = (text) => `<p class="muted dw-empty">${esc(text)}</p>`;

  // Marks: a badge in a tone, a chip, quiet text, a number over its label on a banner, a tile's letter and colour.
  const badge = (text, tone) => `<span class="ubadge ${tone}">${esc(text)}</span>`;
  const chip = (text, cls = '', icon = '') => `<span class="uchip ${cls}">${icon ? svg(icon) : ''}${esc(text)}</span>`;
  const muted = (text) => `<span class="muted">${esc(text)}</span>`;
  const stat = (num, label) => `<div><b>${num === null ? '<i class="sk sk-num"></i>' : num}</b><span>${esc(label)}</span></div>`;
  const letter = (name) => esc((String(name).match(/[A-Za-z0-9]/) || ['?'])[0].toUpperCase());
  const colour = (name) => COLOURS[[...String(name)].reduce((n, c) => (n * 31 + c.charCodeAt(0)) >>> 0, 7) % COLOURS.length];

  // Words: a number with its noun, computers counted and named, a list as words, minutes, and two texts that differ
  // only in case.
  const count = (n, one, many = `${one}s`) => `${n.toLocaleString()} ${n === 1 ? one : many}`;
  const many = (n, one = 'computer') => `${n.toLocaleString()} ${one}${n === 1 ? '' : 's'}`;
  const names = (list) => (list.length <= 4 ? list.map((c) => c.computer).join(', ') : many(list.length));
  const listed = (xs) => (xs.length < 2 ? xs.join('') : `${xs.slice(0, -1).join(', ')} and ${xs[xs.length - 1]}`);
  const mins = (m) => (m === 60 ? '1 hour' : m === 1 ? '1 minute' : `${m} minutes`);
  const sameText = (a, b) => String(a).toLowerCase() === String(b).toLowerCase();

  // Whether a client's version is older than one that can do something.
  const below = (version, than) => {
    const [a, b] = [version || '0', than].map((v) => v.split('.').map(Number));
    for (let i = 0; i < 3; i++) if ((a[i] || 0) !== b[i]) return (a[i] || 0) < b[i];
    return false;
  };
  const BRAKE = 10; // more computers than this, and how many has to be typed

  // What this browser keeps: a plain value, or anything as JSON (nothing takes it away). Never an error.
  const stored = (key) => { try { return localStorage.getItem(key); } catch { return null; } };
  const saved = (k) => { try { return JSON.parse(localStorage.getItem(k) || 'null'); } catch { return null; } };
  const save = (k, v) => { try { if (v == null) localStorage.removeItem(k); else localStorage.setItem(k, JSON.stringify(v)); } catch { /* not kept, then */ } };

  // A change that failed says so, unless it was only cancelled.
  const oops = (e) => { if (!e.cancelled) toast('The operation failed.', e.message, { tone: 'warn' }); };

  Object.assign(R, { head, section, strip, facts, qa, none, badge, chip, muted, stat, letter, colour, count, many, names, listed, mins, sameText, below, BRAKE, stored, saved, save, oops });
})();
