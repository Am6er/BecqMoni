using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace GapDefaultProbe
{
    /// <summary>
    /// Приёмка `AMBER47` (П98, 17.09.2026): зазор между отражателем и корпусом
    /// БЕЗ вещества получает воздух (`Air, dry`) на каждом входе геометрии в
    /// приложение, а ЗАДАННОЕ вещество не подменяется. Слово Amber дословно:
    /// «Занеси в шаблоны приложения - что там GAP по умолчанию - воздух (Air,
    /// dry). Если это влияет на корпус - перезапускай.»
    ///
    /// Что проверяется и почему именно это:
    ///
    /// (а) `.in` БЕЗ ключей зазора (`G1S_point5.in`, таковы 55 из 61 файлов
    ///     корпуса и моделей) → воздух из библиотеки: имя, плотность 0.001205,
    ///     состав N/O.
    /// (б) `.in` С явным зазором (`AS80_point0.in`) → воздух ИЗ ФАЙЛА: доли
    ///     ровно те шесть знаков, что в файле, а не библиотечные — иначе
    ///     умолчание подменяло бы заданное.
    /// (в) конфигурация прибора (`EfficiencyConfigData` через `XmlSerializer`):
    ///     без `&lt;Gap&gt;` → воздух; с пустым `&lt;Gap&gt;` → воздух; с водой →
    ///     ОСТАЁТСЯ вода (положительный контроль: подмены явного нет).
    /// (г) свой шаблон (`GeometryTemplate.Apply`): пустой зазор → воздух;
    ///     заданный → как задан; шаблон из XML без блока → воздух.
    /// (д) КЛЕЙМО на всех файлах `.in` корпуса, закреплённых копий и моделей:
    ///     клеймо новой геометрии против клейма той же геометрии, разобранной
    ///     ПО-СТАРОМУ (зазор без ключей → пустое вещество). Ожидание —
    ///     «разошлось 0 из 61»: это и есть ответ «на корпус не влияет,
    ///     перезапуск не нужен». Чтобы сравнение не было слепым, рядом два
    ///     контроля чувствительности: у сцен с НЕнулевым зазором снятое
    ///     вещество клеймо МЕНЯЕТ, у сцен с нулевым — вода в зазоре клеймо
    ///     НЕ меняет (`dropGap`).
    ///     `--store=&lt;каталог&gt;` (только чтение): каждая матрица `&lt;ключ&gt;.rmx`
    ///     проверяется `IsValidFor` против новой геометрии `&lt;ключ&gt;.in`.
    /// (е) редактор: `GeometryEditorPanel` без окна, загрузка файла без
    ///     вещества зазора → в списке «Gap» выбран `Air, dry`. Рядом — что
    ///     сам редактор умолчание НЕ ставит (`A303` в силе): геометрия с
    ///     пустым зазором, собранная в памяти мимо входов, даёт пустой список.
    ///
    /// Положительный контроль всей пробы — ключ `--break`: воспроизводит
    /// поведение ДО правки (после каждого входа зазор без ключей снова
    /// пустой) и обязан дать код 1.
    ///
    ///   GapDefaultProbe.exe [--root=&lt;корень репозитория&gt;] [--store=&lt;каталог *.rmx&gt;]
    ///                       [--break] [--no-editor]
    /// </summary>
    static class Program
    {
        static int bad;
        static bool simulateOld;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentUICulture = new CultureInfo("en");

            string root = null;
            string store = null;
            bool editor = true;
            foreach (string a in args)
            {
                if (a.StartsWith("--root=", StringComparison.Ordinal)) root = a.Substring(7);
                else if (a.StartsWith("--store=", StringComparison.Ordinal)) store = a.Substring(8);
                else if (a == "--break") simulateOld = true;
                else if (a == "--no-editor") editor = false;
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: " + a);
                    return 2;
                }
            }

            if (root == null)
            {
                root = FindRoot();
            }

            if (root == null || !Directory.Exists(Path.Combine(root, "tools", "CORPUS", "corpus", "geometries")))
            {
                Console.Error.WriteLine("корень репозитория не найден; задайте --root=");
                return 2;
            }

            Say("корень: {0}", root);
            Say("режим: {0}", simulateOld ? "--break (поведение ДО правки)" : "штатный");

            // Библиотека веществ читает matdb — база лежит рядом с пробой.
            GlobalConfigManager.GetInstance();
            // Свои шаблоны — во временный файл, чтобы панель не читала и тем
            // более не писала чужой.
            string tempDir = Path.Combine(Path.GetTempPath(), "gapdefault_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            GeometryTemplateStore.PathOverride = Path.Combine(tempDir, "GeometryTemplates.xml");
            GeometryTemplateStore.Reload();

            string geometries = Path.Combine(root, "tools", "CORPUS", "corpus", "geometries");
            try
            {
                InWithoutGap(Path.Combine(geometries, "G1S_point5.in"));
                InWithGap(Path.Combine(geometries, "AS80_point0.in"));
                Xml();
                Template();
                Stamps(root, store);
                if (editor)
                {
                    Editor(Path.Combine(geometries, "G1S_point5.in"));
                }
            }
            catch (Exception e)
            {
                Fail("проба оборвалась: " + e);
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch (IOException) { }
            }

            Say("");
            Say(bad == 0 ? "ВСЕ СОШЛИСЬ" : "РАСХОЖДЕНИЙ: {0}", bad);
            return bad == 0 ? 0 : 1;
        }

        static string FindRoot()
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 8 && dir != null; i++)
            {
                if (Directory.Exists(Path.Combine(dir, "tools", "CORPUS", "corpus", "geometries")))
                {
                    return dir;
                }

                dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
            }

            return null;
        }

        // ------------------------------------------------------------------
        // Поведение ДО правки — для положительного контроля `--break`
        // ------------------------------------------------------------------

        /// <summary>
        /// Пустое ли вещество зазора оставил бы СТАРЫЙ разбор `.in`: судит по
        /// сырым ключам файла — имя, плотность и доли, ровно как
        /// `GeometryModel.Material`.
        /// </summary>
        static bool OldGapWasEmpty(GeometryModel g)
        {
            string name;
            if (g.Raw.TryGetValue("M_DS_Gap.MName", out name) && name.Trim().Length > 0)
            {
                return false;
            }

            string ro;
            double density;
            if (g.Raw.TryGetValue("DS_RoCrystalGap", out ro)
                && double.TryParse(ro.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out density)
                && density > 0.0)
            {
                return false;
            }

            for (int i = 0; i < 24; i++)
            {
                string z, f;
                double zv, fv;
                if (g.Raw.TryGetValue("DS_ZCrystalGap[" + i.ToString(CultureInfo.InvariantCulture) + "]", out z)
                    && g.Raw.TryGetValue("DS_FractionsCrystalGap[" + i.ToString(CultureInfo.InvariantCulture) + "]", out f)
                    && double.TryParse(z.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out zv)
                    && double.TryParse(f.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out fv)
                    && zv > 0 && fv > 0.0)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Под `--break` — вернуть зазор в то, что оставлял старый разбор.</summary>
        static GeometryModel Loaded(string path)
        {
            GeometryModel g = GeometryModel.Load(path);
            if (simulateOld && OldGapWasEmpty(g))
            {
                g.Gap = new GeometryMaterial();
            }

            return g;
        }

        // ------------------------------------------------------------------
        // (а) .in без ключей зазора
        // ------------------------------------------------------------------

        static void InWithoutGap(string path)
        {
            Head("(а) .in без ключей зазора: " + Path.GetFileName(path));
            // Цена первого умолчания — подъём базы веществ (`matdb.sqlite`):
            // первый разбор её поднимает, второй уже нет. Число — в журнал.
            System.Diagnostics.Stopwatch first = System.Diagnostics.Stopwatch.StartNew();
            GeometryModel g = Loaded(path);
            first.Stop();
            System.Diagnostics.Stopwatch second = System.Diagnostics.Stopwatch.StartNew();
            Loaded(path);
            second.Stop();
            Say("первый разбор (с подъёмом базы веществ): {0} мс; второй: {1} мс",
                first.ElapsedMilliseconds, second.ElapsedMilliseconds);
            Check("в файле нет ключей зазора", false, g.Raw.ContainsKey("DS_RoCrystalGap"));
            Say("толщина зазора: торец {0:R} мм, бок {1:R} мм", g.FrontGapThickness, g.SideGapThickness);
            Say("вещество: \"{0}\", плотность {1:R}, состав {2}", g.Gap.Name, g.Gap.Density, Fractions(g.Gap));
            Check("имя", "Air, dry", g.Gap.Name);
            Check("плотность", 0.001205, g.Gap.Density, 1e-12);
            // ⚠ (П123 22.09.2026) Здесь состав ЧИТАЕТСЯ ИЗ ФАЙЛА, а не берётся
            // умолчанием: `G1S_point5.in` в дереве с 18.09.2026 (`1c406579`, B30)
            // несёт блок зазора (`N2 O1`, два элемента) — посылка раздела (а)
            // «файл БЕЗ ключей зазора» устарела, о чём говорит и первая
            // проверка выше. Ожидание «2» — состав ЭТОГО файла; умолчание засева
            // (4 элемента NIST, с аргоном, `AMBER53`) меряется разделами (в) и
            // (г) ниже.
            Check("элементов", 2, g.Gap.Fractions.Count);
            Check("есть азот", true, g.Gap.Fractions.ContainsKey(7));
            Check("есть кислород", true, g.Gap.Fractions.ContainsKey(8));
            Check("IsEmpty снят", false, g.Gap.IsEmpty);
            Check("предупреждений разбора нет", 0, g.Warnings.Count);
            Say("предупреждения: {0}", g.Warnings.Count == 0 ? "нет" : string.Join(" | ", g.Warnings.ToArray()));
        }

        // ------------------------------------------------------------------
        // (б) .in с явным зазором
        // ------------------------------------------------------------------

        static void InWithGap(string path)
        {
            Head("(б) .in с явным зазором: " + Path.GetFileName(path));
            GeometryModel g = Loaded(path);
            Check("в файле есть ключи зазора", true, g.Raw.ContainsKey("DS_RoCrystalGap"));
            Say("толщина зазора: торец {0:R} мм, бок {1:R} мм", g.FrontGapThickness, g.SideGapThickness);
            Say("вещество: \"{0}\", плотность {1:R}, состав {2}", g.Gap.Name, g.Gap.Density, Fractions(g.Gap));
            Check("имя из файла", g.Raw["M_DS_Gap.MName"].Trim(), g.Gap.Name);
            Check("плотность из файла",
                  double.Parse(g.Raw["DS_RoCrystalGap"].Trim(), CultureInfo.InvariantCulture),
                  g.Gap.Density, 0.0);
            for (int i = 0; i < 24; i++)
            {
                string zk = "DS_ZCrystalGap[" + i.ToString(CultureInfo.InvariantCulture) + "]";
                string fk = "DS_FractionsCrystalGap[" + i.ToString(CultureInfo.InvariantCulture) + "]";
                if (!g.Raw.ContainsKey(zk)) continue;
                int z = (int)double.Parse(g.Raw[zk].Trim(), CultureInfo.InvariantCulture);
                double f = double.Parse(g.Raw[fk].Trim(), CultureInfo.InvariantCulture);
                double have;
                Check("доля Z=" + z + " есть", true, g.Gap.Fractions.TryGetValue(z, out have));
                Check("доля Z=" + z + " ровно из файла", f, have, 0.0);
            }

            // Библиотечный воздух — те же элементы, но доли с полной точностью
            // атомных масс, а не шесть знаков файла: если они РАЗЛИЧИМЫ, то
            // «из файла, не из библиотеки» — проверка, а не тавтология.
            GeometryMaterial lib = GeometryModel.DefaultGapMaterial();
            Say("библиотечный воздух для сравнения: плотность {0:R}, состав {1}", lib.Density, Fractions(lib));
            bool distinguishable = false;
            foreach (KeyValuePair<int, double> pair in lib.Fractions)
            {
                double have;
                if (g.Gap.Fractions.TryGetValue(pair.Key, out have) && have != pair.Value)
                {
                    distinguishable = true;
                }
            }

            Say("файл и библиотека различимы по долям: {0}", distinguishable ? "да" : "НЕТ (проверка слепа)");
            Check("доли файла отличимы от библиотечных", true, distinguishable);
        }

        // ------------------------------------------------------------------
        // (в) конфигурация прибора через XmlSerializer
        // ------------------------------------------------------------------

        static void Xml()
        {
            Head("(в) EfficiencyConfigData через XmlSerializer");
            XmlSerializer serializer = new XmlSerializer(typeof(EfficiencyConfigData));

            // Исходник — воздух в зазоре, как его оставляет заготовка.
            EfficiencyConfigData source = new EfficiencyConfigData();
            GeometryModel g = GeometryEditorPanel.Blank();
            g.FrontGapThickness = 2.5;
            g.CrystalDiameter = 76.2;
            source.Geometry = g;
            string full = Serialize(serializer, source);
            Check("в XML есть блок <Gap>", true, full.IndexOf("<Gap>", StringComparison.Ordinal) >= 0);

            // 1. Без <Gap> вовсе — конфигурация до 08.09.2026.
            string without = Regex.Replace(full, @"\s*<Gap>.*?</Gap>", "", RegexOptions.Singleline);
            Check("блок <Gap> вырезан", false, without.IndexOf("<Gap", StringComparison.Ordinal) >= 0);
            EfficiencyConfigData a = Deserialize(serializer, without);
            Report("без <Gap>", a);
            Check("без <Gap>: диаметр кристалла пережил", 76.2, a.Geometry.CrystalDiameter, 1e-12);
            Check("без <Gap>: толщина зазора пережила", 2.5, a.Geometry.FrontGapThickness, 1e-12);
            Check("без <Gap>: имя", "Air, dry", a.Geometry.Gap.Name);
            Check("без <Gap>: плотность", 0.001205, a.Geometry.Gap.Density, 1e-12);
            Check("без <Gap>: элементов", 4, a.Geometry.Gap.Fractions.Count);

            // 2. Пустой блок — пустое имя, плотность 0, без долей.
            string empty = Regex.Replace(full, @"<Gap>.*?</Gap>",
                                         "<Gap><Name></Name><Density>0</Density><Fractions /></Gap>",
                                         RegexOptions.Singleline);
            Check("пустой блок вставлен", true, empty.IndexOf("<Density>0</Density><Fractions />", StringComparison.Ordinal) >= 0);
            EfficiencyConfigData b = Deserialize(serializer, empty);
            Report("пустой <Gap>", b);
            Check("пустой <Gap>: имя", "Air, dry", b.Geometry.Gap.Name);
            Check("пустой <Gap>: плотность", 0.001205, b.Geometry.Gap.Density, 1e-12);
            Check("пустой <Gap>: элементов", 4, b.Geometry.Gap.Fractions.Count);

            // 3. Вода — ЗАДАННОЕ вещество, положительный контроль подмены.
            EfficiencyConfigData waterSource = new EfficiencyConfigData();
            GeometryModel wg = GeometryEditorPanel.Blank();
            wg.FrontGapThickness = 21.7;
            wg.Gap = Library("Water, liquid");
            Check("вода из библиотеки нашлась", "Water, liquid", wg.Gap.Name);
            waterSource.Geometry = wg;
            Check("вода не подменена при присвоении", "Water, liquid", waterSource.Geometry.Gap.Name);
            string water = Serialize(serializer, waterSource);
            EfficiencyConfigData c = Deserialize(serializer, water);
            Report("<Gap> = вода", c);
            Check("вода: имя осталось", "Water, liquid", c.Geometry.Gap.Name);
            Check("вода: плотность осталась", 1.0, c.Geometry.Gap.Density, 1e-12);
            Check("вода: элементов", 2, c.Geometry.Gap.Fractions.Count);
            Check("вода: есть водород", true, c.Geometry.Gap.Fractions.ContainsKey(1));

            // 4. Имя без состава — тоже ЗАДАННОЕ (IsEmpty ложен), не подменяется.
            string named = Regex.Replace(full, @"<Gap>.*?</Gap>",
                                         "<Gap><Name>Foam</Name><Density>0</Density><Fractions /></Gap>",
                                         RegexOptions.Singleline);
            EfficiencyConfigData d = Deserialize(serializer, named);
            Report("<Gap> = имя без состава", d);
            Check("имя без состава: не подменено", "Foam", d.Geometry.Gap.Name);
            Check("имя без состава: IsEmpty ложен", false, d.Geometry.Gap.IsEmpty);

            // 5. Геометрии нет вовсе — сеттер с null не падает.
            EfficiencyConfigData e = Deserialize(serializer, Regex.Replace(full, @"\s*<Geometry>.*?</Geometry>", "", RegexOptions.Singleline));
            Check("без <Geometry>: геометрии нет", false, e.HasGeometry);

            // Круг «прочитать → записать → прочитать» с воздухом устойчив.
            string again = Serialize(serializer, a);
            EfficiencyConfigData f = Deserialize(serializer, again);
            Check("повторный круг: имя", "Air, dry", f.Geometry.Gap.Name);
            Check("повторный круг: состав", Fractions(a.Geometry.Gap), Fractions(f.Geometry.Gap));
        }

        static void Report(string what, EfficiencyConfigData config)
        {
            GeometryMaterial m = config.Geometry != null ? config.Geometry.Gap : null;
            Say("{0,-24} → вещество \"{1}\", плотность {2:R}, состав {3}",
                what, m == null ? "(нет геометрии)" : m.Name, m == null ? 0.0 : m.Density,
                m == null ? "-" : Fractions(m));
        }

        static string Serialize(XmlSerializer serializer, EfficiencyConfigData config)
        {
            StringWriter buffer = new StringWriter(CultureInfo.InvariantCulture);
            serializer.Serialize(buffer, config);
            return buffer.ToString();
        }

        static EfficiencyConfigData Deserialize(XmlSerializer serializer, string xml)
        {
            using (StringReader reader = new StringReader(xml))
            {
                EfficiencyConfigData config = (EfficiencyConfigData)serializer.Deserialize(reader);
                if (simulateOld && config.Geometry != null && config.Geometry.Gap != null
                    && config.Geometry.Gap.Name == "Air, dry"
                    && !Regex.IsMatch(xml, @"<Gap>\s*<Name>\s*Air, dry\s*</Name>"))
                {
                    // До правки сеттер ничего не ставил: воздух, которого в
                    // блоке <Gap> не было (в пробе воздухом стоит ещё и ПРОБА —
                    // потому ищется именно блок зазора), — след правки; убираем.
                    config.Geometry.Gap = new GeometryMaterial();
                }

                return config;
            }
        }

        // ------------------------------------------------------------------
        // (г) свой шаблон
        // ------------------------------------------------------------------

        static void Template()
        {
            Head("(г) GeometryTemplate.Apply");

            // Пустой зазор в шаблоне (заготовка класса).
            GeometryTemplate t = new GeometryTemplate { Name = "p98-empty" };
            t.CrystalDiameter = 40.0;
            t.CrystalHeight = 40.0;
            t.FrontGapThickness = 1.0;
            GeometryModel g = GeometryEditorPanel.Blank();
            g.Gap = new GeometryMaterial();
            t.Apply(g);
            if (simulateOld) g.Gap = new GeometryMaterial();
            Say("пустой зазор шаблона → \"{0}\", {1:R}, {2}", g.Gap.Name, g.Gap.Density, Fractions(g.Gap));
            Check("пустой шаблон: имя", "Air, dry", g.Gap.Name);
            Check("пустой шаблон: плотность", 0.001205, g.Gap.Density, 1e-12);
            Check("пустой шаблон: элементов", 4, g.Gap.Fractions.Count);
            Check("пустой шаблон: диаметр перенесён", 40.0, g.CrystalDiameter, 1e-12);

            // Заданное вещество шаблона переносится как есть.
            GeometryTemplate w = new GeometryTemplate { Name = "p98-water" };
            w.Gap = Library("Water, liquid");
            GeometryModel g2 = GeometryEditorPanel.Blank();
            w.Apply(g2);
            Say("вода в шаблоне → \"{0}\", {1:R}, {2}", g2.Gap.Name, g2.Gap.Density, Fractions(g2.Gap));
            Check("вода в шаблоне: имя", "Water, liquid", g2.Gap.Name);
            Check("вода в шаблоне: плотность", 1.0, g2.Gap.Density, 1e-12);

            // Шаблон из XML без блока <Gap>.
            XmlSerializer serializer = new XmlSerializer(typeof(GeometryTemplateConfig));
            GeometryTemplateConfig cfg = new GeometryTemplateConfig { Templates = new[] { t } };
            StringWriter buffer = new StringWriter(CultureInfo.InvariantCulture);
            serializer.Serialize(buffer, cfg);
            string xml = Regex.Replace(buffer.ToString(), @"\s*<Gap>.*?</Gap>", "", RegexOptions.Singleline);
            Check("в XML шаблона блока <Gap> нет", false, xml.IndexOf("<Gap", StringComparison.Ordinal) >= 0);
            GeometryTemplateConfig back;
            using (StringReader reader = new StringReader(xml))
            {
                back = (GeometryTemplateConfig)serializer.Deserialize(reader);
            }

            Check("шаблон прочитан", 1, back.Templates == null ? 0 : back.Templates.Length);
            GeometryModel g3 = GeometryEditorPanel.Blank();
            g3.Gap = new GeometryMaterial();
            back.Templates[0].Apply(g3);
            if (simulateOld) g3.Gap = new GeometryMaterial();
            Say("шаблон из XML без <Gap> → \"{0}\", {1:R}, {2}", g3.Gap.Name, g3.Gap.Density, Fractions(g3.Gap));
            Check("шаблон из XML: имя", "Air, dry", g3.Gap.Name);
            Check("шаблон из XML: элементов", 4, g3.Gap.Fractions.Count);
        }

        // ------------------------------------------------------------------
        // (д) клеймо: новая геометрия против старого разбора, все .in
        // ------------------------------------------------------------------

        static void Stamps(string root, string store)
        {
            Head("(д) клеймо матриц: разбор новый против старого, все .in");
            List<string> files = new List<string>();
            files.AddRange(Directory.GetFiles(Path.Combine(root, "tools", "CORPUS", "corpus", "geometries"), "*.in"));
            files.AddRange(Directory.GetFiles(Path.Combine(root, "tools", "CORPUS", "corpus", "geometries", "pinned"), "*.in"));
            files.AddRange(Directory.GetFiles(Path.Combine(root, "tools", "effmaker", "models"), "*.in"));
            files.Sort(StringComparer.OrdinalIgnoreCase);

            ResponseMatrixOptions options = new ResponseMatrixOptions();
            int diverged = 0, withGap = 0, sensitive = 0, insensitive = 0, oldEmpty = 0;
            Say("{0,-40} {1,8} {2,-14} {3,-9} {4}", "файл", "зазор,мм", "вещество", "старое", "клеймо");
            foreach (string file in files)
            {
                GeometryModel fresh = GeometryModel.Load(file);
                bool wasEmpty = OldGapWasEmpty(fresh);
                if (wasEmpty) oldEmpty++;

                // Старый разбор: то же, но у файла без ключей зазора вещество пустое.
                GeometryModel old = fresh.Clone();
                if (wasEmpty)
                {
                    old.Gap = new GeometryMaterial();
                }

                // `--break` этот раздел не трогает: клеймо — измерение сцены, а
                // не поведения входов; его слепоту ловят два контроля ниже.
                string sNew = ResponseMatrix.ComputeStamp(fresh, options);
                string sOld = ResponseMatrix.ComputeStamp(old, options);
                bool same = string.Equals(sNew, sOld, StringComparison.Ordinal);
                if (!same) diverged++;

                double thickness = Math.Max(fresh.FrontGapThickness, fresh.SideGapThickness);
                string rel = MakeRelative(root, file);
                Say("{0,-40} {1,8:F2} {2,-14} {3,-9} {4}", rel, thickness,
                    fresh.Gap.Name.Length > 0 ? fresh.Gap.Name : "(пусто)",
                    wasEmpty ? "пусто" : "из файла",
                    same ? "совпало" : "РАЗОШЛОСЬ " + Short(sNew) + " / " + Short(sOld));

                // Контроли чувствительности — на настоящем разборе, без --break.
                GeometryModel voided = fresh.Clone();
                voided.Gap = new GeometryMaterial();
                GeometryModel watered = fresh.Clone();
                watered.Gap = Library("Water, liquid");
                if (thickness > 0.0)
                {
                    withGap++;
                    if (!string.Equals(sNew, ResponseMatrix.ComputeStamp(voided, options), StringComparison.Ordinal))
                    {
                        sensitive++;
                    }
                }
                else if (string.Equals(sNew, ResponseMatrix.ComputeStamp(watered, options), StringComparison.Ordinal))
                {
                    insensitive++;
                }
            }

            Say("");
            Say("файлов: {0}; у старого разбора зазор пустой: {1}; с ненулевым зазором: {2}",
                files.Count, oldEmpty, withGap);
            Say("разошлось {0} из {1}", diverged, files.Count);
            Check("файлов ровно 61", 61, files.Count);
            Check("разошлось 0", 0, diverged);
            Say("контроль: ненулевой зазор — снятое вещество меняет клеймо у {0} из {1}", sensitive, withGap);
            Check("ненулевой зазор чувствителен к веществу", withGap, sensitive);
            Check("ненулевых зазоров ровно 6", 6, withGap);
            Say("контроль: нулевой зазор — вода в зазоре клеймо НЕ меняет у {0} из {1}",
                insensitive, files.Count - withGap);
            Check("нулевой зазор к веществу нечувствителен", files.Count - withGap, insensitive);

            if (string.IsNullOrEmpty(store))
            {
                Say("склад не задан (--store=), сверка с матрицами пропущена");
                return;
            }

            Say("");
            Say("склад (только чтение): {0}", store);
            string[] matrices = Directory.GetFiles(store, "*.rmx");
            int valid = 0, missing = 0, total = 0;
            foreach (string rmx in matrices)
            {
                string key = Path.GetFileNameWithoutExtension(rmx);
                string inPath = Path.Combine(root, "tools", "CORPUS", "corpus", "geometries", key + ".in");
                if (!File.Exists(inPath))
                {
                    missing++;
                    Say("  {0,-32} сцены .in нет — пропуск", key);
                    continue;
                }

                total++;
                ResponseMatrix matrix = ResponseMatrix.Load(rmx);
                GeometryModel g = GeometryModel.Load(inPath);
                bool ok = matrix.IsValidFor(g);
                if (ok) valid++;
                Say("  {0,-32} {1} phys={2} {3}", key, ok ? "годна" : "НЕ ГОДНА", matrix.PhysicsVersionOf(),
                    ok ? "" : Short(matrix.Stamp) + " / " + Short(ResponseMatrix.ComputeStamp(g, matrix.Options)));
            }

            Say("матриц: {0}, годных против новой геометрии: {1}, без сцены: {2}", total, valid, missing);
            Check("все матрицы склада годны против новой геометрии", total, valid);
        }

        static string PhysicsVersionOf(this ResponseMatrix matrix)
        {
            string stamp = matrix.Stamp ?? "";
            int semi = stamp.IndexOf(';');
            return semi > 0 ? stamp.Substring(0, semi).Replace("phys=", "") : "?";
        }

        static string MakeRelative(string root, string file)
        {
            string r = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string rel = file.StartsWith(r, StringComparison.OrdinalIgnoreCase) ? file.Substring(r.Length) : file;
            rel = rel.Replace("tools\\CORPUS\\corpus\\geometries\\", "corpus\\").Replace("tools\\effmaker\\models\\", "models\\");
            return rel;
        }

        static string Short(string stamp)
        {
            int semi = stamp.IndexOf(';');
            string hex = semi >= 0 ? stamp.Substring(semi + 1) : stamp;
            return hex.Length > 12 ? hex.Substring(0, 12) : hex;
        }

        // ------------------------------------------------------------------
        // (е) редактор без окна
        // ------------------------------------------------------------------

        static void Editor(string path)
        {
            Head("(е) GeometryEditorPanel без окна: " + Path.GetFileName(path));
            Application.EnableVisualStyles();
            using (GeometryEditorPanel panel = new GeometryEditorPanel())
            {
                panel.CreateControl();
                FieldInfo field = typeof(GeometryEditorPanel).GetField("materials", BindingFlags.Instance | BindingFlags.NonPublic);
                Check("поле materials найдено", true, field != null);
                Dictionary<string, ComboBox> combos = (Dictionary<string, ComboBox>)field.GetValue(panel);
                Check("список Gap есть", true, combos.ContainsKey("Gap"));
                ComboBox gap = combos["Gap"];

                // Штатный вход: файл без вещества зазора через разбор.
                panel.SetModel(Loaded(path));
                GeometryMaterialLibrary.Entry chosen = gap.SelectedItem as GeometryMaterialLibrary.Entry;
                Say("файл без зазора → список «Gap»: индекс {0}, выбрано \"{1}\"",
                    gap.SelectedIndex, chosen == null ? "(пусто)" : chosen.Name);
                Check("в списке выбран воздух", "Air, dry", chosen == null ? "(пусто)" : chosen.Name);
                Check("модель панели: воздух", "Air, dry", panel.Model.Gap.Name);

                // Сам редактор умолчание НЕ ставит (`A303`): пустой зазор,
                // собранный в памяти мимо входов, остаётся пустым списком.
                GeometryModel bare = GeometryEditorPanel.Blank();
                bare.Gap = new GeometryMaterial();
                panel.SetModel(bare);
                Say("пустой зазор мимо входов → список «Gap»: индекс {0}", gap.SelectedIndex);
                Check("редактор сам не подставляет (A303)", -1, gap.SelectedIndex);
            }
        }

        // ------------------------------------------------------------------
        // Служебное
        // ------------------------------------------------------------------

        static GeometryMaterial Library(string name)
        {
            GeometryMaterialLibrary.Entry entry = GeometryMaterialLibrary.ByName(name);
            return entry != null ? GeometryMaterialLibrary.Make(entry, entry.Density) : new GeometryMaterial();
        }

        static string Fractions(GeometryMaterial m)
        {
            if (m == null || m.Fractions.Count == 0) return "(нет)";
            List<int> keys = new List<int>(m.Fractions.Keys);
            keys.Sort();
            StringBuilder sb = new StringBuilder();
            foreach (int z in keys)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(z.ToString(CultureInfo.InvariantCulture)).Append(':')
                  .Append(m.Fractions[z].ToString("R", CultureInfo.InvariantCulture));
            }

            return sb.ToString();
        }

        static void Head(string title)
        {
            Say("");
            Say("=== {0} ===", title);
        }

        static void Check(string what, double expected, double got, double tol)
        {
            if (Math.Abs(expected - got) <= tol) return;
            Fail(string.Format(CultureInfo.InvariantCulture, "{0}: ожидалось {1:R}, вышло {2:R}", what, expected, got));
        }

        static void Check(string what, object expected, object got)
        {
            if (Equals(expected, got)) return;
            Fail(string.Format(CultureInfo.InvariantCulture, "{0}: ожидалось {1}, вышло {2}", what, expected, got));
        }

        static void Fail(string message)
        {
            bad++;
            Say("  ✗ {0}", message);
        }

        static void Say(string format, params object[] args)
        {
            Console.WriteLine(args.Length == 0 ? format : string.Format(CultureInfo.InvariantCulture, format, args));
        }
    }
}
