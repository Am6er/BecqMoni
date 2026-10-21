/* Гид BecqMoni — дневная и ночная темы (AMBER258).
   Ночная — исходная и тема по умолчанию (Amber 21.10.2026: «Дефолт - как сейчас»); выбор хранится в
   localStorage и общий для всех страниц. Скрипт подключается в <head> ДО стилей страницы: атрибут
   data-theme встаёт до первой отрисовки, и дневная страница не мигает тёмной.

   CSS дневной темы — правила [data-theme=light] в guide.css, в стилях страниц и в how-it-works.html.

   Холсты рисуются кодом с цветами под тёмный фон (решение Amber 21.10.2026 вопросником, дословно:
   «Всё светлое, вместе с холстами»). Чтобы не переписывать сотни мест рисования, в дневной теме
   каждый цвет, отданный холсту (fillStyle, strokeStyle, shadowColor, addColorStop), пересчитывается:
     • серые и тёмные (насыщенность < 0.25 или светлота < 0.2) — светлота наоборот: белые подписи
       становятся тёмными, тёмные заливки и фон — светлыми;
     • яркие насыщенные (светлота > 0.5) — темнее с тем же оттенком: золото, неон, линии нуклидов
       остаются узнаваемыми и читаются на белом;
     • прочие (насыщенные средней светлоты) — как есть.
   Прозрачность сохраняется. Цвет, который пересчитывать НЕЛЬЗЯ (подражание окну программы: жёлтый
   флажок пика с чёрной рамкой, белая цифра на цветной метке), задаётся через BQTHEME.keep(цвет). */
