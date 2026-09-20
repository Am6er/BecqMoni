# -*- coding: utf-8 -*-
def edit(p, pairs):
    b=open(p,'rb').read(); t=b.decode('utf-8'); n=0
    for old,new in pairs:
        c=t.count(old)
        assert c==1, (p, old[:70], c)
        t=t.replace(old,new); n+=1
    open(p,'wb').write(t.encode('utf-8')); print(p, n, 'replacements')
CR='\r\n'
p='D:/BqMoni_Claude/p87/wt/tools/effmaker/probes/CorpusMatrixProbe.cs'
edit(p, [
 ('            Console.WriteLine("   шум конт.: взвешенная {0:F2} %  {1}",'+CR+
  '                              matrix.ContinuumWeightedError,'+CR+
  '                              noisy ? string.Format(CultureInfo.InvariantCulture,'+CR+
  '                                                    "ВЫШЕ ПОРОГА {0:F1} %", noiseLimit)'+CR+
  '                                    : "тихо");',
  '            Console.WriteLine("   шум конт.: взвешенная {0:F2} %  {1}",'+CR+
  '                              matrix.ContinuumWeightedError,'+CR+
  '                              noisy ? string.Format(CultureInfo.InvariantCulture,'+CR+
  '                                                    "ВЫШЕ ПОРОГА {0:F1} %", noiseLimit)'+CR+
  '                                    : "тихо");'+CR+
  '            // (`AMBER46`, П87) Q_k угловой корреляции — из тех же историй,'+CR+
  '            // блок формата 9; печатается, чтобы было видно, что блок доехал'+CR+
  '            // до файла: узел у 662 кэВ и худший шум по узлам.'+CR+
  '            AngularAttenuation qk = matrix.AngularQk;'+CR+
  '            if (qk != null && qk.Count == matrix.Energies.Length)'+CR+
  '            {'+CR+
  '                int i662 = 0;'+CR+
  '                double worstQk = 0.0;'+CR+
  '                for (int i = 0; i < qk.Count; i++)'+CR+
  '                {'+CR+
  '                    if (Math.Abs(qk.Energies[i] - 661.7) < Math.Abs(qk.Energies[i662] - 661.7)) i662 = i;'+CR+
  '                    worstQk = Math.Max(worstQk, Math.Max(qk.Q2Err[i], qk.Q4Err[i]));'+CR+
  '                }'+CR+
  CR+
  '                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,'+CR+
  '                                  "   Q_k      : узлов {0}; у {1:F1} кэВ Q2 {2:F4} ± {3:F4}, Q4 {4:F4} ± {5:F4}, Q2T {6:F4}, Q4T {7:F4}; худший шум Q_k {8:F4}",'+CR+
  '                                  qk.Count, qk.Energies[i662], qk.Q2[i662], qk.Q2Err[i662], qk.Q4[i662], qk.Q4Err[i662],'+CR+
  '                                  qk.Q2T[i662], qk.Q4T[i662], worstQk));'+CR+
  '            }'+CR+
  '            else'+CR+
  '            {'+CR+
  '                Console.WriteLine("   Q_k      : ⛔ БЛОКА НЕТ — построитель не положил таблицу угловой корреляции");'+CR+
  '            }'),
 ('                    w.WriteLine("node,energy_kev,histories,error_pct,seconds_wall,dropped,scored,dropped_pct,dropped_scat,scat_pct");',
  '                    // (`AMBER46`) Q_k узла — те же числа, что в блоке ANGK файла.'+CR+
  '                    w.WriteLine("node,energy_kev,histories,error_pct,seconds_wall,dropped,scored,dropped_pct,dropped_scat,scat_pct,q2,q4,dq2,dq4,q2t,q4t,dq2t,dq4t,eps_peak,eps_total,qk_histories");'),
 ('                        w.WriteLine(string.Format(CultureInfo.InvariantCulture,'+CR+
  '                            "{0},{1:F3},{2},{3:F3},{4:F3},{5},{6},{7:F3},{8},{9:F3}", i, matrix.Energies[i],'+CR+
  '                            matrix.NodeHistories[i],'+CR+
  '                            matrix.NodeErrors != null ? matrix.NodeErrors[i] : 0.0,'+CR+
  '                            matrix.NodeSeconds != null ? matrix.NodeSeconds[i] : 0.0,'+CR+
  '                            d, sc, d + sc > 0L ? 100.0 * d / (d + sc) : 0.0,'+CR+
  '                            ds, d + sc > 0L ? 100.0 * ds / (d + sc) : 0.0));',
  '                        AngularAttenuation q = matrix.AngularQk;'+CR+
  '                        bool hasQ = q != null && q.Count == matrix.Energies.Length;'+CR+
  '                        w.WriteLine(string.Format(CultureInfo.InvariantCulture,'+CR+
  '                            "{0},{1:F3},{2},{3:F3},{4:F3},{5},{6},{7:F3},{8},{9:F3},{10:R},{11:R},{12:R},{13:R},{14:R},{15:R},{16:R},{17:R},{18:R},{19:R},{20}",'+CR+
  '                            i, matrix.Energies[i],'+CR+
  '                            matrix.NodeHistories[i],'+CR+
  '                            matrix.NodeErrors != null ? matrix.NodeErrors[i] : 0.0,'+CR+
  '                            matrix.NodeSeconds != null ? matrix.NodeSeconds[i] : 0.0,'+CR+
  '                            d, sc, d + sc > 0L ? 100.0 * d / (d + sc) : 0.0,'+CR+
  '                            ds, d + sc > 0L ? 100.0 * ds / (d + sc) : 0.0,'+CR+
  '                            hasQ ? q.Q2[i] : 0.0, hasQ ? q.Q4[i] : 0.0, hasQ ? q.Q2Err[i] : 0.0, hasQ ? q.Q4Err[i] : 0.0,'+CR+
  '                            hasQ ? q.Q2T[i] : 0.0, hasQ ? q.Q4T[i] : 0.0, hasQ ? q.Q2TErr[i] : 0.0, hasQ ? q.Q4TErr[i] : 0.0,'+CR+
  '                            hasQ ? q.PeakEff[i] : 0.0, hasQ ? q.TotalEff[i] : 0.0, hasQ ? q.Histories[i] : 0L));'),
])
