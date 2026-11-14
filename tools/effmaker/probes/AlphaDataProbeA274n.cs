using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AlphaDataProbeA274n
{
    /// <summary>
    /// (`AMBER274` Э3, полоса `a274n` 14.11.2026) Приёмка данных альф ряда Ac-227 — кодом приложения.
    ///
    ///   --mode=alpha   альфа-линии ряда, как их видит слой кристалла (`CrystalOwnActivityBuilder.AlphaLines`,
    ///                  `nucdb.sqlite` рядом с пробой): Σ выходов, число линий, у скольких найден каскад; доля β⁻
    ///                  головы (`FsaBetaLibrary.Branches`) и альф на распад головы с весами ряда слоя
    ///                  (`ActiniumSeries`: Th-227 — β⁻ Ac-227, Ra-223 / Rn-219 / Po-215 — 1, альфы Ac-227 и Bi-211 —
    ///                  своими строками). Гоняется на каталоге проб с прежней и с новой базой.
    ///   --mode=qf      таблица гашения альф `matdb.scint_alpha_quenching` (`ScintAlphaQuenching`): чтение обратно,
    ///                  QF на энергиях ряда против закона 0.3785 + 0.019·(E − 6 МэВ); строка «по веществу» (её
    ///                  нет — is_default 0); отпечаток склада `MaterialDatabase.SimulatorDataFingerprint` двух файлов
    ///                  (`--a=`, `--b=`) — обязан совпасть; положительный контроль — `--spoiled=` (QF другой — чтение
    ///                  обязано дать другое), `--old=` (база без таблицы — пусто), `--fp-spoiled=` (тронута таблица
    ///                  переноса — отпечаток обязан смениться).
    ///
    ///     AlphaDataProbeA274n --mode=alpha
    ///     AlphaDataProbeA274n --mode=qf --a=<matdb до> --b=<matdb после> --spoiled=<копия> --old=<без таблицы> --fp-spoiled=<копия>
    ///     AlphaDataProbeA274n --mode=beta [--nuc=207TL]   (третий круг) ветви β нуклида глазами читателя
    ///                                      (`FsaBetaLibrary.Branches`: все открытые свойства ветви и хеш строки события
    ///                                      `EventSpec`) и Σ выходов — для сравнения базы с заменённой строкой (отсев) и
    ///                                      базы, где строки нет вовсе: форма обязана совпасть до бита
    ///     AlphaDataProbeA274n --mode=cfg   поле `EfficiencyConfigData.AlphaQuenchingSource` (второй круг): пустое в XML не
    ///                                      пишется (XML побайтно как без поля), заполненное — пишется и читается обратно,
    ///                                      `Copy()` и `Duplicate()` его несут; источник находит строку таблицы
    ///
    /// Код 0 — всё сошлось (у `qf` — и все контроли поймали подмену); 1 — расхождение; 2 — неверный вызов.
    /// </summary>
    internal static class Program
    {
        static readonly string[] Series = { "227AC", "227TH", "223RA", "219RN", "215PO", "211BI", "211PO" };

        static int Main(string[] args)
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            var opt = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string a in args)
            {
                int eq = a.IndexOf('=');
                if (!a.StartsWith("--", StringComparison.Ordinal) || eq < 0)
                {
                    Console.Error.WriteLine("ключ не понят: " + a);
                    return 2;
                }

                opt[a.Substring(2, eq - 2)] = a.Substring(eq + 1);
            }

            string mode;
            if (!opt.TryGetValue("mode", out mode))
            {
                Console.Error.WriteLine("нужен --mode=alpha|qf");
                return 2;
            }

            if (mode == "alpha")
            {
                return Alpha();
            }

            if (mode == "qf")
            {
                return Qf(opt);
            }

            if (mode == "cfg")
            {
                return Cfg();
            }

            if (mode == "beta")
            {
                string nuc;
                return Beta(opt.TryGetValue("nuc", out nuc) ? nuc : "207TL");
            }

            Console.Error.WriteLine("--mode: alpha|qf|cfg|beta");
            return 2;
        }

        static string F(double v, string fmt)
        {
            return v.ToString(fmt, CultureInfo.InvariantCulture);
        }

        static string Xml(BecquerelMonitor.EfficiencyConfigData data)
        {
            var serializer = new System.Xml.Serialization.XmlSerializer(typeof(BecquerelMonitor.EfficiencyConfigData));
            using (var writer = new System.IO.StringWriter(CultureInfo.InvariantCulture))
            {
                serializer.Serialize(writer, data);
                return writer.ToString();
            }
        }

        static BecquerelMonitor.EfficiencyConfigData FromXml(string xml)
        {
            var serializer = new System.Xml.Serialization.XmlSerializer(typeof(BecquerelMonitor.EfficiencyConfigData));
            using (var reader = new System.IO.StringReader(xml))
            {
                return (BecquerelMonitor.EfficiencyConfigData)serializer.Deserialize(reader);
            }
        }

        static int Beta(string nucid)
        {
            double sum = 0.0;
            int n = 0;
            foreach (FsaBeta beta in FsaBetaLibrary.Branches(nucid))
            {
                if (beta == null)
                {
                    continue;
                }

                var sb = new StringBuilder();
                foreach (System.Reflection.PropertyInfo prop in typeof(FsaBeta).GetProperties())
                {
                    if (prop.GetIndexParameters().Length > 0)
                    {
                        continue;
                    }

                    object v = prop.GetValue(beta, null);
                    string text = v is double ? ((double)v).ToString("R", CultureInfo.InvariantCulture)
                                  : v is System.Collections.IEnumerable && !(v is string) ? "[" + Seq((System.Collections.IEnumerable)v) + "]"
                                  : Convert.ToString(v, CultureInfo.InvariantCulture);
                    sb.Append(prop.Name).Append('=').Append(text).Append("; ");
                }

                string why;
                ElectronBranchSpec spec = beta.IsLine || beta.Positron ? null : FsaBetaLibrary.EventSpec(beta, out why);
                sb.Append("EventSpec=").Append(spec == null ? "—" : spec.Hash);
                Console.WriteLine(nucid + " ветвь " + n.ToString(CultureInfo.InvariantCulture) + ": " + sb);
                if (!beta.IsLine && !beta.Positron && beta.Intensity > 0.0)
                {
                    sum += beta.Intensity;
                }

                n++;
            }

            Console.WriteLine(nucid + ": ветвей " + n.ToString(CultureInfo.InvariantCulture) + ", Σβ⁻ " + F(sum, "R") + " %");
            return 0;
        }

        static string Seq(System.Collections.IEnumerable items)
        {
            var sb = new StringBuilder();
            int k = 0;
            foreach (object o in items)
            {
                if (k++ > 0)
                {
                    sb.Append(',');
                }

                sb.Append(o is double ? ((double)o).ToString("R", CultureInfo.InvariantCulture) : Convert.ToString(o, CultureInfo.InvariantCulture));
            }

            return sb.ToString();
        }

        static int Cfg()
        {
            int bad = 0;
            var data = new BecquerelMonitor.EfficiencyConfigData("проба a274n");
            data.LastUpdated = new DateTime(2026, 11, 14, 0, 0, 0, DateTimeKind.Utc);
            string empty = Xml(data);
            bool noElement = empty.IndexOf("AlphaQuenchingSource", StringComparison.Ordinal) < 0;
            Console.WriteLine("пустое поле: элемента в XML " + (noElement ? "нет (прежний XML)" : "ЕСТЬ"));
            if (!noElement)
            {
                bad++;
            }

            BecquerelMonitor.EfficiencyConfigData back0 = FromXml(empty);
            Console.WriteLine("прежний XML читается: поле «" + back0.AlphaQuenchingSource + "»" + (back0.AlphaQuenchingSource == "" ? " (пусто — верно)" : " — НЕ ПУСТО"));
            if (back0.AlphaQuenchingSource != "")
            {
                bad++;
            }

            data.AlphaQuenchingSource = "a274a_bg_vertices";
            string filled = Xml(data);
            BecquerelMonitor.EfficiencyConfigData back = FromXml(filled);
            bool element = filled.IndexOf("<AlphaQuenchingSource>a274a_bg_vertices</AlphaQuenchingSource>", StringComparison.Ordinal) >= 0;
            Console.WriteLine("заполненное: элемент " + (element ? "есть" : "НЕТ") + ", прочитано обратно «" + back.AlphaQuenchingSource + "»");
            if (!element || back.AlphaQuenchingSource != "a274a_bg_vertices")
            {
                bad++;
            }

            string copy = data.Copy().AlphaQuenchingSource;
            string dup = data.Duplicate("копия").AlphaQuenchingSource;
            Console.WriteLine("Copy() «" + copy + "», Duplicate() «" + dup + "»");
            if (copy != "a274a_bg_vertices" || dup != "a274a_bg_vertices")
            {
                bad++;
            }

            ScintAlphaQuenching.Law law = ScintAlphaQuenching.Find("LaBr3:Ce", back.AlphaQuenchingSource);
            ScintAlphaQuenching.Law none = ScintAlphaQuenching.Find("LaBr3:Ce", back0.AlphaQuenchingSource);
            Console.WriteLine("источник прибора → строка таблицы: " + (law == null ? "НЕТ" : law.Source + ", QF(6 МэВ) " + F(law.QfAtBuilderReference, "R"))
                              + "; пустое поле → " + (none == null ? "строки нет, альф нет" : "ЕСТЬ " + none.Source));
            if (law == null || none != null)
            {
                bad++;
            }

            Console.WriteLine(bad == 0 ? "ИТОГ: СОШЛОСЬ" : "ИТОГ: РАСХОЖДЕНИЙ " + bad.ToString(CultureInfo.InvariantCulture));
            return bad == 0 ? 0 : 1;
        }

        static double BetaShare(string nucid)
        {
            double sum = 0.0;
            foreach (FsaBeta beta in FsaBetaLibrary.Branches(nucid))
            {
                if (beta != null && !beta.IsLine && !beta.Positron && beta.Intensity > 0.0)
                {
                    sum += beta.Intensity;
                }
            }

            return sum;
        }

        static int Alpha()
        {
            var sum = new Dictionary<string, double>(StringComparer.Ordinal);
            Console.WriteLine("нуклид  линий  Σ выходов α, %  с каскадом  без каскада  β⁻ (FsaBetaLibrary), %");
            foreach (string n in Series)
            {
                List<CrystalOwnActivityBuilder.AlphaLine> lines = CrystalOwnActivityBuilder.AlphaLines(n);
                double s = 0.0;
                int withCascade = 0;
                foreach (CrystalOwnActivityBuilder.AlphaLine line in lines)
                {
                    s += line.Intensity;
                    if (line.Cascade != null)
                    {
                        withCascade++;
                    }
                }

                foreach (CrystalOwnActivityBuilder.AlphaLine line in lines)
                {
                    if (line.Cascade == null && line.LevelKev > 1.0)
                    {
                        Console.WriteLine("    без каскада: " + n + " α " + F(line.EnergyKev, "0.00") + " кэВ " + F(line.Intensity, "0.#####")
                                          + " %, уровень " + F(line.LevelKev, "0.0") + " кэВ — " + (line.NoCascade ?? "?"));
                    }
                }

                sum[n] = s;
                Console.WriteLine(n.PadRight(7) + " " + lines.Count.ToString(CultureInfo.InvariantCulture).PadLeft(5) + "  "
                                  + F(s, "0.00000").PadLeft(14) + "  " + withCascade.ToString(CultureInfo.InvariantCulture).PadLeft(10)
                                  + "  " + (lines.Count - withCascade).ToString(CultureInfo.InvariantCulture).PadLeft(11)
                                  + "  " + F(BetaShare(n), "0.00000"));
            }

            Console.WriteLine("β⁻ Pb-211, %: " + F(BetaShare("211PB"), "0.00000") + "; β⁻ Tl-207, %: " + F(BetaShare("207TL"), "0.00000"));
            double head = BetaShare("227AC") / 100.0;
            double perDecay = sum["227AC"] / 100.0 + head * sum["227TH"] / 100.0 + sum["223RA"] / 100.0 + sum["219RN"] / 100.0
                              + sum["215PO"] / 100.0 + sum["211BI"] / 100.0;
            Console.WriteLine("альф на распад головы (веса ряда слоя, без Po-211): " + F(perDecay, "0.000000")
                              + "  (Th-227 с весом β⁻ Ac-227 = " + F(head, "0.000000") + ")");
            Console.WriteLine("Σ β⁻ + α Ac-227, %: " + F(BetaShare("227AC") + sum["227AC"], "0.00000"));
            return 0;
        }

        static int Qf(Dictionary<string, string> opt)
        {
            int bad = 0;
            string a, b, spoiled, old, fpSpoiled;
            opt.TryGetValue("a", out a);
            opt.TryGetValue("b", out b);
            opt.TryGetValue("spoiled", out spoiled);
            opt.TryGetValue("old", out old);
            opt.TryGetValue("fp-spoiled", out fpSpoiled);
            if (b == null)
            {
                Console.Error.WriteLine("нужен --b=<matdb после>");
                return 2;
            }

            ScintAlphaQuenching.Reload(b);
            IList<ScintAlphaQuenching.Law> all = ScintAlphaQuenching.All();
            Console.WriteLine("строк в " + ScintAlphaQuenching.Table + ": " + all.Count.ToString(CultureInfo.InvariantCulture));
            foreach (ScintAlphaQuenching.Law law in all)
            {
                Console.WriteLine("  " + law.Material + " / " + law.Source + " по умолчанию " + (law.IsDefault ? "1" : "0")
                                  + "; QF(" + F(law.ReferenceKev, "0") + ") = " + F(law.QfAtReference, "R") + ", наклон "
                                  + F(law.SlopePerMev, "R") + "/МэВ; охват " + F(law.MeasuredMinKev, "0") + "…" + F(law.MeasuredMaxKev, "0") + " кэВ");
            }

            ScintAlphaQuenching.Law byMaterial = ScintAlphaQuenching.Find("LaBr3:Ce", null);
            Console.WriteLine("по веществу LaBr3:Ce (без источника): " + (byMaterial == null ? "нет (верно: is_default 0)" : "ЕСТЬ — " + byMaterial.Source));
            if (byMaterial != null)
            {
                bad++;
            }

            ScintAlphaQuenching.Law m = ScintAlphaQuenching.Find("LaBr3:Ce", "a274a_bg_vertices");
            if (m == null)
            {
                Console.WriteLine("⛔ строки LaBr3:Ce / a274a_bg_vertices нет");
                return 1;
            }

            double maxDiff = 0.0;
            foreach (double e in new[] { 4953.23, 6038.01, 6622.4, 6819.2, 7386.1, 7450.2 })
            {
                double want = 0.3785 + 0.019 * (e - 6000.0) / 1000.0;
                double got = m.At(e);
                maxDiff = Math.Max(maxDiff, Math.Abs(got - want));
                Console.WriteLine("  E " + F(e, "0.00") + " кэВ: QF " + F(got, "0.000000") + " (закон " + F(want, "0.000000") + ")"
                                  + (m.Measured(e) ? "" : " — вне охвата замера"));
            }

            Console.WriteLine("QF на опорной рычага слоя (" + F(CrystalOwnActivityBuilder.AlphaQuenchingReferenceKev, "0") + " кэВ): "
                              + F(m.QfAtBuilderReference, "R") + ", наклон " + F(m.SlopePerMev, "R"));
            Console.WriteLine("наибольшее расхождение с законом: " + F(maxDiff, "E2"));
            if (maxDiff > 1e-12)
            {
                bad++;
            }

            if (spoiled != null)
            {
                ScintAlphaQuenching.Reload(spoiled);
                ScintAlphaQuenching.Law s = ScintAlphaQuenching.Find("LaBr3:Ce", "a274a_bg_vertices");
                bool caught = s == null || Math.Abs(s.At(6000.0) - m.At(6000.0)) > 1e-9;
                Console.WriteLine("контроль: испорченная копия (QF другой) — " + (caught ? "ПОЙМАНО" : "НЕ ПОЙМАНО")
                                  + (s != null ? " (QF(6 МэВ) " + F(s.At(6000.0), "R") + ")" : " (строки нет)"));
                if (!caught)
                {
                    bad++;
                }
            }

            if (old != null)
            {
                ScintAlphaQuenching.Reload(old);
                int n = ScintAlphaQuenching.All().Count;
                bool ok = n == 0 && ScintAlphaQuenching.Find("LaBr3:Ce", "a274a_bg_vertices") == null;
                Console.WriteLine("контроль: база без таблицы — строк " + n.ToString(CultureInfo.InvariantCulture) + ", отказов чтения "
                                  + FsaDatabaseFailures.ProcessCount.ToString(CultureInfo.InvariantCulture) + " — " + (ok ? "альф нет, как прежде" : "НЕ ТАК"));
                if (!ok || FsaDatabaseFailures.ProcessCount != 0)
                {
                    bad++;
                }
            }

            ScintAlphaQuenching.Reload(null);
            if (a != null)
            {
                string fa = MaterialDatabase.SimulatorDataFingerprint(a);
                string fb = MaterialDatabase.SimulatorDataFingerprint(b);
                Console.WriteLine("отпечаток склада mdb= до:    " + fa);
                Console.WriteLine("отпечаток склада mdb= после: " + fb + (fa == fb ? " — СОВПАЛ" : " — РАЗОШЁЛСЯ"));
                if (fa != fb)
                {
                    bad++;
                }

                if (fpSpoiled != null)
                {
                    string fs = MaterialDatabase.SimulatorDataFingerprint(fpSpoiled);
                    bool caught = fs != fb;
                    Console.WriteLine("контроль: копия с тронутой таблицей переноса — " + fs + (caught ? " — ПОЙМАНО" : " — НЕ ПОЙМАНО"));
                    if (!caught)
                    {
                        bad++;
                    }
                }
            }

            Console.WriteLine(bad == 0 ? "ИТОГ: СОШЛОСЬ" : "ИТОГ: РАСХОЖДЕНИЙ " + bad.ToString(CultureInfo.InvariantCulture));
            return bad == 0 ? 0 : 1;
        }
    }
}
