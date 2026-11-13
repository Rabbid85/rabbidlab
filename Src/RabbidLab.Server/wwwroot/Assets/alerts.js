// Settings, Alerts: the alert rules (#settings/alerts), the notifications they go to (#settings/notifications) and every
// alert that went (#settings/history). Also R.notes, the Send to drop-down sensors and links pick notifications with, and
// the dialog that adds one. Alerts.cs and Channels.cs on the server.
(() => {
  const R = window.RabbidLab;
  const { $, esc, svg, say, has: can } = R;

  // --- Notifications: the list, their marks, and words for them ----------------------------------------------------------

  let notes = null; // /api/channels, once read, for people who may change alert settings
  const KIND = { email: 'Email', discord: 'Discord', teams: 'Teams', slack: 'Slack', ntfy: 'ntfy', webhook: 'Webhook' };
  const KINDS = ['email', 'discord', 'teams', 'slack', 'ntfy', 'webhook'];
  // Each kind's mark, small and in its colour: an envelope, Discord's face, Teams' T, Slack's hash, ntfy's bubble, a link.
  const MARK = {
    email: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="5" width="18" height="14" rx="2.5"/><path d="m4 7.5 8 5.5 8-5.5"/></svg>',
    discord: '<svg viewBox="0 0 24 24" fill="currentColor" fill-rule="evenodd"><path d="M6.3 6.2C7.9 5.4 9.7 5 12 5s4.1.4 5.7 1.2c1.9 2.7 2.8 5.6 2.6 8.9-1.5 1.2-3 2-4.6 2.5l-1-1.6c-.9.3-1.8.4-2.7.4s-1.8-.1-2.7-.4l-1 1.6c-1.6-.5-3.1-1.3-4.6-2.5-.2-3.3.7-6.2 2.6-8.9zM9.3 10.6c-.9 0-1.5.8-1.5 1.8s.7 1.8 1.5 1.8 1.5-.8 1.5-1.8-.6-1.8-1.5-1.8zM14.7 10.6c-.9 0-1.5.8-1.5 1.8s.7 1.8 1.5 1.8 1.5-.8 1.5-1.8-.6-1.8-1.5-1.8z"/></svg>',
    teams: '<svg viewBox="0 0 24 24" fill="currentColor" fill-rule="evenodd"><circle cx="18" cy="6.8" r="2.3"/><path d="M16.5 10.2h4a1.5 1.5 0 0 1 1.5 1.5v3.6a3.4 3.4 0 0 1-5.5 2.7z"/><path d="M4.5 6h8A2.5 2.5 0 0 1 15 8.5v8a2.5 2.5 0 0 1-2.5 2.5h-8A2.5 2.5 0 0 1 2 16.5v-8A2.5 2.5 0 0 1 4.5 6zM5.3 9h6.4v1.8H9.4V16H7.6v-5.2H5.3z"/></svg>',
    slack: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round"><path d="M9.5 4v16M14.5 4v16M4 9.5h16M4 14.5h16"/></svg>',
    ntfy: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M4 6a2 2 0 0 1 2-2h12a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2h-8l-4 3v-3a2 2 0 0 1-2-2z"/><path d="m8 8.5 2.5 2-2.5 2M12.5 12.5h3.5"/></svg>',
  };
  const mark = (k) => MARK[k] || svg('link');
  // What sends it, as a tag: Gmail, Mail server, Microsoft 365, Webhook or Topic.
  const how = (n) => (n.kind === 'email' ? { gmail: 'Gmail', m365: 'Microsoft 365' }[n.mail?.provider] || 'Mail server' : n.kind === 'ntfy' ? 'Topic' : 'Webhook');
  const named = (id) => notes?.channels.find((n) => n.id === id);
  const and = (names) => (names.length < 2 ? names.join('') : `${names.slice(0, -1).join(', ')} and ${names[names.length - 1]}`);
  const words = (ids) => and((ids || []).map((id) => named(id)?.name).filter(Boolean));
  const chip = (n) => `<span class="np-chip"><span class="chan-ic ${n.kind}">${mark(n.kind)}</span>${esc(n.name)}</span>`;
  const load = async () => {
    if (!can('sensors.alerts')) return null;
    try {
      notes = await R.get('/api/channels');
    } catch {
      notes = notes || null;
    }
    return notes;
  };

  // Send to: the picked notifications as chips on a button, and a menu of them all by kind to tick, with a line to add one.
  // picked is changed in place; then() runs after every change.
  let pickers = 0;
  const pick = (host, picked, then = () => {}) => {
    const n = ++pickers;
    const draw = () => {
      const list = notes?.channels || [];
      const on = list.filter((x) => picked.includes(x.id));
      host.innerHTML = `<button type="button" class="np-btn" popovertarget="np-menu-${n}" aria-haspopup="true">${on.length ? on.map(chip).join('') : '<span class="np-none">Nowhere yet</span>'}<svg class="np-chev" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M6 9l6 6 6-6"/></svg></button>`
        + `<div class="np-menu" id="np-menu-${n}" popover>`
        + KINDS.filter((k) => list.some((x) => x.kind === k)).map((k) => `<p class="np-h">${KIND[k]}</p>`
          + list.filter((x) => x.kind === k).map((x) => `<button type="button" class="np-opt${picked.includes(x.id) ? ' on' : ''}" data-np="${esc(x.id)}" aria-pressed="${picked.includes(x.id)}">`
            + `<span class="chan-ic ${x.kind}">${mark(x.kind)}</span><span class="np-t">${esc(x.name)}<small>${esc(how(x))}${x.ready ? '' : ' · not finished'}</small></span><i class="np-box">${svg('check')}</i></button>`).join('')).join('')
        + (list.length ? '' : '<p class="np-empty">No notifications yet.</p>')
        + `<button type="button" class="np-add" data-np-add>${svg('plus')}Add a notification</button></div>`;
      const btn = host.querySelector('.np-btn');
      const menu = host.querySelector('.np-menu');
      btn.style.setProperty('anchor-name', `--np-${n}`);
      menu.style.setProperty('position-anchor', `--np-${n}`);
    };
    host.onclick = (e) => {
      const o = e.target.closest('[data-np]');
      if (o) {
        const at = picked.indexOf(o.dataset.np);
        if (at < 0) picked.push(o.dataset.np);
        else picked.splice(at, 1);
        const open = host.querySelector('.np-menu').matches(':popover-open');
        draw();
        if (open) host.querySelector('.np-menu').showPopover();
        return then();
      }
      if (e.target.closest('[data-np-add]')) {
        host.querySelector('.np-menu').hidePopover();
        openNotify(null, (id) => {
          if (id && !picked.includes(id)) picked.push(id);
          draw();
          then();
        });
      }
    };
    draw();
  };

  R.notes = { load, list: () => notes, name: (id) => named(id)?.name || '', words, mark, pick };

  // --- Add a notification, or change one ----------------------------------------------------------------------------------

  const SUB = { email: 'Gmail or a mail server', discord: 'A channel webhook', teams: 'A Workflows webhook', slack: 'An incoming webhook', ntfy: 'Pushes to your phone', webhook: 'JSON to any address' };
  const HOOK = {
    discord: ['Webhook address', 'https://discord.com/api/webhooks/…', 'In Discord: channel settings, Integrations, Webhooks, New Webhook, Copy Webhook URL.'],
    teams: ['Webhook address', 'https://…', 'In Teams: the channel menu, Workflows, Post to a channel when a webhook request is received. Copy the address it gives you.'],
    slack: ['Webhook address', 'https://hooks.slack.com/services/…', 'In Slack: add the Incoming Webhooks app to a channel and copy its Webhook URL.'],
    ntfy: ['Topic address', 'https://ntfy.sh/lab-alerts', 'The ntfy server and a topic. Subscribe to the same topic in the ntfy app on your phone. Anyone who knows the topic can read it, so make it hard to guess.'],
    webhook: ['Address', 'https://example.com/hooks/lab', 'Alerts are posted as JSON: event (down, up, warn), subject, facts, sentBy and at.'],
  };
  const PROV = {
    gmail: ['Gmail', 'With an app password', '<svg viewBox="0 0 24 24"><path fill="#4285F4" d="M3 7v11a1.5 1.5 0 0 0 1.5 1.5H7v-9z"/><path fill="#34A853" d="M17 10.5v9h2.5A1.5 1.5 0 0 0 21 18V7z"/><path fill="#EA4335" d="M3 7l9 6.5L21 7V6a1.5 1.5 0 0 0-2.4-1.2L12 9.6 5.4 4.8A1.5 1.5 0 0 0 3 6z"/></svg>'],
    m365: ['Microsoft 365', 'Through Microsoft Graph', '<svg viewBox="0 0 24 24"><rect x="3" y="3" width="8.5" height="8.5" fill="#F25022"/><rect x="12.5" y="3" width="8.5" height="8.5" fill="#7FBA00"/><rect x="3" y="12.5" width="8.5" height="8.5" fill="#00A4EF"/><rect x="12.5" y="12.5" width="8.5" height="8.5" fill="#FFB900"/></svg>'],
    smtp: ['Other mail server', 'Any SMTP server', svg('server')],
  };
  const PORTS = { starttls: 587, ssl: 465, none: 25 };
  let editingNote = null;
  let noteKind = 'email';
  let provider = 'gmail';
  let security = 'starttls';
  let mailTo = [];
  let noteThen = null; // what to do with a new one's id, when it was added from a Send to

  const drawKinds = () => {
    $('nf-kinds').hidden = !!editingNote;
    $('nf-kinds').innerHTML = KINDS.map((k) => `<button type="button" class="chan${k === noteKind ? ' on' : ''}" data-nf-kind="${k}" aria-pressed="${k === noteKind}">`
      + `<span class="chan-ic ${k}">${mark(k)}</span><span><b>${KIND[k]}</b><small>${SUB[k]}</small></span><i class="chan-tick">${svg('check')}</i></button>`).join('');
    const mail = noteKind === 'email';
    $('nf-hook').hidden = mail;
    $('nf-mail').hidden = !mail;
    if (!mail) {
      const [label, ph, help] = HOOK[noteKind];
      $('nf-url-label').textContent = label;
      $('nf-url').placeholder = editingNote ? 'Leave it empty to keep the saved one' : ph;
      $('nf-url-help').textContent = help;
    }
    drawMail();
  };
  const drawMail = () => {
    $('nf-provs').innerHTML = Object.entries(PROV).map(([p, [name, sub, ic]]) => `<button type="button" class="chan${p === provider ? ' on' : ''}" data-nf-prov="${p}" aria-pressed="${p === provider}">`
      + `<span class="chan-ic nf-p ${p}">${ic}</span><span><b>${name}</b><small>${sub}</small></span><i class="chan-tick">${svg('check')}</i></button>`).join('');
    $('nf-mail').querySelectorAll('[data-for]').forEach((el) => { el.hidden = !el.dataset.for.split(' ').includes(provider); });
    $('nf-sec').querySelectorAll('button').forEach((b) => b.setAttribute('aria-pressed', b.dataset.sec === security));
    const m = editingNote?.mail;
    const stored = (has) => (has && editingNote?.mail?.provider === provider ? 'Stored. Type a new one to change it.' : '');
    $('nf-gpass').placeholder = stored(m?.hasPassword);
    $('nf-pass').placeholder = stored(m?.hasPassword);
    $('nf-secret').placeholder = stored(m?.hasSecret);
    $('nf-to').innerHTML = mailTo.map((a, i) => `<span class="np-chip addr">${esc(a)}<button type="button" class="np-x" data-to-x="${i}" aria-label="Take ${esc(a)} off">${svg('x')}</button></span>`).join('')
      + '<input id="nf-to-in" placeholder="Add an address" aria-label="Add an address" autocomplete="off" spellcheck="false" data-1p-ignore data-lpignore="true" data-bwignore>';
  };
  // An address typed in Send alerts to becomes a chip at a comma, a space, Enter or leaving the box.
  const takeTo = () => {
    const box = $('nf-to-in');
    if (!box) return;
    const found = box.value.split(/[,;\s]+/).map((a) => a.trim()).filter(Boolean);
    if (!found.length) return;
    found.forEach((a) => { if (!mailTo.some((x) => x.toLowerCase() === a.toLowerCase())) mailTo.push(a); });
    drawMail();
    $('nf-to-in').focus();
  };
  const openNotify = async (n, then = null) => {
    if (!notes) await load();
    editingNote = n || null;
    noteThen = then;
    noteKind = n?.kind || 'email';
    provider = n?.mail?.provider || 'gmail';
    security = n?.mail?.security || 'starttls';
    mailTo = [...(n?.mail?.to || [])];
    $('nf-title').textContent = n ? `Change ${n.name}` : 'Add a notification';
    $('nf-name').value = n?.name || '';
    $('nf-url').value = '';
    const m = n?.mail || {};
    $('nf-gmail').value = m.provider === 'gmail' ? m.username || '' : '';
    $('nf-gpass').value = '';
    $('nf-host').value = m.provider === 'smtp' ? m.host || '' : '';
    $('nf-port').value = m.provider === 'smtp' ? m.port || 587 : 587;
    $('nf-user').value = m.provider === 'smtp' ? m.username || '' : '';
    $('nf-pass').value = '';
    $('nf-from').value = m.provider !== 'gmail' ? m.from || '' : '';
    $('nf-tenant').value = m.tenant || '';
    $('nf-client').value = m.clientId || '';
    $('nf-secret').value = '';
    $('nf-delete').hidden = !n;
    $('nf-save').textContent = n ? 'Save' : 'Add the notification';
    say('nf-done', '');
    say('nf-problem', '');
    drawKinds();
    $('notify-dialog').showModal();
    $('nf-name').focus();
  };
  const noteForm = () => {
    takeTo();
    const form = { kind: noteKind, name: $('nf-name').value, url: $('nf-url').value };
    if (noteKind !== 'email') return form;
    const to = [...mailTo];
    form.mail = provider === 'gmail' ? { provider, username: $('nf-gmail').value, password: $('nf-gpass').value, to }
      : provider === 'm365' ? { provider, tenant: $('nf-tenant').value, clientId: $('nf-client').value, clientSecret: $('nf-secret').value, from: $('nf-from').value, to }
        : { provider, host: $('nf-host').value, port: Number($('nf-port').value) || PORTS[security], security, username: $('nf-user').value, password: $('nf-pass').value, from: $('nf-from').value, to };
    return form;
  };
  $('nf-kinds').addEventListener('click', (e) => {
    const b = e.target.closest('[data-nf-kind]');
    if (!b) return;
    noteKind = b.dataset.nfKind;
    say('nf-problem', '');
    drawKinds();
  });
  $('nf-provs').addEventListener('click', (e) => {
    const b = e.target.closest('[data-nf-prov]');
    if (!b) return;
    takeTo();
    provider = b.dataset.nfProv;
    drawMail();
  });
  // Encryption picked: the port follows, unless one of its own was typed.
  $('nf-sec').addEventListener('click', (e) => {
    const b = e.target.closest('[data-sec]');
    if (!b) return;
    if (Object.values(PORTS).includes(Number($('nf-port').value)) || !$('nf-port').value) $('nf-port').value = PORTS[b.dataset.sec];
    security = b.dataset.sec;
    takeTo();
    drawMail();
  });
  $('nf-to').addEventListener('click', (e) => {
    const x = e.target.closest('[data-to-x]');
    if (x) {
      mailTo.splice(Number(x.dataset.toX), 1);
      drawMail();
    }
    $('nf-to-in')?.focus();
  });
  $('nf-to').addEventListener('keydown', (e) => {
    if (e.target.id !== 'nf-to-in') return;
    if (['Enter', ',', ';', ' '].includes(e.key)) {
      e.preventDefault();
      takeTo();
    } else if (e.key === 'Backspace' && !e.target.value && mailTo.length) {
      mailTo.pop();
      drawMail();
      $('nf-to-in').focus();
    }
  });
  $('nf-to').addEventListener('focusout', (e) => { if (e.target.id === 'nf-to-in') takeTo(); });
  $('nf-to').addEventListener('paste', () => setTimeout(takeTo, 0));
  $('nf-test').addEventListener('click', async () => {
    say('nf-done', '');
    say('nf-problem', '');
    $('nf-test').disabled = true;
    R.busy($('notify-dialog').querySelector('.nf-body'), 'Sending a test…');
    try {
      await R.send('/api/channels/test', { id: editingNote?.id || null, form: noteForm() });
      say('nf-done', noteKind === 'email' ? `Sent to ${mailTo.join(', ')}. Check the inbox, and the junk folder.` : 'Sent a test. Look for it there.');
    } catch (err) {
      say('nf-problem', err.message);
    }
    R.busy($('notify-dialog').querySelector('.nf-body'), '');
    $('nf-test').disabled = false;
  });
  $('nf-save').addEventListener('click', async () => {
    $('nf-save').disabled = true;
    try {
      if (editingNote) {
        notes = await R.send(`/api/channels/${encodeURIComponent(editingNote.id)}`, noteForm());
      } else {
        const made = await R.send('/api/channels', noteForm());
        notes = made.now;
        noteThen?.(made.id);
      }
      $('notify-dialog').close();
      drawNotes();
      R.toast(editingNote ? `Saved ${$('nf-name').value.trim()}` : `Added ${$('nf-name').value.trim()}`, editingNote ? '' : 'Pick it in an alert rule, or under Send to on a sensor or link.');
    } catch (err) {
      say('nf-problem', err.message);
    }
    $('nf-save').disabled = false;
  });
  $('nf-cancel').addEventListener('click', () => $('notify-dialog').close());
  // What uses one: 11 sensors · 2 links.
  const usedWords = (n) => [n?.sensors ? `${n.sensors} sensor${n.sensors === 1 ? '' : 's'}` : '', n?.links ? `${n.links} link${n.links === 1 ? '' : 's'}` : ''].filter(Boolean).join(' · ');
  $('nf-delete').addEventListener('click', () => {
    const n = editingNote;
    if (!n) return;
    const used = usedWords(n);
    R.sure(`Delete ${n.name}?`, used ? `${used} send to it, and stop sending to it. Alert rules drop it too.` : 'Nothing sends to it.', 'Delete', async () => {
      try {
        notes = await R.send(`/api/channels/${encodeURIComponent(n.id)}/delete`, {});
        $('notify-dialog').close();
        drawNotes();
        if (rules) loadRules();
      } catch (err) {
        say('nf-problem', err.message);
      }
    });
  });

  // --- Settings, Notifications: every notification by its name, in groups by kind --------------------------------------------

  // A time as the list says it: Today 6:20 PM, Yesterday 9:14 PM, or Nov 3, 6:20 PM.
  const day = (d) => {
    const today = new Date();
    const y = new Date(today);
    y.setDate(today.getDate() - 1);
    return d.toDateString() === today.toDateString() ? 'Today' : d.toDateString() === y.toDateString() ? 'Yesterday'
      : d.toLocaleDateString([], { month: 'short', day: 'numeric', ...(d.getFullYear() === today.getFullYear() ? {} : { year: 'numeric' }) });
  };
  const at = (iso) => {
    const d = new Date(iso);
    return `${day(d)} ${d.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' })}`;
  };
  const drawNotes = () => {
    if (!notes || R.where()[0] !== 'settings') return;
    const q = $('nt-find').value.trim().toLowerCase();
    const list = notes.channels.filter((n) => !q || [n.name, KIND[n.kind], how(n), n.where].some((v) => String(v || '').toLowerCase().includes(q)));
    const card = (n) => {
      const used = usedWords(n);
      const last = n.lastTried ? (n.lastProblem ? ['bad', at(n.lastTried), 'last try failed'] : ['ok', at(n.lastSent), 'last sent']) : ['off', 'Never sent', 'nothing yet'];
      return `<div class="nrow"><span class="chan-ic ${n.kind}">${mark(n.kind)}</span>`
        + `<div class="nnm"><b>${esc(n.name)}<em>${esc(how(n))}</em>${n.ready ? '' : '<em class="warn">Not finished</em>'}</b><span title="${esc(n.where)}">${esc(n.where)}</span></div>`
        + `<div class="nst"><b>${esc(used || 'Not used yet')}</b>${!used ? 'pick it in a rule' : n.sensors + n.links === 1 ? 'sends to it' : 'send to it'}</div>`
        + `<div class="nst"${n.lastProblem ? ` title="${esc(n.lastProblem)}"` : ''}><b><i class="${last[0]}"></i>${esc(last[1])}</b>${last[2]}</div>`
        + `<div class="nacts"><button type="button" class="ds-btn" data-nt-test="${esc(n.id)}">Test</button><button type="button" class="ds-btn" data-nt-edit="${esc(n.id)}">Edit</button></div></div>`;
    };
    $('nt-list').innerHTML = KINDS.filter((k) => list.some((n) => n.kind === k)).map((k) => {
      const mine = list.filter((n) => n.kind === k);
      return `<section class="ngroup"><h4>${KIND[k]} <small>${mine.length}</small></h4><div class="nrows">${mine.map(card).join('')}</div></section>`;
    }).join('') || (notes.channels.length ? '<p class="hint">Nothing matches.</p>'
      : `<div class="nt-empty"><span class="big-ic ember">${svg('bell')}</span><div><b>No notifications yet</b><span>Add one: an email to you, a Discord channel, your phone through ntfy. Then pick it in an alert rule.</span></div></div>`);
  };
  $('nt-find').addEventListener('input', drawNotes);
  $('nt-add').addEventListener('click', () => openNotify(null));
  $('nt-list').addEventListener('click', async (e) => {
    const ed = e.target.closest('[data-nt-edit]');
    if (ed) return openNotify(named(ed.dataset.ntEdit));
    const t = e.target.closest('[data-nt-test]');
    if (!t) return;
    const n = named(t.dataset.ntTest);
    t.disabled = true;
    try {
      await R.send('/api/channels/test', { id: n.id });
      R.toast(`Sent a test to ${n.name}`, n.kind === 'email' ? `To ${(n.mail?.to || []).join(', ')}. Check the junk folder too.` : 'Look for it there.');
    } catch (err) {
      R.toast(`${n.name} didn't take the test`, err.message, { tone: 'warn' });
    }
    await load();
    drawNotes();
  });

  // --- Settings, Alert rules: every kind of alert, when it goes and where -------------------------------------------------------

  let rules = null; // /api/alerts: { now: { rules, quiet }, counts, gather }
  const ICON = {
    down: '<path d="M3 12h4l3 7 4-14 3 7h4"/>',
    cert: '<rect x="4" y="3" width="16" height="13" rx="2"/><path d="M8 20l2-4M16 20l-2-4M8 8h8M8 11h5"/>',
    space: '<ellipse cx="12" cy="6" rx="7" ry="3"/><path d="M5 6v12c0 1.7 3.1 3 7 3s7-1.3 7-3V6M5 12c0 1.7 3.1 3 7 3s7-1.3 7-3"/>',
    hw: '<rect x="3" y="4" width="18" height="7" rx="2"/><rect x="3" y="13" width="18" height="7" rx="2"/><path d="M7 7.5h.01M7 16.5h.01M15 7.5h3M15 16.5h3"/>',
    link: '<path d="M10 13a5 5 0 0 0 7.5.5l3-3a5 5 0 0 0-7-7l-1.7 1.7"/><path d="M14 11a5 5 0 0 0-7.5-.5l-3 3a5 5 0 0 0 7 7l1.7-1.7"/>',
    seat: '<rect x="3" y="4" width="18" height="12" rx="2"/><path d="M8 20h8M12 16v4"/>',
    upd: '<path d="M21 12a9 9 0 1 1-3-6.7L21 8"/><path d="M21 3v5h-5"/>',
    lock: '<rect x="5" y="11" width="14" height="10" rx="2"/><path d="M8 11V7a4 4 0 0 1 8 0v4"/>',
  };
  const icon = (k) => `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">${ICON[k]}</svg>`;
  const RULES = [
    { key: 'sensor.cert', group: 'sensors', ic: 'cert', title: 'A certificate is close to expiring', kind: 'cert' },
    { key: 'sensor.space', group: 'sensors', ic: 'space', title: 'A pool or drive runs low', kind: 'space' },
    { key: 'sensor.hardware', group: 'sensors', ic: 'hw', title: 'A fan, power supply or disk reports a fault', kind: 'hw' },
    { key: 'seat.offline', group: 'seats', ic: 'seat', title: 'A computer stops checking in', kind: 'seat' },
    { key: 'update.ready', group: 'lab', ic: 'upd', title: 'An update is ready', kind: 'plain', when: 'Once for each new version' },
    { key: 'update.failed', group: 'lab', ic: 'upd', title: "An update didn't install", kind: 'plain', when: 'Right away' },
    { key: 'signin.failing', group: 'lab', ic: 'lock', title: 'Sign-ins keep failing', kind: 'signin' },
    // Down alerts go by each sensor's or link's alert type; these two only name them in the history.
    { key: 'sensor.down', group: '', ic: 'down', title: 'A sensor stopped answering', kind: 'times' },
    { key: 'link.down', group: '', ic: 'link', title: 'A watched link stopped answering', kind: 'times' },
  ];
  const GROUPS = { sensors: 'Sensor warnings', seats: 'Seats', lab: '' }; // lab: the lab's name, when it's drawn
  const ruleOf = (key) => RULES.find((x) => x.key === key);
  const mins = (m) => (m === 60 ? 'an hour' : m === 1 ? 'a minute' : `${m} minutes`);
  const EVERY = { 60: 'every hour', 360: 'every 6 hours', 1440: 'every day', 10080: 'every week' };
  // A rule or an alert type in a line: After 5 minutes down, once, and an all-clear when it comes back.
  const when = (x, r) => {
    const again = r.again ? `again ${EVERY[r.again]}` : 'once';
    switch (x.kind) {
      case 'times': return `${r.after ? `After ${mins(r.after)} down` : 'Right away'}, ${again}${r.back ? ', and an all-clear when it comes back' : ''}`;
      case 'cert': return `${r.limit} days before it expires${r.again ? `, then ${r.again === 10080 ? 'once a week' : 'once a day'}` : ''}`;
      case 'space': return `Below ${r.limit}% free${r.again ? `, again ${EVERY[r.again]} while it stays low` : ''}`;
      case 'hw': return `Right away${r.again ? `, again ${EVERY[r.again]} while it lasts` : ''}`;
      case 'seat': return `Its client has been quiet for ${r.limit} minutes${r.back ? ', and an all-clear when it checks in' : ''}`;
      case 'signin': return `${r.limit} failed sign-ins for one account within 10 minutes`;
      default: return x.when;
    }
  };
  const meta = (x) => {
    const c = rules.counts[x.key];
    return c ? `<b>${c.count} sensor${c.count === 1 ? '' : 's'}</b>${{ 'sensor.cert': 'check certificates', 'sensor.space': 'watch space', 'sensor.hardware': 'watch hardware' }[x.key]}` : '';
  };
  const toChips = (ids) => {
    const to = ids.map(named).filter(Boolean);
    return to.length ? to.map(chip).join('') : '<span class="np-chip none">Not sending anywhere</span>';
  };
  const drawRules = () => {
    if (!rules) return;
    GROUPS.lab = R.app();
    const r = rules.now.rules;
    const types = rules.now.types;
    $('ar-types').innerHTML = `<h4 class="ar-h">Alert types <small>${types.length}</small><button type="button" class="link ar-add" data-type-add>${svg('plus')}New alert type</button></h4>`
      + `<p class="ar-lead">Sensors and watched links each pick one by name. Change one here and everything using it changes too.</p><div class="rrows">${types.map((t) => {
        const used = usedWords(rules.counts.types[t.id]);
        return `<div class="rrow"><span class="ric down">${icon('down')}</span>`
          + `<div class="rnm"><b>${esc(t.name)}${t.id === rules.now.startType ? '<em class="ar-tag">New ones start with it</em>' : ''}</b><span>${esc(when({ kind: 'times' }, t))}</span><div class="to">${toChips(t.to)}</div></div>`
          + `<div class="rmeta">${used ? `<b>${esc(used)}</b>use it` : '<b>Not used yet</b>pick it on a sensor'}</div>`
          + `<div class="racts"><button type="button" class="ds-btn" data-type-edit="${esc(t.id)}">Edit</button></div></div>`;
      }).join('')}</div>`;
    Object.keys(GROUPS).forEach((g) => {
      const mine = RULES.filter((x) => x.group === g);
      $(`ar-${g}`).innerHTML = `<h4 class="ar-h">${esc(GROUPS[g])} <small>${mine.length}</small></h4><div class="rrows">${mine.map((x) => {
        const s = r[x.key];
        return `<div class="rrow${s.on ? '' : ' off'}"><span class="ric ${x.ic}">${icon(x.ic)}</span>`
          + `<div class="rnm"><b>${esc(x.title)}</b><span>${esc(when(x, s))}</span><div class="to">${toChips(s.to)}</div></div>`
          + `<div class="rmeta">${meta(x)}</div><div class="racts"><input type="checkbox" class="switch" data-rule-on="${x.key}" role="switch" aria-label="${esc(x.title)}"${s.on ? ' checked' : ''}>`
          + `<button type="button" class="ds-btn" data-rule-edit="${x.key}">Edit</button></div></div>`;
      }).join('')}</div>`;
    });
    const q = rules.now.quiet;
    $('ar-gather').checked = rules.gather.on;
    $('ar-secs').querySelectorAll('button').forEach((b) => b.setAttribute('aria-pressed', Number(b.dataset.secs) === rules.gather.secs));
    $('ar-quiet').checked = q.on;
    $('ar-from').value = q.from;
    $('ar-to').value = q.to;
    $('ar-down').checked = q.downStillGoes;
    $('ar-quiet-now').hidden = !q.now;
  };
  const loadRules = async () => {
    try {
      await load();
      rules = await R.get('/api/alerts');
      say('ar-problem', '');
    } catch (err) {
      say('ar-problem', err.message);
    }
    drawRules();
  };
  const saveRule = async (key, change) => {
    try {
      rules = await R.send(`/api/alerts/rules/${key}`, change);
      say('ar-problem', '');
      return true;
    } catch (err) {
      say('ar-problem', err.message);
      return false;
    } finally {
      drawRules();
    }
  };
  $('page-settings').addEventListener('change', (e) => {
    const k = e.target.dataset?.ruleOn;
    if (k) saveRule(k, { on: e.target.checked });
  });
  // The times as the clock says them, every half hour.
  const halves = [...Array(48).keys()].map((i) => {
    const hh = String(Math.floor(i / 2)).padStart(2, '0');
    const mm = i % 2 ? '30' : '00';
    return [`${hh}:${mm}`, new Date(2000, 0, 1, Math.floor(i / 2), i % 2 ? 30 : 0).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' })];
  });
  ['ar-from', 'ar-to'].forEach((id) => { $(id).innerHTML = halves.map(([v, t]) => `<option value="${v}">${t}</option>`).join(''); });
  const setQuiet = async (change) => {
    try {
      rules = await R.send('/api/alerts/quiet', change);
      say('ar-problem', '');
    } catch (err) {
      say('ar-problem', err.message);
    }
    drawRules();
  };
  $('ar-quiet').addEventListener('change', () => setQuiet({ on: $('ar-quiet').checked }));
  $('ar-from').addEventListener('change', () => setQuiet({ from: $('ar-from').value }));
  $('ar-to').addEventListener('change', () => setQuiet({ to: $('ar-to').value }));
  $('ar-down').addEventListener('change', () => setQuiet({ downStillGoes: $('ar-down').checked }));
  const setGather = async (change) => {
    try {
      await R.send('/api/sensors/gather', { ...rules.gather, ...change });
      rules = await R.get('/api/alerts');
      say('ar-problem', '');
    } catch (err) {
      say('ar-problem', err.message);
    }
    drawRules();
  };
  $('ar-gather').addEventListener('change', () => setGather({ on: $('ar-gather').checked }));
  $('ar-secs').addEventListener('click', (e) => {
    const b = e.target.closest('[data-secs]');
    if (b) setGather({ secs: Number(b.dataset.secs) });
  });

  // Edit a rule: the rows its kind has, then Send to.
  let editingRule = null; // { key, x, vals }
  const row = (name, about, right) => `<div class="ds-row"><div class="ds-l"><b>${name}</b><span>${about}</span></div><div class="ds-r">${right}</div></div>`;
  const select = (k, items, on) => `<select data-rl="${k}">${items.map(([v, t]) => `<option value="${v}"${v === on ? ' selected' : ''}>${t}</option>`).join('')}</select>`;
  const AFTER = [[0, 'Right away'], [1, '1 minute'], [5, '5 minutes'], [10, '10 minutes'], [30, '30 minutes'], [60, 'An hour']];
  const AGAIN = [[0, 'Alert once'], [60, 'Again every hour'], [360, 'Again every 6 hours'], [1440, 'Again every day']];
  const switchRow = (k, name, about, on) => row(name, about, `<input type="checkbox" class="switch" data-rl="${k}" role="switch" aria-label="${name}"${on ? ' checked' : ''}>`);
  const drawRule = () => {
    const { x, vals: v } = editingRule;
    const rows = {
      cert: () => row('Warn this long before it expires', 'New certificate sensors start with this. Each can change it.', select('limit', [[14, '14 days'], [21, '21 days'], [30, '30 days'], [60, '60 days']], v.limit))
        + row('While it stays close', 'Reminders until it\'s renewed.', select('again', [[0, 'Alert once'], [1440, 'Again every day'], [10080, 'Again every week']], v.again)),
      space: () => row('Warn below', 'New drive sensors start with this. Each can change it.', select('limit', [[5, '5% free'], [10, '10% free'], [20, '20% free']], v.limit))
        + row('While it stays low', 'Reminders until there\'s room again.', select('again', AGAIN, v.again)),
      hw: () => row('While it lasts', 'Reminders until the part is fixed.', select('again', AGAIN, v.again)),
      seat: () => row('Alert when it has been quiet for', 'A seat\'s client checks in about once a minute.', select('limit', [[5, '5 minutes'], [15, '15 minutes'], [30, '30 minutes'], [60, 'An hour']], v.limit))
        + switchRow('back', 'When it checks in again', 'An all-clear, only after an alert went.', v.back),
      signin: () => row('Alert after', 'Failed sign-ins for one account within 10 minutes.', select('limit', [[3, '3 failed'], [5, '5 failed'], [10, '10 failed']], v.limit)),
      plain: () => '',
    }[x.kind]();
    $('rl-rows').innerHTML = rows + row('Send to', 'Picked by name from Notifications.', '<div class="np" id="rl-send"></div>');
    pick($('rl-send'), v.to);
  };
  const openRule = (key) => {
    const x = ruleOf(key);
    const s = rules.now.rules[key];
    editingRule = { key, x, vals: { ...s, to: [...s.to] } };
    $('rl-title').textContent = x.title;
    $('rl-lead').textContent = key.startsWith('sensor.') ? 'Warnings: the sensor is still answering, but something needs attention soon.' : 'When it alerts, and where.';
    say('rl-problem', '');
    drawRule();
    $('rule-dialog').showModal();
  };
  $('rl-rows').addEventListener('change', (e) => {
    const k = e.target.dataset.rl;
    if (!k) return;
    editingRule.vals[k] = k === 'back' ? e.target.checked : Number(e.target.value);
  });
  $('rl-cancel').addEventListener('click', () => $('rule-dialog').close());
  $('rl-save').addEventListener('click', async () => {
    const { key, vals: v } = editingRule;
    if (await saveRule(key, { on: v.on, after: v.after, again: v.again, back: v.back, limit: v.limit, to: v.to })) $('rule-dialog').close();
    else say('rl-problem', $('ar-problem').textContent);
  });
  $('page-settings').addEventListener('click', (e) => {
    const b = e.target.closest('[data-rule-edit]');
    if (b) openRule(b.dataset.ruleEdit);
    const t = e.target.closest('[data-type-edit]');
    if (t) openType(rules.now.types.find((x) => x.id === t.dataset.typeEdit));
    if (e.target.closest('[data-type-add]')) openType(null);
  });

  // An alert type: its name, how long down before the alert, reminders, the all-clear, where it goes, and whether new ones start with it.
  let editingType = null; // { id, vals, then }
  const drawType = () => {
    const v = editingType.vals;
    $('ty-rows').innerHTML = row('Alert when it has been down for', 'Counted from the first miss.', select('after', AFTER, v.after))
      + row('While it stays down', 'Reminders until it comes back.', select('again', AGAIN, v.again))
      + switchRow('back', 'When it comes back', 'An all-clear, only after a down alert went.', v.back)
      + row('Send to', 'Picked by name from Notifications.', '<div class="np" id="ty-send"></div>')
      + switchRow('start', 'New sensors and links start with it', 'Each one can pick another.', v.start);
    pick($('ty-send'), v.to);
  };
  const openType = async (t, then = null) => {
    if (!notes) await load();
    if (!rules) rules = await R.get('/api/alerts').catch(() => null);
    editingType = { id: t?.id || null, then, vals: { after: t?.after ?? 5, again: t?.again ?? 0, back: t?.back ?? true, to: [...(t?.to || [])], start: !!t && t.id === rules?.now.startType } };
    $('ty-title').textContent = t ? `Change ${t.name}` : 'New alert type';
    $('ty-name').value = t?.name || '';
    $('ty-delete').hidden = !t;
    $('ty-save').textContent = t ? 'Save' : 'Add the alert type';
    say('ty-problem', '');
    drawType();
    $('type-dialog').showModal();
    $('ty-name').focus();
  };
  $('ty-rows').addEventListener('change', (e) => {
    const k = e.target.dataset.rl;
    if (!k) return;
    editingType.vals[k] = k === 'back' || k === 'start' ? e.target.checked : Number(e.target.value);
  });
  $('ty-cancel').addEventListener('click', () => $('type-dialog').close());
  $('ty-save').addEventListener('click', async () => {
    const { id, vals: v, then } = editingType;
    $('ty-save').disabled = true;
    try {
      const r = await R.send(id ? `/api/alerts/types/${encodeURIComponent(id)}` : '/api/alerts/types', { name: $('ty-name').value, ...v });
      rules = r;
      $('type-dialog').close();
      drawRules();
      then?.(r.id);
    } catch (err) {
      say('ty-problem', err.message);
    }
    $('ty-save').disabled = false;
  });
  $('ty-delete').addEventListener('click', () => {
    const t = rules.now.types.find((x) => x.id === editingType.id);
    const start = rules.now.types.find((x) => x.id !== t.id && (x.id === rules.now.startType || t.id === rules.now.startType));
    const used = usedWords(rules.counts.types[t.id]);
    R.sure(`Delete ${t.name}?`, used ? `${used} use it. They move to ${start?.name || 'the alert type new ones start with'}.` : 'Nothing uses it.', 'Delete', async () => {
      try {
        rules = await R.send(`/api/alerts/types/${encodeURIComponent(t.id)}/delete`, {});
        $('type-dialog').close();
        drawRules();
      } catch (err) {
        say('ty-problem', err.message);
      }
    });
  });
  R.alertTypes = { open: (t, then) => openType(t, then) };

  // --- Settings, Alert history: every alert, where it went, and whether it got there --------------------------------------------

  let histDays = 7;
  let histFailed = false;
  let histTimer = null;
  const OUT = { sent: ['ok', 'Sent'], partial: ['held', 'Partly sent'], failed: ['bad', "Didn't go"], held: ['held', 'Held for quiet hours'] };
  const loadHistory = async () => {
    $('ah-range').querySelectorAll('button').forEach((b) => b.setAttribute('aria-pressed', b.dataset.days ? !histFailed && Number(b.dataset.days) === histDays : histFailed));
    try {
      await load();
      const list = await R.get(`/api/alerts/history?days=${histFailed ? 30 : histDays}${histFailed ? '&failed=true' : ''}&find=${encodeURIComponent($('ah-find').value.trim())}`);
      say('ah-problem', '');
      let last = '';
      $('ah-list').innerHTML = list.map((h) => {
        const d = new Date(h.at);
        const head = day(d) === last ? '' : `${last ? '</div>' : ''}<p class="hday">${esc((last = day(d)))}</p><div class="hrows">`;
        const x = ruleOf(h.rule);
        const [tone, word] = OUT[h.outcome] || ['ok', h.outcome];
        return `${head}<div class="hrow"><time>${esc(d.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' }))}</time><span class="ric ${x?.ic || 'down'}">${icon(x?.ic || 'down')}</span>`
          + `<span class="w"><b>${esc(h.title)}</b><span>${esc(`${x?.title || 'Alert'} · to ${h.to.join(', ') || 'nowhere'}`)}</span>${h.why ? `<small>${esc(h.why)}</small>` : ''}</span><span class="${tone}">${word}</span></div>`;
      }).join('') + (list.length ? '</div>' : `<p class="hint">${histFailed ? 'Every alert in the last 30 days got where it was going.' : 'No alerts in that time.'}</p>`);
    } catch (err) {
      say('ah-problem', err.message);
      R.skeleton($('ah-list'), '');
    }
  };
  $('ah-range').addEventListener('click', (e) => {
    const b = e.target.closest('button');
    if (!b) return;
    histFailed = !b.dataset.days;
    if (b.dataset.days) histDays = Number(b.dataset.days);
    loadHistory();
  });
  $('ah-find').addEventListener('input', () => {
    clearTimeout(histTimer);
    histTimer = setTimeout(loadHistory, 250);
  });

  // --- Showing the pages -----------------------------------------------------------------------------------------------------

  document.addEventListener('rabbidlab:show', async (e) => {
    if (e.detail[0] !== 'settings' || !can('sensors.alerts')) return;
    if (e.detail[1] === 'alerts') loadRules();
    else if (e.detail[1] === 'notifications') {
      await load();
      drawNotes();
    } else if (e.detail[1] === 'history') loadHistory();
  });
})();
