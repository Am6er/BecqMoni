# -*- coding: utf-8 -*-
import sys
def edit(p, pairs):
    b=open(p,'rb').read(); t=b.decode('utf-8'); n=0
    for old,new in pairs:
        c=t.count(old)
        assert c==1, (p, old[:70], c)
        t=t.replace(old,new); n+=1
    open(p,'wb').write(t.encode('utf-8')); print(p, n, 'replacements')
CR='\r\n'
p='D:/BqMoni_Claude/p87/wt/tools/effmaker/probes/MatrixDiffProbe.cs'
edit(p, [
 ('///   matrixdiffprobe --a=было.rmx --b=стало.rmx'+CR+
  '///'+CR+
  '/// Печатается расхождение в ПРОЦЕНТАХ: пик (последний бин), сумма строки'+CR+
  '/// (эффективность узла) и форма (L1 к сумме) — медиана и худший узел.'+CR+
  '/// </summary>',
  '///   matrixdiffprobe --a=было.rmx --b=стало.rmx'+CR+
  '///'+CR+
  '/// Печатается расхождение в ПРОЦЕНТАХ: пик (последний бин), сумма строки'+CR+
  '/// (эффективность узла) и форма (L1 к сумме) — медиана и худший узел.'+CR+
  '///'+CR+
  '/// (`AMBER46`, П87 16.09.2026) Файл ПРЕЖНЕГО формата (8) проба читает ради'+CR+
  '/// сравнения тел — `ResponseMatrix.Load(…, legacyFormatForComparison: 8)`:'+CR+
  '/// матрица формата 9 обязана давать ТОТ ЖЕ отпечаток тела, что её'+CR+
  '/// предшественница того же рецепта, а разница — только блок Q_k. Блок'+CR+
  '/// сличается отдельно: «есть у обеих / у одной / ни у одной», по узлам —'+CR+
  '/// побитово, наибольшее |Δ| и в σ обеих; формат каждого файла печатается.'+CR+
  '/// Годным старый файл от этого не становится — приложение его не читает.'+CR+
  '/// </summary>'),
 ('        ResponseMatrix a = ResponseMatrix.Load(aPath, out refusalA, out formatA);'+CR+
  '        ResponseMatrix b = ResponseMatrix.Load(bPath, out refusalB, out formatB);',
  '        ResponseMatrix a = ResponseMatrix.Load(aPath, out refusalA, out formatA, ResponseMatrix.PreviousFormatVersion);'+CR+
  '        ResponseMatrix b = ResponseMatrix.Load(bPath, out refusalB, out formatB, ResponseMatrix.PreviousFormatVersion);'),
 ('        Console.WriteLine("Расхождение двух матриц (T43)");'+CR+
  '        Console.WriteLine("  A: {0}", Path.GetFileName(aPath));'+CR+
  '        Console.WriteLine("  B: {0}", Path.GetFileName(bPath));',
  '        Console.WriteLine("Расхождение двух матриц (T43)");'+CR+
  '        Console.WriteLine("  A: {0}  (формат {1}{2})", Path.GetFileName(aPath), formatA,'+CR+
  '                          formatA == ResponseMatrix.FormatVersion ? "" : " — ПРЕЖНИЙ, прочитан только для сравнения");'+CR+
  '        Console.WriteLine("  B: {0}  (формат {1}{2})", Path.GetFileName(bPath), formatB,'+CR+
  '                          formatB == ResponseMatrix.FormatVersion ? "" : " — ПРЕЖНИЙ, прочитан только для сравнения");'),
 ('        if (corrupt)'+CR+
  '        {'+CR+
  '            Console.WriteLine("  ⛔ ОТПЕЧАТОК НЕ СХОДИТСЯ С ЗАПИСАННЫМ: тело файла правлено после записи");'+CR+
  '        }'+CR+
  '        Console.WriteLine();',
  '        if (corrupt)'+CR+
  '        {'+CR+
  '            Console.WriteLine("  ⛔ ОТПЕЧАТОК НЕ СХОДИТСЯ С ЗАПИСАННЫМ: тело файла правлено после записи");'+CR+
  '        }'+CR+
  CR+
  '        // (`AMBER46`) Блок Q_k — ОТДЕЛЬНО от тела: он в отпечаток не входит'+CR+
  '        // нарочно, и «тело побайтно, Q_k добавлен» — штатный итог сравнения'+CR+
  '        // матрицы формата 8 с её пересчётом форматом 9.'+CR+
  '        CompareAngular(a, b);'+CR+
  '        Console.WriteLine();'),
 ('    /// <summary>Отпечаток тела словами: пересчитанный, и сходится ли с записанным.</summary>'+CR+
  '    static string Fingerprint(ResponseMatrix m)',
  '    /// <summary>'+CR+
  '    /// Блок Q_k двух матриц (`AMBER46`): есть ли, и насколько расходится по'+CR+
  '    /// узлам — побитово, наибольшее |Δ| и в σ обеих (для двух зёрен одного'+CR+
  '    /// кода ожидание — в 3 σ; для того же зерна и числа историй — до бита).'+CR+
  '    /// </summary>'+CR+
  '    static void CompareAngular(ResponseMatrix a, ResponseMatrix b)'+CR+
  '    {'+CR+
  '        AngularAttenuation qa = a.AngularQk, qb = b.AngularQk;'+CR+
  '        Console.WriteLine("  блок Q_k угловой корреляции (формат 9):");'+CR+
  '        if (qa == null && qb == null)'+CR+
  '        {'+CR+
  '            Console.WriteLine("     нет ни у одной");'+CR+
  '            return;'+CR+
  '        }'+CR+
  CR+
  '        if (qa == null || qb == null)'+CR+
  '        {'+CR+
  '            AngularAttenuation q = qa ?? qb;'+CR+
  '            Console.WriteLine("     Q_k ДОБАВЛЕН у {0}: у {1} блока нет (прежний формат либо собрана не построителем), у {0} {2} узлов",'+CR+
  '                              qa == null ? "B" : "A", qa == null ? "A" : "B", q.Count);'+CR+
  '            int i662 = 0;'+CR+
  '            for (int i = 0; i < q.Count; i++)'+CR+
  '            {'+CR+
  '                if (Math.Abs(q.Energies[i] - 661.7) < Math.Abs(q.Energies[i662] - 661.7)) i662 = i;'+CR+
  '            }'+CR+
  CR+
  '            Console.WriteLine("     у узла {0:F1} кэВ: Q2 {1:F4} ± {2:F4}, Q4 {3:F4} ± {4:F4}, Q2T {5:F4}, Q4T {6:F4}, историй {7}",'+CR+
  '                              q.Energies[i662], q.Q2[i662], q.Q2Err[i662], q.Q4[i662], q.Q4Err[i662],'+CR+
  '                              q.Q2T[i662], q.Q4T[i662], q.Histories[i662]);'+CR+
  '            return;'+CR+
  '        }'+CR+
  CR+
  '        if (qa.Count != qb.Count)'+CR+
  '        {'+CR+
  '            Console.WriteLine("     узлов РАЗНОЕ число: A {0}, B {1}", qa.Count, qb.Count);'+CR+
  '            return;'+CR+
  '        }'+CR+
  CR+
  '        int bitwise = 0, worstNode = 0;'+CR+
  '        double worstAbs = 0.0, worstSigma = 0.0;'+CR+
  '        for (int i = 0; i < qa.Count; i++)'+CR+
  '        {'+CR+
  '            bool same = qa.Q2[i] == qb.Q2[i] && qa.Q4[i] == qb.Q4[i] && qa.Q2T[i] == qb.Q2T[i] && qa.Q4T[i] == qb.Q4T[i]'+CR+
  '                        && qa.Q2Err[i] == qb.Q2Err[i] && qa.Q4Err[i] == qb.Q4Err[i]'+CR+
  '                        && qa.PeakEff[i] == qb.PeakEff[i] && qa.TotalEff[i] == qb.TotalEff[i]'+CR+
  '                        && qa.Histories[i] == qb.Histories[i];'+CR+
  '            if (same) bitwise++;'+CR+
  '            double[] da = { qa.Q2[i] - qb.Q2[i], qa.Q4[i] - qb.Q4[i], qa.Q2T[i] - qb.Q2T[i], qa.Q4T[i] - qb.Q4T[i] };'+CR+
  '            double[] sa = { qa.Q2Err[i], qa.Q4Err[i], qa.Q2TErr[i], qa.Q4TErr[i] };'+CR+
  '            double[] sb = { qb.Q2Err[i], qb.Q4Err[i], qb.Q2TErr[i], qb.Q4TErr[i] };'+CR+
  '            for (int c = 0; c < 4; c++)'+CR+
  '            {'+CR+
  '                double d = Math.Abs(da[c]);'+CR+
  '                double s = Math.Sqrt(sa[c] * sa[c] + sb[c] * sb[c]);'+CR+
  '                double z = s > 0.0 ? d / s : (d > 0.0 ? double.PositiveInfinity : 0.0);'+CR+
  '                if (d > worstAbs) worstAbs = d;'+CR+
  '                if (z > worstSigma) { worstSigma = z; worstNode = i; }'+CR+
  '            }'+CR+
  '        }'+CR+
  CR+
  '        Console.WriteLine("     есть у обеих, узлов {0}: побитово {1}; наибольшее |Δ| {2:F5}, худший узел в σ {3} (узел {4}, {5:F1} кэВ)",'+CR+
  '                          qa.Count, bitwise, worstAbs,'+CR+
  '                          double.IsInfinity(worstSigma) ? "∞ (σ = 0)" : worstSigma.ToString("F2", CultureInfo.InvariantCulture),'+CR+
  '                          worstNode, qa.Energies[worstNode]);'+CR+
  '    }'+CR+
  CR+
  '    /// <summary>Отпечаток тела словами: пересчитанный, и сходится ли с записанным.</summary>'+CR+
  '    static string Fingerprint(ResponseMatrix m)'),
])
