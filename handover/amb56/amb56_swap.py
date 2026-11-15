# -*- coding: utf-8 -*-
r"""amb56 (AMBER274, 15.11.2026) — подмена рабочего склада матриц конфигурации приложения (12 `.rmx` в
`config\device\response`) на склад полосы `store_full` с разделами электрона сегодняшнего рецепта (`cl=2;l0=1`, экранирование
ВЫКЛ) и, если в складе полосы есть, НОВЫМИ матрицами кривых, у которых матрицы не было (их `restore` снимает). Склад полосы
сверяется до копирования с принятым списком sha256 (STORE_SHA). Ещё переносятся XML приборов из XML_STORE (кривая, посчитанная
полосой на копии): только файлы из списка XML_SHA, со снимком в BACKUP\xml, сверкой с XML_BASE_SHA до и откатом в restore.
Образец — handover/store55/store55_swap.py; отличия: склад конфигурации лежит по guid кривой (каталога «по ключу» нет),
XML приборов не меняются (матрица ищется по guid кривой), и подмена отказывает, пока приложение открыто.

⛔ Запускать на конфигурации Amber — только её словом, при ЗАКРЫТОМ приложении и ПОСЛЕ обновления приложения до выпуска не
старше rev55 (затвор MIN_APP: версия последнего запуска из becqmoni.log). Полоса проверяла скрипт на копиях.

  python -I swap.py backup  [--target=<каталог response>]  -- снимок целевого склада (12 .rmx) в BACKUP + sha256_before.txt;
                                                              каталог снимка обязан НЕ существовать
  python -I swap.py swap    [--target=…]  -- склад полосы -> целевой склад; только после backup; целевой обязан быть равен
                                             снимку И тому складу, от которого полоса считала (BASE_SHA), иначе отказ; затем verify
  python -I swap.py verify  [--target=…]  -- целевой склад = склад полосы по sha256, лишних и недостающих нет
  python -I swap.py restore [--target=…]  -- снимок -> целевой склад, затем сверка со снимком
  ключ --backup=<каталог снимка> — вместо умолчания BACKUP (для проверки на копиях); --store=<склад полосы> — вместо STORE;
  --store-sha=<список> — вместо STORE_SHA;
  --log=<журнал приложения> — откуда читать версию последнего запуска; --old-app-ok — снять затвор версии (только для копий)

Спектры прибора M13 (решение Amber 15.11.2026 «Исправить скриптом»; папка — ТОЛЬКО ключом):
  python -I swap.py fix-spectra     --fix-spectra=<папка> --dry-run   -- перечень: какие файлы и что меняется, ничего не пишет
  python -I swap.py backup-spectra  --fix-spectra=<папка> [--spec-backup=<снимок>]  -- копии и sha256 затрагиваемых файлов
  python -I swap.py fix-spectra     --fix-spectra=<папка> [--spec-backup=…]  -- правка; только после снимка и пока файлы = снимку
  python -I swap.py restore-spectra --fix-spectra=<папка> [--spec-backup=…]  -- снимок обратно, сверка побайтно

Умолчание --target — %APPDATA%\BecqMoni\config\device\response. Коды: 0 — сошлось; 1 — расхождение; 2 — стенд/отказ.
"""
import hashlib, io, os, re, shutil, subprocess, sys

