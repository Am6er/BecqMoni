using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml.Serialization;

namespace LabelsFromNucdbProbeA306
{
    /// <summary>
    /// (`AMBER306`, 06.11.2026) МЕТКИ ПИКОВ ВЫБИРАЮТ НУКЛИДЫ, ИЗЛУЧЕНИЯ И ВЫХОДЫ — ИЗ `nucdb`.
    ///
    ///     LabelsFromNucdbProbeA306 [--spectrum=&lt;файл&gt;] [--set=&lt;сет&gt;] [--element=&lt;символ&gt;] [--timeout=300]
    ///
    /// Постановка Amber 05.11.2026 вопросником, дословно: «Линии и выходы из nucdb (Рекомендую)».
    ///
    /// Разделы, все обязательны (образ собирается в памяти; библиотека конфигурации с диска не правится):
    ///   1. КАЖДЫЙ НУКЛИД БИБЛИОТЕКИ каталога проб, у которого в базе есть линии: библиотека нарочно
    ///      врёт — выход × 0.37 + 1, лишняя линия руками. Образ обязан совпасть ПОБИТОВО с тем, что
    ///      собирает объявленный состав `FsaSampleLibrary.Build`, и не содержать лишней линии.
    ///   2. Независимая сверка прямым запросом к `nucdb`: сильнейшая гамма образа — выход базы; все
    ///      строки рентгена базы выше пола — в образе; число нуклидов с рентгеном печатается.
    ///   3. Нуклида в базе нет: образ из линий, вбитых руками, вид Single; битые числа не идут; вылет и
    ///      аннигиляция — от этих линий. Линия руками к нуклиду из базы — в образ не идёт и названа.
    ///   4. Подпись элемента — образ из `matdb` (линии руками не читаются), флажок рентгена снимает его;
    ///      безымянная подпись — как в файле.
    ///   5. Хвост «(ряд)» — свой распад; два написания — один столбец; вылет, пустая подпись, пустой состав.
    ///   6. Путь окна: `FsaAnalysisSession` по меткам против «Из NucBase» на спектре, где состав один —
    ///      результат побитово один (`--spectrum=`, `--set=`). Без ключа — раздел пропущен и это сказано.
    ///
    /// Положительный контроль: мутант «снова линии библиотеки» — код 1 (разделы 1 и 2); мутант сеанса
    /// «снова без обстановки спектра» — код 1 (раздел 6).
    /// Код: 0 — сошлось; 1 — расхождение; 2 — нечем мерить.
    /// </summary>
    static class Program
    {
        static int bad;

        [STAThread]
        static int Main(string[] args)
        {
            FsaTuningReport.Snapshot();
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            string spectrum = null, setName = null, element = "Pb";
            int timeoutS = 300;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrum = a.Substring(11);
                else if (a.StartsWith("--set=", StringComparison.Ordinal)) setName = a.Substring(6);
                else if (a.StartsWith("--element=", StringComparison.Ordinal)) element = a.Substring(10);
                else if (a.StartsWith("--timeout=", StringComparison.Ordinal)) timeoutS = int.Parse(a.Substring(10), CultureInfo.InvariantCulture);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();
            List<NuclideDefinition> library = nuclides.NuclideDefinitions.Where(d => d != null).ToList();
            List<string> names = library.Where(d => !d.IsElementXray && d.Energy > 0.0)
                                        .Select(d => d.NuclideName)
                                        .Where(n => FsaSampleLibrary.DecayLines(FsaSampleLibrary.NucidOf(n), new FsaSampleLibrary.Report()).Count > 0)
                                        .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            Console.WriteLine("SETUP\tзаписей библиотеки {0}, нуклидов с линиями в базе {1}", library.Count, names.Count);
            if (names.Count == 0)
            {
                Console.Error.WriteLine("МЕРИТЬ НЕЧЕМ: в библиотеке каталога проб нет нуклидов базы");
                return 2;
            }

            EveryNuclide(library, names);
            DirectSql(library, names);
            Typed(names[0]);
            Elements(element);
            Odds(library, names);
            if (spectrum != null)
            {
                SessionPath(spectrum, setName, nuclides, timeoutS);
            }
            else
            {
                Console.WriteLine();
                Console.WriteLine("=== 6. путь окна: пропущен, нет --spectrum= ===");
            }

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "НЕ СОШЛОСЬ: " + bad.ToString(CultureInfo.InvariantCulture));
            Console.Out.Flush();
            return bad == 0 ? 0 : 1;
        }

