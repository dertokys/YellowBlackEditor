(() => {
  if (window.top !== window || window.__ybnInit) return;
  if (window.__ybnHost && location.host.toLowerCase() !== String(window.__ybnHost).toLowerCase()) return;
  if (location.pathname !== '/' && location.pathname !== '') return;
  window.__ybnInit = true;

  const MODE_KEY = 'ybn.helpdesk.mode';
  const PAGE = 60;
  const FONT = '"Segoe UI", system-ui, -apple-system, Arial, sans-serif';
  let theme = window.__ybnTheme || null;
  const st = { key: '', seq: 0, items: [], assets: {}, title: '', total: 0, shown: PAGE, filter: '', chip: 'all', loadedAt: 0, error: '', loading: false, searchValue: '', searchSince: 0, overviews: [], ovAt: 0, ovSig: '' };
  const states = new Map();
  const arts = new Map();
  const queue = [];
  const inflight = new Set();
  let running = 0;
  let themeStyle, cardStyle, root, tabsEl, list, head, toggle, io;

  const tr = s => { try { return (window.App && App.i18n && App.i18n.translateInline(s)) || s; } catch (e) { return s; } };
  const clean = s => String(s || '').replace(/[\u3164\u200b\u00a0]/g, ' ').replace(/\s+/g, ' ').trim();
  const el = (tag, cls, text) => { const e = document.createElement(tag); if (cls) e.className = cls; if (text != null) e.textContent = text; return e; };
  const plural = (n, a, b, c) => { const m = n % 100, k = n % 10; return m > 10 && m < 20 ? c : k === 1 ? a : k > 1 && k < 5 ? b : c; };

  function api(path) {
    return fetch('/api/v1/' + path, { credentials: 'same-origin', headers: { 'X-Requested-With': 'XMLHttpRequest' } }).then(r => {
      if (!r.ok) { const e = new Error('HTTP ' + r.status); e.status = r.status; throw e; }
      return r.json();
    });
  }

  function themeCss(t) {
    const on = '#111111';
    return `:root, html[data-theme] {
--ybn-bg:${t.bg};--ybn-panel:${t.panel};--ybn-panel2:${t.panel2};--ybn-border:${t.border};--ybn-text:${t.text};--ybn-sub:${t.sub};
--ybn-accent:${t.accent};--ybn-accent-light:${t.accentLight};--ybn-accent-text:${t.accentText};--ybn-accent-soft:${t.accentSoft};
--ybn-hover:${t.hover};--ybn-field:${t.field};--ybn-field-border:${t.fieldBorder};--ybn-danger:${t.danger};--ybn-on-accent:${on};
--background-primary:${t.bg} !important;--background-primary-alt:${t.panel2} !important;--background-secondary:${t.panel} !important;
--background-secondary-hover:${t.hover} !important;--background-tertiary:${t.panel} !important;--background-quaternary:${t.panel2} !important;
--background-quaternary-alt:${t.border} !important;--background-popover:${t.panel} !important;--background-active:${t.accent} !important;
--background-highlight:${t.accentSoft} !important;--background-article-meta:${t.panel2} !important;--background-article-customer:${t.accentSoft} !important;
--background-article-customer-meta:${t.accentSoft} !important;--background-modifier-hover:${t.hover} !important;
--border:${t.border} !important;--border-alt:${t.border} !important;--border-highlight:${t.accent} !important;--border-article-customer:${t.border} !important;
--border-article-customer-selected:${t.accent} !important;--shadow-highlight:${t.accentSoft} !important;--highlight:${t.accent} !important;
--text-normal:${t.text} !important;--header-primary:${t.text} !important;--header-secondary:${t.sub} !important;--text-muted:${t.sub} !important;
--text-muted-alt:${t.sub} !important;--text-link:${t.accentText} !important;--text-nav:${t.text} !important;--interactive-primary:${t.text} !important;
--button-primary-background:${t.accent} !important;--button-primary-background-active:${t.accentLight} !important;--button-primary-text:${on} !important;
--text-button-primary:${t.accentText} !important;--text-button-primary-active:${t.accentText} !important;
--menu-background-active:${t.accent} !important;--menu-text-active:${on} !important;--menu-background-primary:${t.panel2} !important;
--menu-background-secondary:${t.panel} !important;--menu-close-tab-background:${t.panel} !important;--fullscreen-background:${t.bg} !important;
}
body, input, textarea, select, button, .btn, .form-control { font-family: ${FONT} !important; }
.menu-item.is-active .menu-item-name, .menu-item.is-active .icon, .nav-tab.is-active, .nav-tab.is-active .nav-tab-name, .nav-tab.is-active .icon { color: ${on} !important; fill: ${on} !important; }
.btn--primary, .btn--success, .btn.btn--create { background: ${t.accent} !important; color: ${on} !important; }
.btn--primary .icon, .btn--success .icon, .btn--create .icon { fill: ${on} !important; }
::selection { background: ${t.accent}; color: ${on}; }
* { scrollbar-color: ${t.fieldBorder} transparent; }`;
  }

  const CARD_CSS = `
#ybn-cards{position:fixed;z-index:20;display:none;flex-direction:column;background:var(--ybn-bg);color:var(--ybn-text);font:14px/1.45 ${FONT};box-sizing:border-box}
#ybn-cards *{box-sizing:border-box}
#ybn-cards.on{display:flex}
#ybn-cards .ybn-head{display:flex;align-items:center;gap:10px 14px;padding:18px clamp(12px,3vw,24px) 12px;flex-wrap:wrap;border-bottom:1px solid var(--ybn-border)}
#ybn-cards .ybn-title{font-size:20px;font-weight:600;margin:0;color:var(--ybn-text);min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;flex:0 1 auto}
#ybn-cards .ybn-count{color:var(--ybn-sub);white-space:nowrap}
#ybn-cards .ybn-spacer{flex:1}
#ybn-cards .ybn-filter{height:32px;flex:0 1 220px;min-width:120px;padding:0 10px;border-radius:8px;border:1px solid var(--ybn-field-border);background:var(--ybn-field);color:var(--ybn-text);font:inherit;outline:none}
#ybn-cards .ybn-filter:focus{border-color:var(--ybn-accent)}
#ybn-cards .ybn-chips{display:flex;border:1px solid var(--ybn-border);border-radius:8px;overflow:hidden}
#ybn-cards .ybn-chips button{height:30px;padding:0 12px;white-space:nowrap;border:0;background:transparent;color:var(--ybn-sub);font:inherit;cursor:pointer}
#ybn-cards .ybn-chips button+button{border-left:1px solid var(--ybn-border)}
#ybn-cards .ybn-chips button.on{background:var(--ybn-accent);color:var(--ybn-on-accent);font-weight:600}
#ybn-cards .ybn-btn,#ybn-toggle{height:32px;padding:0 14px;border-radius:8px;border:1px solid var(--ybn-border);background:var(--ybn-panel);color:var(--ybn-text);font:14px ${FONT};cursor:pointer;white-space:nowrap}
#ybn-cards .ybn-btn:hover,#ybn-toggle:hover{border-color:var(--ybn-accent)}
#ybn-cards .ybn-create{background:var(--ybn-accent);border-color:var(--ybn-accent);color:var(--ybn-on-accent);font-weight:600}
#ybn-cards button:focus-visible,#ybn-toggle:focus-visible,#ybn-cards .ybn-card:focus-visible{outline:2px solid var(--ybn-accent);outline-offset:2px}
#ybn-cards .ybn-list{flex:1;overflow:auto;padding:16px clamp(12px,3vw,24px) 32px;display:grid;grid-template-columns:repeat(auto-fill,minmax(min(460px,100%),1fr));gap:12px;align-content:start}
#ybn-cards .ybn-card{background:var(--ybn-panel);border:1px solid var(--ybn-border);border-radius:10px;padding:14px 16px;cursor:pointer;display:flex;flex-direction:column;gap:10px;min-width:0}
#ybn-cards .ybn-card:hover{border-color:var(--ybn-accent)}
#ybn-cards .ybn-card.closed .ybn-headline{color:var(--ybn-sub)}
#ybn-cards .ybn-top{display:flex;flex-wrap:wrap;gap:6px 12px;align-items:flex-start}
#ybn-cards .ybn-headline{flex:1 1 220px;min-width:0;font-size:16px;font-weight:600;line-height:1.35;white-space:pre-wrap;overflow-wrap:break-word}
#ybn-cards .ybn-state{flex:none;font-size:12px;line-height:20px;padding:0 9px;border-radius:999px;border:1px solid var(--ybn-border);color:var(--ybn-sub);white-space:nowrap}
#ybn-cards .ybn-state.new{background:var(--ybn-accent);border-color:var(--ybn-accent);color:var(--ybn-on-accent);font-weight:600}
#ybn-cards .ybn-state.open{border-color:var(--ybn-accent);color:var(--ybn-accent-text);font-weight:600}
#ybn-cards .ybn-state.pending{border-style:dashed;color:var(--ybn-text)}
#ybn-cards .ybn-who{display:flex;flex-direction:column;gap:2px}
#ybn-cards .ybn-org{font-weight:600;overflow-wrap:break-word}
#ybn-cards .ybn-meta{color:var(--ybn-sub);font-size:13px;display:flex;flex-wrap:wrap;gap:2px 14px}
#ybn-cards .ybn-body{white-space:pre-wrap;overflow-wrap:break-word;color:var(--ybn-text)}
#ybn-cards .ybn-wait{color:var(--ybn-sub);font-size:13px}
#ybn-cards .ybn-notes{border-top:1px solid var(--ybn-border);padding-top:10px;display:flex;flex-direction:column;gap:10px}
#ybn-cards .ybn-note{display:flex;gap:10px;align-items:flex-start}
#ybn-cards .ybn-ava{flex:none;width:26px;height:26px;border-radius:50%;background:var(--ybn-accent-soft);color:var(--ybn-accent-text);font-size:11px;font-weight:700;display:flex;align-items:center;justify-content:center}
#ybn-cards .ybn-note.customer .ybn-ava{background:var(--ybn-accent);color:var(--ybn-on-accent)}
#ybn-cards .ybn-note-main{flex:1;min-width:0}
#ybn-cards .ybn-note-meta{color:var(--ybn-sub);font-size:12px}
#ybn-cards .ybn-note-text{white-space:pre-wrap;overflow-wrap:break-word}
#ybn-cards .ybn-foot{color:var(--ybn-sub);font-size:12px;display:flex;gap:4px 14px;flex-wrap:wrap}
#ybn-cards .ybn-msg{grid-column:1/-1;color:var(--ybn-sub);padding:40px 0;text-align:center;display:flex;flex-direction:column;align-items:center;gap:12px}
#ybn-cards .ybn-more{grid-column:1/-1;justify-self:center}
#ybn-toggle{position:fixed;z-index:20;display:none;background:var(--ybn-accent);color:var(--ybn-on-accent);border-color:var(--ybn-accent);font-weight:600}
#navigation .logo.js-toggleNotifications,#navigation .js-dashboardMenuItem{display:none !important}
#navigation .tasks .nav-tab.is-active{background:var(--ybn-accent) !important}
#navigation .tasks .nav-tab .nav-tab-close-inner .icon{fill:var(--ybn-text) !important}
#navigation .tasks .nav-tab.is-active .nav-tab-close-inner{background:rgba(0,0,0,.12) !important}
#navigation .tasks .nav-tab.is-active .icon{fill:var(--ybn-on-accent) !important}
#navigation:not(.is-not-collapsed) .tasks .nav-tab-name,#navigation:not(.is-not-collapsed) .tasks .nav-tab-close{display:none !important}
#navigation:not(.is-not-collapsed) .tasks{width:100% !important;min-width:0 !important}
#navigation:not(.is-not-collapsed) .tasks .nav-tab{width:100% !important;min-width:0 !important;flex-direction:column !important;justify-content:center !important;align-items:center !important;gap:2px;padding:6px 0 !important;height:auto !important;min-height:46px}
#navigation:not(.is-not-collapsed) .tasks .nav-tab-icon{margin:0 !important}
#navigation:not(.is-not-collapsed) .tasks .nav-tab{position:relative !important}
#navigation:not(.is-not-collapsed) .tasks .nav-tab:hover .nav-tab-close{display:flex !important;position:absolute !important;top:2px;right:2px;width:18px;height:18px;align-items:center;justify-content:center;margin:0 !important}
#navigation:not(.is-not-collapsed) .tasks .nav-tab:hover .nav-tab-close-inner{width:18px !important;height:18px !important;border-radius:50% !important;display:flex !important;align-items:center;justify-content:center;background:rgba(0,0,0,.35) !important}
#navigation:not(.is-not-collapsed) .tasks .nav-tab:hover .nav-tab-close .icon{opacity:1 !important;fill:#fff !important;width:9px;height:9px}
#app .tabsSidebar .sidebar[data-tab="template"],#app .tabsSidebar-tab[data-tab="template"]{display:none !important}
#app .tabsSidebar:not(:has(.tabsSidebar-tab:not([data-tab="template"]))){display:none !important}
#app .type-tabs .tab[data-type="phone-out"],#app .type-tabs .tab[data-type="email-out"]{display:none !important}
#navigation:not(.is-not-collapsed) .tasks .nav-tab[data-short]::after{content:attr(data-short);font-size:10px;line-height:12px;color:var(--ybn-sub)}
#navigation:not(.is-not-collapsed) .tasks .nav-tab.is-active[data-short]::after{color:var(--ybn-on-accent);font-weight:600}
#navigation .icon-plus{fill:var(--ybn-accent) !important}
#app .internal-border{background:none !important;padding:0 !important;border-left:3px solid var(--ybn-accent) !important;border-radius:6px !important}
html.ybn-cards-on #content_permanent_TicketOverview > .sidebar{display:none !important}
#ybn-cards .ybn-tabs{display:flex;gap:6px;padding:14px clamp(12px,3vw,24px) 0;overflow-x:auto;flex:none;scrollbar-width:thin}
#ybn-cards .ybn-tabs:empty{display:none}
#ybn-cards .ybn-tab{flex:none;height:32px;padding:0 12px;border-radius:8px;border:1px solid var(--ybn-border);background:var(--ybn-panel);color:var(--ybn-text);font:inherit;cursor:pointer;white-space:nowrap;display:flex;align-items:center;gap:8px}
#ybn-cards .ybn-tab:hover{border-color:var(--ybn-accent)}
#ybn-cards .ybn-tab.on{background:var(--ybn-accent);border-color:var(--ybn-accent);color:var(--ybn-on-accent);font-weight:600}
#ybn-cards .ybn-tab b{font-weight:600;font-size:12px;color:var(--ybn-sub)}
#ybn-cards .ybn-tab.on b{color:var(--ybn-on-accent)}`;

  function applyTheme(t) {
    if (!t) return;
    theme = t;
    if (themeStyle) themeStyle.textContent = themeCss(t);
    fixThemeAttr();
  }
  window.__ybnApplyTheme = applyTheme;

  function fixThemeAttr() {
    if (!theme) return;
    const want = theme.light ? 'light' : 'dark';
    if (document.documentElement.getAttribute('data-theme') !== want) document.documentElement.setAttribute('data-theme', want);
  }

  function mode() { try { return localStorage.getItem(MODE_KEY) === 'table' ? 'table' : 'cards'; } catch (e) { return 'cards'; } }
  function setMode(m) { try { localStorage.setItem(MODE_KEY, m); } catch (e) { } tick(); }

  function stateOf(t) {
    const s = states.get(t.state_id);
    const type = s ? s.state_type_id : 0;
    const cls = type === 1 ? 'new' : type === 2 ? 'open' : type === 3 || type === 4 ? 'pending' : type >= 5 ? 'closed' : '';
    return { name: s ? tr(s.name) : '', cls };
  }

  function userName(u) {
    if (!u) return '';
    return clean(((u.firstname || '') + ' ' + (u.lastname || '')).trim() || u.login || u.email || '');
  }

  function when(iso) {
    if (!iso) return '';
    const d = new Date(iso), now = new Date();
    const hm = d.toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit' });
    const day = new Date(d.getFullYear(), d.getMonth(), d.getDate());
    const today = new Date(now.getFullYear(), now.getMonth(), now.getDate());
    const diff = Math.round((today - day) / 864e5);
    if (diff === 0) return 'сегодня в ' + hm;
    if (diff === 1) return 'вчера в ' + hm;
    return d.toLocaleDateString('ru-RU') + ' в ' + hm;
  }

  function articleText(a) {
    let s = String(a.body || '');
    if (/html/i.test(a.content_type || '')) {
      s = s.replace(/<br\s*\/?>/gi, '\n').replace(/<\/(p|div|li|tr|h\d|blockquote)>/gi, '\n');
      s = new DOMParser().parseFromString(s, 'text/html').body.textContent || '';
    }
    return s.replace(/\u00a0/g, ' ').replace(/\r/g, '').replace(/[ \t]+\n/g, '\n').replace(/\n{3,}/g, '\n\n').trim();
  }

  function fields(text) {
    const out = {};
    let key = null, buf = [], n = 0;
    const flush = () => { if (key) { out[key] = buf.join('\n').trim(); n++; } };
    for (const line of text.split('\n')) {
      const m = line.match(/^\s*([^:\n]{2,40}):\s*$/);
      if (m) { flush(); key = m[1].trim(); buf = []; }
      else if (key) buf.push(line);
      else if (line.trim()) return null;
    }
    flush();
    return n ? out : null;
  }

  const duration = m => { m = Math.round(m); const h = Math.floor(m / 60), r = m % 60; return h ? h + ' ч' + (r ? ' ' + r + ' мин' : '') : r + ' мин'; };
  const clip = (s, n) => s.length > n ? s.slice(0, n).trimEnd() + '…' : s;
  const initials = s => clean(s).split(' ').filter(Boolean).slice(0, 2).map(w => w[0]).join('').toUpperCase() || '?';
  const isEmpty = v => !v || /^не указан/i.test(v.trim());

  function build() {
    if (root) return;
    themeStyle = el('style'); themeStyle.id = 'ybn-theme';
    cardStyle = el('style'); cardStyle.id = 'ybn-cards-style'; cardStyle.textContent = CARD_CSS;
    document.head.appendChild(cardStyle);
    document.head.appendChild(themeStyle);
    applyTheme(theme);
    new MutationObserver(fixThemeAttr).observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme'] });
    new MutationObserver(() => {
      if (themeStyle.nextElementSibling) document.head.appendChild(themeStyle);
    }).observe(document.head, { childList: true });

    root = el('div'); root.id = 'ybn-cards';
    tabsEl = el('div', 'ybn-tabs');
    head = el('div', 'ybn-head');
    list = el('div', 'ybn-list');
    root.append(tabsEl, head, list);
    toggle = el('button', null, 'Карточки');
    toggle.id = 'ybn-toggle';
    toggle.type = 'button';
    toggle.title = 'Показать заявки карточками с текстом обращения';
    toggle.addEventListener('click', () => setMode('cards'));
    document.body.append(root, toggle);
    io = new IntersectionObserver(es => {
      for (const e of es) if (e.isIntersecting) { io.unobserve(e.target); need(e.target); }
    }, { root: list, rootMargin: '400px' });
    const kbOpen = e => {
      if (!(e.button === 1 || (e.button === 0 && (e.ctrlKey || e.metaKey)))) return;
      const a = e.target.closest && e.target.closest('a[href^="#knowledge_base/"]');
      if (!a) return;
      const m = a.getAttribute('href').match(/^#knowledge_base\/\d+\/locale\/([^/]+)\/(answer|category)\/(\d+)/);
      if (!m) return;
      e.preventDefault();
      e.stopPropagation();
      if (e.type === 'mousedown') return;
      const kind = m[2] === 'answer' ? 'KnowledgeBaseAnswer' : 'KnowledgeBaseCategory';
      window.open('/api/v1/knowledge_bases/preview/' + kind + '/' + m[3] + '/' + m[1], '_blank');
    };
    document.addEventListener('mousedown', kbOpen, true);
    document.addEventListener('click', kbOpen, true);
    document.addEventListener('auxclick', kbOpen, true);
    window.addEventListener('resize', tick);
    window.addEventListener('hashchange', tick);
    setInterval(tick, 400);
    tick();
  }

  function route() {
    let h = location.hash.replace(/^#/, '');
    try { h = decodeURIComponent(h); } catch (e) { }
    let m = h.match(/^ticket\/view\/([^/]+)/);
    if (m) return { kind: 'view', key: m[1] };
    m = h.match(/^search\/(.*)$/);
    if (m) {
      const c = document.querySelector('#app > .content.active');
      const tab = c && c.querySelector('.js-tabTicket');
      if (tab && !tab.classList.contains('active')) return null;
      const input = c && c.querySelector('.detail-search .js-search');
      const q = (input && input.value.trim()) || m[1].trim();
      return q ? { kind: 'search', key: q } : null;
    }
    return null;
  }

  function area(r) {
    const c = document.querySelector('#app > .content.active');
    if (!c) return null;
    const main = r.kind === 'view' ? c.querySelector(':scope > .main') : c.querySelector('.main');
    if (!main) return null;
    const a = main.getBoundingClientRect();
    if (a.width < 100 || a.height < 100) return null;
    if (r.kind === 'view') return { left: a.left, top: a.top, width: a.width, height: a.height };
    const content = c.querySelector('.detail-search .js-content');
    if (!content) return null;
    const b = content.getBoundingClientRect();
    return { left: a.left, top: b.top, width: a.width, height: a.bottom - b.top };
  }

  function place(node, box) {
    node.style.left = box.left + 'px';
    node.style.top = box.top + 'px';
    if (node === root) { node.style.width = box.width + 'px'; node.style.height = box.height + 'px'; }
  }

  function tick() {
    if (!root) return;
    fixThemeAttr();
    for (const t of document.querySelectorAll('#navigation .tasks .nav-tab')) {
      const n = t.querySelector('.nav-tab-name');
      const name = n ? n.textContent.trim() : '';
      const m = name.match(/ITF\s*(\d+)/i) || name.match(/(\d{3,})/);
      const short = m ? m[1].slice(-5) : name.slice(0, 5);
      if (t.dataset.short !== short) t.dataset.short = short;
    }
    const typeTabs = document.querySelector('#app > .content.active .type-tabs');
    const phoneIn = typeTabs && typeTabs.querySelector('[data-type="phone-in"]');
    if (phoneIn && !phoneIn.classList.contains('active')) phoneIn.click();
    const h = location.hash;
    if ((h === '' || h === '#' || h.startsWith('#dashboard')) && document.querySelector('#navigation .js-overviewsMenuItem')) {
      location.replace('#ticket/view');
      return;
    }
    const r = route();
    const cardsView = !!r && r.kind === 'view' && mode() === 'cards';
    document.documentElement.classList.toggle('ybn-cards-on', cardsView);
    const box = r && area(r);
    if (!r || !box) { root.classList.remove('on'); toggle.style.display = 'none'; return; }
    if (mode() === 'table') {
      root.classList.remove('on');
      toggle.style.display = 'block';
      const h2 = r.kind === 'view' && document.querySelector('#app > .content.active .page-header-title h2');
      const hr = h2 && h2.getBoundingClientRect();
      place(toggle, hr && hr.width ? { left: hr.right + 16, top: hr.top + (hr.height - toggle.offsetHeight) / 2 } : { left: box.left + box.width - toggle.offsetWidth - 24, top: box.top + 12 });
      return;
    }
    toggle.style.display = 'none';
    let key = r.kind + ':' + r.key;
    if (st.authFail && key === st.key) {
      root.classList.remove('on');
      if (!st.loading && Date.now() - st.loadedAt > 5000) load(r, key, true);
      return;
    }
    root.classList.add('on');
    place(root, box);
    if (r.kind === 'search') {
      if (r.key !== st.searchValue) { st.searchValue = r.key; st.searchSince = Date.now(); }
      if (st.key.startsWith('search:') && key !== st.key && Date.now() - st.searchSince < 600) return;
    }
    if (r.kind === 'view' && Date.now() - st.ovAt > 15000 && document.visibilityState === 'visible') loadOverviews();
    if (key !== st.key) load(r, key, false);
    else if (!st.loading && Date.now() - st.loadedAt > 30000 && document.visibilityState === 'visible') load(r, key, true);
  }

  async function loadOverviews() {
    st.ovAt = Date.now();
    try {
      const d = await api('ticket_overviews');
      if (!Array.isArray(d)) return;
      st.overviews = d;
      const sig = d.map(o => o.link + ':' + o.count).join(',');
      const prev = st.ovSig;
      st.ovSig = sig;
      renderTabs();
      if (prev && sig !== prev && st.key.startsWith('view:') && !st.loading) {
        const r = route();
        if (r && r.kind === 'view') load(r, st.key, true);
      }
    } catch (e) { }
  }

  function renderTabs() {
    tabsEl.replaceChildren();
    const r = route();
    if (!r || r.kind !== 'view') return;
    for (const o of st.overviews) {
      const b = el('button', 'ybn-tab' + (o.link === r.key ? ' on' : ''));
      b.type = 'button';
      b.append(document.createTextNode(tr(o.name)), el('b', null, String(o.count)));
      b.addEventListener('click', () => { location.hash = '#ticket/view/' + o.link; });
      tabsEl.append(b);
    }
    const on = tabsEl.querySelector('.on');
    if (on) on.scrollIntoView({ block: 'nearest', inline: 'nearest' });
  }

  async function ensureStates() {
    if (states.size) return;
    const list = await api('ticket_states');
    for (const s of list) states.set(s.id, s);
  }

  async function load(r, key, silent) {
    const seq = ++st.seq;
    const keyChanged = st.key !== key;
    st.key = key;
    st.loading = true;
    if (keyChanged) {
      st.items = []; st.total = 0; st.sig = ''; st.shown = PAGE; st.filter = ''; st.chip = 'all'; st.error = ''; st.authFail = false;
      st.title = r.kind === 'search' ? 'Поиск: ' + r.key : '';
      renderTabs();
      if (r.kind === 'view') loadOverviews();
      render(true);
      list.replaceChildren(el('div', 'ybn-msg', 'Загружаю заявки…'));
    }
    try {
      await ensureStates();
      let ids, assets, title, total;
      if (r.kind === 'view') {
        const d = await api('ticket_overviews?view=' + encodeURIComponent(r.key));
        ids = (d.index.tickets || []).map(t => t.id);
        assets = d.assets || {};
        title = tr((d.index.overview && d.index.overview.name) || 'Заявки');
        total = d.index.count != null ? d.index.count : ids.length;
      } else {
        const d = await api('tickets/search?query=' + encodeURIComponent(r.key) + '&limit=200&full=true');
        if (Array.isArray(d)) {
          assets = { Ticket: {} };
          for (const t of d) assets.Ticket[t.id] = t;
          ids = d.map(t => t.id);
        } else {
          assets = d.assets || {};
          ids = (d.tickets || []).slice();
        }
        const T = assets.Ticket || {};
        ids.sort((a, b) => String((T[b] || {}).created_at).localeCompare(String((T[a] || {}).created_at)));
        title = 'Поиск: ' + r.key;
        total = d.tickets_count != null ? d.tickets_count : ids.length;
      }
      if (seq !== st.seq) return;
      const T = assets.Ticket || {};
      const items = ids.map(id => T[id]).filter(Boolean);
      const sig = items.map(t => t.id + '@' + t.updated_at).join(',') + '|' + total;
      st.loadedAt = Date.now();
      if (silent && sig === st.sig && !st.error && !st.authFail) return;
      st.items = items; st.sig = sig;
      st.assets = assets; st.title = title; st.total = total; st.error = ''; st.authFail = false;
      prune();
      render(!silent);
    } catch (e) {
      if (seq !== st.seq) return;
      st.loadedAt = Date.now();
      if (e.status === 401) { st.authFail = true; root.classList.remove('on'); return; }
      if (silent) return;
      st.error = 'Заявки не загрузились. Проверьте интернет и нажмите «Повторить».';
      render(true);
    } finally {
      if (seq === st.seq) st.loading = false;
    }
  }

  function visible() {
    const f = st.filter.trim().toLowerCase();
    return st.items.filter(t => {
      const c = stateOf(t).cls;
      if (st.chip === 'active' && c === 'closed') return false;
      if (st.chip === 'closed' && c !== 'closed') return false;
      if (!f) return true;
      return searchText(t).includes(f);
    });
  }

  function searchText(t) {
    const A = st.assets, org = (A.Organization || {})[t.organization_id], cust = (A.User || {})[t.customer_id];
    const a = arts.get(t.id + '@' + t.updated_at);
    return [t.number, t.title, org && org.name, userName(cust), cust && cust.phone, cust && cust.mobile, a ? a.map(articleText).join(' ') : ''].join(' ').toLowerCase();
  }

  function renderHead() {
    head.replaceChildren();
    const title = el('h2', 'ybn-title', st.title);
    title.title = st.title;
    const count = el('span', 'ybn-count');
    renderCount(count);
    const filter = el('input', 'ybn-filter');
    filter.type = 'search';
    filter.placeholder = 'Найти в этом списке';
    filter.value = st.filter;
    filter.addEventListener('input', () => { st.filter = filter.value; st.shown = PAGE; renderList(); renderCount(count); });
    const chips = el('div', 'ybn-chips');
    for (const [k, label] of [['all', 'Все'], ['active', 'В работе'], ['closed', 'Закрытые']]) {
      const b = el('button', k === st.chip ? 'on' : '', label);
      b.type = 'button';
      b.addEventListener('click', () => { st.chip = k; st.shown = PAGE; render(true); });
      chips.append(b);
    }
    const table = el('button', 'ybn-btn', 'Таблица');
    table.type = 'button';
    table.title = 'Вернуть обычную таблицу Zammad';
    table.addEventListener('click', () => setMode('table'));
    const create = el('button', 'ybn-btn ybn-create', '+ Новая заявка');
    create.type = 'button';
    create.addEventListener('click', () => { location.hash = '#ticket/create'; });
    if (!st.key.startsWith('view:')) head.append(title);
    head.append(count, el('div', 'ybn-spacer'), filter, chips, table, create);
  }

  function renderCount(node) {
    if (st.loading && !st.items.length) { node.textContent = ''; return; }
    const shown = visible().length;
    let text = st.total + ' ' + plural(st.total, 'заявка', 'заявки', 'заявок');
    if (st.items.length < st.total) text += ', загружено ' + st.items.length;
    if (shown !== st.items.length) text += ', подходит ' + shown;
    node.textContent = text;
  }

  function render(resetScroll) {
    const top = list.scrollTop;
    const focused = document.activeElement && document.activeElement.classList.contains('ybn-filter');
    renderHead();
    renderList();
    if (focused) { const f = head.querySelector('.ybn-filter'); f.focus(); f.setSelectionRange(f.value.length, f.value.length); }
    list.scrollTop = resetScroll ? 0 : top;
  }

  function renderList() {
    io.disconnect();
    queue.length = 0;
    list.replaceChildren();
    if (st.error) {
      const m = el('div', 'ybn-msg', st.error);
      const b = el('button', 'ybn-btn', 'Повторить');
      b.type = 'button';
      b.addEventListener('click', () => { st.key = ''; tick(); });
      m.append(b);
      list.append(m);
      return;
    }
    const items = visible();
    if (!items.length) {
      list.append(el('div', 'ybn-msg', st.items.length ? 'В этом списке таких заявок нет. Измените фильтр.' : 'Заявок нет.'));
      return;
    }
    for (const t of items.slice(0, st.shown)) list.append(card(t));
    if (items.length > st.shown) {
      const rest = items.length - st.shown;
      const more = el('button', 'ybn-btn ybn-more', 'Показать ещё ' + Math.min(rest, PAGE) + ' из ' + rest);
      more.type = 'button';
      more.addEventListener('click', () => { st.shown += PAGE; render(false); });
      list.append(more);
    }
  }

  function card(t) {
    const c = el('div', 'ybn-card');
    c.tabIndex = 0;
    c.dataset.id = t.id;
    c._t = t;
    const open = () => { location.hash = '#ticket/zoom/' + t.id; };
    c.addEventListener('click', e => { if (!window.getSelection().toString()) open(); });
    c.addEventListener('keydown', e => { if (e.key === 'Enter') open(); });
    fill(c, t, arts.get(t.id + '@' + t.updated_at) || null);
    io.observe(c);
    return c;
  }

  function fill(c, t, a) {
    const A = st.assets;
    const org = (A.Organization || {})[t.organization_id];
    const cust = (A.User || {})[t.customer_id];
    const owner = (A.User || {})[t.owner_id];
    const group = (A.Group || {})[t.group_id];
    const s = stateOf(t);
    c.className = 'ybn-card' + (s.cls === 'closed' ? ' closed' : '');
    c.replaceChildren();

    const first = a && a.length ? a[0] : null;
    const firstText = first ? articleText(first) : '';
    const f = firstText ? fields(firstText) : null;
    const headline = f && f['Заявка'] ? f['Заявка'] : (first && !/^ITF\d+/.test(t.title) ? t.title : firstText ? clip(firstText.split('\n')[0], 200) : t.title);

    const top = el('div', 'ybn-top');
    top.append(el('div', 'ybn-headline', clip(headline.trim(), 600)));
    if (s.name) top.append(el('span', 'ybn-state ' + s.cls, s.name));
    c.append(top);

    const who = el('div', 'ybn-who');
    const orgName = clean((org && org.name) || (f && !isEmpty(f['Организация']) ? f['Организация'] : ''));
    if (orgName) who.append(el('div', 'ybn-org', orgName));
    const meta = el('div', 'ybn-meta');
    const inn = f && !isEmpty(f['ИНН']) ? f['ИНН'].trim() : '';
    if (inn) meta.append(el('span', null, 'ИНН ' + inn));
    let custName = userName(cust);
    if (orgName) custName = clean(custName.replace('(' + orgName + ')', ''));
    if (custName && custName !== orgName) meta.append(el('span', null, custName));
    const phone = cust && clean(cust.phone || cust.mobile);
    if (phone) meta.append(el('span', null, phone));
    if (meta.childNodes.length) who.append(meta);
    if (who.childNodes.length) c.append(who);

    if (!a) {
      c.append(el('div', 'ybn-wait', 'Загружаю обращение…'));
    } else {
      if (firstText && !f) c.append(el('div', 'ybn-body', clip(firstText, 3000)));
      if (f) {
        const extra = Object.entries(f).filter(([k, v]) => !['Заявка', 'ИНН', 'Организация'].includes(k) && !isEmpty(v));
        if (extra.length) {
          const m = el('div', 'ybn-meta');
          for (const [k, v] of extra) m.append(el('span', null, k + ': ' + clean(v)));
          c.append(m);
        }
      }
      const rest = a.slice(1).filter(x => articleText(x));
      if (rest.length) {
        const notes = el('div', 'ybn-notes');
        for (const x of rest) {
          const n = el('div', 'ybn-note' + (x.sender === 'Customer' ? ' customer' : ''));
          const from = clean(x.from || x.created_by || '');
          const shortFrom = from.replace(/<[^>]*>/g, '').replace(/"/g, '').trim() || '—';
          n.append(el('div', 'ybn-ava', initials(shortFrom)));
          const main = el('div', 'ybn-note-main');
          const bits = [shortFrom, when(x.created_at)];
          if (x.internal) bits.push('внутренняя заметка');
          main.append(el('div', 'ybn-note-meta', bits.join(' · ')), el('div', 'ybn-note-text', clip(articleText(x), 1500)));
          n.append(main);
          notes.append(n);
        }
        c.append(notes);
      }
    }

    const foot = el('div', 'ybn-foot');
    foot.append(el('span', null, '№ ' + t.number));
    if (/^ITF\d+/.test(t.title)) foot.append(el('span', null, t.title.split(' ')[0]));
    foot.append(el('span', null, 'создана ' + when(t.created_at)));
    if (group) foot.append(el('span', null, clean(group.name)));
    const ownerName = owner && owner.id !== 1 ? userName(owner) : '';
    if (ownerName) foot.append(el('span', null, 'исполнитель ' + ownerName));
    const tu = parseFloat(t.time_unit);
    if (tu > 0) foot.append(el('span', null, 'учтено ' + duration(tu)));
    c.append(foot);
  }

  function prune() {
    if (arts.size < 400) return;
    const keep = new Set(st.items.map(t => t.id + '@' + t.updated_at));
    for (const k of arts.keys()) if (!keep.has(k)) arts.delete(k);
  }

  function need(c) {
    const t = c._t;
    if (!t) return;
    const k = t.id + '@' + t.updated_at;
    if (arts.has(k)) return;
    if (inflight.has(k)) return;
    queue.push({ c, t, k });
    pump();
  }

  function pump() {
    while (running < 4 && queue.length) {
      const job = queue.shift();
      if (!job.c.isConnected) continue;
      if (arts.has(job.k) || inflight.has(job.k)) continue;
      inflight.add(job.k);
      running++;
      api('ticket_articles/by_ticket/' + job.t.id)
        .then(a => {
          a = Array.isArray(a) ? a.slice().sort((x, y) => String(x.created_at).localeCompare(String(y.created_at))) : [];
          arts.set(job.k, a);
          for (const c of list.querySelectorAll('.ybn-card')) if (c._t && c._t.id + '@' + c._t.updated_at === job.k) fill(c, c._t, a);
        })
        .catch(() => {
          for (const c of list.querySelectorAll('.ybn-card')) {
            if (!c._t || c._t.id + '@' + c._t.updated_at !== job.k) continue;
            const w = c.querySelector('.ybn-wait');
            if (w) w.textContent = 'Текст обращения не загрузился. Откройте заявку.';
          }
        })
        .finally(() => { inflight.delete(job.k); running--; pump(); });
    }
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', build);
  else build();
})();
