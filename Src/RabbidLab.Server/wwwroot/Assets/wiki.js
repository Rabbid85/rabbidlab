// Wiki: #wiki/<article>. The articles are in Assets/wiki.html, read once; the column lists them by group, Search
// narrows it to the articles that mention what's typed, and each article ends with the one before and after it.
(() => {
  const R = window.RabbidLab;
  const { $, esc } = R;
  let articles = null; // [{ key, title, group, html, text }]

  const load = async () => {
    if (articles) return;
    const r = await fetch('Assets/wiki.html');
    if (!r.ok) throw new Error(r.statusText);
    const doc = new DOMParser().parseFromString(await r.text(), 'text/html');
    const found = [...doc.querySelectorAll('article[data-key]')].map((a) => ({
      key: a.dataset.key, title: a.dataset.title, group: a.dataset.group, html: a.innerHTML, text: a.textContent.toLowerCase(),
    }));
    // Nothing is kept from a bad read, so opening the wiki again tries again.
    if (!found.length) throw new Error('The wiki is empty');
    articles = found;
  };

  const draw = (key) => {
    const find = $('wiki-find').value.trim().toLowerCase();
    const shown = articles.filter((a) => !find || a.title.toLowerCase().includes(find) || a.text.includes(find));
    const groups = [...new Set(shown.map((a) => a.group))];
    const now = articles.find((a) => a.key === key) || articles[0];
    $('wiki-toc').innerHTML = groups.map((g) => `<p class="side-label">${esc(g)}</p>`
      + shown.filter((a) => a.group === g).map((a) => `<a href="#wiki/${a.key}"${a === now ? ' aria-current="page"' : ''}><span class="nm">${esc(a.title)}</span></a>`).join('')).join('')
      || '<p class="side-sub">Nothing mentions that.</p>';
    const i = articles.indexOf(now);
    $('wiki-body').innerHTML = `<p class="wiki-group">${esc(now.group)}</p><h2>${esc(now.title)}</h2>${now.html}`;
    const prev = articles[i - 1];
    const next = articles[i + 1];
    $('wiki-next').innerHTML = (prev ? `<a href="#wiki/${prev.key}" class="prev"><small>Before</small><b>${esc(prev.title)}</b></a>` : '<span></span>')
      + (next ? `<a href="#wiki/${next.key}" class="next"><small>Next</small><b>${esc(next.title)}</b></a>` : '');
  };

  const show = async () => {
    const [, , rest] = R.where();
    try {
      await load();
    } catch {
      $('wiki-body').innerHTML = '<p class="hint problem">The wiki couldn\'t be read. Open it again to retry.</p>';
      return;
    }
    draw(rest[0]);
  };

  $('wiki-find').addEventListener('input', () => {
    const [, , rest] = R.where();
    if (articles) draw(rest[0]);
  });
  document.addEventListener('rabbidlab:show', (e) => {
    if (e.detail[0] === 'wiki') show();
  });
})();