        static void Say(bool ok, string text)
        {
            Console.WriteLine((ok ? "  ok  " : "  ⛔  ") + text);
            if (!ok) bad++;
        }

        // ---------------------------------------------------------------
        // Вход: подписанный пик и библиотека «как у человека»
        // ---------------------------------------------------------------

        static NuclideDefinition Line(string name, double energy, double intensity, string chain = "")
        {
            return new NuclideDefinition { Name = name, Energy = energy, Intencity = intensity, Chain = chain };
        }

        static Peak Labelled(NuclideDefinition definition)
        {
            return new Peak { Nuclide = definition };
        }

        static List<FsaComponent> Labels(IEnumerable<Peak> peaks, List<NuclideDefinition> defs, FsaSampleSpec spec,
                                         out FsaLibrary.LabelReport report)
        {
            return FsaLibrary.BuildFromLabels(peaks, defs, spec, out report);
        }

        static string Bits(double value)
        {
            return BitConverter.DoubleToInt64Bits(value).ToString("X16", CultureInfo.InvariantCulture);
        }

        /// <summary>Образ построчно: всё, что читает анализатор. Сравнивается как текст, побитово.</summary>
        static string Dump(List<FsaComponent> library)
        {
            var text = new StringBuilder();
            foreach (FsaComponent c in library)
            {
                text.Append(c.Name).Append('|').Append(c.Kind).Append('|').Append(c.EscapeParent).Append('|')
                    .Append(c.DecayChainRoot).Append('|').Append(Bits(c.TotalYieldPercent)).Append('|')
                    .Append(c.Betas.Count).Append('\n');
                foreach (FsaLine line in c.Lines)
                {
                    text.Append("  ").Append(line.Nuclide).Append(' ').Append(Bits(line.Energy)).Append(' ')
                        .Append(Bits(line.Intensity)).Append(' ').Append(Bits(line.AnnihilationIntensity)).Append('\n');
                }
            }

            return text.ToString();
        }

        // ---------------------------------------------------------------
        // 1. Каждый нуклид библиотеки
        // ---------------------------------------------------------------

        static void EveryNuclide(List<NuclideDefinition> library, List<string> names)
        {
            Console.WriteLine();
            Console.WriteLine("=== 1. каждый нуклид библиотеки: образ из базы, библиотека нарочно врёт ===");
            int same = 0, hand = 0;
            var wrong = new List<string>();
            foreach (string name in names)
            {
                var defs = new List<NuclideDefinition>();
                double top = 0.0;
                foreach (NuclideDefinition d in library)
                {
                    if (string.Equals(d.NuclideName, name, StringComparison.OrdinalIgnoreCase))
                    {
                        defs.Add(Line(name, d.Energy, d.Intencity * 0.37 + 1.0));
                        top = Math.Max(top, d.Energy);
                    }
                }

                double extra = top + 7.77;
                defs.Add(Line(name, extra, 50.0));
                FsaLibrary.LabelReport report;
                string got = Dump(Labels(new[] { Labelled(defs[0]) }, defs, new FsaSampleSpec(), out report));
                string nucid = FsaSampleLibrary.NucidOf(name);
                var declared = new FsaSampleSpec();
                declared.Nuclides.Add(nucid);
                string want = Dump(FsaSampleLibrary.Build(declared));
                if (got == want) same++;
                else wrong.Add(name);
                if (report.IgnoredTypedLines.Any(l => l.IndexOf(extra.ToString("0.###", CultureInfo.InvariantCulture), StringComparison.Ordinal) >= 0)) hand++;
            }

            Say(wrong.Count == 0, string.Format(CultureInfo.InvariantCulture,
                "побитово как объявленный состав: {0} из {1}{2}", same, names.Count,
                wrong.Count > 0 ? "; не совпали: " + string.Join(", ", wrong.Take(12)) : ""));
            Say(hand == names.Count, string.Format(CultureInfo.InvariantCulture,
                "лишняя линия руками названа в отчёте сборки: {0} из {1}", hand, names.Count));
        }

        // ---------------------------------------------------------------
        // 2. Прямой запрос к базе, мимо кода сборки
        // ---------------------------------------------------------------

