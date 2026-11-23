// Settings, Alerts: the alert rules (#settings/alerts), the notifications they go to (#settings/notifications) and every
// alert that went (#settings/history). Also R.notes, the Send to drop-down sensors and links pick notifications with, and
// the dialog that adds one. Alerts.cs and Channels.cs on the server.
(() => {
  const R = window.RabbidLab;
  const { $, esc, svg, say, has: can, panel } = R;

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
  let notesWrong = ''; // why the last read of them failed; what was read before stays
  const load = async () => {
    if (!can('sensors.alerts')) return null;
    try {
      notes = await R.get('/api/channels');
      notesWrong = '';
    } catch (err) {
      notesWrong = err.message;
    }
    return notes;
  };

  // Send to: the picked notifications as chips on a button, and a menu of them all by kind to tick, with a line to add one.
  // picked is changed in place; then() runs after every change. only: one kind, such as email, for what goes only that way.
  let pickers = 0;
  const pick = (host, picked, then = () => {}, only = '') => {
    const n = ++pickers;
    const draw = () => {
      const list = (notes?.channels || []).filter((x) => !only || x.kind === only);
      const on = list.filter((x) => picked.includes(x.id));
      host.innerHTML = `<button type="button" class="np-btn" popovertarget="np-menu-${n}" aria-haspopup="true">${on.length ? on.map(chip).join('') : '<span class="np-none">None selected</span>'}<svg class="np-chev" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M6 9l6 6 6-6"/></svg></button>`
        + `<div class="np-menu" id="np-menu-${n}" popover>`
        + KINDS.filter((k) => list.some((x) => x.kind === k)).map((k) => `<p class="np-h">${KIND[k]}</p>`
          + list.filter((x) => x.kind === k).map((x) => `<button type="button" class="np-opt${picked.includes(x.id) ? ' on' : ''}" data-np="${esc(x.id)}" aria-pressed="${picked.includes(x.id)}">`
            + `<span class="chan-ic ${x.kind}">${mark(x.kind)}</span><span class="np-t">${esc(x.name)}<small>${esc(how(x))}${x.ready ? '' : ' · not configured'}</small></span><i class="np-box">${svg('check')}</i></button>`).join('')).join('')
        + (list.length ? '' : `<p class="np-empty">No ${only ? `${KIND[only].toLowerCase()} ` : ''}notification channels yet.</p>`)
        + `<button type="button" class="np-add" data-np-add>${svg('plus')}Add a notification channel</button></div>`;
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

  const SUB = { email: 'Gmail or a mail server', discord: 'A channel webhook', teams: 'A Workflows webhook', slack: 'An incoming webhook', ntfy: 'Push notifications to your phone', webhook: 'JSON to any URL' };
  const HOOK = {
    discord: ['Webhook URL', 'https://discord.com/api/webhooks/…', 'In Discord, go to channel settings > Integrations > Webhooks > New Webhook, then select Copy Webhook URL.'],
    teams: ['Webhook URL', 'https://…', 'In Teams, open the channel menu > Workflows > Post to a channel when a webhook request is received. Copy the generated URL.'],
    slack: ['Webhook URL', 'https://hooks.slack.com/services/…', 'In Slack, add the Incoming Webhooks app to a channel and copy the Webhook URL.'],
    ntfy: ['Topic URL', 'https://ntfy.sh/lab-alerts', 'The ntfy server and topic. Subscribe to the same topic in the ntfy app on your phone. Anyone who knows the topic name can read it, so use a name that is hard to guess.'],
    webhook: ['URL', 'https://example.com/hooks/lab', 'Alerts are posted as JSON: event (down, up, warn), subject, facts, sentBy and at.'],
  };
  const PROV = {
    gmail: ['Gmail', 'With an app password', '<svg viewBox="0 0 24 24"><path fill="#4285F4" d="M3 7v11a1.5 1.5 0 0 0 1.5 1.5H7v-9z"/><path fill="#34A853" d="M17 10.5v9h2.5A1.5 1.5 0 0 0 21 18V7z"/><path fill="#EA4335" d="M3 7l9 6.5L21 7V6a1.5 1.5 0 0 0-2.4-1.2L12 9.6 5.4 4.8A1.5 1.5 0 0 0 3 6z"/></svg>'],
    m365: ['Microsoft 365', 'Uses Microsoft Graph', '<svg viewBox="0 0 24 24"><rect x="3" y="3" width="8.5" height="8.5" fill="#F25022"/><rect x="12.5" y="3" width="8.5" height="8.5" fill="#7FBA00"/><rect x="3" y="12.5" width="8.5" height="8.5" fill="#00A4EF"/><rect x="12.5" y="12.5" width="8.5" height="8.5" fill="#FFB900"/></svg>'],
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
      $('nf-url').placeholder = editingNote ? 'Leave blank to keep the saved URL' : ph;
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
    const stored = (has) => (has && editingNote?.mail?.provider === provider ? 'Stored encrypted. Enter a new value to change it.' : '');
    $('nf-gpass').placeholder = stored(m?.hasPassword);
    $('nf-pass').placeholder = stored(m?.hasPassword);
    $('nf-secret').placeholder = stored(m?.hasSecret);
    $('nf-to').innerHTML = mailTo.map((a, i) => `<span class="np-chip addr">${esc(a)}<button type="button" class="np-x" data-to-x="${i}" aria-label="Remove ${esc(a)}">${svg('x')}</button></span>`).join('')
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
    $('nf-title').textContent = n ? `Edit ${n.name}` : 'Add a notification channel';
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
    $('nf-save').textContent = n ? 'Save' : 'Add channel';
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
      say('nf-done', noteKind === 'email' ? `Test message sent to ${mailTo.join(', ')}. Check the inbox and the junk folder.` : 'Test message sent. Check the channel.');
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
      R.toast(editingNote ? `${$('nf-name').value.trim()} saved.` : `${$('nf-name').value.trim()} added.`, editingNote ? '' : 'Select it in an alert rule, or under Send to on a sensor or link.');
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
    R.sure(`Delete ${n.name}?`, used ? `Used by ${used}. Alerts stop going to this channel, and it is removed from all alert rules.` : 'This channel is not in use.', 'Delete', async () => {
      try {
        notes = await R.send(`/api/channels/${encodeURIComponent(n.id)}/delete`, {});
        $('notify-dialog').close();
        drawNotes();
        if (rules) reload();
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
    if (R.where()[0] !== 'settings') return;
    // A read that failed says why, and takes the placeholders away if nothing was read before.
    say('nt-problem', notesWrong);
    if (!notes) {
      if (notesWrong) R.skeleton($('nt-list'), '');
      return;
    }
    const q = $('nt-find').value.trim().toLowerCase();
    const list = notes.channels.filter((n) => !q || [n.name, KIND[n.kind], how(n), n.where].some((v) => String(v || '').toLowerCase().includes(q)));
    const card = (n) => {
      const used = usedWords(n);
      const last = n.lastTried ? (n.lastProblem ? ['bad', at(n.lastTried), 'Last attempt failed'] : ['ok', at(n.lastSent), 'Last sent']) : ['off', 'Never', 'Last sent'];
      return `<div class="nrow"><span class="chan-ic ${n.kind}">${mark(n.kind)}</span>`
        + `<div class="nnm"><b>${esc(n.name)}<em>${esc(how(n))}</em>${n.ready ? '' : '<em class="warn">Not configured</em>'}</b><span title="${esc(n.where)}">${esc(n.where)}</span></div>`
        + `<div class="nst"><b>${esc(used || 'Not in use')}</b>${!used ? '' : 'Using this channel'}</div>`
        + `<div class="nst"${n.lastProblem ? ` title="${esc(n.lastProblem)}"` : ''}><b><i class="${last[0]}"></i>${esc(last[1])}</b>${last[2]}</div>`
        + `<div class="nacts"><button type="button" class="ds-btn" data-nt-test="${esc(n.id)}">Test</button><button type="button" class="ds-btn" data-nt-edit="${esc(n.id)}">Edit</button></div></div>`;
    };
    $('nt-list').innerHTML = KINDS.filter((k) => list.some((n) => n.kind === k)).map((k) => {
      const mine = list.filter((n) => n.kind === k);
      return `<section class="ngroup"><h4>${KIND[k]} <small>${mine.length}</small></h4><div class="nrows">${mine.map(card).join('')}</div></section>`;
    }).join('') || (notes.channels.length ? '<p class="hint">No channels match the search.</p>'
      : `<div class="nt-empty"><span class="big-ic ember">${svg('bell')}</span><div><b>No notification channels yet</b><span>Add an email address, a Discord channel or ntfy push notifications, then select the channel in an alert rule.</span></div></div>`);
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
      R.toast(`Test message sent to ${n.name}.`, n.kind === 'email' ? `Sent to ${(n.mail?.to || []).join(', ')}. Check the junk folder too.` : 'Check the channel.');
    } catch (err) {
      R.toast(`Could not send a test message to ${n.name}.`, err.message, { tone: 'warn' });
    }
    await load();
    drawNotes();
  });

  // --- Settings, Alert rules: every alert in one list, each with its alert type, or Don't alert ---------------------------------
  // The rules here (Alerts.cs) and the alerts on the Alerts page (EventAlerts.cs) side by side: picking an alert type turns an
  // alert on and sends it there; Don't alert turns it off. A thing that can happen with no alert yet gets one when it's given a type.

  let rules = null; // /api/alerts: { now: { rules, types, startType, quiet }, counts, gather }
  let events = null; // /api/event-alerts: { events, rules, ... }
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
    { key: 'sensor.down', area: 'Sensors and links', ic: 'down', title: 'Sensor down', kind: 'times', sev: 'critical', each: 'sensor' },
    { key: 'link.down', area: 'Sensors and links', ic: 'link', title: 'Watched link down', kind: 'times', sev: 'warning', each: 'link' },
    { key: 'sensor.cert', area: 'Sensors and links', ic: 'cert', title: 'Certificate expiring soon', kind: 'cert', sev: 'warning' },
    { key: 'sensor.space', area: 'Sensors and links', ic: 'space', title: 'Low free space on a pool or drive', kind: 'space', sev: 'warning' },
    { key: 'sensor.hardware', area: 'Sensors and links', ic: 'hw', title: 'Hardware fault (fan, power supply or disk)', kind: 'hw', sev: 'critical' },
    { key: 'seat.offline', area: 'Devices', ic: 'seat', title: 'Computer stopped checking in', kind: 'seat', sev: 'warning' },
    { key: 'update.ready', area: 'Rabbid Lab', ic: 'upd', title: 'Update available', kind: 'plain', when: 'Once for each new version', sev: 'info' },
    { key: 'update.failed', area: 'Rabbid Lab', ic: 'upd', title: 'Update failed', kind: 'plain', when: 'Immediately', sev: 'critical' },
    { key: 'signin.failing', area: 'Rabbid Lab', ic: 'lock', title: 'Repeated failed sign-ins', kind: 'signin', sev: 'warning' },
  ];
  const ruleOf = (key) => RULES.find((x) => x.key === key);
  // Things the Alerts page lists that a rule above already says, so each is listed once.
  const SAID = new Set(['dev.offline', 'rl.update', 'signin.fail', 'lab.down', 'lab.cert']);
  const AREAS = ['Domain sign-ins', 'Active Directory', 'Devices', 'Network', 'Sensors and links', 'Printers', 'Connected apps', 'Rabbid Lab'];
  const AREA_ICON = { 'Domain sign-ins': 'lock', 'Active Directory': 'users', Devices: 'monitor', Network: 'radar', 'Sensors and links': 'activity', Printers: 'printer', 'Connected apps': 'plug', 'Rabbid Lab': 'shield' };
  const SEV = { critical: 'Critical', warning: 'Warning', info: 'Info' };
  const TONE = { critical: 'red', warning: 'amber', info: 'grey' };
  const STATE = { on: 'Alerts', off: 'Logged only', none: 'Not detected' };
  const FIELD = { account: 'account', address: 'address', computer: 'computer', reason: 'reason', group: 'group', object: 'object', kind: 'kind of object', by: 'changed by', hour: 'hour', day: 'day' };
  const OPS = { is: 'is', isnot: 'is not', oneof: 'is one of', noneof: 'is none of', contains: 'contains', starts: 'starts with', between: 'is between' };
  const PER = { account: 'account', address: 'address', computer: 'computer', group: 'group' };
  const DISTINCT = { '': 'times', account: 'different accounts', address: 'different addresses', computer: 'different computers' };
  const mins = (m) => (m === 60 ? '1 hour' : m === 1 ? '1 minute' : `${m} minutes`);
  const EVERY = { 60: 'every hour', 360: 'every 6 hours', 1440: 'every day', 10080: 'every week' };
  // A rule or an alert type in a line: After 5 minutes down, once, and an all-clear when it comes back.
  const when = (x, r) => {
    const again = r.again ? `repeated ${EVERY[r.again]}` : 'once';
    switch (x.kind) {
      case 'times': return `${r.after ? `After ${mins(r.after)} down` : 'Immediately'}, ${again}${r.back ? ', with a recovery alert when back up' : ''}`;
      case 'cert': return `${r.limit} days before expiry${r.again ? `, then ${r.again === 10080 ? 'once a week' : 'once a day'}` : ''}`;
      case 'space': return `Below ${r.limit}% free${r.again ? `, repeated ${EVERY[r.again]} while low` : ''}`;
      case 'hw': return `Immediately${r.again ? `, repeated ${EVERY[r.again]} while the fault persists` : ''}`;
      case 'seat': return `No client check-in for ${r.limit} minutes${r.back ? ', with a recovery alert when it checks in again' : ''}`;
      case 'signin': return `${r.limit} failed sign-ins for one account within 10 minutes`;
      default: return x.when;
    }
  };
  const evOf = (id) => events?.events.find((e) => e.id === id);
  const evName = (r) => r.name || evOf(r.event)?.name || r.event;
  // An alert from the Alerts page in a line: what happens, how often and its conditions.
  const evWhen = (r) => {
    const bits = [r.times > 1 ? `${r.times} ${DISTINCT[r.distinct || '']} within ${r.minutes} minutes${PER[r.per] ? `, for each ${PER[r.per]}` : ''}` : '',
      ...r.conditions.map((c) => `${FIELD[c.field] || c.field} ${OPS[c.op] || c.op} ${c.value}`)].filter(Boolean);
    return `${evOf(r.event)?.name || r.event}${bits.length ? ` (${bits.join('; ')})` : ''}`;
  };
  // One that alerts each time with the usual counting, so the thing itself needn't be listed again.
  const plainFor = (r, e) => !r.about && !r.conditions.length && r.times === e.times;
  const typeOf = (id) => rules?.now.types.find((t) => t.id === id);

  // Every alert as a row: src rule (Alert rules), event (an alert on the Alerts page), new (no alert yet) or none (can't be detected).
  // type: an alert type's id, none (Don't alert), own (its own notifications, from before), each (each sensor's), null (not available).
  const items = () => {
    if (!rules) return [];
    const out = [];
    RULES.forEach((x) => {
      if (x.each) {
        out.push({ id: `r:${x.key}`, src: 'rule', key: x.key, area: x.area, ic: x.ic, name: x.title, sev: x.sev, type: 'each', state: 'on',
          trigger: x.each === 'sensor' ? 'Set by each sensor’s alert type: how long it is down first, reminders and the recovery alert.' : 'Set by each watched link’s alert type.' });
        return;
      }
      const s = rules.now.rules[x.key];
      if (!s) return;
      const type = !s.on ? 'none' : s.type && typeOf(s.type) ? s.type : s.to.length ? 'own' : 'none';
      out.push({ id: `r:${x.key}`, src: 'rule', key: x.key, area: x.area, ic: x.ic, name: x.title, sev: x.sev, type, state: type === 'none' ? 'off' : 'on', trigger: when(x, s), to: s.to });
    });
    if (!events) return out;
    const mine = events.rules.filter((r) => !r.about);
    mine.forEach((r) => {
      const type = r.on && r.types.some(typeOf) ? r.types.find(typeOf) : 'none';
      out.push({ id: `e:${r.id}`, src: 'event', rule: r.id, area: evOf(r.event)?.area || 'Rabbid Lab', name: evName(r), sev: r.severity, type, state: type === 'none' ? 'off' : 'on', trigger: evWhen(r), note: evOf(r.event)?.note });
    });
    events.events.forEach((e) => {
      if (SAID.has(e.id) || mine.some((r) => r.event === e.id && plainFor(r, e))) return;
      out.push(e.notYet ? { id: `x:${e.id}`, src: 'none', area: e.area, name: e.name, sev: e.severity, type: null, state: 'none', trigger: e.name, why: e.notYet, note: e.note }
        : { id: `n:${e.id}`, src: 'new', event: e.id, area: e.area, name: e.name, sev: e.severity, type: 'none', state: 'off', trigger: `${e.name}, each time`, note: e.note });
    });
    return out;
  };
  const itemOf = (id) => items().find((it) => it.id === id);
  const toIds = (it) => (it.type === 'own' ? it.to : typeOf(it.type)?.to || []);
  const sends = (it) => (it.type === null || it.type === 'each' ? '' : it.type === 'none' ? 'Not sent' : words(toIds(it)) || 'No notifications');
  const typeWord = (it) => (it.type === null ? 'Not available' : it.type === 'each' ? (it.key === 'link.down' ? 'Each link’s' : 'Each sensor’s') : it.type === 'none' ? 'Don’t alert'
    : it.type === 'own' ? 'Its own notifications' : typeOf(it.type)?.name || '');
  const pic = (it, cls = 'ar-ric') => (it.ic ? `<span class="ric ${it.ic} ${cls}">${icon(it.ic)}</span>` : `<span class="ric ev ${cls}">${svg(AREA_ICON[it.area] || 'bell')}</span>`);
  const sevBadge = (s) => `<span class="ubadge ${TONE[s] || 'grey'}">${SEV[s] || s}</span>`;
  const toChips = (ids) => {
    const to = ids.map(named).filter(Boolean);
    return to.length ? to.map(chip).join('') : '<span class="np-chip none">No notifications</span>';
  };
  // The alert type list: Don't alert, every alert type, and New alert type…; a rule set per sensor or not detected says so instead.
  const typeSel = (it) => {
    if (it.type === null) return `<span class="muted" data-tip="${esc(it.why || 'Not available yet.')}">Not available</span>`;
    if (it.type === 'each') return `<span class="muted">${typeWord(it)}</span>`;
    return `<select class="ar-sel" data-ar-type="${esc(it.id)}" aria-label="${esc(`Alert type for ${it.name}`)}"><option value="none"${it.type === 'none' ? ' selected' : ''}>Don’t alert</option>`
      + (it.type === 'own' ? '<option value="own" selected disabled>Its own notifications</option>' : '')
      + rules.now.types.map((t) => `<option value="${esc(t.id)}"${it.type === t.id ? ' selected' : ''}>${esc(t.name)}</option>`).join('')
      + '<option value="new">New alert type…</option></select>';
  };

  // The list: find, Show, Actions with the ticks, grouped by area to start with.
  const picked = new Set();
  let show = 'all';
  const shown = () => {
    const q = $('ar-find').value.trim().toLowerCase();
    return items().filter((it) => (show === 'all' || it.state === show) && (!q || `${it.name} ${it.trigger} ${it.area} ${typeWord(it)}`.toLowerCase().includes(q)));
  };
  const grid = R.grid({
    el: $('ar-table'), key: 'alertlist', sort: { key: 'name', dir: 1 }, group: { on: true, keys: ['area'] },
    lead: { w: 18, head: '<label class="tk"><input type="checkbox" data-ar-tick-all aria-label="Select all"></label>',
      cell: (it) => `<label class="tk"><input type="checkbox" data-ar-tick="${esc(it.id)}" aria-label="${esc(`Select ${it.name}`)}"${picked.has(it.id) ? ' checked' : ''}></label>` },
    cols: [
      { key: 'name', label: 'Alert', w: 300, fixed: true, get: (it) => it.name, cell: (it) => `<div class="who">${pic(it)}<b class="ell">${esc(it.name)}</b></div>` },
      { key: 'area', label: 'Area', w: 150, on: true, get: (it) => AREAS.indexOf(it.area), text: (it) => it.area, cell: (it) => `<span class="ell">${esc(it.area)}</span>` },
      { key: 'sev', label: 'Severity', w: 100, on: true, get: (it) => Object.keys(SEV).indexOf(it.sev), text: (it) => SEV[it.sev], cell: (it) => sevBadge(it.sev) },
      { key: 'type', label: 'Alert type', w: 190, on: true, get: typeWord, cell: typeSel },
      { key: 'state', label: 'Today', w: 130, on: true, get: (it) => Object.keys(STATE).indexOf(it.state), text: (it) => STATE[it.state],
        cell: (it) => `<span class="ar-st"><i class="ar-dot ${it.state}"></i>${STATE[it.state]}</span>` },
      { key: 'to', label: 'Sends to', w: 200, on: true, get: sends, cell: (it) => `<span class="ell${it.type === 'none' || !sends(it) ? ' muted' : ''}">${esc(sends(it) || '-')}</span>` },
      { key: 'when', label: 'Trigger', w: 360, get: (it) => it.trigger, cell: (it) => `<span class="ell">${esc(it.trigger)}</span>` },
    ],
    row: (it) => ` data-ar-row="${esc(it.id)}"`, rowClass: (it) => `${it.state === 'on' ? '' : 'off'}${picked.has(it.id) ? ' picked' : ''}`,
    empty: () => 'No alerts match.', loading: () => !rules, redraw: () => drawList(), picked: () => picked.size,
  });
  const drawTicks = () => {
    $('ar-table').querySelectorAll('[data-ar-tick]').forEach((b) => { b.checked = picked.has(b.dataset.arTick); b.closest('.tr')?.classList.toggle('picked', b.checked); });
    const n = picked.size;
    const act = $('ar-act');
    act.disabled = !n;
    act.querySelector('.cnt').hidden = !n;
    act.querySelector('.cnt').textContent = n;
    if (n) delete act.dataset.tip;
    else act.dataset.tip = 'Select alerts first';
  };
  const drawList = () => {
    if (!rules) return;
    const list = shown();
    [...picked].forEach((id) => { if (!list.some((it) => it.id === id)) picked.delete(id); });
    grid.draw(list);
    grid.tools($('ar-tools'), $('ar-rows'));
    drawTicks();
  };
  // The alert types on top: each with how many alerts use it and where it sends, then Don't alert.
  const drawTypes = (all) => {
    const types = rules.now.types;
    $('ar-types-n').textContent = types.length;
    const n = (id) => all.filter((it) => it.type === id).length;
    $('ar-types').innerHTML = types.map((t) => `<button type="button" class="ar-type${panel.shows('artype', t.id) ? ' on' : ''}" data-ar-tchip="${esc(t.id)}"><b>${esc(t.name)}</b><small>${n(t.id)}</small>${toChips(t.to)}</button>`).join('')
      + `<button type="button" class="ar-type${panel.shows('artype', 'none') ? ' on' : ''}" data-ar-tchip="none"><b>Don’t alert</b><small>${n('none')}</small><span class="np-chip none">Not sent</span></button>`;
  };
  const drawShow = (all) => {
    const n = { all: all.length, on: 0, off: 0, none: 0 };
    all.forEach((it) => { n[it.state]++; });
    $('ar-show').innerHTML = [['all', 'All'], ['on', STATE.on], ['off', STATE.off], ['none', STATE.none]]
      .map(([k, w]) => `<button type="button" data-ar-show="${k}" aria-pressed="${show === k}">${w}<small>${n[k]}</small></button>`).join('');
  };
  const drawRules = () => {
    if (!rules) return;
    const all = items();
    $('ar-all-n').textContent = all.length;
    $('ar-lead').textContent = `All ${all.length} alerts in one list. Select an alert type for each alert, or Don’t alert.`;
    drawTypes(all);
    drawShow(all);
    drawList();
    const q = rules.now.quiet;
    $('ar-gather').checked = rules.gather.on;
    $('ar-secs').querySelectorAll('button').forEach((b) => b.setAttribute('aria-pressed', Number(b.dataset.secs) === rules.gather.secs));
    $('ar-quiet').checked = q.on;
    $('ar-from').value = q.from;
    $('ar-to').value = q.to;
    $('ar-down').checked = q.downStillGoes;
    $('ar-quiet-now').hidden = !q.now;
    if (panel.is('aralert')) { const it = itemOf(panel.key); if (it) openItem(it, true); else panel.close(); }
    if (panel.is('artype')) { if (panel.key === 'none' || typeOf(panel.key)) openTypePanel(panel.key, true); else panel.close(); }
  };
  const reload = async () => {
    try {
      await load();
      [rules, events] = await Promise.all([R.get('/api/alerts'), R.get('/api/event-alerts').catch(() => null)]);
      say('ar-problem', '');
    } catch (err) {
      say('ar-problem', err.message);
      if (!rules) R.skeleton($('ar-types'), '');
    }
    drawRules();
  };
  // Gives alerts an alert type (turning them on), or Don't alert (turning them off). A thing with no alert yet gets one.
  const assign = async (list, type) => {
    const on = type !== 'none';
    const can2 = list.filter((it) => it.type !== null && it.type !== 'each' && (on || it.src !== 'new'));
    try {
      for (const it of can2) {
        if (it.src === 'rule') await R.send(`/api/alerts/rules/${it.key}`, on ? { on: true, type } : { on: false });
        else if (it.src === 'event') await R.send(`/api/event-alerts/rules/${encodeURIComponent(it.rule)}`, on ? { on: true, types: [type] } : { on: false });
        else if (it.src === 'new') await R.send('/api/event-alerts/rules', { event: it.event, types: [type], from: 'list' });
      }
      if (can2.length) R.toast('Saved.', can2.length === 1 ? `${can2[0].name}: ${on ? typeOf(type)?.name : 'Don’t alert'}.` : `${can2.length} alerts set to ${on ? typeOf(type)?.name : 'Don’t alert'}.`);
    } catch (err) {
      R.toast('The operation failed.', err.message, { tone: 'warn' });
    }
    await reload();
  };
  // A pick in an alert type list, in the grid or the side panel.
  const picks = (sel) => {
    const it = itemOf(sel.dataset.arType);
    if (!it) return;
    if (sel.value === 'new') {
      sel.value = it.type;
      openType(null, (id) => assign([it], id));
      return;
    }
    if (sel.value !== it.type) assign([it], sel.value);
  };

  // The side panel: an alert, with its trigger, its alert type and a preview of it arriving; or an alert type with what uses it.
  const sec = (title, body) => `<section class="dw-sec"><div class="dw-sec-head"><h3>${title}</h3></div>${body}</section>`;
  const preview = (it) => `<div class="ar-msg"><span class="ar-av" aria-hidden="true">RL</span><div class="ar-mbody">`
    + `<p class="ar-mwho"><b>${esc(R.app())}</b><i>App</i><time>${esc(new Date().toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' }))}</time></p>`
    + `<div class="ar-mcard ${esc(it.sev)}"><p class="ar-mtag">${SEV[it.sev] || ''}</p><p class="ar-mtitle">${esc(it.name)}</p><p class="ar-mline">${esc(it.trigger)}</p><p class="ar-mfoot">${esc(R.app())}</p></div></div></div>`;
  const editable = (it) => (it.src === 'rule' && !ruleOf(it.key)?.each && ruleOf(it.key)?.kind !== 'plain') || it.src === 'event';
  const openItem = (it, again = false) => {
    const counts = it.src === 'rule' ? rules.counts[it.key] : null;
    const bar = [editable(it) ? R.editButton('data-ar-edit', '') : '',
      ruleOf(it.key)?.each ? `<button type="button" class="dw-qa" data-ar-goto>${svg(it.key === 'link.down' ? 'link' : 'activity')}<span>${it.key === 'link.down' ? 'Open Links' : 'Open Sensors'}</span></button>` : '',
      it.src === 'event' && it.state === 'on' ? `<button type="button" class="dw-qa" data-ar-test>${svg('bell')}<span>Send test</span></button>` : '',
      it.state === 'on' && it.type !== 'each' ? `<button type="button" class="dw-qa" data-ar-mute>${svg('x')}<span>Don’t alert</span></button>` : ''].join('');
    panel.body.innerHTML = `<div class="dw-head">${pic(it, 'dw-pic')}<div class="dw-name"><h2 id="dw-title">${esc(it.name)}</h2><p>${esc(it.area)}</p></div>`
      + `<button type="button" class="icon dw-close" data-dw-close aria-label="Close">${svg('x')}</button></div>`
      + (bar ? `<div class="dw-bar">${bar}</div>` : '')
      + `<div class="dw-strip"><div><span class="l">Severity</span><span class="v">${sevBadge(it.sev)}</span></div><div><span class="l">Alert type</span><span class="v">${esc(typeWord(it))}</span></div>`
      + `<div><span class="l">Today</span><span class="v">${STATE[it.state]}</span></div>${counts ? `<div><span class="l">Sensors</span><span class="v">${counts.count}</span></div>` : ''}</div>`
      + sec('Trigger', `<p class="ar-p">${esc(it.trigger)}</p>${it.why ? `<p class="hint ar-note">${esc(it.why)}</p>` : it.note ? `<p class="hint ar-note">${esc(it.note)}</p>` : ''}`)
      + (it.type === null || it.type === 'each' ? '' : sec('Alert type', `<div class="ar-prow">${typeSel(it)}<span class="ar-to">${it.type === 'none' ? '<span class="np-chip none">Not sent</span>' : toChips(toIds(it))}</span></div>`))
      + sec('Preview', `${preview(it)}<p class="hint ar-note">An example of how it arrives in Discord, Teams, Slack or ntfy.</p>`);
    if (!again) panel.open('aralert', it.id);
    drawTypes(items());
  };
  const openTypePanel = (id, again = false) => {
    const t = typeOf(id);
    const mine = items().filter((it) => it.type === id);
    const used = t ? usedWords(rules.counts.types[t.id]) : '';
    panel.body.innerHTML = `<div class="dw-head"><span class="ric dw-pic">${svg(t ? 'bell' : 'x')}</span><div class="dw-name"><h2 id="dw-title">${esc(t ? t.name : 'Don’t alert')}</h2>`
      + `<p>${esc(t ? `Sends to ${words(t.to) || 'no notifications'}` : 'These alerts are not sent.')}</p></div><button type="button" class="icon dw-close" data-dw-close aria-label="Close">${svg('x')}</button></div>`
      + (t ? `<div class="dw-bar">${R.editButton('data-ar-tedit', '')}</div>` : '')
      + `<div class="dw-strip"><div><span class="l">Alerts</span><span class="v">${mine.length}</span></div>`
      + (t ? `<div><span class="l">Sensors and links</span><span class="v">${esc(used || 'None')}</span></div><div><span class="l">New sensors</span><span class="v">${t.id === rules.now.startType ? 'Start with it' : 'No'}</span></div>` : '') + '</div>'
      + (t ? sec('Where it sends', `<div class="ar-to">${toChips(t.to)}</div>`) : '')
      + sec(`Alerts <small>${mine.length}</small>`, mine.length ? `<div class="dw-list">${mine.map((it) => `<button type="button" class="mrow ar-pitem" data-ar-item="${esc(it.id)}">${pic(it)}<b class="ell">${esc(it.name)}</b>${sevBadge(it.sev)}</button>`).join('')}</div>`
        : '<p class="muted dw-empty">None.</p>');
    if (!again) panel.open('artype', id);
    drawTypes(items());
  };
  panel.el.addEventListener('click', async (e) => {
    if (!panel.is('aralert') && !panel.is('artype')) return;
    if (e.target.closest('[data-dw-close]')) { setTimeout(() => drawTypes(items()), 0); return; }
    let b;
    if ((b = e.target.closest('[data-ar-item]'))) { openItem(itemOf(b.dataset.arItem)); return; }
    if (e.target.closest('[data-ar-tedit]')) { openType(typeOf(panel.key)); return; }
    const it = panel.is('aralert') ? itemOf(panel.key) : null;
    if (!it) return;
    if (e.target.closest('[data-ar-edit]')) {
      if (it.src === 'rule') openRule(it.key);
      else R.alerts?.edit(it.rule);
    } else if (e.target.closest('[data-ar-goto]')) location.hash = it.key === 'link.down' ? '#links' : '#sensors';
    else if (e.target.closest('[data-ar-mute]')) assign([it], 'none');
    else if (e.target.closest('[data-ar-test]')) {
      try {
        await R.send(`/api/event-alerts/rules/${encodeURIComponent(it.rule)}/test`);
        R.toast('Test sent.', `${it.name} to ${sends(it)}.`);
      } catch (err) {
        R.toast('The test was not sent.', err.message, { tone: 'warn' });
      }
    }
  });
  panel.el.addEventListener('change', (e) => { if ((panel.is('aralert')) && e.target.matches('[data-ar-type]')) picks(e.target); });

  $('ar-find').addEventListener('input', () => drawList());
  $('ar-show').addEventListener('click', (e) => {
    const b = e.target.closest('[data-ar-show]');
    if (!b) return;
    show = b.dataset.arShow;
    drawShow(items());
    drawList();
  });
  $('ar-types').addEventListener('click', (e) => {
    const b = e.target.closest('[data-ar-tchip]');
    if (!b) return;
    if (panel.shows('artype', b.dataset.arTchip)) { panel.close(); drawTypes(items()); } else openTypePanel(b.dataset.arTchip);
  });
  $('ar-table').addEventListener('click', (e) => {
    const tk = e.target.closest('.tk');
    if (tk) {
      const box = tk.querySelector('input');
      if (e.target !== box) return;
      if (box.dataset.arTickAll !== undefined) shown().forEach((it) => (box.checked ? picked.add(it.id) : picked.delete(it.id)));
      else if (box.checked) picked.add(box.dataset.arTick);
      else picked.delete(box.dataset.arTick);
      drawTicks();
      return;
    }
    if (e.target.closest('input, select, button, a')) return;
    const row = e.target.closest('[data-ar-row]');
    if (!row) return;
    if (panel.shows('aralert', row.dataset.arRow)) { panel.close(); drawTypes(items()); } else openItem(itemOf(row.dataset.arRow));
  });
  $('ar-table').addEventListener('change', (e) => { if (e.target.matches('[data-ar-type]')) picks(e.target); });
  $('ar-act').addEventListener('click', () => {
    const list = items().filter((it) => picked.has(it.id));
    R.actions.open($('ar-act'), {
      items: [...rules.now.types.map((t) => ({ act: t.id, label: `Send with ${t.name}` })), { act: 'none', label: 'Don’t alert' }, '-', { act: 'clear', label: 'Clear selection' }],
      pick: async (act) => {
        if (act === 'clear') { picked.clear(); drawTicks(); return; }
        picked.clear();
        await assign(list, act);
      },
    });
  });
  // The Alerts page changed an alert (its Edit window was opened from here): read again.
  document.addEventListener('rabbidlab:alerts', () => { if (R.where()[0] === 'settings' && R.where()[1] === 'alerts' && can('sensors.alerts')) reload(); });
  // While it's open: every 15 seconds, but not while an alert type list is in use.
  R.live({
    page: () => R.where()[0] === 'settings' && R.where()[1] === 'alerts' && can('sensors.alerts'),
    hold: () => !!document.activeElement?.matches?.('[data-ar-type]'),
    every: 15000,
    read: () => Promise.all([R.get('/api/alerts'), R.get('/api/event-alerts').catch(() => null)]),
    now: () => (rules ? [rules, events] : null),
    draw: ([a, b]) => {
      rules = a;
      events = b;
      drawRules();
    },
    areas: () => [$('ar-table')],
  });
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

  // Edit a rule: the times or numbers its kind has. Where it goes is its alert type, picked in the list.
  let editingRule = null; // { key, x, vals }
  const row = (name, about, right) => `<div class="ds-row"><div class="ds-l"><b>${name}</b><span>${about}</span></div><div class="ds-r">${right}</div></div>`;
  const select = (k, items2, on) => `<select data-rl="${k}">${items2.map(([v, t]) => `<option value="${v}"${v === on ? ' selected' : ''}>${t}</option>`).join('')}</select>`;
  const AFTER = [[0, 'Immediately'], [1, '1 minute'], [5, '5 minutes'], [10, '10 minutes'], [30, '30 minutes'], [60, '1 hour']];
  const AGAIN = [[0, 'Alert once'], [60, 'Repeat every hour'], [360, 'Repeat every 6 hours'], [1440, 'Repeat every day']];
  const switchRow = (k, name, about, on) => row(name, about, `<input type="checkbox" class="switch" data-rl="${k}" role="switch" aria-label="${name}"${on ? ' checked' : ''}>`);
  const drawRule = () => {
    const { x, vals: v } = editingRule;
    $('rl-rows').innerHTML = {
      cert: () => row('Warn before expiry', 'Default for new certificate sensors. Each sensor can override it.', select('limit', [[14, '14 days'], [21, '21 days'], [30, '30 days'], [60, '60 days']], v.limit))
        + row('Reminders', 'Repeat until the certificate is renewed.', select('again', [[0, 'Alert once'], [1440, 'Repeat every day'], [10080, 'Repeat every week']], v.again)),
      space: () => row('Warn below', 'Default for new drive sensors. Each sensor can override it.', select('limit', [[5, '5% free'], [10, '10% free'], [20, '20% free']], v.limit))
        + row('Reminders', 'Repeat until free space recovers.', select('again', AGAIN, v.again)),
      hw: () => row('Reminders', 'Repeat until the fault is cleared.', select('again', AGAIN, v.again)),
      seat: () => row('Alert after no check-in for', 'The client checks in about once a minute.', select('limit', [[5, '5 minutes'], [15, '15 minutes'], [30, '30 minutes'], [60, '1 hour']], v.limit))
        + switchRow('back', 'Recovery alert', 'Sent when the computer checks in again, only after an alert was sent.', v.back),
      signin: () => row('Alert after', 'Failed sign-ins for one account within 10 minutes.', select('limit', [[3, '3 failed'], [5, '5 failed'], [10, '10 failed']], v.limit)),
      plain: () => '',
    }[x.kind]();
  };
  const openRule = (key) => {
    const x = ruleOf(key);
    const s = rules.now.rules[key];
    editingRule = { key, x, vals: { ...s } };
    $('rl-title').textContent = x.title;
    $('rl-lead').textContent = key.startsWith('sensor.') ? 'Warnings: the sensor is up, but something needs attention soon. Where it goes is its alert type.' : 'When this alert is sent. Where it goes is its alert type.';
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
    if (await saveRule(key, { after: v.after, again: v.again, back: v.back, limit: v.limit })) $('rule-dialog').close();
    else say('rl-problem', $('ar-problem').textContent);
  });
  $('page-settings').addEventListener('click', (e) => {
    if (e.target.closest('[data-type-add]')) openType(null);
  });

  // An alert type: its name, how long down before the alert, reminders, the all-clear, where it goes, and whether new ones start with it.
  let editingType = null; // { id, vals, then }
  const drawType = () => {
    const v = editingType.vals;
    $('ty-rows').innerHTML = row('Alert after down for', 'Counted from the first failed check.', select('after', AFTER, v.after))
      + row('Reminders', 'Repeat while the sensor is down.', select('again', AGAIN, v.again))
      + switchRow('back', 'Recovery alert', 'Sent when back up, only after a down alert was sent.', v.back)
      + row('Send to', 'Notification channels to send this alert to.', '<div class="np" id="ty-send"></div>')
      + switchRow('start', 'Default for new sensors and links', 'Each sensor or link can use a different alert type.', v.start);
    pick($('ty-send'), v.to);
  };
  const openType = async (t, then = null) => {
    if (!notes) await load();
    if (!rules) rules = await R.get('/api/alerts').catch(() => null);
    editingType = { id: t?.id || null, then, vals: { after: t?.after ?? 5, again: t?.again ?? 0, back: t?.back ?? true, to: [...(t?.to || [])], start: !!t && t.id === rules?.now.startType } };
    $('ty-title').textContent = t ? `Edit ${t.name}` : 'New alert type';
    $('ty-name').value = t?.name || '';
    $('ty-delete').hidden = !t;
    $('ty-save').textContent = t ? 'Save' : 'Add alert type';
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
    R.sure(`Delete ${t.name}?`, used ? `Used by ${used}. These move to ${start?.name || 'the default alert type'}.` : 'This alert type is not in use.', 'Delete', async () => {
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
  const OUT = { sent: ['ok', 'Sent'], partial: ['held', 'Partially sent'], failed: ['bad', 'Failed'], held: ['held', 'Held (quiet hours)'] };
  const histUrl = () => `/api/alerts/history?days=${histFailed ? 30 : histDays}${histFailed ? '&failed=true' : ''}&find=${encodeURIComponent($('ah-find').value.trim())}`;
  let hist = null; // { url, list } as drawn
  const drawHistory = (got) => {
    hist = got;
    const { list } = got;
    let last = '';
    $('ah-list').innerHTML = list.map((h) => {
      const d = new Date(h.at);
      const head = day(d) === last ? '' : `${last ? '</div>' : ''}<p class="hday">${esc((last = day(d)))}</p><div class="hrows">`;
      const x = ruleOf(h.rule);
      const [tone, word] = OUT[h.outcome] || ['ok', h.outcome];
      return `${head}<div class="hrow"><time>${esc(d.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' }))}</time><span class="ric ${x?.ic || 'down'}">${icon(x?.ic || 'down')}</span>`
        + `<span class="w"><b>${esc(h.title)}</b><span>${esc(`${x?.title || 'Alert'} · to ${h.to.join(', ') || 'no channels'}`)}</span>${h.why ? `<small>${esc(h.why)}</small>` : ''}</span><span class="${tone}">${word}</span></div>`;
    }).join('') + (list.length ? '</div>' : `<p class="hint">${histFailed ? 'No failed alerts in the last 30 days.' : 'No alerts in this period.'}</p>`);
  };
  const loadHistory = async () => {
    $('ah-range').querySelectorAll('button').forEach((b) => b.setAttribute('aria-pressed', b.dataset.days ? !histFailed && Number(b.dataset.days) === histDays : histFailed));
    try {
      await load();
      const url = histUrl();
      drawHistory({ url, list: await R.get(url) });
      say('ah-problem', '');
    } catch (err) {
      hist = null; // the next read that works draws
      say('ah-problem', err.message);
      R.skeleton($('ah-list'), '');
    }
  };
  // While it's open: every 10 seconds, for the range and words picked.
  R.live({
    page: () => R.where()[0] === 'settings' && R.where()[1] === 'history' && can('sensors.alerts'),
    every: 10000,
    read: async () => {
      const url = histUrl();
      return { url, list: await R.get(url) };
    },
    now: () => hist,
    draw: (got) => {
      if (got.url !== histUrl()) return; // the range or the words changed meanwhile
      drawHistory(got);
      say('ah-problem', '');
    },
  });
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
    if (e.detail[1] === 'alerts') reload();
    else if (e.detail[1] === 'notifications') {
      await load();
      drawNotes();
    } else if (e.detail[1] === 'history') loadHistory();
  });
})();
