using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MaterialOrderProbe
{
    /// <summary>
    /// ПОРЯДОК элементов в веществе — цена канонизации (`A162`).
    ///
    /// ⛔ Зачем это меряется до правки, а не после. Порядок элементов в
    /// <c>GeometryMaterial.Fractions</c> — вход розыгрыша: по нему
    /// `EfficiencySimulator` строит массивы (`ScatterersOf`, `BuildFluorescers`),
    /// а `PickAtom`/`SampleFluorescence` выбирают из них элемент случайным
    /// числом. Клеймо порядка НЕ ВИДИТ (`ComputeStamp` берёт текст
    /// `GeometryWriter.Render`, а тот состав сортирует), поэтому любая правка
    /// порядка меняет числа МОЛЧА. Цена обязана быть названа числом заранее.
    ///
    /// Проба считает три вещи и ничего не правит:
    ///
    /// 1. сколько веществ БИБЛИОТЕКИ отдают состав не по возрастанию Z —
    ///    источник дефекта (`GeometryMaterialLibrary.Compose` строит доли из
    ///    формулы, и у `CsI` выходит `55, 53`);
    /// 2. сколько ГЕОМЕТРИЙ на диске несут несортированный состав — это и есть
    ///    цена в сценах: у них правка на потреблении сдвинет числа;
    /// 3. совпадает ли состав, собранный БИБЛИОТЕКОЙ, с составом того же
    ///    вещества, прочитанным из файла, — то самое расхождение «в редакторе
    ///    одно, после сохранения другое».
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            // ⛔ (`T247`) Культура ЦЕЛИКОМ инвариантная, приказ Amber 05.09.2026. Проба
            //    не ставила её ВОВСЕ, и на русской машине часть её чисел шла с ЗАПЯТОЙ
            //    (замер 10.09.2026, полоса П8: мест без поставщика культуры — 1).
            //    Инвариант ЦЕЛИКОМ, а не клон с подменённым разделителем: клон
            //    чинит печать и оставляет РАЗБОР системным (`T245`).
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            try
            {
                Console.OutputEncoding = new UTF8Encoding(false);
            }
            catch (Exception)
            {
            }

            var dirs = new List<string>();
            string report = "";
            string scene = "";
            string curves = "";
            foreach (string a in args)
            {
                if (a.StartsWith("--dirs=", StringComparison.Ordinal))
                {
                    foreach (string d in a.Substring(7).Split(';'))
                    {
                        if (d.Trim().Length > 0)
                        {
                            dirs.Add(d.Trim());
                        }
                    }
                }
                else if (a.StartsWith("--report=", StringComparison.Ordinal))
                {
                    report = a.Substring(9);
                }
                else if (a.StartsWith("--scene=", StringComparison.Ordinal))
                {
                    scene = a.Substring(8);
                }
                else if (a.StartsWith("--curves=", StringComparison.Ordinal))
                {
                    curves = a.Substring(9);
                }
                else
                {
                    Console.Error.WriteLine("не знаю ключа: " + a);
                    return 1;
                }
            }

            var text = new StringBuilder();
            Action<string> say = s => { Console.WriteLine(s); text.Append(s).Append("\r\n"); };

            // ------------------------------------------------------------------
            // 1. Библиотека
            // ------------------------------------------------------------------
            List<GeometryMaterialLibrary.Entry> seed = GeometryMaterialLibrary.Seed();
            Report(say, "ВШИТЫЙ ЗАСЕВ", seed);

            List<GeometryMaterialLibrary.Entry> user = null;
            try
            {
                user = GeometryMaterialStore.Entries;
            }
            catch (Exception e)
            {
                say("библиотека пользователя недоступна: " + e.GetType().Name + " " + e.Message);
            }

            if (user != null)
            {
                say("");
                Report(say, "БИБЛИОТЕКА ПОЛЬЗОВАТЕЛЯ" +
                            (string.IsNullOrEmpty(GeometryMaterialStore.LoadError)
                                 ? "" : " (ОШИБКА ЧТЕНИЯ: " + GeometryMaterialStore.LoadError + ")"),
                       user);
            }

            // ------------------------------------------------------------------
            // 2. Геометрии на диске
            // ------------------------------------------------------------------
            int files = 0, badFiles = 0;
            var badList = new List<string>();
            var byName = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string d in dirs)
            {
                if (!Directory.Exists(d))
                {
                    Console.Error.WriteLine("НЕТ КАТАЛОГА: " + d);
                    return 1;
                }

                string[] found = Directory.GetFiles(d, "*.in");
                Array.Sort(found, StringComparer.Ordinal);
                foreach (string path in found)
                {
                    files++;
                    GeometryModel g = GeometryModel.Load(path);

                    // Кривая каждой геометрии — ЦЕНА правки числом. Два прогона
                    // разных сборок сводятся построчно, и «не сдвинулось ни у
                    // одной» становится проверяемым, а не обещанным.
                    if (curves.Length > 0)
                    {
                        var line = new StringBuilder(path);
                        var sim = new EfficiencySimulator(g) { Histories = 20000 };
                        foreach (double energy in new double[] { 50, 100, 300, 662, 1461, 2614 })
                        {
                            double err;
                            line.Append('\t').Append(sim.Efficiency(energy, out err)
                                                        .ToString("R", CultureInfo.InvariantCulture));
                        }

                        curveLines.Add(line.ToString());
                    }

                    bool bad = false;
                    foreach (GeometryMaterial m in new GeometryMaterial[]
                             { g.Crystal, g.Reflector, g.Cladding, g.BeakerWall, g.Source })
                    {
                        if (m == null)
                        {
                            continue;
                        }

                        if (!Ascending(m))
                        {
                            bad = true;
                        }

                        if (m.Name.Length > 0 && m.Fractions.Count > 0 && !byName.ContainsKey(m.Name))
                        {
                            byName[m.Name] = Order(m.Fractions);
                        }
                    }

                    if (bad)
                    {
                        badFiles++;
                        badList.Add(path);
                    }
                }
            }

            say("");
            say(string.Format(CultureInfo.InvariantCulture,
                              "ГЕОМЕТРИИ: файлов {0}, с несортированным составом {1}", files, badFiles));
            foreach (string p in badList)
            {
                say("    " + p);
            }

            // ------------------------------------------------------------------
            // 3. Файл против библиотеки — по ИМЕНИ вещества
            // ------------------------------------------------------------------
            say("");
            say("ФАЙЛ ПРОТИВ БИБЛИОТЕКИ (порядок Z у одноимённого вещества)");
            int compared = 0, differ = 0, absent = 0;
            List<GeometryMaterialLibrary.Entry> lib = user ?? seed;
            var names = new List<string>(byName.Keys);
            names.Sort(StringComparer.Ordinal);
            foreach (string name in names)
            {
                GeometryMaterialLibrary.Entry e = Find(lib, name);
                if (e == null)
                {
                    absent++;
                    continue;
                }

                GeometryMaterial made = GeometryMaterialLibrary.Make(e, 0.0);
                string fromLib = Order(made.Fractions);
                compared++;
                if (!string.Equals(fromLib, byName[name], StringComparison.Ordinal))
                {
                    differ++;
                    say(string.Format(CultureInfo.InvariantCulture,
                                      "    {0,-34} файл [{1}]   библиотека [{2}]",
                                      name, byName[name], fromLib));
                }
            }

            say(string.Format(CultureInfo.InvariantCulture,
                              "    сверено {0}, разошлось {1}, нет в библиотеке {2}",
                              compared, differ, absent));

            // ------------------------------------------------------------------
            // 4. Сам дефект `A162`: «в редакторе одно, после сохранения другое»
            // ------------------------------------------------------------------
            //
            // ⛔ Это и есть проверяемое утверждение, а не описание. Сцена
            // собирается ДВАЖДЫ и обе считаются одним зерном:
            //   А — вещество взято из БИБЛИОТЕКИ, как делает редактор;
            //   Б — та же сцена сохранена в `.in` и прочитана обратно.
            // До правки кривые расходились при совпадающих полях, тексте и
            // клейме — на 0.13…3.14 %; после правки обязаны сойтись в пределах
            // ТОЧНОСТИ ЗАПИСИ (порог ниже, у самой проверки).
            if (scene.Length > 0)
            {
                say("");
                say("РЕДАКТОР ПРОТИВ ФАЙЛА: " + Path.GetFileName(scene));
                GeometryModel a = GeometryModel.Load(scene);
                int replaced = 0;
                foreach (GeometryMaterial m in new GeometryMaterial[]
                         { a.Crystal, a.Reflector, a.Cladding, a.BeakerWall, a.Source })
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

                    m.Fractions.Clear();
                    foreach (KeyValuePair<int, double> pair in made.Fractions)
                    {
                        m.Fractions[pair.Key] = pair.Value;
                    }

                    replaced++;
                }

                say(string.Format(CultureInfo.InvariantCulture,
                                  "    веществ взято из библиотеки: {0}", replaced));
                foreach (GeometryMaterial m in new GeometryMaterial[]
                         { a.Crystal, a.Reflector, a.Cladding, a.BeakerWall, a.Source })
                {
                    say(string.Format(CultureInfo.InvariantCulture,
                                      "    {0,-34} [{1}]", m.Name, Order(m.Fractions)));
                }

                string tmp = Path.Combine(Path.GetTempPath(),
                                          "bq_a162_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".in");
                GeometryWriter.Save(a, tmp);
                GeometryModel b = GeometryModel.Load(tmp);
                File.Delete(tmp);

                var options = new ResponseMatrixOptions();
                bool sameStamp = string.Equals(ResponseMatrix.ComputeStamp(a, options),
                                               ResponseMatrix.ComputeStamp(b, options),
                                               StringComparison.Ordinal);
                say("    клеймо совпало: " + (sameStamp ? "да" : "НЕТ"));

                var sa = new EfficiencySimulator(a) { Histories = 60000 };
                var sb = new EfficiencySimulator(b) { Histories = 60000 };
                int moved = 0;
                foreach (double energy in new double[] { 50, 100, 300, 662, 1461, 2614 })
                {
                    double err;
                    double ea = sa.Efficiency(energy, out err);
                    double eb = sb.Efficiency(energy, out err);
                    double delta = ea > 0.0 ? (eb / ea - 1.0) * 100.0 : 0.0;

                    // ⛔ Порог берётся у ФОРМАТА, а не «чтобы прошло», и он тот
                    // же, что у `RoundTrip`: писатель кладёт `.in` в
                    // сантиметрах через `G8`, восемь значащих, — значит круг
                    // вправе сдвинуть кривую на 1e-7 относительных. Требовать
                    // побитового совпадения тут нельзя: обе стороны пришли из
                    // РАЗНЫХ источников (одна из памяти, вторая из файла), и
                    // разряд округления записи ловился бы как дефект порядка.
                    bool ok = Math.Abs(delta) <= 1e-5;
                    if (!ok)
                    {
                        moved++;
                    }

                    say(string.Format(CultureInfo.InvariantCulture,
                                      "    {0,6:F0} кэВ: {1:E10} / {2:E10}  {3}  (|откл| {4:E2} %)",
                                      energy, ea, eb,
                                      ok ? (ea == eb ? "побитово" : "в пределах формата") : "РАЗОШЛОСЬ",
                                      Math.Abs(delta)));
                }

                say(string.Format(CultureInfo.InvariantCulture,
                                  "    РАСХОЖДЕНИЙ ПО КРИВОЙ: {0} из 6", moved));
                if (moved > 0)
                {
                    bad162 = true;
                }
            }

            if (report.Length > 0)
            {
                string dir = Path.GetDirectoryName(report);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(report, text.ToString(), new UTF8Encoding(false));
            }

            if (curves.Length > 0)
            {
                var body = new StringBuilder();
                foreach (string l in curveLines)
                {
                    body.Append(l).Append("\r\n");
                }

                File.WriteAllText(curves, body.ToString(), new UTF8Encoding(false));
                say("кривые: " + curves + " (строк " + curveLines.Count.ToString(CultureInfo.InvariantCulture) + ")");
            }

            return bad162 ? 2 : 0;
        }

        static bool bad162;
        static readonly List<string> curveLines = new List<string>();

        static void Report(Action<string> say, string title, List<GeometryMaterialLibrary.Entry> list)
        {
            int bad = 0;
            var names = new List<string>();
            foreach (GeometryMaterialLibrary.Entry e in list)
            {
                GeometryMaterial m;
                try
                {
                    m = GeometryMaterialLibrary.Make(e, 0.0);
                }
                catch (Exception ex)
                {
                    say("    " + e.Name + ": " + ex.GetType().Name);
                    continue;
                }

                if (!Ascending(m))
                {
                    bad++;
                    names.Add(string.Format(CultureInfo.InvariantCulture,
                                            "{0,-34} [{1}]", e.Name, Order(m.Fractions)));
                }
            }

            say(string.Format(CultureInfo.InvariantCulture,
                              "{0}: веществ {1}, НЕ по возрастанию Z — {2}", title, list.Count, bad));
            foreach (string n in names)
            {
                say("    " + n);
            }
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

        static bool Ascending(GeometryMaterial m)
        {
            int prev = int.MinValue;
            foreach (KeyValuePair<int, double> pair in m.Fractions)
            {
                if (pair.Key < prev)
                {
                    return false;
                }

                prev = pair.Key;
            }

            return true;
        }

        static string Order(Dictionary<int, double> f)
        {
            var sb = new StringBuilder();
            foreach (KeyValuePair<int, double> pair in f)
            {
                if (sb.Length > 0)
                {
                    sb.Append(' ');
                }

                sb.Append(pair.Key.ToString(CultureInfo.InvariantCulture));
            }

            return sb.ToString();
        }
    }
}
