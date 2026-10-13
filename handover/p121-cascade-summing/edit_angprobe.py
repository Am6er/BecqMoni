# -*- coding: utf-8 -*-
import io
p = r"C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\effmaker\probes\AngularProbe.cs"
s = io.open(p, encoding="utf-8", newline="").read()
def rep(old, new, count=1):
    global s
    old = old.replace("\n", "\r\n"); new = new.replace("\n", "\r\n")
    assert s.count(old) == count, (s.count(old), old[:60])
    s = s.replace(old, new)

# 1. ключ --old-order
rep("""            bool oldSign = false;
            foreach (string s in args)""", """            bool oldSign = false;
            bool oldOrder = false;
            foreach (string s in args)""")
rep("""                else if (s == "--old-sign")
                {
                    oldSign = true;
                }""", """                else if (s == "--old-sign")
                {
                    oldSign = true;
                }
                else if (s == "--old-order")
                {
                    oldOrder = true;
                }""")
rep("""            bad += Geant4Cascades(oldSign);

            Console.WriteLine();""", """            bad += Geant4Cascades(oldSign);
            bad += MultipoleOrder(oldOrder);

            Console.WriteLine();""")

# 2. строка таблицы Geant4 для 964+122 — правленная поставка
rep("""            new G4Case("Eu-152 964(E2+M1, δ=−9.3)+122",      62, 152,  964.1,  121.8,  0.3261, 0.0040,  0.0014, 0.0054),""",
"""            // ⛔ (П121, `AMBER58`) Правленная поставка PhotonEvaporation (403 → 304 при
            // δ ≠ 0; `patch_pe.py` в `handover/p121-cascade-summing/`), той же сборкой
            // g4cf, 5 млн распадов: A₂₂ = 0.00629 ± 0.00404, A₄₄ = 0.32004 ± 0.00529.
            // Штатная поставка той же сборкой в тот же день — 0.32611 ± 0.00400 /
            // 0.00145 ± 0.00539 (= П85): позиционное чтение Geant4, контроль в разделе 5.
            new G4Case("Eu-152 964(E2+M1, δ=−9.3)+122",      62, 152,  964.1,  121.8,  0.0063, 0.0040,  0.3200, 0.0053),""")

