// Devices: doing things on computers. The ticks on Computers and its Actions menu, the lists under Tools (Scripts, Jobs,
// Workflows), the windows for running a script, writing one and making a workflow, and confirming who's asking before
// anything runs. The page itself is console.js; this adds to it through RabbidLab.devices.
(() => {
  const R = window.RabbidLab;
  const D = R.devices;
  const { $, esc, svg, send, get, toast, sure, panel, when, has, say } = R;

  const NEEDS = '1.1.168'; // the first client that takes jobs
  const BRAKE = 10; // more computers than this, and how many has to be typed
  const canScripts = () => has('seats.scripts.run') || has('seats.scripts.edit');
  const canDo = () => canScripts() || has('seats.actions');
  const canJobs = () => canDo() || has('seats.workflows');
  const canPick = () => canDo() || has('seats.approve') || has('seats.remove');
  const many = (n, one = 'computer') => `${n.toLocaleString()} ${one}${n === 1 ? '' : 's'}`;
  // Whether a computer's client is older than the version given.
  const below = (c, version) => {
    const [a, b] = [c.version || '0', version].map((v) => v.split('.').map(Number));
    for (let i = 0; i < 3; i++) if ((a[i] || 0) !== b[i]) return (a[i] || 0) < b[i];
    return false;
  };
  const older = (c) => below(c, NEEDS);
  const names = (list) => (list.length <= 4 ? list.map((c) => c.computer).join(', ') : many(list.length));
  const LANG = { powershell: 'PowerShell', cmd: 'Command' };
  const oops = (e) => { if (!e.cancelled) toast("That didn't work.", e.message, { tone: 'warn' }); };
  const head = (icon, title, sub) => `<div class="dw-head"><span class="pic dw-pic">${svg(icon)}</span><div class="dw-name"><h2 id="dw-title">${esc(title)}</h2><p>${sub}</p></div>`
    + `<button type="button" class="icon dw-close" data-dw-close aria-label="Close">${svg('x')}</button></div>`;
  const qa = (attr, icon, label) => `<button type="button" class="dw-qa" ${attr}>${svg(icon)}<span>${esc(label)}</span></button>`;
  const section = (title, body, right = '') => `<section class="dw-sec"><div class="dw-sec-head"><h3>${title}</h3>${right}</div>${body}</section>`;
  const facts = (rows) => `<ul class="does">${rows.filter(Boolean).map(([k, v]) => `<li><b>${esc(k)}</b><span>${esc(v)}</span></li>`).join('')}</ul>`;
  const none = (text) => `<p class="muted dw-empty">${esc(text)}</p>`;
  const pill = ([tone, label]) => `<span class="state ${tone}">${esc(label)}</span>`;
  const pressed = (seg, attr, value) => seg.querySelectorAll('button').forEach((b) => b.setAttribute('aria-pressed', String(b.dataset[attr] === value)));

  // --- Confirming who it is ------------------------------------------------------------------------------------
  // The server refuses to run or save anything until this session has confirmed lately, and says what it takes: a code
  // from the authenticator app, or the password. confirmed() asks, then does what was refused once more.
  let asking = null; // { how, ok, no } while the window is open
  const askConfirm = (how) => new Promise((ok, no) => {
    asking = { how, ok, no };
    const code = how === 'code';
    $('cf-lead').textContent = code ? 'Type the code from your authenticator app. You are asked again after 10 minutes.' : 'Type your password. You are asked again after 10 minutes.';
    for (const [id, show] of [['cf-code', code], ['cf-code-label', code], ['cf-password', !code], ['cf-password-label', !code]]) $(id).hidden = !show;
    $('cf-code').value = '';
    $('cf-password').value = '';
    say('cf-problem', '');
    $('confirm-dialog').showModal();
    $(code ? 'cf-code' : 'cf-password').focus();
  });
  const confirmGo = async () => {
    if (!asking) return;
    $('cf-go').disabled = true;
    try {
      await send('/api/confirm', asking.how === 'code' ? { code: $('cf-code').value } : { password: $('cf-password').value });
      const { ok } = asking;
      asking = null;
      $('confirm-dialog').close();
      ok();
    } catch (e) {
      say('cf-problem', e.message);
    }
    $('cf-go').disabled = false;
  };
  $('cf-go').addEventListener('click', confirmGo);
  $('cf-cancel').addEventListener('click', () => $('confirm-dialog').close());
  for (const id of ['cf-code', 'cf-password']) $(id).addEventListener('keydown', (e) => { if (e.key === 'Enter') confirmGo(); });
  $('confirm-dialog').addEventListener('close', () => {
    if (!asking) return;
    const { no } = asking;
    asking = null;
    no(Object.assign(new Error('Cancelled.'), { cancelled: true }));
  });
  const confirmed = async (act) => {
    try {
      return await act();
    } catch (e) {
      if (!e.answer?.confirm) throw e;
      await askConfirm(e.answer.confirm);
      return act();
    }
  };

  // --- The lists under Tools -------------------------------------------------------------------------------------
  let scripts = null; // /api/devices/scripts
  let jobs = null; // /api/devices/jobs
  let flows = null; // /api/devices/workflows
  const scriptOf = (id) => (scripts || []).find((s) => s.id === id);
  const jobTitle = (j) => (j.kind === 'workflow' ? `Workflow: ${j.title}` : j.kind === 'script' ? (j.title === 'A script typed in' ? 'Run a script typed in' : `Run script: ${j.title}`) : j.title);
  const JOB_ICON = { script: 'code', restart: 'restart', updates: 'updates', workflow: 'org', printer: 'printer', check: 'refresh', lock: 'lock', signout: 'logout', shutdown: 'power', message: 'message' };
  const STATE = { waiting: ['waiting', 'Waiting'], running: ['run', 'Running'], done: ['online', 'Done'], failed: ['bad', 'Failed'], refused: ['bad', 'Refused'],
    noanswer: ['bad', 'No answer'], cancelled: ['offline', 'Cancelled'], expired: ['offline', 'Expired'] };
  // How a job went across its computers, in a word or two.
  const result = (j) => {
    const of = (k) => j.states[k] || 0;
    const n = j.computers;
    const bad = of('failed') + of('refused') + of('noanswer');
    const open = of('waiting') + of('running');
    if (open) return [of('running') ? 'run' : 'waiting', open === n ? (of('running') ? 'Running' : 'Waiting') : `${n - open} of ${n} finished`];
    if (bad) return ['bad', n === 1 ? STATE[Object.keys(j.states)[0]][1] : `Failed on ${bad} of ${n}`];
    if (of('done') === n) return ['online', 'Done'];
    return of('done') ? ['waiting', `Done on ${of('done')} of ${n}`] : ['offline', of('expired') ? 'Expired' : 'Cancelled'];
  };
  const open = (j) => (j.states.waiting || 0) + (j.states.running || 0) > 0;
  const DAYS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
  const clock = (time) => {
    const [h, m] = (time || '00:00').split(':').map(Number);
    return new Date(2000, 0, 1, h, m).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' });
  };
  const WHEN = [['approved', 'A new computer is approved'], ['schedule', 'On a schedule'], ['online', 'A computer comes online'], ['updates', 'A computer is missing Windows updates'],
    ['restart', 'A computer needs a restart'], ['disk', 'Free space on C: runs low']];
  const IF = [['always', 'Every computer'], ['name', 'Computers named'], ['windows', 'Windows name contains'], ['notwindows', "Windows name doesn't contain"], ['nobody', 'Computers with nobody signed in']];
  const ACTIONS = [['updates', 'Install missing Windows updates'], ['restart', 'Restart'], ['restart-if-needed', 'Restart if Windows needs it']];
  const whenText = (w) => (w.when === 'schedule' ? `${w.days.length === 7 ? 'Every day' : w.days.map((d) => DAYS[d]).join(', ')} at ${clock(w.time)}`
    : w.when === 'disk' ? `Free space on C: drops below ${w.below}%` : WHEN.find((x) => x[0] === w.when)?.[1] || w.when);
  const ifText = (w) => (w.if === 'always' ? '' : w.if === 'nobody' ? 'Nobody is signed in' : `${IF.find((x) => x[0] === w.if)?.[1] || w.if} ${w.ifText || ''}`);
  const stepText = (s) => (s.kind === 'script' ? `Run script: ${scriptOf(s.script)?.name || 'a script that was deleted'}` : ACTIONS.find((a) => a[0] === s.kind)?.[1] || s.kind);
  const ms = (iso) => (iso ? new Date(iso).getTime() : 0);

  let scriptsG = null;
  const scriptsGrid = () => (scriptsG ||= R.grid({
    el: $('dv-scripts'), key: 'scripts',
    cols: [
      { key: 'name', label: 'Name', w: 240, fixed: true, get: (s) => s.name, cell: (s) => `<b>${esc(s.name)}</b>` },
      { key: 'language', label: 'Language', w: 100, on: true, get: (s) => LANG[s.language] },
      { key: 'about', label: 'Description', w: 280, on: true, get: (s) => s.about || '', cell: (s) => `<span class="ell muted">${esc(s.about || '')}</span>` },
      { key: 'changed', label: 'Changed', w: 150, on: true, get: (s) => ms(s.changed), text: (s) => s.changed || '', cell: (s) => `<span class="muted">${esc(when(s.changed))}</span>` },
      { key: 'by', label: 'By', w: 110, on: true, get: (s) => (s.builtIn ? 'Built-in' : s.by || '') },
      { key: 'minutes', label: 'Time limit', w: 100, get: (s) => s.minutes, text: (s) => `${s.minutes} min`, cell: (s) => `<span class="muted">${s.minutes} min</span>` },
      { key: 'workflows', label: 'Workflows', w: 180, get: (s) => s.workflows.join(', ') },
    ],
    row: (s) => ` data-row="script:${esc(s.id)}"`,
    empty: () => D.more.scripts.empty(),
    redraw: () => D.draw(),
  }));
  let jobsG = null;
  const jobsGrid = () => (jobsG ||= R.grid({
    el: $('dv-jobs'), key: 'jobs', sort: { key: 'at', dir: -1 },
    cols: [
      { key: 'title', label: 'What', w: 270, fixed: true, get: (j) => jobTitle(j), cell: (j) => `<b>${esc(jobTitle(j))}</b>` },
      { key: 'at', label: 'When', w: 150, on: true, get: (j) => ms(j.at), text: (j) => j.at, cell: (j) => `<span class="muted">${esc(when(j.at))}</span>` },
      { key: 'by', label: 'By', w: 110, on: true, get: (j) => j.by },
      { key: 'on', label: 'Computers', w: 180, on: true, get: (j) => (j.computers <= 3 ? j.names.join(', ') : many(j.computers)), cell: (j) => `<span class="ell">${esc(j.computers <= 3 ? j.names.join(', ') : many(j.computers))}</span>` },
      { key: 'result', label: 'Result', w: 140, on: true, get: (j) => result(j)[1], cell: (j) => pill(result(j)) },
    ],
    row: (j) => ` data-row="job:${esc(j.id)}"`,
    empty: () => D.more.jobs.empty(),
    redraw: () => D.draw(),
  }));
  let flowsG = null;
  const flowsGrid = () => (flowsG ||= R.grid({
    el: $('dv-workflows'), key: 'workflows',
    cols: [
      { key: 'name', label: 'Workflow', w: 200, fixed: true, get: (w) => w.name, cell: (w) => `<b>${esc(w.name)}</b>` },
      { key: 'when', label: 'When', w: 230, on: true, get: (w) => whenText(w), cell: (w) => `<span class="ell">${esc(whenText(w))}</span>` },
      { key: 'steps', label: 'Steps', w: 250, on: true, get: (w) => w.steps.map(stepText).join(', '), cell: (w) => `<span class="ell">${esc(w.steps.map(stepText).join(' → '))}</span>` },
      { key: 'on', label: 'State', w: 80, on: true, get: (w) => (w.on ? 0 : 1), text: (w) => (w.on ? 'Enabled' : 'Disabled'), cell: (w) => pill(w.on ? ['online', 'Enabled'] : ['offline', 'Disabled']) },
      { key: 'lastRun', label: 'Last run', w: 130, on: true, get: (w) => ms(w.lastRun), text: (w) => w.lastRun || '', cell: (w) => `<span class="muted">${w.lastRun ? esc(when(w.lastRun)) : 'Never'}</span>` },
      { key: 'if', label: 'Computers', w: 200, get: (w) => ifText(w) || 'Every computer' },
    ],
    row: (w) => ` data-row="flow:${esc(w.id)}"`,
    empty: () => D.more.workflows.empty(),
    redraw: () => D.draw(),
  }));
  D.lists.scripts = ['Scripts', 'code', 'PowerShell and command scripts to run on computers, as SYSTEM.'];
  D.lists.jobs = ['Jobs', 'history', 'Everything that was run on computers: by whom, what exactly, and how it went.'];
  D.lists.workflows = ['Workflows', 'org', 'Steps that run by themselves, when something happens or on a schedule.'];
  D.more.scripts = { table: 'dv-scripts', grid: scriptsGrid, rows: () => scripts || [], count: () => scripts?.length, can: () => canScripts() || has('seats.workflows'),
    load: async () => { scripts = await get('/api/devices/scripts'); },
    empty: () => (!scripts ? 'Reading the list.' : has('seats.scripts.edit') ? 'No scripts yet. New script adds the first one.' : 'No scripts yet.') };
  D.more.jobs = { table: 'dv-jobs', grid: jobsGrid, rows: () => jobs || [], count: () => (jobs || []).filter(open).length, can: canJobs,
    load: async () => { jobs = await get('/api/devices/jobs'); },
    empty: () => (jobs ? 'Nothing has been run on a computer yet.' : 'Reading the list.') };
  D.more.workflows = { table: 'dv-workflows', grid: flowsGrid, rows: () => flows || [], count: () => flows?.length, can: () => has('seats.workflows'),
    // A workflow's steps are shown by their script's name.
    load: async () => { [flows, scripts] = await Promise.all([get('/api/devices/workflows'), get('/api/devices/scripts')]); },
    empty: () => (flows ? 'No workflows yet. New workflow adds the first one.' : 'Reading the list.') };

  // --- WMI query ------------------------------------------------------------------------------------------------
  // Anything Windows keeps in WMI, read on each computer picked, as SYSTEM, by its client (the client's Wmi.cs). What
  // comes back is a list like the others: Columns, Export and Filter work on it, and a row opens all it has in the panel.
  const WMI_FROM = '1.1.173'; // the first client that answers
  const WQ_EXAMPLES = [
    ['BIOS and serial number', '', 'SELECT Manufacturer, SMBIOSBIOSVersion, ReleaseDate, SerialNumber FROM Win32_BIOS'],
    ['Make, model and memory', '', 'SELECT Manufacturer, Model, TotalPhysicalMemory, Domain, UserName FROM Win32_ComputerSystem'],
    ['Windows version and last restart', '', 'SELECT Caption, Version, BuildNumber, InstallDate, LastBootUpTime FROM Win32_OperatingSystem'],
    ['Processor', '', 'SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor'],
    ['Memory sticks', '', 'SELECT BankLabel, Capacity, Speed, Manufacturer, PartNumber FROM Win32_PhysicalMemory'],
    ['Drives and free space', '', 'SELECT DeviceID, VolumeName, FileSystem, Size, FreeSpace FROM Win32_LogicalDisk WHERE DriveType = 3'],
    ['Physical disks', '', 'SELECT Model, MediaType, Size, SerialNumber, Status FROM Win32_DiskDrive'],
    ['Network adapters', '', 'SELECT Description, IPAddress, DefaultIPGateway, DNSServerSearchOrder, MACAddress, DHCPEnabled FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = TRUE'],
    ['Automatic services that are stopped', '', "SELECT Name, DisplayName, State, StartMode FROM Win32_Service WHERE StartMode = 'Auto' AND State <> 'Running'"],
    ['Programs that start with Windows', '', 'SELECT Name, Command, Location, User FROM Win32_StartupCommand'],
    ['Installed updates', '', 'SELECT HotFixID, Description, InstalledOn, InstalledBy FROM Win32_QuickFixEngineering'],
    ['User profiles', '', 'SELECT LocalPath, LastUseTime, Loaded FROM Win32_UserProfile WHERE Special = FALSE'],
    ['Printers', '', 'SELECT Name, PortName, DriverName, Shared, Network FROM Win32_Printer'],
    ['Shared folders', '', 'SELECT Name, Path, Description FROM Win32_Share'],
    ['Monitors and their serial numbers', 'root\\wmi', 'SELECT ManufacturerName, UserFriendlyName, SerialNumberID, YearOfManufacture FROM WmiMonitorID'],
    ['BitLocker on each drive', 'root\\cimv2\\Security\\MicrosoftVolumeEncryption', 'SELECT DriveLetter, ProtectionStatus, ConversionStatus, EncryptionMethod FROM Win32_EncryptableVolume'],
    ['TPM', 'root\\cimv2\\Security\\MicrosoftTpm', 'SELECT IsActivated_InitialValue, IsEnabled_InitialValue, SpecVersion, ManufacturerVersion FROM Win32_Tpm'],
    ['Microsoft Defender', 'root\\Microsoft\\Windows\\Defender', 'SELECT AMProductVersion, AntivirusSignatureLastUpdated, RealTimeProtectionEnabled, QuickScanEndTime FROM MSFT_MpComputerStatus'],
    ['Antivirus Windows knows about', 'root\\SecurityCenter2', 'SELECT displayName, productState, timestamp FROM AntiVirusProduct'],
    ['Battery', '', 'SELECT Name, EstimatedChargeRemaining, BatteryStatus FROM Win32_Battery'],
    ['Running processes', '', 'SELECT Name, ProcessId, WorkingSetSize, ExecutablePath FROM Win32_Process'],
  ];
  // What the last run brought back: its columns and their kinds, the rows (with the computer each came from), and
  // what each computer that didn't answer said. want is the Computers choice to make when the page opens next.
  const wq = { names: [], kinds: {}, rows: [], problems: [], cls: '', space: '', asked: false, want: '' };
  const dash = '<span class="muted">-</span>';
  // A column wide enough for its heading and what's in it, within reason.
  const wqWidth = (label, values) => Math.round(Math.min(320, Math.max(90, 7.4 * Math.max(label.length * 0.9, ...values.slice(0, 80).map((v) => String(v ?? '').length)) + 30)));
  let wqCols = [];
  const buildCols = () => {
    wqCols = [{ key: '@computer', label: 'Computer', w: 150, fixed: true, get: (r) => r['@computer'], cell: (r) => `<b>${esc(r['@computer'])}</b>` },
      ...wq.names.map((name) => {
        const number = wq.kinds[name] === 'number';
        return { key: name, label: name, w: wqWidth(name, wq.rows.map((r) => r[name])), on: true,
          get: (r) => (number && r[name] !== '' && r[name] != null ? Number(r[name]) : r[name] ?? ''), text: (r) => r[name] ?? '',
          cell: (r) => (r[name] === '' || r[name] == null ? dash : `<span class="ell">${esc(r[name])}</span>`) };
      })];
  };
  let wqG = null;
  const wqGrid = () => (wqG ||= R.grid({
    el: $('dv-wmi'), key: () => `wmi.${wq.cls || 'query'}`, cols: () => wqCols,
    row: (r) => ` data-row="wmi:${r['@i']}"`,
    empty: () => D.more.wmi.empty(),
    redraw: () => D.draw(),
  }));
  // Filter's fields: the computer, and each column by its kind; a column with few values offers them to pick.
  const wqFields = () => (!wq.names.length ? null : [
    ['Computer', [{ key: '@computer', label: 'Computer', type: 'choice', get: (r) => r['@computer'] }]],
    ['Properties', wq.names.map((name) => {
      const kind = wq.kinds[name];
      const type = kind === 'number' ? 'number' : kind === 'date' ? 'days' : kind === 'yes' || new Set(wq.rows.map((r) => r[name])).size <= 20 ? 'choice' : 'text';
      return { key: name, label: name, type, get: (r) => (r[name] === '' ? undefined : r[name]) };
    })],
  ]);
  D.lists.wmi = ['WMI query', 'list', 'Ask computers for anything Windows keeps in WMI: hardware, settings, services, security. Read only.'];
  D.more.wmi = { table: 'dv-wmi', grid: wqGrid, rows: () => wq.rows, count: () => '', can: () => has('seats.wmi'), noun: 'rows', fields: wqFields,
    load: async () => {}, // nothing to read again: a query runs when Run is clicked
    empty: () => (wq.asked ? 'No rows came back.' : 'Pick the computers, type a query or pick an example, then Run.') };

  // Computers: the ticked ones, every online one, or one by name.
  const wqTargets = () => {
    const v = $('wq-on').value;
    const approved = D.computers().filter((c) => c.approved);
    if (v === 'ticked') return approved.filter((c) => D.picked.has(c.id));
    if (v === 'online') return approved.filter((c) => D.stateOf(c) === 'online');
    return approved.filter((c) => `c:${c.id}` === v);
  };
  let wqOnHtml = '';
  const drawWqOn = () => {
    const approved = D.computers().filter((c) => c.approved).sort((a, b) => a.computer.localeCompare(b.computer));
    const ticked = approved.filter((c) => D.picked.has(c.id)).length;
    const online = approved.filter((c) => D.stateOf(c) === 'online').length;
    const html = (ticked ? `<option value="ticked">Ticked computers (${ticked})</option>` : '') + `<option value="online">Every online computer (${online})</option>`
      + (approved.length ? `<optgroup label="One computer">${approved.map((c) => `<option value="c:${esc(c.id)}">${esc(c.computer)}${D.stateOf(c) === 'online' ? '' : ' (offline)'}${below(c, WMI_FROM) ? ' (older client)' : ''}</option>`).join('')}</optgroup>` : '');
    const was = wq.want || $('wq-on').value;
    wq.want = '';
    // Drawn again only when what it offers changes, so an open list isn't shut by the page's next look.
    if (html !== wqOnHtml) {
      $('wq-on').innerHTML = html;
      wqOnHtml = html;
    }
    const values = [...$('wq-on').options].map((o) => o.value);
    $('wq-on').value = values.includes(was) ? was : ticked ? 'ticked' : 'online';
  };
  let wqAsking = 0;
  const runWmi = async () => {
    const on = wqTargets();
    const query = $('wq-text').value.trim();
    if (!query) {
      $('wq-text').focus({ preventScroll: true }); // Ctrl+Enter runs it from here
      toast('Type a query first.', 'Or choose one under Built-in queries.', { tone: 'warn' });
      return;
    }
    if (!on.length) {
      toast('No computers to ask.', 'Pick one under Computers, or tick some on the Computers list.', { tone: 'warn' });
      return;
    }
    const mine = ++wqAsking;
    const space = $('wq-space').value.trim() || 'root\\cimv2';
    $('wq-run').disabled = true;
    R.busy($('dv-lib'), `Asking ${names(on)}…`, 'Up to 40 seconds');
    try {
      const j = await send('/api/devices/wmi', { clients: on.map((c) => c.id), space, query });
      if (mine !== wqAsking) return;
      const heads = [];
      const kinds = {};
      const rows = [];
      for (const res of j.results) {
        if (!res.ok) continue;
        res.columns.forEach((c, i) => {
          if (!(c in kinds)) {
            heads.push(c);
            kinds[c] = res.kinds[i] || 'text';
          }
        });
        for (const row of res.rows) {
          const r = { '@computer': res.computer, '@client': res.client, '@i': rows.length };
          res.columns.forEach((c, i) => { r[c] = row[i]; });
          rows.push(r);
        }
      }
      const cls = (/\bfrom\s+([A-Za-z0-9_]+)/i.exec(query) || /\bof\s*\{\s*([A-Za-z0-9_]+)/i.exec(query) || [])[1] || '';
      Object.assign(wq, { names: heads, kinds, rows, cls, space, asked: true,
        problems: j.results.filter((r) => !r.ok || r.more).map((r) => [r.computer || 'A computer', r.ok ? `Sent the first ${r.rows.length.toLocaleString()} rows, and had more. Ask for less with a WHERE.` : r.error]) });
      buildCols();
      if (panel.is('devices') && String(panel.key).startsWith('wmi:')) panel.close();
    } catch (e) {
      if (mine === wqAsking) oops(e);
    } finally {
      if (mine === wqAsking) {
        R.busy($('dv-lib'), '');
        $('wq-run').disabled = false;
      }
    }
    D.draw();
  };
  // A built-in query fills the boxes; Run runs it. It stays picked until the query is changed by hand.
  $('wq-examples').innerHTML = '<option value="" selected>Choose one</option>' + WQ_EXAMPLES.map(([label], i) => `<option value="${i}">${esc(label)}</option>`).join('');
  $('wq-examples').addEventListener('change', () => {
    const ex = WQ_EXAMPLES[Number($('wq-examples').value)];
    if (!ex) return;
    $('wq-space').value = ex[1] || 'root\\cimv2';
    $('wq-text').value = ex[2];
    $('wq-text').focus({ preventScroll: true }); // Ctrl+Enter runs it from here
  });
  for (const id of ['wq-text', 'wq-space']) $(id).addEventListener('input', () => { $('wq-examples').value = ''; });
  $('wq-run').addEventListener('click', runWmi);
  $('wq-text').addEventListener('keydown', (e) => {
    if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) {
      e.preventDefault();
      runWmi();
    }
  });
  // One row: every property it came back with, in the side panel.
  const openWmiRow = (i) => {
    const r = wq.rows[i];
    if (!r) return;
    panel.body.innerHTML = head('list', r['@computer'], esc(`${wq.cls || 'WMI query'} · ${wq.space}`))
      + section(`Properties <small>${wq.names.length}</small>`, `<ul class="does wide">${wq.names.map((n) => `<li><b>${esc(n)}</b><span>${r[n] === '' || r[n] == null ? dash : esc(r[n])}</span></li>`).join('')}</ul>`);
    panel.open('devices', `wmi:${i}`);
    mark();
  };

  // The class list: the namespaces down the left, the classes on the right, read from one of the computers picked.
  // A class picked becomes SELECT * FROM it, and runs.
  const wqc = { space: '', spaces: [], classes: [] };
  let wqcAsking = 0;
  const openClasses = async (space) => {
    const on = wqTargets().filter((c) => !below(c, WMI_FROM));
    const c = on.find((x) => D.stateOf(x) === 'online') || on[0];
    if (!c) {
      toast('Pick a computer first.', `The class list is read from one of them, with client ${WMI_FROM} or later.`, { tone: 'warn' });
      return;
    }
    const mine = ++wqcAsking;
    if (!$('wqc-dialog').open) {
      $('wqc-find').value = '';
      $('wqc-dialog').showModal();
      $('wqc-find').focus();
    }
    $('wqc-lead').textContent = `Read from ${c.computer}. Pick a class to see everything it has.`;
    $('wqc-list').innerHTML = `<p class="muted">Reading the classes in ${esc(space)}…</p>`;
    $('wqc-count').textContent = '';
    try {
      const j = await send('/api/devices/wmi/classes', { clients: [c.id], space });
      if (mine !== wqcAsking) return;
      Object.assign(wqc, { space: j.space, spaces: j.spaces, classes: j.classes });
      drawSpaces();
      drawClassList();
    } catch (e) {
      if (mine === wqcAsking) $('wqc-list').innerHTML = `<p class="muted">${esc(e.message)}</p>`;
    }
  };
  const drawSpaces = () => {
    const parts = wqc.space.split('\\');
    const up = parts.map((_, i) => parts.slice(0, i + 1).join('\\'));
    $('wqc-spaces').innerHTML = '<p class="rail-label">Namespace</p>'
      + up.map((s, i) => `<a data-space="${esc(s)}"${i === up.length - 1 ? ' aria-current="page"' : ''}>${svg('folder')}<span class="nm">${esc(i ? parts[i] : s)}</span></a>`).join('')
      + (wqc.spaces.length ? `<div class="dkids open">${wqc.spaces.map((s) => `<a data-space="${esc(s)}"><span class="nm">${esc(s.split('\\').pop())}</span></a>`).join('')}</div>` : '');
  };
  const drawClassList = () => {
    const find = $('wqc-find').value.trim().toLowerCase();
    const list = wqc.classes.filter((c) => !find || c.toLowerCase().includes(find));
    const shown = list.slice(0, 500);
    $('wqc-list').innerHTML = (shown.map((c) => `<button type="button" data-class="${esc(c)}">${esc(c)}</button>`).join('')
      + (list.length > shown.length ? `<p class="muted">And ${(list.length - shown.length).toLocaleString()} more. Find narrows the list.</p>` : ''))
      || `<p class="muted">${wqc.classes.length ? 'No class has that in its name.' : 'No classes in this namespace.'}</p>`;
    $('wqc-count').textContent = `${list.length.toLocaleString()} of ${wqc.classes.length.toLocaleString()} classes`;
  };
  $('wq-classes').addEventListener('click', () => openClasses($('wq-space').value.trim() || 'root\\cimv2'));
  $('wqc-spaces').addEventListener('click', (e) => {
    const a = e.target.closest('[data-space]');
    if (a) openClasses(a.dataset.space);
  });
  $('wqc-find').addEventListener('input', drawClassList);
  $('wqc-list').addEventListener('click', (e) => {
    const b = e.target.closest('[data-class]');
    if (!b) return;
    $('wq-space').value = wqc.space;
    $('wq-text').value = `SELECT * FROM ${b.dataset.class}`;
    $('wqc-dialog').close();
    runWmi();
  });
  for (const id of ['wqc-x', 'wqc-cancel']) $(id).addEventListener('click', () => $('wqc-dialog').close());

  // --- Ticks, and what Actions does with them ------------------------------------------------------------------
  const ticked = () => D.computers().filter((c) => D.picked.has(c.id));
  D.hooks.lead = () => (canPick() ? { w: 18, head: '<label class="tk"><input type="checkbox" data-tick-all aria-label="Select all"></label>',
    cell: (c) => (c.server ? '' : `<label class="tk"><input type="checkbox" data-tick="${esc(c.id)}" aria-label="Select ${esc(c.computer)}"></label>`) } : null);
  // The ticks, each row's highlight and the count on Actions, without drawing the list again.
  const drawPicked = () => {
    const n = D.picked.size;
    const act = $('dv-act');
    act.hidden = !canPick();
    act.disabled = !n;
    act.querySelector('.cnt').hidden = !n;
    act.querySelector('.cnt').textContent = n.toLocaleString();
    act.dataset.tip = n ? `${many(n)} ticked` : 'Tick computers first';
    // Computers and Windows updates' own row per computer share the ticks.
    for (const table of TICKED.map((id) => $(id))) {
      const boxes = [...table.querySelectorAll('[data-tick]')];
      boxes.forEach((box) => {
        box.checked = D.picked.has(box.dataset.tick);
        box.closest('.tr')?.classList.toggle('picked', box.checked);
      });
      const all = table.querySelector('[data-tick-all]');
      if (all) {
        const on = boxes.filter((b) => b.checked).length;
        all.checked = !!boxes.length && on === boxes.length;
        all.indeterminate = on > 0 && on < boxes.length;
      }
    }
  };
  // Shift-click ticks every row between the last one ticked and this one.
  let lastTick = null;
  const TICKED = ['seats-table', 'dv-updpc'];
  for (const id of TICKED) {
    $(id).addEventListener('click', (e) => {
      const box = e.target.closest('.tk')?.querySelector('input');
      if (!box || e.target !== box) return; // the label's own click comes through the box
      const boxes = [...$(id).querySelectorAll('[data-tick]')];
      const set = (b) => (box.checked ? D.picked.add(b.dataset.tick) : D.picked.delete(b.dataset.tick));
      if (box.dataset.tickAll !== undefined) boxes.forEach(set);
      else {
        const i = boxes.indexOf(box);
        const from = e.shiftKey && lastTick !== null ? Math.min(lastTick, i) : i;
        const to = e.shiftKey && lastTick !== null ? Math.max(lastTick, i) : i;
        boxes.slice(from, to + 1).forEach(set);
        lastTick = i;
      }
      drawPicked();
    });
  }
  const menu = $('dv-act-menu');
  // What can be done on computers, from this one list: the Actions menu for the ticked ones, and the buttons at the
  // top of a computer's panel (printers.js adds Add a printer through actItems).
  // Two groups: the computer and who's on it (lock, sign out, restart, shut down, wake), then what runs on it.
  const doItems = (item, on) => {
    if (!on.some((c) => c.approved)) return ['', ''];
    const off = on.filter((c) => c.approved && !c.online && !c.server).length;
    const acts = has('seats.actions');
    return [acts ? item('lock', 'lock', 'Lock…') + item('signout', 'logout', 'Sign everyone out…') + item('restart', 'restart', 'Restart…') + item('shutdown', 'power', 'Shut down…')
      + item('wake', 'wake', 'Wake', String(off || '')) : '',
    (canScripts() ? item('script', 'code', 'Run a script…') : '') + (acts ? item('updates', 'updates', 'Install Windows updates…') + item('message', 'message', 'Send a message…') : '')
      + (has('seats.wmi') ? item('wmi', 'list', 'Query with WMI…') : '') + (D.hooks.actItems?.(item) || '')];
  };
  // One of those, on these computers: from the menu, or from one computer's panel.
  const doAct = async (act, on, fromPanel = false) => {
    if (act === 'wake') {
      // The offline ones; when none are, all of them (one that just went to sleep still shows online for a few minutes).
      const can = on.filter((c) => c.approved && !c.server);
      const off = can.filter((c) => !c.online);
      await wake(off.length ? off : can);
    } else if (act === 'wmi') {
      wq.want = fromPanel ? `c:${on[0].id}` : 'ticked';
      location.hash = '#seats/wmi';
    } else if (D.hooks.act?.(act, on)) {
      // done by another part of the page (printers.js)
    } else openRun(act, on);
  };
  // The Actions menu: for the ticked computers from the toolbar, or for one computer from its panel.
  let menuOn = []; // the computers the open menu is for
  let menuPanel = false; // opened from a computer's panel
  const openActs = (button, on, inPanel) => {
    menuOn = on;
    menuPanel = inPanel;
    const waiting = on.filter((c) => !c.approved).length;
    const item = (act, icon, label, hint = '', danger = false) => `<button type="button" role="menuitem" class="am-link${danger ? ' out' : ''}" data-dv-act="${act}">${svg(icon)}<span>${esc(label)}</span>${hint ? `<small>${esc(hint)}</small>` : ''}</button>`;
    const groups = [
      ...doItems(item, on),
      inPanel ? '' : (has('seats.approve') && waiting ? item('approve', 'check', 'Approve', String(waiting)) : '') + item('export', 'csv', 'Export these') + item('clear', 'x', 'Clear the ticks'),
      has('seats.remove') ? item('remove', 'trash', 'Remove from Devices…', '', true) : '',
    ];
    menu.innerHTML = groups.filter(Boolean).map((g) => `<div class="am-links">${g}</div>`).join('');
    const r = button.getBoundingClientRect();
    menu.style.cssText = `position:fixed;inset:auto;width:250px;margin:0;left:${inPanel ? Math.min(r.left, innerWidth - 258) : Math.max(8, r.right - 250)}px;top:${r.bottom + 6}px`;
    menu.showPopover();
    // Too tall for under the button: it opens above it.
    if (r.bottom + 6 + menu.offsetHeight > innerHeight - 8) menu.style.top = `${Math.max(8, r.top - 6 - menu.offsetHeight)}px`;
  };
  $('dv-act').addEventListener('click', () => openActs($('dv-act'), ticked(), false));
  // A computer's panel has the one button; what it opens is the same menu.
  D.hooks.seatBar = (c) => (c.server || (!doItems(() => 'x', [c]).join('') && !has('seats.remove')) ? ''
    : `<button type="button" class="small dw-acts" data-dv-menu aria-haspopup="menu">${svg('menu')}Actions${svg('chevron-down', 'chev')}</button>`);
  const each = async (list, path) => {
    let failed = 0;
    for (const c of list) {
      try {
        await send(`/api/clients/${encodeURIComponent(c.id)}/${path}`);
      } catch {
        failed++;
      }
    }
    D.reload();
    return failed;
  };
  menu.addEventListener('click', async (e) => {
    const b = e.target.closest('[data-dv-act]');
    if (!b) return;
    menu.hidePopover();
    const on = menuOn;
    const act = b.dataset.dvAct;
    if (act === 'clear') {
      D.picked.clear();
      drawPicked();
    } else if (act === 'export') D.grid().csv(D.grid().sorted(on), 'devices-ticked');
    else if (act === 'approve') {
      const list = on.filter((c) => !c.approved);
      const failed = await each(list, 'approve');
      toast(failed ? `${failed} of ${list.length} couldn't be approved.` : `Approved ${many(list.length)}.`, '', failed ? { tone: 'warn' } : {});
    } else if (act === 'remove') {
      const one = on.length === 1;
      sure(one ? `Remove ${on[0].computer}?` : `Remove ${many(on.length)}?`, one ? 'Its client asks to join again and waits for approval.' : `${names(on)}. Each client asks to join again and waits for approval.`, 'Remove', async () => {
        const failed = await each(on, 'remove');
        if (menuPanel) panel.close();
        else D.picked.clear();
        toast(failed ? `${failed} of ${on.length} couldn't be removed.` : `Removed ${one ? on[0].computer : many(on.length)}.`, '', failed ? { tone: 'warn' } : {});
      });
    } else await doAct(act, on, menuPanel);
  });

  // --- Running something on computers ---------------------------------------------------------------------------
  let run = null; // { kind, on, lang } while the window is open
  // Each kind's words in the run window: its title, its button, what it does, the toast once sent, and whether it's
  // for now (an offline computer gets it within 15 minutes, not a day).
  const KINDS = {
    script: { title: () => 'Run a script', go: 'Run', sent: 'Script sent.', note: 'It runs as SYSTEM. Who ran it, the script itself and what it wrote are kept under Jobs.' },
    updates: { title: () => 'Install Windows updates', go: 'Install', sent: 'Updates started.', note: "Every missing update is downloaded and installed. The computer isn't restarted: one that needs it shows under Needs restart." },
    check: { title: () => 'Check for updates', go: 'Check', sent: 'Checking for updates.', note: 'Each computer asks Windows Update what it is missing. Windows updates here shows the answer within a few minutes.' },
    restart: { title: (n) => `Restart ${many(n)}?`, go: 'Restart', sent: 'Restart sent.', note: 'Anyone signed in gets a five minute warning first.' },
    shutdown: { title: (n) => `Shut down ${many(n)}?`, go: 'Shut down', sent: 'Shut down sent.', now: true, note: 'Anyone signed in gets a five minute warning first.' },
    lock: { title: (n) => `Lock ${many(n)}?`, go: 'Lock', sent: 'Lock sent.', now: true, note: 'Everyone signed in goes back to the sign-in screen. Their apps keep running, and getting back in takes their password.' },
    signout: { title: (n) => `Sign everyone out of ${many(n)}?`, go: 'Sign out', sent: 'Sign out sent.', now: true, note: 'Everyone signed in is signed out straight away. Anything they had not saved is lost.' },
    message: { title: () => 'Send a message', go: 'Send', sent: 'Message sent.', now: true, note: 'It pops up for everyone signed in, until they close it or an hour passes.' },
  };
  const kindOf = (kind) => KINDS[kind] || KINDS.script;
  const runNote = () => {
    const off = run.on.filter((c) => D.stateOf(c) === 'offline').length;
    const old = run.on.filter(older).length;
    const base = kindOf(run.kind).note;
    const within = kindOf(run.kind).now ? 'within 15 minutes' : 'within a day';
    const all = run.on.length;
    return base + (!off ? '' : off < all ? ` ${off} ${off === 1 ? 'is offline and gets' : 'are offline and get'} it once back, ${within}.`
      : all === 1 ? ` It's offline and gets it once it's back, ${within}.` : ` They're offline and get it once back, ${within}.`)
      + (!old ? '' : old === all && all === 1 ? ` Its client is older than ${NEEDS} and gets it after updating.`
        : ` ${old} ${old === 1 ? 'has' : 'have'} a client older than ${NEEDS} and ${old === 1 ? 'gets' : 'get'} it after updating.`);
  };
  const drawRun = () => {
    const script = run.kind === 'script';
    const picked = $('run-script').value;
    const typed = script && !picked && has('seats.scripts.edit');
    for (const id of ['run-script', 'run-script-label']) $(id).hidden = !script;
    $('run-typed').hidden = !typed;
    $('run-preview').hidden = !(script && picked);
    $('run-preview').textContent = script && picked ? scriptOf(picked)?.text || '' : '';
    $('run-copy').hidden = !(script && picked && has('seats.scripts.edit'));
    $('run-save-row').hidden = !typed;
    $('run-test').hidden = !script || (!picked && !typed);
    const message = run.kind === 'message';
    for (const id of ['run-message', 'run-message-label']) $(id).hidden = !message;
    $('run-form').hidden = !script && !message && run.on.length <= BRAKE;
    $('run-go').disabled = (script && !picked && !typed) || (message && !$('run-message').value.trim());
    pressed($('run-lang'), 'lang', run.lang);
    $('run-text').placeholder = run.lang === 'cmd' ? 'ipconfig /flushdns' : 'Get-Service Spooler';
  };
  // The scripts to pick: the library's own, then the built-in ones, then one typed in now.
  const fillScripts = (selected = '') => {
    const opt = (s) => `<option value="${esc(s.id)}"${s.id === selected ? ' selected' : ''}>${esc(s.name)} (${LANG[s.language]})</option>`;
    const mine = (scripts || []).filter((s) => !s.builtIn);
    const built = (scripts || []).filter((s) => s.builtIn);
    $('run-script').innerHTML = (has('seats.scripts.run') ? (mine.length ? `<optgroup label="Your scripts">${mine.map(opt).join('')}</optgroup>` : '')
      + (built.length ? `<optgroup label="Built-in">${built.map(opt).join('')}</optgroup>` : '') : '')
      + (has('seats.scripts.edit') ? `<option value=""${selected === null ? ' selected' : ''}>Type one now</option>` : '') || '<option value="" disabled selected>The library has no scripts yet</option>';
  };
  const openRun = async (kind, on, preset = '') => {
    const list = on.filter((c) => c.approved);
    if (!list.length) {
      toast('Nothing to run it on.', 'Pick computers that are approved.', { tone: 'warn' });
      return;
    }
    if (kind === 'script') {
      try {
        scripts = await get('/api/devices/scripts');
      } catch (e) {
        oops(e);
        return;
      }
    }
    run = { kind, on: list, lang: 'powershell' };
    const n = list.length;
    $('run-title').textContent = kindOf(kind).title(n);
    $('run-message').value = '';
    $('run-lead').textContent = `On ${names(list)}.`;
    fillScripts(preset);
    $('run-text').value = '';
    // Test on one of them first: online ones first.
    $('run-test-on').innerHTML = [...list].sort((a, b) => (D.stateOf(a) === 'online' ? 0 : 1) - (D.stateOf(b) === 'online' ? 0 : 1))
      .map((c) => `<option value="${esc(c.id)}">${esc(c.computer)}${D.stateOf(c) === 'online' ? '' : ' (offline)'}</option>`).join('');
    $('run-test-out').hidden = true;
    $('run-save-form').hidden = true;
    $('run-save').hidden = false;
    testing = null;
    $('run-count').value = '';
    for (const id of ['run-count', 'run-count-label']) $(id).hidden = n <= BRAKE;
    $('run-count-label').textContent = `That's ${n} computers. Type ${n} to go ahead`;
    $('run-go').textContent = kindOf(kind).go;
    $('run-note').textContent = runNote();
    say('run-problem', '');
    drawRun();
    $('run-dialog').showModal();
    if (kind === 'message') $('run-message').focus();
  };
  $('run-script').addEventListener('change', drawRun);
  $('run-message').addEventListener('input', drawRun);
  $('run-lang').addEventListener('click', (e) => {
    const b = e.target.closest('button');
    if (!b) return;
    run.lang = b.dataset.lang;
    drawRun();
  });
  $('run-cancel').addEventListener('click', () => $('run-dialog').close());
  // A library or built-in script as the start of one typed in now.
  $('run-copy').addEventListener('click', () => {
    const s = scriptOf($('run-script').value);
    if (!s) return;
    $('run-script').value = '';
    run.lang = s.language;
    $('run-text').value = s.text.replace(/\r\n/g, '\n');
    drawRun();
    $('run-text').focus();
  });
  // What was typed, kept in the library; it's then the script picked.
  $('run-save').addEventListener('click', () => {
    $('run-save').hidden = true;
    $('run-save-form').hidden = false;
    $('run-save-name').value = '';
    $('run-save-name').focus();
  });
  const saveTyped = async () => {
    const name = $('run-save-name').value.trim();
    if (!name) {
      $('run-save-name').focus();
      return;
    }
    $('run-save-go').disabled = true;
    try {
      const made = await confirmed(() => send('/api/devices/scripts', { name, language: run.lang, text: $('run-text').value, minutes: 10 }));
      scripts = await get('/api/devices/scripts');
      fillScripts(made.id);
      $('run-save-form').hidden = true;
      $('run-save').hidden = false;
      say('run-problem', '');
      drawRun();
      toast('Saved to the library.', `${name} is under Scripts.`);
      if (D.at() === 'scripts') D.reload();
    } catch (e) {
      if (!e.cancelled) say('run-problem', e.message);
    }
    $('run-save-go').disabled = false;
  };
  $('run-save-go').addEventListener('click', saveTyped);
  $('run-save-name').addEventListener('keydown', (e) => { if (e.key === 'Enter') { e.preventDefault(); saveTyped(); } });

  // Test: the script on one computer, with what it wrote shown here, before it goes to all of them.
  let testing = null; // the test job being watched
  const showTest = (text, bad = false) => {
    $('run-test-out').hidden = false;
    $('run-test-out').textContent = text;
    $('run-test-out').classList.toggle('bad', bad);
  };
  $('run-test-go').addEventListener('click', async () => {
    const c = run.on.find((x) => x.id === $('run-test-on').value);
    if (!c) return;
    const body = { kind: 'script', clients: [c.id] };
    if ($('run-script').value) body.script = $('run-script').value;
    else Object.assign(body, { language: run.lang, text: $('run-text').value });
    $('run-test-go').disabled = true;
    try {
      const made = await confirmed(() => send('/api/devices/run', body));
      testing = made.id;
      say('run-problem', '');
      showTest(`Sent to ${c.computer}. Waiting for it to pick it up…`);
      const id = made.id;
      const until = Date.now() + 6 * 60000;
      while (testing === id && $('run-dialog').open && Date.now() < until) {
        await new Promise((r) => setTimeout(r, 1500));
        let j;
        try {
          j = await get(`/api/devices/jobs/${encodeURIComponent(id)}`);
        } catch {
          continue;
        }
        const t = j.targets.find((x) => x.client === c.id);
        if (!t) break;
        if (t.state === 'running') showTest(`Running on ${c.computer}…`);
        if (t.state === 'waiting' || t.state === 'running') continue;
        const out = await get(`/api/devices/jobs/${encodeURIComponent(id)}/output/${encodeURIComponent(c.id)}`).catch(() => ({ output: [] }));
        const step = t.steps[0] || {};
        const head = [`${c.computer}: ${(STATE[t.state] || ['', t.state])[1]}`, step.exit != null ? `exit ${step.exit}` : '', step.seconds != null ? secs(step.seconds) : '', t.problem || ''].filter(Boolean).join(' · ');
        showTest(`${head}\n\n${(out.output || []).join('\n').trim() || 'It wrote nothing.'}`, t.state !== 'done' || (step.exit != null && step.exit !== 0));
        break;
      }
    } catch (e) {
      if (!e.cancelled) say('run-problem', e.message);
    }
    $('run-test-go').disabled = false;
  });
  $('run-dialog').addEventListener('close', () => { testing = null; });
  $('run-go').addEventListener('click', async () => {
    const body = { kind: run.kind, clients: run.on.map((c) => c.id) };
    if (run.kind === 'script') {
      if ($('run-script').value) body.script = $('run-script').value;
      else Object.assign(body, { language: run.lang, text: $('run-text').value });
    }
    if (run.kind === 'message') body.text = $('run-message').value;
    if (run.on.length > BRAKE) body.count = Number($('run-count').value) || 0;
    $('run-go').disabled = true;
    try {
      const made = await confirmed(() => send('/api/devices/run', body));
      $('run-dialog').close();
      toast(kindOf(run.kind).sent, `On ${names(run.on)}. It's under Jobs.`);
      await openJob(made.id);
      if (D.at() === 'jobs') D.reload();
    } catch (e) {
      if (!e.cancelled) say('run-problem', e.message);
    }
    $('run-go').disabled = false;
  });

  // --- One job in the side panel: each computer, and what each step wrote there ----------------------------------
  let job = null; // the one that's open, as /api/devices/jobs/{id} gave it
  let jobSeen = ''; // and as text, so the panel is only drawn again when something changed
  const outputs = {}; // 'job:client' -> what each step wrote
  const wanted = new Set(); // 'job:client' whose output was asked for
  const secs = (s) => (s < 60 ? `${Math.round(s * 10) / 10} s` : `${Math.floor(s / 60)} min ${Math.round(s % 60)} s`);
  const drawJob = () => {
    const waiting = job.targets.filter((t) => t.state === 'waiting').length;
    const bar = waiting ? `<div class="dw-bar">${qa('data-job-cancel', 'x', `Cancel for the ${waiting === job.targets.length ? '' : `${waiting} `}computer${waiting === 1 ? '' : 's'} still waiting`)}</div>` : '';
    const targets = job.targets.map((t) => {
      const c = D.computers().find((x) => x.id === t.client);
      const why = t.problem || (t.state !== 'waiting' ? '' : c && older(c) ? `Its client is older than ${NEEDS}. It gets this once it has updated.`
        : c && D.stateOf(c) === 'offline' ? `It's offline. It gets this when it's back, until ${when(job.expires)}.` : 'Its client takes it in a few seconds.');
      const out = outputs[`${job.id}:${t.client}`];
      const steps = t.steps.map((s, i) => `<p class="job-step"><b>${esc(job.steps[i]?.title || `Step ${i + 1}`)}</b><small>${s.timedOut ? 'stopped' : `exit code ${s.exit ?? 'unknown'}`} · ${secs(s.seconds)}</small></p>`
        + (out ? `<pre class="code-box">${esc(out[i] || '(it wrote nothing)')}</pre>` : '')).join('');
      const ask = t.steps.length && !out ? `<button type="button" class="small" data-job-out="${esc(t.client)}">${svg('eye')}Show output</button>` : '';
      return section(esc(t.computer), (why ? `<p class="dw-how">${esc(why)}</p>` : '') + steps + ask, pill(STATE[t.state] || ['offline', t.state]));
    }).join('');
    const what = job.steps.map((s) => `<details class="job-what"><summary><b>${esc(s.title)}</b><small>${LANG[s.language] || s.language} · time limit ${s.minutes} min</small></summary><pre class="code-box">${esc(s.text)}</pre></details>`).join('');
    panel.body.innerHTML = head(JOB_ICON[job.kind] || 'code', jobTitle(job), esc(`${when(job.at)} · by ${job.by}`)) + bar + targets + section('What ran', what);
  };
  // What a computer wrote is asked for once it has answered: for a few computers by itself, for many when it's clicked.
  const loadOutputs = () => {
    const few = job.targets.length <= 5;
    for (const t of job.targets) {
      const key = `${job.id}:${t.client}`;
      if (!t.steps.length || outputs[key] || (!few && !wanted.has(key))) continue;
      wanted.delete(key);
      outputs[key] = []; // asked for
      const id = job.id;
      get(`/api/devices/jobs/${id}/output/${encodeURIComponent(t.client)}`).then((got) => {
        outputs[key] = got.output;
        if (job?.id === id && panel.shows('devices', `job:${id}`)) drawJob();
      }).catch(() => { delete outputs[key]; });
    }
  };
  const jobOpen = () => job && panel.shows('devices', `job:${job.id}`);
  const readJob = async (id) => {
    const got = await get(`/api/devices/jobs/${id}`);
    const text = JSON.stringify(got);
    if (job?.id === id && text === jobSeen) return false;
    [job, jobSeen] = [got, text];
    return true;
  };
  // While something is waiting or running: the open job, and the Jobs list when it shows, read every three seconds.
  // It stops by itself when nothing is, when the page is hidden, and after ten minutes.
  let watching = null;
  let watched = 0;
  const busyNow = () => (jobOpen() && job.targets.some((t) => t.state === 'waiting' || t.state === 'running')) || (D.at() === 'jobs' && R.where()[0] === 'seats' && (jobs || []).some(open));
  const watch = () => {
    if (watching || !busyNow()) return;
    watched = 0;
    watching = setInterval(async () => {
      watched += 3;
      if (!busyNow() || document.hidden || watched > 600) {
        clearInterval(watching);
        watching = null;
        return;
      }
      try {
        if (jobOpen() && await readJob(job.id) && jobOpen()) {
          drawJob();
          loadOutputs();
        }
        if (D.at() === 'jobs' && R.where()[0] === 'seats') {
          jobs = await get('/api/devices/jobs');
          D.draw();
        }
      } catch {
        // Read again in three seconds.
      }
    }, 3000);
  };
  const openJob = async (id) => {
    try {
      jobSeen = '';
      await readJob(id);
    } catch (e) {
      oops(e);
      return;
    }
    drawJob();
    panel.open('devices', `job:${id}`);
    mark();
    loadOutputs();
    watch();
  };

  // --- A script: in the side panel, and the window that writes one ---------------------------------------------
  let shown = null; // the script or workflow open in the side panel
  const openScript = (s) => {
    shown = s;
    panel.body.innerHTML = head('code', s.name, esc(`${LANG[s.language]} · time limit ${s.minutes} minutes`))
      + `<div class="dw-bar">${has('seats.scripts.run') ? qa('data-sc-run', 'right', 'Run…') : ''}${has('seats.scripts.edit') ? (s.builtIn ? '' : qa('data-sc-edit', 'edit', 'Edit')) + qa('data-sc-copy', 'copy', 'Duplicate') + (s.builtIn ? '' : qa('data-sc-del', 'trash', 'Delete')) : ''}</div>`
      + (s.builtIn ? `<p class="dw-how">Built into ${esc(R.app())}. Duplicate it to make one of your own to change.</p>` : '')
      + (s.about ? section('About', `<p class="dw-how">${esc(s.about)}</p>`) : '')
      + section('Script', `<pre class="code-box">${esc(s.text)}</pre>`)
      + (s.workflows.length ? section(`Workflows that run it <small>${s.workflows.length}</small>`, `<div class="dw-list">${s.workflows.map((w) => `<div class="mrow"><span class="pic small">${svg('org')}</span><div class="two"><b>${esc(w)}</b></div></div>`).join('')}</div>`) : '')
      + section('Changed', facts([['When', when(s.changed)], s.by && ['By', s.by]]));
    panel.open('devices', `script:${s.id}`);
    mark();
  };
  // A new script can start from a built-in one (BuiltInScripts.cs): things people run on computers all the time.
  const builtIns = () => (scripts || []).filter((s) => s.builtIn);
  let editing = { id: null, lang: 'powershell' };
  $('sc-min').innerHTML = `${R.stepper('id="sc-minutes" min="1" max="240" value="10" aria-label="Minutes"')}<span>minutes</span>`;
  const openScriptEdit = async (s, copy = false) => {
    if (!scripts) {
      try {
        scripts = await get('/api/devices/scripts');
      } catch (e) {
        oops(e);
        return;
      }
    }
    editing = { id: s && !copy ? s.id : null, lang: s?.language || 'powershell' };
    $('sc-title').textContent = editing.id ? 'Edit script' : 'New script';
    $('sc-name').value = s ? (copy ? `${s.name} (copy)`.slice(0, 80) : s.name) : '';
    $('sc-about').value = s?.about || '';
    $('sc-minutes').value = s?.minutes || 10;
    $('sc-text').value = s?.text || '';
    $('sc-start-row').hidden = !!s;
    $('sc-start').innerHTML = `<option value="">An empty script</option>${builtIns().map((x) => `<option value="${esc(x.id)}">${esc(x.name)} (${LANG[x.language]})</option>`).join('')}`;
    pressed($('sc-lang'), 'lang', editing.lang);
    say('sc-problem', '');
    $('script-dialog').showModal();
    $('sc-name').focus();
  };
  $('sc-lang').addEventListener('click', (e) => {
    const b = e.target.closest('button');
    if (!b) return;
    editing.lang = b.dataset.lang;
    pressed($('sc-lang'), 'lang', editing.lang);
  });
  $('sc-start').addEventListener('change', () => {
    const x = builtIns().find((s) => s.id === $('sc-start').value);
    if (!x) return;
    [$('sc-name').value, editing.lang, $('sc-about').value, $('sc-text').value, $('sc-minutes').value] = [x.name, x.language, x.about || '', x.text, x.minutes];
    pressed($('sc-lang'), 'lang', editing.lang);
  });
  // Tab indents in a script; Escape still leaves the window.
  for (const id of ['sc-text', 'run-text']) {
    $(id).addEventListener('keydown', (e) => {
      if (e.key !== 'Tab' || e.shiftKey || e.ctrlKey || e.altKey) return;
      e.preventDefault();
      e.target.setRangeText('    ', e.target.selectionStart, e.target.selectionEnd, 'end');
    });
  }
  $('sc-cancel').addEventListener('click', () => $('script-dialog').close());
  $('sc-save').addEventListener('click', async () => {
    const form = { id: editing.id, name: $('sc-name').value, about: $('sc-about').value, language: editing.lang, text: $('sc-text').value, minutes: Number($('sc-minutes').value) || 10 };
    $('sc-save').disabled = true;
    try {
      const made = await confirmed(() => send('/api/devices/scripts', form));
      $('script-dialog').close();
      scripts = await get('/api/devices/scripts');
      D.draw();
      toast(editing.id ? 'Script saved.' : 'Script added.', form.name.trim());
      if (panel.is('devices') && scriptOf(made.id)) openScript(scriptOf(made.id));
    } catch (e) {
      if (!e.cancelled) say('sc-problem', e.message);
    }
    $('sc-save').disabled = false;
  });

  // --- A workflow: in the side panel, and the window that makes one ---------------------------------------------
  const openFlow = (w) => {
    shown = w;
    const row = (k, v, icon) => `<div class="mrow"><span class="pic small">${svg(icon)}</span><div class="two"><small>${esc(k)}</small><b>${esc(v)}</b></div></div>`;
    panel.body.innerHTML = head('org', w.name, pill(w.on ? ['online', 'Enabled'] : ['offline', 'Disabled']))
      + `<div class="dw-bar">${qa('data-fl-toggle', 'power', w.on ? 'Disable' : 'Enable')}${qa('data-fl-edit', 'edit', 'Edit')}${qa('data-fl-run', 'right', 'Run now')}${qa('data-fl-del', 'trash', 'Delete')}</div>`
      + section('Steps', `<div class="dw-list seat-list fl-view">${row('When', whenText(w), 'clock')}${ifText(w) ? row('Only on', ifText(w), 'filter') : ''}${w.steps.map((s, i) => row(i ? 'Then' : 'Do', stepText(s), 'right')).join('')}</div>`)
      + section('Last run', w.lastRun ? facts([['When', when(w.lastRun)]]) + (w.lastJob ? `<div class="seat-acts"><button type="button" class="small" data-job-open="${esc(w.lastJob)}">${svg('history')}Open its job</button></div>` : '') : none("It hasn't run yet."))
      + section('Changed', facts([['When', when(w.changed)], w.by && ['By', w.by]]));
    panel.open('devices', `flow:${w.id}`);
    mark();
  };
  let flow = null; // the one being made or changed
  $('fl-when').innerHTML = WHEN.map(([k, label]) => `<option value="${k}">${esc(label)}</option>`).join('');
  $('fl-if').innerHTML = IF.map(([k, label]) => `<option value="${k}">${esc(label)}</option>`).join('');
  $('fl-hour').innerHTML = Array.from({ length: 24 }, (_, h) => `<option value="${String(h).padStart(2, '0')}">${esc(new Date(2000, 0, 1, h).toLocaleTimeString([], { hour: 'numeric' }))}</option>`).join('');
  $('fl-minute').innerHTML = ['00', '15', '30', '45'].map((m) => `<option value="${m}">:${m}</option>`).join('');
  $('fl-days').innerHTML = [1, 2, 3, 4, 5, 6, 0].map((d) => `<button type="button" data-day="${d}" aria-pressed="false">${DAYS[d]}</button>`).join('');
  $('fl-below-box').innerHTML = `${R.stepper('id="fl-below" min="1" max="90" value="10" aria-label="Percent"')}<span>% free</span>`;
  const drawFlow = () => {
    $('fl-time-row').hidden = $('fl-days-row').hidden = flow.when !== 'schedule';
    $('fl-below-row').hidden = flow.when !== 'disk';
    const words = ['name', 'windows', 'notwindows'].includes(flow.if);
    $('fl-if-row').hidden = !words;
    $('fl-if-label').textContent = flow.if === 'name' ? 'Name, with * for anything' : 'Words';
    $('fl-if-text').placeholder = flow.if === 'name' ? 'SHOP-*' : 'Server';
    $('fl-days').querySelectorAll('button').forEach((b) => b.setAttribute('aria-pressed', String(flow.days.includes(Number(b.dataset.day)))));
    const last = flow.steps.length - 1;
    $('fl-steps').innerHTML = flow.steps.map((s, i) => `<div class="mrow"><span class="pic small">${i + 1}</span><div class="two"><b>${esc(stepText(s))}</b></div>`
      + `<button type="button" class="icon" data-step-up="${i}" data-tip="Move up" aria-label="Move up"${i ? '' : ' disabled'}>${svg('chevron-up')}</button>`
      + `<button type="button" class="icon" data-step-down="${i}" data-tip="Move down" aria-label="Move down"${i < last ? '' : ' disabled'}>${svg('chevron-down')}</button>`
      + `<button type="button" class="icon" data-step-x="${i}" data-tip="Remove" aria-label="Remove this step">${svg('x')}</button></div>`).join('') || none('No steps yet. Add the first one below.');
    const run1 = has('seats.scripts.run') && (scripts || []).length ? `<optgroup label="Run a script">${scripts.map((s) => `<option value="script:${esc(s.id)}">${esc(s.name)}</option>`).join('')}</optgroup>` : '';
    const acts = has('seats.actions') ? `<optgroup label="Actions">${ACTIONS.map(([k, label]) => `<option value="${k}">${esc(label)}</option>`).join('')}</optgroup>` : '';
    $('fl-add').innerHTML = `<option value="" selected>Add a step…</option>${run1}${acts}`;
  };
  const openFlowEdit = async (w) => {
    try {
      scripts = await get('/api/devices/scripts');
    } catch (e) {
      oops(e);
      return;
    }
    flow = w ? { id: w.id, name: w.name, on: w.on, when: w.when, days: [...w.days], time: w.time || '02:00', below: w.below, if: w.if, ifText: w.ifText || '', steps: w.steps.map((s) => ({ ...s })) }
      : { id: null, name: '', on: true, when: 'approved', days: [2], time: '02:00', below: 10, if: 'always', ifText: '', steps: [] };
    $('fl-title').textContent = w ? 'Edit workflow' : 'New workflow';
    $('fl-name').value = flow.name;
    $('fl-when').value = flow.when;
    [$('fl-hour').value, $('fl-minute').value] = flow.time.split(':');
    if (!$('fl-minute').value) $('fl-minute').value = '00';
    $('fl-below').value = flow.below;
    $('fl-if').value = flow.if;
    $('fl-if-text').value = flow.ifText;
    $('fl-on').checked = flow.on;
    say('fl-problem', '');
    drawFlow();
    $('flow-dialog').showModal();
    $('fl-name').focus();
  };
  $('fl-when').addEventListener('change', () => { flow.when = $('fl-when').value; drawFlow(); });
  $('fl-if').addEventListener('change', () => { flow.if = $('fl-if').value; drawFlow(); });
  $('fl-days').addEventListener('click', (e) => {
    const b = e.target.closest('button');
    if (!b) return;
    const d = Number(b.dataset.day);
    flow.days = flow.days.includes(d) ? flow.days.filter((x) => x !== d) : [...flow.days, d];
    drawFlow();
  });
  $('fl-add').addEventListener('change', () => {
    const v = $('fl-add').value;
    if (!v) return;
    flow.steps.push(v.startsWith('script:') ? { kind: 'script', script: v.slice(7) } : { kind: v });
    drawFlow();
  });
  $('fl-steps').addEventListener('click', (e) => {
    const b = e.target.closest('button');
    if (!b) return;
    const i = Number(b.dataset.stepUp ?? b.dataset.stepDown ?? b.dataset.stepX);
    if (b.dataset.stepX !== undefined) flow.steps.splice(i, 1);
    else {
      const j = b.dataset.stepUp !== undefined ? i - 1 : i + 1;
      [flow.steps[i], flow.steps[j]] = [flow.steps[j], flow.steps[i]];
    }
    drawFlow();
  });
  $('fl-cancel').addEventListener('click', () => $('flow-dialog').close());
  const readFlows = async () => {
    await D.more.workflows.load();
    D.draw();
  };
  $('fl-save').addEventListener('click', async () => {
    const form = { ...flow, name: $('fl-name').value, time: `${$('fl-hour').value}:${$('fl-minute').value}`, below: Number($('fl-below').value) || 10, ifText: $('fl-if-text').value, on: $('fl-on').checked };
    $('fl-save').disabled = true;
    try {
      const made = await confirmed(() => send('/api/devices/workflows', form));
      $('flow-dialog').close();
      await readFlows();
      toast(flow.id ? 'Workflow saved.' : 'Workflow added.', form.on ? 'It runs the next time what starts it happens.' : "It's disabled, so it doesn't run yet.");
      const now = (flows || []).find((w) => w.id === made.id);
      if (panel.is('devices') && now) openFlow(now);
    } catch (e) {
      if (!e.cancelled) say('fl-problem', e.message);
    }
    $('fl-save').disabled = false;
  });

  // --- Clicks: the three lists, New, and the side panel -------------------------------------------------------
  const mark = () => document.querySelectorAll('#page-seats [data-row]').forEach((el) => el.classList.toggle('dw-on', panel.shows('devices', el.dataset.row)));
  panel.el.addEventListener('close', mark);
  const openRow = (key) => {
    if (panel.shows('devices', key)) {
      panel.close();
      return;
    }
    const [kind, id] = key.split(':');
    if (kind === 'job') openJob(id);
    else if (kind === 'script' && scriptOf(id)) openScript(scriptOf(id));
    else if (kind === 'flow' && (flows || []).some((w) => w.id === id)) openFlow(flows.find((w) => w.id === id));
    else if (kind === 'wmi') openWmiRow(Number(id));
  };
  for (const id of ['dv-scripts', 'dv-jobs', 'dv-workflows', 'dv-wmi']) {
    $(id).addEventListener('click', (e) => {
      const row = e.target.closest('[data-row]');
      if (row) openRow(row.dataset.row);
    });
  }
  $('dv-new').addEventListener('click', () => (D.at() === 'workflows' ? openFlowEdit(null) : openScriptEdit(null)));
  panel.el.addEventListener('click', async (e) => {
    const on = (attr) => e.target.closest(`[${attr}]`);
    // In a computer's panel (console.js draws it): Actions opens the same menu, for this one computer; and its jobs.
    const actsButton = on('data-dv-menu');
    if (actsButton && panel.is('seats')) {
      const c = D.computers().find((x) => x.id === panel.key);
      if (c) openActs(actsButton, [c], true);
      return;
    }
    const jobLink = on('data-job-open');
    if (jobLink) {
      openJob(jobLink.dataset.jobOpen);
      return;
    }
    if (!panel.is('devices')) return;
    try {
      if (on('data-job-out') && job) {
        wanted.add(`${job.id}:${on('data-job-out').dataset.jobOut}`);
        loadOutputs();
      } else if (on('data-job-cancel') && job) {
        await send(`/api/devices/jobs/${job.id}/cancel`);
        await openJob(job.id);
        if (D.at() === 'jobs') D.reload();
      } else if (on('data-sc-run')) openRun('script', ticked().length ? ticked() : D.computers().filter((c) => c.approved), shown.id);
      else if (on('data-sc-edit')) openScriptEdit(shown);
      else if (on('data-sc-copy')) openScriptEdit(shown, true);
      else if (on('data-sc-del')) {
        const s = shown;
        sure(`Delete ${s.name}?`, 'It goes from the library. Jobs that ran it keep what they ran.', 'Delete', async () => {
          try {
            await confirmed(() => send(`/api/devices/scripts/${s.id}/delete`));
            panel.close();
            scripts = await get('/api/devices/scripts');
            D.draw();
            toast('Script deleted.', s.name);
          } catch (err) {
            oops(err);
          }
        });
      } else if (on('data-fl-edit')) openFlowEdit(shown);
      else if (on('data-fl-toggle')) {
        const w = shown;
        await confirmed(() => send(`/api/devices/workflows/${w.id}/on`, { on: !w.on }));
        await readFlows();
        const now = flows.find((x) => x.id === w.id);
        if (now) openFlow(now);
        toast(w.on ? 'Workflow disabled.' : 'Workflow enabled.', w.name);
      } else if (on('data-fl-run')) {
        const w = shown;
        sure(`Run ${w.name} now?`, 'It starts on every approved computer it is for. Ones that are offline get it once back, within a day.', 'Run now', async () => {
          try {
            const made = await confirmed(() => send(`/api/devices/workflows/${w.id}/run`));
            toast('Workflow started.', `On ${many(made.computers)}. It's under Jobs.`);
            await readFlows();
            openJob(made.id);
          } catch (err) {
            oops(err);
          }
        });
      } else if (on('data-fl-del')) {
        const w = shown;
        sure(`Delete ${w.name}?`, "It stops running. Jobs it started stay under Jobs.", 'Delete', async () => {
          try {
            await send(`/api/devices/workflows/${w.id}/delete`);
            panel.close();
            await readFlows();
            toast('Workflow deleted.', w.name);
          } catch (err) {
            oops(err);
          }
        });
      }
    } catch (err) {
      oops(err);
    }
  });

  // --- Wake-on-LAN: the server, or a client on the computer's network, sends the magic packet ----------------------
  const wakingAt = {}; // computer id -> when a wake-up went to it
  const WAKING = 3 * 60000; // State says Waking this long, unless it comes online first
  D.hooks.waking = (c) => !c.online && Date.now() - (wakingAt[c.id] || 0) < WAKING;
  const wake = async (on) => {
    try {
      const { results } = await send('/api/devices/wake', { clients: on.map((c) => c.id) });
      const sent = results.filter((r) => r.sent);
      const failed = results.filter((r) => !r.sent);
      sent.forEach((r) => { wakingAt[r.client] = Date.now(); });
      if (sent.length) setTimeout(() => D.draw(), WAKING + 1000); // Waking stops showing
      D.draw();
      D.panelAgain();
      const via = [...new Set(sent.flatMap((r) => r.via))].join(' and ');
      // One that shows Online is running: a wake-up can't turn its screen on.
      const running = on.length === 1 && on[0].online;
      if (!failed.length) toast(`Sent a wake-up to ${names(sent)}.`, running ? `It already shows Online. Wake turns on a computer that is asleep or shut down. It can't turn on the screen of one that is running.`
        : `From ${via}. It shows Online once Windows is up.`);
      else if (!sent.length) toast(failed.length === 1 ? `Couldn't wake ${failed[0].computer}.` : `Couldn't wake ${many(failed.length)}.`, failed[0].problem, { tone: 'warn' });
      else toast(`Sent a wake-up to ${names(sent)}.`, `${names(failed)}: ${failed[0].problem}`, { tone: 'warn' });
    } catch (err) {
      oops(err);
    }
  };

  // --- A computer's side panel: what can be run on it, and its jobs ---------------------------------------------
  const seatJobs = {}; // computer id -> its jobs, once asked for
  D.hooks.seatTabs = (c) => (canJobs() && !c.server ? [['jobs', 'Jobs', seatJobs[c.id] ? seatJobs[c.id].length : '']] : []);
  D.hooks.seatPane = (c, tab) => {
    if (tab !== 'jobs') return null;
    const list = seatJobs[c.id];
    return section(`Jobs <small>${list ? list.length : ''}</small>`, !list ? none('Reading the list.') : !list.length ? none('Nothing has been run on it.')
      : `<div class="dw-list seat-list">${list.map((j) => `<div class="mrow job-row" data-job-open="${esc(j.id)}"><div class="two"><b>${esc(jobTitle(j))}</b><small>${esc(`${when(j.at)} · ${j.by}`)}</small></div>${pill(STATE[j.state] || ['offline', j.state])}</div>`).join('')}</div>`);
  };
  D.hooks.seatOpened = (c) => {
    if (!canJobs() || c.server) return;
    get(`/api/devices/jobs?client=${encodeURIComponent(c.id)}`).then((list) => {
      seatJobs[c.id] = list;
      D.panelAgain();
    }).catch(() => {});
  };

  // After the page drew: the ticks, which New shows, the open row, and watching what's still running.
  D.hooks.drawn = () => {
    drawPicked();
    const at = D.at();
    const make = at === 'scripts' ? has('seats.scripts.edit') && 'New script' : at === 'workflows' ? has('seats.workflows') && 'New workflow' : '';
    $('dv-new').hidden = !make;
    $('dv-new-text').textContent = make || '';
    // WMI query: its box over the list, and what any computer that didn't answer said.
    $('dv-wq').hidden = at !== 'wmi';
    if (at === 'wmi') {
      drawWqOn();
      $('wq-problems').hidden = !wq.problems.length;
      $('wq-problems').innerHTML = wq.problems.map(([who, why]) => `<li><b>${esc(who)}</b>${esc(why)}</li>`).join('');
    }
    mark();
    watch();
  };
  // For printers.js and the rest: confirming who it is before a run, and opening a job in the panel.
  D.confirmed = confirmed;
  D.openJob = (id) => openJob(id);
  D.run = (kind, on) => openRun(kind, on);
})();
