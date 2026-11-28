// Devices: doing things on computers. The ticks on Computers and its Actions menu, the lists under Tools (Scripts, Jobs,
// Workflows), the windows for running a script, writing one and making a workflow, and confirming who's asking before
// anything runs. The page itself is console.js; this adds to it through RabbidLab.devices.
(() => {
  const R = window.RabbidLab;
  const D = R.devices;
  const { $, esc, svg, send, get, toast, sure, panel, when, has, say, BRAKE, many, names, oops, qa, section, none, closeX } = R;

  const NEEDS = '1.1.168'; // the first client that takes jobs
  const canScripts = () => has('seats.scripts.run') || has('seats.scripts.edit');
  const canDo = () => canScripts() || has('seats.actions') || has('seats.installs');
  const canJobs = () => canDo() || has('seats.workflows');
  const canPick = () => canDo() || has('seats.approve') || has('seats.remove');
  // Whether a computer's client is older than the version given.
  const below = (c, version) => {
    const [a, b] = [c.version || '0', version].map((v) => v.split('.').map(Number));
    for (let i = 0; i < 3; i++) if ((a[i] || 0) !== b[i]) return (a[i] || 0) < b[i];
    return false;
  };
  const older = (c) => below(c, NEEDS);
  const LANG = { powershell: 'PowerShell', cmd: 'Command' };
  const head = (icon, title, sub) => `<div class="dw-head"><span class="pic dw-pic">${svg(icon)}</span><div class="dw-name"><h2 id="dw-title">${esc(title)}</h2><p>${sub}</p></div>`
    + `${closeX}</div>`;
  const facts = (rows) => `<ul class="does">${rows.filter(Boolean).map(([k, v]) => `<li><b>${esc(k)}</b><span>${esc(v)}</span></li>`).join('')}</ul>`;
  const pill = ([tone, label]) => `<span class="state ${tone}">${esc(label)}</span>`;
  const pressed = (seg, attr, value) => seg.querySelectorAll('button').forEach((b) => b.setAttribute('aria-pressed', String(b.dataset[attr] === value)));

  // --- Confirming who it is ------------------------------------------------------------------------------------
  // The server refuses to run or save anything until this session has confirmed lately, and says what it takes: a code
  // from the authenticator app, or the password. confirmed() asks, then does what was refused once more.
  let asking = null; // { how, ok, no } while the window is open
  const askConfirm = (how) => new Promise((ok, no) => {
    asking = { how, ok, no };
    const code = how === 'code';
    $('cf-lead').textContent = code ? 'Enter the code from your authenticator app. You will be asked again after 10 minutes.' : 'Enter your password. You will be asked again after 10 minutes.';
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
    const typed = asking.how === 'code' ? { code: $('cf-code').value } : { password: $('cf-password').value };
    $('cf-password').value = ''; // sent once, and not left in the page
    try {
      await send('/api/confirm', typed);
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
    $('cf-password').value = '';
    $('cf-code').value = '';
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
  const jobTitle = (j) => (j.kind === 'workflow' ? `Workflow: ${j.title}` : j.kind === 'script' ? (j.title === 'A script typed in' ? 'Run an ad hoc script' : `Run script: ${j.title}`) : j.title);
  const JOB_ICON = { script: 'code', restart: 'restart', updates: 'updates', workflow: 'org', printer: 'printer', check: 'refresh', lock: 'lock', signout: 'logout', shutdown: 'power', message: 'message', client: 'download', gpupdate: 'policy', install: 'box' };
  const STATE = { waiting: ['waiting', 'Pending'], running: ['run', 'Running'], done: ['online', 'Completed'], failed: ['bad', 'Failed'], refused: ['bad', 'Rejected'],
    noanswer: ['bad', 'No response'], cancelled: ['offline', 'Cancelled'], expired: ['offline', 'Expired'] };
  // How a job went across its computers, in a word or two.
  const result = (j) => {
    const of = (k) => j.states[k] || 0;
    const n = j.computers;
    const bad = of('failed') + of('refused') + of('noanswer');
    const open = of('waiting') + of('running');
    if (open) return [of('running') ? 'run' : 'waiting', open === n ? (of('running') ? 'Running' : 'Pending') : `${n - open} of ${n} completed`];
    if (bad) return ['bad', n === 1 ? STATE[Object.keys(j.states)[0]][1] : `Failed on ${bad} of ${n}`];
    if (of('done') === n) return ['online', 'Completed'];
    return of('done') ? ['waiting', `Completed on ${of('done')} of ${n}`] : ['offline', of('expired') ? 'Expired' : 'Cancelled'];
  };
  const open = (j) => (j.states.waiting || 0) + (j.states.running || 0) > 0;
  const DAYS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
  const clock = (time) => {
    const [h, m] = (time || '00:00').split(':').map(Number);
    return new Date(2000, 0, 1, h, m).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' });
  };
  const WHEN = [['approved', 'A new computer is approved'], ['schedule', 'On a schedule'], ['online', 'A computer comes online'], ['updates', 'A computer is missing Windows updates'],
    ['restart', 'A computer has a restart pending'], ['disk', 'Low free space on C:']];
  const IF = [['always', 'All computers'], ['name', 'Computers named'], ['windows', 'OS name contains'], ['notwindows', 'OS name does not contain'], ['nobody', 'Computers with no user signed in']];
  const ACTIONS = [['updates', 'Install missing Windows updates'], ['restart', 'Restart'], ['restart-if-needed', 'Restart if a restart is pending']];
  const whenText = (w) => (w.when === 'schedule' ? `${w.days.length === 7 ? 'Every day' : w.days.map((d) => DAYS[d]).join(', ')} at ${clock(w.time)}`
    : w.when === 'disk' ? `Free space on C: below ${w.below}%` : WHEN.find((x) => x[0] === w.when)?.[1] || w.when);
  const ifText = (w) => (w.if === 'always' ? '' : w.if === 'nobody' ? 'No user signed in' : `${IF.find((x) => x[0] === w.if)?.[1] || w.if} ${w.ifText || ''}`);
  // An install step is named by its app on the Installs page (installs.js keeps those).
  const appName = (id) => D.apps?.().find((a) => a.id === id)?.name;
  const stepText = (s) => (s.kind === 'script' ? `Run script: ${scriptOf(s.script)?.name || '(deleted script)'}`
    : s.kind === 'install' ? `Install ${appName(s.app) || '(deleted app)'}` : ACTIONS.find((a) => a[0] === s.kind)?.[1] || s.kind);
  const ms = (iso) => (iso ? new Date(iso).getTime() : 0);

  let scriptsG = null;
  const scriptsGrid = () => (scriptsG ||= R.grid({
    el: $('dv-scripts'), key: 'scripts',
    cols: [
      { key: 'name', label: 'Name', w: 240, fixed: true, get: (s) => s.name, cell: (s) => `<b>${esc(s.name)}</b>` },
      { key: 'language', label: 'Language', w: 100, on: true, get: (s) => LANG[s.language] },
      { key: 'about', label: 'Description', w: 280, on: true, get: (s) => s.about || '', cell: (s) => `<span class="ell muted">${esc(s.about || '')}</span>` },
      { key: 'changed', label: 'Modified', w: 150, on: true, get: (s) => ms(s.changed), text: (s) => s.changed || '', cell: (s) => `<span class="muted">${esc(when(s.changed))}</span>` },
      { key: 'by', label: 'By', w: 110, on: true, get: (s) => (s.builtIn ? 'Built-in' : s.by || '') },
      { key: 'minutes', label: 'Time limit', w: 100, get: (s) => s.minutes, text: (s) => `${s.minutes} min`, cell: (s) => `<span class="muted">${s.minutes} min</span>` },
      { key: 'workflows', label: 'Workflows', w: 180, get: (s) => s.workflows.join(', ') },
    ],
    row: (s) => ` data-row="script:${esc(s.id)}"`,
    empty: () => D.more.scripts.empty(), loading: () => !scripts && !D.failed(),
    redraw: () => D.draw(),
  }));
  let jobsG = null;
  const jobsGrid = () => (jobsG ||= R.grid({
    el: $('dv-jobs'), key: 'jobs', sort: { key: 'at', dir: -1 },
    cols: [
      { key: 'title', label: 'Job', w: 270, fixed: true, get: (j) => jobTitle(j), cell: (j) => `<b>${esc(jobTitle(j))}</b>` },
      { key: 'at', label: 'Started', w: 150, on: true, get: (j) => ms(j.at), text: (j) => j.at, cell: (j) => `<span class="muted">${esc(when(j.at))}</span>` },
      { key: 'by', label: 'By', w: 110, on: true, get: (j) => j.by },
      { key: 'on', label: 'Computers', w: 180, on: true, get: (j) => (j.computers <= 3 ? j.names.join(', ') : many(j.computers)), cell: (j) => `<span class="ell">${esc(j.computers <= 3 ? j.names.join(', ') : many(j.computers))}</span>` },
      { key: 'result', label: 'Result', w: 140, on: true, get: (j) => result(j)[1], cell: (j) => pill(result(j)) },
    ],
    row: (j) => ` data-row="job:${esc(j.id)}"`,
    empty: () => D.more.jobs.empty(), loading: () => !jobs && !D.failed(),
    redraw: () => D.draw(),
  }));
  let flowsG = null;
  const flowsGrid = () => (flowsG ||= R.grid({
    el: $('dv-workflows'), key: 'workflows',
    cols: [
      { key: 'name', label: 'Workflow', w: 200, fixed: true, get: (w) => w.name, cell: (w) => `<b>${esc(w.name)}</b>` },
      { key: 'when', label: 'Trigger', w: 230, on: true, get: (w) => whenText(w), cell: (w) => `<span class="ell">${esc(whenText(w))}</span>` },
      { key: 'steps', label: 'Steps', w: 250, on: true, get: (w) => w.steps.map(stepText).join(', '), cell: (w) => `<span class="ell">${esc(w.steps.map(stepText).join(' → '))}</span>` },
      { key: 'on', label: 'State', w: 80, on: true, get: (w) => (w.on ? 0 : 1), text: (w) => (w.on ? 'Enabled' : 'Disabled'), cell: (w) => pill(w.on ? ['online', 'Enabled'] : ['offline', 'Disabled']) },
      { key: 'lastRun', label: 'Last run', w: 130, on: true, get: (w) => ms(w.lastRun), text: (w) => w.lastRun || '', cell: (w) => `<span class="muted">${w.lastRun ? esc(when(w.lastRun)) : 'Never'}</span>` },
      { key: 'if', label: 'Computers', w: 200, get: (w) => ifText(w) || 'All computers' },
    ],
    row: (w) => ` data-row="flow:${esc(w.id)}"`,
    empty: () => D.more.workflows.empty(), loading: () => !flows && !D.failed(),
    redraw: () => D.draw(),
  }));
  D.lists.scripts = ['Scripts', 'code', 'PowerShell and command scripts that run on computers as SYSTEM.'];
  D.lists.jobs = ['Jobs', 'history', 'Every job run on computers, who started it and the result.'];
  D.lists.workflows = ['Workflows', 'org', 'Automated steps that run on a trigger or a schedule.'];
  D.more.scripts = { table: 'dv-scripts', grid: scriptsGrid, rows: () => scripts || [], count: () => scripts?.length, can: () => canScripts() || has('seats.workflows'),
    load: async () => { scripts = await get('/api/devices/scripts'); },
    empty: () => (!scripts ? 'Could not load the list.' : has('seats.scripts.edit') ? 'No scripts yet. Click New script to add one.' : 'No scripts yet.') };
  D.more.jobs = { table: 'dv-jobs', grid: jobsGrid, rows: () => jobs || [], count: () => (jobs || []).filter(open).length, can: canJobs,
    load: async () => { jobs = await get('/api/devices/jobs'); },
    empty: () => (jobs ? 'No jobs yet.' : 'Could not load the list.') };
  D.more.workflows = { table: 'dv-workflows', grid: flowsGrid, rows: () => flows || [], count: () => flows?.length, can: () => has('seats.workflows'),
    // A workflow's steps are shown by their script's name, or their app's.
    load: async () => { [flows, scripts] = await Promise.all([get('/api/devices/workflows'), get('/api/devices/scripts'), D.loadApps?.()?.catch(() => {})]); },
    empty: () => (flows ? 'No workflows yet. Click New workflow to add one.' : 'Could not load the list.') };

  // --- WMI query ------------------------------------------------------------------------------------------------
  // Anything Windows keeps in WMI, read on each computer picked, as SYSTEM, by its client (the client's Wmi.cs). What
  // comes back is a list like the others: Columns, Export and Filter work on it, and a row opens all it has in the panel.
  const WMI_FROM = '1.1.173'; // the first client that answers
  const WQ_EXAMPLES = [
    ['BIOS and serial number', '', 'SELECT Manufacturer, SMBIOSBIOSVersion, ReleaseDate, SerialNumber FROM Win32_BIOS'],
    ['Manufacturer, model and memory', '', 'SELECT Manufacturer, Model, TotalPhysicalMemory, Domain, UserName FROM Win32_ComputerSystem'],
    ['Windows version and last boot time', '', 'SELECT Caption, Version, BuildNumber, InstallDate, LastBootUpTime FROM Win32_OperatingSystem'],
    ['Processor', '', 'SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor'],
    ['Memory modules', '', 'SELECT BankLabel, Capacity, Speed, Manufacturer, PartNumber FROM Win32_PhysicalMemory'],
    ['Drives and free space', '', 'SELECT DeviceID, VolumeName, FileSystem, Size, FreeSpace FROM Win32_LogicalDisk WHERE DriveType = 3'],
    ['Physical disks', '', 'SELECT Model, MediaType, Size, SerialNumber, Status FROM Win32_DiskDrive'],
    ['Network adapters', '', 'SELECT Description, IPAddress, DefaultIPGateway, DNSServerSearchOrder, MACAddress, DHCPEnabled FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = TRUE'],
    ['Stopped automatic services', '', "SELECT Name, DisplayName, State, StartMode FROM Win32_Service WHERE StartMode = 'Auto' AND State <> 'Running'"],
    ['Startup programs', '', 'SELECT Name, Command, Location, User FROM Win32_StartupCommand'],
    ['Installed updates', '', 'SELECT HotFixID, Description, InstalledOn, InstalledBy FROM Win32_QuickFixEngineering'],
    ['User profiles', '', 'SELECT LocalPath, LastUseTime, Loaded FROM Win32_UserProfile WHERE Special = FALSE'],
    ['Printers', '', 'SELECT Name, PortName, DriverName, Shared, Network FROM Win32_Printer'],
    ['Shared folders', '', 'SELECT Name, Path, Description FROM Win32_Share'],
    ['Monitor serial numbers', 'root\\wmi', 'SELECT ManufacturerName, UserFriendlyName, SerialNumberID, YearOfManufacture FROM WmiMonitorID'],
    ['BitLocker status', 'root\\cimv2\\Security\\MicrosoftVolumeEncryption', 'SELECT DriveLetter, ProtectionStatus, ConversionStatus, EncryptionMethod FROM Win32_EncryptableVolume'],
    ['TPM', 'root\\cimv2\\Security\\MicrosoftTpm', 'SELECT IsActivated_InitialValue, IsEnabled_InitialValue, SpecVersion, ManufacturerVersion FROM Win32_Tpm'],
    ['Microsoft Defender', 'root\\Microsoft\\Windows\\Defender', 'SELECT AMProductVersion, AntivirusSignatureLastUpdated, RealTimeProtectionEnabled, QuickScanEndTime FROM MSFT_MpComputerStatus'],
    ['Antivirus products', 'root\\SecurityCenter2', 'SELECT displayName, productState, timestamp FROM AntiVirusProduct'],
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
  D.lists.wmi = ['WMI query', 'list', 'Query WMI on one or more computers. Read-only.'];
  D.more.wmi = { table: 'dv-wmi', grid: wqGrid, rows: () => wq.rows, count: () => '', can: () => has('seats.wmi'), noun: 'rows', fields: wqFields,
    load: async () => {}, every: 0, // nothing to read again: a query runs when Run is clicked
    empty: () => (wq.asked ? 'The query returned no results.' : 'Select computers, enter a query or choose a built-in query, then click Run.') };

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
    const html = (ticked ? `<option value="ticked">Selected computers (${ticked})</option>` : '') + `<option value="online">All online computers (${online})</option>`
      + (approved.length ? `<optgroup label="Single computer">${approved.map((c) => `<option value="c:${esc(c.id)}">${esc(c.computer)}${D.stateOf(c) === 'online' ? '' : ' (offline)'}${below(c, WMI_FROM) ? ' (client out of date)' : ''}</option>`).join('')}</optgroup>` : '');
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
      toast('Enter a query.', 'Or select one under Built-in queries.', { tone: 'warn' });
      return;
    }
    if (!on.length) {
      toast('No computers selected.', 'Select a computer under Computers, or select computers on the Computers list.', { tone: 'warn' });
      return;
    }
    const mine = ++wqAsking;
    const space = $('wq-space').value.trim() || 'root\\cimv2';
    $('wq-run').disabled = true;
    R.busy($('dv-lib'), `Querying ${names(on)}…`, 'This can take up to 40 seconds.');
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
        problems: j.results.filter((r) => !r.ok || r.more).map((r) => [r.computer || 'Unknown computer', r.ok ? `Showing the first ${r.rows.length.toLocaleString()} rows. Add a WHERE clause to narrow the results.` : r.error]) });
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
      toast('Select a computer.', `The class list is read from a selected computer with client ${WMI_FROM} or later.`, { tone: 'warn' });
      return;
    }
    const mine = ++wqcAsking;
    if (!$('wqc-dialog').open) {
      $('wqc-find').value = '';
      $('wqc-dialog').showModal();
      $('wqc-find').focus();
    }
    $('wqc-lead').textContent = `Classes on ${c.computer}. Select a class to query all of its properties.`;
    $('wqc-list').innerHTML = `<p class="muted">Loading classes in ${esc(space)}…</p>`;
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
      + (list.length > shown.length ? `<p class="muted">${(list.length - shown.length).toLocaleString()} more not shown. Use Find to narrow the list.</p>` : ''))
      || `<p class="muted">${wqc.classes.length ? 'No matching classes.' : 'No classes in this namespace.'}</p>`;
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
    const own = D.more[D.at()]?.acts; // a list with ticks of its own (Local admins)
    const n = own ? own.count() : D.picked.size;
    D.grid().recount?.(); // 3/7 Items selected
    const act = $('dv-act');
    act.hidden = own ? !own.can() : !canPick();
    act.disabled = !n;
    act.querySelector('.cnt').hidden = !n;
    act.querySelector('.cnt').textContent = n.toLocaleString();
    act.dataset.tip = own ? (n ? `${n.toLocaleString()} selected` : 'Select items first') : n ? `${many(n)} selected` : 'Select computers first';
    // Computers and Windows updates' own row per computer share the ticks.
    for (const table of TICKED.map((id) => $(id))) {
      R.grid.ticks(table, 'tick', D.picked);
    }
  };
  const TICKED = ['seats-table'];
  for (const id of TICKED) $(id).addEventListener('click', (e) => { if (R.grid.tick(e, $(id), 'tick', D.picked)) drawPicked(); });
  // What can be done on computers, from this one list: the Actions menu for the ticked ones, and the Actions button of
  // a computer's panel (actions.js draws it). Related ones go in a sub-menu; a group of one is a row of its own.
  // printers.js and installs.js add theirs through actItems.
  const E = (act, label, hint = '', danger = false) => ({ act, label, hint: hint ? String(hint) : '', danger });
  const group = (label, sub) => (!sub.length ? [] : sub.length === 1 ? sub : [{ label, sub }]);
  const doItems = (on) => {
    if (!on.some((c) => c.approved)) return [];
    const off = on.filter((c) => c.approved && !c.online && !c.server).length;
    const acts = has('seats.actions');
    const behind = on.filter((c) => c.approved && c.behind).length;
    return [
      ...group('Power', acts ? [E('restart', 'Restart…'), E('shutdown', 'Shut down…'), E('wake', 'Wake', off || '')] : []),
      ...group('Sessions', acts ? [E('lock', 'Lock…'), E('signout', 'Sign out all users…'), E('message', 'Send a message…')] : []),
      ...group('Run', [...(canScripts() ? [E('script', 'Run script…')] : []), ...(has('seats.wmi') ? [E('wmi', 'Run WMI query…')] : []), ...(acts ? [E('gpupdate', 'Run gpupdate…')] : [])]),
      ...group('Install', [...(acts ? [E('updates', 'Windows updates…'), E('check', 'Check for updates…'), E('client', 'Update client…', behind || '')] : []), ...(D.hooks.actItems?.(E) || [])]),
    ];
  };
  // Said over the menu when not every computer ticked takes everything in it.
  const noteFor = (on) => {
    const waiting = on.filter((c) => !c.approved).length;
    const off = on.filter((c) => c.approved && !c.online && !c.server).length;
    const all = on.length;
    const some = (n) => (n === all ? (all === 1 ? 'It is' : 'They are') : `${n} of ${all} ${n === 1 ? 'is' : 'are'}`);
    return [waiting ? `${some(waiting)} pending approval. Only Approve and Remove apply${waiting === all ? '' : ` to ${waiting === 1 ? 'it' : 'them'}`}.` : '',
      off ? `${some(off)} offline. Jobs run on ${off === 1 ? 'it' : 'them'} when back online.` : ''].filter(Boolean).join(' ');
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
    // This server without the client: putting it on is all there is to do.
    if (on.length === 1 && on[0].server) {
      const started = D.hooks.hereStarted?.();
      R.actions.open(button, { items: [{ act: 'here', label: started ? 'Installing client…' : 'Install client on this server', disabled: !!started }], pick: picked });
      return;
    }
    const waiting = on.filter((c) => !c.approved).length;
    const items = doItems(on);
    if (!inPanel) items.push('-', ...(has('seats.approve') && waiting ? [E('approve', 'Approve', waiting)] : []), E('export', 'Export selected'), E('clear', 'Clear selection'));
    if (has('seats.remove')) items.push('-', E('remove', 'Remove from Devices…', '', true));
    R.actions.open(button, { items, pick: picked, note: noteFor(on) });
  };
  $('dv-act').addEventListener('click', () => {
    const own = D.more[D.at()]?.acts;
    if (own) own.open($('dv-act'));
    else openActs($('dv-act'), ticked(), false);
  });
  // A computer's panel has the one button; what it opens is the same menu.
  D.hooks.seatBar = (c) => ((c.server ? !has('seats.add') : !doItems([c]).length && !has('seats.remove')) ? ''
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
  // What was picked in the menu, for the computers it was opened for.
  async function picked(act) {
    const on = menuOn;
    if (act === 'here') D.hooks.installHere?.();
    else if (act === 'clear') {
      D.picked.clear();
      drawPicked();
    } else if (act === 'export') D.grid().csv(D.grid().sorted(on), 'devices-ticked');
    else if (act === 'approve') {
      const list = on.filter((c) => !c.approved);
      const failed = await each(list, 'approve');
      toast(failed ? `Could not approve ${failed} of ${list.length} computers.` : `${many(list.length)} approved.`, '', failed ? { tone: 'warn' } : {});
    } else if (act === 'remove') {
      const one = on.length === 1;
      sure(one ? `Remove ${on[0].computer}?` : `Remove ${many(on.length)}?`, one ? 'The client reconnects and needs approval again.' : `${names(on)}. Each client reconnects and needs approval again.`, 'Remove', async () => {
        const failed = await each(on, 'remove');
        if (menuPanel) panel.close();
        else D.picked.clear();
        toast(failed ? `Could not remove ${failed} of ${on.length} computers.` : `${one ? on[0].computer : many(on.length)} removed.`, '', failed ? { tone: 'warn' } : {});
      });
    } else await doAct(act, on, menuPanel);
  }

  // --- Running something on computers ---------------------------------------------------------------------------
  let run = null; // { kind, on, lang } while the window is open
  // Each kind's words in the run window: its title, its button, what it does, the toast once sent, and whether it's
  // for now (an offline computer gets it within 15 minutes, not a day).
  const KINDS = {
    script: { title: () => 'Run script', go: 'Run', sent: 'Script sent.', note: 'Runs as SYSTEM. The script, its output and who ran it are recorded under Jobs.' },
    updates: { title: () => 'Install Windows updates', go: 'Install', sent: 'Update installation started.', note: 'All missing updates are downloaded and installed. Computers are not restarted. Computers that need a restart are listed under Needs restart.' },
    check: { title: () => 'Check for updates', go: 'Check', sent: 'Update check started.', note: 'Each computer scans Windows Update for missing updates. Results appear under Windows updates within a few minutes.' },
    restart: { title: (n) => `Restart ${many(n)}?`, go: 'Restart', sent: 'Restart requested.', note: 'Signed-in users get a five-minute warning first.' },
    shutdown: { title: (n) => `Shut down ${many(n)}?`, go: 'Shut down', sent: 'Shutdown requested.', now: true, note: 'Signed-in users get a five-minute warning first.' },
    lock: { title: (n) => `Lock ${many(n)}?`, go: 'Lock', sent: 'Lock requested.', now: true, note: 'Signed-in users are returned to the sign-in screen. Their apps keep running, and they need their password to unlock.' },
    signout: { title: (n) => `Sign out all users on ${many(n)}?`, go: 'Sign out', sent: 'Sign-out requested.', now: true, note: 'All users are signed out immediately. Unsaved work is lost.' },
    message: { title: () => 'Send a message', go: 'Send', sent: 'Message sent.', now: true, note: 'The message appears for all signed-in users until they close it, for up to an hour.' },
    client: { title: (n) => `Update the client on ${many(n)}?`, go: 'Update', sent: 'Client update requested.', now: true, note: 'Each client installs the version on this server now instead of at its next scheduled check, then restarts. Clients already on that version only restart.' },
    gpupdate: { title: (n) => `Run gpupdate on ${many(n)}?`, go: 'Run gpupdate', sent: 'gpupdate requested.', now: true, note: 'Computer policy is reapplied now, and user policy for signed-in users. The Group Policy tabs refresh their results a minute or two later.' },
  };
  const kindOf = (kind) => KINDS[kind] || KINDS.script;
  const runNote = () => {
    const off = run.on.filter((c) => D.stateOf(c) === 'offline').length;
    const old = run.on.filter(older).length;
    const base = kindOf(run.kind).note;
    const within = kindOf(run.kind).now ? 'within 15 minutes' : 'within a day';
    const all = run.on.length;
    return base + (!off ? '' : off < all ? ` ${off} ${off === 1 ? 'is offline and receives' : 'are offline and receive'} the job when back online, ${within}.`
      : all === 1 ? ` The computer is offline. The job runs when it is back online, ${within}.` : ` The computers are offline. The job runs when they are back online, ${within}.`)
      + (!old ? '' : old === all && all === 1 ? ` The client is older than ${NEEDS}. The job runs after the client updates.`
        : ` ${old} ${old === 1 ? 'has' : 'have'} a client older than ${NEEDS} and ${old === 1 ? 'receives' : 'receive'} the job after updating.`);
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
      + (has('seats.scripts.edit') ? `<option value=""${selected === null ? ' selected' : ''}>Enter a script</option>` : '') || '<option value="" disabled selected>No scripts in the library</option>';
  };
  const openRun = async (kind, on, preset = '') => {
    const list = on.filter((c) => c.approved);
    if (!list.length) {
      toast('No computers selected.', 'Select approved computers.', { tone: 'warn' });
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
    $('run-count-label').textContent = `This affects ${n} computers. Type ${n} to confirm`;
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
      toast('Script saved.', `${name} was added to Scripts.`);
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
      showTest(`Sent to ${c.computer}. Waiting for the client to start the job…`);
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
        showTest(`${head}\n\n${(out.output || []).join('\n').trim() || 'No output.'}`, t.state !== 'done' || (step.exit != null && step.exit !== 0));
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
      toast(kindOf(run.kind).sent, `On ${names(run.on)}. View progress under Jobs.`);
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
    const bar = waiting ? `<div class="dw-bar">${qa('data-job-cancel', 'x', `Cancel for the ${waiting === job.targets.length ? '' : `${waiting} `}computer${waiting === 1 ? '' : 's'} still pending`)}</div>` : '';
    const targets = job.targets.map((t) => {
      const c = D.computers().find((x) => x.id === t.client);
      const why = t.problem || (t.state !== 'waiting' ? '' : c && older(c) ? `The client is older than ${NEEDS}. The job runs after the client updates.`
        : c && D.stateOf(c) === 'offline' ? `Offline. The job runs when the computer is back online, until ${when(job.expires)}.` : 'The client starts the job within a few seconds.');
      const out = outputs[`${job.id}:${t.client}`];
      const steps = t.steps.map((s, i) => `<p class="job-step"><b>${esc(job.steps[i]?.title || `Step ${i + 1}`)}</b><small>${s.timedOut ? 'timed out' : `exit code ${s.exit ?? 'unknown'}`} · ${secs(s.seconds)}</small></p>`
        + (out ? `<pre class="code-box">${esc(out[i] || '(no output)')}</pre>` : '')).join('');
      const ask = t.steps.length && !out ? `<button type="button" class="small" data-job-out="${esc(t.client)}">${svg('eye')}Show output</button>` : '';
      return section(esc(t.computer), (why ? `<p class="dw-how">${esc(why)}</p>` : '') + steps + ask, pill(STATE[t.state] || ['offline', t.state]));
    }).join('');
    const what = job.steps.map((s) => `<details class="job-what"><summary><b>${esc(s.title)}</b><small>${LANG[s.language] || s.language} · time limit ${s.minutes} min</small></summary><pre class="code-box">${esc(s.text)}</pre></details>`).join('');
    panel.body.innerHTML = head(JOB_ICON[job.kind] || 'code', jobTitle(job), esc(`${when(job.at)} · by ${job.by}`)) + bar + targets + section('Steps', what);
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
  // The panel opens straight away with placeholder rows, and fills in once the job is read.
  const openJob = async (id) => {
    const here = () => panel.shows('devices', `job:${id}`);
    if (!here()) {
      const j = (jobs || []).find((x) => x.id === id);
      panel.body.innerHTML = head(JOB_ICON[j?.kind] || 'code', j ? jobTitle(j) : 'Job', j ? esc(`${when(j.at)} · by ${j.by}`) : '') + '<div data-job-skel></div>';
      R.skeleton(panel.body.querySelector('[data-job-skel]'), 'rows', 4);
      panel.open('devices', `job:${id}`);
      mark();
    }
    try {
      jobSeen = '';
      await readJob(id);
    } catch (e) {
      const sk = here() && panel.body.querySelector('[data-job-skel]');
      if (sk) sk.outerHTML = `<p class="hint problem">${esc(e.message)}</p>`;
      else oops(e);
      return;
    }
    if (!here()) return;
    drawJob();
    loadOutputs();
    watch();
  };

  // --- A script: in the side panel, and the window that writes one ---------------------------------------------
  let shown = null; // the script or workflow open in the side panel
  const openScript = (s) => {
    shown = s;
    panel.body.innerHTML = head('code', s.name, esc(`${LANG[s.language]} · time limit ${s.minutes} minutes`))
      + `<div class="dw-bar">${R.editButton('data-sc-edit', !has('seats.scripts.edit') ? 'Your role cannot edit scripts' : s.builtIn ? 'Built-in scripts cannot be edited. Duplicate it to make an editable copy.' : '')}`
      + `${has('seats.scripts.run') ? qa('data-sc-run', 'right', 'Run…') : ''}${has('seats.scripts.edit') ? qa('data-sc-copy', 'copy', 'Duplicate') + (s.builtIn ? '' : qa('data-sc-del', 'trash', 'Delete')) : ''}</div>`
      + (s.builtIn ? `<p class="dw-how">Built into ${esc(R.app())}. Duplicate it to make an editable copy.</p>` : '')
      + (s.about ? section('About', `<p class="dw-how">${esc(s.about)}</p>`) : '')
      + section('Script', `<pre class="code-box">${esc(s.text)}</pre>`)
      + (s.workflows.length ? section(`Used by workflows <small>${s.workflows.length}</small>`, `<div class="dw-list">${s.workflows.map((w) => `<div class="mrow"><span class="pic small">${svg('org')}</span><div class="two"><b>${esc(w)}</b></div></div>`).join('')}</div>`) : '')
      + section('Modified', facts([['When', when(s.changed)], s.by && ['By', s.by]]));
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
    $('sc-start').innerHTML = `<option value="">Blank script</option>${builtIns().map((x) => `<option value="${esc(x.id)}">${esc(x.name)} (${LANG[x.language]})</option>`).join('')}`;
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
      toast(editing.id ? 'Script saved.' : 'Script created.', form.name.trim());
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
      + `<div class="dw-bar">${R.editButton('data-fl-edit')}${qa('data-fl-toggle', 'power', w.on ? 'Disable' : 'Enable')}${qa('data-fl-run', 'right', 'Run now')}${qa('data-fl-del', 'trash', 'Delete')}</div>`
      + section('Steps', `<div class="dw-list seat-list fl-view">${row('Trigger', whenText(w), 'clock')}${ifText(w) ? row('Computers', ifText(w), 'filter') : ''}${w.steps.map((s, i) => row(i ? 'Then' : 'First', stepText(s), 'right')).join('')}</div>`)
      + section('Last run', w.lastRun ? facts([['When', when(w.lastRun)]]) + (w.lastJob ? `<div class="seat-acts"><button type="button" class="small" data-job-open="${esc(w.lastJob)}">${svg('history')}Open job</button></div>` : '') : none('Not run yet.'))
      + section('Modified', facts([['When', when(w.changed)], w.by && ['By', w.by]]));
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
    $('fl-if-label').textContent = flow.if === 'name' ? 'Name (* is a wildcard)' : 'Text';
    $('fl-if-text').placeholder = flow.if === 'name' ? 'SHOP-*' : 'Server';
    $('fl-days').querySelectorAll('button').forEach((b) => b.setAttribute('aria-pressed', String(flow.days.includes(Number(b.dataset.day)))));
    const last = flow.steps.length - 1;
    $('fl-steps').innerHTML = flow.steps.map((s, i) => `<div class="mrow"><span class="pic small">${i + 1}</span><div class="two"><b>${esc(stepText(s))}</b></div>`
      + `<button type="button" class="icon" data-step-up="${i}" data-tip="Move up" aria-label="Move up"${i ? '' : ' disabled'}>${svg('chevron-up')}</button>`
      + `<button type="button" class="icon" data-step-down="${i}" data-tip="Move down" aria-label="Move down"${i < last ? '' : ' disabled'}>${svg('chevron-down')}</button>`
      + `<button type="button" class="icon" data-step-x="${i}" data-tip="Remove" aria-label="Remove this step">${svg('x')}</button></div>`).join('') || none('No steps yet. Add a step below.');
    const run1 = has('seats.scripts.run') && (scripts || []).length ? `<optgroup label="Run script">${scripts.map((s) => `<option value="script:${esc(s.id)}">${esc(s.name)}</option>`).join('')}</optgroup>` : '';
    const acts = has('seats.actions') ? `<optgroup label="Actions">${ACTIONS.map(([k, label]) => `<option value="${k}">${esc(label)}</option>`).join('')}</optgroup>` : '';
    const installs = has('seats.installs') && D.apps?.().length ? `<optgroup label="Install an app">${[...D.apps()].sort((a, b) => a.name.localeCompare(b.name))
      .map((a) => `<option value="install:${esc(a.id)}">${esc(a.name)}</option>`).join('')}</optgroup>` : '';
    $('fl-add').innerHTML = `<option value="" selected>Add a step…</option>${run1}${installs}${acts}`;
  };
  const openFlowEdit = async (w) => {
    try {
      [scripts] = await Promise.all([get('/api/devices/scripts'), D.loadApps?.()?.catch(() => {})]);
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
    flow.steps.push(v.startsWith('script:') ? { kind: 'script', script: v.slice(7) } : v.startsWith('install:') ? { kind: 'install', app: v.slice(8) } : { kind: v });
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
      toast(flow.id ? 'Workflow saved.' : 'Workflow created.', form.on ? 'It runs at the next trigger.' : 'It is disabled and does not run until it is enabled.');
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
  // New in the banner's bar: it adds what the open list holds (Add computers anywhere else), and its arrow has
  // everything this page adds. Add computers is console.js's, Add an app installs.js's.
  const ADDS = [
    ['computers', 'monitor', 'Add computers', () => has('seats.add'), () => D.hooks.addComputers?.()],
    ['installs', 'box', 'Add app', () => has('seats.installs'), () => D.hooks.addApp?.()],
    ['scripts', 'code', 'New script', () => has('seats.scripts.edit'), () => openScriptEdit(null)],
    ['workflows', 'org', 'New workflow', () => has('seats.workflows'), () => openFlowEdit(null)],
  ];
  const addHere = () => { const ok = ADDS.filter((a) => a[3]()); return ok.find((a) => a[0] === D.at()) || ok[0]; };
  $('dv-new').addEventListener('click', () => addHere()?.[4]());
  $('dv-new-more').addEventListener('click', () => R.picker.menu($('dv-new-pair'),
    ADDS.filter((a) => a[3]()).map(([, icon, label, , run]) => ({ label, icon, run })), { key: 'dv.new' }));
  panel.el.addEventListener('click', async (e) => {
    const on = (attr) => e.target.closest(`[${attr}]`);
    // In a computer's panel (console.js draws it): Actions opens the same menu, for this one computer; and its jobs.
    const actsButton = on('data-dv-menu');
    if (actsButton && panel.is('seats')) {
      const c = D.rows().find((x) => x.id === panel.key); // this server's row too
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
        sure(`Delete ${s.name}?`, 'The script is removed from the library. Past jobs keep a copy of what they ran.', 'Delete', async () => {
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
        sure(`Run ${w.name} now?`, 'Runs on all approved computers in scope. Offline computers run it when back online, within a day.', 'Run now', async () => {
          try {
            const made = await confirmed(() => send(`/api/devices/workflows/${w.id}/run`));
            toast('Workflow started.', `On ${many(made.computers)}. View progress under Jobs.`);
            await readFlows();
            openJob(made.id);
          } catch (err) {
            oops(err);
          }
        });
      } else if (on('data-fl-del')) {
        const w = shown;
        sure(`Delete ${w.name}?`, 'The workflow no longer runs. Jobs it started stay under Jobs.', 'Delete', async () => {
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
      // Waking stops showing; one that still hasn't come online is said, with where to look.
      if (sent.length) {
        setTimeout(() => {
          D.draw();
          D.panelAgain();
          const asleep = sent.map((r) => D.computers().find((c) => c.id === r.client)).filter((c) => c && !c.online);
          if (asleep.length) toast(`${names(asleep)} did not wake.`, 'Check the Wake-on-LAN settings in the computer\'s panel. The BIOS must also allow wake from the network, with ErP or deep sleep disabled.', { tone: 'warn' });
        }, WAKING + 1000);
      }
      D.draw();
      D.panelAgain();
      const via = [...new Set(sent.flatMap((r) => r.via))].join(' and ');
      // One that shows Online is running: a wake-up can't turn its screen on.
      const running = on.length === 1 && on[0].online;
      if (!failed.length) toast(`Wake-on-LAN packet sent to ${names(sent)}.`, running ? `The computer is already online. Wake-on-LAN starts a computer that is asleep or shut down. It cannot turn on the display of a running computer.`
        : `Sent from ${via}. The computer shows as Online once Windows starts.`);
      else if (!sent.length) toast(failed.length === 1 ? `Could not wake ${failed[0].computer}.` : `Could not wake ${many(failed.length)}.`, failed[0].problem, { tone: 'warn' });
      else toast(`Wake-on-LAN packet sent to ${names(sent)}.`, `${names(failed)}: ${failed[0].problem}`, { tone: 'warn' });
    } catch (err) {
      oops(err);
    }
  };

  // --- A computer's side panel: what can be run on it, and its jobs ---------------------------------------------
  const seatJobs = {}; // computer id -> its jobs, once asked for
  const seatJobsWrong = {}; // computer id -> why its jobs couldn't be read
  D.hooks.seatTabs = (c) => (canJobs() && !c.server ? [['jobs', 'Jobs', seatJobs[c.id] ? seatJobs[c.id].length : '']] : []);
  D.hooks.seatPane = (c, tab) => {
    if (tab !== 'jobs') return null;
    const list = seatJobs[c.id];
    // Placeholder rows (the panel fills data-seat-skel) until they're read.
    return section(`Jobs <small>${list ? list.length : ''}</small>`, !list ? (seatJobsWrong[c.id] ? none(seatJobsWrong[c.id]) : '<div data-seat-skel></div>') : !list.length ? none('No jobs.')
      : `<div class="dw-list seat-list">${list.map((j) => `<div class="mrow job-row" data-job-open="${esc(j.id)}"><div class="two"><b>${esc(jobTitle(j))}</b><small>${esc(`${when(j.at)} · ${j.by}`)}</small></div>${pill(STATE[j.state] || ['offline', j.state])}</div>`).join('')}</div>`);
  };
  D.hooks.seatOpened = (c) => {
    if (!canJobs() || c.server) return;
    delete seatJobsWrong[c.id];
    get(`/api/devices/jobs?client=${encodeURIComponent(c.id)}`).then((list) => {
      seatJobs[c.id] = list;
      delete seatJobsWrong[c.id];
      D.panelAgain();
    }).catch((err) => {
      seatJobsWrong[c.id] = `Could not load jobs. ${err.message}`;
      D.panelAgain();
    });
  };

  // After the page drew: the ticks, what New adds here, the open row, and watching what's still running.
  D.hooks.drawn = () => {
    drawPicked();
    const at = D.at();
    R.newLabel($('dv-new'), addHere()?.[1] || 'plus', addHere()?.[2] || 'New');
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
  R.askConfirm = askConfirm; // send() in console.js asks with it, for changes on any page
  D.mark = mark;
  D.drawPicked = drawPicked;
  D.openJob = (id) => openJob(id);
  D.run = (kind, on) => openRun(kind, on);
})();
