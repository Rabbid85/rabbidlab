// Notes in the side panel: on a computer (seat:{id}), an AD user, group or computer (ad:{SID}) and a GPO (gpo:{id}).
// One engine for every panel: a page puts R.notes.count(on, again) in its tabs and R.notes.pane(on, name) as the
// Notes tab. The notes themselves are /api/notes (Notes.cs). Devices gets its tab through the panel hooks here.
(() => {
  const R = window.RabbidLab;
  const { esc, svg } = R;
  const lists = new Map(); // on → its notes once read, newest first
  const asking = new Map(); // on → the read under way
  const read = new Map(); // on → when it was last read
  const agains = new Map(); // on → what draws its panel again, for the tab's count
  const names = new Map(); // on → its name, for the activity log
  const boxes = new Map(); // on → its Notes element, kept while the panel is drawn again so a half-written note stays

  // What notes keep: the tags the editor makes, links to web and mail addresses, and text. Anything else keeps only its
  // text. The server takes nothing more (Notes.BadText).
  const ALLOW = { B: 'b', STRONG: 'b', I: 'i', EM: 'i', U: 'u', S: 's', STRIKE: 's', DEL: 's', CODE: 'code', PRE: 'pre', BLOCKQUOTE: 'blockquote',
    UL: 'ul', OL: 'ol', LI: 'li', BR: 'br', P: 'p', DIV: 'div', H1: 'h3', H2: 'h3', H3: 'h3', H4: 'h3', A: 'a' };
  const SKIP = /^(SCRIPT|STYLE|TEMPLATE|NOSCRIPT|IFRAME|OBJECT|EMBED|SVG|MATH|HEAD|TITLE)$/;
  const LINK = /^(https?:\/\/|mailto:)/i;
  const clean = (from, to) => {
    for (const n of from.childNodes) {
      if (n.nodeType === 3) to.append(document.createTextNode(n.data));
      if (n.nodeType !== 1) continue;
      const name = n.tagName.toUpperCase();
      if (SKIP.test(name)) continue;
      const tag = ALLOW[name];
      const href = tag === 'a' ? n.getAttribute('href') || '' : '';
      if (!tag || (tag === 'a' && !LINK.test(href))) {
        clean(n, to);
        continue;
      }
      const el = document.createElement(tag);
      if (href) el.setAttribute('href', href);
      clean(n, el);
      to.append(el);
    }
    return to;
  };
  // A kept note as nodes: parsed where nothing runs, then rebuilt from the allowed tags only. Links open in a new tab.
  const shown = (html) => {
    const frag = clean(new DOMParser().parseFromString(html, 'text/html').body, document.createDocumentFragment());
    frag.querySelectorAll('a').forEach((a) => { a.target = '_blank'; a.rel = 'noopener noreferrer'; });
    return frag;
  };
  // What the editor holds, as the tags kept, without empty lines at the end.
  const written = (ed) => {
    const box = clean(ed, document.createElement('div'));
    while (box.lastChild && (box.lastChild.nodeName === 'BR' || (box.lastChild.nodeType === 1 && !box.lastChild.textContent.trim() && !box.lastChild.querySelector('br, li'))
      || (box.lastChild.nodeType === 3 && !box.lastChild.data.trim()))) box.lastChild.remove();
    return box.innerHTML;
  };
  const blank = (ed) => !ed.textContent.replace(/ /g, ' ').trim();
  const plain = (html) => shown(html).textContent;

  const ago = (iso) => {
    const s = (Date.now() - new Date(iso)) / 1000;
    return s < 60 ? 'Just now' : s < 3600 ? `${Math.floor(s / 60)} min ago` : s < 86400 ? `${Math.floor(s / 3600)} h ago` : R.daysAgo(iso);
  };

  // Reads one thing's notes; what's waiting for them is drawn again when they come.
  const load = (on) => {
    if (asking.has(on)) return asking.get(on);
    const p = R.get(`/api/notes?on=${encodeURIComponent(on)}`).then((got) => {
      lists.set(on, got);
      read.set(on, Date.now());
    }).catch((e) => {
      if (!lists.has(on)) lists.set(on, { error: e.message });
    }).finally(() => {
      asking.delete(on);
      const box = boxes.get(on);
      if (box) drawList(box);
      agains.get(on)?.();
    });
    asking.set(on, p);
    return p;
  };

  // The tab's count: '' until they're read, then how many.
  const count = (on, again) => {
    if (again) agains.set(on, again);
    const got = lists.get(on);
    if (!got) {
      load(on);
      return '';
    }
    return Array.isArray(got) ? got.length : '';
  };

  // The toolbar: the kind of line, then marks, lists, code and a link.
  const TOOLS = [['bold', 'bold', 'Bold'], ['italic', 'italic', 'Italic'], ['underline', 'underline', 'Underline'], ['strikeThrough', 'strike', 'Strikethrough'], '-',
    ['quote', 'quote', 'Quote'], ['insertUnorderedList', 'list-ul', 'Bulleted list'], ['insertOrderedList', 'list-ol', 'Numbered list'], '-', ['code', 'code', 'Code'], '-', ['link', 'link', 'Link']];
  const BLOCKS = [['div', 'Normal text'], ['h3', 'Heading'], ['pre', 'Code block']];
  const composer = (mode) => `<div class="note-box"><div class="note-tools" role="toolbar" aria-label="Formatting">`
    + `<button type="button" class="note-block" data-nt="block" aria-haspopup="menu"><span>Normal text</span>${svg('chevron-down', 'chev')}</button><i class="note-rule"></i>`
    + TOOLS.map((t) => (t === '-' ? '<i class="note-rule"></i>' : `<button type="button" class="note-t" data-nt="${t[0]}" data-tip="${t[2]}" aria-label="${t[2]}">${svg(t[1])}</button>`)).join('')
    + `</div><div class="note-edit blank" contenteditable="true" role="textbox" aria-multiline="true" aria-label="Note" data-ph="${mode === 'add' ? 'Type your note…' : ''}" data-note-ed></div>`
    + '<div class="note-url" hidden><input type="text" data-note-url placeholder="https://" aria-label="Link address" autocomplete="off" spellcheck="false">'
    + '<button type="button" class="small primary" data-note-url-ok>Add link</button><button type="button" class="small" data-note-url-no>Cancel</button></div></div>'
    + `<div class="note-foot">${mode === 'add' ? '<span class="note-keys">Ctrl+Enter adds it</span><button type="button" class="primary" data-note-add disabled>Add note</button>'
      : '<button type="button" data-note-cancel>Cancel</button><button type="button" class="primary" data-note-save>Save</button>'}</div>`;

  const make = (on) => {
    const box = document.createElement('section');
    box.className = 'dw-sec notes';
    box.innerHTML = '<div class="dw-sec-head"><h3>Notes <small data-notes-n></small></h3>'
      + `<label class="side-find notes-find" hidden><svg><use href="#i-search"/></svg><input type="search" data-notes-find placeholder="Find in notes" aria-label="Find in notes" autocomplete="off" spellcheck="false"></label></div>`
      + `<div class="note-new">${composer('add')}</div><div class="note-list" data-notes-list></div>`;
    Object.assign(box, { on, find: '', editing: '', focused: null, mark: null });
    wire(box);
    return box;
  };

  // The list under the editor: newest first, narrowed by Find, each with Edit and Delete when this person may.
  const drawList = (box) => {
    const got = lists.get(box.on);
    const el = box.querySelector('[data-notes-list]');
    const n = Array.isArray(got) ? got.length : 0;
    box.querySelector('[data-notes-n]').textContent = n ? n.toLocaleString() : '';
    box.querySelector('.notes-find').hidden = n < 2;
    if (!got) {
      if (!el.firstChild) R.skeleton(el, 'rows', 2);
      return;
    }
    if (got.error) {
      el.innerHTML = `<p class="hint problem">${esc(got.error)}</p>`;
      return;
    }
    const q = box.find.trim().toLowerCase();
    const rows = got.filter((x) => !q || `${x.by} ${plain(x.text)}`.toLowerCase().includes(q));
    if (box.editing && !rows.some((x) => x.id === box.editing)) box.editing = '';
    // A note being changed stays as it is while the rest is drawn again.
    const keep = box.editing ? el.querySelector(`[data-note="${CSS.escape(box.editing)}"]`) : null;
    el.replaceChildren();
    if (!rows.length) {
      el.innerHTML = `<p class="muted dw-empty">${got.length ? 'No notes match.' : 'No notes yet.'}</p>`;
      return;
    }
    for (const x of rows) {
      if (keep && x.id === box.editing) {
        el.append(keep);
        continue;
      }
      const card = document.createElement('article');
      card.className = 'note';
      card.dataset.note = x.id;
      card.innerHTML = `<div class="note-top"><span class="pic note-pic">${esc(R.initials(x.by))}</span><b class="ell">${esc(x.by)}</b>`
        + `<span class="note-when" data-tip="${esc(R.when(x.at))}">${esc(ago(x.at))}</span>${x.changed ? `<span class="note-when" data-tip="Changed ${esc(R.when(x.changed))}">· edited</span>` : ''}<span class="grow"></span>`
        + (x.mine ? `<button type="button" class="at-more note-x" data-note-edit data-tip="Edit note" aria-label="Edit note">${svg('edit')}</button>` : '')
        + (x.canDelete ? `<button type="button" class="at-more note-x" data-note-del data-tip="Delete note" aria-label="Delete note">${svg('trash')}</button>` : '')
        + '</div><div class="note-body"></div>';
      card.querySelector('.note-body').append(shown(x.text));
      el.append(card);
    }
  };

  // The editor a button or key belongs to, and putting the caret back where it was before a menu or the link box.
  const edOf = (el) => el.closest('.note-box')?.querySelector('[data-note-ed]');
  const keepSpot = (box, ed) => {
    const s = getSelection();
    if (s.rangeCount && ed.contains(s.anchorNode)) {
      const r = s.getRangeAt(0);
      box.mark = { ed, sc: r.startContainer, so: r.startOffset, ec: r.endContainer, eo: r.endOffset };
    }
  };
  const backTo = (box, ed) => {
    const now = getSelection();
    // Still in the editor: the caret is where it is now.
    if (document.activeElement === ed && now.rangeCount && ed.contains(now.anchorNode)) return;
    ed.focus();
    const m = box.mark;
    if (!m || m.ed !== ed || !ed.contains(m.sc) || !ed.contains(m.ec)) return;
    try {
      const r = document.createRange();
      r.setStart(m.sc, Math.min(m.so, m.sc.length ?? m.sc.childNodes.length));
      r.setEnd(m.ec, Math.min(m.eo, m.ec.length ?? m.ec.childNodes.length));
      const s = getSelection();
      s.removeAllRanges();
      s.addRange(r);
    } catch { /* the text changed under it: the caret stays at the start */ }
  };
  const within = (ed, tag) => {
    const s = getSelection();
    let n = s.rangeCount ? s.anchorNode : null;
    for (; n && n !== ed; n = n.parentNode) if (n.nodeName === tag) return n;
    return null;
  };
  const refresh = (box, ed) => {
    ed.classList.toggle('blank', blank(ed));
    const add = box.querySelector('[data-note-add]');
    if (add && ed.closest('.note-new')) add.disabled = blank(ed);
  };

  const run = (box, ed, cmd) => {
    backTo(box, ed);
    if (cmd === 'quote') {
      if (within(ed, 'BLOCKQUOTE')) {
        document.execCommand('formatBlock', false, 'div');
        if (within(ed, 'BLOCKQUOTE')) document.execCommand('outdent');
      } else document.execCommand('formatBlock', false, 'blockquote');
    } else if (cmd === 'code') {
      const was = within(ed, 'CODE');
      if (was) was.replaceWith(document.createTextNode(was.textContent));
      else document.execCommand('insertHTML', false, `<code>${esc(getSelection().toString() || 'code')}</code>`);
    } else if (cmd === 'link') {
      keepSpot(box, ed);
      const row = ed.parentElement.querySelector('.note-url');
      row.hidden = false;
      const input = row.querySelector('[data-note-url]');
      input.value = within(ed, 'A')?.getAttribute('href') || '';
      input.focus();
      return;
    } else document.execCommand(cmd);
    keepSpot(box, ed);
    refresh(box, ed);
  };
  const linkDone = (box, ed, yes) => {
    const row = ed.parentElement.querySelector('.note-url');
    let url = row.querySelector('[data-note-url]').value.trim();
    row.hidden = true;
    backTo(box, ed);
    if (!yes || !url) return;
    if (!LINK.test(url)) url = /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(url) ? `mailto:${url}` : `https://${url}`;
    if (getSelection().isCollapsed) document.execCommand('insertHTML', false, `<a href="${esc(url)}">${esc(url.replace(/^mailto:/, ''))}</a>&nbsp;`);
    else document.execCommand('createLink', false, url);
    refresh(box, ed);
  };

  const add = async (box) => {
    const ed = box.querySelector('.note-new [data-note-ed]');
    const button = box.querySelector('[data-note-add]');
    if (blank(ed) || button.disabled) return;
    button.disabled = true;
    try {
      await R.send('/api/notes', { on: box.on, text: written(ed), name: names.get(box.on) || '' });
      ed.replaceChildren();
      refresh(box, ed);
      await load(box.on);
    } catch (e) {
      R.toast("The note wasn't added.", e.message, { tone: 'warn' });
      refresh(box, ed);
    }
  };
  const edit = (box, card) => {
    const x = (lists.get(box.on) || []).find((n) => n.id === card.dataset.note);
    if (!x) return;
    box.editing = x.id;
    const body = card.querySelector('.note-body');
    body.innerHTML = composer('edit');
    const ed = body.querySelector('[data-note-ed]');
    ed.append(shown(x.text));
    refresh(box, ed);
    card.querySelectorAll('.note-x').forEach((b) => { b.hidden = true; });
    ed.focus();
    getSelection().selectAllChildren(ed);
    getSelection().collapseToEnd();
  };
  const save = async (box, card) => {
    const ed = card.querySelector('[data-note-ed]');
    if (blank(ed)) {
      R.toast('A note needs some text.', 'Delete it instead to take it away.', { tone: 'warn' });
      return;
    }
    try {
      await R.send(`/api/notes/${encodeURIComponent(card.dataset.note)}`, { text: written(ed), name: names.get(box.on) || '' });
      box.editing = '';
      await load(box.on);
    } catch (e) {
      R.toast("The note wasn't saved.", e.message, { tone: 'warn' });
    }
  };
  const remove = (box, card) => {
    const x = (lists.get(box.on) || []).find((n) => n.id === card.dataset.note);
    if (!x) return;
    R.sure('Delete this note?', x.mine ? 'It\'s gone for everyone.' : `It's ${x.by}'s note. It's gone for everyone.`, 'Delete', async () => {
      try {
        await R.send(`/api/notes/${encodeURIComponent(x.id)}/delete`, { name: names.get(box.on) || '' });
        await load(box.on);
        R.toast('Note deleted', names.get(box.on) || '');
      } catch (e) {
        R.toast("The note wasn't deleted.", e.message, { tone: 'warn' });
      }
    });
  };

  const wire = (box) => {
    // Toolbar buttons keep the caret in the editor.
    box.addEventListener('mousedown', (e) => { if (e.target.closest('[data-nt]')) e.preventDefault(); });
    box.addEventListener('click', (e) => {
      const t = e.target.closest('[data-nt]');
      const card = e.target.closest('[data-note]');
      if (t) {
        const ed = edOf(t);
        if (t.dataset.nt !== 'block') run(box, ed, t.dataset.nt);
        else {
          keepSpot(box, ed);
          R.picker.menu(t, BLOCKS.map(([tag, label]) => ({ label, run: () => {
            backTo(box, ed);
            if (tag === 'div' && within(ed, 'BLOCKQUOTE')) document.execCommand('outdent');
            document.execCommand('formatBlock', false, tag);
            t.querySelector('span').textContent = label;
            refresh(box, ed);
          } })), { key: 'note-block' });
        }
      } else if (e.target.closest('[data-note-url-ok]')) linkDone(box, edOf(e.target), true);
      else if (e.target.closest('[data-note-url-no]')) linkDone(box, edOf(e.target), false);
      else if (e.target.closest('[data-note-add]')) add(box);
      else if (card && e.target.closest('[data-note-edit]')) edit(box, card);
      else if (card && e.target.closest('[data-note-del]')) remove(box, card);
      else if (card && e.target.closest('[data-note-save]')) save(box, card);
      else if (card && e.target.closest('[data-note-cancel]')) {
        box.editing = '';
        drawList(box);
      }
    });
    box.addEventListener('input', (e) => {
      if (e.target.matches('[data-notes-find]')) {
        box.find = e.target.value;
        drawList(box);
      } else if (e.target.matches('[data-note-ed]')) refresh(box, e.target);
    });
    box.addEventListener('keydown', (e) => {
      const url = e.target.closest('[data-note-url]');
      if (url && (e.key === 'Enter' || e.key === 'Escape')) {
        e.preventDefault();
        linkDone(box, edOf(url), e.key === 'Enter');
        return;
      }
      if (!e.target.matches('[data-note-ed]')) return;
      const card = e.target.closest('[data-note]');
      if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) {
        e.preventDefault();
        if (card) save(box, card);
        else add(box);
      } else if (e.key === 'Escape' && card) {
        e.stopPropagation(); // not the panel's Esc
        box.editing = '';
        drawList(box);
      } else if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') {
        e.preventDefault();
        run(box, e.target, 'link');
      }
    });
    // Pasted text comes in plain, so a note never carries a web page's styles.
    box.addEventListener('paste', (e) => {
      if (!e.target.closest('[data-note-ed]')) return;
      e.preventDefault();
      document.execCommand('insertText', false, e.clipboardData.getData('text/plain'));
    });
    // Where the typing was, so a panel drawn again puts the caret back.
    box.addEventListener('focusin', (e) => { box.focused = e.target; });
    box.addEventListener('focusout', () => setTimeout(() => { if (box.isConnected) box.focused = box.contains(document.activeElement) ? document.activeElement : null; }));
  };
  document.addEventListener('selectionchange', () => {
    const s = getSelection();
    const ed = s.rangeCount ? s.anchorNode?.parentElement?.closest('[data-note-ed]') || (s.anchorNode?.matches?.('[data-note-ed]') ? s.anchorNode : null) : null;
    const box = ed?.closest('.notes');
    if (box) keepSpot(box, ed);
  });

  // The page draws the panel as HTML: its Notes tab is an empty host, and the kept element goes back into it.
  const pane = (on, name) => {
    if (name) names.set(on, name);
    return `<div class="notes-host" data-notes-host="${esc(on)}"></div>`;
  };
  const adopt = () => {
    const host = R.panel.body.querySelector('[data-notes-host]');
    if (!host) return;
    const on = host.dataset.notesHost;
    let box = boxes.get(on);
    if (box && host.firstChild === box) return;
    if (!box) {
      box = make(on);
      boxes.set(on, box);
      // Only the newest few are kept, for going back and forth between rows.
      if (boxes.size > 12) boxes.delete(boxes.keys().next().value);
    }
    const back = box.focused && !box.contains(document.activeElement) ? box.focused : null;
    host.replaceChildren(box);
    drawList(box);
    // Read again when it's been a minute, not every time the panel is drawn.
    if (!asking.has(on) && Date.now() - (read.get(on) || 0) > 60000) load(on);
    if (back && (document.activeElement === document.body || !document.activeElement)) {
      if (back.matches('[data-note-ed]')) backTo(box, back);
      else back.focus();
    }
  };
  new MutationObserver(adopt).observe(R.panel.body, { childList: true, subtree: true });

  // Devices: a Notes tab on every computer, last.
  const D = R.devices;
  const tabs0 = D.hooks.seatTabs;
  D.hooks.seatTabs = (c) => [...(tabs0?.(c) || []), ['notes', 'Notes', count(`seat:${c.id}`, () => D.panelAgain())]];
  const pane0 = D.hooks.seatPane;
  D.hooks.seatPane = (c, tab) => (tab === 'notes' ? pane(`seat:${c.id}`, c.computer) : pane0 ? pane0(c, tab) : null);

  R.notes = { count, pane };
})();
