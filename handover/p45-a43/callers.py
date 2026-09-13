# -*- coding: utf-8 -*-
"""Кто зовёт функцию: по стековому XML PerfView печатает включительное время
функции в разрезе БЛИЖАЙШЕГО известного вызывающего кадра (П45).

  python callers.py <file.perfView.xml> <подстрока имени> [top]
"""
import io, re, sys, collections

path, want = sys.argv[1], sys.argv[2]
top = int(sys.argv[3]) if len(sys.argv) > 3 else 25
txt = io.open(path, encoding='utf-8-sig').read()
frames = {}
for m in re.finditer(r'<Frame ID="(\d+)">(.*?)</Frame>', txt, re.S):
    frames[int(m.group(1))] = m.group(2)
caller, frame_of = {}, {}
for m in re.finditer(r'<Stack ID="(\d+)" CallerID="(-?\d+)" FrameID="(\d+)"', txt):
    sid = int(m.group(1)); caller[sid] = int(m.group(2)); frame_of[sid] = int(m.group(3))
samples = collections.Counter()
for m in re.finditer(r'<Sample ID="\d+" Time="[\d.]+" StackID="(\d+)"', txt):
    samples[int(m.group(1))] += 1
total = sum(samples.values())


def short(name):
    name = re.sub(r'^.*?!', '', name)
    name = re.sub(r'BecquerelMonitor\.EfficiencyMaker\.', '', name)
    name = re.sub(r'\(.*$', '', name)
    return name


by_caller = collections.Counter()
own = 0
for sid, n in samples.items():
    cur = sid
    # ищем самое ВЕРХНЕЕ (ближайшее к вершине) вхождение искомой функции
    chain = []
    while cur >= 0:
        chain.append(cur)
        cur = caller.get(cur, -1)
    hit = None
    for c in chain:
        if want in short(frames[frame_of[c]]):
            hit = c
            break
    if hit is None:
        continue
    # вызывающий — первый кадр НИЖЕ hit, чьё имя не совпадает с want и не «?»
    c = caller.get(hit, -1)
    name = '(корень)'
    while c >= 0:
        f = short(frames[frame_of[c]])
        if want not in f and f != '?':
            name = f
            break
        c = caller.get(c, -1)
    by_caller[name] += n
    if frame_of[sid] == frame_of[hit]:
        own += n

hits = sum(by_caller.values())
print('%s: всего сэмплов %d, с «%s» в стеке %d (%.1f %%), собственных %d (%.1f %%)'
      % (path.split('\\')[-1], total, want, hits, hits * 100.0 / total, own, own * 100.0 / total))
for name, n in by_caller.most_common(top):
    print('%6.1f %%  <- %s' % (n * 100.0 / total, name[:100]))
