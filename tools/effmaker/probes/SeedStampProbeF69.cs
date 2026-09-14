using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SeedStampProbeF69
{
    /// <summary>
    /// ЗАСЕВ ЭПОХИ 2024 И ШИРИНА ПОДМЕНЫ СОСТАВА — полоса F69, строки `A181` и `A262`.
    ///
    /// Проба отвечает на два вопроса, и ни одного не берёт на веру.
    ///
    ///   1. `A181`. В библиотеку добавлены `ОИСН-06 (2024)` и `ОИСН-16 (2024)`.
    ///      ⛔ Обязано остаться верным: НИ ОДНА готовая матрица от этого не
    ///      устарела. Доказательство — клейма всех сцен трёх каталогов ДО и
    ///      ПОСЛЕ засева, сличаемые ПОБАЙТНО по отчёту пробы (§5). Отчёт
    ///      несёт и снимок библиотеки (§1) — он-то как раз ОБЯЗАН измениться,
    ///      и это положительный контроль самого сличения: если два отчёта
    ///      совпадут целиком, значит мерили не то.
    ///
    ///   2. `A262`. `GeometryEditorPanel.EditMaterials` подменял состав у ВСЕХ
    ///      ПЯТИ слотов, а не у правленого. Проба гоняет по каждой сцене два
    ///      плеча — СТАРОЕ (все пять) и НОВОЕ (решает
    ///      <see cref="GeometryMaterialLibrary.CompositionChanged"/> по снимкам
    ///      библиотеки до и после окна) — и считает, у скольких сцен «ОК» без
    ///      единой правки двигает клеймо (§3).
    ///
    /// ⚠ Плечо повторяет `SelectMaterial` буква в букву, включая то, чего
    /// развёртка полосы F67 не знала: имя ищется НЕ ВО ВСЕЙ библиотеке, а в
    /// списке своего слота, то есть среди веществ его вида плюс ввезённых
    /// (`FillMaterialCombo`). Считаются оба числа — с этим отбором и без него,
    /// чтобы видно было, расходятся ли они.
    ///
    /// Положительные контроли (§4), у каждого свой заведомый ответ:
    ///   К1 библиотека не тронута, плечо НОВОЕ      → клеймо СОВПАДАЕТ;
    ///   К2 правлено вещество ПРОБЫ, плечо НОВОЕ    → клеймо РАСХОДИТСЯ;
    ///   К3 правлено ЧУЖОЕ вещество, плечо НОВОЕ    → клеймо СОВПАДАЕТ,
    ///      а СТАРОЕ на той же сцене — расходится;
    ///   К4 кристалл +1 мм                          → клеймо РАСХОДИТСЯ.
    /// Отказ любого означает, что сравнение клейм ничего не меряет.
    ///
    /// Ничего не правит и никуда, кроме своего `--report=`, не пишет.
    /// </summary>
    static class Program
    {
        sealed class Slot
        {
            public string Title;
            public GeometryMaterialLibrary.MaterialKind Kind;
            public Func<GeometryModel, GeometryMaterial> Of;
        }

        static readonly Slot[] Slots =
        {
            new Slot { Title = "Crystal",    Kind = GeometryMaterialLibrary.MaterialKind.Crystal,    Of = g => g.Crystal },
            new Slot { Title = "Reflector",  Kind = GeometryMaterialLibrary.MaterialKind.Reflector,  Of = g => g.Reflector },
            new Slot { Title = "Cladding",   Kind = GeometryMaterialLibrary.MaterialKind.Cladding,   Of = g => g.Cladding },
            new Slot { Title = "BeakerWall", Kind = GeometryMaterialLibrary.MaterialKind.BeakerWall, Of = g => g.BeakerWall },
            new Slot { Title = "Source",     Kind = GeometryMaterialLibrary.MaterialKind.Source,     Of = g => g.Source },
        };

        /// <summary>Имена, состав которых печатается в §1 — снимок библиотеки.</summary>
        static readonly string[] Watched =
        {
            "ОИСН-06", "ОИСН-06 (2024)", "ОИСН-10", "ОИСН-16", "ОИСН-16 (2024)", "РИСН-379",
        };

        static readonly StringBuilder Text = new StringBuilder();

        static void Say(string s)
        {
            Console.WriteLine(s);
            Text.Append(s).Append("\r\n");
        }

        static int Main(string[] args)
        {
            try
            {
                Console.OutputEncoding = new UTF8Encoding(false);
            }
            catch (Exception)
            {
            }

            string report = "";
            var sweeps = new List<string>();
            foreach (string a in args)
            {
                if (a.StartsWith("--sweep=", StringComparison.Ordinal))
                {
                    foreach (string s in a.Substring(8).Split(';'))
                    {
                        if (s.Trim().Length > 0)
                        {
                            sweeps.Add(s.Trim());
                        }
                    }
                }
                else if (a.StartsWith("--report=", StringComparison.Ordinal))
                {
                    report = a.Substring(9).Trim();
                }
                else
                {
                    Console.Error.WriteLine("не знаю ключа: " + a);
                    return 1;
                }
            }

            if (sweeps.Count == 0)
            {
                Console.Error.WriteLine("нужен --sweep=<каталог со сценами .in> (можно несколько через ;)");
                return 1;
            }

            int bad = 0;
            var options = new ResponseMatrixOptions();
            List<GeometryMaterialLibrary.Entry> lib = Snapshot(GeometryMaterialStore.Entries);

            // ------------------------------------------------------------------
            // 1. Снимок библиотеки. ⛔ ЭТИ строки после засева обязаны стать
            //    другими — иначе сличение отчётов «до/после» ничего не меряет.
            // ------------------------------------------------------------------
            Say("§1 БИБЛИОТЕКА");
            Say("  файл " + GeometryMaterialStore.FilePath
                + (File.Exists(GeometryMaterialStore.FilePath) ? " (ЕСТЬ)" : " (НЕТ → действует вшитый засев)"));
            Say("  веществ действующих " + lib.Count.ToString(CultureInfo.InvariantCulture)
                + ", в засеве " + GeometryMaterialLibrary.Seed().Count.ToString(CultureInfo.InvariantCulture)
                + ", поколение засева "
                + GeometryMaterialStore.CurrentSeedVersion.ToString(CultureInfo.InvariantCulture));
            foreach (string name in Watched)
            {
                GeometryMaterialLibrary.Entry e = Find(lib, name);
                if (e == null)
                {
                    Say("  " + name + ": В БИБЛИОТЕКЕ НЕТ");
                    continue;
                }

                GeometryMaterial made = GeometryMaterialLibrary.Make(e, 0.0, n => Find(lib, n));
                Say(string.Format(CultureInfo.InvariantCulture, "  {0}: ro={1} [{2}]",
                                  name, e.Density.ToString("G8", CultureInfo.InvariantCulture),
                                  Describe(made.Fractions)));
            }

            // ------------------------------------------------------------------
            // 2. Чем эпохи различаются ФИЗИЧЕСКИ: массовый коэффициент
            //    ослабления пробы. Разное имя без разного числа было бы
            //    переименованием, а не веществом.
            // ------------------------------------------------------------------
            Say("");
            Say("§2 ОСЛАБЛЕНИЕ В ПРОБЕ (массовый коэффициент, см²/г)");
            double[] energies = { 46.5, 59.5, 88.0, 186.2, 351.9, 661.7, 1460.8 };
            var line = new StringBuilder("  вещество                 ");
            foreach (double e in energies)
            {
                line.Append(string.Format(CultureInfo.InvariantCulture, "{0,12}", e.ToString("G6", CultureInfo.InvariantCulture)));
            }

            Say(line.ToString());
            var mu = new Dictionary<string, double[]>(StringComparer.Ordinal);
            foreach (string name in Watched)
            {
                GeometryMaterialLibrary.Entry e = Find(lib, name);
                if (e == null)
                {
                    continue;
                }

                GeometryMaterial made = GeometryMaterialLibrary.Make(e, 0.0, n => Find(lib, n));
                var row = new double[energies.Length];
                var text = new StringBuilder(string.Format(CultureInfo.InvariantCulture, "  {0,-25}", name));
                for (int i = 0; i < energies.Length; i++)
                {
                    double sum = 0.0;
                    foreach (KeyValuePair<int, double> pair in made.Fractions)
                    {
                        sum += pair.Value * AttenuationData.MassAttenuation(pair.Key, energies[i]);
                    }

                    row[i] = sum;
                    text.Append(string.Format(CultureInfo.InvariantCulture, "{0,12}",
                                              sum.ToString("F5", CultureInfo.InvariantCulture)));
                }

                mu[name] = row;
                Say(text.ToString());
            }

            foreach (string pair in new[] { "ОИСН-06", "ОИСН-16" })
            {
                double[] older, newer;
                if (!mu.TryGetValue(pair, out older) || !mu.TryGetValue(pair + " (2024)", out newer))
                {
                    continue;
                }

                var text = new StringBuilder(string.Format(CultureInfo.InvariantCulture,
                                                           "  {0,-25}", "во сколько раз 2024/2016"));
                for (int i = 0; i < energies.Length; i++)
                {
                    text.Append(string.Format(CultureInfo.InvariantCulture, "{0,12}",
                                              (older[i] > 0.0 ? newer[i] / older[i] : 0.0)
                                                  .ToString("F3", CultureInfo.InvariantCulture)));
                }

                Say("  --- " + pair);
                Say(text.ToString());
            }

            // ------------------------------------------------------------------
            // 3. Развёртка: у скольких сцен «ОК» в редакторе веществ, нажатый
            //    БЕЗ ЕДИНОЙ ПРАВКИ, двигает клеймо.
            // ------------------------------------------------------------------
            Say("");
            Say("§3 РАЗВЁРТКА: «ОК» БЕЗ ПРАВКИ");
            foreach (string sweep in sweeps)
            {
                if (!Directory.Exists(sweep))
                {
                    Console.Error.WriteLine("НЕТ КАТАЛОГА: " + sweep);
                    return 1;
                }

                var list = new List<string>(Directory.GetFiles(sweep, "*.in"));
                list.Sort(StringComparer.OrdinalIgnoreCase);
                int oldMoved = 0, oldMovedNoKind = 0, newMoved = 0;
                var names = new List<string>();
                foreach (string path in list)
                {
                    GeometryModel a = GeometryModel.Load(path);
                    string stamp = ResponseMatrix.ComputeStamp(a, options);
                    if (!string.Equals(stamp, Arm(path, lib, lib, true, true, options), StringComparison.Ordinal))
                    {
                        oldMoved++;
                        names.Add(Path.GetFileName(path));
                    }

                    if (!string.Equals(stamp, Arm(path, lib, lib, true, false, options), StringComparison.Ordinal))
                    {
                        oldMovedNoKind++;
                    }

                    if (!string.Equals(stamp, Arm(path, lib, lib, false, true, options), StringComparison.Ordinal))
                    {
                        newMoved++;
                    }
                }

                Say("");
                Say("  каталог " + sweep);
                Say(string.Format(CultureInfo.InvariantCulture,
                                  "    сцен {0}; клеймо двигает СТАРОЕ плечо (все пять слотов) у {1} "
                                  + "(без отбора по виду вещества — у {2}); НОВОЕ плечо — у {3}",
                                  list.Count, oldMoved, oldMovedNoKind, newMoved));
                foreach (string n in names)
                {
                    Say("      " + n);
                }

                if (newMoved != 0)
                {
                    Say("    ⛔ ПРИЁМКА ОТКАЗАЛА: новое плечо обязано двигать клеймо у 0 сцен");
                    bad++;
                }
            }

            // ------------------------------------------------------------------
            // 4. Положительные контроли — на первой сцене первого каталога.
            // ------------------------------------------------------------------
            Say("");
            Say("§4 ПОЛОЖИТЕЛЬНЫЕ КОНТРОЛИ");

            // Сцена берётся НЕ первая попавшаяся, а та, на которой дефект
            // `A262` и правда виден: где старое плечо клеймо двигает. На сцене,
            // у которой состав файла и библиотеки совпадает до знака, оба плеча
            // молчат, и контроль «старое двигает» отказал бы, ничего не измерив
            // (первый заход полосы на этом и споткнулся).
            string scene = DefectScene(sweeps, lib, options) ?? FirstScene(sweeps);
            if (scene == null)
            {
                Say("  ⛔ сцен нет вовсе — контроли не делались");
                bad++;
            }
            else
            {
                Say("  сцена " + Path.GetFileName(scene));
                GeometryModel plain = GeometryModel.Load(scene);
                string baseStamp = ResponseMatrix.ComputeStamp(plain, options);

                bad += Check("К1 «ОК» без единой правки, плечо СТАРОЕ (сам дефект A262)", baseStamp,
                             Arm(scene, lib, lib, true, true, options), false);
                bad += Check("К2 «ОК» без единой правки, плечо НОВОЕ", baseStamp,
                             Arm(scene, lib, lib, false, true, options), true);

                // К3: человек и правда переписал состав вещества пробы.
                string sourceName = plain.Source == null ? null : plain.Source.Name;
                List<GeometryMaterialLibrary.Entry> edited = Snapshot(lib);
                GeometryMaterialLibrary.Entry target = Find(edited, sourceName);
                if (target == null)
                {
                    Say("  ⛔ К3 не поставлен: вещества пробы «" + (sourceName ?? "") + "» в библиотеке нет");
                    bad++;
                }
                else
                {
                    target.ElementFractions.Clear();
                    target.ElementFractions[8] = 1.0;
                    bad += Check("К3 правлено вещество ПРОБЫ (" + sourceName + "), плечо НОВОЕ",
                                 baseStamp, Arm(scene, lib, edited, false, true, options), false);
                }

                // К4: правлено вещество, которого в этой сцене нет ни в одном
                // слоте. Новое плечо обязано молчать.
                List<GeometryMaterialLibrary.Entry> stranger = Snapshot(lib);
                GeometryMaterialLibrary.Entry alien = FindAlien(stranger, plain);
                if (alien == null)
                {
                    Say("  ⛔ К4 не поставлен: чужого вещества в библиотеке не нашлось");
                    bad++;
                }
                else
                {
                    alien.ElementFractions.Clear();
                    alien.ElementFractions[8] = 1.0;
                    bad += Check("К4 правлено ЧУЖОЕ вещество (" + alien.Name + "), плечо НОВОЕ",
                                 baseStamp, Arm(scene, lib, stranger, false, true, options), true);
                }

                // К5: заведомая перемена геометрии — клеймо обязано её видеть,
                // иначе все совпадения выше ничего не стоят.
                GeometryModel broken = GeometryModel.Load(scene);
                if (broken.Shape == CrystalShape.Box)
                {
                    broken.CrystalBoxZ += 1.0;
                }
                else
                {
                    broken.CrystalHeight += 1.0;
                }

                bad += Check("К5 кристалл +1 мм", baseStamp,
                             ResponseMatrix.ComputeStamp(broken, options), false);
            }

            // ------------------------------------------------------------------
            // 5. Клейма всех сцен — для побайтного сличения отчётов ДО и ПОСЛЕ
            //    засева эпохи 2024.
            // ------------------------------------------------------------------
            Say("");
            Say("§5 КЛЕЙМА СЦЕН (файл, sha256 текста Render, клеймо)");
            int total = 0;
            foreach (string sweep in sweeps)
            {
                var list = new List<string>(Directory.GetFiles(sweep, "*.in"));
                list.Sort(StringComparer.OrdinalIgnoreCase);
                Say("  --- " + sweep);
                foreach (string path in list)
                {
                    GeometryModel g = GeometryModel.Load(path);
                    string text = GeometryWriter.Render(g);
                    Say(string.Format(CultureInfo.InvariantCulture, "  {0,-44} {1} {2}",
                                      Path.GetFileName(path),
                                      Sha(Encoding.UTF8.GetBytes(text)).Substring(0, 16),
                                      ResponseMatrix.ComputeStamp(g, options)));
                    total++;
                }
            }

            Say("  всего сцен " + total.ToString(CultureInfo.InvariantCulture));

            // ------------------------------------------------------------------
            // 6. Список слота ПРОБЫ — тот самый, что видит человек в редакторе
            //    геометрии (`FillMaterialCombo`: вещества своего вида плюс
            //    ввезённые). Вещество, лежащее в библиотеке, но не попавшее в
            //    список, выбрать нельзя — а значит и засев ничего не дал.
            // ------------------------------------------------------------------
            Say("");
            Say("§6 СПИСОК СЛОТА ПРОБЫ (что видно в редакторе геометрии)");
            List<GeometryMaterialLibrary.Entry> source =
                GeometryMaterialLibrary.Of(GeometryMaterialLibrary.MaterialKind.Source);
            Say("  веществ вида «проба» " + source.Count.ToString(CultureInfo.InvariantCulture));
            foreach (string name in Watched)
            {
                Say("  " + name + ": " + (Find(source, name) == null ? "В СПИСКЕ НЕТ" : "в списке ЕСТЬ"));
            }

            if (report.Length > 0)
            {
                string dir = Path.GetDirectoryName(report);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(report, Text.ToString(), new UTF8Encoding(false));
                Console.WriteLine("отчёт: " + report);
            }

            Say("");
            Say(bad == 0 ? "ОТКАЗОВ ПРОВЕРКИ НЕТ"
                         : "ОТКАЗОВ ПРОВЕРКИ: " + bad.ToString(CultureInfo.InvariantCulture));
            return bad == 0 ? 0 : 2;
        }

        /// <summary>
        /// Клеймо сцены после «ОК» в редакторе веществ — так, как это делает
        /// `GeometryEditorPanel.EditMaterials` вместе с `SelectMaterial`.
        /// </summary>
        /// <param name="old">
        /// `true` — прежнее поведение: состав берётся из библиотеки у ВСЕХ пяти
        /// слотов. `false` — нынешнее: только у тех, чьё вещество правка и
        /// правда изменила.
        /// </param>
        /// <param name="byKind">
        /// Отбор по виду вещества, как в `FillMaterialCombo`: в списке слота
        /// стоят вещества его вида плюс ввезённые (`Other`). Имени, которого в
        /// списке нет, `SelectMaterial` не находит вовсе и состав слота не
        /// трогает.
        /// </param>
        static string Arm(string path, List<GeometryMaterialLibrary.Entry> before,
                          List<GeometryMaterialLibrary.Entry> after, bool old, bool byKind,
                          ResponseMatrixOptions options)
        {
            GeometryModel g = GeometryModel.Load(path);
            foreach (Slot slot in Slots)
            {
                GeometryMaterial m = slot.Of(g);
                if (m == null || m.Fractions.Count == 0 || string.IsNullOrEmpty(m.Name))
                {
                    continue;
                }

                GeometryMaterialLibrary.Entry e = Find(after, m.Name);
                if (e == null)
                {
                    continue;
                }

                if (byKind && e.Kind != slot.Kind && e.Kind != GeometryMaterialLibrary.MaterialKind.Other)
                {
                    continue;
                }

                if (!old && !GeometryMaterialLibrary.CompositionChanged(m.Name, before, after))
                {
                    continue;
                }

                GeometryMaterial made = GeometryMaterialLibrary.Make(e, m.Density, n => Find(after, n));
                if (made.Fractions.Count == 0)
                {
                    continue;
                }

                m.Fractions.Clear();
                foreach (KeyValuePair<int, double> pair in made.Fractions)
                {
                    m.Fractions[pair.Key] = pair.Value;
                }
            }

            return ResponseMatrix.ComputeStamp(g, options);
        }

        static int Check(string title, string was, string now, bool expectSame)
        {
            bool same = string.Equals(was, now, StringComparison.Ordinal);
            bool ok = same == expectSame;
            Say("  " + title + ": клеймо " + (same ? "совпало" : "разошлось")
                + ", ждали " + (expectSame ? "совпадения" : "расхождения")
                + " — " + (ok ? "верно" : "⛔ КОНТРОЛЬ ОТКАЗАЛ"));
            return ok ? 0 : 1;
        }

        /// <summary>Первая сцена, у которой СТАРОЕ плечо клеймо двигает.</summary>
        static string DefectScene(List<string> sweeps, List<GeometryMaterialLibrary.Entry> lib,
                                  ResponseMatrixOptions options)
        {
            foreach (string sweep in sweeps)
            {
                var list = new List<string>(Directory.GetFiles(sweep, "*.in"));
                list.Sort(StringComparer.OrdinalIgnoreCase);
                foreach (string path in list)
                {
                    string stamp = ResponseMatrix.ComputeStamp(GeometryModel.Load(path), options);
                    if (!string.Equals(stamp, Arm(path, lib, lib, true, true, options), StringComparison.Ordinal))
                    {
                        return path;
                    }
                }
            }

            return null;
        }

        static string FirstScene(List<string> sweeps)
        {
            foreach (string sweep in sweeps)
            {
                var list = new List<string>(Directory.GetFiles(sweep, "*.in"));
                list.Sort(StringComparer.OrdinalIgnoreCase);
                if (list.Count > 0)
                {
                    return list[0];
                }
            }

            return null;
        }

        /// <summary>Вещество библиотеки, которого в этой сцене нет ни в одном слоте.</summary>
        static GeometryMaterialLibrary.Entry FindAlien(List<GeometryMaterialLibrary.Entry> list,
                                                       GeometryModel g)
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Slot slot in Slots)
            {
                GeometryMaterial m = slot.Of(g);
                if (m != null && !string.IsNullOrEmpty(m.Name))
                {
                    used.Add(m.Name);
                }
            }

            foreach (GeometryMaterialLibrary.Entry e in list)
            {
                if (e != null && !string.IsNullOrEmpty(e.Name) && !used.Contains(e.Name))
                {
                    return e;
                }
            }

            return null;
        }

        static List<GeometryMaterialLibrary.Entry> Snapshot(List<GeometryMaterialLibrary.Entry> list)
        {
            var copy = new List<GeometryMaterialLibrary.Entry>();
            foreach (GeometryMaterialLibrary.Entry e in list)
            {
                if (e != null)
                {
                    copy.Add(e.Clone());
                }
            }

            return copy;
        }

        static GeometryMaterialLibrary.Entry Find(List<GeometryMaterialLibrary.Entry> list, string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            foreach (GeometryMaterialLibrary.Entry e in list)
            {
                if (e != null && string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return e;
                }
            }

            return null;
        }

        static string Describe(Dictionary<int, double> fractions)
        {
            var keys = new List<int>(fractions.Keys);
            keys.Sort();
            var text = new StringBuilder();
            foreach (int z in keys)
            {
                if (text.Length > 0)
                {
                    text.Append(' ');
                }

                text.Append(z.ToString(CultureInfo.InvariantCulture)).Append(':')
                    .Append(fractions[z].ToString("G12", CultureInfo.InvariantCulture));
            }

            return text.ToString();
        }

        static string Sha(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                var text = new StringBuilder();
                foreach (byte b in sha.ComputeHash(bytes))
                {
                    text.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                }

                return text.ToString();
            }
        }
    }
}