        static void DirectSql(List<NuclideDefinition> library, List<string> names)
        {
            Console.WriteLine();
            Console.WriteLine("=== 2. прямой запрос к nucdb: сильнейшая гамма и весь рентген в образе ===");
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nucdb.sqlite");
            if (!File.Exists(path))
            {
                Say(false, "нет базы рядом с пробой: " + path);
                return;
            }

            int gammaOk = 0, gammaAll = 0, withX = 0, xLines = 0, xMissing = 0;
            double floor = new FsaSampleSpec().LineFloorKev, max = new FsaSampleSpec().MaxEnergyKev;
            var wrong = new List<string>();
            using (var connection = new SqliteConnection("Data Source=" + path + ";Mode=ReadOnly"))
            {
                connection.Open();
                foreach (string name in names)
                {
                    string nucid = FsaSampleLibrary.NucidOf(name);
                    NuclideDefinition def = Line(name, 100.0, 1.0);
                    FsaLibrary.LabelReport report;
                    List<FsaComponent> built = Labels(new[] { Labelled(def) }, new List<NuclideDefinition> { def }, new FsaSampleSpec(), out report);
                    FsaComponent image = built.FirstOrDefault(c => c.Kind == FsaComponentKind.Single);
                    if (image == null)
                    {
                        wrong.Add(name + ": образа нет");
                        continue;
                    }

                    using (SqliteCommand command = connection.CreateCommand())
                    {
                        command.CommandText = "select cast(energy_num as real), cast(intensity_num as real) from decay_radiations"
                            + " where parent_nucid = $n and type_a = 'G' and energy_num not null and cast(intensity_num as real) > 0"
                            + " and cast(energy_num as real) >= $lo and cast(energy_num as real) <= $hi"
                            + " and (intensity_unc is null or upper(trim(intensity_unc)) not in ('LT','LE'))" + DecayParentRule.LevelClause
                            + " order by cast(intensity_num as real) desc limit 1";
                        command.Parameters.AddWithValue("$n", nucid);
                        command.Parameters.AddWithValue("$lo", floor);
                        command.Parameters.AddWithValue("$hi", max);
                        using (SqliteDataReader reader = command.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                gammaAll++;
                                double e = reader.GetDouble(0), i = reader.GetDouble(1);
                                FsaLine near = image.Lines.OrderBy(l => Math.Abs(l.Energy - e)).FirstOrDefault();
                                // Слияние каналов (`S161`) выход только прибавляет; ниже базы быть не может.
                                if (near != null && Math.Abs(near.Energy - e) < 0.05 && near.Intensity >= i * 0.999 && near.Intensity <= i * 2.0) gammaOk++;
                                else wrong.Add(string.Format(CultureInfo.InvariantCulture, "{0}: γ {1} / {2} %, в образе {3} / {4} %", name, e, i, near != null ? near.Energy : double.NaN, near != null ? near.Intensity : double.NaN));
                            }
                        }
                    }

                    using (SqliteCommand command = connection.CreateCommand())
                    {
                        command.CommandText = "select cast(energy_num as real) from decay_radiations"
                            + " where parent_nucid = $n and type_a = 'X' and type_c not in ('KB', 'L') and energy_num not null and cast(intensity_num as real) > 0"
                            + " and cast(energy_num as real) >= $lo and cast(energy_num as real) <= $hi and (intensity_unc is null or upper(trim(intensity_unc)) not in ('LT','LE'))" + DecayParentRule.LevelClause;
                        command.Parameters.AddWithValue("$n", nucid);
                        command.Parameters.AddWithValue("$lo", floor);
                        command.Parameters.AddWithValue("$hi", max);
                        using (SqliteDataReader reader = command.ExecuteReader())
                        {
                            int here = 0, lost = 0;
                            while (reader.Read())
                            {
                                here++;
                                double e = reader.GetDouble(0);
                                // Итоги серий (`KB`, `L`) в запрос не взяты: при разложении их заменяют подлинии
                                // (`KSeriesRule`). Доехавшей считается линия в пределах 1 кэВ — разнос внутри серии.
                                if (!image.Lines.Any(l => Math.Abs(l.Energy - e) < 1.0)) lost++;
                            }

                            if (here > 0) withX++;
                            xLines += here;
                            xMissing += lost;
                            if (lost > 0) wrong.Add(string.Format(CultureInfo.InvariantCulture, "{0}: рентгена базы {1}, нет в образе {2}", name, here, lost));
                        }
                    }
                }
            }

