# -*- coding: utf-8 -*-
r"""П103 — правило «дальним точкам ×2» (решение Amber 18.09.2026, дословно: «Добить историями ночью: дальним
точкам ×2») у читателей рецепта склада: `MatrixStampProbe --hist=` (клеймо при умолчаниях с другим числом историй)
и `check_corpus_scenes.py` (реестр густых сцен `DENSE_SCENES`, приговор «на своей сцене и рецепте ×K»).
CRLF/BOM сохраняются."""
import os
WT = r'D:\BqMoni_Claude\p103\wt'


def patch(rel, reps):
    p = os.path.join(WT, rel)
    b = open(p, 'rb').read()
    bom = b[:3] == b'\xef\xbb\xbf'
    s = b.decode('utf-8-sig').replace('\r\n', '\n')
    for o, n in reps:
        assert s.count(o) == 1, (rel, o[:70], s.count(o))
        s = s.replace(o, n)
    nb = s.replace('\n', '\r\n').encode('utf-8')
    if bom:
        nb = b'\xef\xbb\xbf' + nb
    open(p, 'wb').write(nb)
    print('ok', rel, 'CRLF', nb.count(b'\r\n'), 'BOM', bom)


patch(r'tools\effmaker\probes\MatrixStampProbe.cs', [
(u"""///     matrixstampprobe --geometry=X.in [--matrix=X.rmx]
/// </summary>""",
 u"""///     matrixstampprobe --geometry=X.in [--matrix=X.rmx] [--hist=N|xK]
///
/// `--hist=N` (или `xK` — K умолчаний; П103 19.09.2026) печатает ЧЕТВЁРТОЕ
/// клеймо — при умолчаниях, но с N историями на узел: «клеймо при умолчаниях,
/// историй N: …». Нужно сторожу `check_corpus_scenes.py` для густых сцен склада
/// (решение Amber 18.09.2026 «дальним точкам ×2»): матрица дальней точки
/// посчитана штатным рецептом с удвоенным числом историй, и судить её надо
/// РАВЕНСТВОМ клейма с этим числом, а не «историй больше — значит гуще».
/// </summary>"""),
(u"""        string geometryPath = null, matrixPath = null;
        foreach (string a in args)
        {
            if (a.StartsWith("--geometry=", StringComparison.Ordinal))
            {
                geometryPath = a.Substring(11);
            }
            else if (a.StartsWith("--matrix=", StringComparison.Ordinal))
            {
                matrixPath = a.Substring(9);
            }""",
 u"""        string geometryPath = null, matrixPath = null, histArg = null;
        foreach (string a in args)
        {
            if (a.StartsWith("--geometry=", StringComparison.Ordinal))
            {
                geometryPath = a.Substring(11);
            }
            else if (a.StartsWith("--matrix=", StringComparison.Ordinal))
            {
                matrixPath = a.Substring(9);
            }
            else if (a.StartsWith("--hist=", StringComparison.Ordinal))
            {
                histArg = a.Substring(7);
            }"""),
(u"""        var plain = new ResponseMatrixOptions();
        string byDefaults = ResponseMatrix.ComputeStamp(geometry, plain);
        Console.WriteLine("клеймо при умолчаниях : {0}", byDefaults);
""",
 u"""        var plain = new ResponseMatrixOptions();
        string byDefaults = ResponseMatrix.ComputeStamp(geometry, plain);
        Console.WriteLine("клеймо при умолчаниях : {0}", byDefaults);

        if (histArg != null)
        {
            // Клеймо штатного рецепта с ДРУГИМ числом историй (правило «дальним
            // точкам ×2»): те же умолчания, только `Histories`. Разбор строгий,
            // инвариантной культурой (`A244`): `x2` — множитель умолчания, число —
            // как есть; иное — отказ кодом 2, а не «молча умолчание».
            int hist;
            if (histArg.StartsWith("x", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(histArg.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out hist) && hist > 0)
            {
                hist *= plain.Histories;
            }
            else if (!int.TryParse(histArg, NumberStyles.None, CultureInfo.InvariantCulture, out hist) || hist <= 0)
            {
                Console.Error.WriteLine("--hist= ждёт положительное целое или xK: " + histArg);
                return 2;
            }

            var dense = new ResponseMatrixOptions();
            dense.Histories = hist;
            Console.WriteLine("клеймо при умолчаниях, историй {0}: {1}",
                              hist.ToString(CultureInfo.InvariantCulture),
                              ResponseMatrix.ComputeStamp(geometry, dense));
        }
"""),
])

