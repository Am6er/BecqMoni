using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MaterialNameProbeF67
{
    /// <summary>
    /// ИМЯ ВЕЩЕСТВА ПРОТИВ ЕГО СОСТАВА — перепись всего дерева (`A181`).
    ///
    /// Строка `A181` утверждает три вещи, и проба меряет каждую отдельно, не
    /// принимая ни одной на веру:
    ///
    ///   1. «Состав вещества `ОИСН-06` в корпусных файлах и в библиотеке
    ///      РАЗНЫЙ». Считается СПЛОШЬ, по всем `*.in` дерева и всем четырнадцати
    ///      слотам вещества в каждом файле, а не по двум названным именам:
    ///      сколько имён на деле несут разный состав — В САМОМ ДЕРЕВЕ (одно имя,
    ///      два вещества в разных файлах) и ПРОТИВ БИБЛИОТЕКИ.
    ///
    ///   2. «Вещество с именем `Water, liquid` несёт лютеций» — тот же счёт, имя
    ///      попадает в тот же список само, если утверждение верно.
    ///
    ///   3. ⛔ «При этом КЛЕЙМО СОВПАДАЕТ». Вот это и есть настоящий вопрос:
    ///      входит ли состав в отпечаток вообще. Проба собирает сцену ДВАЖДЫ —
    ///      из файла и «как в редакторе» (состав взят из библиотеки ПО ИМЕНИ) —
    ///      и сравнивает `ResponseMatrix.ComputeStamp` у обеих. Утверждение
    ///      строки подтверждается только совпадением клейма при разном составе.
    ///
    /// ⚠ Порог различия берётся у ФОРМАТА, а не «чтобы прошло»: доли пишутся
    /// `{0:G6}`, шесть значащих, — значит расхождение ниже 1e-6 относительных
    /// клеймо не видит в принципе и веществом называться не может. Всё, что
    /// выше, — разные вещества под одним именем.
    ///
    /// Положительные контроли (ключ `--control`), у каждого свой заведомый ответ:
    ///   К1 состав НЕ трогаем            → клеймо обязано СОВПАСТЬ;
    ///   К2 доля сдвинута на 1e-3        → клеймо обязано РАЗОЙТИСЬ;
    ///   К3 доля сдвинута на 1e-12       → клеймо обязано СОВПАСТЬ (ниже G6);
    ///   К4 размер кристалла +1 мм       → клеймо обязано РАЗОЙТИСЬ.
    /// Отказ любого из четырёх означает, что сравнение клейм ничего не меряет.
    ///
    /// Ничего не правит и никуда, кроме своего `--report=`, не пишет.
    /// </summary>
    static class Program
    {
        // Все четырнадцать слотов вещества формата `.in`: имя ключей счётчика,
        // плотности, Z, долей и подписи. Список повторяет `GeometryWriter`
        // буква в букву — там он приватный, и второй копии не избежать.
        sealed class Slot
        {
            public string Ro, Z, Fr, Name, Title;
        }

        static readonly Slot[] Slots =
        {
            new Slot { Ro = "DC_RoCrystal", Z = "DC_ZCrystal", Fr = "DC_FractionsCrystal", Name = "M_DC_Crystal.MName", Title = "DC кристалл" },
            new Slot { Ro = "DC_RoCrystalSideCladding", Z = "DC_ZCrystalSideCladding", Fr = "DC_FractionsCrystalSideCladding", Name = "M_DC_Crystal_Cladding.MName", Title = "DC оправа" },
            new Slot { Ro = "DC_RoCrystalMounting", Z = "DC_ZCrystalMounting", Fr = "DC_FractionsCrystalMounting", Name = "M_DC_Crystal_Mounting.MName", Title = "DC подвес" },
            new Slot { Ro = "DC_RoDetectorCap", Z = "DC_ZDetectorCap", Fr = "DC_FractionsDetectorCap", Name = "M_DC_Detector_Cap.MName", Title = "DC колпак" },
            new Slot { Ro = "DC_RoVacuum", Z = "DC_ZVacuum", Fr = "DC_FractionsVacuum", Name = "M_DC_Vacuum.MName", Title = "DC вакуум" },
            new Slot { Ro = "DS_RoCrystal", Z = "DS_ZCrystal", Fr = "DS_FractionsCrystal", Name = "M_DS_Crystal.MName", Title = "DS кристалл" },
            new Slot { Ro = "DS_RoCrystalCladding", Z = "DS_ZCrystalCladding", Fr = "DS_FractionsCrystalCladding", Name = "M_DS_Crystal_Cladding.MName", Title = "DS оправа" },
            new Slot { Ro = "DS_RoCrystalReflector", Z = "DS_ZCrystalReflector", Fr = "DS_FractionsCrystalReflector", Name = "M_DS_Reflector.MName", Title = "DS отражатель" },
            new Slot { Ro = "SC_RoWall", Z = "SC_ZWall", Fr = "SC_FractionsWall", Name = "M_SC_Beaker.MName", Title = "SC стенка" },
            new Slot { Ro = "SC_RoSource", Z = "SC_ZSource", Fr = "SC_FractionsSource", Name = "M_SC_Source.MName", Title = "SC проба" },
            new Slot { Ro = "SC_RoEmptySpace", Z = "SC_ZEmptySpace", Fr = "SC_FractionsEmptySpace", Name = "M_SC_EmptySpace.MName", Title = "SC пустота" },
            new Slot { Ro = "SM_RoWall", Z = "SM_ZWall", Fr = "SM_FractionsWall", Name = "M_SM_Beaker.MName", Title = "SM стенка" },
            new Slot { Ro = "SM_RoSource", Z = "SM_ZSource", Fr = "SM_FractionsSource", Name = "M_SM_Source.MName", Title = "SM проба" },
            new Slot { Ro = "SM_RoEmptySpace", Z = "SM_ZEmptySpace", Fr = "SM_FractionsEmptySpace", Name = "M_SM_EmptySpace.MName", Title = "SM пустота" },
        };

        /// <summary>
        /// Порог «РАЗНЫЕ ВЕЩЕСТВА», относительный. Умолчание 1e-3, и оно взято
        /// не с потолка: измеренные расхождения в дереве ложатся двумя кучами,
        /// между которыми ЧЕТЫРЕ ПОРЯДКА пустоты — округление записи даёт до
        /// 3.8e-5 (файлы ЛСРМ хранят доли четырьмя значащими: `0.04196` против
        /// `0.0419585` библиотеки), а настоящая разница вещества начинается с
        /// 6.1e-1. Любой порог от 1e-4 до 1e-1 даёт ОДИН И ТОТ ЖЕ ответ; ключ
        /// `--tol=` оставлен, чтобы это можно было проверить, а не поверить.
        /// </summary>
        static double Tolerance = 1e-3;

        /// <summary>
        /// Точность ЗАПИСИ долей: писатель кладёт их `{0:G6}`. Ниже неё клеймо
        /// слепо в принципе, и разница веществом не считается никогда.
        /// </summary>
        const double FormatTolerance = 1e-6;

        sealed class Variant
        {
            public Dictionary<int, double> Fractions;
            public readonly List<string> Files = new List<string>();
            public readonly List<string> Slots = new List<string>();
        }

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

            string root = "";
            string report = "";
            var scenes = new List<string>();
            var dumps = new List<string>();
            var sweeps = new List<string>();
            bool control = false;
            bool curves = false;
            foreach (string a in args)
            {
                if (a.StartsWith("--root=", StringComparison.Ordinal))
                {
                    root = a.Substring(7);
                }
                else if (a.StartsWith("--report=", StringComparison.Ordinal))
                {
                    report = a.Substring(9);
                }
                else if (a.StartsWith("--scene=", StringComparison.Ordinal))
                {
                    foreach (string s in a.Substring(8).Split(';'))
                    {
                        if (s.Trim().Length > 0)
                        {
                            scenes.Add(s.Trim());
                        }
                    }
                }
                else if (a.StartsWith("--tol=", StringComparison.Ordinal))
                {
                    if (!double.TryParse(a.Substring(6), NumberStyles.Float,
                                         CultureInfo.InvariantCulture, out Tolerance))
                    {
                        Console.Error.WriteLine("не разобрал --tol=");
                        return 1;
                    }
                }
                else if (a.StartsWith("--sweep=", StringComparison.Ordinal))
                {
                    foreach (string s in a.Substring(8).Split(';'))
                    {
                        if (s.Trim().Length > 0)
                        {
                            sweeps.Add(s.Trim());
                        }
                    }
                }
                else if (a.StartsWith("--dump=", StringComparison.Ordinal))
                {
                    foreach (string s in a.Substring(7).Split(';'))
                    {
                        if (s.Trim().Length > 0)
                        {
                            dumps.Add(s.Trim());
                        }
                    }
                }
                else if (a == "--control")
                {
                    control = true;
                }
                else if (a == "--curves")
                {
                    curves = true;
                }
                else
                {
                    Console.Error.WriteLine("не знаю ключа: " + a);
                    return 1;
                }
            }

            if (root.Length == 0)
            {
                Console.Error.WriteLine("нужен --root=<корень дерева>");
                return 1;
            }

            int bad = 0;

            // ------------------------------------------------------------------
            // 0. Библиотека: чья она сегодня
            // ------------------------------------------------------------------
            List<GeometryMaterialLibrary.Entry> lib = GeometryMaterialStore.Entries;
            Say("БИБЛИОТЕКА: веществ " + lib.Count.ToString(CultureInfo.InvariantCulture)
                + ", файл " + GeometryMaterialStore.FilePath
                + (File.Exists(GeometryMaterialStore.FilePath) ? " (ЕСТЬ)" : " (НЕТ → вшитый засев)")
                + (string.IsNullOrEmpty(GeometryMaterialStore.LoadError)
                       ? "" : ", ОШИБКА ЧТЕНИЯ: " + GeometryMaterialStore.LoadError));
            Say("вшитый засев: веществ "
                + GeometryMaterialLibrary.Seed().Count.ToString(CultureInfo.InvariantCulture));
            Say("");

            // ------------------------------------------------------------------
            // 1. Перепись дерева
            // ------------------------------------------------------------------
            var files = new List<string>();
            Walk(root, files);
            files.Sort(StringComparer.OrdinalIgnoreCase);

            var byName = new Dictionary<string, List<Variant>>(StringComparer.OrdinalIgnoreCase);
            int slotsFilled = 0, unnamed = 0, failed = 0;
            foreach (string path in files)
            {
                GeometryModel g;
                try
                {
                    g = GeometryModel.Load(path);
                }
                catch (Exception e)
                {
                    failed++;
                    Say("ОТКАЗ ЧТЕНИЯ " + Rel(root, path) + ": " + e.GetType().Name + " " + e.Message);
                    continue;
                }

                foreach (Slot slot in Slots)
                {
                    string name;
                    if (!g.Raw.TryGetValue(slot.Name, out name))
                    {
                        name = "";
                    }

                    name = name.Trim();
                    Dictionary<int, double> fr = ReadFractions(g.Raw, slot);
                    if (fr.Count == 0)
                    {
                        continue;
                    }

                    slotsFilled++;
                    if (name.Length == 0)
                    {
                        unnamed++;
                        continue;
                    }

                    List<Variant> list;
                    if (!byName.TryGetValue(name, out list))
                    {
                        list = new List<Variant>();
                        byName[name] = list;
                    }

                    Variant found = null;
                    foreach (Variant v in list)
                    {
                        if (Same(v.Fractions, fr))
                        {
                            found = v;
                            break;
                        }
                    }

                    if (found == null)
                    {
                        found = new Variant { Fractions = fr };
                        list.Add(found);
                    }

                    found.Files.Add(Rel(root, path));
                    found.Slots.Add(slot.Title);
                }
            }

            Say(string.Format(CultureInfo.InvariantCulture,
                              "ДЕРЕВО: файлов .in {0}, отказов чтения {1}, слотов с составом {2}, "
                              + "без имени {3}, РАЗЛИЧНЫХ ИМЁН {4}",
                              files.Count, failed, slotsFilled, unnamed, byName.Count));

            var names = new List<string>(byName.Keys);
            names.Sort(StringComparer.Ordinal);

            int multi = 0, multiReal = 0;
            Say("");
            Say(string.Format(CultureInfo.InvariantCulture,
                              "§1 ОДНО ИМЯ — НЕСКОЛЬКО ВЕЩЕСТВ В САМОМ ДЕРЕВЕ (порог {0:E1} отн.)",
                              Tolerance));
            foreach (string name in names)
            {
                List<Variant> list = byName[name];
                if (list.Count < 2)
                {
                    continue;
                }

                multi++;
                Say("  " + name + ": вариантов " + list.Count.ToString(CultureInfo.InvariantCulture));
                for (int i = 0; i < list.Count; i++)
                {
                    Say(string.Format(CultureInfo.InvariantCulture,
                                      "     [{0}]  вхождений {1}, напр. {2} ({3})",
                                      Describe(list[i].Fractions), list[i].Files.Count,
                                      list[i].Files[0], list[i].Slots[0]));
                }

                // Насколько они разные: худшее относительное расхождение пары.
                double worst = 0.0;
                bool zDiffer = false;
                for (int i = 0; i < list.Count; i++)
                {
                    for (int j = i + 1; j < list.Count; j++)
                    {
                        bool z;
                        double d = Deviation(list[i].Fractions, list[j].Fractions, out z);
                        worst = Math.Max(worst, d);
                        zDiffer |= z;
                    }
                }

                bool real = zDiffer || worst > Tolerance;
                if (real)
                {
                    multiReal++;
                }

                Say(string.Format(CultureInfo.InvariantCulture,
                                  "     набор Z {0}, худшее расхождение доли {1:E2} отн. — {2}",
                                  zDiffer ? "РАЗНЫЙ" : "тот же", worst,
                                  real ? "РАЗНЫЕ ВЕЩЕСТВА"
                                       : (worst > FormatTolerance
                                              ? "разница НА УРОВНЕ ЗАПИСИ, одно вещество"
                                              : "ниже точности записи G6, одно вещество")));
            }

            Say(string.Format(CultureInfo.InvariantCulture,
                              "  ИТОГО имён с несколькими записями состава: {0} из {1}; "
                              + "из них РАЗНЫЕ ВЕЩЕСТВА: {2}",
                              multi, byName.Count, multiReal));

            // ------------------------------------------------------------------
            // 2. Файл против библиотеки
            // ------------------------------------------------------------------
            Say("");
            Say(string.Format(CultureInfo.InvariantCulture,
                              "§2 ФАЙЛ ПРОТИВ БИБЛИОТЕКИ (по имени, порог {0:E1} отн.)", Tolerance));
            int compared = 0, absent = 0, differZ = 0, differF = 0, agree = 0, rounding = 0;
            var offenders = new List<string>();
            foreach (string name in names)
            {
                GeometryMaterialLibrary.Entry e = Find(lib, name);
                if (e == null)
                {
                    absent++;
                    Say("  НЕТ В БИБЛИОТЕКЕ: " + name);
                    continue;
                }

                Dictionary<int, double> made;
                try
                {
                    made = GeometryMaterialLibrary.Make(e, 0.0).Fractions;
                }
                catch (Exception ex)
                {
                    Say("  " + name + ": библиотека отказала — " + ex.GetType().Name);
                    continue;
                }

                compared++;
                bool anyZ = false;
                double worst = 0.0;
                foreach (Variant v in byName[name])
                {
                    bool z;
                    double d = Deviation(v.Fractions, made, out z);
                    anyZ |= z;
                    worst = Math.Max(worst, d);
                }

                if (anyZ)
                {
                    differZ++;
                    offenders.Add(name);
                }
                else if (worst > Tolerance)
                {
                    differF++;
                    offenders.Add(name);
                }
                else if (worst > FormatTolerance)
                {
                    rounding++;
                    Say(string.Format(CultureInfo.InvariantCulture,
                                      "  {0}: разница {1:E2} отн. — УРОВЕНЬ ЗАПИСИ, одно вещество",
                                      name, worst));
                    continue;
                }
                else
                {
                    agree++;
                    continue;
                }

                Say("  " + name + "  библиотека [" + Describe(made) + "]");
                foreach (Variant v in byName[name])
                {
                    bool z;
                    double d = Deviation(v.Fractions, made, out z);
                    Say(string.Format(CultureInfo.InvariantCulture,
                                      "     файл [{0}]  {1}, |откл| {2:E2} отн., вхождений {3}",
                                      Describe(v.Fractions),
                                      z ? "НАБОР Z ДРУГОЙ"
                                        : (d > Tolerance ? "ДРУГОЕ ВЕЩЕСТВО"
                                           : (d > FormatTolerance ? "разница уровня записи" : "совпало")),
                                      d, v.Files.Count));
                }
            }

            Say(string.Format(CultureInfo.InvariantCulture,
                              "  сверено {0}, совпало точно {1}, разница уровня записи {2}, "
                              + "ДРУГОЙ НАБОР Z {3}, ДРУГИЕ ДОЛИ {4}, нет в библиотеке {5}",
                              compared, agree, rounding, differZ, differF, absent));
            Say("  ⛔ РАЗНЫХ ВЕЩЕСТВ ПОД ОДНИМ ИМЕНЕМ (файл ≠ библиотека): "
                + (differZ + differF).ToString(CultureInfo.InvariantCulture)
                + " — " + string.Join(", ", offenders.ToArray()));

            // ------------------------------------------------------------------
            // 3. Клеймо: входит ли состав в отпечаток
            // ------------------------------------------------------------------
            Say("");
            Say("§3 КЛЕЙМО: сцена ИЗ ФАЙЛА против той же сцены ПО ИМЕНИ ИЗ БИБЛИОТЕКИ");
            Say("   плечо Б — подменено ТОЛЬКО то вещество, что и правда другое (порог выше);");
            Say("   плечо В — подменены ВСЕ пять веществ, как делает правка библиотеки в редакторе.");
            var options = new ResponseMatrixOptions();
            int sceneMoved = 0, sceneSame = 0, sceneUntouched = 0;
            foreach (string scene in scenes)
            {
                if (!File.Exists(scene))
                {
                    Console.Error.WriteLine("НЕТ СЦЕНЫ: " + scene);
                    return 1;
                }

                GeometryModel a = GeometryModel.Load(scene);
                GeometryModel b = GeometryModel.Load(scene);
                GeometryModel c = GeometryModel.Load(scene);
                int replacedB = 0, replacedC = 0;
                var notes = new List<string>();
                foreach (GeometryMaterial m in Visible(b))
                {
                    GeometryMaterialLibrary.Entry e = Find(lib, m.Name);
                    if (e == null)
                    {
                        continue;
                    }

                    GeometryMaterial made = GeometryMaterialLibrary.Make(e, m.Density);
                    if (made.Fractions.Count == 0)
                    {
                        continue;
                    }

                    bool z;
                    double d = Deviation(m.Fractions, made.Fractions, out z);
                    if (!z && d <= Tolerance)
                    {
                        continue;
                    }

                    replacedB++;
                    notes.Add(string.Format(CultureInfo.InvariantCulture,
                                            "     {0}: файл [{1}] → библиотека [{2}], |откл| {3:E2}",
                                            m.Name, Describe(m.Fractions), Describe(made.Fractions), d));
                    Put(m, made);
                }

                foreach (GeometryMaterial m in Visible(c))
                {
                    GeometryMaterialLibrary.Entry e = Find(lib, m.Name);
                    if (e == null)
                    {
                        continue;
                    }

                    GeometryMaterial made = GeometryMaterialLibrary.Make(e, m.Density);
                    if (made.Fractions.Count == 0)
                    {
                        continue;
                    }

                    replacedC++;
                    Put(m, made);
                }

                string sa = ResponseMatrix.ComputeStamp(a, options);
                string sb = ResponseMatrix.ComputeStamp(b, options);
                string sc = ResponseMatrix.ComputeStamp(c, options);
                bool sameB = string.Equals(sa, sb, StringComparison.Ordinal);
                bool sameC = string.Equals(sa, sc, StringComparison.Ordinal);
                Say("  " + Path.GetFileName(scene)
                    + ": РАЗНЫХ веществ " + replacedB.ToString(CultureInfo.InvariantCulture)
                    + ", подменено в плече В " + replacedC.ToString(CultureInfo.InvariantCulture));
                foreach (string n in notes)
                {
                    Say(n);
                }

                Say("     А клеймо файла          " + sa);
                Say("     Б клеймо (только разное) " + sb + "  → " + (sameB ? "СОВПАЛО" : "РАЗОШЛОСЬ"));
                Say("     В клеймо (все пять)      " + sc + "  → " + (sameC ? "СОВПАЛО" : "РАЗОШЛОСЬ"));
                if (replacedB == 0)
                {
                    sceneUntouched++;
                    if (!sameB)
                    {
                        Say("     ⛔ ОТКАЗ: состав не двигался, а клеймо плеча Б разошлось");
                        bad++;
                    }
                }
                else if (sameB)
                {
                    sceneSame++;
                    Say("     ⛔ СОСТАВ РАЗНЫЙ, А КЛЕЙМО СОВПАЛО — посылка `A181` подтверждается");
                }
                else
                {
                    sceneMoved++;
                }

                if (curves && replacedB > 0)
                {
                    Curves(a, b);
                }
            }

            if (scenes.Count > 0)
            {
                Say(string.Format(CultureInfo.InvariantCulture,
                                  "  сцен {0}: без разницы в составе {1}, состав РАЗНЫЙ и клеймо разошлось {2}, "
                                  + "состав РАЗНЫЙ, а КЛЕЙМО СОВПАЛО {3}",
                                  scenes.Count, sceneUntouched, sceneMoved, sceneSame));
            }

            // ------------------------------------------------------------------
            // 4. Положительные контроли
            // ------------------------------------------------------------------
            if (control && scenes.Count > 0)
            {
                Say("");
                Say("§4 ПОЛОЖИТЕЛЬНЫЕ КОНТРОЛИ (сцена " + Path.GetFileName(scenes[0]) + ")");
                string baseStamp = ResponseMatrix.ComputeStamp(GeometryModel.Load(scenes[0]), options);

                // К1 — ничего не трогаем.
                GeometryModel k1 = GeometryModel.Load(scenes[0]);
                bad += Check("К1 состав не тронут", baseStamp,
                             ResponseMatrix.ComputeStamp(k1, options), true);

                // К2 — доля сдвинута на 1e-3 (выше G6).
                GeometryModel k2 = GeometryModel.Load(scenes[0]);
                bad += Check("К2 доля +1e-3", baseStamp,
                             ResponseMatrix.ComputeStamp(Nudge(k2, 1e-3), options), false);

                // К3 — доля сдвинута на 1e-12 (ниже G6): клеймо слепо.
                GeometryModel k3 = GeometryModel.Load(scenes[0]);
                bad += Check("К3 доля +1e-12 (ниже G6)", baseStamp,
                             ResponseMatrix.ComputeStamp(Nudge(k3, 1e-12), options), true);

                // К4 — размер кристалла +1 мм.
                GeometryModel k4 = GeometryModel.Load(scenes[0]);
                if (k4.Shape == CrystalShape.Box)
                {
                    k4.CrystalBoxZ += 1.0;
                }
                else
                {
                    k4.CrystalHeight += 1.0;
                }

                bad += Check("К4 кристалл +1 мм", baseStamp,
                             ResponseMatrix.ComputeStamp(k4, options), false);

                // К5 — ГДЕ ИМЕННО клеймо слепнет. Не «ниже G6» словами, а
                // числом: половинным делением ищется наименьший сдвиг доли,
                // который клеймо ещё видит. Ниже него два РАЗНЫХ состава дают
                // одно клеймо — это и есть настоящая слепая полоса отпечатка.
                double lo = 0.0, hi = 1e-3;
                for (int i = 0; i < 60; i++)
                {
                    double mid = 0.5 * (lo + hi);
                    GeometryModel probe = Nudge(GeometryModel.Load(scenes[0]), mid);
                    if (string.Equals(baseStamp, ResponseMatrix.ComputeStamp(probe, options),
                                      StringComparison.Ordinal))
                    {
                        lo = mid;
                    }
                    else
                    {
                        hi = mid;
                    }
                }

                GeometryModel firstScene = GeometryModel.Load(scenes[0]);
                var first = new List<int>(firstScene.Source.Fractions.Keys);
                double value = first.Count > 0 ? firstScene.Source.Fractions[first[0]] : 0.0;
                Say(string.Format(CultureInfo.InvariantCulture,
                                  "  К5 порог видимости клейма по доле: {0:E3} абс. "
                                  + "(доля {1:G6}, то есть {2:E2} отн.) — НИЖЕ него два разных "
                                  + "состава дают ОДНО клеймо",
                                  hi, value, value > 0.0 ? hi / value : 0.0));
            }

            // ------------------------------------------------------------------
            // 5. Опись сцены: текст, клеймо, состав, кривая — для сверки ДО и ПОСЛЕ
            //    правки файла. Приём полосы F64: если правка двигает матрицы,
            //    двинется КЛЕЙМО, и это видно тремя независимыми величинами.
            // ------------------------------------------------------------------
            // ------------------------------------------------------------------
            // 6. Развёртка: у СКОЛЬКИХ сцен каталога «ОК» в редакторе веществ
            //    сдвинет клеймо, ничего человеком не правя.
            //
            // ⛔ Это не то же, что §3. Плечо Б там спрашивает про РАЗНЫЕ
            //    вещества; здесь — про плечо В, то есть про путь
            //    `GeometryEditorPanel.EditMaterials`, который берёт состав из
            //    библиотеки у ВСЕХ ПЯТИ слотов, а не у правленого. Разница
            //    уровня записи (`0.04196` в файле против `0.0419585` в
            //    библиотеке) клеймо двигает так же, как настоящая.
            // ------------------------------------------------------------------
            foreach (string sweep in sweeps)
            {
                if (!Directory.Exists(sweep))
                {
                    Console.Error.WriteLine("НЕТ КАТАЛОГА: " + sweep);
                    return 1;
                }

                var list = new List<string>(Directory.GetFiles(sweep, "*.in"));
                list.Sort(StringComparer.OrdinalIgnoreCase);
                int movedB = 0, movedC = 0;
                var namesC = new List<string>();
                foreach (string path in list)
                {
                    GeometryModel a = GeometryModel.Load(path);
                    GeometryModel b = GeometryModel.Load(path);
                    GeometryModel c = GeometryModel.Load(path);
                    foreach (GeometryMaterial m in Visible(b))
                    {
                        GeometryMaterialLibrary.Entry e = Find(lib, m.Name);
                        if (e == null)
                        {
                            continue;
                        }

                        GeometryMaterial made = GeometryMaterialLibrary.Make(e, m.Density);
                        bool z;
                        if (made.Fractions.Count > 0
                            && (Deviation(m.Fractions, made.Fractions, out z) > Tolerance || z))
                        {
                            Put(m, made);
                        }
                    }

                    foreach (GeometryMaterial m in Visible(c))
                    {
                        GeometryMaterialLibrary.Entry e = Find(lib, m.Name);
                        if (e == null)
                        {
                            continue;
                        }

                        GeometryMaterial made = GeometryMaterialLibrary.Make(e, m.Density);
                        if (made.Fractions.Count > 0)
                        {
                            Put(m, made);
                        }
                    }

                    string sa = ResponseMatrix.ComputeStamp(a, options);
                    if (!string.Equals(sa, ResponseMatrix.ComputeStamp(b, options), StringComparison.Ordinal))
                    {
                        movedB++;
                    }

                    if (!string.Equals(sa, ResponseMatrix.ComputeStamp(c, options), StringComparison.Ordinal))
                    {
                        movedC++;
                        namesC.Add(Path.GetFileName(path));
                    }
                }

                Say("");
                Say("§6 РАЗВЁРТКА ПО КАТАЛОГУ " + sweep);
                Say(string.Format(CultureInfo.InvariantCulture,
                                  "  сцен {0}; клеймо двигает РАЗНОЕ ВЕЩЕСТВО у {1}; "
                                  + "клеймо двигает «ОК» в редакторе веществ (все пять слотов) у {2}",
                                  list.Count, movedB, movedC));
                foreach (string n in namesC)
                {
                    Say("     " + n);
                }
            }

            if (dumps.Count > 0)
            {
                Say("");
                Say("§5 ОПИСЬ СЦЕН (текст `Render`, клеймо, состав, кривая)");
                foreach (string scene in dumps)
                {
                    if (!File.Exists(scene))
                    {
                        Console.Error.WriteLine("НЕТ СЦЕНЫ: " + scene);
                        return 1;
                    }

                    Dump(scene, "");
                    if (control)
                    {
                        // Заведомая перемена: кристалл +1 мм. Все три величины
                        // ОБЯЗАНЫ сдвинуться, иначе опись ничего не меряет.
                        Dump(scene, "кристалл +1 мм");
                    }
                }
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
            Say(bad == 0 ? "ОТКАЗОВ ПРОВЕРКИ НЕТ" : "ОТКАЗОВ ПРОВЕРКИ: "
                + bad.ToString(CultureInfo.InvariantCulture));
            return bad == 0 ? 0 : 2;
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

        /// <summary>Сдвинуть долю первого элемента пробы на заданную величину.</summary>
        static GeometryModel Nudge(GeometryModel g, double delta)
        {
            GeometryMaterial m = g.Source;
            if (m == null || m.Fractions.Count == 0)
            {
                return g;
            }

            var keys = new List<int>(m.Fractions.Keys);
            m.Fractions[keys[0]] = m.Fractions[keys[0]] + delta;
            return g;
        }

        static void Dump(string scene, string breakage)
        {
            GeometryModel g = GeometryModel.Load(scene);
            if (breakage.Length > 0)
            {
                if (g.Shape == CrystalShape.Box)
                {
                    g.CrystalBoxZ += 1.0;
                }
                else
                {
                    g.CrystalHeight += 1.0;
                }
            }

            string text = GeometryWriter.Render(g);
            Say("  " + Path.GetFileName(scene)
                + (breakage.Length > 0 ? "  [ПОРЧА: " + breakage + "]" : ""));
            Say("     байт файла   " + new FileInfo(scene).Length.ToString(CultureInfo.InvariantCulture)
                + ", sha256 файла " + Sha(File.ReadAllBytes(scene)).Substring(0, 16));
            Say("     Render: знаков " + text.Length.ToString(CultureInfo.InvariantCulture)
                + ", sha256 " + Sha(Encoding.UTF8.GetBytes(text)).Substring(0, 16));
            Say("     клеймо " + ResponseMatrix.ComputeStamp(g, new ResponseMatrixOptions()));
            foreach (GeometryMaterial m in Visible(g))
            {
                Say(string.Format(CultureInfo.InvariantCulture,
                                  "     {0,-30} ro={1,-10} [{2}]",
                                  m.Name, m.Density.ToString("G8", CultureInfo.InvariantCulture),
                                  Describe(m.Fractions)));
            }

            var sim = new EfficiencySimulator(g) { Histories = 60000 };
            var line = new StringBuilder("     кривая:");
            foreach (double energy in new double[] { 50, 100, 300, 662, 1461, 2614 })
            {
                double err;
                line.Append(' ').Append(sim.Efficiency(energy, out err)
                                           .ToString("E10", CultureInfo.InvariantCulture));
            }

            Say(line.ToString());
        }

        static string Sha(byte[] bytes)
        {
            using (System.Security.Cryptography.SHA256 sha
                       = System.Security.Cryptography.SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                var hex = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash)
                {
                    hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                }

                return hex.ToString();
            }
        }

        static void Put(GeometryMaterial into, GeometryMaterial from)
        {
            into.Fractions.Clear();
            foreach (KeyValuePair<int, double> pair in from.Fractions)
            {
                into.Fractions[pair.Key] = pair.Value;
            }
        }

        static IEnumerable<GeometryMaterial> Visible(GeometryModel g)
        {
            yield return g.Crystal;
            yield return g.Reflector;
            yield return g.Cladding;
            yield return g.BeakerWall;
            yield return g.Source;
        }

        static void Curves(GeometryModel a, GeometryModel b)
        {
            var sa = new EfficiencySimulator(a) { Histories = 60000 };
            var sb = new EfficiencySimulator(b) { Histories = 60000 };
            foreach (double energy in new double[] { 50, 100, 300, 662, 1461, 2614 })
            {
                double err;
                double ea = sa.Efficiency(energy, out err);
                double eb = sb.Efficiency(energy, out err);
                double delta = ea > 0.0 ? (eb / ea - 1.0) * 100.0 : 0.0;
                Say(string.Format(CultureInfo.InvariantCulture,
                                  "     {0,6:F0} кэВ: файл {1:E6} / редактор {2:E6}  откл {3:F3} %",
                                  energy, ea, eb, delta));
            }
        }

        static void Walk(string dir, List<string> files)
        {
            string name = Path.GetFileName(dir);
            if (string.Equals(name, ".git", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            foreach (string f in Directory.GetFiles(dir, "*.in"))
            {
                files.Add(f);
            }

            foreach (string d in Directory.GetDirectories(dir))
            {
                Walk(d, files);
            }
        }

        static string Rel(string root, string path)
        {
            return path.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                       ? path.Substring(root.Length).TrimStart('\\', '/')
                       : path;
        }

        static Dictionary<int, double> ReadFractions(Dictionary<string, string> raw, Slot slot)
        {
            var fr = new Dictionary<int, double>();
            for (int i = 0; i < 24; i++)
            {
                string index = "[" + i.ToString(CultureInfo.InvariantCulture) + "]";
                string zRaw, fRaw;
                if (!raw.TryGetValue(slot.Z + index, out zRaw)
                    || !raw.TryGetValue(slot.Fr + index, out fRaw))
                {
                    continue;
                }

                int z;
                double f;
                if (int.TryParse(zRaw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out z)
                    && double.TryParse(fRaw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out f)
                    && z > 0 && f > 0.0)
                {
                    double have;
                    fr.TryGetValue(z, out have);
                    fr[z] = have + f;
                }
            }

            return Normalize(fr);
        }

        /// <summary>
        /// Доли нормируются на сумму: файлы ЛСРМ кладут их и суммой 1, и суммой
        /// 100, и сравнивать ненормированные значило бы объявить разными
        /// вещества, записанные в разных единицах.
        /// </summary>
        static Dictionary<int, double> Normalize(Dictionary<int, double> fr)
        {
            double total = 0.0;
            foreach (double v in fr.Values)
            {
                total += v;
            }

            if (!(total > 0.0))
            {
                return fr;
            }

            var norm = new Dictionary<int, double>();
            foreach (KeyValuePair<int, double> pair in fr)
            {
                norm[pair.Key] = pair.Value / total;
            }

            return norm;
        }

        static bool Same(Dictionary<int, double> a, Dictionary<int, double> b)
        {
            bool z;
            return !ZDiffer(a, b, out z) && Deviation(a, b, out z) <= 0.0;
        }

        static bool ZDiffer(Dictionary<int, double> a, Dictionary<int, double> b, out bool differ)
        {
            differ = a.Count != b.Count;
            if (!differ)
            {
                foreach (int z in a.Keys)
                {
                    if (!b.ContainsKey(z))
                    {
                        differ = true;
                        break;
                    }
                }
            }

            return differ;
        }

        /// <summary>
        /// Худшее ОТНОСИТЕЛЬНОЕ расхождение долей. Набор Z разошёлся — величина
        /// не определена, и возвращается бесконечность: это не «сильно
        /// отличается», это другое вещество.
        /// </summary>
        static double Deviation(Dictionary<int, double> a, Dictionary<int, double> b, out bool zDiffer)
        {
            ZDiffer(a, b, out zDiffer);
            if (zDiffer)
            {
                return double.PositiveInfinity;
            }

            double worst = 0.0;
            foreach (KeyValuePair<int, double> pair in a)
            {
                double other = b[pair.Key];
                double scale = Math.Max(Math.Abs(pair.Value), Math.Abs(other));
                if (scale > 0.0)
                {
                    worst = Math.Max(worst, Math.Abs(pair.Value - other) / scale);
                }
            }

            return worst;
        }

        static string Describe(Dictionary<int, double> fr)
        {
            var order = new List<int>(fr.Keys);
            order.Sort();
            var sb = new StringBuilder();
            foreach (int z in order)
            {
                if (sb.Length > 0)
                {
                    sb.Append(' ');
                }

                sb.Append(z.ToString(CultureInfo.InvariantCulture)).Append(':')
                  .Append(fr[z].ToString("G6", CultureInfo.InvariantCulture));
            }

            return sb.ToString();
        }

        static GeometryMaterialLibrary.Entry Find(List<GeometryMaterialLibrary.Entry> list, string name)
        {
            foreach (GeometryMaterialLibrary.Entry e in list)
            {
                if (string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return e;
                }
            }

            return null;
        }
    }
}