# 3. раздел 5 — перед Describe
section = '''        /// <summary>
        /// Штатный Geant4 на 964+122 (позиционное чтение кода 403): П85 и повтор
        /// П121 той же сборкой — 0.32611 ± 0.00400 / 0.00145 ± 0.00539.
        /// </summary>
        const double G4Positional22 = 0.3261, G4Positional22Sigma = 0.0040,
                     G4Positional44 = 0.0014, G4Positional44Sigma = 0.0054;

        /// <summary>
        /// A_k одного перехода ПОЗИЦИОННЫМ чтением кода: δ² на ВТОРОЙ компонент
        /// (`l` — первый, `lPrime` — второй), знак интерференции — как в
        /// приложении после П86. Это формула ДО правки П121 и формула Geant4
        /// (`G4PolarizationTransition::GammaTransFCoefficient`).
        /// </summary>
        static double AkPositional(int k, int l, int lPrime, double delta, double jOther, double jMiddle)
        {
            double pure = AngularCorrelation.F(k, l, l, jOther, jMiddle);
            if (delta == 0.0)
            {
                return pure;
            }

            double cross = AngularCorrelation.F(k, l, lPrime, jOther, jMiddle);
            double high = AngularCorrelation.F(k, lPrime, lPrime, jOther, jMiddle);
            return (pure + 2.0 * delta * cross + delta * delta * high) / (1.0 + delta * delta);
        }

        /// <summary>Порядок мультиполя по коду Geant4 (E1/M1 → 1, E2/M2 → 2, …); 0 — не гамма.</summary>
        static int OrderOfCode(int code)
        {
            return code >= 2 && code <= 16 ? code / 2 : 0;
        }

        /// <summary>
        /// Раздел 5 (`AMBER58`): δ² — на старшую мультипольность. Четыре плеча,
        /// см. шапку. Возвращает число провалов.
        /// </summary>
        static int MultipoleOrder(bool oldOrder)
        {
            Console.WriteLine();
            Console.WriteLine("5. Порядок мультиполей в коде смеси (AMBER58): δ² ложится на старшую");
            if (oldOrder)
            {
                Console.WriteLine("   ⚠ --old-order: в (в) судится реплика позиционного чтения — формула ДО правки П121; ждём КРАСНОГО");
            }

            Console.WriteLine();
            int bad = 0;

            // (а) одна смесь в обе стороны записи.
            AngularCorrelation.Coefficients a = AngularCorrelation.For(2, 2, 0, 403, -9.3, E2, 0.0);
            AngularCorrelation.Coefficients b = AngularCorrelation.For(2, 2, 0, 304, -9.3, E2, 0.0);
            bool same = a.A22 == b.A22 && a.A44 == b.A44;
            Console.WriteLine("   (а) 2→2→0, δ₁ = −9.3: код 403 → {0}; код 304 → {1}   {2}",
                              a, b, same ? "ok (побитово одно)" : "⛔ РАСХОЖДЕНИЕ");
            bad += same ? 0 : 1;

            // (б) при δ = 0 первый компонент — чистое приближение: 403 = E2, 304 = M1.
            AngularCorrelation.Coefficients c = AngularCorrelation.For(2, 2, 0, 403, 0.0, E2, 0.0);
            AngularCorrelation.Coefficients d = AngularCorrelation.For(2, 2, 0, 304, 0.0, E2, 0.0);
            AngularCorrelation.Coefficients pureE2 = AngularCorrelation.For(2, 2, 0, E2, 0.0, E2, 0.0);
            AngularCorrelation.Coefficients pureM1 = AngularCorrelation.For(2, 2, 0, M1, 0.0, E2, 0.0);
            bool okZero = c.A22 == pureE2.A22 && c.A44 == pureE2.A44
                          && d.A22 == pureM1.A22 && d.A44 == pureM1.A44
                          && (c.A22 != d.A22 || c.A44 != d.A44);
            Console.WriteLine("   (б) δ₁ = 0: код 403 → {0} (чистый E2 {1}); код 304 → {2} (чистый M1 {3})   {4}",
                              c, pureE2, d, pureM1, okZero ? "ok (первый компонент чистым)" : "⛔ РАСХОЖДЕНИЕ");
            bad += okZero ? 0 : 1;

            // (в) 964+122 по схеме базы: приложение против правленного Geant4,
            // реплика позиционного чтения — против штатного.
            AngularCorrelation.Scheme scheme = AngularCorrelation.SchemeOf(62, 152);
            AngularCorrelation.Transition first = scheme != null ? scheme.Find(964.1, 0.5) : null;
            AngularCorrelation.Transition second = scheme != null ? scheme.Find(121.8, 0.5) : null;
            double jStart, jMiddle, jEnd;
            if (first == null || second == null || first.ToSeq != second.FromSeq
                || !scheme.Jpi.TryGetValue(first.FromSeq, out jStart)
                || !scheme.Jpi.TryGetValue(first.ToSeq, out jMiddle)
                || !scheme.Jpi.TryGetValue(second.ToSeq, out jEnd))
            {
                Console.WriteLine("   (в) схемы Sm-152 или каскада 964+122 в базе нет — ⛔ ПРОВАЛ");
                return bad + 1;
            }

            jStart = Math.Abs(jStart); jMiddle = Math.Abs(jMiddle); jEnd = Math.Abs(jEnd);
            AngularCorrelation.Coefficients app = AngularCorrelation.For(
                jStart, jMiddle, jEnd, first.Multipolarity, first.Mixing, second.Multipolarity, second.Mixing);
            int l1 = OrderOfCode(first.Multipolarity / 100), l1Prime = OrderOfCode(first.Multipolarity % 100);
            int l2 = OrderOfCode(second.Multipolarity >= 100 ? second.Multipolarity / 100 : second.Multipolarity);
            double delta1 = ((l1 + l1Prime) % 2 != 0) ? -first.Mixing : first.Mixing;
            AngularCorrelation.Coefficients replica = new AngularCorrelation.Coefficients();
            replica.A22 = AkPositional(2, l1, l1Prime, delta1, jStart, jMiddle) * AkPositional(2, l2, l2, 0.0, jEnd, jMiddle);
            replica.A44 = AkPositional(4, l1, l1Prime, delta1, jStart, jMiddle) * AkPositional(4, l2, l2, 0.0, jEnd, jMiddle);
            G4Case patched = Geant4Table[7];
            AngularCorrelation.Coefficients judged = oldOrder ? replica : app;
            bool ok22 = Math.Abs(judged.A22 - patched.A22) <= G4Sigmas * patched.S22 + G4Floor;
            bool ok44 = Math.Abs(judged.A44 - patched.A44) <= G4Sigmas * patched.S44 + G4Floor;
            bool rep22 = Math.Abs(replica.A22 - G4Positional22) <= G4Sigmas * G4Positional22Sigma + G4Floor;
            bool rep44 = Math.Abs(replica.A44 - G4Positional44) <= G4Sigmas * G4Positional44Sigma + G4Floor;
            Console.WriteLine("   (в) Sm-152 964 (код {0}, δ = {1:F1}) + 122, спины {2}→{3}→{4}:",
                              first.Multipolarity, first.Mixing, jStart, jMiddle, jEnd);
            Console.WriteLine("       приложение          A22 = {0,7:F4}  A44 = {1,7:F4}  | Geant4 на правленной поставке {2:F4} ± {3:F4} / {4:F4} ± {5:F4}   {6}",
                              app.A22, app.A44, patched.A22, patched.S22, patched.A44, patched.S44,
                              (oldOrder ? "(судится реплика)" : (ok22 && ok44 ? "ok" : "⛔ РАСХОЖДЕНИЕ")));
            Console.WriteLine("       реплика позиционная A22 = {0,7:F4}  A44 = {1,7:F4}  | штатный Geant4 (П85, П121)     {2:F4} ± {3:F4} / {4:F4} ± {5:F4}   {6}",
                              replica.A22, replica.A44, G4Positional22, G4Positional22Sigma, G4Positional44, G4Positional44Sigma,
                              rep22 && rep44 ? "ok (штатный арбитр = позиционное чтение)" : "⛔ РАСХОЖДЕНИЕ");
            if (oldOrder)
            {
                Console.WriteLine("       реплика против правленной поставки: {0}", ok22 && ok44 ? "ok (⛔ контроль НЕ взят)" : "⛔ КРАСНОЕ — как и должно");
            }

            bad += (ok22 ? 0 : 1) + (ok44 ? 0 : 1) + (rep22 ? 0 : 1) + (rep44 ? 0 : 1);

            // (г) перебор schemedb.
            bad += ReversedCodes();
            return bad;
        }

        /// <summary>
        /// (г) Все переходы `g4_gamma` с кодом смеси, δ ≠ 0 и СТАРШЕЙ первой:
        /// число (ревизия П119 насчитала 753), и у скольких перестановка меняет
        /// A₂ или A₄ перехода (переход берётся снимающим: j_middle = J_from,
        /// j_other = J_to; без спина обоих уровней — «нечем считать»). Провал —
        /// если таких переходов нет вовсе (перебор мерил бы пустоту).
        /// </summary>
        static int ReversedCodes()
        {
            string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "schemedb.sqlite");
            if (!System.IO.File.Exists(path))
            {
                Console.WriteLine("   (г) рядом с пробой нет schemedb.sqlite — ⛔ ПРОВАЛ");
                return 1;
            }

            int reversed = 0, changed = 0, noSpin = 0, zeroMixingReversed = 0;
            var byCode = new SortedDictionary<int, int>();
            double worst = 0.0;
            string worstWhere = "";
            using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(
                "Data Source=" + path + ";Mode=ReadOnly;"))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText =
                        "select g.z, g.a, g.from_seq, g.to_seq, g.energy_ev, g.multipolarity, g.mixing_ratio,"
                        + " lf.jpi, lt.jpi from g4_gamma g"
                        + " left join g4_level lf on lf.z = g.z and lf.a = g.a and lf.seq = g.from_seq"
                        + " left join g4_level lt on lt.z = g.z and lt.a = g.a and lt.seq = g.to_seq"
                        + " where g.multipolarity >= 100";
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            int code = reader.GetInt32(5);
                            int hi = OrderOfCode(code / 100), lo = OrderOfCode(code % 100);
                            if (hi == 0 || lo == 0 || hi <= lo)
                            {
                                continue;
                            }

                            double mixing = reader.IsDBNull(6) ? 0.0 : reader.GetDouble(6);
                            if (mixing == 0.0)
                            {
                                zeroMixingReversed++;
                                continue;
                            }

                            reversed++;
                            int had;
                            byCode.TryGetValue(code, out had);
                            byCode[code] = had + 1;
                            if (reader.IsDBNull(7) || reader.IsDBNull(8))
                            {
                                noSpin++;
                                continue;
                            }

                            double jFrom = Math.Abs(reader.GetDouble(7)), jTo = Math.Abs(reader.GetDouble(8));
                            double d = mixing;
                            double a2Old = AkPositional(2, hi, lo, d, jTo, jFrom), a2New = AkPositional(2, lo, hi, d, jTo, jFrom);
                            double a4Old = AkPositional(4, hi, lo, d, jTo, jFrom), a4New = AkPositional(4, lo, hi, d, jTo, jFrom);
                            double diff = Math.Max(Math.Abs(a2Old - a2New), Math.Abs(a4Old - a4New));
                            if (diff > 1e-6)
                            {
                                changed++;
                                if (diff > worst)
                                {
                                    worst = diff;
                                    worstWhere = string.Format(CultureInfo.InvariantCulture,
                                        "Z={0} A={1} {2:F1} кэВ код {3} δ={4:G4}: A2 {5:F4}→{6:F4}, A4 {7:F4}→{8:F4}",
                                        reader.GetInt32(0), reader.GetInt32(1), reader.GetInt64(4) / 1000.0, code, d,
                                        a2Old, a2New, a4Old, a4New);
                                }
                            }
                        }
                    }
                }
            }

            var codes = new StringBuilder();
            foreach (KeyValuePair<int, int> entry in byCode)
            {
                codes.Append(entry.Key).Append(':').Append(entry.Value).Append(' ');
            }

            Console.WriteLine("   (г) schemedb: переходов смеси с δ ≠ 0 и старшей первой — {0} ({1}); с δ = 0 таких {2} (не трогаются);",
                              reversed, codes.ToString().TrimEnd(), zeroMixingReversed);
            Console.WriteLine("       перестановка меняет A₂/A₄ у {0}, спина обоих уровней нет у {1}; худшее: {2}",
                              changed, noSpin, worstWhere);
            bool ok = reversed > 0 && changed > 0;
            Console.WriteLine("       {0}", ok ? "ok (перебор не пуст)" : "⛔ ПРОВАЛ: перебор пуст");
            return ok ? 0 : 1;
        }

        static void Describe(string tag, AngularCorrelation.Transition t,
                             AngularCorrelation.Scheme scheme)'''
rep('''        static void Describe(string tag, AngularCorrelation.Transition t,
                             AngularCorrelation.Scheme scheme)''', section)

rep('''using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Globalization;
using System.Text;''', '''using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;''')
rep('''    ///     angularprobe [--nuclide=28:60] [--pair=1173.2:1332.5] [--old-sign]''',
    '''    ///     angularprobe [--nuclide=28:60] [--pair=1173.2:1332.5] [--old-sign] [--old-order]''')
io.open(p, "w", encoding="utf-8", newline="").write(s)
print("ok")
