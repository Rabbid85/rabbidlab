// Workflows (PeopleWorkflows.cs), their own page under Automation: the list, a workflow in the side panel with its recent
// runs and Undo run, the Edit workflow window (fields asked for each run, then the steps), and Run. A user's Actions ›
// Run workflow… opens Run for that user (users.js).
(() => {
  const R = window.RabbidLab;
  const { $, esc, svg, has, panel, toast, sure, get, send } = R;
  const may = () => has('ad.workflows');
  const count = (n, one, many) => `${n.toLocaleString()} ${n === 1 ? one : many}`;
  const section = (title, body, right = '') => `<section class="dw-sec"><div class="dw-sec-head"><h3>${title}</h3>${right}</div>${body}</section>`;
  const none = (text) => `<p class="muted dw-empty">${esc(text)}</p>`;
  const oops = (e) => { if (!e.cancelled) toast('The operation failed.', e.message, { tone: 'warn' }); };
  const KIND_WORD = { text: 'Text', choice: 'Choice', user: 'User', ou: 'OU', date: 'Date' };
  // What each step's values are called in the editor, and what goes in them.
  const VALUE = {
    name: ['Name', '{First name} {Last name}'], logon: ['Logon name', '{First name}.{Last name}'], ou: ['OU', ''], template: ['Copy from user', 'A logon name, such as _template.sales (optional)'],
    groups: ['Groups', ''], manager: ['Manager', '{Manager}, or a logon name'], title: ['Job title', ''], department: ['Department', '{Department}'], company: ['Company', ''], office: ['Office', ''],
    date: ['Date', '{Last day}, or 2026-12-31'], path: ['Path', '\\\\FS01\\Home$\\{Logon name}'], drive: ['Drive', 'H:'], to: ['To', '{Manager}, a logon name or an email address'],
    subject: ['Subject', 'Sign-in details for {First name} {Last name}'], text: ['Message', ''],
  };

  let data = null; // /api/ad/workflows
  let places = [];
  let groups = [];
  const picked = new Set();
  const flowOf = (id) => data?.flows.find((f) => f.id === id);
  const makes = (f) => f.steps[0]?.kind === 'create';
  const load = async () => {
    try {
      const was = JSON.stringify(data);
      data = await get('/api/ad/workflows');
      R.say('pf-problem', '');
      if (JSON.stringify(data) !== was) draw();
    } catch (e) {
      R.say('pf-problem', e.message);
      if (!data) { data = { flows: [], kinds: [], fieldKinds: [] }; draw(); }
    }
  };

  // --- The list ------------------------------------------------------------------------------------------------
  const lastRun = (f) => f.runs[0];
  let G = null;
  const grid = () => (G ||= R.grid({
    el: $('pf-list'), key: 'pflows', sort: { key: 'name', dir: 1 },
    lead: { w: 18, head: '<label class="tk"><input type="checkbox" data-pf-tick-all aria-label="Select all"></label>',
      cell: (f) => `<label class="tk"><input type="checkbox" data-pf-tick="${esc(f.id)}" aria-label="Select ${esc(f.name)}"${picked.has(f.id) ? ' checked' : ''}></label>` },
    cols: [
      { key: 'name', label: 'Workflow', w: 230, fixed: true, get: (f) => f.name, cell: (f) => `<div class="who"><span class="pic">${svg('org')}</span><b class="ell">${esc(f.name)}</b></div>` },
      { key: 'starts', label: 'Runs on', w: 150, on: true, get: (f) => (makes(f) ? 'A new user' : 'A user') },
      { key: 'asks', label: 'Asks for', w: 220, on: true, get: (f) => f.fields.map((x) => x.label).join(', '), cell: (f) => `<span class="ell">${esc(f.fields.map((x) => x.label).join(', ') || 'Nothing')}</span>` },
      { key: 'steps', label: 'Steps', w: 300, on: true, get: (f) => f.steps.map((s) => s.label).join(' → '), cell: (f) => `<span class="ell">${esc(f.steps.map((s) => s.label).join(' → '))}</span>` },
      { key: 'state', label: 'State', w: 100, on: true, get: (f) => (f.on ? 'Enabled' : 'Disabled'), cell: (f) => `<span class="ubadge ${f.on ? 'green' : 'grey'}">${f.on ? 'Enabled' : 'Disabled'}</span>` },
      { key: 'last', label: 'Last run', w: 140, on: true, get: (f) => (lastRun(f) ? new Date(lastRun(f).at).getTime() : 0), text: (f) => (lastRun(f) ? R.when(lastRun(f).at) : 'Never'),
        cell: (f) => `<span class="muted">${esc(lastRun(f) ? R.when(lastRun(f).at) : 'Never')}</span>` },
      { key: 'by', label: 'Changed by', w: 130, get: (f) => f.by || '' },
    ],
    row: (f) => ` data-pf-row="${esc(f.id)}"`, empty: () => 'No workflows yet. Select New workflow to make one.', loading: () => !data, redraw: () => draw(),
  }));
  // The banner: how many workflows, how many are on, and how many runs this week.
  R.scatter?.($('pf-hero'), 20261127);
  const drawHero = () => {
    if (!data) return;
    const flows = data.flows;
    const week = flows.reduce((n, f) => n + f.runs.filter((r) => Date.now() - new Date(r.at) < 7 * 86400000).length, 0);
    $('pf-state').textContent = flows.length ? `${count(flows.filter((f) => f.on).length, 'workflow is', 'workflows are')} enabled.` : 'No workflows yet.';
    $('pf-n-all').textContent = flows.length;
    $('pf-n-on').textContent = flows.filter((f) => f.on).length;
    $('pf-n-week').textContent = week;
    $('rail-workflows').textContent = flows.length || '';
  };
  const draw = () => {
    drawHero();
    if (R.where()[0] !== 'workflows') return;
    $('pf-new').hidden = !may();
    $('pf-acts').hidden = !may();
    grid().draw(data?.flows || []);
    $('pf-count').textContent = data ? count(data.flows.length, 'workflow', 'workflows') : '';
    drawTicks();
    if (panel.is('pflow')) { const f = flowOf(panel.key); if (f) openFlow(f, true); else panel.close(); }
  };
  const drawTicks = () => {
    [...$('pf-list').querySelectorAll('[data-pf-tick]')].forEach((b) => { b.checked = picked.has(b.dataset.pfTick); b.closest('.tr')?.classList.toggle('picked', b.checked); });
    const n = picked.size;
    $('pf-acts').disabled = !n;
    $('pf-acts').querySelector('.cnt').hidden = !n;
    $('pf-acts').querySelector('.cnt').textContent = n;
  };
  $('pf-list').addEventListener('click', (e) => {
    const tk = e.target.closest('.tk');
    if (tk) {
      const box = tk.querySelector('input');
      if (e.target !== box) return;
      if (box.dataset.pfTickAll !== undefined) (data?.flows || []).forEach((f) => (box.checked ? picked.add(f.id) : picked.delete(f.id)));
      else if (box.checked) picked.add(box.dataset.pfTick);
      else picked.delete(box.dataset.pfTick);
      drawTicks();
      return;
    }
    const row = e.target.closest('[data-pf-row]');
    if (!row) return;
    if (panel.shows('pflow', row.dataset.pfRow)) panel.close();
    else if (flowOf(row.dataset.pfRow)) openFlow(flowOf(row.dataset.pfRow));
  });
  $('pf-acts').addEventListener('click', () => {
    const list = () => (data?.flows || []).filter((f) => picked.has(f.id));
    R.actions.open($('pf-acts'), {
      items: [{ act: 'on', label: 'Enable' }, { act: 'off', label: 'Disable' }, '-', { act: 'delete', label: 'Delete…', hint: String(list().length), danger: true }],
      pick: (act) => (act === 'delete' ? remove(list()) : turn(list(), act === 'on')),
    });
  });
  $('pf-new').addEventListener('click', () => openEdit(null));
  document.addEventListener('rabbidlab:show', (e) => {
    if (e.detail[0] !== 'workflows') return;
    if (!data) R.skeleton($('pf-list'), 'grid', 4, { cols: 5 });
    load();
  });
  setInterval(() => { if (R.where()[0] === 'workflows' && document.visibilityState === 'visible') load(); }, 30000);
  // The count in the sidebar from the start, when the page itself hasn't asked already.
  document.addEventListener('rabbidlab:me', () => { if (has('ad.view') && !data) load(); });

  const save = (f, over = {}) => send('/api/ad/workflows', { id: f.id, name: f.name, on: f.on, fields: f.fields, steps: f.steps.map((s) => ({ kind: s.kind, values: s.values })), ...over });
  const turn = async (list, on) => {
    try {
      for (const f of list) await save(f, { on });
      toast(on ? 'Workflows enabled.' : 'Workflows disabled.', list.map((f) => f.name).join(', '));
      picked.clear();
      await load();
    } catch (e) {
      oops(e);
    }
  };
  const remove = (list) => sure(list.length === 1 ? `Delete ${list[0].name}?` : `Delete ${list.length} workflows?`, 'Their steps and run history are deleted. Changes they made stay.', 'Delete', async () => {
    try {
      for (const f of list) await send(`/api/ad/workflows/${f.id}/delete`);
      toast(list.length === 1 ? 'Workflow deleted.' : `${list.length} workflows deleted.`, list.map((f) => f.name).join(', '));
      picked.clear();
      if (panel.is('pflow')) panel.close();
      await load();
    } catch (e) {
      oops(e);
    }
  });

  // --- A workflow in the side panel ------------------------------------------------------------------------------
  const RUN_TONE = { done: ['Completed', 'green'], failed: ['Failed', 'red'] };
  const openFlow = (f, again = false) => {
    const runs = f.runs.map((r) => {
      const [word, tone] = RUN_TONE[r.state] || [r.state, 'grey'];
      const undo = r.created && r.user ? `<button type="button" class="dw-link" data-pf-undo="${esc(r.id)}">${svg('restart')}Undo run</button>`
        : r.changes.length ? `<button type="button" class="dw-link" data-pf-undo="${esc(r.id)}">${svg('restart')}Undo run</button>` : '';
      return `<div class="pf-run"><p><b>${esc(R.when(r.at))}</b><span class="ubadge ${tone}">${word}</span><span class="muted ell">${esc([r.userName, r.by].filter(Boolean).join(' · by '))}</span><span class="grow"></span>${undo}</p>`
        + `<ul class="pf-results">${r.steps.map((s) => `<li class="${s.state}">${svg(s.state === 'done' ? 'check' : s.state === 'failed' ? 'alert' : 'dots')}<span><b>${esc(s.label)}</b><small>${esc(s.detail)}</small></span></li>`).join('')}</ul></div>`;
    }).join('');
    panel.body.innerHTML = `<div class="dw-head"><span class="pic dw-pic">${svg('org')}</span><div class="dw-name"><h2 id="dw-title">${esc(f.name)}</h2><p>${esc(makes(f) ? 'Makes a new user' : 'Runs on a user')} · ${f.on ? 'Enabled' : 'Disabled'}</p></div>`
      + `<button type="button" class="icon dw-close" data-dw-close aria-label="Close">${svg('x')}</button></div>`
      + `<div class="dw-bar">${may() ? R.editButton('data-pf-edit', '') : ''}<button type="button" class="dw-qa" data-pf-run${f.on ? '' : ' disabled'}>${svg('play')}<span>Run</span></button>`
      + (may() ? `<button type="button" class="dw-qa" data-pf-dup>${svg('copy')}<span>Duplicate</span></button><button type="button" class="dw-qa" data-pf-turn>${svg(f.on ? 'pause' : 'play')}<span>${f.on ? 'Disable' : 'Enable'}</span></button>`
        + `<button type="button" class="dw-qa" data-pf-del>${svg('trash')}<span>Delete</span></button>` : '') + '</div>'
      + section(`Asks for <small>${f.fields.length}</small>`, f.fields.length ? `<ul class="does wide">${f.fields.map((x) => `<li><b>${esc(x.label)}</b><span>${esc(KIND_WORD[x.kind] || x.kind)}${x.required ? ', required' : ''}${x.choices?.length ? `: ${esc(x.choices.join(', '))}` : ''}</span></li>`).join('')}</ul>` : none('Nothing. It runs on the user picked.'))
      + section(`Steps <small>${f.steps.length}</small>`, `<ol class="pf-steps">${f.steps.map((s) => `<li><b>${esc(s.label)}</b><span>${tokens(s.sentence)}</span></li>`).join('')}</ol>`)
      + section(`Recent runs <small>${f.runs.length}</small>`, runs || none('It has not run yet.'));
    if (!again) panel.open('pflow', f.id);
  };
  // {Field} in a sentence, marked so it stands out.
  const tokens = (text) => esc(text).replace(/\{([^{}]+)\}/g, '<span class="pf-tok">$1</span>');
  panel.el.addEventListener('click', async (e) => {
    if (!panel.is('pflow')) return;
    const f = flowOf(panel.key);
    if (!f) return;
    let b;
    if (e.target.closest('[data-pf-edit]')) openEdit(f);
    else if (e.target.closest('[data-pf-run]')) openRun(f);
    else if (e.target.closest('[data-pf-dup]')) openEdit({ ...JSON.parse(JSON.stringify(f)), id: '', name: `${f.name} (copy)` });
    else if (e.target.closest('[data-pf-turn]')) turn([f], !f.on);
    else if (e.target.closest('[data-pf-del]')) remove([f]);
    else if ((b = e.target.closest('[data-pf-undo]'))) undoRun(f, f.runs.find((r) => r.id === b.dataset.pfUndo));
  });
  // A run undone as one: every change it made, newest first; a run that made the account deletes it instead.
  const undoRun = (f, r) => {
    if (!r) return;
    if (r.created) {
      sure(`Delete ${r.userName}?`, `This run of ${f.name} created the account, so undoing it deletes the account. It can be restored from the Recycle Bin if that is turned on.`, 'Delete account', async () => {
        try {
          await send(`/api/ad/users/${encodeURIComponent(r.user)}/delete`);
          toast('Run undone.', `${r.userName} was deleted.`);
          document.dispatchEvent(new CustomEvent('rabbidlab:undone'));
        } catch (e) {
          oops(e);
        }
      });
      return;
    }
    R.undo([...r.changes].reverse(), { host: panel.body });
  };

  // --- Edit workflow ---------------------------------------------------------------------------------------------
  let draft = null;
  let open = -1; // the step whose values show
  let lastInput = null; // where a field's chip goes
  const blankFlow = () => ({ id: '', name: 'New user', on: true,
    fields: [{ label: 'First name', kind: 'text', required: true, choices: [] }, { label: 'Last name', kind: 'text', required: true, choices: [] },
      { label: 'Job title', kind: 'text', required: false, choices: [] }, { label: 'Manager', kind: 'user', required: false, choices: [] }],
    steps: [{ kind: 'create', values: { name: '{First name} {Last name}', logon: '{First name}.{Last name}', ou: '' } }, { kind: 'props', values: { title: '{Job title}' } },
      { kind: 'manager', values: { manager: '{Manager}' } }] });
  const labelOf = (kind) => data?.kinds.find((k) => k.key === kind)?.label || kind;
  const valuesOf = (kind) => data?.kinds.find((k) => k.key === kind)?.values || [];
  const chips = () => `<span class="pf-chips">${['Logon name', 'Name', ...draft.fields.map((x) => x.label).filter(Boolean)].map((l) => `<button type="button" class="pf-tok" data-pf-chip="${esc(l)}">${esc(l)}</button>`).join('')}</span>`;
  const valueInput = (i, key, v) => {
    const [label, hint] = VALUE[key] || [key, ''];
    if (key === 'ou') {
      return `<label class="pf-v"><span>${label}</span><select data-pf-val="${i}:${key}"><option value="">${i === 0 ? 'Where new users go' : 'Select an OU'}</option>${places.map((p) => `<option value="${esc(p.dn)}"${p.dn === v ? ' selected' : ''}>${esc(p.path || p.name)}</option>`).join('')}</select></label>`;
    }
    if (key === 'groups') {
      const now = (v || '').split('\n').filter(Boolean);
      return `<div class="pf-v"><span>${label}</span><div><div class="pf-groups">${now.map((dn) => `<span class="uchip">${esc(dn.split(',')[0].replace(/^CN=/i, ''))}<button type="button" class="icon" data-pf-ungroup="${i}:${esc(dn)}" aria-label="Remove">${svg('x')}</button></span>`).join('') || '<span class="muted">No groups yet.</span>'}</div>`
        + `<select data-pf-group="${i}"><option value="">Add a group…</option>${groups.filter((g) => !now.includes(g.dn)).map((g) => `<option value="${esc(g.dn)}">${esc(g.name)}</option>`).join('')}</select></div></div>`;
    }
    if (key === 'drive') return `<label class="pf-v"><span>${label}</span><select data-pf-val="${i}:${key}">${['', 'H:', 'U:', 'Z:', 'P:'].map((d) => `<option${d === (v || '') ? ' selected' : ''}>${d}</option>`).join('')}</select></label>`;
    const tag = key === 'text' ? 'textarea' : 'input';
    return `<label class="pf-v"><span>${label}</span>${tag === 'textarea' ? `<textarea rows="3" data-pf-val="${i}:${key}" placeholder="${esc(hint)}">${esc(v || '')}</textarea>`
      : `<input data-pf-val="${i}:${key}" value="${esc(v || '')}" placeholder="${esc(hint)}" autocomplete="off" spellcheck="false">`}</label>`;
  };
  const drawEdit = () => {
    const d = draft;
    const box = $('pf-edit-body');
    const top = box.scrollTop;
    box.innerHTML = `<label class="pf-v wide"><span>Name</span><input data-pf-name value="${esc(d.name)}" maxlength="80" autocomplete="off"></label>`
      + '<div class="pf-two"><div><span class="pf-l">Runs</span><b>By hand</b></div><div><span class="pf-l">Runs as</span><b>The service account, within your roles</b></div></div>'
      + `<h3>Run-time fields</h3><div class="pf-fields">${d.fields.map((x, i) => `<div class="pf-field"><input data-pf-flabel="${i}" value="${esc(x.label)}" placeholder="Field name" maxlength="40" autocomplete="off">`
        + `<select data-pf-fkind="${i}">${(data?.fieldKinds || []).map((k) => `<option value="${k}"${k === x.kind ? ' selected' : ''}>${KIND_WORD[k] || k}</option>`).join('')}</select>`
        + `<label class="tick"><input type="checkbox" data-pf-freq="${i}"${x.required ? ' checked' : ''}>Required</label>`
        + `<button type="button" class="icon" data-pf-fdel="${i}" data-tip="Remove" aria-label="Remove ${esc(x.label)}">${svg('x')}</button>`
        + (x.kind === 'choice' ? `<input class="pf-choices" data-pf-fchoices="${i}" value="${esc((x.choices || []).join(', '))}" placeholder="Choices, separated by commas" autocomplete="off">` : '')
        + '</div>').join('') || '<p class="muted">No fields. The workflow runs on the user picked.</p>'}</div>`
      + '<button type="button" class="small" data-pf-fadd>' + svg('plus') + 'Add a field</button>'
      + `<h3>Steps</h3><ol class="pf-steplist">${d.steps.map((s, i) => `<li class="${i === open ? 'open' : ''}"><div class="pf-sh"><i>${i + 1}</i><span><b>${esc(labelOf(s.kind))}</b><small>${tokens(sentence(s))}</small></span>`
        + `<button type="button" class="icon" data-pf-sopen="${i}" data-tip="Edit" aria-label="Edit step ${i + 1}">${svg('edit')}</button>`
        + `<button type="button" class="icon" data-pf-sup="${i}"${i === 0 ? ' disabled' : ''} data-tip="Move up" aria-label="Move up">${svg('chevron-up')}</button>`
        + `<button type="button" class="icon" data-pf-sdown="${i}"${i === d.steps.length - 1 ? ' disabled' : ''} data-tip="Move down" aria-label="Move down">${svg('chevron-down')}</button>`
        + `<button type="button" class="icon" data-pf-sdel="${i}" data-tip="Remove" aria-label="Remove step ${i + 1}">${svg('x')}</button></div>`
        + (i === open ? `<div class="pf-sv">${valuesOf(s.kind).map((k) => valueInput(i, k, s.values[k])).join('') || '<p class="muted">This step has nothing to set.</p>'}${valuesOf(s.kind).length ? `<p class="pf-hint">Fill in a field: ${chips()}</p>` : ''}</div>` : '')
        + '</li>').join('')}</ol>`
      + `<select class="pf-add" data-pf-sadd><option value="">Add a step…</option>${(data?.kinds || []).filter((k) => k.key !== 'create' || !d.steps.length).map((k) => `<option value="${k.key}">${esc(k.label)}</option>`).join('')}</select>`;
    box.scrollTop = top;
    $('pf-on').checked = d.on;
  };
  // A step as a sentence while it's edited, like the server says it.
  const sentence = (s) => {
    const v = (k) => s.values[k] || '';
    const ou = (dn) => (dn ? dn.split(',')[0].replace(/^(OU|CN)=/i, '') : 'where new users go');
    switch (s.kind) {
      case 'create': return `Create ${v('name')} (${v('logon')}) in ${ou(v('ou'))}${v('template') ? `, copying ${v('template')}` : ''}`;
      case 'groups': return v('groups') ? `Add to ${v('groups').split('\n').filter(Boolean).map((g) => g.split(',')[0].replace(/^CN=/i, '')).join(', ')}` : 'Add to groups';
      case 'ungroup': return 'Remove from every group except the primary group';
      case 'manager': return `Set the manager to ${v('manager')}`;
      case 'props': return `Set ${['title', 'department', 'company', 'office'].filter((k) => v(k)).map((k) => `${VALUE[k][0].toLowerCase()} to ${v(k)}`).join(', ') || 'properties'}`;
      case 'move': return `Move to ${ou(v('ou'))}`;
      case 'enable': return 'Enable the account';
      case 'disable': return 'Disable the account';
      case 'password': return 'Reset the password to a random value, changed at next sign-in';
      case 'expire': return `Set the account to expire on ${v('date')}`;
      case 'home': return `Create the home folder ${v('path')}${v('drive') ? ` as ${v('drive')}` : ''}`;
      case 'email': return `Email ${v('to')}: “${v('subject')}”`;
      default: return labelOf(s.kind);
    }
  };
  const openEdit = async (f) => {
    draft = JSON.parse(JSON.stringify(f || blankFlow()));
    draft.steps = draft.steps.map((s) => ({ kind: s.kind, values: { ...s.values } }));
    open = f ? -1 : 0;
    $('pf-edit-title').textContent = f?.id ? 'Edit workflow' : 'New workflow';
    R.say('pf-edit-problem', '');
    R.skeleton($('pf-edit-body'), 'form', 5);
    $('pf-edit').showModal();
    try {
      [places, groups] = await Promise.all([R.keep('/api/ad/places', 60000), R.keep('/api/ad/groups', 60000)]);
      places = (places || []).filter((p) => !p.fixed);
      groups = [...(groups || [])].sort((a, b) => a.name.localeCompare(b.name));
    } catch {
      // The OU and group lists stay empty; typing still works.
    }
    drawEdit();
  };
  $('pf-edit').addEventListener('click', (e) => {
    const t = e.target.closest('button, [data-pf-chip]');
    if (!t || !draft) return;
    const d = draft;
    const n = (attr) => Number(t.getAttribute(attr));
    if (t.id === 'pf-edit-cancel') { $('pf-edit').close(); return; }
    if (t.id === 'pf-edit-save') { saveEdit(); return; }
    if (t.matches('[data-pf-fadd]')) { d.fields.push({ label: '', kind: 'text', required: false, choices: [] }); drawEdit(); $('pf-edit-body').querySelector(`[data-pf-flabel="${d.fields.length - 1}"]`)?.focus(); return; }
    if (t.matches('[data-pf-fdel]')) { d.fields.splice(n('data-pf-fdel'), 1); drawEdit(); return; }
    if (t.matches('[data-pf-sopen]')) { open = open === n('data-pf-sopen') ? -1 : n('data-pf-sopen'); drawEdit(); return; }
    if (t.matches('[data-pf-sdel]')) { d.steps.splice(n('data-pf-sdel'), 1); open = -1; drawEdit(); return; }
    if (t.matches('[data-pf-sup], [data-pf-sdown]')) {
      const i = n(t.matches('[data-pf-sup]') ? 'data-pf-sup' : 'data-pf-sdown');
      const j = t.matches('[data-pf-sup]') ? i - 1 : i + 1;
      [d.steps[i], d.steps[j]] = [d.steps[j], d.steps[i]];
      open = open === i ? j : open;
      drawEdit();
      return;
    }
    if (t.matches('[data-pf-ungroup]')) {
      const [i, dn] = t.dataset.pfUngroup.split(/:(.*)/s);
      d.steps[i].values.groups = (d.steps[i].values.groups || '').split('\n').filter((g) => g && g !== dn).join('\n');
      drawEdit();
      return;
    }
    if (t.matches('[data-pf-chip]') && lastInput?.isConnected) {
      const el = lastInput;
      const at = el.selectionStart ?? el.value.length;
      el.value = `${el.value.slice(0, at)}{${t.dataset.pfChip}}${el.value.slice(el.selectionEnd ?? at)}`;
      el.dispatchEvent(new Event('input', { bubbles: true }));
      el.focus();
    }
  });
  $('pf-edit').addEventListener('focusin', (e) => { if (e.target.matches('[data-pf-val]')) lastInput = e.target; });
  $('pf-edit').addEventListener('input', (e) => {
    const t = e.target;
    if (!draft) return;
    if (t.matches('[data-pf-name]')) draft.name = t.value;
    else if (t.dataset.pfFlabel) draft.fields[t.dataset.pfFlabel].label = t.value;
    else if (t.dataset.pfFchoices) draft.fields[t.dataset.pfFchoices].choices = t.value.split(',').map((c) => c.trim()).filter(Boolean);
    else if (t.dataset.pfVal) {
      const [i, key] = t.dataset.pfVal.split(':');
      draft.steps[i].values[key] = t.value;
      const small = t.closest('li')?.querySelector('.pf-sh small');
      if (small) small.innerHTML = tokens(sentence(draft.steps[i]));
    }
  });
  $('pf-edit').addEventListener('change', (e) => {
    const t = e.target;
    if (!draft) return;
    if (t.dataset.pfFkind) { draft.fields[t.dataset.pfFkind].kind = t.value; drawEdit(); } else if (t.dataset.pfFreq) draft.fields[t.dataset.pfFreq].required = t.checked;
    else if (t.matches('[data-pf-val]') && t.tagName === 'SELECT') { const [i, key] = t.dataset.pfVal.split(':'); draft.steps[i].values[key] = t.value; drawEdit(); } else if (t.dataset.pfGroup && t.value) {
      const s = draft.steps[t.dataset.pfGroup];
      s.values.groups = [...(s.values.groups || '').split('\n').filter(Boolean), t.value].join('\n');
      drawEdit();
    } else if (t.matches('[data-pf-sadd]') && t.value) {
      draft.steps.push({ kind: t.value, values: {} });
      open = draft.steps.length - 1;
      drawEdit();
    } else if (t.id === 'pf-on') draft.on = t.checked;
  });
  const saveEdit = async () => {
    R.busy($('pf-edit'), 'Saving the workflow', draft.name);
    try {
      const { id } = await save(draft);
      $('pf-edit').close();
      toast(draft.id ? 'Workflow saved.' : 'Workflow created.', draft.name);
      await load();
      const f = flowOf(id);
      if (f && R.where()[0] === 'workflows') openFlow(f);
    } catch (e) {
      R.say('pf-edit-problem', e.message);
    } finally {
      R.busy($('pf-edit'), '');
    }
  };

  // --- Run -------------------------------------------------------------------------------------------------------
  let running = null; // { flow, user }
  let people = [];
  const fieldInput = (x) => {
    const id = `data-pf-in="${esc(x.label)}"`;
    const label = `<span>${esc(x.label)}${x.required ? '' : ' <small>(optional)</small>'}</span>`;
    if (x.kind === 'choice') return `<label class="pf-v">${label}<select ${id}><option value=""></option>${x.choices.map((c) => `<option>${esc(c)}</option>`).join('')}</select></label>`;
    if (x.kind === 'user') return `<label class="pf-v">${label}<select ${id}><option value=""></option>${people.map((u) => `<option value="${esc(u.username)}">${esc(u.name || u.username)}</option>`).join('')}</select></label>`;
    if (x.kind === 'ou') return `<label class="pf-v">${label}<select ${id}><option value=""></option>${places.map((p) => `<option value="${esc(p.dn)}">${esc(p.path || p.name)}</option>`).join('')}</select></label>`;
    return `<label class="pf-v">${label}<input ${id} type="${x.kind === 'date' ? 'date' : 'text'}" autocomplete="off"></label>`;
  };
  const drawRun = () => {
    const { flow, user } = running;
    const choose = !flow;
    const list = (data?.flows || []).filter((f) => f.on && !makes(f));
    $('pf-run-title').textContent = flow ? `Run ${flow.name}` : 'Run a workflow';
    $('pf-run-lead').textContent = flow ? (makes(flow) ? 'Fill in the fields, then Run makes the user.' : 'Pick the user, fill in the fields, then Run.') : `Pick the workflow to run${user ? ` on ${user.name || user.username}` : ''}.`;
    $('pf-run-body').innerHTML = (choose || user ? `<label class="pf-v"><span>Workflow</span><select data-pf-pick><option value="">Select a workflow</option>${list.map((f) => `<option value="${esc(f.id)}"${f.id === flow?.id ? ' selected' : ''}>${esc(f.name)}</option>`).join('')}</select></label>` : '')
      + (flow && !makes(flow) ? `<label class="pf-v"><span>User</span><select data-pf-user><option value=""></option>${people.map((u) => `<option value="${esc(u.username)}"${u.username === user?.username ? ' selected' : ''}>${esc(u.name || u.username)}</option>`).join('')}</select></label>` : '')
      + (flow ? flow.fields.map(fieldInput).join('') + `<h3>Steps</h3><ol class="pf-steps">${flow.steps.map((s) => `<li><b>${esc(s.label)}</b><span>${tokens(s.sentence)}</span></li>`).join('')}</ol>` : '');
    $('pf-run-go').disabled = !flow;
  };
  const openRun = async (flow, user = null) => {
    running = { flow, user };
    R.say('pf-run-problem', '');
    R.skeleton($('pf-run-body'), 'form', 3);
    $('pf-run').showModal();
    try {
      if (!data) data = await get('/api/ad/workflows');
      const [p, u] = await Promise.all([R.keep('/api/ad/places', 60000), R.keep('/api/ad/users', 60000)]);
      places = (p || []).filter((x) => !x.fixed);
      people = (u?.users || []).filter((x) => !x.service).sort((a, b) => (a.name || a.username).localeCompare(b.name || b.username));
    } catch {
      // The lists stay short; the run says what's wrong.
    }
    drawRun();
  };
  $('pf-run').addEventListener('change', (e) => {
    if (e.target.matches('[data-pf-pick]')) { running.flow = flowOf(e.target.value) || null; drawRun(); }
  });
  $('pf-run-cancel').addEventListener('click', () => $('pf-run').close());
  $('pf-run-go').addEventListener('click', async () => {
    const { flow } = running;
    if (!flow) return;
    const values = {};
    $('pf-run-body').querySelectorAll('[data-pf-in]').forEach((el) => { values[el.dataset.pfIn] = el.value; });
    const user = $('pf-run-body').querySelector('[data-pf-user]')?.value || null;
    R.busy($('pf-run'), `Running ${flow.name}`, user || '');
    try {
      const run = await send(`/api/ad/workflows/${flow.id}/run`, { user, values });
      $('pf-run').close();
      const bad = run.steps.find((s) => s.state === 'failed');
      if (bad) toast(`${flow.name} stopped at ${bad.label}.`, bad.detail, { tone: 'warn' });
      else toast(`${flow.name} completed.`, `${count(run.steps.length, 'step', 'steps')}${run.userName ? ` for ${run.userName}` : ''}.`, { action: run.changes.length && !run.created ? { label: 'Undo', run: () => R.undo([...run.changes].reverse()), changes: run.changes } : null });
      document.dispatchEvent(new CustomEvent('rabbidlab:undone')); // the Users list reads again
      await load();
    } catch (e) {
      R.say('pf-run-problem', e.message);
    } finally {
      R.busy($('pf-run'), '');
    }
  });

  // users.js: a user's Actions › Run workflow…
  R.flows = { run: (user) => openRun(null, user), any: () => has('ad.view') };
})();