# Склад к подмене — решение Amber 15.11.2026 вопросником, дословно: «Целиком новой библиотекой (Рекомендую)» (store_full:
# все разделы — DLL сегодняшней сборки; склейка `store` по правилу склада недопустима — смешение паспортов DLL, критик critamb56).
STORE = r'D:\BqMoni_Claude\amb56\store_full'
STORE_SHA = r'D:\BqMoni_Claude\amb56\store_full_sha256.txt'  # ПРИНЯТЫЙ склад: 12 + новая матрица кривой без матрицы; сверка до копирования
BACKUP = r'D:\BqMoni_Claude\amb56\backup_live'
BASE_SHA = r'D:\BqMoni_Claude\amb56\store_base_sha256.txt'   # склад, от которого считала полоса (копия 15.11.2026)
EXPECT = 12   # матриц в целевом складе до подмены; склад полосы несёт ещё НОВЫЕ guid (кривая, у которой матрицы не было)
# ⛔ Разделы полосы несут поля `cl=2;l0=1`. Выпуск 2026.11.07.01 (bb075668) этих полей не знает: он раздел ВОЗЬМЁТ, а веса
# беты посчитает прежней формой (без λ и L0) — молчаливое смешение двух форм, против которого и стоит сверка `cl`/`l0`
# (a274s). Поэтому подмена — только когда приложение обновлено до выпуска после rev55 (b4788ff6, 13.11.2026 22:26):
# выпусков между 2026.11.07.01 и b4788ff6 нет, следующий выпуск от master получает номер не раньше 2026.11.14.
MIN_APP = (2026, 11, 14)
# XML приборов, которые подмена ПЕРЕНОСИТ (кривая, посчитанная полосой на копии; решение Amber 15.11.2026 вопросником, дословно:
# «Полоса на копии»). Имена файлов в код не пишутся — только списками на диске: XML_STORE\<имя> — новый файл, XML_SHA —
# его принятый sha256, XML_BASE_SHA — sha256 того же файла у Amber до счёта полосы. Лежат в каталоге приборов (родитель response).
XML_STORE = r'D:\BqMoni_Claude\amb56\store_xml'
XML_SHA = r'D:\BqMoni_Claude\amb56\store_xml_sha256.txt'
XML_BASE_SHA = r'D:\BqMoni_Claude\amb56\store_xml_base_sha256.txt'


def xml_table(d, names):
    return dict((f, sha(os.path.join(d, f))) for f in names if os.path.isfile(os.path.join(d, f)))


# ---------------------------------------------------------------------------------------------------------------------------
# СПЕКТРЫ ПРИБОРА M13 (решение Amber 15.11.2026 вопросником, дословно: «Исправить скриптом»). Только ключом --fix-spectra=<папка>,
# путь в код не пишется. В каждом файле, где ссылка на прибор по ИМЕНИ — прибор M13, а guid чужой: (1) guid ссылки -> guid
# прибора M13; (2) кривая M13 назначается спектру — элемент <Efficiency>, каким его пишет сериализатор приложения при выборе
# кривой (`Efficiency = кривая.Copy()`), вставляется сразу за </DeviceConfigReference>, как ставит его сериализатор.
# ⚠ ТЕКСТОВОЙ правкой, а не пересохранением: сериализатор приложения меняет файл шире (снимает BOM и пустой
# <ROIConfigReference />, оборачивает имя пробы в CDATA, дописывает <LiveTime>0</LiveTime> спектру и фону, кладёт встроенную
# матрицу ~0.7 МБ). Фрагмент <Efficiency> снят с файла, записанного сериализатором приложения, и закреплён sha256 (SPEC_FRAG_SHA).
# Прочие байты файла (BOM, переводы строк, порядок элементов) — прежние; чтение приложением проверяется отдельно (OwnSpecCheck).
SPEC_FRAG = r'D:\BqMoni_Claude\amb56\spec_fix\efficiency_fragment.xml'
SPEC_FRAG_SHA = r'D:\BqMoni_Claude\amb56\spec_fix\efficiency_fragment_sha256.txt'
SPEC_BACKUP = r'D:\BqMoni_Claude\amb56\backup_spectra'

REF_RE = re.compile(r'(\r?\n)( *)<DeviceConfigReference>\r?\n *<Name>([^<]*)</Name>\r?\n *<Guid>([^<]*)</Guid>\r?\n *</DeviceConfigReference>(\r?\n)')


def m13_identity():
    """Имя и guid прибора M13 и guid его кривой — из принятого XML прибора (XML_STORE) и фрагмента; ничего не вшито."""
    names = xml_names()
    if len(names) != 1:
        return None
    text = io.open(os.path.join(XML_STORE, names[0]), encoding='utf-8-sig').read()
    dev_guid = re.search(r'<Guid>([^<]+)</Guid>', text).group(1)
    dev_name = re.search(r'<Name>([^<]*)</Name>', text).group(1)
    frag = open(SPEC_FRAG, 'rb').read()
    curve_guid = re.search(br'<Guid>([^<]+)</Guid>', frag).group(1).decode('ascii')
    return dev_name, dev_guid, curve_guid, frag


