// Devices, Deploy the client: one button puts the client on every computer through the Rabbid Lab Client GPO
// (ClientDeploy.cs), and what to add in Intune when Intune manages Defender. Drawn where the other views draw a list.
(() => {
  const R = window.RabbidLab;
  const D = R.devices;
  const { $, esc, svg, has, get, toast, sure } = R;

  let state = null; // /api/gpo/client
  let addresses = []; // the console's addresses, full names first
  let problem = '';
  let loading = null;
  const when = (iso) => (iso ? new Date(iso).toLocaleString([], { dateStyle: 'medium', timeStyle: 'short' }) : '');
  const may = () => has('seats.add') && has('gpo.change');
  // What deploying does, as six small tiles: icon, a few words, one line.
  const DOES = [
    ['policy', 'One GPO', 'Rabbid Lab Client, linked at the domain level. Includes domain controllers.'],
    ['download', 'Installer in SYSVOL', 'All domain computers can read it from SYSVOL.'],
    ['clock', 'Installs at the next refresh', 'Within about 90 minutes. No restart required.'],
    ['eye-off', 'Silent install', 'No installer window, tray icon or messages.'],
    ['shield', 'Allowed in Defender', 'Excluded from attack surface reduction rules until the client is code-signed.'],
    ['check', 'Approval required', 'New computers appear under Computers as Pending approval.'],
  ];
  const copyBtn = (text) => `<button type="button" class="ds-btn ds-ibtn" data-deploy-copy="${esc(text)}" data-tip="Copy" aria-label="Copy">${svg('copy')}</button>`;

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
    } finally {
      loading = null;
    }
    if (D.at?.() === 'deploy') draw();
  })());

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
    const badge = !s.gpo ? '<span class="ubadge grey">Not deployed</span>' : s.linked ? '<span class="ubadge green">Deployed</span>' : '<span class="ubadge amber">Not linked</span>';
    // Under the buttons: who deployed what, or what pressing Deploy asks for.
    const fine = !may() ? 'Deploying requires the Add computers and Create and edit GPOs permissions.'
      : s.gpo ? `Client ${s.version}, connecting to ${s.server}. Deployed by ${s.by} on ${when(s.at)}.${s.version !== s.thisVersion ? ` This server has version ${s.thisVersion}. Click Deploy again to update the installer in the GPO.` : ''}`
        : 'You will be asked to confirm your identity. The deployment is recorded in the Activity log.';
    const gpmc = s.gpmc ? '' : R.gpmcWaiting?.() ? `<div class="dp-note">${svg('alert')}<span>Group Policy Management is installed. Restart this server to finish the installation, then deploy.</span></div>`
      : `<div class="dp-note">${svg('alert')}<span>Group Policy Management is not installed on this server. It is required to create the GPO.</span>`
      + `${has('gpo.change') ? '<button type="button" class="ds-btn" data-install-gpmc>Install…</button>' : ''}</div>`;
    const html = '<div class="deploy">'
      + `<section class="card dp-card"><div class="dp-head"><span class="big-ic ember">${svg('policy')}</span>`
      + `<div class="dp-title"><b>Deploy with Group Policy</b><span>One GPO installs the client on every computer in ${esc(s.domain || 'the domain')}. The installation is silent.</span></div>${badge}</div>`
      + `<ul class="dp-does">${DOES.map(([icon, title, text]) => `<li>${svg(icon)}<b>${title}</b><span>${text}</span></li>`).join('')}</ul>`
      + gpmc
      + `<div class="dp-foot"><label class="dp-addr"><span>Server address</span><select id="deploy-server"${may() ? '' : ' disabled'}>${addresses.map((u) => `<option${u === picked ? ' selected' : ''}>${esc(u)}</option>`).join('')}</select></label>`
      + (may() ? `<span class="dp-acts">${s.linked ? '<button type="button" class="ds-btn" data-deploy-stop>Stop deploying</button>' : ''}`
        + `<button type="button" class="primary" data-deploy-go${s.gpmc && addresses.length ? '' : ' disabled'}>${svg('policy')}${s.gpo ? 'Deploy again' : 'Deploy to all computers'}</button></span>` : '')
      + `</div><p class="dp-fine">${esc(fine)}</p></section>`
      + `<section class="card dp-card"><div class="dp-head"><span class="big-ic ember">${svg('shield')}</span>`
      + '<div class="dp-title"><b>Using Intune for Defender?</b><span>Add this path to your attack surface reduction policy, under Attack Surface Reduction Only Exclusions.</span></div></div>'
      + `<div class="dp-copy"><code>${esc(s.clientPath)}</code>${copyBtn(s.clientPath)}</div></section>`
      + '</div>';
    // Drawn again only when something in it changed.
    if (html === drawn && el.firstElementChild?.classList.contains('deploy')) return;
    drawn = html;
    el.innerHTML = html;
  };

  D.lists.deploy = ['Deploy the client', 'download', 'Install the client on all computers with one GPO, plus the Intune exclusion.'];
  D.more.deploy = {
    table: 'dv-deploy', rows: () => [], count: () => '', can: () => has('seats.add') && has('gpo.view'), noun: 'settings',
    grid: () => ({ draw }), load: async () => load(), empty: () => '',
    every: 60000, // a minute apart while it's open: the GPO is read from Active Directory
  };

  // Group Policy Management just went on (gpo.js): read again.
  document.addEventListener('rabbidlab:gpmc', () => {
    state = null;
    load();
  });

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