(function (W) {
  'use strict';
  const KEY = 'bq-theme', root = document.documentElement;
  let theme = 'dark';
  try { if (localStorage.getItem(KEY) === 'light') theme = 'light'; } catch (e) { }
  function mark() { root.setAttribute('data-theme', theme); root.style.colorScheme = theme; }
  mark();

  /* ── разбор и пересчёт цвета ── */
  const NAMED = { white: [255, 255, 255, 1], black: [0, 0, 0, 1], transparent: [0, 0, 0, 0] };
  const num = (v, max) => /%$/.test(v) ? parseFloat(v) / 100 * max : parseFloat(v);
  function hsl2rgb(h, s, l) {
    h = ((h % 360) + 360) % 360 / 360;
    if (s === 0) return [l * 255, l * 255, l * 255];
    const q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
    const f = t => { t = (t + 1) % 1; return t < 1 / 6 ? p + (q - p) * 6 * t : t < 1 / 2 ? q : t < 2 / 3 ? p + (q - p) * (2 / 3 - t) * 6 : p; };
    return [f(h + 1 / 3) * 255, f(h) * 255, f(h - 1 / 3) * 255];
  }
  function parse(c) {
    c = c.trim().toLowerCase();
    if (NAMED[c]) return NAMED[c].slice();
    let m;
    if (c[0] === '#') {
      let h = c.slice(1);
      if (h.length === 3 || h.length === 4) h = h.split('').map(x => x + x).join('');
      if (!/^[0-9a-f]{6}([0-9a-f]{2})?$/.test(h)) return null;
      const n = parseInt(h.slice(0, 6), 16);
      return [(n >> 16) & 255, (n >> 8) & 255, n & 255, h.length === 8 ? parseInt(h.slice(6), 16) / 255 : 1];
    }
    if ((m = c.match(/^rgba?\(\s*([\d.]+%?)\s*[,\s]\s*([\d.]+%?)\s*[,\s]\s*([\d.]+%?)\s*(?:[,/]\s*([\d.]+%?)\s*)?\)$/)))
      return [num(m[1], 255), num(m[2], 255), num(m[3], 255), m[4] !== undefined ? num(m[4], 1) : 1];
    if ((m = c.match(/^hsla?\(\s*(-?[\d.]+)(?:deg)?\s*[,\s]\s*([\d.]+)%\s*[,\s]\s*([\d.]+)%\s*(?:[,/]\s*([\d.]+%?)\s*)?\)$/)))
      return hsl2rgb(+m[1], +m[2] / 100, +m[3] / 100).concat([m[4] !== undefined ? num(m[4], 1) : 1]);
    return null;
  }
  function toDay(r, g, b) {
    r /= 255; g /= 255; b /= 255;
    const mx = Math.max(r, g, b), mn = Math.min(r, g, b);
    let h = 0, s = 0, l = (mx + mn) / 2;
    if (mx !== mn) {
      const d = mx - mn;
      s = l > 0.5 ? d / (2 - mx - mn) : d / (mx + mn);
      h = (mx === r ? (g - b) / d + (g < b ? 6 : 0) : mx === g ? (b - r) / d + 2 : (r - g) / d + 4) * 60;
    }
    if (s < 0.25 || l < 0.2) l = l < 0.2 ? 1 - l * 0.6 : 1 - l;   // серые и тёмные — наоборот
    else if (l > 0.5) l = 0.47 - (l - 0.5) * 0.4;                    // яркие — темнее, оттенок тот же
    return hsl2rgb(h, s, l);
  }
  const cache = new Map();
  function day(c) {
    let v = cache.get(c);
    if (v !== undefined) return v;
    const p = parse(c);
    if (!p) v = c;
    else {
      const [r, g, b] = toDay(p[0], p[1], p[2]).map(x => Math.round(Math.max(0, Math.min(255, x))));
      const a = Math.max(0, Math.min(1, p[3]));
      v = a >= 1 ? '#' + ((1 << 24) | (r << 16) | (g << 8) | b).toString(16).slice(1) : 'rgba(' + r + ',' + g + ',' + b + ',' + +a.toFixed(3) + ')';
    }
    if (cache.size > 4000) cache.clear();
    cache.set(c, v);
    return v;
  }
  // что уходит в холст: строка — пересчёт в дневной теме; BQTHEME.keep(…) (объект String) — как есть; градиент — как есть
  function conv(v) {
    if (typeof v === 'string') return theme === 'light' ? day(v) : v;
    if (v instanceof String) return v.valueOf();
    return v;
  }

  /* ── перехват цветов холста; контекст с __bqRaw (BQTHEME.raw) и его градиенты идут как есть ── */
  const P = W.CanvasRenderingContext2D && W.CanvasRenderingContext2D.prototype;
  const plain = v => v instanceof String ? v.valueOf() : v;
  if (P) {
    ['fillStyle', 'strokeStyle', 'shadowColor'].forEach(k => {
      const d = Object.getOwnPropertyDescriptor(P, k);
      if (!d || !d.set) return;
      Object.defineProperty(P, k, { configurable: true, enumerable: d.enumerable, get() { return d.get.call(this); }, set(v) { d.set.call(this, this.__bqRaw ? plain(v) : conv(v)); } });
    });
    ['createLinearGradient', 'createRadialGradient', 'createConicGradient'].forEach(k => {
      const f = P[k]; if (!f) return;
      P[k] = function () { const g = f.apply(this, arguments); if (this.__bqRaw) g.__bqRaw = true; return g; };
    });
  }
  const GP = W.CanvasGradient && W.CanvasGradient.prototype;
  if (GP) { const acs = GP.addColorStop; GP.addColorStop = function (o, c) { return acs.call(this, o, this.__bqRaw ? plain(c) : conv(c)); }; }

  /* ── цвета в разметке: квадратики легенд и подписи, раскрашенные под линии холста (style="background:#…").
        Окна-макеты программы светлые сами по себе и не трогаются. Исходный style хранится в data-bq-style. ── */
  const MOCK = '.wp,.win,.qtab,.statusbar,.dlg,.desk,.mbox,.winp,.flagbox,.docs,.shot-frame,.tb,.mb,.dd';
  const STYLE_COLOR = /(^|;)(\s*(?:color|background(?:-color)?)\s*:\s*)([^;]+)/gi;
  function domFix(el) {
    let st = el.getAttribute('data-bq-style');
    if (st === null) {
      st = el.getAttribute('style');
      if (!st || !/(^|;)\s*(color|background(-color)?)\s*:/i.test(st) || (el.closest && el.closest(MOCK))) return;
      el.setAttribute('data-bq-style', st);
    }
    el.setAttribute('style', theme === 'light' ? st.replace(STYLE_COLOR, (m, a, b, v) => a + b + (/var\(|gradient|url\(|^\s*(inherit|initial|none)\s*$/i.test(v) ? v : day(v.trim()))) : st);
  }
  // иконки SVG: stroke="#…" и fill="#…" — тот же пересчёт, что у холстов; исходник — в data-bq-stroke / data-bq-fill
  function svgFix(el) {
    ['stroke', 'fill'].forEach(a => {
      let v = el.getAttribute('data-bq-' + a);
      if (v === null) { v = el.getAttribute(a); if (!v || !/^(#|rgb|hsl)/i.test(v.trim())) return; el.setAttribute('data-bq-' + a, v); }
      el.setAttribute(a, theme === 'light' ? day(v.trim()) : v);
    });
  }
  function domFixAll(root) {
    if (root.nodeType !== 1) return;
    if (root.hasAttribute('style')) domFix(root);
    root.querySelectorAll('[style]').forEach(domFix);
    if (root instanceof SVGElement && (root.hasAttribute('stroke') || root.hasAttribute('fill'))) svgFix(root);
    root.querySelectorAll('svg [stroke],svg [fill],svg[stroke],svg[fill]').forEach(svgFix);
  }
  function domStart() {
    if (theme === 'light') domFixAll(document.body);
    new MutationObserver(ms => {
      if (theme !== 'light') return;
      ms.forEach(m => m.addedNodes.forEach(n => domFixAll(n)));
    }).observe(document.body, { childList: true, subtree: true });
  }
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', domStart); else domStart();

  /* ── API ── */
  const listeners = [];
  const T = W.BQTHEME = {
    get name() { return theme; },
    get light() { return theme === 'light'; },
    keep: c => new String(c),             // цвет мимо пересчёта
    raw: canvas => { const c = canvas.getContext('2d'); if (c) c.__bqRaw = true; return c; },   // весь холст мимо пересчёта (подражание окну программы)
    day: c => theme === 'light' ? day(c) : c,   // для цветов в DOM (style.color), если нужно
    pick: (dark, light) => theme === 'light' ? new String(light) : new String(dark),   // свой цвет каждой теме, мимо пересчёта
    on: f => listeners.push(f),
    set(v) {
      if ((v !== 'light' && v !== 'dark') || v === theme) return;
      theme = v; try { localStorage.setItem(KEY, v); } catch (e) { }
      mark(); cache.clear();
      if (document.body) { domFixAll(document.body); document.querySelectorAll('[data-bq-style]').forEach(domFix); }
      listeners.forEach(f => { try { f(theme); } catch (e) { console.error(e); } });
    },
    // переключатель для шапки: две иконки, как RU|EN (`AMBER258`: «переключение по иконкам сверху»)
    switchHtml: '<div class="theme"><button data-theme-set="light" data-tt="theme.light" aria-label="light">' +
      '<svg viewBox="0 0 24 24" width="16" height="16" aria-hidden="true"><circle cx="12" cy="12" r="4.2" fill="currentColor"/><g stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M12 2.5v2.4M12 19.1v2.4M2.5 12h2.4M19.1 12h2.4M5.3 5.3l1.7 1.7M17 17l1.7 1.7M5.3 18.7 7 17M17 7l1.7-1.7"/></g></svg></button>' +
      '<button data-theme-set="dark" data-tt="theme.dark" aria-label="dark">' +
      '<svg viewBox="0 0 24 24" width="16" height="16" aria-hidden="true"><path d="M20.5 14.6A8.5 8.5 0 0 1 9.4 3.5a8.5 8.5 0 1 0 11.1 11.1z" fill="currentColor"/></svg></button></div>',
    bind(box) {
      const sync = () => box.querySelectorAll('[data-theme-set]').forEach(b => { const on = b.dataset.themeSet === theme; b.classList.toggle('on', on); b.setAttribute('aria-pressed', on); });
      box.querySelectorAll('[data-theme-set]').forEach(b => b.addEventListener('click', () => T.set(b.dataset.themeSet)));
      T.on(sync); sync();
    },
  };
})(window);
