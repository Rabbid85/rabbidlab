// Devices, Deploy the client: one button puts the client on every computer through the Rabbid Lab Client GPO
// (ClientDeploy.cs); every computer in the domain with the client on it or not, and one computer's install log read from
// that computer (ClientDeployStatus.cs); the installer for one computer; and what to add in Intune when Intune manages
// Defender. Drawn where the other views draw a list.
(() => {
  const R = window.RabbidLab;
  const D = R.devices;
  const { $, esc, svg, has, get, toast, sure, panel, day, stamp: when, head, section, strip, badge } = R; // the last four: parts.js

  let state = null; // /api/gpo/client
  let addresses = []; // the console's addresses, full names first
  let computers = null; // /api/gpo/client/computers; null until read
  let listProblem = '';
  let joining = null; // /api/clients/settings: the installer's version and this server's fingerprint
  let show = 'all'; // which computers the list shows
  const logs = {}; // a computer's log as just read, for Download; the server keeps only its last lines
  let problem = '';
  let loading = null;
  const may = () => has('seats.add') && has('gpo.change');
  // What deploying does, as six small tiles: icon, a few words, one line.
  const DOES = [
    ['policy', 'One GPO', 'Rabbid Lab Client, linked at the domain level. Includes domain controllers.'],
    ['download', 'Installer in SYSVOL', 'All domain computers can read it from SYSVOL.'],
    ['clock', 'Installs at the next refresh', 'Within about 90 minutes. No restart required.'],
    ['eye-off', 'No installer window', 'The installation shows nothing. The tray icon shows when it is done.'],
    ['shield', 'Allowed in Defender', 'Excluded from attack surface reduction rules until the client is code-signed.'],
    ['check', 'Approval required', 'New computers appear under Computers as Pending approval.'],
  ];
  const copyBtn = (text) => `<button type="button" class="ds-btn ds-ibtn" data-deploy-copy="${esc(text)}" data-tip="Copy" aria-label="Copy">${svg('copy')}</button>`;
  // A computer's state: its badge, and the filter it is under.
  const STATES = { installed: ['green', 'Installed'], pending: ['amber', 'Pending approval'], failed: ['red', 'Install failed'], none: ['grey', 'Not installed'] };
  const SHOWS = [['all', 'All', () => true], ['failed', 'Install failed', (c) => c.state === 'failed'], ['none', 'Not installed', (c) => c.state === 'none'],
    ['installed', 'Installed', (c) => c.state === 'installed' || c.state === 'pending']];
  const signedIn = (c) => (c.lastSignIn ? `Last sign-in to the domain: ${day(c.lastSignIn)}` : 'Never signed in to the domain');
  // The line beside a computer's badge.
  const about = (c) => (c.state === 'installed' ? `Client ${c.version || ''}. ${c.online ? 'Online' : `Last seen ${when(c.seen)}`}`
    : c.state === 'pending' ? `Client ${c.version || ''}. Approve it under Computers.`
      : c.state === 'failed' ? `Tried ${when(c.log.written)}. ${c.log.reason || `Windows Installer result ${c.log.status}`}`
        : c.log && !c.log.written ? 'No install log on this computer' : signedIn(c));

  const load = () => (loading ||= (async () => {
    try {
      const [s, c] = await Promise.all([get('/api/gpo/client'), get('/api/console')]);
      state = s;
      // Full names reach every computer in the domain; short ones need the DNS suffix, and an IP breaks with DHCP.
      const urls = (c.addresses || []).map((a) => a.url);
      const rank = (u) => { const h = new URL(u).hostname; return /^\d+\.\d+\.\d+\.\d+$/.test(h) ? 2 : h.includes('.') ? 0 : 1; };
      addresses = [...new Set(urls)].sort((a, b) => rank(a) - rank(b));
      problem = '';
    } catch (e) {
      problem = e.message;
    }
    // The computers and the installer's details are read on their own: the deployment shows without them.
    try {
      computers = await get('/api/gpo/client/computers');
      listProblem = '';
    } catch (e) {
      listProblem = e.message;
    }
    try {
      joining ||= await get('/api/clients/settings');
    } catch {
      // The download is offered without its command.
    }
    loading = null;
    if (D.at?.() === 'deploy') draw();
    if (panel.is('deploy')) drawPanel(panel.key);
  })());

  // The list of computers: a filter, then a row each that opens the side panel.
  const list = (s) => {
    const sub = `Every enabled computer in ${esc(s.domain || 'the domain')}. A computer where the install failed tries again when you click Deploy again.`;
    const top = `<div class="dp-head"><span class="big-ic ember">${svg('monitor')}</span><div class="dp-title"><b>Computers</b><span>${sub}</span></div></div>`;
    if (!computers) return `<section class="card dp-card">${top}<p class="hint${listProblem ? ' problem' : ''}">${esc(listProblem || 'Reading computers from Active Directory')}</p></section>`;
    const of = SHOWS.find(([k]) => k === show)[2];
    const rows = computers.filter(of);
    return `<section class="card dp-card">${top}<div class="seg dp-seg" role="group" aria-label="Show">`
      + SHOWS.map(([k, l, is]) => `<button type="button" data-deploy-show="${k}" aria-pressed="${k === show}">${l} ${computers.filter(is).length.toLocaleString()}</button>`).join('')
      + `</div><div class="dp-rows">${rows.map((c) => `<button type="button" class="dp-row" data-deploy-pc="${esc(c.name)}"${panel.shows('deploy', c.name) ? ' aria-current="true"' : ''}>`
        + `<b>${esc(c.name)}</b><span>${esc(c.os || '')}</span>${badge(STATES[c.state][1], STATES[c.state][0])}<span class="dp-more">${esc(about(c))}</span>${svg('right')}</button>`).join('')
        || '<p class="hint">No computers to show.</p>'}</div></section>`;
  };
  // The installer for one computer, and the command for a silent install, to the address the deployment uses.
  const byHand = (server) => {
    if (!joining?.msi) return '';
    const line = [`msiexec /i RabbidLab-Client-${joining.version}.msi /qn SERVER=${server || location.origin}`, joining.fingerprint ? `FINGERPRINT=${joining.fingerprint}` : ''].filter(Boolean).join(' ');
    return `<section class="card dp-card"><div class="dp-head"><span class="big-ic ember">${svg('download')}</span>`
      + '<div class="dp-title"><b>Install on one computer</b><span>Download the installer and run it on the computer. It already knows this server. For a silent install, run the command as an administrator.</span></div>'
      + `<a class="ds-btn dp-get" href="/api/clients/download" download>${svg('download')}Download the installer</a></div>`
      + `<div class="dp-copy"><code>${esc(line)}</code>${copyBtn(line)}</div></section>`;
  };

  let drawn = ''; // what the card has, so the same again leaves it alone
  const draw = () => {
    const el = $('dv-deploy');
    if (!el) return;
    if (!state && !problem) {
      R.skeleton(el, 'rows', 4);
      return;
    }
    if (!state) {
      el.innerHTML = `<p class="hint problem">${esc(problem)}</p>`;
      return;
    }
    const s = state;
    const was = $('deploy-server')?.value; // an address picked and not deployed yet stays picked
    const picked = was && addresses.includes(was) ? was : s.server && addresses.includes(s.server) ? s.server : addresses[0] || '';
    const mark = !s.gpo ? badge('Not deployed', 'grey') : s.linked ? badge('Deployed', 'green') : badge('Not linked', 'amber');
    // Under the buttons: who deployed what, or what pressing Deploy asks for.
    const fine = !may() ? 'Deploying requires the Add computers and Create and edit GPOs permissions.'
      : s.gpo ? `Client ${s.version}, connecting to ${s.server}. Deployed by ${s.by} on ${when(s.at)}.${s.version !== s.thisVersion ? ` This server has version ${s.thisVersion}. Click Deploy again to update the installer in the GPO.` : ''}`
        : 'You will be asked to confirm your identity. The deployment is recorded in the Activity log.';
    const gpmc = s.gpmc ? '' : R.gpmcWaiting?.() ? `<div class="dp-note">${svg('alert')}<span>Group Policy Management is installed. Restart this server to finish the installation, then deploy.</span></div>`
      : `<div class="dp-note">${svg('alert')}<span>Group Policy Management is not installed on this server. It is required to create the GPO.</span>`
      + `${has('gpo.change') ? '<button type="button" class="ds-btn" data-install-gpmc>Install…</button>' : ''}</div>`;
    const html = '<div class="deploy">'
      + `<section class="card dp-card"><div class="dp-head"><span class="big-ic ember">${svg('policy')}</span>`
      + `<div class="dp-title"><b>Deploy with Group Policy</b><span>One GPO installs the client on every computer in ${esc(s.domain || 'the domain')}. The installation is silent.</span></div>${mark}</div>`
      + `<ul class="dp-does">${DOES.map(([icon, title, text]) => `<li>${svg(icon)}<b>${title}</b><span>${text}</span></li>`).join('')}</ul>`
      + gpmc
      + `<div class="dp-foot"><label class="dp-addr"><span>Server address</span><select id="deploy-server"${may() ? '' : ' disabled'}>${addresses.map((u) => `<option${u === picked ? ' selected' : ''}>${esc(u)}</option>`).join('')}</select></label>`
      + (may() ? `<span class="dp-acts">${s.linked ? '<button type="button" class="ds-btn" data-deploy-stop>Stop deploying</button>' : ''}`
        + `<button type="button" class="primary" data-deploy-go${s.gpmc && addresses.length ? '' : ' disabled'}>${svg('policy')}${s.gpo ? 'Deploy again' : 'Deploy to all computers'}</button></span>` : '')
      + `</div><p class="dp-fine">${esc(fine)}</p></section>`
      + list(s)
      + byHand(picked)
      + `<section class="card dp-card"><div class="dp-head"><span class="big-ic ember">${svg('shield')}</span>`
      + '<div class="dp-title"><b>Using Intune for Defender?</b><span>Add this path to your attack surface reduction policy, under Attack Surface Reduction Only Exclusions.</span></div></div>'
      + `<div class="dp-copy"><code>${esc(s.clientPath)}</code>${copyBtn(s.clientPath)}</div></section>`
      + '</div>';
    // Drawn again only when something in it changed.
    if (html === drawn && el.firstElementChild?.classList.contains('deploy')) return;
    drawn = html;
    el.innerHTML = html;
  };

  // One computer in the side panel: its state, and for one without the client its install log, read when asked for.
  const drawPanel = (name) => {
    const c = (computers || []).find((x) => x.name === name);
    if (!c) {
      panel.close();
      return;
    }
    const log = c.log;
    const get1 = (label) => (may() ? `<button type="button" class="ds-btn" data-deploy-log="${esc(c.name)}">${svg('refresh')}${label}</button>` : '');
    const say = (text) => `<p class="dp-say">${esc(text)}</p>`;
    const result = log?.status === 0 ? 'Installed (0)' : log?.status ? `Failed (${log.status})` : 'Not finished';
    const body = c.state === 'installed' ? section('Client', say(`The client is installed and approved. ${c.online ? 'This computer is online.' : `It last checked in ${when(c.seen)}.`}`))
      : c.state === 'pending' ? section('Client', say('The client is installed and waiting for approval. Approve this computer under Computers.'))
        : !log ? section('Install log', say('Read the install log from this computer to see whether the installer ran and what it reported. The computer must be turned on.') + `<div class="dp-log-acts">${get1('Get install log')}</div>`)
          : !log.written ? section('Install log', say('There is no install log on this computer, so the installer has not run there. Computers run it at a Group Policy refresh after the GPO changes.')
            + `<div class="dp-log-acts">${get1('Read again')}</div>`)
            : (log.status === 0 ? section('Result', say('The installer finished successfully, but the client has not connected to this server yet.'))
              : (log.reason ? section('What went wrong', say(log.reason)) : '') + (c.advice ? section('How to fix it', say(c.advice)) : ''))
              + section('Install log', `<pre class="code-box dp-log">${esc(log.tail || '')}</pre><div class="dp-log-acts">`
                + `<button type="button" class="ds-btn" data-deploy-copy="${esc(log.tail || '')}">${svg('copy')}Copy</button>`
                + `<button type="button" class="ds-btn" data-deploy-save="${esc(c.name)}">${svg('download')}Download</button>${get1('Read again')}</div>`);
    panel.body.innerHTML = head(c.controller || /server/i.test(c.os || '') ? 'server' : 'monitor', c.name, [c.os, STATES[c.state][1]].filter(Boolean).join(' · '))
      + strip([['Status', STATES[c.state][1]], ...(c.version ? [['Client', esc(c.version)]] : []),
        ...(log?.written ? [['Result', result], ['Installer', esc(log.version || '')], ['Log written', when(log.written)]] : []),
        ...(log ? [['Log read', `${when(log.read)} by ${esc(log.by)}`]] : [['Last sign-in', c.lastSignIn ? day(c.lastSignIn) : 'Never']])])
      + body
      + (joining?.msi ? `<div class="dw-bar"><a class="dw-qa" href="/api/clients/download" download>${svg('download')}<span>Download the installer</span></a></div>` : '');
  };
  const open = (name) => {
    if (panel.shows('deploy', name)) {
      panel.close();
      return;
    }
    panel.open('deploy', name);
    drawPanel(name);
  };
  // Read from the computer itself, as an administrator there: the server asks for a login when its own account is not one.
  const readLog = async (name) => {
    try {
      const a = await R.gpoChange('/api/gpo/client/log', { computer: name }, panel.body, 'Reading the install log');
      computers = (computers || []).map((c) => (c.name === a.computer.name ? a.computer : c));
      if (a.text) logs[a.computer.name] = a.text;
      const log = a.computer.log;
      if (!log.written) toast(`No install log on ${a.computer.name}.`, 'The installer has not run on this computer.');
      else if (log.status === 0) toast(`The install on ${a.computer.name} succeeded.`);
      else toast(`Read the install log from ${a.computer.name}.`, log.status ? `Windows Installer result ${log.status}.` : '');
    } catch (err) {
      if (!err.cancelled) toast('Could not read the install log.', err.message, { tone: 'warn' });
    }
    draw();
    if (panel.is('deploy')) drawPanel(panel.key);
  };

  D.lists.deploy = ['Deploy the client', 'download', 'Install the client on all computers with one GPO, plus the Intune exclusion.'];
  D.more.deploy = {
    table: 'dv-deploy', rows: () => [], count: () => '', can: () => has('seats.add') && has('gpo.view'), noun: 'settings',
    grid: () => ({ draw }), load: async () => load(), empty: () => '',
    every: 60000, // a minute apart while it's open: the GPO and the computers are read from Active Directory
  };

  // Group Policy Management just went on (gpo.js): read again.
  document.addEventListener('rabbidlab:gpmc', () => {
    state = null;
    load();
  });
  // Its panel goes with its view.
  window.addEventListener('hashchange', () => { if (panel.is('deploy') && (R.where()[0] !== 'seats' || D.at?.() !== 'deploy')) panel.close(); });
  panel.el.addEventListener('close', () => { if (D.at?.() === 'deploy') draw(); });

  document.addEventListener('click', async (e) => {
    const c = e.target.closest('[data-deploy-copy]');
    if (c) {
      try {
        await navigator.clipboard.writeText(c.dataset.deployCopy);
        toast('Copied to the clipboard.');
      } catch {
        toast('Could not copy to the clipboard.', 'Select the text and press Ctrl+C.', { tone: 'warn' });
      }
      return;
    }
    const showing = e.target.closest('[data-deploy-show]');
    if (showing) {
      show = showing.dataset.deployShow;
      draw();
      return;
    }
    const pc = e.target.closest('[data-deploy-pc]');
    if (pc) {
      open(pc.dataset.deployPc);
      draw();
      return;
    }
    const log = e.target.closest('[data-deploy-log]');
    if (log) {
      readLog(log.dataset.deployLog);
      return;
    }
    const save = e.target.closest('[data-deploy-save]');
    if (save) {
      // The whole of what was just read, or the lines the server kept.
      const name = save.dataset.deploySave;
      const text = logs[name] || (computers || []).find((x) => x.name === name)?.log?.tail || '';
      const a = Object.assign(document.createElement('a'), { href: URL.createObjectURL(new Blob([text.replace(/\r?\n/g, '\r\n')], { type: 'text/plain' })), download: `${name}-RabbidLab-Client-install.log` });
      a.click();
      URL.revokeObjectURL(a.href);
      return;
    }
    if (e.target.closest('[data-deploy-go]')) {
      const server = $('deploy-server')?.value;
      sure(state?.gpo ? 'Deploy the client again?' : 'Deploy the client to all computers?',
        `All computers in the domain install the client at the next Group Policy refresh and connect to ${server}.`, 'Deploy', async () => {
          try {
            await R.gpoChange('/api/gpo/client', { server }, $('dv-deploy'));
            toast('Client deployed.', 'Computers install it at the next Group Policy refresh, within about 90 minutes.');
          } catch (err) {
            if (!err.cancelled) toast('Could not deploy the client.', err.message, { tone: 'warn' });
          }
          state = null;
          load();
        });
    }
    if (e.target.closest('[data-deploy-stop]')) {
      sure('Stop deploying the client?', 'The GPO no longer applies to the domain, so no new computers install the client. Computers that already have it keep it.', 'Stop deploying', async () => {
        try {
          await R.gpoChange('/api/gpo/client/stop', {}, $('dv-deploy'));
          toast('Client deployment stopped.');
        } catch (err) {
          if (!err.cancelled) toast('Could not stop deploying the client.', err.message, { tone: 'warn' });
        }
        state = null;
        load();
      });
    }
  });
})();