def spectrum_plan(path, ident):
    """None — файл не трогается (с причиной); иначе (новые байты, описание правки)."""
    dev_name, dev_guid, curve_guid, frag = ident
    raw = open(path, 'rb').read()
    bom = raw[:3] == b'\xef\xbb\xbf'
    text = raw[3:].decode('utf-8') if bom else raw.decode('utf-8')
    refs = list(REF_RE.finditer(text))
    if len(refs) != 1:
        return None, u'ссылок на прибор %d (ждём 1) — не трогается' % len(refs)
    m = refs[0]
    if m.group(3) != dev_name:
        return None, u'ссылка на другой прибор по имени — не трогается'
    if m.group(2) != '      ':
        return None, u'отступ ссылки не тот, что у фрагмента — не трогается'
    has_eff = re.search(r'\n      <Efficiency>\r?\n', text) is not None
    if m.group(4) == dev_guid and has_eff:
        return None, u'уже исправлен — не трогается'
    nl = m.group(5)
    piece = frag.decode('utf-8')
    if nl == '\n':
        piece = piece.replace('\r\n', '\n')
    ref_new = m.group(0).replace('<Guid>%s</Guid>' % m.group(4), '<Guid>%s</Guid>' % dev_guid)
    what = []
    if m.group(4) != dev_guid:
        what.append(u'guid ссылки %s… -> %s… (прибор M13)' % (m.group(4)[:8], dev_guid[:8]))
    if not has_eff:
        ref_new += piece
        what.append(u'кривая M13 (%s…) назначена: <Efficiency> %d байт за ссылкой' % (curve_guid[:8], len(piece.encode('utf-8'))))
    else:
        what.append(u'<Efficiency> уже есть — кривая не меняется')
    new_text = text[:m.start()] + ref_new + text[m.end():]
    new_raw = (b'\xef\xbb\xbf' if bom else b'') + new_text.encode('utf-8')
    return new_raw, u'; '.join(what)


def spectra_mode(mode, folder, dry, backup):
    if not folder or not os.path.isdir(folder):
        print(u'⛔ нужен --fix-spectra=<папка спектров>')
        return 2
    if not os.path.isfile(SPEC_FRAG) or sha(SPEC_FRAG) != list(read_list(SPEC_FRAG_SHA).values())[0]:
        print(u'⛔ фрагмент <Efficiency> не тот, что закреплён (%s)' % SPEC_FRAG_SHA)
        return 2
    ident = m13_identity()
    if ident is None:
        print(u'⛔ нет принятого XML прибора M13 (XML_STORE)')
        return 2
    accepted = read_list(STORE_SHA) if os.path.isfile(STORE_SHA) else {}
    if ident[2] + '.rmx' not in accepted:
        print(u'⛔ матрицы кривой фрагмента нет в принятом складе — кривая без матрицы не назначается')
        return 2
    files = sorted(f for f in os.listdir(folder) if f.lower().endswith('.xml'))
    plan = {}
    for f in files:
        new_raw, what = spectrum_plan(os.path.join(folder, f), ident)
        if new_raw is not None:
            plan[f] = new_raw
        if mode == 'fix-spectra' and dry:
            print(u'  %s %s: %s' % (u'ПРАВКА' if new_raw is not None else u'—', f, what))
    print(u'файлов в папке %d, к правке %d' % (len(files), len(plan)))
    if mode == 'fix-spectra' and dry:
        return 0
    if mode in ('fix-spectra', 'restore-spectra') and app_running():
        print(u'⛔ BecquerelMonitor.exe запущен — закройте приложение; спектры не тронуты')
        return 2
    snap_list = os.path.join(backup, 'sha256_spectra_before.txt')
    if mode == 'backup-spectra':
        if os.path.exists(backup):
            print(u'⛔ %s уже есть — снимок не перезаписывается' % backup)
            return 2
        if not plan:
            print(u'⛔ править нечего')
            return 2
        os.makedirs(backup)
        for f in plan:
            shutil.copy2(os.path.join(folder, f), os.path.join(backup, f))
        t = xml_table(backup, sorted(plan))
        write_list(t, snap_list)
        ok = compare(xml_table(folder, sorted(plan)), t, u'снимок спектров')
        print(u'снимок: %s (%d файлов)' % (backup, len(t)))
        return 0 if ok else 1
    if not os.path.isfile(snap_list):
        print(u'⛔ сперва backup-spectra: снимка %s нет' % backup)
        return 2
    snap = read_list(snap_list)
    if mode == 'fix-spectra':
        if set(plan) != set(snap) or not compare(xml_table(folder, sorted(snap)), snap, u'спектры = снимку'):
            print(u'⛔ спектры сменились после снимка (или состав правки другой) — не тронуты')
            return 1
        for f in sorted(plan):
            dst = os.path.join(folder, f)
            tmp = dst + '.amb56tmp'
            with open(tmp, 'wb') as fh:
                fh.write(plan[f])
            os.replace(tmp, dst)
        bad = [f for f in sorted(plan) if spectrum_plan(os.path.join(folder, f), ident)[0] is not None]
        print(u'исправлено: %d; повторный план пуст: %s' % (len(plan), u'да' if not bad else u'⛔ нет (%d)' % len(bad)))
        return 0 if not bad else 1
    if mode == 'restore-spectra':
        for f in sorted(snap):
            if not copy_checked(os.path.join(backup, f), os.path.join(folder, f), snap[f]):
                print(u'⛔ снимок испорчен: %s' % f)
                return 1
        ok = compare(xml_table(folder, sorted(snap)), snap, u'спектры = снимку (побайтно)')
        return 0 if ok else 1
    return 2


