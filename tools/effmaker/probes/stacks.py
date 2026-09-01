# -*- coding: utf-8 -*-
"""Разбор стекового профиля PerfView: включительное и собственное время по функциям.

Плоский CSV, которым мы пользовались, приписывает сэмпл ИМЕНИ ВЕРХНЕГО ФРЕЙМА, и
на инлайненном коде это врёт — дважды за заход снятие работы с «горячей строки»
дало ноль. Здесь берутся полные стеки: у каждой функции считается ВКЛЮЧИТЕЛЬНОЕ
время (сэмпл засчитывается функции, если она где-то есть в стеке — по одному разу,
даже если рекурсия) и СОБСТВЕННОЕ (функция на вершине стека).
"""
import io, re, sys, collections

path = sys.argv[1]
top = int(sys.argv[2]) if len(sys.argv) > 2 else 30

txt = io.open(path, encoding='utf-8-sig').read()

frames = {}
for m in re.finditer(r'<Frame ID="(\d+)">(.*?)</Frame>', txt, re.S):
    frames[int(m.group(1))] = m.group(2)

caller, frame_of = {}, {}
for m in re.finditer(r'<Stack ID="(\d+)" CallerID="(-?\d+)" FrameID="(\d+)"', txt):
    sid = int(m.group(1))
    caller[sid] = int(m.group(2))
    frame_of[sid] = int(m.group(3))

samples = collections.Counter()
for m in re.finditer(r'<Sample ID="\d+" Time="[\d.]+" StackID="(\d+)"', txt):
    samples[int(m.group(1))] += 1

total = sum(samples.values())


def short(name):
    name = re.sub(r'^.*?!', '', name)
    name = re.sub(r'BecquerelMonitor\.EfficiencyMaker\.', '', name)
    name = re.sub(r'\(.*$', '', name)
    return name


inc = collections.Counter()
exc = collections.Counter()
for sid, n in samples.items():
    exc[short(frames[frame_of[sid]])] += n
    seen, cur = set(), sid
    while cur >= 0:
        f = short(frames[frame_of[cur]])
        if f not in seen:
            seen.add(f)
            inc[f] += n
        cur = caller.get(cur, -1)

print('%s: сэмплов %d' % (path.split('\\')[-1], total))
print()
print('=== ВКЛЮЧИТЕЛЬНОЕ время (функция где-то в стеке) ===')
for name, n in inc.most_common(200):
    if name.startswith(('ROOT', 'Process', 'Thread', 'BROKEN')):
        continue
    if n * 100.0 / total < 1.0:
        break
    print('%6.1f %%  %s' % (n * 100.0 / total, name[:95]))
print()
print('=== СОБСТВЕННОЕ время (вершина стека) ===')
for name, n in exc.most_common(top):
    print('%6.1f %%  %s' % (n * 100.0 / total, name[:95]))