patch(r'tools\check_corpus_scenes.py', [
(u"""Сцена принята, когда G == L и (нет `.rmx` или M == L). Равенство именно
клеймом, а не байтом:""",
 u"""Сцена принята, когда G == L и (нет `.rmx` или M == L). ⚠ ГУСТЫЕ СЦЕНЫ
(`DENSE_SCENES`; решение Amber 18.09.2026, вопросником, дословно: «Добить
историями ночью: дальним точкам ×2», исполнено П103 19.09.2026 единым счётом
физики 20): у дальней точки малого кристалла (`RC103_point50`,
`ASN16_point10_house`, `G1S_point25` — шум худшего узла 11…12 % при 3 000 000
историй) матрица склада считается штатным рецептом с K × 3 000 000 историй, и
для неё принимается M == L_K, где L_K — клеймо живого `.in` при умолчаниях с
K × историй (`MatrixStampProbe --hist=xK`, четвёртая строка). Именно РАВЕНСТВО
клейма с названным числом, а не «историй больше»: гуще другим числом или иным
ключом — расхождение, как и прежде. Реестр густых сцен — здесь, одно место
(README корпуса, раздел рецепта склада, называет те же три); рычаг `--dense=`
подменяет реестр (пустой — выключить: положительный контроль П103 — три сцены
краснеют «ДРУГИМ РЕЦЕПТОМ»). Равенство именно клеймом, а не байтом:"""),
(u"""  --csv=<файл>     записать регистр клейм (сцена, живой, генератор, матрица, приговор).
""",
 u"""  --csv=<файл>     записать регистр клейм (сцена, живой, генератор, матрица, приговор);
  --dense=<сцена>:<K>[,…]  реестр густых сцен взамен `DENSE_SCENES` (пусто — ни одной:
                   тогда матрицы дальних точек ОБЯЗАНЫ краснеть «ДРУГИМ РЕЦЕПТОМ»).
"""),
(u"""RE_DEFAULT = re.compile(r'клеймо при умолчаниях\\s*:\\s*(phys=\\S+)')""",
 u"""RE_DEFAULT = re.compile(r'клеймо при умолчаниях\\s*:\\s*(phys=\\S+)')
RE_DENSE = re.compile(r'клеймо при умолчаниях, историй\\s+(\\d+)\\s*:\\s*(phys=\\S+)')

#: Густые сцены склада: ключ → множитель историй к умолчанию (решение Amber
#: 18.09.2026 «дальним точкам ×2»; П103). Матрица такой сцены годна, когда её
#: клеймо равно клейму сцены при умолчаниях с K × историй — см. шапку.
DENSE_SCENES = {
    u'RC103_point50': 2,
    u'ASN16_point10_house': 2,
    u'G1S_point25': 2,
}"""),
(u"""def stamps_of(probes, geometry, matrix):
    u\"\"\"Три клейма `MatrixStampProbe`: при умолчаниях, в файле `.rmx`, по настройкам
    файла; плюс строка сетки. Код возврата пробы НЕ читается (см. шапку).\"\"\"
    exe = os.path.join(probes, STAMPER)
    proc = subprocess.run([exe, u'--geometry=' + geometry, u'--matrix=' + matrix], cwd=probes,
                          stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    text = decode_console(proc.stdout)
    m1 = RE_DEFAULT.search(text)
    m2 = RE_INFILE.search(text)
    m3 = RE_BYOWN.search(text)
    mg = RE_GRID.search(text)
    return {
        u'default': m1.group(1) if m1 else None,
        u'infile': m2.group(1) if m2 else None,
        u'byown': m3.group(1) if m3 else None,
        u'grid': mg.groups() if mg else None,
        u'text': text,
        u'code': proc.returncode,
    }
""",
 u"""def stamps_of(probes, geometry, matrix, dense_k=None):
    u\"\"\"Три клейма `MatrixStampProbe`: при умолчаниях, в файле `.rmx`, по настройкам
    файла; плюс строка сетки; с `dense_k` — четвёртое, при умолчаниях с K × историй
    (`--hist=xK`, густые сцены). Код возврата пробы НЕ читается (см. шапку).\"\"\"
    exe = os.path.join(probes, STAMPER)
    cmd = [exe, u'--geometry=' + geometry, u'--matrix=' + matrix]
    if dense_k:
        cmd.append(u'--hist=x%d' % int(dense_k))
    proc = subprocess.run(cmd, cwd=probes, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    text = decode_console(proc.stdout)
    m1 = RE_DEFAULT.search(text)
    m2 = RE_INFILE.search(text)
    m3 = RE_BYOWN.search(text)
    mg = RE_GRID.search(text)
    md = RE_DENSE.search(text) if dense_k else None
    return {
        u'default': m1.group(1) if m1 else None,
        u'infile': m2.group(1) if m2 else None,
        u'byown': m3.group(1) if m3 else None,
        u'dense': md.group(2) if md else None,
        u'dense_hist': int(md.group(1)) if md else None,
        u'grid': mg.groups() if mg else None,
        u'text': text,
        u'code': proc.returncode,
    }
"""),
(u"""def judge_scene(key, live_dir, gen_dir, probes, none_rmx):
    u\"\"\"Приговор одной сцене: словарь с клеймами и списком расхождений (пусто = сошлось).\"\"\"
    live_in = os.path.join(live_dir, key + u'.in')
    gen_in = os.path.join(gen_dir, key + u'.in')
    rmx = os.path.join(live_dir, key + u'.rmx')
    has_rmx = os.path.isfile(rmx)
    row = {u'key': key, u'live': None, u'gen': None, u'rmx': None, u'has_rmx': has_rmx, u'bad': []}
    if not os.path.isfile(live_in):
        row[u'bad'].append(u'в корпусе нет этой сцены, а генератор её строит')
    else:
        s = stamps_of(probes, live_in, rmx if has_rmx else none_rmx)
        row[u'live'] = s[u'default']
        row[u'rmx'] = s[u'infile'] if has_rmx else None
        row[u'byown'] = s[u'byown']
        row[u'grid'] = s[u'grid']""",
 u"""def judge_scene(key, live_dir, gen_dir, probes, none_rmx, dense=None):
    u\"\"\"Приговор одной сцене: словарь с клеймами и списком расхождений (пусто = сошлось).
    `dense` — реестр густых сцен (ключ → K); у такой сцены матрица годна при M == L_K.\"\"\"
    live_in = os.path.join(live_dir, key + u'.in')
    gen_in = os.path.join(gen_dir, key + u'.in')
    rmx = os.path.join(live_dir, key + u'.rmx')
    has_rmx = os.path.isfile(rmx)
    dense = DENSE_SCENES if dense is None else dense
    k_dense = dense.get(key)
    row = {u'key': key, u'live': None, u'gen': None, u'rmx': None, u'has_rmx': has_rmx, u'bad': [],
           u'dense_k': k_dense, u'dense_ok': False}
    if not os.path.isfile(live_in):
        row[u'bad'].append(u'в корпусе нет этой сцены, а генератор её строит')
    else:
        s = stamps_of(probes, live_in, rmx if has_rmx else none_rmx, dense_k=k_dense)
        row[u'live'] = s[u'default']
        row[u'rmx'] = s[u'infile'] if has_rmx else None
        row[u'byown'] = s[u'byown']
        row[u'dense'] = s.get(u'dense')
        row[u'dense_hist'] = s.get(u'dense_hist')
        row[u'grid'] = s[u'grid']
        if k_dense and row[u'live'] and row[u'dense'] is None:
            row[u'bad'].append(u'густая сцена (×%d), а клейма с K × историй проба не напечатала' % k_dense)"""),
(u"""    if has_rmx and row[u'live']:
        if row[u'rmx'] is None:
            row[u'bad'].append(u'матрица .rmx есть, а клейма из неё проба не прочла')
        elif row[u'rmx'] != row[u'live']:
            if row.get(u'byown') and row[u'byown'] == row[u'rmx']:""",
 u"""    if has_rmx and row[u'live']:
        if row[u'rmx'] is None:
            row[u'bad'].append(u'матрица .rmx есть, а клейма из неё проба не прочла')
        elif k_dense and row.get(u'dense') and row[u'rmx'] == row[u'dense']:
            # густая сцена: матрица штатного рецепта с K × историй — годна по правилу Amber 18.09.2026
            row[u'dense_ok'] = True
        elif row[u'rmx'] != row[u'live']:
            if row.get(u'byown') and row[u'byown'] == row[u'rmx']:"""),
(u"""            if row.get(u'byown') and row[u'byown'] == row[u'rmx']:
                grid = row.get(u'grid')
                detail = (u' (узлов %s против умолчания %s, сетка %s против %s)' % grid) if grid else u''
                row[u'bad'].append(u'МАТРИЦА ПОСЧИТАНА НА ЭТУ СЦЕНУ, НО ДРУГИМ РЕЦЕПТОМ: клеймо файла %s '
                                   u'равно клейму по его настройкам, а не умолчаниям%s' % (short(row[u'rmx']), detail))""",
 u"""            if row.get(u'byown') and row[u'byown'] == row[u'rmx']:
                grid = row.get(u'grid')
                detail = (u' (узлов %s против умолчания %s, сетка %s против %s)' % grid) if grid else u''
                if k_dense:
                    detail += u' [густая сцена ×%d: ждали клеймо при %s историях, не сошлось]' % (
                        k_dense, row.get(u'dense_hist') or u'K × умолчание')
                row[u'bad'].append(u'МАТРИЦА ПОСЧИТАНА НА ЭТУ СЦЕНУ, НО ДРУГИМ РЕЦЕПТОМ: клеймо файла %s '
                                   u'равно клейму по его настройкам, а не умолчаниям%s' % (short(row[u'rmx']), detail))"""),
(u"""def run_check(probes, live_dir, only=None, jobs=None, keep=None, skip_freshness=False,
              quiet=False, silent=False, csv_path=None):""",
 u"""def run_check(probes, live_dir, only=None, jobs=None, keep=None, skip_freshness=False,
              quiet=False, silent=False, csv_path=None, dense=None):"""),
(u"""            rows = list(pool.map(lambda k: judge_scene(k, live_dir, gen_dir, probes, none_rmx), to_stamp))""",
 u"""            rows = list(pool.map(lambda k: judge_scene(k, live_dir, gen_dir, probes, none_rmx, dense), to_stamp))"""),
(u"""        n_bad = 0
        n_rmx = 0
        n_rmx_ok = 0
        header = False
        for r in rows:
            ok = not r[u'bad']
            if r[u'has_rmx']:
                n_rmx += 1
                if r[u'rmx'] and r[u'rmx'] == r[u'live']:
                    n_rmx_ok += 1""",
 u"""        n_bad = 0
        n_rmx = 0
        n_rmx_ok = 0
        n_dense_ok = 0
        header = False
        for r in rows:
            ok = not r[u'bad']
            if r[u'has_rmx']:
                n_rmx += 1
                if r[u'rmx'] and r[u'rmx'] == r[u'live']:
                    n_rmx_ok += 1
                elif r.get(u'dense_ok'):
                    n_rmx_ok += 1
                    n_dense_ok += 1"""),
(u"""            out.say(u'  %-30s %-24s %-24s %-24s %s' % (
                r[u'key'], short(r[u'live']), short(r[u'gen']),
                short(r[u'rmx']) if r[u'has_rmx'] else u'нет .rmx',
                u'СОШЛОСЬ' if ok else u'РАЗОШЛОСЬ'))""",
 u"""            out.say(u'  %-30s %-24s %-24s %-24s %s%s' % (
                r[u'key'], short(r[u'live']), short(r[u'gen']),
                short(r[u'rmx']) if r[u'has_rmx'] else u'нет .rmx',
                u'СОШЛОСЬ' if ok else u'РАЗОШЛОСЬ',
                (u' (рецепт ×%d, историй %s)' % (r[u'dense_k'], r.get(u'dense_hist') or u'?')) if r.get(u'dense_ok') else u''))"""),
(u"""        out.say(u'  сцен %d: сошлось %d, разошлось %d; клейма сняты у %d; матриц на месте %d из %d, на своей сцене и рецепте %d'
                % (len(rows), len(rows) - n_bad, n_bad, len(stamped), n_rmx, len(live_keys), n_rmx_ok))""",
 u"""        out.say(u'  сцен %d: сошлось %d, разошлось %d; клейма сняты у %d; матриц на месте %d из %d, на своей сцене и рецепте %d%s'
                % (len(rows), len(rows) - n_bad, n_bad, len(stamped), n_rmx, len(live_keys), n_rmx_ok,
                   (u' (из них густых ×K по правилу Amber 18.09.2026: %d)' % n_dense_ok) if n_dense_ok else u''))"""),
(u"""        out.say(u'  ✅ СОШЛОСЬ: генератор воспроизводит корпус с точностью до клейма, опись побайтно, '
                u'матрицы склада — на своих сценах штатным рецептом')""",
 u"""        out.say(u'  ✅ СОШЛОСЬ: генератор воспроизводит корпус с точностью до клейма, опись побайтно, '
                u'матрицы склада — на своих сценах штатным рецептом%s'
                % (u' (густые сцены — ×K историй по реестру DENSE_SCENES)' if n_dense_ok else u''))"""),
])
