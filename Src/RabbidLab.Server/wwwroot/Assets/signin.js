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

  const post = async (url, body, button) => {
    showError('');
    button.disabled = true;
    try {
      const r = await fetch(url, { method: 'POST', headers, body: JSON.stringify(body) });
      if (r.ok) {
        location.replace('/');
        return;
      }
      const said = await r.json().catch(() => null);
      showError(said?.error || `Something went wrong (${r.status}). Try again.`);
    } catch {
      showError("Couldn't reach Rabbid Lab. Check that it's running, then try again.");
    }
    button.disabled = false;
  };

  // The eye in a password box shows or hides what's typed.
  document.querySelectorAll('[data-reveal]').forEach((button) => button.addEventListener('click', () => {
    const input = $(button.dataset.reveal);
    const show = input.type === 'password';
    input.type = show ? 'text' : 'password';
    button.classList.toggle('on', show);
    button.setAttribute('aria-label', show ? 'Hide password' : 'Show password');
    input.focus();
  }));

  // Caps Lock on while typing a password: a line under the box says so.
  ['local-password', 'local-confirm', 'ad-password'].forEach((id) => {
    const warn = $(`${id}-caps`);
    const check = (e) => { warn.hidden = !e.getModifierState?.('CapsLock'); };
    ['keydown', 'keyup', 'mousedown'].forEach((type) => $(id).addEventListener(type, check));
    $(id).addEventListener('blur', () => { warn.hidden = true; });
  });

  // A box with a list under it: typing filters the list (when it can be typed in), the arrow keys move, Enter picks.
  const combo = ({ input, list, toggle, items, label, detail = () => '', tone = () => '', empty, typing = true, pick }) => {
    let shown = [];
    let active = -1;

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
    input.addEventListener('blur', () => setTimeout(close, 120));
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
    toggle.addEventListener('click', () => {
      if (list.hidden) input.focus();
      else close();
    });
    return { refresh: () => { if (!list.hidden) open(); } };
  };

  // The marker in the domain controller box: which one Automatic would use now, or whether the chosen one answers.
  const short = (name) => name.split('.')[0].toUpperCase();
  const markServer = (controllers, server) => {
    const now = $('server-now');
    const chosen = controllers.find((c) => c.name === server);
    const nearest = controllers.find((c) => c.online);
    const text = server ? (chosen ? (chosen.online ? 'online' : 'not answering') : '') : nearest ? `now ${short(nearest.name)}` : controllers.length ? 'none answering' : '';
    now.textContent = text;
    now.hidden = !text;
    now.classList.toggle('off', server ? !!chosen && !chosen.online : !nearest);
    // The same, on the line above the account boxes when signing in.
    const dc = $('ad-context-dc');
    dc.textContent = server ? short(server) + (chosen && !chosen.online ? ', not answering' : '')
      : nearest ? `${short(nearest.name)}, automatic` : controllers.length ? 'None answering' : 'Automatic';
    dc.classList.toggle('off', server ? !!chosen && !chosen.online : controllers.length > 0 && !nearest);
    dc.classList.toggle('unknown', !controllers.length);
  };

  // Where updating stands, at the foot of the card: asked when the page opens and every 30 seconds while it's open, and
  // every 2 while an update is under way. Once Rabbid Lab answers as another version, the page reloads as it.
  const watchUpdates = () => {
    let timer = null;
    let target = '';
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
    const bar = (pct) => {
      $('update-bar').hidden = pct == null;
      if (pct != null) $('update-fill').style.width = `${Math.min(100, pct)}%`;
    };
    // The download fills two thirds of the bar; while Rabbid Lab is stopped to install, it moves on the minute that takes.
    const installing = () => {
      since ||= Date.now();
      bar(66 + 32 * (1 - Math.exp(-(Date.now() - since) / 30000)));
      say(`Installing version ${target}. This page reloads when it's done.`, 'busy');
      tick ||= setInterval(installing, 1000);
    };
    const draw = (u) => {
      target = u.available || target;
      if (u.status === 'installing') return installing();
      clearInterval(tick);
      tick = null;
      since = 0;
      bar(u.status === 'downloading' ? 4 + 62 * (u.total ? (u.downloaded || 0) / u.total : 0) : null);
      if (u.status === 'available') {
        const how = u.installHere ? '' : u.canInstall ? ' Sign in to install it.' : '';
        say(`Version ${u.available} is available.${how}`, 'busy', false, !!u.installHere);
      } else if (u.status === 'waiting') say(`Version ${u.available} installs in a moment.`, 'busy');
      else if (u.status === 'downloading') say(`Downloading version ${u.available}…`, 'busy');
      else if (u.status === 'failed') say(`Version ${u.available} didn't install. Sign in to see why.`, 'bad');
      else if (u.checking) say('Checking for updates…');
      else say('Up to date', 'ok', true);
    };
    const ask = async (opened = false, wait = false) => {
      clearTimeout(timer);
      let busy = !!target;
      try {
        const r = await fetch(`/api/update/watch?opened=${opened}&wait=${wait}`, { headers: { Accept: 'application/json' } });
        if (!r.ok) throw new Error(r.statusText);
        const u = await r.json();
        // Back as a new version: a full bar and the version for a moment, then the new page.
        if (running && u.version !== running) {
          clearInterval(tick);
          bar(100);
          say(`Updated to version ${u.version}.`, 'ok');
          return setTimeout(() => location.reload(), 1500);
        }
        running = u.version;
        draw(u);
        busy = u.checking || ['waiting', 'downloading', 'installing'].includes(u.status);
      } catch {
        // Not answering: restarting as the new version, or stopped.
        if (target) installing();
      }
      timer = setTimeout(ask, busy ? 2000 : 30000);
    };
    $('update-check').addEventListener('click', () => {
      say('Checking for updates…');
      ask(true, true);
    });
    $('update-install').addEventListener('click', async () => {
      say(`Starting version ${target}…`, 'busy');
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
    say('Checking for updates…');
    ask(true, true);
  };

  const start = async () => {
    const s = (await getJson('/api/signin')).body;
    if (s.user) return location.replace('/');
    const setup = !s.setUp;

    // What changes between setting up and signing in: the words, the confirm box, and Keep me signed in.
    document.title = (setup ? 'Set up ' : 'Sign in to ') + s.product;
    document.querySelectorAll('[data-product]').forEach((e) => { e.textContent = s.product; });
    document.querySelectorAll('[data-version]').forEach((e) => {
      e.textContent = `${s.machine} · ${s.version}`;
      e.title = `${s.product} ${s.version} on ${s.machine} (${s.platform})`;
    });
    document.body.classList.toggle('is-dev', !!s.dev);
    $('heading').textContent = setup ? 'Set up' : 'Sign in';
    $('other-kind').hidden = !setup && !s.domain;
    $('confirm-row').hidden = !setup;
    document.querySelectorAll('[data-signed-in]').forEach((e) => { e.hidden = setup; });
    document.querySelectorAll('[data-setup]').forEach((e) => { e.hidden = !setup; });
    // Signing in with Active Directory, the domain and its controller show on one line, and Change opens the list.
    $('ad-context').hidden = setup;
    $('ad-where').hidden = !setup;
    $('local-go').textContent = setup ? 'Create account' : 'Sign in';
    $('ad-go').textContent = setup ? 'Connect and sign in' : 'Sign in';
    if (setup) {
      $('local-user').value = 'admin';
      $('local-password').placeholder = 'Password, at least 8 characters';
      $('local-password').autocomplete = 'new-password';
    }

    // The Domain and Domain controller boxes. Setting up, any domain can be typed or picked; signing in, it's the one set up.
    let domains = setup ? [] : [s.domain];
    let controllers = [];
    let server = setup ? '' : s.server || ''; // empty is Automatic
    let lookedUp = '';
    let started = false;
    const domain = () => $('ad-domain').value.trim();
    const showServer = () => markServer(controllers, server);

    const loadControllers = async () => {
      controllers = [];
      showServer();
      if (!domain()) return;
      const url = `${setup ? '/api/setup/directory/controllers' : '/api/signin/controllers'}?domain=${encodeURIComponent(domain())}`;
      const r = await getJson(url).catch(() => null);
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
      empty: () => 'No domains found from this computer. Type the domain name.',
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
          return nearest ? `The quickest that answers at each sign-in. Now ${nearest.name}.` : 'The quickest domain controller that answers at each sign-in.';
        }
        return c.online ? `Answering in ${c.ms} ms` : 'Not answering';
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
      $('lead').hidden = !setup && kind === 'ad'; // the domain line says it
      $('lead').textContent = setup ? (kind === 'ad' ? 'Connect Rabbid Lab to your domain.' : 'Create the administrator account.')
        : kind === 'ad' ? `Use your ${s.domain} account.` : s.domain ? 'Use a local account.' : `Use your ${s.product} account.`;
      $('other-kind').textContent = kind === 'ad' ? 'Use a local account instead' : 'Use Active Directory instead';
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
      post('/api/signin', { username: $('local-user').value, password: $('local-password').value, remember: $('local-remember').checked, local: true }, e.submitter);
    });
    $('ad-form').addEventListener('submit', (e) => {
      e.preventDefault();
      if (setup) {
        post('/api/setup/directory', { domain: domain(), server, username: $('ad-user').value, password: $('ad-password').value, remember: false }, e.submitter);
        return;
      }
      remember('ad');
      post('/api/signin', {
        username: $('ad-user').value, password: $('ad-password').value, remember: $('ad-remember').checked,
        server, domain: domain() !== s.domain ? domain() : undefined,
      }, e.submitter);
    });

    choose(setup ? (location.hash === '#ad' ? 'ad' : 'local') : s.domain && remembered() !== 'local' ? 'ad' : 'local');
    document.body.classList.remove('loading'); // drawn once, already in its final layout
    watchUpdates();
  };

  start().catch(() => {
    document.body.classList.remove('loading');
    showError("Couldn't reach Rabbid Lab. Check that it's running, then refresh the page.");
  });
})();
