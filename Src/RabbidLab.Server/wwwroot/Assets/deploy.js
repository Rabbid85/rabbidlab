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
    ['policy', 'One GPO', 'Rabbid Lab Client, linked to the whole domain. Domain controllers too.'],
    ['download', 'Installer in SYSVOL', 'Every domain computer can already read it there.'],
    ['clock', 'At the next policy refresh', 'Within about 90 minutes. No restart.'],
    ['eye-off', 'Nothing on screen', 'No installer window, no tray icon, no messages.'],
    ['shield', 'Allowed in Defender', 'Past attack surface reduction, until the client is code-signed.'],
    ['check', 'You approve each one', 'New computers wait under Computers.'],
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
    const picked = s.server && addresses.includes(s.server) ? s.server : addresses[0] || '';
    const badge = !s.gpo ? '<span class="ubadge grey">Not deployed</span>' : s.linked ? '<span class="ubadge green">Deployed</span>' : '<span class="ubadge amber">Not linked</span>';
    // Under the buttons: who deployed what, or what pressing Deploy asks for.
    const fine = !may() ? 'Deploying takes Add computers and Change Group Policy.'
      : s.gpo ? `Client ${s.version}, pointed at ${s.server}. Deployed by ${s.by} on ${when(s.at)}.${s.version !== s.thisVersion ? ` This server is on ${s.thisVersion}. Deploy again puts that installer in the GPO.` : ''}`
        : "You confirm it's you first. It goes in the activity log.";
    const gpmc = s.gpmc ? '' : R.gpmcWaiting?.() ? `<div class="dp-note">${svg('alert')}<span>Group Policy Management is installed, and Windows needs a restart to finish. Restart this server, then deploy.</span></div>`
      : `<div class="dp-note">${svg('alert')}<span>Group Policy Management isn't installed on this server. It makes the GPO.</span>`
      + `${has('gpo.change') ? '<button type="button" class="ds-btn" data-install-gpmc>Install it…</button>' : ''}</div>`;
    el.innerHTML = '<div class="deploy">'
      + `<section class="card dp-card"><div class="dp-head"><span class="big-ic ember">${svg('policy')}</span>`
      + `<div class="dp-title"><b>Deploy with Group Policy</b><span>One GPO puts the client on every computer in ${esc(s.domain || 'the domain')}. Nobody at the computer sees a thing.</span></div>${badge}</div>`
      + `<ul class="dp-does">${DOES.map(([icon, title, text]) => `<li>${svg(icon)}<b>${title}</b><span>${text}</span></li>`).join('')}</ul>`
      + gpmc
      + `<div class="dp-foot"><label class="dp-addr"><span>Server address</span><select id="deploy-server"${may() ? '' : ' disabled'}>${addresses.map((u) => `<option${u === picked ? ' selected' : ''}>${esc(u)}</option>`).join('')}</select></label>`
      + (may() ? `<span class="dp-acts">${s.linked ? '<button type="button" class="ds-btn" data-deploy-stop>Stop deploying</button>' : ''}`
        + `<button type="button" class="primary" data-deploy-go${s.gpmc && addresses.length ? '' : ' disabled'}>${svg('policy')}${s.gpo ? 'Deploy again' : 'Deploy to every computer'}</button></span>` : '')
      + `</div><p class="dp-fine">${esc(fine)}</p></section>`
      + `<section class="card dp-card"><div class="dp-head"><span class="big-ic ember">${svg('shield')}</span>`
      + '<div class="dp-title"><b>Using Intune for Defender?</b><span>Add this path to your attack surface reduction policy, under Attack Surface Reduction Only Exclusions.</span></div></div>'
      + `<div class="dp-copy"><code>${esc(s.clientPath)}</code>${copyBtn(s.clientPath)}</div></section>`
      + '</div>';
  };

  D.lists.deploy = ['Deploy the client', 'download', 'Put the client on every computer with one GPO, and what Intune needs.'];
  D.more.deploy = {
    table: 'dv-deploy', rows: () => [], count: () => '', can: () => has('seats.add') && has('gpo.view'), noun: 'settings',
    grid: () => ({ draw }), load: async () => load(), empty: () => '',
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
        toast('Copied.');
      } catch {
        toast("Couldn't copy.", 'Select it and press Ctrl+C.', { tone: 'warn' });
      }
      return;
    }
    if (e.target.closest('[data-deploy-go]')) {
      const server = $('deploy-server')?.value;
      sure(state?.gpo ? 'Deploy the client again?' : 'Deploy the client to every computer?',
        `Every computer in the domain installs it at its next policy refresh and connects to ${server}.`, 'Deploy', async () => {
          try {
            await R.gpoChange('/api/gpo/client', { server }, $('dv-deploy'));
            toast('The client is deployed.', 'Computers install it at their next policy refresh, within about 90 minutes.');
          } catch (err) {
            if (!err.cancelled) toast("That didn't work.", err.message, { tone: 'warn' });
          }
          state = null;
          load();
        });
    }
    if (e.target.closest('[data-deploy-stop]')) {
      sure('Stop deploying the client?', 'The GPO comes off the domain, so no more computers install it. Computers that have it keep it.', 'Stop deploying', async () => {
        try {
          await R.gpoChange('/api/gpo/client/stop', {}, $('dv-deploy'));
          toast('Stopped deploying the client.');
        } catch (err) {
          if (!err.cancelled) toast("That didn't work.", err.message, { tone: 'warn' });
        }
        state = null;
        load();
      });
    }
  });
})();
