// The sign-in page (SignIn.cs): one form for setting Rabbid Lab up and for signing in after. Before setup it makes the
// first local account or connects Active Directory; after, the same form signs in. Someone signed in goes to the console.
(() => {
  const $ = (id) => document.getElementById(id);
  const headers = { 'X-Rabbid-Lab': '1', 'Content-Type': 'application/json', Accept: 'application/json' };
  const KIND = 'rabbidlab-signin-kind'; // which way this browser signed in last
  const getJson = async (url) => {
    const r = await fetch(url, { headers: { Accept: 'application/json' } });
    return { ok: r.ok, status: r.status, body: await r.json().catch(() => ({})) };
  };
  const remember = (kind) => { try { localStorage.setItem(KIND, kind); } catch { /* private browsing */ } };
  const remembered = () => { try { return localStorage.getItem(KIND); } catch { return null; } };

  const showError = (text) => {
    $('error').textContent = text;
    $('error').hidden = !text;
  };

  // The left side's time, date and how much of the day has gone: this computer's, as a lock screen's are. Again on each minute.
  const ink = document.createElement('canvas').getContext('2d');
  const clock = () => {
    const now = new Date();
    const time = $('side-time');
    time.textContent = new Intl.DateTimeFormat([], { hour: 'numeric', minute: '2-digit' }).formatToParts(now)
      .filter((p) => p.type !== 'dayPeriod').map((p) => p.value).join('').trim();
    // Big digits start a few pixels in from their box, a 2 more than a 1: the first is pulled left by its own amount, so
    // the time lines up with the date under it.
    const style = getComputedStyle(time);
    ink.font = `${style.fontWeight} ${style.fontSize} ${style.fontFamily}`;
    time.style.marginLeft = `${Math.min(0, ink.measureText(time.textContent[0] || '0').actualBoundingBoxLeft)}px`;
    $('side-date').textContent = now.toLocaleDateString([], { weekday: 'long', month: 'long', day: 'numeric' });
    const gone = Math.round(((now.getHours() * 60 + now.getMinutes()) / 1440) * 100);
    $('side-day-fill').style.width = `${gone}%`;
    $('side-day-text').textContent = `${now.toLocaleDateString([], { weekday: 'long' })} · ${gone}% of the day elapsed`;
    setTimeout(clock, 60000 - (Date.now() % 60000) + 50);
  };
  clock();
  // What moves behind the card: the lab's (Settings, Appearance), or what this browser picked at the page's foot.
  const Backdrop = window.RabbidLabBackdrop;
  const Theme = window.RabbidLabTheme;
  const BG = 'rabbidlab.backdrop';
  const showBackdrop = Backdrop.on(document.querySelector('canvas.backdrop'), { fit: true });
  let labBg = 'squares';
  let pickedBg = '';
  try { pickedBg = localStorage.getItem(BG) || ''; } catch { /* the lab's */ }
  if (!Backdrop.KINDS.some(([k]) => k === pickedBg)) pickedBg = '';
  const bgNow = () => pickedBg || labBg;
  const showBg = () => {
    showBackdrop(bgNow());
    $('look-name').textContent = Backdrop.KINDS.find(([k]) => k === bgNow())?.[1] || 'Squares';
  };
  const look = (brand) => {
    labBg = brand.background || 'squares';
    showBg();
  };
  look(window.RabbidLabBrand.now);

  // The page's foot: a pill opening tiles of every background, each moving as the page would (only while open), and the themes.
  let tiles = [];
  const drawLook = () => {
    tiles.forEach((stop) => stop());
    $('look-bgs').replaceChildren(...Backdrop.KINDS.map(([k, name]) => {
      const b = document.createElement('button');
      b.type = 'button';
      b.className = 'look-bg';
      b.dataset.kind = k;
      b.setAttribute('role', 'radio');
      b.setAttribute('aria-checked', String(k === bgNow()));
      b.innerHTML = '<canvas aria-hidden="true"></canvas><span></span>';
      b.lastChild.textContent = name;
      return b;
    }));
    tiles = [...$('look-bgs').querySelectorAll('canvas')].map((c) => Backdrop.run(c, c.parentElement.dataset.kind, { width: 240 }));
    const [, , lp] = Theme.THEMES.find(([t]) => t === 'light');
    const [, , dp] = Theme.THEMES.find(([t]) => t === 'dark');
    $('look-themes').replaceChildren(...Theme.THEMES.map(([k, name, page, , card]) => {
      const b = document.createElement('button');
      b.type = 'button';
      b.className = 'look-th';
      b.dataset.pick = k; // not data-theme: that would put the theme's rules on the button
      b.setAttribute('role', 'radio');
      b.setAttribute('aria-checked', String(Theme.picked === k));
      b.innerHTML = '<i></i><span></span>';
      b.lastChild.textContent = name;
      b.firstChild.style.background = `linear-gradient(135deg, ${k === 'system' ? lp : page} 0 50%, ${k === 'system' ? dp : card} 50%)`;
      return b;
    }));
  };
  const lookOpen = (open) => {
    $('look-pop').hidden = !open;
    $('look-open').setAttribute('aria-expanded', String(open));
    if (open) drawLook();
    else {
      tiles.forEach((stop) => stop());
      tiles = [];
    }
  };
  $('look-open').addEventListener('click', () => lookOpen($('look-pop').hidden));
  document.addEventListener('pointerdown', (e) => { if (!$('look-pop').hidden && !$('look').contains(e.target)) lookOpen(false); });
  document.addEventListener('keydown', (e) => {
    if (e.key !== 'Escape' || $('look-pop').hidden) return;
    lookOpen(false);
    $('look-open').focus();
  });
  $('look-bgs').addEventListener('click', (e) => {
    const b = e.target.closest('.look-bg');
    if (!b) return;
    // The lab's own again: this browser follows the lab's from then on.
    pickedBg = b.dataset.kind === labBg ? '' : b.dataset.kind;
    try {
      if (pickedBg) localStorage.setItem(BG, pickedBg);
      else localStorage.removeItem(BG);
    } catch { /* this visit only */ }
    $('look-bgs').querySelectorAll('.look-bg').forEach((t) => t.setAttribute('aria-checked', String(t === b)));
    showBg();
  });
  $('look-themes').addEventListener('click', (e) => {
    const b = e.target.closest('.look-th');
    if (b) Theme.set(b.dataset.pick);
  });
  // Another theme: the background again in its colours, and the tiles too while they show.
  window.addEventListener('rabbidlab:theme', () => {
    showBg();
    if (!$('look-pop').hidden) drawLook();
  });

  // Signed out for being idle (console.js): said here, and signing in goes back to the page they were on.
  const idleFor = Number(new URLSearchParams(location.search).get('idle'));
  if (idleFor > 0) {
    $('idle-text').textContent = `You were signed out after ${idleFor % 60 ? `${idleFor} minutes` : idleFor === 60 ? '1 hour' : `${idleFor / 60} hours`} of inactivity.`;
    $('idle-note').hidden = false;
  }
  const home = () => {
    let back = '';
    try {
      back = sessionStorage.getItem('rabbidlab.back') || '';
      sessionStorage.removeItem('rabbidlab.back');
    } catch { /* none */ }
    return back.startsWith('#') ? `/${back}` : '/';
  };
  // Signed in just now: the console plays its opening screen for this one (not for a refresh).
  const enter = () => {
    try { sessionStorage.setItem('rabbidlab.signedin', '1'); } catch { /* it opens without it */ }
    location.replace(home());
  };

  const unreachable = (then) => `Could not connect to the server. Check that the Rabbid Lab service is running, then ${then}.`;
  // Sends a form with its button off meanwhile. Null when the server can't be reached, which the page then says.
  const send = async (url, body, button) => {
    showError('');
    button.disabled = true;
    try {
      const r = await fetch(url, { method: 'POST', headers, body: JSON.stringify(body) });
      const said = await r.json().catch(() => null);
      return { ok: r.ok, said, problem: said?.error || `The server returned HTTP ${r.status}. Try again.` };
    } catch {
      showError(unreachable('try again'));
      button.disabled = false;
      return null;
    }
  };

  // MFA, after a right password: the answer is a ticket to send the code with, and nobody is signed in yet. MFA is
  // required for everyone, so an account without it gets the QR code to set it up with; after a reset, the one-time code too.
  let ticket = '';
  const hideForms = () => ['local-form', 'ad-form', 'other-kind', 'admin-unlock'].forEach((id) => { $(id).hidden = true; });
  const askCode = (said) => {
    ticket = said.ticket;
    const setup = said.mfa === 'setup';
    hideForms();
    $('heading').textContent = setup ? 'Set up MFA' : 'Enter your code';
    $('lead').hidden = false;
    $('lead').textContent = !setup ? 'Enter the 6-digit code from your authenticator app.'
      : said.reset ? 'Your MFA was reset. Enter the one-time code you received, then scan the QR code with your authenticator app and enter the 6-digit code.'
        : 'Sign-in requires a code from an authenticator app, such as Microsoft Authenticator or Google Authenticator. Scan the QR code with the app, then enter the 6-digit code.';
    $('mfa-reset').value = '';
    if (setup) {
      $('mfa-qr').innerHTML = said.setup.qr; // an SVG picture drawn by the server
      $('mfa-secret').textContent = said.setup.secret.replace(/(.{4})/g, '$1 ').trim();
    }
    $('mfa-code').value = '';
    // Rabbid Lab Dev, in a browser on the server itself: a checkbox to sign in without the code while testing.
    $('mfa-skip-row').hidden = !said.skip;
    $('mfa-skip').checked = false;
    resetShown = !!said.reset;
    skipping(setup);
    $('mfa-form').hidden = false;
    $('mfa-back').hidden = false;
    (said.reset ? $('mfa-reset') : $('mfa-code')).focus();
  };
  // With Skip ticked, the code boxes and the QR code step aside and the button says what it does.
  let setupShown = false;
  let resetShown = false;
  const skipping = (setup = setupShown) => {
    setupShown = setup;
    const skip = $('mfa-skip').checked;
    $('mfa-code-box').hidden = skip;
    $('mfa-setup').hidden = !setup || skip;
    $('mfa-reset-box').hidden = skip || !resetShown;
    $('mfa-go').textContent = skip ? 'Sign in without a code' : setup ? 'Enable and sign in' : 'Verify';
  };
  $('mfa-skip').addEventListener('change', () => skipping());
  $('mfa-back').addEventListener('click', () => location.reload());
  $('mfa-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    const skip = $('mfa-skip').checked && !$('mfa-skip-row').hidden;
    const r = await send('/api/signin/mfa', { ticket, code: skip ? null : $('mfa-code').value, reset: skip ? null : $('mfa-reset').value || null, skip }, $('mfa-go'));
    if (!r) return;
    if (r.ok) return enter();
    if (r.said?.again) return location.reload(); // the ticket ran out: the password again
    showError(r.problem);
    $('mfa-go').disabled = false;
    $('mfa-code').select();
  });

  // The eye in a password box shows or hides what's typed.
  const reveal = (button, show) => {
    $(button.dataset.reveal).type = show ? 'text' : 'password';
    button.classList.toggle('on', show);
    button.setAttribute('aria-label', show ? 'Hide password' : 'Show password');
  };
  document.querySelectorAll('[data-reveal]').forEach((button) => button.addEventListener('click', () => {
    reveal(button, $(button.dataset.reveal).type === 'password');
    $(button.dataset.reveal).focus();
  }));

  // A password that was sent isn't left in its box or showing: not behind the MFA step, and not after a wrong one.
  const PASSWORDS = ['local-password', 'local-confirm', 'ad-password'];
  const forget = () => {
    [...PASSWORDS, 'local-code', 'ad-code'].forEach((id) => { $(id).value = ''; });
    document.querySelectorAll('[data-reveal]').forEach((button) => reveal(button, false));
  };

  // Wrong passwords make the next try wait, longer each time, and enough of them lock the name (SignInLocks.cs). The
  // button counts the wait down; a locked name offers an administrator the code box, to send with the password.
  let counting = 0;
  const kindOf = (button) => button.form.id.split('-')[0]; // local or ad: what its boxes' ids start with
  const coded = (kind) => !$(`${kind}-code-box`).hidden;
  const codeOf = (kind) => (coded(kind) ? $(`${kind}-code`).value : undefined);
  // Back to a form that can be sent.
  const release = (button) => {
    clearInterval(counting);
    button.disabled = false;
    button.classList.remove('wait', 'fill', 'locked');
    button.style.removeProperty('transition');
    button.style.removeProperty('background-size');
    button.textContent = coded(kindOf(button)) ? 'Unlock and sign in' : button.dataset.words;
    $(`${kindOf(button)}-password`).disabled = false;
  };
  const hold = (button, said) => {
    const kind = kindOf(button);
    button.dataset.words ||= button.textContent;
    release(button);
    if (said?.locked && !coded(kind)) {
      button.disabled = true;
      button.classList.add('wait', 'locked');
      button.textContent = 'Locked out';
      $(`${kind}-password`).disabled = true;
      $('admin-unlock').hidden = false;
      return;
    }
    if (!said?.wait) return;
    // The fill is one CSS transition over the whole wait; only the words change, once a second.
    const until = Date.now() + said.wait * 1000;
    button.disabled = true;
    button.classList.add('wait', 'fill');
    button.style.setProperty('transition', 'none');
    button.style.setProperty('background-size', '0% 100%');
    void button.offsetWidth;
    button.style.setProperty('transition', `background-size ${said.wait}s linear`);
    button.style.setProperty('background-size', '100% 100%');
    const tick = () => {
      const left = Math.ceil((until - Date.now()) / 1000);
      if (left <= 0) return release(button);
      button.textContent = `${coded(kind) ? 'Try again in' : 'Retry in'}${left < 60 ? `${left} s` : `${Math.floor(left / 60)}:${String(left % 60).padStart(2, '0')}`}`;
    };
    tick();
    counting = setInterval(tick, 1000);
  };
  // Another name typed, or the other kind of account picked: whatever held the form is that name's, not this one's.
  const fresh = (kind) => {
    const button = $(`${kind}-go`);
    if (!button.dataset.words) return;
    $(`${kind}-code-box`).hidden = true;
    $('admin-unlock').hidden = true;
    showError('');
    release(button);
  };
  ['local', 'ad'].forEach((kind) => $(`${kind}-user`).addEventListener('input', () => fresh(kind)));
  $('admin-unlock').addEventListener('click', () => {
    const kind = $('ad-form').hidden ? 'local' : 'ad';
    $(`${kind}-code-box`).hidden = false;
    $('admin-unlock').hidden = true;
    showError('');
    release($(`${kind}-go`));
    $(`${kind}-password`).focus();
  });

  const post = async (url, body, button) => {
    const r = await send(url, body, button);
    if (!r) return;
    forget();
    if (r.ok && !r.said?.mfa) return enter();
    button.disabled = false;
    if (r.ok) return askCode(r.said);
    showError(r.problem);
    hold(button, r.said);
  };

  // Caps Lock on while typing a password: a line under the box says so.
  PASSWORDS.forEach((id) => {
    const warn = $(`${id}-caps`);
    const check = (e) => { warn.hidden = !e.getModifierState?.('CapsLock'); };
    ['keydown', 'keyup', 'mousedown'].forEach((type) => $(id).addEventListener(type, check));
    $(id).addEventListener('blur', () => { warn.hidden = true; });
  });

  // A box with a list under it: typing filters the list (when it can be typed in), the arrow keys move, Enter picks.
  const combo = ({ input, list, toggle, items, label, detail = () => '', tone = () => '', empty, typing = true, pick }) => {
    let shown = [];
    let active = -1;
    let closing = 0; // the close that waits a moment after the box loses focus

    const render = (opening) => {
      list.replaceChildren();
      if (!shown.length) {
        const li = document.createElement('li');
        li.className = 'empty';
        li.textContent = empty();
        list.append(li);
        return;
      }
      shown.forEach((x, i) => {
        const li = document.createElement('li');
        li.id = `${list.id}-${i}`;
        li.setAttribute('role', 'option');
        li.setAttribute('aria-selected', String(i === active));
        li.classList.toggle('active', i === active);
        const name = document.createElement('b');
        name.textContent = label(x);
        li.append(name);
        const about = detail(x);
        if (about) {
          const small = document.createElement('small');
          small.textContent = about;
          small.className = tone(x);
          li.append(small);
        }
        li.addEventListener('mousedown', (e) => {
          e.preventDefault();
          choose(x);
        });
        list.append(li);
      });
      // Scroll the list only, never the page. On opening, the item before the chosen one sits whole at the top.
      const item = active >= 0 ? $(`${list.id}-${active}`) : null;
      if (item && opening) {
        list.scrollTop = item.previousElementSibling ? item.previousElementSibling.offsetTop - 5 : 0;
      } else if (item && item.offsetTop < list.scrollTop) {
        list.scrollTop = item.offsetTop;
      } else if (item && item.offsetTop + item.offsetHeight > list.scrollTop + list.clientHeight) {
        list.scrollTop = item.offsetTop + item.offsetHeight - list.clientHeight;
      }
    };

    const open = () => {
      clearTimeout(closing);
      const all = items();
      const text = input.value.trim().toLowerCase();
      const exact = all.some((x) => label(x).toLowerCase() === text);
      shown = (!typing || exact || !text ? all : all.filter((x) => `${label(x)} ${detail(x) || ''}`.toLowerCase().includes(text))).slice(0, 200);
      active = shown.findIndex((x) => label(x).toLowerCase() === text);
      list.hidden = false; // shown before drawing, so the chosen item can be scrolled to
      input.setAttribute('aria-expanded', 'true');
      render(true);
    };
    const close = () => {
      list.hidden = true;
      input.setAttribute('aria-expanded', 'false');
    };
    const choose = (x) => {
      input.value = label(x);
      close();
      pick?.(x);
    };

    input.addEventListener('focus', open);
    input.addEventListener('click', () => { if (list.hidden) open(); });
    input.addEventListener('blur', () => { closing = setTimeout(close, 120); });
    input.addEventListener('input', open);
    input.addEventListener('keydown', (e) => {
      if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
        e.preventDefault();
        if (list.hidden) open();
        active = Math.max(0, Math.min(shown.length - 1, active + (e.key === 'ArrowDown' ? 1 : -1)));
        render(false);
      } else if (e.key === 'Enter' && !list.hidden && shown[active]) {
        e.preventDefault();
        choose(shown[active]);
      } else if (e.key === 'Escape') {
        close();
      }
    });
    // The arrow leaves the box its focus, so the list doesn't close under the click.
    toggle.addEventListener('mousedown', (e) => e.preventDefault());
    toggle.addEventListener('click', () => {
      if (!list.hidden) close();
      else if (document.activeElement === input) open();
      else input.focus();
    });
    return { refresh: () => { if (!list.hidden) open(); } };
  };

  // The marker in the domain controller box: which one Automatic would use now, or whether the chosen one answers.
  const short = (name) => name.split('.')[0].toUpperCase();
  const markServer = (controllers, server) => {
    const now = $('server-now');
    const chosen = controllers.find((c) => c.name === server);
    const nearest = controllers.find((c) => c.online);
    const text = server ? (chosen ? (chosen.online ? 'online' : 'unreachable') : '') : nearest ? `using ${short(nearest.name)}` : controllers.length ? 'none reachable' : '';
    now.textContent = text;
    now.hidden = !text;
    now.classList.toggle('off', server ? !!chosen && !chosen.online : !nearest);
    // The same, on the line above the account boxes when signing in.
    const dc = $('ad-context-dc');
    dc.textContent = server ? short(server) + (chosen && !chosen.online ? ' (unreachable)' : '')
      : nearest ? short(nearest.name) : controllers.length ? 'a domain controller (none reachable)' : 'the fastest domain controller';
    dc.classList.toggle('off', server ? !!chosen && !chosen.online : controllers.length > 0 && !nearest);
    dc.classList.toggle('unknown', !controllers.length);
  };

  // Where updating stands, at the foot of the card: asked when the page opens and every 30 seconds while it's open, and
  // every second while an update is under way. Once Rabbid Lab answers as another version, the page reloads as it.
  const watchUpdates = () => {
    let timer = null;
    let target = '';
    let under = false; // the last answer said an update is downloading or installing
    let running = ''; // the version that answered first: another one means it updated
    let since = 0; // when installing began
    let tick = null;
    const say = (text, tone = '', offerCheck = false, offerInstall = false) => {
      $('update-text').textContent = text;
      $('update').className = `update ${tone}`;
      $('update').hidden = false;
      $('update-check').hidden = !offerCheck;
      $('update-install').hidden = !offerInstall;
    };
    // Every open page shows the bar while it updates, with the same numbers.
    const bar = (pct) => {
      $('update-bar').hidden = pct == null;
      if (pct != null) $('update-fill').style.width = `${Math.min(100, pct)}%`;
    };
    // The download fills two thirds of the bar; while Rabbid Lab is stopped to install, it moves on the minute that takes.
    // Counted from when the server said installing began, as the console's own pages do.
    const installing = (forSeconds) => {
      if (forSeconds != null) since = Date.now() - forSeconds * 1000;
      since ||= Date.now();
      const move = () => bar(66 + 32 * (1 - Math.exp(-(Date.now() - since) / 12000)));
      move();
      say(`Installing version ${target}. This page reloads when the update is complete.`, 'busy');
      tick ||= setInterval(move, 250);
    };
    const draw = (u) => {
      target = u.available || target;
      under = ['waiting', 'downloading', 'installing'].includes(u.status);
      if (u.status === 'installing') return installing(u.installingFor);
      clearInterval(tick);
      tick = null;
      since = 0;
      bar(u.status === 'downloading' ? 4 + 62 * (u.total ? (u.downloaded || 0) / u.total : 0) : null);
      if (u.status === 'available') {
        const how = u.installHere ? '' : u.canInstall ? ' Sign in to install it.' : '';
        say(`Version ${u.available} is available.${how}`, 'busy', false, !!u.installHere);
      } else if (u.status === 'waiting') say(`Version ${u.available} will install shortly.`, 'busy');
      else if (u.status === 'downloading') say(`Downloading version ${u.available}…`, 'busy');
      else if (u.status === 'failed') say(`Version ${u.available} failed to install. Sign in for details.`, 'bad');
      else if (u.checking) say('Checking for updates…');
      else say('Up to date', 'ok', true);
    };
    const ask = async (opened = false, wait = false) => {
      clearTimeout(timer);
      let busy = under;
      try {
        const r = await fetch(`/api/update/watch?opened=${opened}&wait=${wait}`, { headers: { Accept: 'application/json' } });
        if (!r.ok) throw new Error(r.statusText);
        const u = await r.json();
        // Back as a new version: a full bar and the version for a moment, then the new page.
        if (running && u.version !== running) {
          clearInterval(tick);
          bar(100);
          say(`Update installed. Now running version ${u.version}.`, 'ok');
          return setTimeout(() => location.reload(), 1500);
        }
        running = u.version;
        draw(u);
        busy = u.checking || under;
      } catch {
        // Not answering in the middle of an update: restarting as the new version. Any other time, nothing is installing.
        if (under) installing();
      }
      timer = setTimeout(ask, busy ? 1000 : 30000);
    };
    const check = () => {
      say('Checking for updates…');
      ask(true, true);
    };
    $('update-check').addEventListener('click', check);
    $('update-install').addEventListener('click', async () => {
      say(`Starting the update to version ${target}…`, 'busy');
      bar(4);
      try {
        const r = await fetch('/api/update/watch/install', { method: 'POST', headers: { 'X-Rabbid-Lab': '1', Accept: 'application/json' } });
        const u = await r.json();
        if (r.ok) draw(u);
        else {
          bar(null);
          say(u.error || r.statusText, 'bad');
        }
      } catch {
        // The next look says where it stands.
      }
      ask();
    });
    check();
  };

  const start = async () => {
    const s = (await getJson('/api/signin')).body;
    if (s.user) return location.replace(home());
    const setup = !s.setUp;

    // The lab's own look: its colours (brand.js), its name in place of Rabbid Lab's, its icon in place of the mark.
    const brand = window.RabbidLabBrand.apply(s.brand);
    const product = s.product.replace(/ Dev$/, ''); // the DEV tag says it's Dev
    const name = brand.name || product;
    document.title = (setup ? 'Set up ' : 'Sign in to ') + name;
    document.querySelectorAll('[data-product]').forEach((e) => { e.textContent = name; });
    // The tagline over the name, with what's known before anyone signs in; one asking for {username} or {name} isn't shown here.
    const tag = window.RabbidLabBrand.fill(brand.tagline, { hostname: s.machine, version: s.version, domain: s.domain });
    $('side-tag').textContent = tag || '';
    $('side-tag').hidden = !tag;
    $('side-sub').textContent = s.domain ? `Lab console for ${s.domain}` : window.RabbidLabBrand.TAG;
    look(brand);
    document.querySelectorAll('[data-version]').forEach((e) => {
      e.textContent = `${s.machine} · ${s.version}`;
      e.title = `${product} ${s.version} on ${s.machine} (${s.platform})`;
    });
    document.body.classList.toggle('is-dev', !!s.dev);
    // What changes between setting up and signing in: the words and the confirm box.
    $('heading').textContent = setup ? 'Set up' : 'Welcome back';
    $('other-kind').hidden = !setup && !s.domain;
    $('confirm-row').hidden = !setup;
    // Signing in with Active Directory, the domain and its controller show on one line, and Change opens the list.
    $('ad-context').hidden = setup;
    $('ad-where').hidden = !setup;
    // The same line for a local account, where there is a domain to tell it from.
    $('local-context').hidden = setup || !s.domain;
    $('local-context-name').textContent = s.machine;
    $('local-go').textContent = setup ? 'Create account' : 'Sign in';
    $('ad-go').textContent = setup ? 'Connect and sign in' : 'Sign in';
    if (setup) {
      $('local-user').value = 'admin';
      $('local-password-label').textContent = 'Password (at least 8 characters)';// the box's name, in the box until it's typed in
      $('local-password').autocomplete = 'new-password';
    }

    // The Domain and Domain controller boxes. Setting up, any domain can be typed or picked; signing in, it's the one set up.
    let domains = setup ? [] : [s.domain];
    let controllers = [];
    let server = setup ? '' : s.server || ''; // empty is Automatic
    let lookedUp = '';
    let started = false;
    let asked = 0; // counts the controller lookups, so only the last one's answer shows
    const domain = () => $('ad-domain').value.trim();
    const showServer = () => markServer(controllers, server);

    const loadControllers = async () => {
      const mine = ++asked;
      controllers = [];
      showServer();
      if (!domain()) return;
      const url = `${setup ? '/api/setup/directory/controllers' : '/api/signin/controllers'}?domain=${encodeURIComponent(domain())}`;
      const r = await getJson(url).catch(() => null);
      if (mine !== asked) return; // the domain changed meanwhile
      controllers = r?.ok ? r.body.controllers : [];
      showServer();
      serverBox.refresh();
    };
    const domainChanged = () => {
      if (domain().toLowerCase() === lookedUp) return;
      lookedUp = domain().toLowerCase();
      // A new domain starts on Automatic, except the one set up, which keeps its own controller.
      server = !setup && domain() === s.domain ? s.server || '' : '';
      $('ad-server').value = server || 'Automatic';
      $('ad-context-domain').textContent = domain();
      loadControllers();
    };

    const domainBox = combo({
      input: $('ad-domain'), list: $('domain-list'), toggle: $('domain-open'), typing: setup,
      items: () => domains, label: (d) => d,
      empty: () => 'No domains found from this computer. Enter the domain name.',
      pick: domainChanged,
    });
    if (setup) {
      $('ad-domain').addEventListener('change', domainChanged);
    } else {
      $('ad-domain').readOnly = true;
      $('ad-domain').closest('.input').classList.add('pick');
    }

    const serverBox = combo({
      input: $('ad-server'), list: $('server-list'), toggle: $('server-open'), typing: false,
      items: () => [{ auto: true }, ...controllers],
      label: (c) => (c.auto ? 'Automatic' : c.name),
      detail: (c) => {
        if (c.auto) {
          const nearest = controllers.find((x) => x.online);
          return nearest ? `The fastest available domain controller at each sign-in. Currently ${nearest.name}.` : 'The fastest available domain controller at each sign-in.';
        }
        return c.online ? `Responded in ${c.ms} ms` : 'Unreachable';
      },
      tone: (c) => (c.auto ? '' : c.online ? 'ok' : 'bad'),
      empty: () => 'Enter the domain first.',
      pick: (c) => {
        server = c.auto ? '' : c.name;
        showServer();
        if (!setup) {
          $('ad-where').hidden = true;
          $('ad-change').setAttribute('aria-expanded', 'false');
          $('ad-user').focus();
        }
      },
    });
    $('ad-server').value = server || 'Automatic';

    // Opening Active Directory the first time: setting up, the domains this computer can find; then the domain's controllers.
    const startDirectory = async () => {
      if (started) return;
      started = true;
      if (setup) {
        if (s.thisDomain) $('ad-domain').value = s.thisDomain;
        const r = await getJson('/api/setup/directory/domains').catch(() => null);
        domains = r?.ok ? r.body.domains : [];
        if (!domain() && domains.length) $('ad-domain').value = domains[0];
        domainBox.refresh();
      } else {
        $('ad-domain').value = s.domain;
        $('ad-context-domain').textContent = s.domain;
      }
      if (domain()) domainChanged();
    };

    // One way shows at a time; the link under the button switches to the other.
    let showing = 'local';
    const choose = (kind, focus = true) => {
      showing = kind;
      $('local-form').hidden = kind !== 'local';
      $('ad-form').hidden = kind !== 'ad';
      // Signing in where a domain is set up has no lead: the line under the button says which account it is, so switching
      // between the two moves nothing.
      const lead = setup ? (kind === 'ad' ? 'Connect to your Active Directory domain.' : 'Create the administrator account.')
        : s.domain ? '' : 'Sign in with your local account.';
      $('lead').textContent = lead;
      $('lead').hidden = !lead;
      $('other-kind').textContent = kind === 'ad' ? 'Use a local account instead' : 'Use Active Directory instead';
      $('other-kind').classList.toggle('to-ad', kind !== 'ad'); // its icon: what it switches to
      $('admin-unlock').hidden = !$(`${kind}-go`).classList.contains('locked');
      showError('');
      if (kind === 'ad') startDirectory();
      if (!focus) return;
      if (kind === 'ad') ($('ad-domain').value || s.thisDomain || s.domain ? $('ad-user') : $('ad-domain')).focus();
      else (setup ? $('local-password') : $('local-user')).focus();
    };
    $('other-kind').addEventListener('click', () => choose(showing === 'ad' ? 'local' : 'ad'));
    // Change, signing in with Active Directory: the domain and controller boxes, with the domains it may sign in from.
    let listed = false;
    $('ad-change').addEventListener('click', async () => {
      const open = $('ad-where').hidden;
      $('ad-where').hidden = !open;
      $('ad-change').setAttribute('aria-expanded', String(open));
      if (!open || listed) return;
      listed = true;
      const r = await getJson('/api/signin/domains').catch(() => null);
      if (r?.ok) domains = r.body.domains;
      domainBox.refresh();
    });

    // .\name in the domain account box means a local account, as in Windows: over to Local account, name and all.
    $('ad-user').addEventListener('input', () => {
      const typed = $('ad-user').value;
      if (setup || !(typed.startsWith('.\\') || typed.startsWith('./'))) return;
      $('local-user').value = typed.slice(2);
      $('ad-user').value = '';
      choose('local');
      $('local-user').setSelectionRange(typed.length - 2, typed.length - 2);
    });

    $('local-form').addEventListener('submit', (e) => {
      e.preventDefault();
      if (setup) {
        if ($('local-password').value !== $('local-confirm').value) return showError("The passwords don't match.");
        post('/api/setup/local', { username: $('local-user').value, password: $('local-password').value }, e.submitter);
        return;
      }
      remember('local');
      post('/api/signin', { username: $('local-user').value, password: $('local-password').value, local: true, code: codeOf('local') }, e.submitter);
    });
    $('ad-form').addEventListener('submit', (e) => {
      e.preventDefault();
      if (setup) {
        post('/api/setup/directory', { domain: domain(), server, username: $('ad-user').value, password: $('ad-password').value }, e.submitter);
        return;
      }
      remember('ad');
      post('/api/signin', {
        username: $('ad-user').value, password: $('ad-password').value,
        server, domain: domain() !== s.domain ? domain() : undefined, code: codeOf('ad'),
      }, e.submitter);
    });

    choose(setup ? (location.hash === '#ad' ? 'ad' : 'local') : s.domain && remembered() !== 'local' ? 'ad' : 'local');
    // Plain HTTP from another computer (PlainHttp.cs): no form to type a password into, and what to do instead. When
    // AllowPlainHttp lets it through anyway, a warning over the form.
    if (s.plain) {
      const https = location.hostname ? ` (such as https://${location.hostname})` : '';
      $('plain-note').textContent = s.plain === 'refused'
        ? `This console uses plain HTTP, so your password and session would be sent over the network unencrypted. Open the console at its HTTPS address${https}. To set up HTTPS, run Setup again and select a certificate, or put an HTTPS reverse proxy in front of the console.`
        : 'This console uses plain HTTP. Your password and session are sent over the network unencrypted. An administrator allowed this with AllowPlainHttp.';
      $('plain-note').classList.toggle('warn', s.plain !== 'refused');
      $('plain-note').hidden = false;
      if (s.plain === 'refused') hideForms();
    }
    document.body.classList.remove('loading'); // drawn once, already in its final layout
    watchUpdates();
  };

  start().catch(() => {
    document.body.classList.remove('loading');
    showError(unreachable('refresh the page'));
  });
})();
