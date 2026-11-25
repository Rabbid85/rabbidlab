// Settings, Help requests (HelpRequests.cs): Ask IT for help in the client on computers. Whether it shows, how to reach IT,
// the notifications a request goes to (chips, and Add for more), Alert history too, the subject, what's sent, a preview of
// the email IT gets, and a test. Each change is saved as it's made.
(() => {
  const R = window.RabbidLab;
  const { $, esc, svg, has: can } = R;
  let s = null; // the settings as the server last said them

  const KIND_ICON = { email: 'message', teams: 'users', slack: 'message', discord: 'message', ntfy: 'bell', webhook: 'link' };
  const note = (id) => s?.notifications.find((n) => n.id === id);
  const problem = (text) => { $('hr-problem').textContent = text || ''; $('hr-problem').hidden = !text; };

  const drawChips = () => {
    $('hr-chips').innerHTML = s.to.map((id) => note(id)).filter(Boolean).map((n) => `<span class="hr-chip">${svg(KIND_ICON[n.kind] || 'bell')}<b>${esc(n.name)}</b>`
      + `<small>${esc(n.where)}</small><button type="button" data-hr-x="${esc(n.id)}" aria-label="Remove ${esc(n.name)}">${svg('x')}</button></span>`).join('')
      || '<span class="hr-none">Nowhere yet: add where requests go.</span>';
  };
  const draw = () => {
    $('hr-on').checked = s.on; $('hr-on-label').textContent = s.on ? 'On' : 'Off';
    $('hr-alert').checked = s.alert; $('hr-alert-label').textContent = s.alert ? 'On' : 'Off';
    $('hr-details').checked = s.details; $('hr-details-label').textContent = s.details ? 'On' : 'Off';
    if (document.activeElement !== $('hr-text')) $('hr-text').value = s.text || '';
    if (document.activeElement !== $('hr-subject')) $('hr-subject').value = s.subject || '';
    $('hr-pictures').value = s.pictures;
    drawChips();
  };
  const load = async () => {
    try {
      s = await R.get('/api/help-requests');
      problem('');
      draw();
    } catch (err) {
      problem(`Could not read the help request settings. ${err.message}`);
    }
  };
  // Saves one part; the others stay as they are.
  const save = async (part, said) => {
    try {
      s = await R.send('/api/help-requests', part);
      problem('');
      draw();
      if (said) R.toast(said);
    } catch (err) {
      R.toast('Could not save the help request settings.', err.message, { tone: 'warn' });
      draw();
    }
  };

  const ready = () => {
    if (!$('hr-page')) return;
    [['hr-on', 'on'], ['hr-alert', 'alert'], ['hr-details', 'details']].forEach(([id, key]) => $(id).addEventListener('change', () => save({ [key]: $(id).checked })));
    $('hr-pictures').addEventListener('change', () => save({ pictures: $('hr-pictures').value }));
    $('hr-text').addEventListener('change', () => save({ text: $('hr-text').value }, 'How to reach IT saved.'));
    $('hr-subject').addEventListener('change', () => save({ subject: $('hr-subject').value }, 'Subject saved.'));
    // Add: the notifications not picked yet, and New notification, which opens Notifications.
    const menu = $('hr-menu');
    const closeMenu = () => { menu.hidden = true; $('hr-add').setAttribute('aria-expanded', 'false'); };
    $('hr-add').addEventListener('click', (e) => {
      e.stopPropagation();
      if (!menu.hidden) { closeMenu(); return; }
      const left = (s?.notifications || []).filter((n) => !s.to.includes(n.id));
      menu.innerHTML = left.map((n) => `<button type="button" role="menuitem" class="hr-mitem" data-hr-pick="${esc(n.id)}">${svg(KIND_ICON[n.kind] || 'bell')}<span class="hr-mi"><b>${esc(n.name)}</b><small>${esc(n.where)}</small></span></button>`).join('')
        + `${left.length ? '<hr>' : ''}<a role="menuitem" class="hr-mitem" href="#settings/notifications">${svg('plus')}<span class="hr-mi"><b>New notification</b><small>Email, Teams, Slack, ntfy or a webhook</small></span></a>`;
      menu.hidden = false;
      $('hr-add').setAttribute('aria-expanded', 'true');
    });
    menu.addEventListener('click', (e) => {
      const b = e.target.closest('[data-hr-pick]');
      closeMenu();
      if (b) save({ to: [...s.to, b.dataset.hrPick] });
    });
    document.addEventListener('click', (e) => { if (!e.target.closest('#hr-menu, #hr-add')) closeMenu(); });
    document.addEventListener('keydown', (e) => { if (e.key === 'Escape') closeMenu(); });
    $('hr-chips').addEventListener('click', (e) => {
      const x = e.target.closest('[data-hr-x]');
      if (x) save({ to: s.to.filter((id) => id !== x.dataset.hrX) });
    });
    $('hr-test').addEventListener('click', async () => {
      const b = $('hr-test');
      b.disabled = true;
      try {
        await R.send('/api/help-requests/test');
        R.toast('Sent a test help request.', `To ${s.to.map((id) => note(id)?.name).filter(Boolean).join(' and ')}.`);
      } catch (err) {
        R.toast('Could not send the test.', err.message, { tone: 'warn' });
      } finally {
        b.disabled = false;
      }
    });
    // The email as it arrives: from the lab, the subject filled in, who and which computer, what they wrote, the details, two screens.
    $('hr-preview').addEventListener('click', () => {
      const app = R.app();
      const to = s.to.map((id) => note(id)).filter((n) => n?.kind === 'email').map((n) => n.where).join(', ') || 'Your help desk';
      const subject = (s.subject || 'Help request: {title} on {computer}').replace('{title}', "The printer won't print").replace('{computer}', 'DESKTOP-4F7K2Q').replace('{user}', 'CONTOSO\\jsmith');
      const facts = [['Title', "The printer won't print"], ['Asked by', 'CONTOSO\\jsmith'], ['Computer', 'DESKTOP-4F7K2Q'], ['What they wrote', "It says it's printing but nothing comes out."]]
        .concat(s.details ? [['Signed-in user', 'CONTOSO\\jsmith'], ['IP address', '192.168.1.42'], ['Make and model', 'Dell Latitude 7440'], ['Serial number', '7HX2QK3'], ['Windows', 'Windows 11 Pro 24H2, build 26300'], ['Client version', '1.1.405']] : [])
        .concat(s.pictures !== 'never' ? [['Pictures', '2 screens, attached']] : []);
      $('hr-mail').innerHTML = `<dl class="hr-mhead"><dt>From</dt><dd>${esc(app)}</dd><dt>To</dt><dd>${esc(to)}</dd><dt>Subject</dt><dd><b>${esc(subject)}</b></dd></dl>`
        + `<dl class="hr-facts">${facts.map(([k, v]) => `<dt>${esc(k)}</dt><dd>${esc(v)}</dd>`).join('')}</dl>`
        + (s.pictures !== 'never' ? `<div class="hr-atts">${['Screen 1.png', 'Screen 2.png'].map((n, i) => `<span class="hr-att"><i class="hr-thumb s${i + 1}"><b></b></i><span><b>${n}</b><small>Attached</small></span></span>`).join('')}</div>` : '');
      $('hr-dialog').showModal();
    });
    $('hr-dialog-close').addEventListener('click', () => $('hr-dialog').close());
    document.addEventListener('rabbidlab:show', (e) => {
      if (e.detail[0] === 'settings' && e.detail[1] === 'help' && can('seats.settings')) load();
    });
  };
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', ready);
  else ready();
})();
