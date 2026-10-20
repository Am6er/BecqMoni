/* BecqMoni гид — кванты: перенос фотонов Монте-Карло для живых анимаций.
   Движок перенесён из how-it-works.html без изменения физики (`AMBER244`): сечения NaI / Al / вода,
   фотоэффект с вылетом K-рентгена йода, комптон по Клейну — Нишине, рождение пар, домик из свинца.
   Подключать ПОСЛЕ guide.js. Даёт GUIDE.Q: heroAnim (банка → кристалл → спектр), photonLab
   («Фотон в кристалле»), а также dims, historyA, runW, blur, view, drawSetup, drawTrack для сцен страниц. */
(function (G) {
  'use strict';
  const { clamp, lerp, setup, scale, axes, eCol, rgba, gauss, Jobs, visible, io } = G;
  const $ = (s, r) => (r || document).querySelector(s), $$ = (s, r) => Array.from((r || document).querySelectorAll(s));
  const t = (...a) => G.t(...a);
  const ru = () => G.lang === 'ru';

  /* сечения, см²/г: те же таблицы, что в how-it-works.html */
  const XSD = {"E":[15.0,16.33,17.78,19.36,21.08,22.96,25.0,27.22,29.64,32.27,33.1,33.25,35.14,38.26,41.66,45.36,49.39,53.78,58.56,63.76,69.43,75.6,82.31,87.9,88.1,89.63,97.59,106.26,115.7,125.99,137.18,149.37,162.64,177.09,192.83,209.96,228.62,248.93,271.05,295.13,321.35,349.91,381.0,414.85,451.71,491.84,535.55,583.13,634.94,691.36,752.79,819.68,892.51,971.81,1058.16,1152.18,1254.55,1366.02,1487.4,1619.56,1763.46,1920.15,2090.76,2276.53,2478.81,2699.05,2938.87,3200.0],"NaI":{"ph":[45.7,36.13,28.55,22.56,17.79,14,11.02,8.68,6.835,5.374,5.001,29.57,25.59,20.49,16.36,13.04,10.4,8.244,6.531,5.161,4.072,3.214,2.534,2.105,2.091,1.992,1.566,1.229,0.9639,0.7557,0.5927,0.4648,0.3645,0.2858,0.2241,0.1762,0.1388,0.1093,0.08613,0.06785,0.05386,0.04282,0.03405,0.0272,0.02186,0.01757,0.01424,0.01157,0.00948,0.007802,0.006422,0.005309,0.004436,0.003708,0.003104,0.002628,0.002225,0.001904,0.001629,0.001405,0.001213,0.001047,0.0009089,0.000796,0.0006971,0.0006105,0.0005347,0.000472],"co":[0.0841,0.08708,0.09017,0.09338,0.09611,0.09855,0.1011,0.1036,0.1063,0.1083,0.1089,0.109,0.1101,0.1118,0.1132,0.1143,0.1155,0.1161,0.1167,0.1168,0.1167,0.1166,0.1162,0.1156,0.1156,0.1154,0.1146,0.1133,0.1117,0.1102,0.1087,0.1072,0.1051,0.103,0.1009,0.09858,0.0961,0.09369,0.09133,0.08903,0.08649,0.08394,0.08147,0.07898,0.07644,0.07398,0.07147,0.06902,0.06654,0.0641,0.06175,0.05944,0.05711,0.05487,0.05263,0.05046,0.04836,0.04627,0.04427,0.04226,0.04033,0.03849,0.03668,0.03488,0.03317,0.03154,0.03,0.02845],"pa":[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,2.592e-05,9.331e-05,0.0001685,0.0003452,0.0007073,0.001051,0.001509,0.002166,0.002895,0.003507,0.004249,0.005146,0.006233,0.007191],"tot":[47.38,37.66,29.95,23.83,18.94,15.03,11.95,9.522,7.596,6.061,5.667,30.23,26.21,21.05,16.87,13.5,10.82,8.625,6.878,5.478,4.363,3.481,2.78,2.336,2.322,2.219,1.777,1.425,1.147,0.9272,0.754,0.6172,0.5083,0.4219,0.3534,0.299,0.2555,0.2206,0.1924,0.1696,0.1512,0.136,0.1233,0.1128,0.1039,0.09632,0.08975,0.08401,0.07892,0.07435,0.07024,0.0665,0.06302,0.05983,0.05682,0.05407,0.05151,0.04916,0.04714,0.04517,0.04343,0.04202,0.04076,0.03942,0.03831,0.03746,0.0369,0.03623]},"Al":{"ph":[7.515,5.786,4.453,3.427,2.63,2.013,1.543,1.182,0.9058,0.6922,0.6386,0.6296,0.5284,0.4035,0.3077,0.2345,0.1786,0.1359,0.1034,0.07862,0.05974,0.0454,0.03451,0.02791,0.02771,0.02621,0.01991,0.01514,0.01151,0.008751,0.006655,0.005061,0.003861,0.002947,0.002248,0.001724,0.001326,0.00102,0.000785,0.000604,0.0004699,0.0003665,0.0002858,0.0002244,0.0001776,0.0001406,0.0001126,9.042e-05,7.348e-05,6.007e-05,4.91e-05,4.037e-05,3.367e-05,2.809e-05,2.327e-05,1.975e-05,1.677e-05,1.442e-05,1.241e-05,1.078e-05,9.379e-06,8.16e-06,7.142e-06,6.31e-06,5.575e-06,4.925e-06,4.351e-06,3.876e-06],"co":[0.1265,0.1295,0.1327,0.1358,0.1383,0.1402,0.1422,0.1441,0.1462,0.1472,0.1475,0.1475,0.1481,0.149,0.1495,0.1495,0.1496,0.1491,0.1485,0.1474,0.1461,0.1448,0.1432,0.1417,0.1417,0.1413,0.1393,0.1369,0.1343,0.1318,0.1293,0.1268,0.1239,0.1209,0.118,0.1149,0.1117,0.1086,0.1056,0.1026,0.09947,0.09634,0.09331,0.09029,0.08724,0.0843,0.08135,0.07848,0.07559,0.07276,0.07003,0.06736,0.06469,0.06213,0.05959,0.0571,0.05471,0.05232,0.05004,0.04776,0.04557,0.04348,0.04145,0.03941,0.03747,0.03562,0.03387,0.03212],"pa":[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,4.97e-06,1.789e-05,3.241e-05,7.154e-05,0.0001579,0.0002463,0.0003699,0.0005554,0.0007705,0.0009577,0.001189,0.001476,0.001832,0.002153],"tot":[7.955,6.192,4.829,3.777,2.957,2.319,1.83,1.454,1.164,0.9367,0.8794,0.8697,0.7611,0.6262,0.5211,0.4393,0.376,0.3261,0.2872,0.2563,0.2317,0.2124,0.1968,0.1864,0.1861,0.1837,0.1731,0.1638,0.1559,0.1491,0.1432,0.1381,0.1329,0.1283,0.124,0.1198,0.1157,0.1119,0.1083,0.1049,0.1013,0.09787,0.09458,0.09134,0.08812,0.08503,0.08196,0.07899,0.07602,0.07312,0.07033,0.06761,0.0649,0.06231,0.05975,0.05725,0.05485,0.05249,0.05027,0.04807,0.046,0.04409,0.04226,0.0404,0.03868,0.03712,0.03572,0.03429]},"H2O":{"ph":[1.369,1.043,0.7934,0.6037,0.4585,0.3474,0.2635,0.1999,0.1516,0.1148,0.1056,0.1041,0.08682,0.0657,0.04968,0.03755,0.02837,0.02142,0.01617,0.01221,0.009215,0.006956,0.005252,0.004228,0.004197,0.003965,0.002994,0.002264,0.001712,0.001295,0.0009796,0.0007409,0.0005628,0.0004276,0.0003248,0.0002481,0.0001903,0.000146,0.000112,8.587e-05,6.663e-05,5.183e-05,4.032e-05,3.158e-05,2.495e-05,1.971e-05,1.576e-05,1.263e-05,1.025e-05,8.375e-06,6.841e-06,5.622e-06,4.689e-06,3.912e-06,3.208e-06,2.724e-06,2.314e-06,1.992e-06,1.715e-06,1.493e-06,1.302e-06,1.135e-06,9.952e-07,8.811e-07,7.801e-07,6.906e-07,6.115e-07,5.456e-07],"co":[0.1699,0.1721,0.1743,0.1765,0.1781,0.1792,0.1804,0.1815,0.1827,0.1828,0.1828,0.1828,0.1828,0.1828,0.1823,0.1814,0.1805,0.179,0.1775,0.1755,0.1733,0.1711,0.1688,0.1667,0.1666,0.1661,0.1634,0.1602,0.157,0.1537,0.1506,0.1475,0.1439,0.1403,0.1368,0.1331,0.1293,0.1257,0.1221,0.1186,0.1149,0.1113,0.1077,0.1042,0.1007,0.09727,0.09384,0.09049,0.08715,0.08388,0.08074,0.07766,0.07458,0.07163,0.0687,0.06582,0.06306,0.0603,0.05767,0.05504,0.05253,0.05012,0.04775,0.0454,0.04316,0.04104,0.03902,0.03701],"pa":[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,2.818e-06,1.014e-05,1.838e-05,4.084e-05,9.073e-05,0.0001419,0.0002136,0.0003214,0.0004467,0.0005572,0.0006938,0.0008631,0.001073,0.001264],"tot":[1.672,1.333,1.072,0.873,0.7181,0.598,0.5063,0.4361,0.3821,0.3391,0.3281,0.3263,0.3055,0.2795,0.2587,0.2419,0.2286,0.2174,0.2082,0.2001,0.1931,0.1872,0.1818,0.1777,0.1776,0.1766,0.172,0.1673,0.1627,0.1585,0.1545,0.1507,0.1465,0.1425,0.1386,0.1346,0.1306,0.1267,0.1229,0.1193,0.1155,0.1118,0.1081,0.1046,0.101,0.09752,0.09405,0.09067,0.0873,0.08401,0.08084,0.07775,0.07466,0.07169,0.06876,0.06588,0.06312,0.06038,0.05779,0.05521,0.05276,0.05046,0.04821,0.04597,0.04387,0.04191,0.0401,0.03828]}};

  G.texts({
    ru: {
      'qn.stat': 'вылетело <b>{0}</b> · поймано кристаллом <b>{1}</b> · в спектре <b>{2}</b>',
      'qn.cap': 'Банка с пробой светит во все стороны, кристалл ловит малую часть — и из пойманного растёт спектр',
      'qn.ax.e': 'энергия, кэВ', 'qn.ax.cnt': 'отсчёты', 'qn.spec': 'Спектр', 'qn.pmt': 'ФЭУ',
      'qn.ann.fep': 'пик', 'qn.ann.ce': 'комптоновский край', 'qn.ann.bs': 'обратное рассеяние', 'qn.ann.pb': 'рентген Pb',
      'qn.ann.se': 'вылет 511', 'qn.ann.de': 'вылет 2×511', 'qn.ann.ie': 'вылет йода',
      'qn.st': 'выпущено <b>{0}</b> · провзаимодействовали <b>{1}</b> · в пике <b>{2}</b>',
      'qn.lg.ph': 'фотоэффект — отдал всё', 'qn.lg.co': 'комптон — отскочил', 'qn.lg.pa': 'рождение пары e⁺e⁻',
      'qn.lg.esc': 'улетел', 'qn.lg.abs': 'застрял по дороге', 'qn.kev': 'кэВ',
    },
    en: {
      'qn.stat': 'emitted <b>{0}</b> · caught by the crystal <b>{1}</b> · in the spectrum <b>{2}</b>',
      'qn.cap': 'A jar with a sample shines in all directions, the crystal catches a small part — and the spectrum grows from what it caught',
      'qn.ax.e': 'energy, keV', 'qn.ax.cnt': 'counts', 'qn.spec': 'Spectrum', 'qn.pmt': 'PMT',
      'qn.ann.fep': 'peak', 'qn.ann.ce': 'Compton edge', 'qn.ann.bs': 'backscatter', 'qn.ann.pb': 'Pb X-rays',
      'qn.ann.se': '511 escape', 'qn.ann.de': '2×511 escape', 'qn.ann.ie': 'iodine escape',
      'qn.st': 'fired <b>{0}</b> · interacted <b>{1}</b> · in the peak <b>{2}</b>',
      'qn.lg.ph': 'photoeffect — gave up everything', 'qn.lg.co': 'Compton — bounced', 'qn.lg.pa': 'pair e⁺e⁻ created',
      'qn.lg.esc': 'escaped', 'qn.lg.abs': 'stopped on the way', 'qn.kev': 'keV',
    },
  });

  /* ============================== СЕЧЕНИЯ ============================== */
  const XS = (() => {
    const g = XSD.E, lg = g.map(Math.log);
    function ip(arr, E) {
      const L = Math.log(E); let i = 0;
      if (L >= lg[lg.length - 1]) i = lg.length - 2; else if (L > lg[0]) { while (lg[i + 1] < L) i++; }
      const y0 = arr[i], y1 = arr[i + 1], u = (L - lg[i]) / (lg[i + 1] - lg[i]);
      if (y0 > 0 && y1 > 0) return Math.exp(Math.log(y0) + (Math.log(y1) - Math.log(y0)) * u);
      return Math.max(0, y0 + (y1 - y0) * u);
    }
    const N = 1100, L0 = Math.log(8), L1 = Math.log(3400), dL = (L1 - L0) / (N - 1);
    const ph = new Float64Array(N), co = new Float64Array(N), pa = new Float64Array(N), al = new Float64Array(N), wa = new Float64Array(N);
    const RN = 3.67, RA = 2.70;
    for (let k = 0; k < N; k++) {
      const E = Math.exp(L0 + k * dL);
      ph[k] = ip(XSD.NaI.ph, E) * RN; co[k] = ip(XSD.NaI.co, E) * RN;
      pa[k] = E > 1022.1 ? ip(XSD.NaI.pa, E) * RN : 0;
      al[k] = ip(XSD.Al.tot, E) * RA; wa[k] = ip(XSD.H2O.tot, E);
    }
    const MU = { ph: 0, co: 0, pa: 0, tot: 0 };
    function mu(E) { let u = (Math.log(E) - L0) / dL; let k = Math.floor(u); if (k < 0) { k = 0; u = 0; } if (k > N - 2) { k = N - 2; u = N - 1; } const f = u - k; MU.ph = ph[k] + (ph[k + 1] - ph[k]) * f; MU.co = co[k] + (co[k + 1] - co[k]) * f; MU.pa = pa[k] + (pa[k + 1] - pa[k]) * f; MU.tot = MU.ph + MU.co + MU.pa; return MU; }
    function at(arr, E) { let u = (Math.log(E) - L0) / dL; let k = clamp(Math.floor(u), 0, N - 2); const f = clamp(u - k, 0, 1); return arr[k] + (arr[k + 1] - arr[k]) * f; }
    return { mu, al: E => at(al, E), water: E => at(wa, E) };
  })();

  /* ============================== ПЕРЕНОС ФОТОНОВ ============================== */
  const TWO_PI = Math.PI * 2;
  function isoDir() { const c = 2 * Math.random() - 1, s = Math.sqrt(1 - c * c), f = TWO_PI * Math.random(); return [s * Math.cos(f), s * Math.sin(f), c]; }
  function rotate(u, cost) {
    const sint = Math.sqrt(Math.max(0, 1 - cost * cost)), f = TWO_PI * Math.random(), cp = Math.cos(f), sp = Math.sin(f);
    const ux = u[0], uy = u[1], uz = u[2];
    if (Math.abs(uz) > 0.99999) return [sint * cp, sint * sp, cost * Math.sign(uz)];
    const q = Math.sqrt(1 - uz * uz);
    return [sint * (ux * uz * cp - uy * sp) / q + ux * cost, sint * (uy * uz * cp + ux * sp) / q + uy * cost, -sint * q * cp + uz * cost];
  }
  function sampleKN(E) {                     // Клейн — Нишина, метод Батчера — Мессела
    const k = E / 511, e0 = 1 / (1 + 2 * k), e02 = e0 * e0, a1 = -Math.log(e0), a2 = a1 + 0.5 * (1 - e02);
    let eps, eps2, oc, g;
    do {
      if (a1 / a2 > Math.random()) { eps = Math.exp(-a1 * Math.random()); eps2 = eps * eps; }
      else { eps2 = e02 + (1 - e02) * Math.random(); eps = Math.sqrt(eps2); }
      oc = (1 - eps) / (eps * k); const s2 = oc * (2 - oc); g = 1 - eps * s2 / (1 + eps2);
    } while (g < Math.random());
    return [E * eps, 1 - oc];
  }
  function rayCyl(p, d, R, z0, z1) {         // отрезок луча внутри цилиндра [tin, tout] или null
    let tin = -Infinity, tout = Infinity;
    const a = d[0] * d[0] + d[1] * d[1], c = p[0] * p[0] + p[1] * p[1] - R * R;
    if (a < 1e-14) { if (c > 0) return null; }
    else { const b = p[0] * d[0] + p[1] * d[1], D = b * b - a * c; if (D < 0) return null; const s = Math.sqrt(D); tin = (-b - s) / a; tout = (-b + s) / a; }
    if (Math.abs(d[2]) < 1e-14) { if (p[2] < z0 || p[2] > z1) return null; }
    else { let ta = (z0 - p[2]) / d[2], tb = (z1 - p[2]) / d[2]; if (ta > tb) { const q = ta; ta = tb; tb = q; } if (ta > tin) tin = ta; if (tb < tout) tout = tb; }
    if (tin > tout || tout <= 0) return null;
    return [tin, tout];
  }
  function segLen(h, a, b) { if (!h) return 0; const x = Math.max(h[0], a), y = Math.min(h[1], b); return y > x ? y - x : 0; }
  const add = (p, d, s) => [p[0] + d[0] * s, p[1] + d[1] * s, p[2] + d[2] * s];

  /* геометрия: size — диаметр и высота кристалла, см; gap — от пробы до торца, см; src — point | jar | mar */
  function dims(Gm) {
    const R = Gm.size / 2, H = Gm.size, D = { R, H, al: Gm.al === undefined ? 0.08 : Gm.al, rho: Gm.rho === undefined ? 1 : Gm.rho, shield: !!Gm.shield };
    if (Gm.src === 'point') D.point = [0, 0, -Math.max(Gm.gap, 0.05)];
    else if (Gm.src === 'jar') D.jar = { R: 3.6, z0: -Gm.gap - 5.0, z1: -Gm.gap };
    else { const Ri = R + 0.35, Ro = Ri + 2.5; D.mar = { Ri, Ro, z0: -2.15, zw: -0.15, z1: H * 0.9 }; }
    D.rb = Math.sqrt(R * R + H * H / 4) * 1.002;
    return D;
  }
  function emitPoint(D) {
    if (D.point) return D.point;
    if (D.jar) { const r = D.jar.R * Math.sqrt(Math.random()), f = TWO_PI * Math.random(); return [r * Math.cos(f), r * Math.sin(f), lerp(D.jar.z0, D.jar.z1, Math.random())]; }
    const m = D.mar;
    for (; ;) { const r = m.Ro * Math.sqrt(Math.random()), f = TWO_PI * Math.random(), z = lerp(m.z0, m.z1, Math.random()); if (!(r < m.Ri && z > m.zw)) return [r * Math.cos(f), r * Math.sin(f), z]; }
  }
  function coneDir(p, D) {                   // направление в конус на кристалл + вес (доля телесного угла)
    const v = [-p[0], -p[1], D.H / 2 - p[2]], dist = Math.hypot(v[0], v[1], v[2]);
    if (dist <= D.rb * 1.02) return [isoDir(), 1];
    const cm = Math.sqrt(1 - (D.rb / dist) * (D.rb / dist)), cost = 1 - Math.random() * (1 - cm);
    return [rotate([v[0] / dist, v[1] / dist, v[2] / dist], cost), (1 - cm) / 2];
  }
  function pathInSource(p, d, tc, D) {
    if (D.point) return 0;
    if (D.jar) return segLen(rayCyl(p, d, D.jar.R, D.jar.z0, D.jar.z1), 0, tc);
    const m = D.mar; return Math.max(0, segLen(rayCyl(p, d, m.Ro, m.z0, m.z1), 0, tc) - segLen(rayCyl(p, d, m.Ri, m.zw, m.z1), 0, tc));
  }
  function attenuation(E, p, d, tin, entry, D) {
    let a = 0; const Ls = pathInSource(p, d, tin, D); if (Ls > 0) a += XS.water(E) * D.rho * Ls;
    const side = entry[2] > 1e-6 && entry[2] < D.H - 1e-6, c = side ? Math.sqrt(d[0] * d[0] + d[1] * d[1]) : Math.abs(d[2]);
    return a + XS.al(E) * D.al / Math.max(c, 0.15);
  }
  /* перенос внутри кристалла: возвращает выделенную энергию; track — для рисования */
  function transport(E0, p0, d0, D, track) {
    let edep = 0; const st = [[E0, p0, d0]];
    while (st.length) {
      let [E, p, d] = st.pop();
      for (let k = 0; k < 80; k++) {
        if (E < 9) { edep += E; break; }
        const h = rayCyl(p, d, D.R, 0, D.H), tex = h ? Math.max(h[1], 0) : 0;
        const M = XS.mu(E), s = -Math.log(1 - Math.random()) / M.tot;
        if (s >= tex) { if (track) track.push({ k: 'out', a: p, b: add(p, d, tex), c: add(p, d, tex + 3.5), E }); break; }
        const q = add(p, d, s); if (track) track.push({ k: 'seg', a: p, b: q, E });
        const u = Math.random() * M.tot;
        if (u < M.ph) {
          edep += E; if (track) track.push({ k: 'ph', p: q, E });
          if (E > 33.17 && Math.random() < 0.74) { edep -= 28.6; st.push([28.6, q, isoDir()]); }
          break;
        } else if (u < M.ph + M.co) {
          const kn = sampleKN(E); edep += E - kn[0]; if (track) track.push({ k: 'co', p: q, E });
          d = rotate(d, kn[1]); E = kn[0]; p = q;
        } else {
          edep += E - 1022; if (track) track.push({ k: 'pa', p: q, E });
          const dd = isoDir(); st.push([511, q, dd]); st.push([511, q, [-dd[0], -dd[1], -dd[2]]]);
          break;
        }
      }
    }
    return edep;
  }
  /* свинцовый домик: часть пролетевших мимо фотонов отражается назад или выбивает рентген свинца */
  function shieldReturn(E, D, wMiss, score, track) {
    const pB = 0.12, pX = E > 88.1 ? 0.07 * Math.min(1, Math.pow(300 / E, 1.1)) : 0, u = Math.random();
    let E2;
    if (u < pB) { const c = -1 + 0.7 * Math.random(); E2 = E / (1 + E / 511 * (1 - c)); }
    else if (u < pB + pX) E2 = Math.random() < 0.8 ? 74.2 : 84.9;
    else return;
    const a = TWO_PI * Math.random(), Rw = D.R + 6.5, p = [Rw * Math.cos(a), Rw * Math.sin(a), lerp(-6, D.H + 2.5, Math.random())];
    const cd = coneDir(p, D);
    if (track && Math.random() > cd[1] * 4) return;
    const h = rayCyl(p, cd[0], D.R, 0, D.H); if (!h) return;
    const entry = add(p, cd[0], Math.max(0, h[0]));
    if (track) track.push({ k: 'bs', a: p, b: entry, E: E2 });
    const ed = transport(E2, entry, cd[0], D, track);
    score(wMiss * (track ? 1 : cd[1]), ed, 'shield');
  }
  /* одна история с весами (для кривых и матриц) */
  function historyW(E, D, S, score) {
    const p = emitPoint(D), cd = coneDir(p, D), d = cd[0], wc = cd[1];
    if (D.shield && wc < 1) shieldReturn(E, D, 1 - wc, score, null);
    const h = rayCyl(p, d, D.R, 0, D.H);
    if (!h) { if (D.shield) shieldReturn(E, D, wc, score, null); return; }
    S.hit += wc;
    const tin = Math.max(0, h[0]), entry = add(p, d, tin), w = wc * Math.exp(-attenuation(E, p, d, tin, entry, D));
    S.arr += w;
    score(w, transport(E, entry, d, D, null), 'main');
  }
  function runW(E, D, N, raw, bw, S) {
    S = S || { n: 0, hit: 0, arr: 0, inter: 0, fep: 0 };
    const score = (w, ed, kind) => {
      if (ed > 0.5) { if (kind === 'main') S.inter += w; if (raw) { const b = Math.floor(ed / bw); if (b < raw.length) raw[b] += w; } }
      if (kind === 'main' && ed >= E - 0.6) S.fep += w;
    };
    for (let i = 0; i < N; i++) historyW(E, D, S, score);
    S.n += N; return S;
  }
  /* одна аналоговая история с треком (для анимаций) */
  function historyA(E, D, track) {
    const p = emitPoint(D), d = isoDir(); track.push({ k: 'src', p });
    const h = rayCyl(p, d, D.R, 0, D.H);
    if (!h) { track.push({ k: 'miss', a: p, b: add(p, d, 13), E }); if (D.shield) { let r = 0; shieldReturn(E, D, 1, (w, ed) => { r = ed; }, track); return { ed: r, hit: false, bs: r > 0 }; } return { ed: 0, hit: false }; }
    const tin = Math.max(0, h[0]), entry = add(p, d, tin);
    track.push({ k: 'seg', a: p, b: entry, E });
    if (Math.random() > Math.exp(-attenuation(E, p, d, tin, entry, D))) { track.push({ k: 'abs', p: entry, E }); return { ed: 0, hit: true }; }
    return { ed: transport(E, entry, d, D, track), hit: true };
  }
  function sigmaAt(E, f) { return f / 100 * 662 * Math.sqrt(Math.max(E, 10) / 662) / 2.3548; }
  function smear(ed, f) { return ed + sigmaAt(ed, f) * gauss(); }
  function blur(raw, bw, f) {
    const n = raw.length, out = new Float64Array(n);
    for (let i = 0; i < n; i++) {
      const v = raw[i]; if (!v) continue;
      const s = Math.max(sigmaAt((i + 0.5) * bw, f), 0.45 * bw), r = Math.ceil(3.5 * s / bw), lo = Math.max(0, i - r), hi = Math.min(n - 1, i + r);
      let nm = 0; for (let j = lo; j <= hi; j++) { const x = (j - i) * bw / s; nm += Math.exp(-0.5 * x * x); }
      for (let j = lo; j <= hi; j++) { const x = (j - i) * bw / s; out[j] += v * Math.exp(-0.5 * x * x) / nm; }
    }
    return out;
  }
  function features(E, shield) {             // где на спектре искать особенности
    const k = E / 511, out = [{ e: E, k: 'qn.ann.fep', c: '#ffcc33' }];
    if (E > 120) out.push({ e: E * 2 * k / (1 + 2 * k), k: 'qn.ann.ce', c: '#ffd166' });
    if (shield && E > 150) out.push({ e: E / (1 + 2 * k), k: 'qn.ann.bs', c: '#CC55BB' });
    if (shield && E > 100) out.push({ e: 74.2, k: 'qn.ann.pb', c: '#FF9900' });
    if (E > 1300) { out.push({ e: E - 511, k: 'qn.ann.se', c: '#4dd0e1' }); out.push({ e: E - 1022, k: 'qn.ann.de', c: '#4dd0e1' }); }
    if (E > 34 && E < 160) out.push({ e: E - 28.6, k: 'qn.ann.ie', c: '#4dd0e1' });
    return out;
  }
  function drawFeatures(ctx, box, X, list) {
    ctx.save(); ctx.font = '11px system-ui,sans-serif'; ctx.textBaseline = 'top';
    let lastX = -1e9, row = 0;
    list.slice().sort((a, b) => b.e - a.e).forEach(f => {
      const x = X(f.e); if (x < box.l + 2 || x > box.r - 2) return;
      ctx.strokeStyle = rgba(f.c.length === 7 ? f.c : '#ffcc33', .55); ctx.setLineDash([4, 4]); ctx.beginPath(); ctx.moveTo(x, box.t + 16); ctx.lineTo(x, box.b); ctx.stroke(); ctx.setLineDash([]);
      const label = t(f.k), w = ctx.measureText(label).width;
      row = (lastX - x < w + 10) ? row + 1 : 0; lastX = x;
      ctx.fillStyle = f.c; ctx.textAlign = x - w - 4 < box.l ? 'left' : 'right';
      ctx.fillText(label, ctx.textAlign === 'left' ? x + 4 : x - 4, box.t + 2 + row * 13);
    });
    ctx.restore();
  }

  /* ============================== СЦЕНА СБОКУ ============================== */
  function view(w, h, z0, z1, x0, x1, pad = 10) {
    const s = Math.min((w - 2 * pad) / (z1 - z0), (h - 2 * pad) / (x1 - x0));
    const ox = (w - (z1 - z0) * s) / 2 - z0 * s, oy = (h - (x1 - x0) * s) / 2 + x1 * s;
    return { s, X: z => ox + z * s, Y: x => oy - x * s };
  }
  function drawSetup(ctx, V, D, o = {}) {
    const { X, Y, s } = V;
    if (D.shield) {
      const Rw = D.R + 6.5, za = Math.min(-7, (D.point ? D.point[2] : D.jar ? D.jar.z0 : D.mar.z0) - 1.5), zb = D.H * 1.9 + 2;
      ctx.fillStyle = '#2a2f3a';
      [[Rw, Rw + 1.6], [-Rw - 1.6, -Rw]].forEach(([a, b]) => { ctx.fillRect(X(za), Y(b), (zb - za) * s, (b - a) * s); });
      ctx.fillRect(X(za - 1.6), Y(Rw + 1.6), 1.6 * s, (2 * Rw + 3.2) * s);
      ctx.fillStyle = '#7f8899'; ctx.font = '11px system-ui'; ctx.textAlign = 'left'; ctx.fillText('Pb', X(za) + 4, Y(Rw) - 3);
    }
    // проба
    if (D.point) {
      const p = D.point, g = ctx.createRadialGradient(X(p[2]), Y(0), 0, X(p[2]), Y(0), 14);
      g.addColorStop(0, 'rgba(255,230,120,1)'); g.addColorStop(.35, 'rgba(255,170,60,.55)'); g.addColorStop(1, 'rgba(255,120,40,0)');
      ctx.fillStyle = g; ctx.beginPath(); ctx.arc(X(p[2]), Y(0), 14, 0, TWO_PI); ctx.fill();
    } else if (D.jar) {
      const j = D.jar; ctx.fillStyle = 'rgba(80,170,255,.18)'; ctx.strokeStyle = 'rgba(120,190,255,.7)'; ctx.lineWidth = 1.5;
      ctx.fillRect(X(j.z0), Y(j.R), (j.z1 - j.z0) * s, 2 * j.R * s); ctx.strokeRect(X(j.z0) - 2, Y(j.R) - 2, (j.z1 - j.z0) * s + 2, 2 * j.R * s + 4);
    } else if (D.mar) {
      const m = D.mar; ctx.fillStyle = 'rgba(80,170,255,.18)'; ctx.strokeStyle = 'rgba(120,190,255,.7)'; ctx.lineWidth = 1.5;
      ctx.beginPath(); ctx.moveTo(X(m.z1), Y(m.Ro)); ctx.lineTo(X(m.z0), Y(m.Ro)); ctx.lineTo(X(m.z0), Y(-m.Ro)); ctx.lineTo(X(m.z1), Y(-m.Ro)); ctx.lineTo(X(m.z1), Y(-m.Ri)); ctx.lineTo(X(m.zw), Y(-m.Ri)); ctx.lineTo(X(m.zw), Y(m.Ri)); ctx.lineTo(X(m.z1), Y(m.Ri)); ctx.closePath(); ctx.fill(); ctx.stroke();
    }
    // ФЭУ
    const pz0 = D.H + D.al, pz1 = D.H * 1.9 + 0.5, pr = D.R * 0.92;
    const gp = ctx.createLinearGradient(X(pz0), 0, X(pz1), 0); gp.addColorStop(0, '#3b4560'); gp.addColorStop(1, '#1d2232');
    ctx.fillStyle = gp; ctx.fillRect(X(pz0), Y(pr), (pz1 - pz0) * s, 2 * pr * s);
    if (o.labels) { ctx.fillStyle = '#9aa7c4'; ctx.font = '11px system-ui'; ctx.textAlign = 'center'; ctx.fillText(t('qn.pmt'), X((pz0 + pz1) / 2), Y(0) + 4); }
    // корпус и кристалл
    ctx.fillStyle = '#5b6474'; ctx.fillRect(X(-D.al), Y(D.R + D.al), (D.H + 2 * D.al) * s, 2 * (D.R + D.al) * s);
    const gc = ctx.createLinearGradient(0, Y(D.R), 0, Y(-D.R)); gc.addColorStop(0, 'rgba(150,200,255,.38)'); gc.addColorStop(.5, 'rgba(110,160,255,.22)'); gc.addColorStop(1, 'rgba(150,200,255,.38)');
    ctx.fillStyle = '#0b1222'; ctx.fillRect(X(0), Y(D.R), D.H * s, 2 * D.R * s);
    ctx.fillStyle = gc; ctx.fillRect(X(0), Y(D.R), D.H * s, 2 * D.R * s);
    if (o.labels) { ctx.fillStyle = '#d7e6ff'; ctx.font = '600 12px system-ui'; ctx.textAlign = 'center'; ctx.fillText('NaI(Tl)', X(D.H / 2), Y(D.R) + 16); }
  }
  /* свечение точки взаимодействия: радиальный градиент рисуется один раз в маленький холст на цвет и масштаб,
     дальше — drawImage с прозрачностью; раньше градиент строился заново у каждой точки в каждом кадре (`AMBER254`) */
  const glowCache = new Map();
  function glowSprite(col, d) {
    const key = col + '|' + d; let c = glowCache.get(key);
    if (!c) {
      const R = 13, n = Math.ceil(2 * R * d); c = document.createElement('canvas'); c.width = c.height = n;
      const g = c.getContext('2d'); g.scale(n / (2 * R), n / (2 * R));
      const gr = g.createRadialGradient(R, R, 0, R, R, R); gr.addColorStop(0, rgba(col, .8)); gr.addColorStop(1, rgba(col, 0));
      g.fillStyle = gr; g.beginPath(); g.arc(R, R, R, 0, TWO_PI); g.fill(); glowCache.set(key, c);
    }
    return c;
  }
  const PT_COL = { ph: '#ff4d6d', co: '#ffd166', pa: '#c38bff' };
  function drawTrack(ctx, V, tr, alpha, prog) {    // prog: 0..1 — какая часть трека уже пролетела
    const { X, Y } = V;
    if (tr.L === undefined) {   // трек после рождения не меняется: длины и цвета отрезков — один раз, а не в каждом кадре (`AMBER254`)
      let L = 0;
      for (const e of tr) if (e.a) { e.len = Math.hypot(e.b[0] - e.a[0], e.b[2] - e.a[2]) + (e.c ? 3.5 : 0) + (e.k === 'miss' ? 0 : 0.01); L += e.len; e.col = e.k === 'bs' ? '#CC55BB' : eCol(e.E, e.k === 'miss' ? 0.22 : 0.95); }
      tr.L = L;
    }
    let lim = prog === undefined ? Infinity : prog * tr.L, acc = 0;
    const d = ctx.canvas._d || ctx.getTransform().a, lite = G.lite();   // масштаб холста запоминает setup()
    // без ctx.save(): у сотни треков в кадре он стоил заметно; прозрачность — общей альфой, цвета — готовыми строками
    const a0 = ctx.globalAlpha, lc0 = ctx.lineCap, lw0 = ctx.lineWidth;
    ctx.lineCap = 'round'; ctx.globalAlpha = a0 * alpha;
    for (const e of tr) {
      if (e.a) {
        if (acc >= lim) break;
        const f = clamp((lim - acc) / e.len, 0, 1); acc += e.len;
        ctx.strokeStyle = e.col; ctx.lineWidth = e.k === 'miss' ? 1 : 1.8;
        const b = e.k === 'out' ? e.c : e.b, bx = lerp(e.a[2], b[2], f), by = lerp(e.a[0], b[0], f);
        ctx.beginPath(); ctx.moveTo(X(e.a[2]), Y(e.a[0])); ctx.lineTo(X(bx), Y(by)); ctx.stroke();
        if (e.k === 'out' && f > 0.98) { ctx.fillStyle = '#4dd0e1'; ctx.beginPath(); ctx.arc(X(e.c[2]), Y(e.c[0]), 2.4, 0, TWO_PI); ctx.fill(); }
      } else if (e.p && e.k !== 'src') {
        if (acc > lim) break;
        const col = PT_COL[e.k] || '#8892a8', r = e.k === 'ph' ? 4.5 : e.k === 'pa' ? 4.5 : 3, px = X(e.p[2]), py = Y(e.p[0]);
        if ((e.k === 'ph' || e.k === 'pa') && !lite) ctx.drawImage(glowSprite(col, d), px - 13, py - 13, 26, 26);
        ctx.fillStyle = col; ctx.beginPath(); ctx.arc(px, py, r, 0, TWO_PI); ctx.fill();
        if (e.k === 'abs') { ctx.strokeStyle = '#8892a8'; ctx.lineWidth = 1.5; ctx.beginPath(); ctx.moveTo(px - 4, py - 4); ctx.lineTo(px + 4, py + 4); ctx.moveTo(px + 4, py - 4); ctx.lineTo(px - 4, py + 4); ctx.stroke(); }
      }
    }
    ctx.globalAlpha = a0; ctx.lineCap = lc0; ctx.lineWidth = lw0;
  }

  /* ============================== ЗАСТАВКА: банка → кристалл → спектр (вид сбоку; 3D снято — `AMBER247`) ============================== */
  function heroAnim(cv, statEl) {
    io.observe(cv);
    const D = dims({ size: 6.3, gap: 0.6, src: 'jar', rho: 1.0, shield: false, al: 0.08 });
    const LINES = [[59.54, 0.36 * 0.6], [661.66, 0.851], [1173.2, 0.45], [1332.5, 0.45], [1460.8, 0.4]];
    const wsum = LINES.reduce((a, l) => a + l[1], 0);
    const pick = () => { let u = Math.random() * wsum; for (const l of LINES) { u -= l[1]; if (u <= 0) return l[0]; } return LINES[1][0]; };
    const NB = 170, EMAX = 1600, hist = new Float64Array(NB); let tracks = [], nEm = 0, nHit = 0, nSp = 0;
    const BC = Array.from({ length: NB }, (_, b) => eCol((b + 0.5) * EMAX / NB, 0.9));   // цвет столбца от кадра не зависит (`AMBER254`)
    let odd = false, fr = 0, statTxt = '';
    function frame() {
      requestAnimationFrame(frame);
      if (!visible.get(cv)) return;
      if (G.lite() && (odd = !odd)) return;          // облегчённый режим: каждый второй кадр
      for (let i = 0; i < (G.lite() ? 1 : 2); i++) { const tr = []; const r = historyA(pick(), D, tr); nEm++; if (r.hit) nHit++; if (r.ed > 15) { const e = smear(r.ed, 7.5), b = Math.floor(e / EMAX * NB); if (b >= 0 && b < NB) hist[b]++; nSp++; } tracks.push({ tr, age: 0 }); }
      const S = { n: 0, hit: 0, arr: 0, inter: 0, fep: 0 };
      for (let i = 0; i < 36; i++) { const E = pick(); historyW(E, D, S, (w, ed) => { if (ed > 15 && Math.random() < w * 3.6) { const e = smear(ed, 7.5), b = Math.floor(e / EMAX * NB); if (b >= 0 && b < NB) hist[b]++; nSp++; } }); }
      nEm += 36; nHit += Math.round(S.hit * 3.6);
      if (nSp > 160000) { for (let b = 0; b < NB; b++) hist[b] *= 0.5; nSp *= 0.5; }
      draw();
      tracks.forEach(x => x.age++); tracks = tracks.filter(x => x.age < 46);
    }
    function draw() {
      const { ctx, w, h } = setup(cv);
      const narrow = w < 760, sw = narrow ? w : w * 0.56, sh = narrow ? h * 0.52 : h;
      const V = view(sw, sh, -6.6, D.H * 1.9 + 1.2, -6.2, 6.2, 8);
      ctx.save(); ctx.beginPath(); ctx.rect(0, 0, sw, sh); ctx.clip();
      drawSetup(ctx, V, D, { labels: true });
      tracks.forEach(x => drawTrack(ctx, V, x.tr, Math.pow(1 - x.age / 46, 1.4), Math.min(1, (x.age + 1) / 14)));
      ctx.restore();
      const bx = narrow ? { l: 44, t: sh + 12, r: w - 12, b: h - 22 } : { l: sw + 50, t: 18, r: w - 14, b: h - 24 };
      let mx = 1; for (let b = 3; b < NB; b++) mx = Math.max(mx, hist[b]);
      const X = scale('lin', 0, EMAX, bx.l, bx.r), Y = scale('sqrt', 0, mx * 1.08, bx.b, bx.t);
      axes(ctx, bx, X, Y, { xl: t('qn.ax.e'), nx: 4, noY: true });
      const bw = (bx.r - bx.l) / NB;
      for (let b = 1; b < NB; b++) { const y = Y(hist[b]); ctx.fillStyle = BC[b]; ctx.fillRect(bx.l + b * bw, y, Math.max(1, bw - 0.6), bx.b - y); }
      ctx.fillStyle = '#cdd8f3'; ctx.font = '600 12px system-ui'; ctx.textAlign = 'left'; ctx.fillText(t('qn.spec'), bx.l + 4, bx.t + 4);
      // счётчики — раз в 6 кадров: запись в DOM в каждом кадре заставляла браузер каждый кадр пересчитывать вёрстку
      if (statEl && (++fr % 6 === 1)) { const s = t('qn.stat', nEm, nHit, Math.round(nSp)); if (s !== statTxt) statEl.innerHTML = statTxt = s; }
    }
    requestAnimationFrame(frame);
  }

  /* ============================== МОНТЕ-КАРЛО-СЦЕНА: 1–2 геометрии рядом, вид сбоку (`AMBER246`; 3D снято — `AMBER247`) ==============================
     o.scenes: [{ geo: {size, gap, src}, label: () => текст }]; o.lines: [[E, выход на распад], …];
     o.layer: цвет — спектр рисуется слоем (FSA), иначе столбиками по энергии;
     o.analog: сколько аналоговых распадов на кадр считать для «воронки» (o.onStats получает s.A);
     o.onStats(i, s) — раз в 8 кадров: s.S (взвешенные истории: n, hit, fep), s.A (аналог: dec, ph, hit, pk). */
  function mcDuo(cv, o) {
    io.observe(cv);
    const NB = 200;
    let lines = o.lines, sc = [], fr = 0, bcEM = 0, BC = [];
    const emax = () => o.emax || Math.max(...lines.map(l => l[0])) * 1.15;
    const pick = () => { const ws = lines.reduce((a, l) => a + l[1], 0); let u = Math.random() * ws; for (const l of lines) { u -= l[1]; if (u <= 0) return l[0]; } return lines[0][0]; };
    function reset() {
      sc = o.scenes.map(s => ({ label: s.label, D: dims(Object.assign({ rho: 1, al: 0.08 }, s.geo)), hist: new Float64Array(NB), tracks: [], S: { n: 0, hit: 0, arr: 0, inter: 0, fep: 0 }, A: { dec: 0, ph: 0, hit: 0, pk: 0 } }));
    }
    let odd = false;
    function frame() {
      requestAnimationFrame(frame);
      if (!visible.get(cv)) return;
      if (G.lite() && (odd = !odd)) return;          // облегчённый режим: каждый второй кадр
      const EM = emax(); fr++;
      sc.forEach((s, i) => {
        for (let k = 0; k < (Math.random() < 0.5 ? 2 : 1); k++) { const tr = []; historyA(pick(), s.D, tr); s.tracks.push({ tr, age: 0 }); }
        for (let k = 0; k < 32; k++) {
          const E = pick();
          historyW(E, s.D, s.S, (w, ed, kind) => {
            if (ed > 8) { const e = smear(ed, o.fwhm || 7.5), b = Math.floor(e / EM * NB); if (b >= 0 && b < NB) s.hist[b] += w; }
            if (kind === 'main' && ed >= E - 0.6) s.S.fep += w;
          });
        }
        s.S.n += 32;
        for (let k = 0; k < (o.analog || 0); k++) {   // аналоговые распады: выход линии — вероятность фотона на распад
          s.A.dec++;
          for (const [E, y] of lines) if (Math.random() < y) { s.A.ph++; const r = historyA(E, s.D, []); if (r.hit) s.A.hit++; if (r.ed >= E - 0.6) s.A.pk++; }
        }
        s.tracks.forEach(x => x.age++); s.tracks = s.tracks.filter(x => x.age < 42);
        if (o.onStats && fr % 8 === 0) o.onStats(i, s);
      });
      draw();
    }
    function draw() {
      const n = sc.length, vert = n > 1 && cv.getBoundingClientRect().width < 600;   // узкий экран: сцены друг под другом
      const want = vert ? '640px' : ''; if (cv.style.height !== want) cv.style.height = want;
      const { ctx, w, h } = setup(cv), EM = emax();
      const box = i => vert ? { x0: 0, y0: i * h / n, cw: w, rh: h / n } : { x0: i * w / n, y0: 0, cw: w / n, rh: h };
      let mx = 1e-12, zMin = Infinity, zMax = -Infinity, xExt = 0;
      sc.forEach(s => { if (s.S.n) for (let b = 2; b < NB; b++) mx = Math.max(mx, s.hist[b] / s.S.n); });
      sc.forEach(s => {                       // общие границы вида сбоку: у сцен «рядом» один масштаб, размеры сравнимы
        const D = s.D; zMin = Math.min(zMin, D.point ? D.point[2] - 2 : D.jar ? D.jar.z0 - 1.2 : D.mar.z0 - 1.2); zMax = Math.max(zMax, D.H * 1.9 + 1.2);
        xExt = Math.max(xExt, D.R + 1.5, D.jar ? D.jar.R + 1.2 : 0, D.mar ? D.mar.Ro + 1.2 : 0);
      });
      sc.forEach((s, i) => {
        const B = box(i), x0 = B.x0, y0 = B.y0, cw = B.cw, sh = B.rh * 0.6, D = s.D;
        const V0 = view(cw, sh, zMin, zMax, -xExt, xExt, 8), V = { s: V0.s, X: z => x0 + V0.X(z), Y: x => y0 + V0.Y(x) };
        ctx.save(); ctx.beginPath(); ctx.rect(x0, y0, cw, sh); ctx.clip();
        drawSetup(ctx, V, D, { labels: true });
        s.tracks.forEach(x => drawTrack(ctx, V, x.tr, Math.pow(1 - x.age / 42, 1.4), Math.min(1, (x.age + 1) / 12)));
        ctx.fillStyle = '#e8eefc'; ctx.font = '700 13px system-ui'; ctx.textAlign = 'left'; ctx.fillText(s.label(), x0 + 10, y0 + 18);
        ctx.restore();
        const bx = { l: x0 + (i && !vert ? 14 : 40), t: y0 + sh + 10, r: x0 + cw - 10, b: y0 + B.rh - 22 };
        const X = scale('lin', 0, EM, bx.l, bx.r), Y = scale('sqrt', 0, mx * 1.1, bx.b, bx.t);
        axes(ctx, bx, X, Y, { xl: vert || i === n - 1 ? t('qn.ax.e') : '', nx: 4, noY: true });
        const bw = (bx.r - bx.l) / NB;
        if (o.layer) {
          ctx.beginPath(); ctx.moveTo(bx.l, bx.b);
          for (let b = 0; b < NB; b++) ctx.lineTo(bx.l + (b + 0.5) * bw, Y(s.S.n ? s.hist[b] / s.S.n : 0));
          ctx.lineTo(bx.r, bx.b); ctx.closePath(); ctx.fillStyle = rgba(o.layer(), .55); ctx.fill(); ctx.strokeStyle = o.layer(); ctx.lineWidth = 1.5; ctx.stroke();
          lines.forEach(([E]) => { const x = X(E); ctx.strokeStyle = 'rgba(255,255,255,.18)'; ctx.setLineDash([3, 4]); ctx.beginPath(); ctx.moveTo(x, bx.t); ctx.lineTo(x, bx.b); ctx.stroke(); ctx.setLineDash([]); });
        } else {
          if (EM !== bcEM) { bcEM = EM; BC = Array.from({ length: NB }, (_, b) => eCol((b + 0.5) * EM / NB, .9)); }   // цвета столбцов — при смене шкалы, не в каждом кадре
          for (let b = 1; b < NB; b++) { if (!s.hist[b]) continue; const y = Y(s.hist[b] / s.S.n); ctx.fillStyle = BC[b]; ctx.fillRect(bx.l + b * bw, y, Math.max(1, bw - 0.4), bx.b - y); }
        }
        if (n > 1 && i) { ctx.strokeStyle = 'rgba(255,255,255,.08)'; ctx.beginPath(); if (vert) { ctx.moveTo(8, y0); ctx.lineTo(w - 8, y0); } else { ctx.moveTo(x0, 8); ctx.lineTo(x0, h - 8); } ctx.stroke(); }
      });
    }
    reset(); requestAnimationFrame(frame);
    return { reset, setLines(l) { lines = l; reset(); }, get scenes() { return sc; } };
  }

  /* ============================== «ФОТОН В КРИСТАЛЛЕ» ==============================
     Разметка — та же, что в how-it-works.html: #cvPh, #cvPhS, #phChips, #phE, #phEv, #phShield,
     #ph1, #ph100, #ph10k, #phClr, #phLegend, #phStat */
  function photonLab() {
    const GEO = { size: 6.3, gap: 1.0, src: 'jar', rho: 1.0, shield: false, fwhm: 7.5, al: 0.08 };
    const PH = { E: 662, n: 0, inter: 0, fep: 0, hist: null, nb: 220, emax: 800, tracks: [] };
    const cv = $('#cvPh'), cvS = $('#cvPhS'); io.observe(cv); io.observe(cvS);
    const chips = [[59.5, 'Am-241'], [661.7, 'Cs-137'], [1460.8, 'K-40'], [2614.5, 'Tl-208']];
    const box = $('#phChips');
    chips.forEach(([e, n]) => { const b = document.createElement('button'); b.className = 'chip'; b.textContent = n + ' · ' + Math.round(e); b.onclick = () => setE(e); box.appendChild(b); });
    const sl = $('#phE');
    const pos2E = p => 30 * Math.pow(3000 / 30, p / 1000), E2pos = e => Math.round(1000 * Math.log(e / 30) / Math.log(100));
    function setE(e) { PH.E = e; sl.value = E2pos(e); reset(); }
    function reset() { PH.n = PH.inter = PH.fep = 0; PH.emax = Math.max(120, PH.E * 1.13); PH.hist = new Float64Array(PH.nb); PH.tracks = []; refresh(); }
    sl.addEventListener('input', () => { PH.E = Math.round(pos2E(+sl.value)); reset(); });
    const sh = $('#phShield'); sh.checked = GEO.shield;
    sh.addEventListener('change', () => { GEO.shield = sh.checked; reset(); });
    function fire(n, keepTracks) {
      const D = dims(GEO);
      for (let i = 0; i < n; i++) {
        const tr = keepTracks ? [] : null;
        let r;
        if (tr) r = historyA(PH.E, D, tr);
        else { const t2 = []; r = historyA(PH.E, D, t2); if (i >= n - 6) PH.tracks.push({ tr: t2, age: 20 }); }
        PH.n++; if (r.ed > 1) { PH.inter++; const e = smear(r.ed, GEO.fwhm), b = Math.floor(e / PH.emax * PH.nb); if (b >= 0 && b < PH.nb) PH.hist[b]++; if (r.ed >= PH.E - 0.6) PH.fep++; }
        if (tr) PH.tracks.push({ tr, age: 0, slow: n === 1 });
      }
    }
    $('#ph1').onclick = () => { fire(1, true); };
    $('#ph100').onclick = () => { let k = 0; Jobs.add('ph100', (function* () { while (k < 100) { fire(3, true); k += 3; yield; } })()); };
    $('#ph10k').onclick = () => { let k = 0; Jobs.add('ph10k', (function* () { while (k < 100000) { fire(4000, false); k += 4000; yield; } })()); };
    $('#phClr').onclick = reset;
    function legend() {
      $('#phLegend').innerHTML = [['#ff4d6d', 'qn.lg.ph'], ['#ffd166', 'qn.lg.co'], ['#c38bff', 'qn.lg.pa'], ['#4dd0e1', 'qn.lg.esc'], ['#8892a8', 'qn.lg.abs']].map(([c, k]) => '<span><i style="background:' + c + ';border-radius:50%"></i>' + t(k) + '</span>').join('');
      $$('#phChips .chip').forEach((b, i) => b.classList.toggle('on', Math.abs(chips[i][0] - PH.E) < 2));
      $('#phEv').textContent = Math.round(PH.E) + ' ' + t('qn.kev');
      $('#phStat').innerHTML = '<span class="pill">' + t('qn.st', PH.n, PH.inter, PH.fep) + '</span>';
    }
    function drawScene() {
      const { ctx, w, h } = setup(cv), D = dims(GEO);
      const zmin = D.point ? D.point[2] - 2.5 : D.jar ? D.jar.z0 - 1.5 : D.mar.z0 - 1.5, ext = Math.max(D.R, D.jar ? D.jar.R : D.mar ? D.mar.Ro : D.R) + (GEO.shield ? 8.6 : 2.5);
      const V = view(w, h, Math.min(zmin, GEO.shield ? -9 : zmin), D.H * 1.9 + 1.5, -ext, ext, 8);
      drawSetup(ctx, V, D, { labels: true });
      PH.tracks.forEach(x => drawTrack(ctx, V, x.tr, x.age < 60 ? 1 : Math.max(0, 1 - (x.age - 60) / 60), x.slow ? Math.min(1, x.age / 70) : Math.min(1, (x.age + 1) / 16)));
    }
    function drawSpec() {
      const { ctx, w, h } = setup(cvS), bx = { l: 46, t: 14, r: w - 12, b: h - 24 };
      let mx = 4; for (let b = 1; b < PH.nb; b++) mx = Math.max(mx, PH.hist[b]);
      const X = scale('lin', 0, PH.emax, bx.l, bx.r), Y = scale('sqrt', 0, mx * 1.1, bx.b, bx.t);
      axes(ctx, bx, X, Y, { xl: t('qn.ax.e'), yl: t('qn.ax.cnt') });
      drawFeatures(ctx, bx, X, features(PH.E, GEO.shield));
      const bw = (bx.r - bx.l) / PH.nb;
      for (let b = 0; b < PH.nb; b++) { if (!PH.hist[b]) continue; const y = Y(PH.hist[b]); ctx.fillStyle = eCol((b + 0.5) * PH.emax / PH.nb, .95); ctx.fillRect(bx.l + b * bw, y, Math.max(1, bw - 0.5), bx.b - y); }
    }
    function refresh() { legend(); drawScene(); drawSpec(); }
    let lastN = -1;
    function loop() {
      requestAnimationFrame(loop);
      if (!visible.get(cv) && !visible.get(cvS)) return;
      if (PH.tracks.length || PH.n !== lastN) {
        PH.tracks.forEach(x => x.age++); PH.tracks = PH.tracks.filter(x => x.age < 120);
        drawScene(); if (PH.n !== lastN) { drawSpec(); legend(); lastN = PH.n; }
      }
    }
    G.onRedraw(refresh);
    setE(661.7); requestAnimationFrame(loop);
    // самозапуск: страница сама «стреляет» по фотону, пока человек не нажал ни одной кнопки
    let touched = false; ['#ph1', '#ph100', '#ph10k', '#phClr', '#phE', '#phShield'].forEach(s => $(s).addEventListener('pointerdown', () => { touched = true; }));
    $$('#phChips .chip').forEach(b => b.addEventListener('pointerdown', () => { touched = true; }));
    setInterval(() => { if (!touched && visible.get(cv) && PH.n < 400) fire(1, true); }, 900);
  }

  G.Q = { mcDuo, XS, dims, emitPoint, historyA, historyW, runW, smear, blur, sigmaAt, features, drawFeatures, view, drawSetup, drawTrack, heroAnim, photonLab };
})(window.GUIDE);