def sha(path):
    h = hashlib.sha256()
    with open(path, 'rb') as fh:
        for chunk in iter(lambda: fh.read(1 << 20), b''):
            h.update(chunk)
    return h.hexdigest()


def rmx(d):
    return sorted(f for f in os.listdir(d) if f.lower().endswith('.rmx')) if os.path.isdir(d) else []


def table(d):
    return dict((f, sha(os.path.join(d, f))) for f in rmx(d))


def read_list(path):
    t = {}
    with io.open(path, encoding='utf-8') as fh:
        for line in fh:
            line = line.strip()
            if line:
                h, f = line.split('  ', 1)
                t[f] = h
    return t


def write_list(t, path):
    with io.open(path, 'w', encoding='utf-8', newline='\n') as fh:
        for f in sorted(t):
            fh.write(u'%s  %s\n' % (t[f], f))


def compare(a, b, what):
    bad = [f for f in sorted(set(a) | set(b)) if a.get(f) != b.get(f)]
    print(u'%s: %d из %d сошлись, расхождений %d' % (what, len(a) - len([f for f in bad if f in a]), len(a), len(bad)))
    for f in bad[:20]:
        print(u'   ⛔ %s: %s / %s' % (f, (a.get(f) or u'нет')[:16], (b.get(f) or u'нет')[:16]))
    return not bad


def app_running():
    try:
        raw = subprocess.run(['tasklist', '/FI', 'IMAGENAME eq BecquerelMonitor.exe', '/FO', 'CSV', '/NH'],
                             capture_output=True, timeout=30).stdout
        out = (raw or b'').decode('ascii', errors='replace')   # вывод tasklist — в кодировке консоли; имя процесса латиницей
    except Exception as ex:  # не смогли проверить — считаем, что открыто
        print(u'⛔ не удалось проверить процессы: %s' % ex)
        return True
    return 'BecquerelMonitor.exe' in out


def pending(target):
    return sorted(f for f in os.listdir(target) if f.lower().endswith('.rmx.pending')) if os.path.isdir(target) else []


def last_app_version(log):
    try:
        with io.open(log, encoding='utf-8', errors='replace') as fh:
            found = re.findall(r'=== BecqMoni (\d+)\.(\d+)\.(\d+)(?:\.(\d+))? ', fh.read())
    except (IOError, OSError):
        return None
    return tuple(int(x or 0) for x in found[-1]) if found else None


def store_accepted():
    """Склад полосы = принятому списку sha256 (STORE_SHA): ни лишнего, ни недостающего, ни правленого."""
    if not os.path.isfile(STORE_SHA):
        print(u'⛔ нет списка принятого склада %s' % STORE_SHA)
        return None
    st = table(STORE)
    if not compare(st, read_list(STORE_SHA), u'склад полосы = принятому списку'):
        return None
    return st


def xml_names():
    return sorted(read_list(XML_SHA)) if os.path.isfile(XML_SHA) else []


def xml_accepted():
    """XML полосы = принятому списку (XML_SHA); пустой список — XML не переносится."""
    names = xml_names()
    if not names:
        return {}
    xs = xml_table(XML_STORE, names)
    if not compare(xs, read_list(XML_SHA), u'XML полосы = принятому списку'):
        return None
    return xs


def copy_checked(src, dst, want):
    tmp = dst + '.amb56tmp'
    shutil.copyfile(src, tmp)
    if sha(tmp) != want:
        os.remove(tmp)
        return False
    os.replace(tmp, dst)
    return True


def verify(target):
    s = store_accepted()
    xs = xml_accepted()
    if s is None or xs is None:
        return False
    t = table(target)
    ok = len(s) >= EXPECT and set(t) == set(s)
    ok &= compare(t, s, u'целевой = склад полосы')
    if xs:
        ok &= compare(xml_table(os.path.dirname(target), sorted(xs)), xs, u'XML приборов целевого = полосы')
    return ok


