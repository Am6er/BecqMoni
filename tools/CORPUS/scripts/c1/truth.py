# -*- coding: utf-8 -*-
"""Истина сцены для подписи пика (полоса C1, S134/S64).

Таблица имён живёт ЗДЕСЬ, а не в пробе на C#: правило «имён нуклидов в коде
быть не должно» относится к приложению, а разметка «что реально лежало под
детектором» — свойство КОРПУСА и его манифеста.

⚠ ДОПУЩЕНИЕ, и оно названо: членство в ряду выписано вручную по обычному
составу рядов; полнота проверялась только для тех имён, что встречаются в
подписях корпуса.
"""
import csv, os

_HERE = os.path.dirname(os.path.abspath(__file__))
_CORPUS = os.path.normpath(os.path.join(_HERE, '..', '..', 'corpus'))

# ряд -> члены, дающие гамма-линии в библиотеке приложения
CHAIN = {
    'Th-232': {'Th-232', 'Ra-228', 'Ac-228', 'Th-228', 'Ra-224', 'Rn-220',
               'Pb-212', 'Bi-212', 'Tl-208', 'Tl-208 SE', 'Tl-208 DE', 'Po-212'},
    'Th-228': {'Th-228', 'Ra-224', 'Rn-220', 'Pb-212', 'Bi-212', 'Tl-208',
               'Tl-208 SE', 'Tl-208 DE', 'Po-212'},
    # Ra-226 как ИСТОЧНИК ряда: сверху него ничего нет
    'Ra-226': {'Ra-226', 'Rn-222', 'Pb-214', 'Bi-214', 'Po-214', 'Pb-210', 'Bi-210'},
    # природный уран: вся цепь от U-238
    'U-238u': {'U-238', 'U-238/U-234', 'Th-234', 'Pa-234m', 'Pa-234', 'U-234',
               'Th-230', 'Ra-226', 'Rn-222', 'Pb-214', 'Bi-214', 'Po-214',
               'Pb-210', 'Bi-210'},
    'U-238': {'U-238', 'U-238/U-234', 'Th-234', 'Pa-234m', 'Pa-234', 'U-234',
              'Th-230', 'Ra-226', 'Rn-222', 'Pb-214', 'Bi-214', 'Po-214',
              'Pb-210', 'Bi-210'},
    'U-235': {'U-235', 'Th-231', 'Pa-231', 'Ac-227', 'Th-227', 'Ra-223',
              'Rn-219', 'Pb-211', 'Bi-211', 'Tl-207'},
}

# «встроенный» природный фон: калий и оба природных ряда
BACKGROUND = (CHAIN['Th-232'] | CHAIN['U-238u'] | CHAIN['U-235'] | {'K-40'})

# NUCID манифеста -> имя нуклида
def nucid_to_name(t):
    t = t.strip()
    if not t:
        return None
    i = 0
    while i < len(t) and t[i].isdigit():
        i += 1
    a, el = t[:i], t[i:]
    if not a or not el:
        return None
    meta = ''
    if el and el[-1] in 'Mm' and len(el) > 1 and el[:-1].isalpha():
        # 234MPA / 108MAG — метка изомера
        pass
    if el[0] in 'Mm' and len(el) > 1:
        meta, el = 'm', el[1:]
    return el.capitalize() + '-' + a + meta


# дочерние, дающие линии вместо родителя
DAUGHTER = {'Ti-44': {'Sc-44'}, 'Cs-137': {'Ba-137m'}, 'Y-88': {'Sr-88'},
            'Ce-139': {'La-139'}, 'Cd-109': {'Ag-109m'}}


def scene_names(row):
    """Имена, ОБЪЯВЛЕННЫЕ в сцене (источник и его ряд), без природного фона."""
    s = set()
    for ch in (row.get('chains') or '').split(';'):
        ch = ch.strip()
        if ch:
            s |= CHAIN.get(ch, {ch})
    for nu in (row.get('nuclides') or '').split(';'):
        nm = nucid_to_name(nu)
        if nm:
            s.add(nm)
            s |= DAUGHTER.get(nm, set())
    return s


def load(manifest=None):
    manifest = manifest or os.path.join(_CORPUS, 'manifest.csv')
    out = {}
    with open(manifest, encoding='utf-8-sig', newline='') as fh:
        for row in csv.DictReader(fh):
            out[row['key']] = scene_names(row)
    return out


# Приборные образы: подпись не называет нуклид сцены и судится отдельно
INSTRUMENTAL = {'Annihilation', 'Xray-NaI', 'Xray-CsI', 'Esc-NaI'}


def is_instrumental(name):
    # ⛔ Приборный образ узнаётся по ПЕРВОЙ ЛЕКСЕМЕ, а не по всей строке: с
    # `A229` подпись суммы двух аннигиляционных квантов несёт хвост из
    # ресурсов приложения («Annihilation (sum 511+511)», по-русски «(сумма
    # 511+511)»), и сверка строки целиком роняла её в ЛОЖЬ. Цена измерена
    # полосой O21: 4 подписи корпуса, приборное 168 -> 164 и ЛОЖЬ 186 -> 190
    # на ОБОИХ плечах сразу. Лексема берётся до первого пробела — тем же
    # правилом, каким её читает приложение (`NuclideDefinition.NuclideNameOf`).
    head = name.split(' ', 1)[0]
    return (name in INSTRUMENTAL
            or head in INSTRUMENTAL
            or 'x-ray' in name.lower()
            or name.endswith(' SE') or name.endswith(' DE'))


def verdict(label, scene):
    """ИСТИНА (объявлено в сцене) / ФОН (только природный) / ЛОЖЬ / приборное.

    ⚠ ФОН отделён от ИСТИНЫ нарочно: природный ряд в пробе есть ВСЕГДА, и
    засчитывать по нему подпись слабой линии значило бы объявить верной любую
    метку любого природного нуклида где угодно. Цену такой поблажки видно
    прямо: `Pa-234m` 1001 кэВ в спектре Na-22 попал бы в ИСТИНУ.
    """
    if not label:
        return 'нет подписи'
    if is_instrumental(label):
        return 'приборное'
    names = [label] + [p.strip() for p in label.split('/')]
    for nm in names:
        if nm in scene:
            return 'ИСТИНА'
    for nm in names:
        if nm in BACKGROUND:
            return 'ФОН'
    return 'ЛОЖЬ'
