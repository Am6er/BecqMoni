import io
p = r"D:\BqMoni_Claude\p147\wt\tools\effmaker\probes\FsaBetaPlusShareProbe.cs"
raw = open(p, 'rb').read()
t = raw.decode('utf-8')
nl = '\r\n' if '\r\n' in t else '\n'
a1 = t.index('<<<<<<< HEAD'); e1 = t.index('>>>>>>> cbd722fc', a1); e1 = t.index(nl, e1) + len(nl)
block1 = nl.join([
"    ///                           [--formula=ensdf|supply] [--identity] [--ti-split=1|0]",
"    ///",
"    /// `--ti-split=0` — питания, данные только полным (TI), не делить теорией",
"    /// ε/β⁺ (`AMBER121`, П169: поведение до правки)."]) + nl
t = t[:a1] + block1 + t[e1:]
a2 = t.index('<<<<<<< HEAD'); e2 = t.index('>>>>>>> cbd722fc', a2); e2 = t.index(nl, e2) + len(nl)
block2 = nl.join([
'            Console.WriteLine("питаний только полным (TI), разделённых теорией ε/β⁺: {0} (--ti-split={1})",',
'                              CascadeAtomicData.TotalFeedingSplits,',
'                              CascadeAtomicData.SplitTotalFeedingByTheory ? 1 : 0);',
'            Console.WriteLine("тождество совместной I(γ)·P(511|γ) = I(511)·P(γ|511) нарушено у {0} линий; худшее {1} %",',
'                              broken, F(100.0 * worst, "F2"));',
'            if (check && (over > 0 || lost > 0)) return 1;',
'            return identity && broken > 0 ? 1 : 0;']) + nl
t = t[:a2] + block2 + t[e2:]
assert '<<<<<<<' not in t and '>>>>>>>' not in t and '=======' + nl not in t
open(p, 'wb').write(t.encode('utf-8') if not raw.startswith(b'\xef\xbb\xbf') else t.encode('utf-8'))
print('ok', nl == '\r\n', raw[:3] == b'\xef\xbb\xbf')
