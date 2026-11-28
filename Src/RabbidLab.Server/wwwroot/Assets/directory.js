// Org chart (#org) and Attribute browser (#attrs/<place>/<object>, each a DN, encoded), read as Rabbid Lab's own AD
// account. console.js shares its helpers as window.RabbidLab, and users.js the service account setup card.
(() => {
  const R = window.RabbidLab;
  const { $, esc, svg, say, initials, closeX, section } = R;

  // Whether the service account is set up: asked until it is, then again only after it changes or a read fails.
  let ad = null;
  const ready = async () => {
    if (!ad?.state) ad = await R.adInfo();
    return ad;
  };

  // --- Org chart -----------------------------------------------------------------------------------------------------
  // NorthStack AD's chart, brought over: the organization drawn as a small city seen at an angle. Each person is a
  // building on its own slab (managers taller, with their initials on a sign), each manager's team stands on a platform,
  // and lines along the ground join them. Reporting lines come from each person's Manager in Active Directory; By
  // department groups people by Department, which works with no managers set. Clicking someone traces their line to the
  // top in the accent colour and opens them in the side panel. Drag to move, scroll or the buttons to zoom, Find goes
  // to a person, and the chart downloads as a picture. Enabled accounts only.

  let people = null;
  // mode: chart (reporting lines) or departments. focus: whose line is shown on its own ('' for everyone), and which
  // teams are open in it. view: where the chart was moved and zoomed to. o: who reports to whom (orgPeople).
  const org = { mode: 'chart', o: null, expanded: null, selected: '', view: null, fitted: '', scene: null, focus: '', focusOpen: new Set() };
  const ISO = { k: 84, c: Math.cos(Math.PI / 6), s: 0.5 };
  const LAY = { nodeW: 170, level: 250, bus: 112, gap: 36, slab: 0.42 };
  // A place on the ground (u, v, and z up) as a point on screen, and back.
  const isoAt = (u, v, z = 0) => [(u - v) * ISO.k * ISO.c, (u + v) * ISO.k * ISO.s - z];
  const isoFrom = (x, y) => [(x / (ISO.k * ISO.c) + y / (ISO.k * ISO.s)) / 2, (y / (ISO.k * ISO.s) - x / (ISO.k * ISO.c)) / 2];
  const pts = (list) => list.map(([x, y]) => `${x.toFixed(1)},${y.toFixed(1)}`).join(' ');
  const keyOf = (dn) => String(dn || '').toLowerCase();
  const nameOf = (u) => u.name || u.username;

  // Who reports to whom: the top of each line (no manager, with people reporting to them), and how many have neither.
  const orgPeople = () => {
    const users = people;
    const byDn = new Map(users.map((u) => [keyOf(u.dn), u]));
    const kids = new Map();
    const bossOf = new Map();
    users.forEach((u) => {
      const boss = u.managerDn && byDn.get(keyOf(u.managerDn));
      if (!boss || boss === u) return; // someone set as their own manager has none
      const k = keyOf(boss.dn);
      if (!kids.has(k)) kids.set(k, []);
      kids.get(k).push(u);
      bossOf.set(keyOf(u.dn), k);
    });
    const order = (a, b) => (kids.has(keyOf(b.dn)) - kids.has(keyOf(a.dn))) || nameOf(a).localeCompare(nameOf(b));
    kids.forEach((list) => list.sort(order));
    const tops = users.filter((u) => !bossOf.has(keyOf(u.dn)) && kids.has(keyOf(u.dn))).sort(order);
    // People who manage each other in a loop have no top: each loop starts at one of them, once.
    const reached = new Set();
    const walk = (k) => {
      if (reached.has(k)) return;
      reached.add(k);
      (kids.get(k) || []).forEach((c) => walk(keyOf(c.dn)));
    };
    tops.forEach((t) => walk(keyOf(t.dn)));
    users.forEach((u) => {
      if (reached.has(keyOf(u.dn)) || !kids.has(keyOf(u.dn))) return;
      tops.push(u);
      walk(keyOf(u.dn));
    });
    return { users, byDn, kids, bossOf, tops, alone: users.filter((u) => !reached.has(keyOf(u.dn))).length };
  };

  // A platform of people: how many across and deep, its size on screen, and where each stands on it (back row first).
  const teamSize = (n, most) => {
    const cols = Math.min(most, Math.ceil(Math.sqrt(n)));
    return { cols, rows: Math.ceil(n / cols) };
  };
  const platformBox = (cols, rows) => ({ w: (cols + rows) * ISO.k * ISO.c, h: (cols + rows) * ISO.k * ISO.s });
  const platform = (list, cols, rows, cx, cy, extra) => {
    const [uc, vc] = isoFrom(cx, cy);
    return {
      district: { uc, vc, cols, rows, ...extra },
      nodes: list.map((m, i) => {
        const [x, y] = isoAt(uc - (cols - 1) / 2 + (i % cols), vc - (rows - 1) / 2 + Math.floor(i / cols));
        return { m, x, y };
      }),
    };
  };

  // Reporting lines: each manager above their people. The people with no reports of their own stand on a platform
  // below them (up to six across), and each manager who reports to them is beside it, laid out the same way.
  const orgLines = (o, opened = null) => {
    if (!opened && !org.expanded) {
      // At first: the top two levels, or everyone in a small organization.
      org.expanded = new Set();
      const all = o.users.length <= 80;
      const open = (u, depth) => {
        const k = keyOf(u.dn);
        if (!o.kids.has(k) || (!all && depth > 1)) return;
        org.expanded.add(k);
        o.kids.get(k).forEach((c) => open(c, depth + 1));
      };
      o.tops.forEach((t) => open(t, 0));
    }
    const isOpen = opened || org.expanded;
    const place = (u, depth, seen) => {
      const k = keyOf(u.dn);
      const reports = seen.has(k) ? [] : o.kids.get(k) || [];
      const node = { user: u, key: k, reports: reports.length, open: reports.length > 0 && isOpen.has(k), depth };
      if (!node.open) return { w: LAY.nodeW, h: 120, x: LAY.nodeW / 2, nodes: [{ ...node, x: LAY.nodeW / 2, y: 0 }], links: [], districts: [] };
      const inner = new Set(seen).add(k);
      const leaves = reports.filter((r) => !o.kids.has(keyOf(r.dn)));
      const parts = [];
      if (leaves.length) {
        const { cols, rows } = teamSize(leaves.length, 6);
        const box = platformBox(cols, rows);
        parts.push({ team: true, cols, rows, members: leaves, box, w: Math.max(LAY.nodeW, box.w + 70), h: box.h + 90 });
      }
      reports.filter((r) => o.kids.has(keyOf(r.dn))).forEach((r) => parts.push(place(r, depth + 1, inner)));
      const width = parts.reduce((t, p) => t + p.w, 0) + LAY.gap * (parts.length - 1);
      const out = { w: Math.max(LAY.nodeW, width), h: 0, nodes: [], links: [], districts: [] };
      out.x = out.w / 2;
      out.nodes.push({ ...node, x: out.x, y: 0 });
      let at = (out.w - width) / 2;
      const drops = [];
      parts.forEach((p) => {
        if (p.team) {
          const cx = at + p.w / 2;
          const top = LAY.level - 40;
          const made = platform(p.members, p.cols, p.rows, cx, top + p.box.h / 2, { team: k });
          made.nodes.forEach(({ m, x, y }) => out.nodes.push({ user: m, key: keyOf(m.dn), reports: 0, open: false, depth: depth + 1, x, y, boss: k }));
          out.districts.push(made.district);
          drops.push({ x: cx, y: top, team: k });
        } else {
          p.nodes.forEach((n) => out.nodes.push({ ...n, x: n.x + at, y: n.y + LAY.level }));
          p.links.forEach((l) => out.links.push({ ...l, pts: l.pts.map(([x, y]) => [x + at, y + LAY.level]) }));
          const [du, dv] = isoFrom(at, LAY.level);
          p.districts.forEach((d) => out.districts.push({ ...d, uc: d.uc + du, vc: d.vc + dv }));
          drops.push({ x: at + p.x, y: LAY.level - LAY.slab * 2 * ISO.k * ISO.s, to: p.nodes[0].key });
        }
        out.h = Math.max(out.h, LAY.level + p.h);
        at += p.w + LAY.gap;
      });
      drops.forEach((d) => out.links.push({ from: k, to: d.to || '', team: d.team || '', pts: [[out.x, 0], [out.x, LAY.bus], [d.x, LAY.bus], [d.x, d.y]] }));
      return out;
    };
    const scene = { nodes: [], links: [], districts: [] };
    let at = 0;
    o.tops.forEach((t) => {
      const p = place(t, 0, new Set());
      const [du, dv] = isoFrom(at, 0);
      p.nodes.forEach((n) => scene.nodes.push({ ...n, x: n.x + at }));
      p.links.forEach((l) => scene.links.push({ ...l, pts: l.pts.map(([x, y]) => [x + at, y]) }));
      p.districts.forEach((d) => scene.districts.push({ ...d, uc: d.uc + du, vc: d.vc + dv }));
      at += p.w + 90;
    });
    return scene;
  };

  // By department: a platform for each department (largest first), its people in rows of up to eight, the platforms in
  // rows across, each named below.
  const orgDepartments = (o) => {
    const groups = new Map();
    o.users.forEach((u) => {
      const d = (u.department || '').trim() || 'No department';
      if (!groups.has(d)) groups.set(d, []);
      groups.get(d).push(u);
    });
    const list = [...groups.entries()].sort((a, b) => b[1].length - a[1].length || a[0].localeCompare(b[0]));
    const scene = { nodes: [], links: [], districts: [] };
    const across = Math.max(1400, Math.sqrt(o.users.length) * 330);
    let x = 0;
    let y = 0;
    let rowH = 0;
    list.forEach(([name, members]) => {
      members.sort((a, b) => (o.kids.has(keyOf(b.dn)) - o.kids.has(keyOf(a.dn))) || nameOf(a).localeCompare(nameOf(b)));
      const { cols, rows } = teamSize(members.length, 8);
      const box = platformBox(cols, rows);
      const w = Math.max(220, box.w + 60);
      if (x > 0 && x + w > across) {
        x = 0;
        y += rowH + 60;
        rowH = 0;
      }
      const made = platform(members, cols, rows, x + w / 2, y + 90 + box.h / 2, { label: name, count: members.length, below: y + 90 + box.h + 64, x: x + w / 2 });
      made.nodes.forEach(({ m, x: nx, y: ny }) => scene.nodes.push({ user: m, key: keyOf(m.dn), reports: (o.kids.get(keyOf(m.dn)) || []).length, open: false, depth: 1, x: nx, y: ny }));
      scene.districts.push(made.district);
      rowH = Math.max(rowH, box.h + 160);
      x += w + 40;
    });
    return scene;
  };

  // One person's reporting line: each of their managers in turn from the top down, then them and their direct reports
  // (one of those with a team of their own shows it closed, to open). The whole organization, cut down to that.
  const orgLine = (o, key) => {
    const chain = [];
    const seen = new Set();
    for (let k = key; k && !seen.has(k); k = o.bossOf.get(k)) {
      seen.add(k);
      chain.unshift(k);
    }
    const kids = new Map();
    chain.slice(0, -1).forEach((k, i) => kids.set(k, [o.byDn.get(chain[i + 1])]));
    (o.kids.get(key) || []).forEach((r) => {
      const rk = keyOf(r.dn);
      if (o.kids.has(rk)) kids.set(rk, o.kids.get(rk));
    });
    if (o.kids.has(key)) kids.set(key, o.kids.get(key));
    return { ...o, kids, tops: [o.byDn.get(chain[0])], chain };
  };
  // The line from the selected person up to the top.
  const orgChain = (o) => {
    const chain = new Set();
    for (let k = org.selected; k && !chain.has(k); k = o.bossOf.get(k)) chain.add(k);
    return chain;
  };

  // The scene as SVG, back to front: platforms, lines on the ground, then buildings, then names over everything.
  const orgSvg = (o, scene) => {
    const chain = orgChain(o);
    const slab = (u0, v0, u1, v1, cls, depth) => {
      const [back, right, front, left] = [isoAt(u0, v0), isoAt(u1, v0), isoAt(u1, v1), isoAt(u0, v1)];
      const down = ([x, y]) => [x, y + depth];
      return `<polygon class="${cls}-l" points="${pts([left, front, down(front), down(left)])}"/>`
        + `<polygon class="${cls}-r" points="${pts([front, right, down(right), down(front)])}"/>`
        + `<polygon class="${cls}" points="${pts([back, right, front, left])}"/>`;
    };
    const out = [];
    scene.districts.slice().sort((a, b) => (a.uc + a.vc) - (b.uc + b.vc)).forEach((d) => {
      const [u0, u1, v0, v1] = [d.uc - d.cols / 2 + 0.02, d.uc + d.cols / 2 - 0.02, d.vc - d.rows / 2 + 0.02, d.vc + d.rows / 2 - 0.02];
      out.push(`<g class="org-district${d.team && chain.has(d.team) && org.selected !== d.team ? ' is-chain' : ''}">${slab(u0, v0, u1, v1, 'org-plat', 7)}</g>`);
    });
    const lit = [];
    scene.links.forEach((l) => {
      const on = chain.has(l.to) || (l.team && chain.has(l.team) && scene.nodes.some((n) => n.boss === l.team && n.key === org.selected));
      const line = `<polyline class="org-link${on ? ' is-chain' : ''}" points="${pts(l.pts)}"/>`;
      if (on) lit.push(line + l.pts.slice(1, -1).map(([x, y]) => `<circle class="org-dot" cx="${x.toFixed(1)}" cy="${y.toFixed(1)}" r="4"/>`).join(''));
      else out.push(line);
    });
    out.push(...lit);
    const nodes = scene.nodes.map((n) => {
      const [u, v] = isoFrom(n.x, n.y);
      return { ...n, u, v };
    }).sort((a, b) => a.y - b.y);
    const S = LAY.slab;
    nodes.forEach((n) => {
      const on = chain.has(n.key);
      const f = n.reports ? 0.25 : 0.2;
      const h = n.reports ? Math.min(70, 30 + n.reports * 2.2) + (n.depth === 0 ? 10 : 0) : 18;
      const back = (z) => isoAt(n.u - f, n.v - f, z);
      const right = (z) => isoAt(n.u + f, n.v - f, z);
      const front = (z) => isoAt(n.u + f, n.v + f, z);
      const left = (z) => isoAt(n.u - f, n.v + f, z);
      const door = [isoAt(n.u - f * 0.2, n.v + f, 0), isoAt(n.u + f * 0.25, n.v + f, 0), isoAt(n.u + f * 0.25, n.v + f, 9), isoAt(n.u - f * 0.2, n.v + f, 9)];
      const [tx, ty] = isoAt(n.u, n.v, h);
      const sign = n.reports ? `<line class="org-stem" x1="${tx.toFixed(1)}" y1="${ty.toFixed(1)}" x2="${tx.toFixed(1)}" y2="${(ty - 16).toFixed(1)}"/>`
        + `<rect class="org-sign" x="${(tx - 15).toFixed(1)}" y="${(ty - 38).toFixed(1)}" width="30" height="22" rx="4"/>`
        + `<text class="org-sign-text" x="${tx.toFixed(1)}" y="${(ty - 23).toFixed(1)}" text-anchor="middle">${esc(initials(nameOf(n.user)))}</text>` : '';
      out.push(`<g class="org-node${on ? ' is-chain' : ''}${n.key === org.selected ? ' is-selected' : ''}${n.reports ? ' is-manager' : ''}" data-org-node="${esc(n.key)}" tabindex="0" role="button" aria-label="${esc(nameOf(n.user))}">`
        + `<g class="org-tile">${slab(n.u - S, n.v - S, n.u + S, n.v + S, 'org-slab', 5)}</g>`
        + `<polygon class="org-left" points="${pts([left(0), front(0), front(h), left(h)])}"/>`
        + `<polygon class="org-right" points="${pts([front(0), right(0), right(h), front(h)])}"/>`
        + `<polygon class="org-top" points="${pts([back(h), right(h), front(h), left(h)])}"/>`
        + `<polygon class="org-door" points="${pts(door)}"/>${sign}</g>`);
    });
    nodes.forEach((n) => {
      const x = n.x;
      const y = n.y + S * 2 * ISO.k * ISO.s + 5;
      const title = n.user.title || '';
      // Under a manager on Reporting lines: a pill that shows or hides their team.
      const more = n.reports && org.mode === 'chart'
        ? `<g class="org-more" data-org-toggle="${esc(n.key)}" role="button" aria-label="${n.open ? 'Hide' : 'Show'} direct reports of ${esc(nameOf(n.user))}">`
          + `<rect x="${(x - 34).toFixed(1)}" y="${(y + (title ? 38 : 24)).toFixed(1)}" width="68" height="19" rx="9.5"/>`
          + `<text x="${x.toFixed(1)}" y="${(y + (title ? 51.5 : 37.5)).toFixed(1)}" text-anchor="middle">${n.open ? 'Hide team' : `+${n.reports} ${n.reports === 1 ? 'report' : 'reports'}`}</text></g>` : '';
      out.push(`<g class="org-label${chain.has(n.key) ? ' is-chain' : ''}" data-org-node="${esc(n.key)}">`
        + `<text class="org-name" x="${x.toFixed(1)}" y="${(y + 14).toFixed(1)}" text-anchor="middle">${esc(nameOf(n.user))}</text>`
        + (title ? `<text class="org-title" x="${x.toFixed(1)}" y="${(y + 28).toFixed(1)}" text-anchor="middle">${esc(title.length > 32 ? `${title.slice(0, 31)}…` : title)}</text>` : '')
        + `</g>${more}`);
    });
    scene.districts.filter((d) => d.label).forEach((d) => {
      out.push(`<text class="org-dept" x="${d.x.toFixed(1)}" y="${d.below.toFixed(1)}" text-anchor="middle">${esc(d.label)} <tspan class="org-dept-n">${d.count}</tspan></text>`);
    });
    return out.join('');
  };

  // How much room a scene takes, for Fit and for the downloads.
  const orgBounds = (scene) => {
    const xs = [];
    const ys = [];
    scene.nodes.forEach((n) => {
      xs.push(n.x - 90, n.x + 90);
      ys.push(n.y - 120, n.y + 100);
    });
    scene.districts.forEach((d) => {
      [[-d.cols / 2, -d.rows / 2], [d.cols / 2, -d.rows / 2], [d.cols / 2, d.rows / 2], [-d.cols / 2, d.rows / 2]].forEach(([a, b]) => {
        const [x, y] = isoAt(d.uc + a, d.vc + b);
        xs.push(x);
        ys.push(y + 10);
      });
      if (d.label) ys.push(d.below + 10);
    });
    if (!xs.length) return { x: 0, y: 0, w: 1, h: 1 };
    const [x, y] = [Math.min(...xs) - 30, Math.min(...ys) - 30];
    return { x, y, w: Math.max(...xs) + 30 - x, h: Math.max(...ys) + 30 - y };
  };

  const orgApply = () => {
    const v = org.view;
    document.getElementById('org-world')?.setAttribute('transform', `translate(${v.x.toFixed(1)} ${v.y.toFixed(1)}) scale(${v.s.toFixed(4)})`);
  };
  // Fit: the whole chart. readable: the whole chart when it can be read like that, otherwise the top of it.
  const orgFit = (readable) => {
    const scene = org.scene;
    if (!scene?.nodes.length) return;
    const b = orgBounds(scene);
    const r = $('org-stage').getBoundingClientRect();
    const s = Math.min(1, Math.max(0.1, Math.min((r.width - 40) / b.w, (r.height - 40) / b.h)));
    if (readable && s < 0.6) {
      const top = scene.nodes.reduce((a, n) => (n.y < a.y ? n : a), scene.nodes[0]);
      org.view = org.mode === 'chart' ? { s: 0.75, x: r.width / 2 - top.x * 0.75, y: 150 - top.y * 0.75 } : { s: 0.75, x: 20 - b.x * 0.75, y: 20 - b.y * 0.75 };
    } else org.view = { s, x: (r.width - b.w * s) / 2 - b.x * s, y: (r.height - b.h * s) / 2 - b.y * s };
    orgApply();
  };
  const orgZoom = (by, px, py) => {
    if (!org.view) return;
    const r = $('org-stage').getBoundingClientRect();
    const [x, y] = [px ?? r.width / 2, py ?? r.height / 2];
    const s = Math.min(2.5, Math.max(0.1, org.view.s * by));
    org.view.x = x - (x - org.view.x) * (s / org.view.s);
    org.view.y = y - (y - org.view.y) * (s / org.view.s);
    org.view.s = s;
    orgApply();
  };

  const drawOrg = () => {
    const o = org.o;
    if (!o) return;
    // No reporting lines at all: By department is the chart there is.
    const none = !o.tops.length;
    if (none && !org.focus) org.mode = 'departments';
    const lines = org.mode === 'chart';
    if (org.focus && !o.byDn.has(org.focus)) org.focus = '';
    const focused = lines && org.focus ? o.byDn.get(org.focus) : null;
    $('org-tabs').innerHTML = [['chart', 'org', 'Reporting lines'], ['departments', 'users', 'By department']]
      .map(([k, icon, label]) => `<a href="#org" data-org-mode="${k}"${org.mode === k ? ' aria-current="true"' : ''}>${svg(icon)}${label}</a>`).join('');
    $('org-count').textContent = focused ? `Reporting line of ${nameOf(focused)}`
      : lines ? o.alone ? `${o.users.length - o.alone} in reporting lines · ${o.alone} not in a reporting line` : `${o.users.length} users`
        : `${o.users.length} users`;
    $('org-whole').hidden = !focused;
    document.querySelectorAll('#org-tools [data-org-all]').forEach((b) => { b.hidden = !lines || !!focused || none; });
    say('org-note', none ? 'No users have a Manager set in Active Directory, so users are shown by department. Set the Manager attribute on each user to show reporting lines.' : '');
    org.scene = !lines ? orgDepartments(o) : focused ? orgLines(orgLine(o, org.focus), org.focusOpen) : orgLines(o);
    $('org-stage').hidden = false;
    $('org-svg').innerHTML = `<g id="org-world">${orgSvg(o, org.scene)}</g>`;
    const shown = `${org.mode}:${org.focus}`;
    if (org.view && org.fitted === shown) {
      orgApply();
      return;
    }
    // One reporting line is shown whole; the whole organization, whole when it can be read like that.
    org.fitted = shown;
    orgFit(!focused);
    // A line too long to read whole: readable, centred on the person.
    const me = focused && org.view.s < 0.6 && org.scene.nodes.find((n) => n.key === org.focus);
    if (me) {
      const r = $('org-stage').getBoundingClientRect();
      org.view = { s: 0.6, x: r.width / 2 - me.x * 0.6, y: r.height / 2 - me.y * 0.6 };
      orgApply();
    }
  };

  // The selected person in the side panel: who they are, who they report to and who reports to them. again: left where it is.
  const drawPerson = (again = false) => {
    const o = org.o;
    const u = org.selected && o?.byDn.get(org.selected);
    if (!u) {
      if (R.panel.is('org')) R.panel.close();
      return;
    }
    const boss = o.byDn.get(o.bossOf.get(org.selected));
    const reports = o.kids.get(org.selected) || [];
    const person = (p) => `<div class="mrow acc-row" data-org-go="${esc(keyOf(p.dn))}"><span class="pic small user">${esc(initials(nameOf(p)))}</span>`
      + `<div class="two"><b>${esc(nameOf(p))}</b><small>${esc([p.title, p.department].filter(Boolean).join(' · ') || p.username)}</small></div>${svg('right', 'acc-go')}</div>`;
    const line = (label, value) => (value ? `<li><b>${label}</b><span>${esc(value)}</span></li>` : '');
    R.panel.body.innerHTML = `<div class="dw-head"><span class="pic dw-pic user">${esc(initials(nameOf(u)))}</span><div class="dw-name"><h2 id="dw-title">${esc(nameOf(u))}</h2><p>${esc(u.title || u.username)}</p></div>`
      + `${closeX}</div>`
      + `<div class="dw-bar">${org.mode !== 'chart' || org.focus !== org.selected ? `<button type="button" class="dw-qa" data-org-focus="${esc(org.selected)}">${svg('org')}<span>Show only this reporting line</span></button>` : ''}`
      + `<button type="button" class="dw-qa" data-org-open="${esc(u.username)}">${svg('user')}<span>Open in Users</span></button></div>`
      + section('Details', `<ul class="does">${line('Username', u.username)}${line('Department', u.department || 'Not set')}${line('Email', u.email)}${line('Office', u.office)}${line('Phone', u.phone)}</ul>`)
      + section('Reports to', boss ? `<div class="dw-list">${person(boss)}</div>` : '<p class="muted dw-empty">No manager set.</p>')
      + section(`Direct reports <small>${reports.length}</small>`, reports.length ? `<div class="dw-list">${reports.map(person).join('')}</div>` : '<p class="muted dw-empty">No direct reports.</p>');
    if (!again) R.panel.open('org', org.selected);
  };
  const orgSelect = (key) => {
    org.selected = key;
    drawOrg();
    drawPerson();
  };
  const orgToggle = (key) => {
    const open = org.mode === 'chart' && org.focus ? org.focusOpen : org.expanded;
    if (!open) return;
    if (open.has(key)) open.delete(key);
    else open.add(key);
    drawOrg();
  };
  // One person's reporting line on its own.
  const orgFocus = (key) => {
    if (!org.o?.byDn.has(key)) return;
    Object.assign(org, { mode: 'chart', focus: key, selected: key, focusOpen: new Set(orgLine(org.o, key).chain), fitted: '' });
    drawOrg();
    drawPerson();
  };
  // Brings someone into view: their managers' teams open, them selected, and the chart centred on them.
  const orgReveal = (key) => {
    const o = org.o;
    if (!o?.byDn.has(key)) return;
    if (org.mode === 'chart' && org.expanded) for (let k = o.bossOf.get(key); k && !org.expanded.has(k); k = o.bossOf.get(k)) org.expanded.add(k);
    org.selected = key;
    drawOrg();
    drawPerson();
    const n = org.scene.nodes.find((x) => x.key === key);
    if (!n) return;
    const r = $('org-stage').getBoundingClientRect();
    org.view.s = Math.max(org.view.s, 0.8);
    org.view.x = r.width / 2 - n.x * org.view.s;
    org.view.y = r.height / 2 - n.y * org.view.s;
    orgApply();
  };
  // Going to someone: within one line shown on its own, theirs; otherwise wherever they are on the chart.
  const orgGo = (key) => (org.mode === 'chart' && org.focus && key !== org.focus ? orgFocus(key) : orgReveal(key));

  // Find: the first few people whose name, username, title or department has every word typed. Picking one shows their
  // line on Reporting lines, or goes to them on By department.
  let matches = [];
  const orgFind = () => {
    const words = $('org-find').value.trim().toLowerCase().split(/\s+/).filter(Boolean);
    matches = words.length && org.o ? org.o.users.filter((u) => words.every((w) => `${u.name || ''} ${u.username} ${u.title || ''} ${u.department || ''}`.toLowerCase().includes(w))).slice(0, 8) : [];
    $('org-matches').hidden = !words.length;
    $('org-matches').innerHTML = matches.length ? matches.map((u) => `<button type="button" class="org-match" data-org-pick="${esc(keyOf(u.dn))}"><b>${esc(nameOf(u))}</b>`
      + `<small>${esc([u.title, u.department].filter(Boolean).join(' · ') || u.username)}</small></button>`).join('') : '<p class="org-nomatch">No matches.</p>';
  };
  const orgPick = (key) => {
    $('org-matches').hidden = true;
    if (org.mode === 'chart' && org.o?.tops.length) orgFocus(key);
    else orgReveal(key);
  };
  $('org-find').addEventListener('input', orgFind);
  $('org-find').addEventListener('focus', () => { if ($('org-find').value.trim()) orgFind(); });
  $('org-find').addEventListener('keydown', (e) => {
    if (e.key === 'Enter' && matches.length) orgPick(keyOf(matches[0].dn));
    if (e.key === 'Escape') $('org-matches').hidden = true;
  });

  // Downloads: the whole chart in the console's colours, as SVG or as a PNG at twice the size. Styles come from the
  // style sheet, so each shape is given the ones it was drawn with before the copy leaves the page.
  const orgExport = (kind) => {
    const drawn = $('org-svg');
    if (!org.scene?.nodes.length) return;
    const b = orgBounds(org.scene);
    const copy = drawn.cloneNode(true);
    const from = [drawn, ...drawn.querySelectorAll('*')];
    const to = [copy, ...copy.querySelectorAll('*')];
    from.forEach((el, i) => {
      const cs = getComputedStyle(el);
      ['fill', 'stroke', 'stroke-width', 'stroke-dasharray', 'opacity', 'font-family', 'font-size', 'font-weight', 'paint-order', 'stroke-linejoin'].forEach((p) => {
        const v = cs.getPropertyValue(p);
        if (v && v !== 'none' && v !== 'normal') to[i].setAttribute(p, v);
        else if (p === 'fill' || p === 'stroke') to[i].setAttribute(p, v || 'none');
      });
      ['class', 'data-org-node', 'data-org-toggle', 'tabindex', 'id'].forEach((a) => to[i].removeAttribute(a));
    });
    copy.firstElementChild.removeAttribute('transform');
    copy.setAttribute('xmlns', 'http://www.w3.org/2000/svg');
    copy.setAttribute('viewBox', `${b.x.toFixed(0)} ${b.y.toFixed(0)} ${b.w.toFixed(0)} ${b.h.toFixed(0)}`);
    copy.setAttribute('width', b.w.toFixed(0));
    copy.setAttribute('height', b.h.toFixed(0));
    const bg = document.createElementNS('http://www.w3.org/2000/svg', 'rect');
    ['x', 'y', 'width', 'height'].forEach((a, i) => bg.setAttribute(a, [b.x, b.y, b.w, b.h][i].toFixed(0)));
    bg.setAttribute('fill', getComputedStyle(document.body).getPropertyValue('--night').trim() || '#0B0C14');
    copy.insertBefore(bg, copy.firstChild);
    const xml = new XMLSerializer().serializeToString(copy);
    const name = `Org chart ${org.mode === 'chart' ? '' : 'by department '}${new Date().toISOString().slice(0, 10)}`;
    const save = (blob, ext) => {
      const a = document.createElement('a');
      a.href = URL.createObjectURL(blob);
      a.download = `${name}.${ext}`;
      document.body.append(a);
      a.click();
      a.remove();
      setTimeout(() => URL.revokeObjectURL(a.href), 10000);
    };
    if (kind === 'svg') {
      save(new Blob([xml], { type: 'image/svg+xml' }), 'svg');
      return;
    }
    const img = new Image();
    img.onload = () => {
      const scale = Math.min(2, 8000 / Math.max(b.w, b.h));
      const canvas = document.createElement('canvas');
      canvas.width = Math.round(b.w * scale);
      canvas.height = Math.round(b.h * scale);
      const ctx = canvas.getContext('2d');
      ctx.scale(scale, scale);
      ctx.drawImage(img, 0, 0, b.w, b.h);
      canvas.toBlob((blob) => { if (blob) save(blob, 'png'); }, 'image/png');
    };
    img.onerror = () => R.toast('Could not create the image.', 'The SVG download is still available.', { tone: 'warn' });
    img.src = `data:image/svg+xml;charset=utf-8,${encodeURIComponent(xml)}`;
  };

  // The stage: drag to move, scroll to zoom where the pointer is, click a person or a team's pill.
  {
    const stage = $('org-stage');
    let drag = null;
    stage.addEventListener('pointerdown', (e) => {
      if (e.button !== 0 || !org.view) return;
      drag = { x: e.clientX, y: e.clientY, vx: org.view.x, vy: org.view.y, moved: false };
      stage.setPointerCapture(e.pointerId);
    });
    stage.addEventListener('pointermove', (e) => {
      if (!drag) return;
      const [dx, dy] = [e.clientX - drag.x, e.clientY - drag.y];
      if (!drag.moved && Math.hypot(dx, dy) < 4) return;
      drag.moved = true;
      stage.classList.add('is-dragging');
      org.view.x = drag.vx + dx;
      org.view.y = drag.vy + dy;
      orgApply();
    });
    stage.addEventListener('pointerup', (e) => {
      if (!drag) return;
      const was = drag;
      drag = null;
      stage.classList.remove('is-dragging');
      if (was.moved) return;
      const hit = document.elementFromPoint(e.clientX, e.clientY);
      const toggle = hit?.closest('[data-org-toggle]');
      const node = hit?.closest('[data-org-node]');
      if (toggle) orgToggle(toggle.getAttribute('data-org-toggle'));
      else if (node) orgSelect(node.getAttribute('data-org-node'));
      else if (org.selected) orgSelect('');
    });
    stage.addEventListener('pointercancel', () => {
      drag = null;
      stage.classList.remove('is-dragging');
    });
    stage.addEventListener('wheel', (e) => {
      e.preventDefault();
      const r = stage.getBoundingClientRect();
      orgZoom(Math.exp(-e.deltaY * 0.0015), e.clientX - r.left, e.clientY - r.top);
    }, { passive: false });
    stage.addEventListener('keydown', (e) => {
      const node = e.target.closest?.('[data-org-node]');
      if (node && (e.key === 'Enter' || e.key === ' ')) {
        e.preventDefault();
        orgSelect(node.getAttribute('data-org-node'));
      }
    });
  }
  document.addEventListener('click', (e) => {
    const t = e.target;
    const mode = t.closest('[data-org-mode]');
    if (mode) {
      e.preventDefault();
      if (org.mode === mode.dataset.orgMode && !org.focus) return;
      Object.assign(org, { mode: mode.dataset.orgMode, focus: '', fitted: '' });
      drawOrg();
      drawPerson();
      return;
    }
    const zoom = t.closest('[data-org-zoom]');
    if (zoom) {
      if (zoom.dataset.orgZoom === 'fit') orgFit(false);
      else orgZoom(zoom.dataset.orgZoom === 'in' ? 1.25 : 0.8);
      return;
    }
    const all = t.closest('[data-org-all]');
    if (all && org.o) {
      org.expanded = new Set(all.dataset.orgAll === 'open' ? [...org.o.kids.keys()] : org.o.tops.map((u) => keyOf(u.dn)));
      org.fitted = '';
      drawOrg();
      return;
    }
    if (t.closest('#org-whole')) {
      // Back to everyone, with the person still selected and in view.
      const key = org.focus;
      Object.assign(org, { focus: '', fitted: '' });
      drawOrg();
      if (key) orgReveal(key);
      return;
    }
    const out = t.closest('[data-org-export]');
    if (out) {
      $('org-export-menu').hidePopover();
      orgExport(out.dataset.orgExport);
      return;
    }
    const pick = t.closest('[data-org-pick]');
    if (pick) {
      orgPick(pick.dataset.orgPick);
      return;
    }
    if (!t.closest('#org-matches, #org-find')) $('org-matches').hidden = true;
    if (!R.panel.is('org')) return;
    const go = t.closest('[data-org-go]');
    const focus = t.closest('[data-org-focus]');
    const open = t.closest('[data-org-open]');
    if (go) orgGo(go.dataset.orgGo);
    else if (focus) orgFocus(focus.dataset.orgFocus);
    else if (open) R.adOpenUser?.(open.dataset.orgOpen);
  });
  // The panel closed from its own X or Escape: nobody is selected any more.
  R.panel.el.addEventListener('close', () => {
    if (R.panel.owner !== 'org' || !org.selected) return;
    org.selected = '';
    if (R.where()[0] === 'org') drawOrg();
  });

  const showOrg = async () => {
    say('org-problem', '');
    say('org-note', '');
    $('org-setup').hidden = true;
    // Until the people are read: grey placeholders where the chart goes, not a sentence.
    if (!org.o) {
      $('org-stage').hidden = true;
      $('org-tools').hidden = true;
      R.skeleton($('org-wait'), 'tiles', 8);
    }
    try {
      const info = await ready();
      if (!info.state) {
        R.skeleton($('org-wait'), '');
        $('org-stage').hidden = true;
        $('org-tools').hidden = true;
        R.adSetupCard($('org-setup'), info);
        return;
      }
      // The Users page's list when it's under a minute old, so both pages ask once.
      people = (await R.keep('/api/ad/users', 60000)).users.filter((u) => !u.system && u.enabled);
      org.o = orgPeople();
      R.skeleton($('org-wait'), '');
      $('org-tools').hidden = false;
      drawOrg();
    } catch (err) {
      ad = null;
      R.skeleton($('org-wait'), '');
      say('org-problem', err.message);
    }
  };
  // While it's open: the people every minute, drawn again only when what the chart shows changed. Where it's moved to stays.
  R.live({
    page: () => R.where()[0] === 'org',
    hold: () => !org.o || !ad?.state,
    every: 60000,
    read: async () => (await R.keep('/api/ad/users', 30000)).users.filter((u) => !u.system && u.enabled),
    now: () => people,
    key: (list) => list.map((u) => [u.dn, u.managerDn, u.name, u.username, u.title, u.department, u.email, u.office, u.phone]),
    draw: (list) => {
      people = list;
      org.o = orgPeople();
      drawOrg();
      if (R.panel.is('org')) drawPerson(true);
    },
    areas: () => [$('org-stage')],
  });

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
  const ICON = { ou: 'folder', container: 'folder', other: 'folder', user: 'user', group: 'users', computer: 'laptop' };

  const drawPlace = () => {
    if (finding.length >= 2) {
      markPlace(); // what was found stays while the find has something in it
      return;
    }
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
      return `<a href="${href}" data-dn="${esc(it.dn)}">${svg(ICON[it.kind] || 'folder')}<span class="nm">${esc(it.name)}</span>${it.opens ? svg('right') : ''}</a>`;
    }).join('') || '<p class="side-sub">This container is empty.</p>';
    $('at-more').hidden = !place.more;
  };
  // Marks what's picked in the list, which stays as it is and scrolled where it was.
  const markPlace = () => $('at-items').querySelectorAll('a[data-dn]').forEach((a) => {
    if (a.dataset.dn === object?.dn) a.setAttribute('aria-current', 'page');
    else a.removeAttribute('aria-current');
  });
  // Find anything in the domain by name: while the box has something in it, what matches shows in place of the tree.
  let finding = '';
  let findTimer = null;
  let findAsk = 0;
  const parentOf = (dn) => parts(dn).slice(1).join(',');
  const drawFound = (list) => {
    $('at-crumbs').innerHTML = `<span class="at-found">${list.length ? `${list.length >= 60 ? '60 or more' : list.length} found` : 'No results'}</span>`;
    $('at-items').innerHTML = list.map((m) => {
      const opens = m.kind === 'ou' || m.kind === 'container';
      const href = opens ? `#attrs/${enc(m.dn)}` : `#attrs/${enc(parentOf(m.dn))}/${enc(m.dn)}`;
      return `<a href="${href}" data-dn="${esc(m.dn)}">${svg(ICON[m.kind] || 'folder')}<span class="nm">${esc(m.name)}<small>${esc(m.where || '')}</small></span></a>`;
    }).join('') || '<p class="side-sub">No objects found.</p>';
    $('at-more').hidden = true;
    markPlace();
  };
  $('at-obj').addEventListener('input', () => {
    clearTimeout(findTimer);
    finding = $('at-obj').value.trim();
    if (finding.length < 2) {
      findAsk++;
      if (place) drawPlace();
      markPlace();
      return;
    }
    findTimer = setTimeout(async () => {
      const mine = ++findAsk;
      try {
        const list = await R.get(`/api/ad/object/find?q=${enc(finding)}`);
        if (mine === findAsk) drawFound(list);
      } catch (err) {
        if (mine === findAsk) say('at-problem', err.message);
      }
    }, 250);
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
    const ro = R.attrs.locked(object);
    $('at-line').textContent = `${object.attributes.length} attributes, ${withValue} with a value${ro ? `. Read-only: ${ro}` : ''}`;
    $('at-find-row').hidden = false;
    $('at-tools').hidden = false;
    attrEmpty = find ? 'No matching attributes.' : 'No attributes have a value.';
    attrGrid.draw(rows);
  };
  // The attributes as a data grid (grid.js), with a menu at the end of each row.
  let attrEmpty = '';
  const attrGrid = R.grid({
    el: $('at-table'),
    key: 'attrs',
    cols: [
      { key: 'name', label: 'Attribute', w: 220, fixed: true, get: (a) => a.name, cell: (a) => `<span class="mono ell">${esc(a.name)}</span>` },
      // One line in the row, however many values: the whole list is under More (Copy value, Edit).
      { key: 'value', label: 'Value', w: 420, on: true, get: (a) => a.values.join(' '),
        cell: (a) => (a.set ? `<span class="ell${a.hidden ? ' muted' : ''}">${esc(a.values.join(', '))}</span>` : '<span class="muted">Not set</span>') },
      { key: 'type', label: 'Type', w: 150, on: true, get: (a) => a.type, cell: (a) => `<span class="ty ell">${esc(a.type)}</span>` },
      { key: 'count', label: 'Values', w: 80, on: true, get: (a) => a.values.length },
    ],
    tail: { w: 30, cell: (a) => `<button type="button" class="at-more" data-attr-menu="${esc(a.name)}" data-tip="More" aria-label="More for ${esc(a.name)}">${svg('dots')}</button>` },
    row: (a) => ` data-attr="${esc(a.name)}"`,
    rowClass: (a) => (a.set ? '' : 'unset'),
    empty: () => attrEmpty,
    redraw: () => drawObject(),
  });

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
      R.toast(ok ? `Copied ${what}.` : 'Could not copy to the clipboard.', ok ? '' : 'Select the text and press Ctrl+C.', { tone: ok ? '' : 'warn' });
    }
  };
  let menuFor = null;
  // Whose attributes the menu and Edit… change: the browser's object, or one a side panel shows (R.attrs.menu).
  const browser = { get: () => object, set: (o) => { object = o; }, draw: () => drawObject() };
  let on = browser;
  const openMenu = (name, x, y, t = browser) => {
    on = t;
    menuFor = t.get()?.attributes.find((a) => a.name === name);
    if (!menuFor) return;
    const m = $('at-menu');
    // Edit… and Clear always show; when they can't be used they're greyed out and say why (the server's reason).
    const why = !R.has('ad.attributes') ? 'Your role does not include the Edit attributes permission.' : menuFor.editable ? '' : menuFor.why || 'This attribute is read-only.';
    const off = (b, reason) => {
      if (reason) b.setAttribute('aria-disabled', 'true');
      else b.removeAttribute('aria-disabled');
      if (reason) b.dataset.tip = reason;
      else delete b.dataset.tip;
    };
    off(m.querySelector('[data-at="edit"]'), why);
    off(m.querySelector('[data-at="clear"]'), why || (menuFor.set ? '' : 'This attribute has no value.'));
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
    if (!b || !menuFor || b.getAttribute('aria-disabled') === 'true') return;
    $('at-menu').hidePopover();
    const a = menuFor;
    if (b.dataset.at === 'value') copyText(a.values.join('\n'), 'the value');
    if (b.dataset.at === 'name') copyText(a.name, 'the name');
    if (b.dataset.at === 'line') copyText(`${a.name}: ${a.values.join('; ')}`, `${a.name} and its value`);
    if (b.dataset.at === 'edit') openEdit(a);
    if (b.dataset.at === 'clear') {
      R.sure(`Clear ${a.name}?`, `The value is removed from ${on.get().name} in Active Directory.`, 'Clear', () => saveAttr(a, []));
    }
  });

  // Edit…: one box for a single value, a line each for several, TRUE or FALSE for a Boolean. Empty clears it.
  let editing = null;
  const HELP = {
    'Date and time': 'A UTC time in the format 20261231170000.0Z.',
    'Large integer': 'A 64-bit integer. Times such as accountExpires are 100-nanosecond intervals since 1601; 0 or 9223372036854775807 means never.',
    Integer: 'An integer.',
    'Numeric string': 'Digits and spaces.',
    'Distinguished name': 'A full distinguished name, such as CN=Name,OU=Users,DC=amw,DC=com.',
    'Object identifier': 'An OID, such as 1.2.840.113556.1.4.1.',
  };
  const openEdit = (a) => {
    editing = a;
    const bool = a.type === 'Boolean';
    const raw = a.raw || [];
    $('attr-title').textContent = `Edit ${a.name}`;
    $('attr-lead').textContent = `${a.type || 'Value'}, ${a.single ? 'single value' : 'one value per line'}. On ${on.get().name}.`;
    $('attr-one').hidden = !a.single || bool;
    $('attr-bool').hidden = !bool;
    $('attr-many').hidden = a.single || bool;
    $('attr-one').value = raw[0] || '';
    $('attr-bool').value = raw[0] || '';
    $('attr-many').value = raw.join('\n');
    $('attr-label').htmlFor = bool ? 'attr-bool' : a.single ? 'attr-one' : 'attr-many';
    $('attr-label').textContent = a.single || bool ? 'Value' : 'Values, one per line';
    $('attr-help').textContent = `${HELP[a.type] || ''}${HELP[a.type] ? ' ' : ''}Leave empty to clear the value.`;
    say('attr-problem', '');
    $('attr-dialog').showModal();
    $(bool ? 'attr-bool' : a.single ? 'attr-one' : 'attr-many').focus();
  };
  const saveAttr = async (a, values) => {
    try {
      const at = on;
      const got = await R.send('/api/ad/object/attribute', { dn: at.get().dn, name: a.name, values });
      at.set(got);
      at.draw();
      if ($('attr-dialog').open) $('attr-dialog').close();
      // Undo, then the object as it is again, if it's still the one shown.
      const back = async () => {
        if (await R.undo(got.change.id) && at.get()?.dn === got.dn) {
          try {
            at.set(await R.get(`/api/ad/object?dn=${encodeURIComponent(got.dn)}`));
            at.draw();
          } catch { /* it stays as it was shown */ }
        }
      };
      R.toast(values.some((v) => v.trim()) ? `${a.name} saved.` : `${a.name} cleared.`, '', got.change?.undo ? { action: { label: 'Undo', run: back, changes: [got.change.id] } } : {});
    } catch (err) {
      if ($('attr-dialog').open) say('attr-problem', err.message);
      else R.toast(`Could not change ${a.name}.`, err.message, { tone: 'warn' });
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
  const exportAttrs = (o) => {
    const cell = (s) => `"${String(s).replace(/"/g, '""')}"`;
    const lines = [['Attribute', 'Value', 'Type'].map(cell).join(',')];
    o.attributes.filter((a) => a.set && !a.hidden).forEach((a) => a.values.forEach((v) => lines.push([a.name, v, a.type].map(cell).join(','))));
    const link = document.createElement('a');
    link.href = URL.createObjectURL(new Blob([`﻿${lines.join('\r\n')}\r\n`], { type: 'text/csv' }));
    link.download = `${o.name.replace(/[\\/:*?"<>|]/g, '_')} attributes.csv`;
    document.body.append(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(link.href), 4000);
  };
  $('at-export').addEventListener('click', () => { if (object) exportAttrs(object); });
  // The AD side panel's Attributes tab (users.js) uses the same menu, Edit… and Export, on the object it shows.
  // Why nothing on an object can be edited (the scope, the role or the service account's rights), or '' when something can.
  const locked = (o) => (!R.has('ad.attributes') || o.attributes.some((a) => a.editable) ? ''
    : o.attributes.map((a) => a.why).find((w) => w && /scope|roles|service account/i.test(w)) || '');
  R.attrs = { menu: openMenu, export: exportAttrs, locked, href: (dn) => `#attrs/${enc(parentOf(dn))}/${enc(dn)}` };
  const showAttrs = async () => {
    const [, , rest] = R.where();
    const at = dec(rest[0]);
    const pick = dec(rest[1]) || at;
    const n = ++asking;
    say('at-problem', '');
    $('at-setup').hidden = true;
    // Placeholders until it's read: the way down, the tree and the attributes the first time, then the attributes on each click.
    if (!place) {
      $('at-title').textContent = 'Loading…';
      $('at-crumbs').innerHTML = '<i class="sk sk-text"></i>';
      R.skeleton($('at-items'), 'rows', 6);
    }
    R.skeleton($('at-table'), 'rows', 8);
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
      $('at-title').textContent = 'Could not load the object';
      for (const id of ['at-items', 'at-table']) R.skeleton($(id), '');
      if ($('at-crumbs').querySelector('.sk')) $('at-crumbs').textContent = '';
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
