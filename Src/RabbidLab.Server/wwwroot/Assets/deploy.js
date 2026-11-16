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
  const row = (title, text, right = '') => `<div class="ds-row"><div class="ds-l"><b>${title}</b><span>${text}</span></div>${right ? `<div class="ds-r">${right}</div>` : ''}</div>`;
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
    const status = s.gpo
      ? row(`${esc(s.name)} ${s.linked ? '<span class="ubadge green">Linked to the domain</span>' : '<span class="ubadge amber">Not linked</span>'}`,
        esc(`Client ${s.version}, pointed at ${s.server}. Deployed by ${s.by} on ${when(s.at)}.${s.version !== s.thisVersion ? ` This server is on ${s.thisVersion}: Deploy again puts that installer in it.` : ''}`),
        may() && s.linked ? '<button type="button" class="ds-btn" data-deploy-stop>Stop deploying</button>' : '')
      : row('Not deployed yet', 'Computers get the client one by one, from Add computers, until it is.');
    const gpmc = s.gpmc ? '' : `<p class="ds-note deploy-warn">${svg('alert')}<span>Group Policy Management isn't on this server, and it's needed to make the GPO.`
      + `${has('gpo.change') ? ' <button type="button" class="linkish" data-install-gpmc>Install it…</button>' : ' Someone who can change Group Policy can install it from here.'}</span></p>`;
    el.innerHTML = '<div class="deploy">'
      + `<section class="ds-sec"><h3 class="ds-h">With Group Policy</h3>${status}`
      + row('Server address', 'What every computer connects to. A full name works anywhere in the domain.',
        `<select id="deploy-server"${may() ? '' : ' disabled'}>${addresses.map((u) => `<option${u === picked ? ' selected' : ''}>${esc(u)}</option>`).join('')}</select>`)
      + `<ul class="deploy-does">${[
        ['policy', `Makes the GPO ${s.gpo ? esc(s.name) : 'Rabbid Lab Client'} and links it to the domain: every computer, domain controllers too.`],
        ['download', "Puts the client installer in the GPO's own folder in SYSVOL, which every domain computer can read."],
        ['clock', 'Each computer installs it quietly as SYSTEM at its next policy refresh, within about 90 minutes. No restart.'],
        ['eye', 'People at the computer see nothing: no install, no tray icon, no messages. The Hide the client policy in the Rabbid Lab template can show the icon again.'],
        ['shield', `Lets <code>${esc(s.clientPath)}</code> past Defender's attack surface reduction rules, until the client is code-signed.`],
        ['check', 'New computers wait under Computers, Waiting for approval.'],
      ].map(([icon, text]) => `<li>${svg(icon)}<span>${text}</span></li>`).join('')}</ul>`
      + gpmc
      + (may() ? `<div class="deploy-go"><button type="button" class="primary" data-deploy-go${s.gpmc && addresses.length ? '' : ' disabled'}>${svg('policy')}${s.gpo ? 'Deploy again' : 'Deploy to every computer'}</button>`
        + '<span class="muted">You confirm it\'s you first. It goes in the activity log.</span></div>'
        : '<p class="muted">Deploying takes Add computers and Change Group Policy.</p>')
      + '</section>'
      + '<section class="ds-sec"><h3 class="ds-h">Intune</h3>'
      + row('Defender exclusion', esc("When Intune manages Defender, add this to your attack surface reduction policy too: Endpoint security, Attack surface reduction, your policy, Attack Surface Reduction Only Exclusions."),
        `<code class="deploy-path">${esc(s.clientPath)}</code>${copyBtn(s.clientPath)}`)
      + '</section></div>';
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
