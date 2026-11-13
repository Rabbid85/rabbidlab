// Org chart (#org) and Attribute browser (#attrs/<place>/<object>, each a DN, encoded), read as Rabbid Lab's own AD
// account. console.js shares its helpers as window.RabbidLab, and users.js the service account setup card.
(() => {
  const R = window.RabbidLab;
  const { $, esc, svg, say, initials } = R;
  const byName = (a, b) => String(a.name || a.username).localeCompare(String(b.name || b.username));

  // Whether the service account is set up: asked until it is, then again only after it changes or a read fails.
  let ad = null;
  const ready = async () => {
    if (!ad?.state) ad = await R.get('/api/ad');
    return ad;
  };

  // --- Org chart -----------------------------------------------------------------------------------------------------

  let people = null;
  const box = (u, reports) => `<div class="obox" data-hay="${esc([u.name, u.username, u.title, u.department].filter(Boolean).join('\n').toLowerCase())}">`
    + `<span class="pic user">${esc(initials(u.name || u.username))}</span>`
    + `<div class="two"><b>${esc(u.name || u.username)}</b><small>${esc([u.title, u.department].filter(Boolean).join(' · ') || u.username)}</small></div>`
    + `${reports ? `<span class="ocount" title="${reports} report${reports === 1 ? '' : 's'}">${reports}</span>` : ''}</div>`;
  // Find marks the boxes that match; the chart isn't drawn again for it.
  const markOrg = () => {
    const find = $('org-find').value.trim().toLowerCase();
    $('org-tree').querySelectorAll('.obox').forEach((b) => b.classList.toggle('hit', !!find && b.dataset.hay.includes(find)));
  };
  const drawOrg = () => {
    const byDn = new Map(people.map((u) => [u.dn.toLowerCase(), u]));
    // Someone set as their own manager has no manager.
    const bossOf = (u) => {
      const boss = u.managerDn ? byDn.get(u.managerDn.toLowerCase()) : null;
      return boss && boss !== u ? boss : null;
    };
    const reports = new Map();
    people.forEach((u) => {
      const boss = bossOf(u);
      if (boss) reports.set(boss.dn, [...(reports.get(boss.dn) || []), u]);
    });
    $('org-count').textContent = `${people.length} people`;
    if (!reports.size) {
      // Nobody has a manager: everyone by department instead.
      say('org-note', 'Nobody has a Manager set in Active Directory yet, so here is everyone by department. Set each person\'s Manager (Active Directory Users and Computers, the Organization tab) and the chart draws itself.');
      const depts = new Map();
      people.forEach((u) => {
        const d = u.department || 'No department';
        depts.set(d, [...(depts.get(d) || []), u]);
      });
      $('org-tree').className = 'org depts';
      $('org-tree').innerHTML = [...depts].sort(([a], [b]) => (a === 'No department') - (b === 'No department') || a.localeCompare(b))
        .map(([d, list]) => `<section class="odept"><h3>${esc(d)}<small>${list.length}</small></h3>${list.sort(byName).map((u) => box(u, 0)).join('')}</section>`).join('');
      markOrg();
      return;
    }
    say('org-note', '');
    const seen = new Set();
    const node = (u) => {
      if (seen.has(u.dn)) return '';
      seen.add(u.dn);
      const kids = (reports.get(u.dn) || []).sort(byName);
      return `<li>${box(u, kids.length)}${kids.length ? `<ul>${kids.map(node).join('')}</ul>` : ''}</li>`;
    };
    const tops = people.filter((u) => !bossOf(u) && reports.has(u.dn)).sort(byName);
    const alone = people.filter((u) => !bossOf(u) && !reports.has(u.dn)).sort(byName);
    let tree = tops.map(node).join('');
    alone.forEach((u) => seen.add(u.dn));
    // People who manage each other in a loop have no top: each loop starts at whoever comes first by name.
    people.filter((u) => !seen.has(u.dn)).sort(byName).forEach((u) => { tree += node(u); });
    $('org-tree').className = 'org';
    $('org-tree').innerHTML = `<ul class="otree">${tree}</ul>`
      + (alone.length ? `<section class="odept alone"><h3>No manager and no reports<small>${alone.length}</small></h3>${alone.map((u) => box(u, 0)).join('')}</section>` : '');
    markOrg();
  };
  const showOrg = async () => {
    say('org-problem', '');
    $('org-setup').hidden = true;
    $('org-tree').innerHTML = '';
    say('org-note', 'Reading Active Directory…');
    try {
      const info = await ready();
      say('org-note', '');
      $('org-tools').hidden = !info.state;
      if (!info.state) {
        R.adSetupCard($('org-setup'), info);
        return;
      }
      // The Users page's list when it's under a minute old, so both pages ask once.
      people = (await R.keep('/api/ad/users', 60000)).users.filter((u) => !u.system && u.enabled);
      drawOrg();
    } catch (err) {
      ad = null;
      say('org-note', '');
      say('org-problem', err.message);
    }
  };
  $('org-find').addEventListener('input', () => { if (people) markOrg(); });

  // --- Attribute browser -------------------------------------------------------------------------------------------

  let place = null; // what's in the place shown on the left
  let placeAt = null; // the DN it was asked for ('' is the top of the domain)
  let object = null;
  let asking = 0;
  const enc = encodeURIComponent;
  const dec = (s) => {
    try {
      return decodeURIComponent(s || '');
    } catch {
      return '';
    }
  };
  // A DN's parts, split on the commas that aren't escaped.
  const parts = (dn) => {
    const out = [];
    let at = 0;
    for (let i = 0; i < dn.length; i++) {
      if (dn[i] === '\\') i++;
      else if (dn[i] === ',') {
        out.push(dn.slice(at, i));
        at = i + 1;
      }
    }
    out.push(dn.slice(at));
    return out;
  };
  const ICON = { ou: 'list', container: 'list', other: 'list', user: 'user', group: 'users', computer: 'laptop' };

  const drawPlace = () => {
    // The way down from the domain: each part above the domain's own DC= parts.
    const p = parts(place.dn);
    const domainAt = p.findIndex((x) => /^DC=/i.test(x));
    const steps = [];
    for (let i = domainAt; i >= 0; i--) {
      const dn = p.slice(i).join(',');
      const label = i === domainAt ? p.slice(domainAt).map((x) => x.slice(3)).join('.') : p[i].replace(/^[A-Z]+=/i, '').replace(/\\(.)/g, '$1');
      steps.push(`<a href="#attrs/${i === domainAt ? '' : enc(dn)}">${esc(label)}</a>`);
    }
    $('at-crumbs').innerHTML = steps.join('<span>/</span>');
    $('at-items').innerHTML = place.items.map((it) => {
      const href = it.opens ? `#attrs/${enc(it.dn)}` : `#attrs/${enc(place.dn)}/${enc(it.dn)}`;
      return `<a href="${href}" data-dn="${esc(it.dn)}">${svg(ICON[it.kind] || 'list')}<span class="nm">${esc(it.name)}</span>${it.opens ? svg('right') : ''}</a>`;
    }).join('') || '<p class="side-sub">Nothing in here.</p>';
    $('at-more').hidden = !place.more;
  };
  // Marks what's picked in the list, which stays as it is and scrolled where it was.
  const markPlace = () => $('at-items').querySelectorAll('a[data-dn]').forEach((a) => {
    if (a.dataset.dn === object?.dn) a.setAttribute('aria-current', 'page');
    else a.removeAttribute('aria-current');
  });
  // Every attribute it may have, with its type; Hide empty shows only the ones with a value. Each has a menu (its …
  // button, or a right-click) to copy it and, where the service account may write it, Edit… and Clear value, as ADSI Edit.
  const drawObject = () => {
    if (!object) return;
    const find = $('at-find').value.trim().toLowerCase();
    const hide = $('at-empty').checked;
    const rows = object.attributes.filter((a) => (!hide || a.set) && (!find || a.name.toLowerCase().includes(find) || a.values.some((v) => v.toLowerCase().includes(find))));
    const withValue = object.attributes.filter((a) => a.set).length;
    $('at-title').textContent = object.name;
    $('at-line').textContent = `${object.attributes.length} attributes, ${withValue} with a value`;
    $('at-find-row').hidden = false;
    $('at-tools').hidden = false;
    $('at-table').innerHTML = '<div class="tr th"><span>Attribute</span><span>Value</span><span>Type</span><span></span></div>'
      + rows.map((a) => `<div class="tr${a.set ? '' : ' unset'}" data-attr="${esc(a.name)}"><span class="mono">${esc(a.name)}</span>`
        + `<div class="vals">${a.set ? a.values.map((v) => `<span${a.hidden ? ' class="muted"' : ''}>${esc(v)}</span>`).join('') : '<span class="muted">Not set</span>'}</div>`
        + `<span class="ty">${esc(a.type)}${a.values.length > 1 ? ` · ${a.values.length}` : ''}</span>`
        + `<button type="button" class="at-more" data-attr-menu="${esc(a.name)}" data-tip="More" aria-label="More for ${esc(a.name)}">${svg('dots')}</button></div>`).join('')
      + (rows.length ? '' : `<div class="empty">${find ? 'No attribute matches.' : 'No attribute has a value.'}</div>`);
  };

  // Copying: the clipboard, or a box to copy from where the browser won't (plain HTTP).
  const copyText = async (text, what) => {
    try {
      await navigator.clipboard.writeText(text);
      R.toast(`Copied ${what}.`);
    } catch {
      const t = document.createElement('textarea');
      t.value = text;
      document.body.append(t);
      t.select();
      const ok = document.execCommand('copy');
      t.remove();
      R.toast(ok ? `Copied ${what}.` : "Couldn't copy.", ok ? '' : 'Select it and press Ctrl+C.', { tone: ok ? '' : 'warn' });
    }
  };
  let menuFor = null;
  const openMenu = (name, x, y) => {
    menuFor = object?.attributes.find((a) => a.name === name);
    if (!menuFor) return;
    const m = $('at-menu');
    const may = R.has('ad.attributes') && menuFor.editable;
    m.querySelector('[data-at="edit"]').hidden = !may;
    m.querySelector('[data-at="clear"]').hidden = !may || !menuFor.set;
    $('at-menu-change').hidden = !may;
    m.querySelector('[data-at="value"]').hidden = !menuFor.set || menuFor.hidden;
    m.querySelector('[data-at="line"]').hidden = !menuFor.set || menuFor.hidden;
    m.style.setProperty('left', `${Math.max(8, Math.min(x, window.innerWidth - 230))}px`);
    m.style.setProperty('top', `${Math.max(8, Math.min(y, window.innerHeight - 230))}px`);
    m.showPopover();
  };
  $('at-table').addEventListener('click', (e) => {
    const b = e.target.closest('[data-attr-menu]');
    if (!b) return;
    const r = b.getBoundingClientRect();
    openMenu(b.dataset.attrMenu, r.right - 220, r.bottom + 6);
  });
  $('at-table').addEventListener('contextmenu', (e) => {
    const row = e.target.closest('.tr[data-attr]');
    if (!row) return;
    e.preventDefault();
    openMenu(row.dataset.attr, e.clientX, e.clientY);
  });
  $('at-menu').addEventListener('click', (e) => {
    const b = e.target.closest('[data-at]');
    if (!b || !menuFor) return;
    $('at-menu').hidePopover();
    const a = menuFor;
    if (b.dataset.at === 'value') copyText(a.values.join('\n'), 'the value');
    if (b.dataset.at === 'name') copyText(a.name, 'the name');
    if (b.dataset.at === 'line') copyText(`${a.name}: ${a.values.join('; ')}`, `${a.name} and its value`);
    if (b.dataset.at === 'edit') openEdit(a);
    if (b.dataset.at === 'clear') {
      R.sure(`Clear ${a.name}?`, `It's taken off ${object.name} in Active Directory.`, 'Clear', () => saveAttr(a, []));
    }
  });

  // Edit…: one box for a single value, a line each for several, TRUE or FALSE for a Boolean. Empty clears it.
  let editing = null;
  const HELP = {
    'Date and time': 'A time in UTC, written like 20261231170000.0Z.',
    'Large integer': 'A whole number. Times such as accountExpires count 100-nanosecond steps since 1601; 0 or 9223372036854775807 is never.',
    Integer: 'A whole number.',
    'Numeric string': 'Digits and spaces.',
    'Distinguished name': 'A full DN, like CN=Name,OU=Users,DC=amw,DC=com.',
    'Object identifier': 'An OID, like 1.2.840.113556.1.4.1.',
  };
  const openEdit = (a) => {
    editing = a;
    const bool = a.type === 'Boolean';
    const raw = a.raw || [];
    $('attr-title').textContent = `Edit ${a.name}`;
    $('attr-lead').textContent = `${a.type || 'Value'}, ${a.single ? 'one value' : 'one value a line'}. On ${object.name}.`;
    $('attr-one').hidden = !a.single || bool;
    $('attr-bool').hidden = !bool;
    $('attr-many').hidden = a.single || bool;
    $('attr-one').value = raw[0] || '';
    $('attr-bool').value = raw[0] || '';
    $('attr-many').value = raw.join('\n');
    $('attr-label').htmlFor = bool ? 'attr-bool' : a.single ? 'attr-one' : 'attr-many';
    $('attr-label').textContent = a.single || bool ? 'Value' : 'Values, one a line';
    $('attr-help').textContent = `${HELP[a.type] || ''}${HELP[a.type] ? ' ' : ''}Leave it empty to clear it.`;
    say('attr-problem', '');
    $('attr-dialog').showModal();
    $(bool ? 'attr-bool' : a.single ? 'attr-one' : 'attr-many').focus();
  };
  const saveAttr = async (a, values) => {
    try {
      object = await R.send('/api/ad/object/attribute', { dn: object.dn, name: a.name, values });
      drawObject();
      if ($('attr-dialog').open) $('attr-dialog').close();
      R.toast(values.some((v) => v.trim()) ? `Saved ${a.name}.` : `Cleared ${a.name}.`);
    } catch (err) {
      if ($('attr-dialog').open) say('attr-problem', err.message);
      else R.toast(`Couldn't change ${a.name}.`, err.message, { tone: 'warn' });
    }
  };
  $('attr-save').addEventListener('click', async () => {
    if (!editing) return;
    const bool = editing.type === 'Boolean';
    const values = bool ? [$('attr-bool').value] : editing.single ? [$('attr-one').value] : $('attr-many').value.split(/\r?\n/);
    $('attr-save').disabled = true;
    await saveAttr(editing, values);
    $('attr-save').disabled = false;
  });
  $('attr-cancel').addEventListener('click', () => $('attr-dialog').close());

  $('at-empty').addEventListener('change', drawObject);
  $('at-copy-dn').addEventListener('click', () => object && copyText(object.dn, 'the distinguished name'));
  // Export CSV: every attribute with a value, a row each value, the way ADSI Edit's lists copy out.
  $('at-export').addEventListener('click', () => {
    if (!object) return;
    const cell = (s) => `"${String(s).replace(/"/g, '""')}"`;
    const lines = [['Attribute', 'Value', 'Type'].map(cell).join(',')];
    object.attributes.filter((a) => a.set && !a.hidden).forEach((a) => a.values.forEach((v) => lines.push([a.name, v, a.type].map(cell).join(','))));
    const link = document.createElement('a');
    link.href = URL.createObjectURL(new Blob([`﻿${lines.join('\r\n')}\r\n`], { type: 'text/csv' }));
    link.download = `${object.name.replace(/[\\/:*?"<>|]/g, '_')} attributes.csv`;
    document.body.append(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(link.href), 4000);
  });
  const showAttrs = async () => {
    const [, , rest] = R.where();
    const at = dec(rest[0]);
    const pick = dec(rest[1]) || at;
    const n = ++asking;
    say('at-problem', '');
    $('at-setup').hidden = true;
    if (!place) $('at-title').textContent = 'Reading Active Directory…';
    try {
      const info = await ready();
      if (n !== asking) return;
      $('at-lib').hidden = !info.state;
      if (!info.state) {
        R.adSetupCard($('at-setup'), info);
        return;
      }
      // Picking something in the place already shown reads only that object.
      const same = !!place && placeAt === at;
      const here = same ? place : await R.get(`/api/ad/browse${at ? `?dn=${enc(at)}` : ''}`);
      const picked = await R.get(`/api/ad/object?dn=${enc(pick || here.dn)}`);
      // A newer click answers instead.
      if (n !== asking) return;
      place = here;
      placeAt = at;
      object = picked;
      if (!same) drawPlace();
      markPlace();
      drawObject();
    } catch (err) {
      if (n !== asking) return;
      ad = null;
      $('at-lib').hidden = false;
      $('at-title').textContent = 'Couldn\'t read it';
      say('at-problem', err.message);
    }
  };
  $('at-find').addEventListener('input', drawObject);

  const show = (pageName) => {
    // Coming back to the browser reads the place again.
    if (pageName !== 'attrs') {
      place = null;
      asking++;
    }
    if (pageName === 'org') showOrg();
    if (pageName === 'attrs') showAttrs();
  };
  document.addEventListener('rabbidlab:show', (e) => show(e.detail[0]));
  // The service account was just set up or disconnected: the page shown reads again.
  document.addEventListener('rabbidlab:ad', () => {
    ad = null;
    place = null;
    people = null;
    show(R.where()[0]);
  });
})();