def main():
    global BACKUP
    mode = sys.argv[1] if len(sys.argv) > 1 else ''
    global STORE, STORE_SHA
    target = os.path.join(os.environ.get('APPDATA', ''), 'BecqMoni', 'config', 'device', 'response')
    log = os.path.join(os.environ.get('APPDATA', ''), 'BecqMoni', 'becqmoni.log')
    old_app_ok = False
    spec_folder, spec_backup, dry = None, SPEC_BACKUP, False
    for x in sys.argv[2:]:
        if x.startswith('--target='):
            target = x.split('=', 1)[1]
        elif x.startswith('--backup='):
            BACKUP = x.split('=', 1)[1]
        elif x.startswith('--store='):
            STORE = x.split('=', 1)[1]
        elif x.startswith('--store-sha='):
            STORE_SHA = x.split('=', 1)[1]
        elif x.startswith('--log='):
            log = x.split('=', 1)[1]
        elif x == '--old-app-ok':
            old_app_ok = True
        elif x.startswith('--fix-spectra='):
            spec_folder = x.split('=', 1)[1]
        elif x.startswith('--spec-backup='):
            spec_backup = x.split('=', 1)[1]
        elif x == '--dry-run':
            dry = True
        else:
            print(u'⛔ ключ? %s' % x)
            return 2
    if mode in ('backup-spectra', 'fix-spectra', 'restore-spectra'):
        print(u'папка спектров: %s; снимок: %s%s' % (spec_folder, spec_backup, u'; ПРОБНЫЙ ПРОГОН' if dry else u''))
        return spectra_mode(mode, spec_folder, dry, spec_backup)
    print(u'целевой склад: %s; склад полосы: %s; снимок: %s' % (target, STORE, BACKUP))
    if not os.path.isdir(target):
        print(u'⛔ нет целевого склада %s' % target)
        return 2
    if mode in ('swap', 'restore'):
        if app_running():
            print(u'⛔ BecquerelMonitor.exe запущен — закройте приложение; подмена не начата')
            return 2
        v = last_app_version(log)
        print(u'последний запуск приложения по журналу %s: %s' % (log, u'.'.join(str(p) for p in v) if v else u'не найден'))
        if mode == 'swap' and not old_app_ok and (v is None or v[:3] < MIN_APP):
            print(u'⛔ приложение старше rev55 (нужен выпуск от %d.%02d.%02d): разделы cl=2;l0=1 оно смешает с прежней формой беты — '
                  u'сперва обновить приложение и запустить его один раз; подмена не начата' % MIN_APP)
            return 2
        if pending(target):
            print(u'⛔ в целевом складе есть ждущие сохранения матрицы (%s) — подмена не начата' % u', '.join(pending(target)))
            return 2
    if mode == 'backup':
        if os.path.exists(BACKUP):
            print(u'⛔ %s уже есть — снимок не перезаписывается' % BACKUP)
            return 2
        a = table(target)
        if len(a) != EXPECT:
            print(u'⛔ в целевом складе %d .rmx (ждём %d)' % (len(a), EXPECT))
            return 2
        os.makedirs(BACKUP)
        for f in a:
            shutil.copy2(os.path.join(target, f), os.path.join(BACKUP, f))
        b = table(BACKUP)
        write_list(b, os.path.join(BACKUP, 'sha256_before.txt'))
        ok = compare(a, b, u'снимок')
        ok &= compare(a, read_list(BASE_SHA), u'целевой = склад, от которого считала полоса')
        names = xml_names()
        if names:
            dev_dir = os.path.dirname(target)
            xa = xml_table(dev_dir, names)
            if len(xa) != len(names):
                print(u'⛔ в каталоге приборов нет %d XML из списка' % (len(names) - len(xa)))
                return 2
            os.makedirs(os.path.join(BACKUP, 'xml'))
            for f in xa:
                shutil.copy2(os.path.join(dev_dir, f), os.path.join(BACKUP, 'xml', f))
            xb = xml_table(os.path.join(BACKUP, 'xml'), names)
            write_list(xb, os.path.join(BACKUP, 'sha256_xml_before.txt'))
            ok &= compare(xa, xb, u'снимок XML приборов')
            ok &= compare(xa, read_list(XML_BASE_SHA), u'XML приборов = тем, от которых считала полоса')
        print(u'снимок: %s (%d .rmx, %d XML)' % (BACKUP, len(b), len(names)))
        return 0 if ok else 1
    if mode == 'swap':
        if not os.path.isdir(BACKUP):
            print(u'⛔ сперва backup: снимка %s нет' % BACKUP)
            return 2
        a = table(target)
        if not compare(a, table(BACKUP), u'целевой = снимок'):
            print(u'⛔ целевой склад сменился после снимка — подмена не начата')
            return 1
        if not compare(a, read_list(BASE_SHA), u'целевой = склад, от которого считала полоса'):
            print(u'⛔ матрицы пересчитаны после счёта полосы — разделы полосы к ним не подходят; подмена не начата')
            return 1
        st = store_accepted()
        if st is None:
            print(u'⛔ склад полосы не тот, что принят, — подмена не начата')
            return 2
        new = sorted(set(st) - set(a))
        if len(a) != EXPECT or not set(a) <= set(st):
            print(u'⛔ guid не те: %d .rmx в складе полосы, %d в целевом, новых %d' % (len(st), len(a), len(new)))
            return 2
        for f in new:
            print(u'новая матрица (в целевом её нет; restore её снимет): %s' % f)
        xs = xml_accepted()
        dev_dir = os.path.dirname(target)
        if xs is None:
            print(u'⛔ XML полосы не тот, что принят, — подмена не начата')
            return 2
        if xs:
            xa = xml_table(dev_dir, sorted(xs))
            if not (compare(xa, xml_table(os.path.join(BACKUP, 'xml'), sorted(xs)), u'XML приборов = снимку')
                    and compare(xa, read_list(XML_BASE_SHA), u'XML приборов = тем, от которых считала полоса')):
                print(u'⛔ XML прибора сменился (снимок или счёт полосы) — подмена не начата')
                return 1
        for f in st:
            if not copy_checked(os.path.join(STORE, f), os.path.join(target, f), st[f]):
                print(u'⛔ копия %s испорчена — подмена прервана, восстановите: restore' % f)
                return 1
        for f in sorted(xs):
            if not copy_checked(os.path.join(XML_STORE, f), os.path.join(dev_dir, f), xs[f]):
                print(u'⛔ копия XML испорчена — подмена прервана, восстановите: restore')
                return 1
        print(u'перенесено: %d .rmx, %d XML' % (len(st), len(xs)))
        return 0 if verify(target) else 1
    if mode == 'verify':
        return 0 if verify(target) else 1
    if mode == 'restore':
        if not os.path.isdir(BACKUP):
            print(u'⛔ снимка %s нет' % BACKUP)
            return 2
        for f in rmx(BACKUP):
            tmp = os.path.join(target, f + '.amb56tmp')
            shutil.copyfile(os.path.join(BACKUP, f), tmp)
            os.replace(tmp, os.path.join(target, f))
        accepted = read_list(STORE_SHA) if os.path.isfile(STORE_SHA) else {}
        for f in sorted(set(rmx(target)) - set(rmx(BACKUP))):
            # снимается только то, что положила подмена: файла не было в снимке, и он побайтно = принятому складу
            if accepted.get(f) == sha(os.path.join(target, f)):
                os.remove(os.path.join(target, f))
                print(u'снята новая матрица %s' % f)
            else:
                print(u'⛔ %s нет в снимке и он не из склада полосы — не тронут' % f)
        ok = compare(table(target), table(BACKUP), u'целевой = снимок')
        ok &= compare(table(target), read_list(os.path.join(BACKUP, 'sha256_before.txt')), u'целевой = список снимка')
        xb_list = os.path.join(BACKUP, 'sha256_xml_before.txt')
        if os.path.isfile(xb_list):
            xb = read_list(xb_list)
            dev_dir = os.path.dirname(target)
            for f in sorted(xb):
                if not copy_checked(os.path.join(BACKUP, 'xml', f), os.path.join(dev_dir, f), xb[f]):
                    print(u'⛔ снимок XML испорчен: %s' % f)
                    ok = False
            ok &= compare(xml_table(dev_dir, sorted(xb)), xb, u'XML приборов = снимку')
        return 0 if ok else 1
    print(__doc__)
    return 2


if __name__ == '__main__':
    for _stream in (sys.stdout, sys.stderr):
        try:
            _stream.reconfigure(encoding='utf-8', errors='replace')
        except (AttributeError, ValueError):
            pass
    sys.exit(main())
