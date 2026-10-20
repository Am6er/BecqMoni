/* Гид BecqMoni — общий скрипт всех страниц (AMBER239).
   Шапка с оглавлением, RU/EN, снимки окон с пронумерованными точками, листалка глав,
   помощники для графиков на canvas. Числа — с точкой и без группировки разрядов. */
'use strict';
(function (W) {
  const G = W.GUIDE = {};

  /* ── главы: единственный список, из него строятся оглавление и листалка ── */
  const CHAPTERS = [
    // главная — обложка без номера; номер главы = её место в списке (`AMBER244`: «Предисловие 1, главная без номера»)
    { id: 'index', file: 'index.html', ru: ['С чего начать', 'что такое BecqMoni и путь за пять минут'], en: ['Getting started', 'what BecqMoni is and the five-minute path'] },
    { id: 'preface', file: 'preface.html', ru: ['Предисловие', 'как вспышки в кристалле становятся беккерелями'], en: ['Preface', 'how flashes in a crystal become becquerels'] },
    { id: 'install', file: 'install.html', ru: ['Установка и окно программы', 'установка, обновления, где что на экране'], en: ['Install and the main window', 'install, updates, what is where on screen'] },
    { id: 'devices', file: 'devices.html', ru: ['Приборы', 'подключение и конфигурация прибора'], en: ['Devices', 'connecting and configuring a detector'] },
    { id: 'acquire', file: 'acquire.html', ru: ['Набор спектра', 'старт, время, фон, сведения об образце'], en: ['Acquiring a spectrum', 'start, time, background, sample info'] },
    { id: 'view', file: 'view.html', ru: ['Окно спектра', 'шкалы, масштаб, режимы, подписи'], en: ['The spectrum view', 'scales, zoom, modes, labels'] },
    { id: 'calibration', file: 'calibration.html', ru: ['Калибровка', 'шкала энергий и ширина пиков'], en: ['Calibration', 'energy scale and peak width'] },
    { id: 'peaks', file: 'peaks.html', ru: ['Пики и нуклиды', 'поиск пиков, библиотека, наборы, NucBase'], en: ['Peaks and nuclides', 'peak search, library, sets, NucBase'] },
    { id: 'geometry', file: 'geometry.html', ru: ['Геометрия и матрица', 'кривая эффективности, матрица отклика, GPU'], en: ['Geometry and matrix', 'efficiency curve, response matrix, GPU'] },
    { id: 'fsa', file: 'fsa.html', ru: ['FSA на практике', 'включить разбор и прочитать отчёт'], en: ['FSA in practice', 'run the decomposition and read the report'] },
    { id: 'result', file: 'result.html', ru: ['Активность и результат', 'Бк, Бк/кг, дата отбора, предел обнаружения'], en: ['Activity and results', 'Bq, Bq/kg, sampling date, detection limit'] },
    { id: 'how', file: 'how-it-works.html', ru: ['Как это устроено', 'для продвинутых: всё руками — геометрия, матрица, FSA, активность'], en: ['How it works', 'advanced: hands-on geometry, matrix, FSA, activity'] },
    { id: 'files', file: 'files.html', ru: ['Файлы и вопросы', 'форматы, перенос, частые вопросы'], en: ['Files and FAQ', 'formats, moving data, common questions'] },
  ];
  G.CHAPTERS = CHAPTERS;

  /* ── общее ── */
  const $ = (s, r = document) => r.querySelector(s);
  const $$ = (s, r = document) => Array.from(r.querySelectorAll(s));
  const clamp = (x, a, b) => Math.max(a, Math.min(b, x));
  const lerp = (a, b, u) => a + (b - a) * u;
  const fx = (x, d) => (+x).toFixed(d);
  function sig(x, n = 3) {
    if (!isFinite(x)) return '—';
    if (x === 0) return '0';
    const d = Math.max(0, n - 1 - Math.floor(Math.log10(Math.abs(x))));
    return (+x).toFixed(Math.min(d, 6));
  }
  function trimZ(s) { return s.indexOf('.') < 0 ? s : s.replace(/0+$/, '').replace(/\.$/, ''); }
  function tickLabel(v) { const n = Math.abs(v); if (n >= 1e6) return trimZ(sig(v / 1e6, 2)) + 'M'; if (n >= 1e4) return trimZ(sig(v / 1e3, 3)) + 'k'; return trimZ(sig(v, 3)); }
  Object.assign(G, { $, $$, clamp, lerp, fx, sig, trimZ, tickLabel });

  /* ── язык: тот же ключ, что у how-it-works.html — выбор общий.
        `AMBER243`: ?lang=ru|en в адресе важнее сохранённого выбора — ссылку дают сразу на нужном языке ── */
  let LANG = 'ru';
  const urlLang = (() => { try { const v = new URLSearchParams(location.search).get('lang'); return v === 'ru' || v === 'en' ? v : null; } catch (e) { return null; } })();
  if (urlLang) { LANG = urlLang; try { localStorage.setItem('bq-hiw-lang', LANG); } catch (e) { } }
  else try { const s = localStorage.getItem('bq-hiw-lang'); if (s === 'ru' || s === 'en') LANG = s; else if (!/^(ru|uk|be|kk)/i.test(navigator.language || '')) LANG = 'en'; } catch (e) { }
  // ссылка на страницу гида («fsa.html», «geometry.html#matrix», «./») получает ?lang=; внешние, якоря и файлы — нет
  function withLang(href) {
    if (!href || /^([a-z][a-z0-9+.-]*:|\/\/|#)/i.test(href)) return null;
    const m = href.match(/^([^?#]*)(\?[^#]*)?(#.*)?$/); if (!m || !/(^|\/)([\w-]+\.html)?$/.test(m[1])) return null;
    const q = new URLSearchParams((m[2] || '').slice(1)); q.set('lang', LANG);
    return m[1] + '?' + q.toString() + (m[3] || '');
  }
  // адресная строка и ссылки страницы — за переключателем RU|EN (скопированный адрес уже на нужном языке)
  function syncLangUrl() {
    $$('a[href]').forEach(a => { const h = withLang(a.getAttribute('href')); if (h) a.setAttribute('href', h); });
    try { const u = new URL(location.href); if (u.searchParams.get('lang') !== LANG) { u.searchParams.set('lang', LANG); history.replaceState(history.state, '', u.href); } } catch (e) { }
  }
  // ссылки, построенные страницей позже (карточки, подсказки), — в момент щелчка
  document.addEventListener('click', e => { const a = e.target.closest && e.target.closest('a[href]'); if (!a) return; const h = withLang(a.getAttribute('href')); if (h) a.setAttribute('href', h); }, true);
  const T = {
    ru: {
      'brand.sub': 'гид', 'toc': 'Оглавление', 'prev': '← Назад', 'next': 'Дальше →', 'hood': 'Как это сделано в BecqMoni',
      'shot.miss': 'снимок окна появится здесь', 'shot.zoom': 'щёлкните, чтобы увеличить',
      'foot': 'Гид по BecqMoni. Снимки окон — настоящей программы; модели на графиках упрощены и считаются прямо в браузере. Числа печатаются с точкой.',
    },
    en: {
      'brand.sub': 'guide', 'toc': 'Contents', 'prev': '← Back', 'next': 'Next →', 'hood': 'How BecqMoni does it',
      'shot.miss': 'the window screenshot goes here', 'shot.zoom': 'click to enlarge',
      'foot': 'The BecqMoni guide. Window screenshots are of the real app; models in the charts are simplified and run right in your browser.',
    },
  };
  G.texts = d => { ['ru', 'en'].forEach(l => Object.assign(T[l], d[l] || {})); };
  function t(k, ...a) { let s = (T[LANG][k] !== undefined ? T[LANG][k] : T.ru[k]); if (s === undefined) return k; a.forEach((v, i) => { s = s.split('{' + i + '}').join(v); }); return s; }
  G.t = t;
  Object.defineProperty(G, 'lang', { get: () => LANG });
  const redrawers = [];
  G.onRedraw = f => redrawers.push(f);
  // перерисовка пересобирает ссылки (карточки глав, листалка) — язык ставится заново после неё
  function redrawAll() { redrawers.forEach(f => { try { f(); } catch (e) { console.error(e); } }); syncLangUrl(); }
  G.redrawAll = redrawAll;
  G.langHref = h => withLang(h) || h;
  function applyLang() {
    document.documentElement.lang = LANG;
    $$('[data-t]').forEach(el => { el.innerHTML = t(el.dataset.t); });
    $$('[data-tt]').forEach(el => { el.title = t(el.dataset.tt); });
    $$('.lang button').forEach(b => b.classList.toggle('on', b.dataset.lang === LANG));
    const ch = cur(); if (ch) document.title = (ch.id === 'index' ? '' : ch[LANG][0] + ' — ') + 'BecqMoni ' + t('brand.sub');
    buildToc(); buildPager(); buildShots();
    redrawAll();
  }
  G.applyLang = applyLang;
  function cur() { const id = document.body.dataset.chapter; return CHAPTERS.find(c => c.id === id); }

  /* ── шапка, оглавление, листалка ── */
  function buildHeader() {
    const h = document.createElement('header'); h.className = 'top';
    h.innerHTML = '<div class="wrap"><a class="brand" href="index.html">Becq<span>Moni</span><small data-t="brand.sub"></small></a>' +
      '<button class="toc-btn" id="tocBtn" aria-expanded="false"><i></i><span data-t="toc"></span></button>' +
      '<nav class="steps" id="nav"></nav><div class="lang"><button data-lang="ru">RU</button><button data-lang="en">EN</button></div></div>';
    document.body.insertBefore(h, document.body.firstChild);
    const p = document.createElement('div'); p.className = 'toc-panel'; p.id = 'tocPanel'; p.innerHTML = '<div class="wrap"><div class="toc-grid" id="tocGrid"></div></div>';
    h.after(p);
    const btn = $('#tocBtn');
    btn.onclick = () => { const o = p.classList.toggle('open'); btn.classList.toggle('on', o); btn.setAttribute('aria-expanded', o); };
    document.addEventListener('keydown', e => { if (e.key === 'Escape') { p.classList.remove('open'); btn.classList.remove('on'); $('#lightbox') && $('#lightbox').classList.remove('open'); } });
    document.addEventListener('click', e => { if (p.classList.contains('open') && !p.contains(e.target) && !btn.contains(e.target)) { p.classList.remove('open'); btn.classList.remove('on'); } });
    $$('.lang button', h).forEach(b => b.addEventListener('click', () => { LANG = b.dataset.lang; try { localStorage.setItem('bq-hiw-lang', LANG); } catch (e) { } applyLang(); }));
    // шаги страницы — из разделов с data-nav
    const secs = $$('section.sec[data-nav]'), nav = $('#nav');
    secs.forEach((s, i) => { const a = document.createElement('a'); a.href = '#' + s.id; a.innerHTML = '<b>' + (i + 1) + '</b><span data-t="' + s.dataset.nav + '"></span>'; nav.appendChild(a); });
    const links = $$('a', nav);
    const ob = new IntersectionObserver(es => { es.forEach(e => { if (e.isIntersecting) { const i = secs.indexOf(e.target); links.forEach((l, j) => l.classList.toggle('on', j === i)); } }); }, { rootMargin: '-45% 0px -50% 0px' });
    secs.forEach(s => ob.observe(s));
  }
  function buildToc() {
    const g = $('#tocGrid'); if (!g) return; const c = cur();
    g.innerHTML = CHAPTERS.map((ch, i) => '<a class="toc-item' + (c && c.id === ch.id ? ' cur' : '') + '" href="' + ch.file + '"><b>' + (i ? i : '⌂') + '</b><div><span>' + ch[LANG][0] + '</span><small>' + ch[LANG][1] + '</small></div></a>').join('');
  }
  function buildPager() {
    const box = $('#pager'); const c = cur(); if (!box || !c) return;
    const i = CHAPTERS.indexOf(c), pv = CHAPTERS[i - 1], nx = CHAPTERS[i + 1];
    box.className = 'pager';
    box.innerHTML = (pv ? '<a class="prev" href="' + pv.file + '"><small>' + t('prev') + '</small><b>' + pv[LANG][0] + '</b></a>' : '<span></span>') +
      (nx ? '<a class="next" href="' + nx.file + '"><small>' + t('next') + '</small><b>' + nx[LANG][0] + '</b></a>' : '<span></span>');
  }

  /* ── снимки окон: img/<язык>/<id>.png, точки из data-hot (доли ширины/высоты) ── */
  // версия снимков: её ставит tools/docs_stamp.py перед заливкой — новый снимок под старым именем не берётся из кэша (`AMBER248`)
  const IMGV = '2bb62bc0';
  function buildShots() {
    $$('figure.shot[data-shot]').forEach(fig => {
      const id = fig.dataset.shot;
      let frame = $('.shot-frame', fig);
      if (!frame) { frame = document.createElement('div'); frame.className = 'shot-frame'; fig.insertBefore(frame, fig.firstChild); }
      frame.innerHTML = ''; frame.title = t('shot.zoom');
      const img = new Image(); img.alt = id; img.loading = 'lazy';
      const other = LANG === 'ru' ? 'en' : 'ru';
      img.onerror = () => { if (!img.dataset.tried) { img.dataset.tried = '1'; img.src = 'img/' + other + '/' + id + '.png?v=' + IMGV; } else { frame.innerHTML = '<div class="miss">' + t('shot.miss') + ' · <code>' + id + '</code></div>'; } };
      img.src = 'img/' + LANG + '/' + id + '.png?v=' + IMGV;
      frame.appendChild(img);
      const hotSrc = fig.getAttribute('data-hot-' + LANG) || fig.getAttribute('data-hot');
      // легенда — по data-legend (id списка), внутри фигуры или рядом с ней в той же сетке (как в calibration.html)
      let lg = fig.dataset.legend ? document.getElementById(fig.dataset.legend) : $('ol.shot-legend', fig); if (!lg && !fig.dataset.legend && fig.parentElement) { const own = $$('ol.shot-legend', fig.parentElement).filter(o => !o.closest('figure')); lg = own[0] || null; }
      const items = lg ? $$(':scope > li', lg) : [];
      let hot = []; try { hot = hotSrc ? JSON.parse(hotSrc) : []; } catch (e) { console.error('data-hot', id, e); }
      const boxes = hot.map((r, i) => { const d = document.createElement('div'); d.className = 'hot'; d.style.left = (r[0] * 100) + '%'; d.style.top = (r[1] * 100) + '%'; d.style.width = (r[2] * 100) + '%'; d.style.height = (r[3] * 100) + '%'; d.innerHTML = '<b>' + (i + 1) + '</b>'; frame.appendChild(d); return d; });
      const on = (i, v) => { if (boxes[i]) boxes[i].classList.toggle('on', v); if (items[i]) items[i].classList.toggle('on', v); };
      boxes.forEach((b, i) => { b.onmouseenter = () => on(i, true); b.onmouseleave = () => on(i, false); });
      items.forEach((li, i) => { li.onmouseenter = () => on(i, true); li.onmouseleave = () => on(i, false); });
      frame.onclick = e => { if (e.target.closest('.hot')) return; const lb = $('#lightbox'); $('img', lb).src = img.src; lb.classList.add('open'); };
    });
  }
  function buildLightbox() {
    const lb = document.createElement('div'); lb.className = 'lightbox'; lb.id = 'lightbox'; lb.innerHTML = '<img alt="">';
    lb.onclick = () => lb.classList.remove('open'); document.body.appendChild(lb);
    const tp = document.createElement('div'); tp.className = 'gtip'; tp.id = 'tip';   // не .tip: так называется и врезка callout tip
    document.body.appendChild(tp);
  }

  /* ── графики ── */
  // облегчённый режим (`AMBER246`): узкий экран или слабый процессор — меньше точек на пиксель, короче фоновые куски, 30 кадров
  const lite = () => W.innerWidth < 760 || (navigator.hardwareConcurrency || 8) <= 4;
  G.lite = lite;
  function setup(c) {
    const r = c.getBoundingClientRect(), d = Math.min(W.devicePixelRatio || 1, lite() ? 1.5 : 2);
    const w = Math.max(20, Math.round(r.width)), h = Math.max(20, Math.round(r.height));
    if (c.width !== Math.round(w * d) || c.height !== Math.round(h * d)) { c.width = Math.round(w * d); c.height = Math.round(h * d); }
    const ctx = c.getContext('2d'); ctx.setTransform(d, 0, 0, d, 0, 0); ctx.clearRect(0, 0, w, h);
    return { ctx, w, h };
  }
  function scale(kind, d0, d1, r0, r1) {
    const f = kind === 'log' ? Math.log10 : kind === 'sqrt' ? Math.sqrt : (v => v);
    const a = f(d0), b = f(d1);
    const s = v => { if (kind === 'log') v = Math.max(v, d0 * 1e-4); else if (kind === 'sqrt') v = Math.max(v, 0); return r0 + (f(v) - a) / (b - a) * (r1 - r0); };
    s.inv = p => { const u = a + (p - r0) / (r1 - r0) * (b - a); return kind === 'log' ? Math.pow(10, u) : kind === 'sqrt' ? u * u : u; };
    s.kind = kind; s.d0 = d0; s.d1 = d1;
    return s;
  }
  function niceTicks(a, b, n = 6) {
    const span = b - a; if (span <= 0) return [a];
    const st0 = span / n, mag = Math.pow(10, Math.floor(Math.log10(st0))), e = st0 / mag;
    const st = (e >= 7.5 ? 10 : e >= 3.5 ? 5 : e >= 1.5 ? 2 : 1) * mag; const out = [];
    for (let v = Math.ceil(a / st) * st; v <= b + st * 1e-9; v += st) out.push(+v.toFixed(10));
    return out;
  }
  function logTicks(a, b, mults = [1, 2, 5]) { const out = []; for (let p = Math.floor(Math.log10(a)); p <= Math.ceil(Math.log10(b)); p++) for (const m of mults) { const v = m * Math.pow(10, p); if (v >= a * 0.999 && v <= b * 1.001) out.push(v); } return out; }
  function sqrtTicks(a, b) { return niceTicks(0, b, 4).filter(v => v > 0 && v >= a); }
  function axes(ctx, box, X, Y, o = {}) {
    ctx.save(); ctx.font = '11px system-ui,sans-serif'; ctx.lineWidth = 1;
    const xt = o.xt || (X.kind === 'log' ? logTicks(X.d0, X.d1) : niceTicks(X.d0, X.d1, o.nx || 6));
    const yt = o.yt || (Y.kind === 'log' ? logTicks(Y.d0, Y.d1, [1]) : Y.kind === 'sqrt' ? sqrtTicks(Y.d0, Y.d1) : niceTicks(Y.d0, Y.d1, 4));
    ctx.strokeStyle = 'rgba(255,255,255,.07)'; ctx.fillStyle = '#7f8db0'; ctx.textAlign = 'center'; ctx.textBaseline = 'top';
    xt.forEach(v => { const x = X(v); if (x < box.l - 1 || x > box.r + 1) return; ctx.beginPath(); ctx.moveTo(x, box.t); ctx.lineTo(x, box.b); ctx.stroke(); ctx.fillText(o.xf ? o.xf(v) : tickLabel(v), x, box.b + 4); });
    if (!o.noY) { ctx.textAlign = 'right'; ctx.textBaseline = 'middle'; yt.forEach(v => { const y = Y(v); if (y < box.t - 1 || y > box.b + 1) return; ctx.beginPath(); ctx.moveTo(box.l, y); ctx.lineTo(box.r, y); ctx.stroke(); ctx.fillText(o.yf ? o.yf(v) : tickLabel(v), box.l - 5, y); }); }
    ctx.strokeStyle = 'rgba(255,255,255,.22)'; ctx.beginPath(); ctx.moveTo(box.l, box.t); ctx.lineTo(box.l, box.b); ctx.lineTo(box.r, box.b); ctx.stroke();
    ctx.fillStyle = '#93a3c4'; ctx.font = '11.5px system-ui,sans-serif';
    if (o.xl) { ctx.textAlign = 'right'; ctx.textBaseline = 'bottom'; ctx.fillText(o.xl, box.r, box.b - 4); }
    if (o.yl) { ctx.textAlign = 'left'; ctx.textBaseline = 'top'; ctx.fillText(o.yl, box.l + 6, box.t + 2); }
    ctx.restore();
  }
  function eHue(E) { return 268 - 268 * clamp(Math.log(E / 25) / Math.log(3000 / 25), 0, 1); }
  function eCol(E, a = 1) { return 'hsla(' + eHue(E).toFixed(0) + ',95%,62%,' + a + ')'; }
  function rgba(hex, a) { const n = parseInt(hex.slice(1), 16); return 'rgba(' + ((n >> 16) & 255) + ',' + ((n >> 8) & 255) + ',' + (n & 255) + ',' + a + ')'; }
  function showTip(ev, html) { const tip = $('#tip'); tip.innerHTML = html; tip.style.display = 'block'; const w = tip.offsetWidth, h = tip.offsetHeight; let x = ev.clientX + 14, y = ev.clientY + 14; if (x + w > innerWidth - 8) x = ev.clientX - w - 14; if (y + h > innerHeight - 8) y = ev.clientY - h - 14; tip.style.left = x + 'px'; tip.style.top = y + 'px'; }
  function hideTip() { const tip = $('#tip'); if (tip) tip.style.display = 'none'; }
  function gauss() { let u = 0; while (u === 0) u = Math.random(); return Math.sqrt(-2 * Math.log(u)) * Math.cos(2 * Math.PI * Math.random()); }
  function poisson(l) { if (l <= 0) return 0; if (l < 40) { const L = Math.exp(-l); let k = 0, p = 1; do { k++; p *= Math.random(); } while (p > L); return k - 1; } return Math.max(0, Math.round(l + Math.sqrt(l) * gauss())); }
  const Jobs = {
    list: [], running: false,
    add(name, gen) { this.list = this.list.filter(j => j.name !== name); this.list.push({ name, it: gen }); if (!this.running) { this.running = true; requestAnimationFrame(() => this.tick()); } },
    tick() { const t0 = performance.now(); while (this.list.length && performance.now() - t0 < (lite() ? 6 : 14)) { const j = this.list[0]; let r; try { r = j.it.next(); } catch (e) { console.error(e); r = { done: true }; } if (r.done) this.list.shift(); } if (this.list.length) requestAnimationFrame(() => this.tick()); else this.running = false; }
  };
  const visible = new Map();
  const io = new IntersectionObserver(es => es.forEach(e => visible.set(e.target, e.isIntersecting)), { rootMargin: '120px' });
  Object.assign(G, { setup, scale, niceTicks, logTicks, sqrtTicks, axes, eHue, eCol, rgba, showTip, hideTip, gauss, poisson, Jobs, visible, io });

  /* ── запуск: страница зовёт GUIDE.init() после своих GUIDE.texts(...) ── */
  G.init = () => {
    buildHeader(); buildLightbox();
    let rz = 0; W.addEventListener('resize', () => { clearTimeout(rz); rz = setTimeout(redrawAll, 120); });
    applyLang();
  };
})(window);