            Say(wrong.Count == 0 && gammaOk == gammaAll, string.Format(CultureInfo.InvariantCulture,
                "сильнейшая гамма с выходом базы: {0} из {1}; нуклидов с рентгеном в базе {2}, строк {3}, потеряно {4}{5}",
                gammaOk, gammaAll, withX, xLines, xMissing, wrong.Count > 0 ? "; " + string.Join("; ", wrong.Take(8)) : ""));
            Say(withX > 0, "положительный контроль раздела: рентген в базе вообще есть");
        }

        // ---------------------------------------------------------------
        // 3. Нуклида в базе нет
        // ---------------------------------------------------------------

        static void Typed(string known)
        {
            Console.WriteLine();
            Console.WriteLine("=== 3. нуклида в базе нет: образ из линий, вбитых руками ===");
            const string Name = "Xx-999";
            var defs = new List<NuclideDefinition>
            {
                Line(Name, 300.0, 40.0),
                Line(Name, 300.02, 40.0),          // дубль записи 0.05 кэВ
                Line(Name, 2000.0, 60.0),          // родитель вылета и аннигиляции
                Line(Name, 400.0, double.NaN),
                Line(Name, double.PositiveInfinity, 5.0),
                Line(Name, 1.0, 50.0),             // ниже пола полосы
            };
            FsaLibrary.LabelReport report;
            List<FsaComponent> built = Labels(new[] { Labelled(defs[0]) }, defs, new FsaSampleSpec(), out report);
            FsaComponent image = built.FirstOrDefault(c => string.Equals(c.Name, Name, StringComparison.OrdinalIgnoreCase));
            Say(image != null && image.Kind == FsaComponentKind.Single, "образ есть, вид Single");
            if (image != null)
            {
                Say(image.Lines.Count == 2 && image.Lines.Any(l => l.Energy == 300.0 && l.Intensity == 40.0)
                    && image.Lines.Any(l => l.Energy == 2000.0 && l.Intensity == 60.0),
                    "линии как вбиты: " + string.Join("; ", image.Lines.Select(l => l.Energy.ToString("0.###", CultureInfo.InvariantCulture) + " / " + l.Intensity.ToString("0.###", CultureInfo.InvariantCulture))));
            }

            Say(built.Any(c => c.Name == "SE-2000") && built.Any(c => c.Name == "DE-2000"), "вылет от линии, вбитой руками");
            Say(built.Any(c => c.Name == FsaResult.AnnihilationComponentName), "аннигиляция от линии, вбитой руками");
            Say(report.Typed.Contains(Name), "имя названо в отчёте сборки: " + report);
            FsaLibrary.LabelReport mixed;
            var both = new List<NuclideDefinition> { Line(known, 100.0, 1.0), Line(Name, 300.0, 40.0) };
            List<FsaComponent> pair = Labels(new[] { Labelled(both[0]), Labelled(both[1]) }, both, new FsaSampleSpec(), out mixed);
            Say(pair.Any(c => string.Equals(c.Name, Name, StringComparison.OrdinalIgnoreCase)) && pair.Any(c => string.Equals(c.Name, FsaSampleLibrary.PrettyName(FsaSampleLibrary.NucidOf(known)), StringComparison.OrdinalIgnoreCase)),
                "рядом с нуклидом из базы: оба столбца");
        }

        // ---------------------------------------------------------------
        // 4. Подпись элемента
        // ---------------------------------------------------------------

        static void Elements(string symbol)
        {
            Console.WriteLine();
            Console.WriteLine("=== 4. подпись элемента: рентген из matdb, линии руками не читаются ===");
            int z = MaterialDatabase.ZOf(symbol);
            if (z <= 0)
            {
                Say(false, "элемента нет в matdb: " + symbol);
                return;
            }

            string label = symbol + " x-ray";
            var defs = new List<NuclideDefinition> { Line(label, 77.0, 55.0) };
            FsaLibrary.LabelReport report;
            FsaSampleSpec spec = new FsaSampleSpec();
            List<FsaComponent> built = Labels(new[] { Labelled(defs[0]) }, defs, spec, out report);
            FsaComponent kSeries = FsaSampleLibrary.FluorescenceComponent(z, spec.LineFloorKev, spec.MaxEnergyKev);
            FsaComponent image = kSeries != null ? built.FirstOrDefault(c => c.Name == kSeries.Name) : null;
            Say(image != null && image.Lines.Count == kSeries.Lines.Count
                && !built.SelectMany(c => c.Lines).Any(l => l.Energy == 77.0 && l.Intensity == 55.0),
                "K-серия из matdb: " + (image != null ? image.Name + ", линий " + image.Lines.Count.ToString(CultureInfo.InvariantCulture) : "нет"));
            Say(report.Elements.Contains(z), "элемент назван в отчёте сборки");
            Say(built.All(c => c.Kind == FsaComponentKind.Nuisance), "образ мешающий, в долях не участвует");
            FsaSampleSpec off = new FsaSampleSpec { AtomicXray = false };
            FsaLibrary.LabelReport offReport;
            Say(Labels(new[] { Labelled(defs[0]) }, defs, off, out offReport).Count == 0, "флажок рентгена снял образ");
            FsaLibrary.LabelReport nameless;
            var noElement = new List<NuclideDefinition> { Line("X-ray", 50.0, 10.0) };
            List<FsaComponent> typed = Labels(new[] { Labelled(noElement[0]) }, noElement, new FsaSampleSpec(), out nameless);
            Say(typed.Count == 1 && typed[0].Kind == FsaComponentKind.Nuisance && typed[0].Lines[0].Energy == 50.0, "подпись без элемента: мешающий образ из строки библиотеки");
            FsaLibrary.LabelReport empty;
            var noLines = new List<NuclideDefinition> { Line("X-ray", 50.0, 0.0) };
            Say(Labels(new[] { Labelled(noLines[0]) }, noLines, new FsaSampleSpec(), out empty).Count == 0, "подпись без элемента и выхода: образа нет");
        }

        // ---------------------------------------------------------------
        // 5. Края
        // ---------------------------------------------------------------

        static void Odds(List<NuclideDefinition> library, List<string> names)
        {
            Console.WriteLine();
            Console.WriteLine("=== 5. ряд в подписи, написания, вылет, пустой состав ===");
            string first = names[0];
            string nucid = FsaSampleLibrary.NucidOf(first);
            var own = new FsaSampleSpec();
            own.Nuclides.Add(nucid);
            string want = Dump(FsaSampleLibrary.Build(own));

            var chained = new List<NuclideDefinition> { Line(first + " (Th-232)", 100.0, 0.1, "Th-232") };
            FsaLibrary.LabelReport report;
            Say(Dump(Labels(new[] { Labelled(chained[0]) }, chained, new FsaSampleSpec(), out report)) == want,
                "хвост и поле ряда: выходы на свой распад");

            int mass; string symbol, state;
            CascadeAtomicData.SplitNucid(nucid, out mass, out symbol, out state);
            string plain = char.ToUpperInvariant(symbol[0]) + symbol.Substring(1).ToLowerInvariant() + mass.ToString(CultureInfo.InvariantCulture) + state;
            var spell = new List<NuclideDefinition> { Line(nucid, 100.0, 1.0), Line(plain, 100.0, 1.0), Line(FsaSampleLibrary.PrettyName(nucid), 100.0, 1.0) };
            Say(Dump(Labels(spell.Select(Labelled), spell, new FsaSampleSpec(), out report)) == want,
                "три написания («" + nucid + "», «" + plain + "», «" + FsaSampleLibrary.PrettyName(nucid) + "») — один столбец");

            var escape = new List<NuclideDefinition> { Line("SE-2615", 2103.5, 100.0) };
            Say(Labels(new[] { Labelled(escape[0]) }, escape, new FsaSampleSpec(), out report).Count == 0, "подпись вылета нуклидом не становится");
            Say(Labels(new[] { new Peak() }, library, new FsaSampleSpec(), out report).Count == 0, "пик без подписи: состава нет");
            Say(Labels(new Peak[0], library, new FsaSampleSpec(), out report).Count == 0, "пиков нет: состава нет, служебных образов тоже");

            var unlabelled = new List<NuclideDefinition>(spell) { Line(names[names.Count - 1], 200.0, 1.0) };
            Say(Dump(Labels(new[] { Labelled(spell[0]) }, unlabelled, new FsaSampleSpec(), out report)) == want,
                "нуклид без подписи в образ не идёт: состав выбирают метки");
            var fractions = new Dictionary<int, double>();
            Say(Dump(FsaLibrary.BuildFromPeaks(new[] { Labelled(spell[0]) }, spell, fractions, true)) == want,
                "перегрузка без спектра (пробы) — тот же путь");
        }

        // ---------------------------------------------------------------
        // 6. Путь окна
        // ---------------------------------------------------------------

        static void SessionPath(string path, string setName, NuclideDefinitionManager nuclides, int timeoutS)
        {
            Console.WriteLine();
            Console.WriteLine("=== 6. путь окна: по меткам против «Из NucBase» ===");
            if (!File.Exists(path))
            {
                Say(false, "нет спектра: " + path);
                return;
            }

            if (setName != null)
            {
                NuclideSet set = (nuclides.NuclideSets ?? new List<NuclideSet>())
                    .FirstOrDefault(s => string.Equals(s.Name, setName, StringComparison.OrdinalIgnoreCase));
                if (set == null)
                {
                    Say(false, "сета нет: " + setName);
                    return;
                }

                nuclides.ActiveSet = set;
            }

            FsaResult labels = Run(path, nuclides, false, timeoutS);
            FsaResult nucBase = Run(path, nuclides, true, timeoutS);
            if (labels == null || nucBase == null)
            {
                Say(false, "разбор не завершился");
                return;
            }

            string a = Fingerprint(labels), b = Fingerprint(nucBase);
            Console.WriteLine("  по меткам:  " + Short(labels));
            Console.WriteLine("  Из NucBase: " + Short(nucBase));
            Say(a == b, "результат побитово один");
        }

        static FsaResult Run(string path, NuclideDefinitionManager nuclides, bool nucBase, int timeoutS)
        {
            ResultData rd = Load(path);
            FsaCalculationOptions options = FsaCalculationOptions.Of(rd);
            options.DbLookups = nucBase;
            FsaAnalysisSession.ProbeAnalyzerHook = analyzer =>
            {
                FsaTuningReport.Print(analyzer, nucBase ? "Из NucBase" : "по меткам");
            };
            var session = new FsaAnalysisSession();
            var done = new ManualResetEvent(false);
            session.Completed += (s, e) => done.Set();
            session.EnsureUpToDate(rd, rd.BackgroundEnergySpectrum != null, options);
            return done.WaitOne(TimeSpan.FromSeconds(timeoutS)) ? session.Result : null;
        }

        static string Short(FsaResult result)
        {
            return string.Format(CultureInfo.InvariantCulture, "χ²/ndf Пуассона {0:F4}; {1}", result.Chi2NdfPoisson,
                string.Join("; ", result.Components.Select(c => c.Name + " " + c.CountRate.ToString("0.#####", CultureInfo.InvariantCulture))));
        }

        static string Fingerprint(FsaResult result)
        {
            var text = new StringBuilder();
            text.Append(Bits(result.Chi2Ndf)).Append(Bits(result.Chi2NdfPoisson)).Append(Bits(result.Gain)).Append(Bits(result.OffsetChannels));
            foreach (FsaComponentResult c in result.Components)
            {
                text.Append('|').Append(c.Name).Append(' ').Append(c.Kind).Append(' ').Append(Bits(c.CountRate)).Append(' ').Append(Bits(c.SharePercent));
            }

            if (result.Model != null)
            {
                foreach (double v in result.Model) text.Append(Bits(v));
            }

            return text.ToString();
        }

        static ResultData Load(string path)
        {
            ResultDataFile file;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                file = (ResultDataFile)new XmlSerializer(typeof(ResultDataFile)).Deserialize(stream);
            }

            ResultData rd = file.ResultDataList[0];
            EnergySpectrum s = rd.EnergySpectrum;
            if (s != null && s.Spectrum != null && s.TotalPulseCount == 0)
            {
                long total = 0;
                for (int i = 0; i < s.Spectrum.Length; i++) total += s.Spectrum[i];
                s.TotalPulseCount = total;
                s.ValidPulseCount = total;
            }

            ProbeDeviceConfig.Attach(rd);
            var cfg = rd.PeakDetectionMethodConfig as FWHMPeakDetectionMethodConfig;
            if (rd.FwhmCalibration == null && cfg != null)
            {
                if (cfg.FwhmCalibration == null && rd.EnergySpectrum != null)
                {
                    cfg.FwhmCalibration = FwhmCalibration.DefaultCalibration(cfg, rd.EnergySpectrum.EnergyCalibration);
                }

                if (cfg.FwhmCalibration != null) rd.FwhmCalibration = cfg.FwhmCalibration.Clone();
            }

            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();
            rd.DetectedPeaks = new PeakDetector().DetectPeak(rd, BackgroundMode.Invisible, SmoothingMethod.None, nuclides.ActiveSet, nuclides.NuclideDefinitions);
            return rd;
        }
    }
}
