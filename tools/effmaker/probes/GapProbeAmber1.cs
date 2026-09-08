using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace GapProbeAmber1
{
    /// <summary>
    /// Приёмка задачи `AMBER1` — зазора между отражателем и корпусом.
    ///
    /// Что проверяется и ПОЧЕМУ именно это:
    ///
    /// 1. **Пресеты.** 21.7 мм стоит у `Atom Spectra Pro 80x80` и НИ У КОГО
    ///    больше, а наполнитель у всех воздух. Контроль обязателен: пресет
    ///    накладывается на уже набранные поля, и «поставил всем» здесь ошибка
    ///    того же разряда, что «не поставил никому».
    /// 2. **Сцена переноса.** Зазор обязан стать НАСТОЯЩИМ слоем, а не числом
    ///    в модели. ⛔ Это главная проверка: ключ, не доехавший до построителя,
    ///    мёртв, и побитовое сравнение матриц такой дыры НЕ ловит — матрица
    ///    выходит верной, испорчено происхождение.
    /// 3. **Вынос кристалла.** Торцевой зазор УГЛУБЛЯЕТ кристалл под корпусом,
    ///    и передний торец сцены уезжает ровно на его толщину. Здесь же — цена
    ///    задачи: эффективность точечной сцены падает почти вдвое.
    /// 4. **Файл `.in`.** Запись и чтение обязаны сойтись, а файл БЕЗ новых
    ///    ключей — читаться нулём, иначе все прежние геометрии сменят смысл.
    /// 5. **Клеймо.** Отпечаток считается по тексту файла; зазор обязан его
    ///    менять, иначе матрица с зазором ляжет под именем матрицы без него.
    /// 6. **Чертёж.** Слой обязан быть ВИДЕН, и только когда он есть.
    /// 7. **Брусок и боковая постановка.** У бруска зазор двусторонний, и при
    ///    съёмке боком торцевой с боковым меняются местами — как вся обвязка.
    ///
    ///   gapprobeamber1
    /// </summary>
    static class Program
    {
        static int bad;

        [STAThread]
        static int Main(string[] args)
        {
            // Служебный ход: перезаписать геометрию нашим же писателем.
            // Стоит здесь, а не отдельной пробой, потому что отвечает на вопрос
            // ЭТОЙ приёмки — можно ли внести зазор в корпусный файл, ничего
            // больше в нём не сдвинув. Сравнение делает вызывающий.
            if (args.Length == 3 && args[0] == "--render")
            {
                // ⛔ Через `Save`, а не `File.WriteAllText`: формат `.in`
                // пишется кодировкой 1251, и запись текста как есть подменила
                // бы кодировку русских примечаний внутри файла.
                GeometryWriter.Save(GeometryModel.Load(args[1]), args[2]);
                Say("записано: {0}", args[2]);
                return 0;
            }

            // ⛔ Клеймо УЗЛА СПЕКТРА против клейма файла `.in` — ровно то
            // сравнение, которым разбор решает, годится ли матрица
            // (`matrix.IsValidFor(rd.Efficiency.Geometry)`). Заведено 08.09.2026:
            // после `AMBER1` встал вопрос, обязателен ли пересчёт кривых у 42
            // сцен без зазора, и отвечать на него рассуждением нельзя —
            // расхождение в одну букву отнимает матрицу МОЛЧА (`A269`).
            if (args.Length == 3 && args[0] == "--nodestamp")
            {
                GlobalConfigManager.GetInstance();
                DeviceConfigManager.GetInstance();

                ResultDataFile file;
                var ser = new System.Xml.Serialization.XmlSerializer(typeof(ResultDataFile));
                using (var stream = new FileStream(args[1], FileMode.Open, FileAccess.Read,
                                                   FileShare.Read))
                {
                    file = (ResultDataFile)ser.Deserialize(stream);
                }

                ResultData rd = file.ResultDataList[0];
                var opts = new ResponseMatrixOptions();
                string nodeStamp = rd.Efficiency == null || !rd.Efficiency.HasGeometry
                    ? "(у спектра нет геометрии)"
                    : ResponseMatrix.ComputeStamp(rd.Efficiency.Geometry, opts);
                string inStamp = ResponseMatrix.ComputeStamp(GeometryModel.Load(args[2]), opts);
                Say("узел  {0}", nodeStamp);
                Say("файл  {0}", inStamp);
                Say(nodeStamp == inStamp ? "СОШЛОСЬ" : "РАЗОШЛОСЬ");
                return nodeStamp == inStamp ? 0 : 1;
            }

            // Поставить зазор в готовый файл геометрии тем же писателем.
            if (args.Length == 3 && args[0] == "--setgap")
            {
                GeometryModel g = GeometryModel.Load(args[1]);
                g.FrontGapThickness = double.Parse(args[2], CultureInfo.InvariantCulture);
                g.SideGapThickness = 0.0;
                GeometryMaterialLibrary.Entry air = GeometryMaterialLibrary.ByName("Air, dry");
                g.Gap = GeometryMaterialLibrary.Make(air, air.Density);
                GeometryWriter.Save(g, args[1]);
                Say("{0}: торец {1:F2} мм, наполнитель {2}",
                    Path.GetFileName(args[1]), g.FrontGapThickness, g.Gap.Name);
                return 0;
            }

            string root = args.Length > 0 ? args[0] : null;

            Presets();
            Scene();
            Depth();
            FileRoundTrip();
            Stamp();
            Sketch();
            BoxAndFacing();
            Corpus(root);

            Say("");
            Say(bad == 0 ? "ВСЕ СОШЛИСЬ" : "РАСХОЖДЕНИЙ: {0}", bad);
            return bad == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------------
        // 1. Пресеты
        // ------------------------------------------------------------------

        static void Presets()
        {
            Head("Пресеты: у кого зазор и чем он налит");
            Say("{0,-26} {1}", "прибор", "торец / бок, мм ; наполнитель");
            foreach (GeometryPresets.Preset preset in GeometryPresets.Items)
            {
                GeometryModel g = GeometryEditorPanel.Blank();
                preset.Apply(g);
                Say("{0,-26} {1,8:F2} {2,8:F2}   {3} ({4:F6} г/см3)",
                    preset.Name, g.FrontGapThickness, g.SideGapThickness,
                    g.Gap.Name, g.Gap.Density);

                bool as80 = preset.Name == "Atom Spectra Pro 80x80";
                Check(preset.Name + ": торец",
                      as80 ? 21.7 : 0.0, g.FrontGapThickness, 1e-9);
                Check(preset.Name + ": бок", 0.0, g.SideGapThickness, 1e-9);
                Check(preset.Name + ": наполнитель воздух",
                      "Air, dry", g.Gap.Name);
            }

            GeometryModel blank = GeometryEditorPanel.Blank();
            Say("{0,-26} {1,8:F2} {2,8:F2}   {3}",
                "заготовка редактора", blank.FrontGapThickness, blank.SideGapThickness,
                blank.Gap.Name);
            Check("заготовка: расстояние ноль", 0.0, blank.FrontGapThickness, 1e-9);
            Check("заготовка: наполнитель воздух", "Air, dry", blank.Gap.Name);
        }

        // ------------------------------------------------------------------
        // 2. Сцена переноса
        // ------------------------------------------------------------------

        static void Scene()
        {
            Head("Сцена переноса: слои сверху вниз, см");
            GeometryModel with = As80();
            GeometryModel without = As80();
            without.FrontGapThickness = 0.0;

            List<string> a = Layers(with);
            List<string> b = Layers(without);

            Say("--- с зазором 21.7 мм ---");
            foreach (string line in a) Say("  {0}", line);
            Say("--- без зазора (контроль) ---");
            foreach (string line in b) Say("  {0}", line);

            // ⛔ Контроль на месте нарочно: без него «слой есть» не отличается
            // от «слой был всегда», и проба прошла бы на неизменённом коде.
            // ⚠ У ЦИЛИНДРА СЛОЙ ОДИН, а не два, и это верно: боковой
            // зазор у него ноль (решение Amber), а кольцо нулевой ширины
            // `Add` не строит вовсе. Ждать здесь два значило бы ждать слой
            // нулевой толщины в сцене переноса.
            Check("с зазором слоёв больше", a.Count - 1, b.Count);
            Check("зазор попал в сцену", 1, Count(a, "Air, dry"));
            Check("без зазора его в сцене нет", 0, Count(b, "Air, dry"));

            // А вот с боковым зазором у цилиндра колец обязано быть два:
            // правило «бока нет» живёт В РЕДАКТОРЕ, а сцена обязана считать
            // то, что ей дали. Без этого плеча пропущенное боковое кольцо
            // цилиндра ничем не отличалось бы от верного нуля.
            GeometryModel both = As80();
            both.SideGapThickness = 4.0;
            Check("боковой зазор цилиндра строится", 2, Count(Layers(both), "Air, dry"));
        }

        static int Count(List<string> lines, string what)
        {
            int n = 0;
            foreach (string line in lines)
            {
                if (line.IndexOf(what, StringComparison.Ordinal) >= 0) n++;
            }

            return n;
        }

        /// <summary>
        /// Области сцены — через отражение: список закрыт, а спрашивать надо
        /// именно его. Числа печатаются как есть, в сантиметрах симулятора.
        /// </summary>
        static List<string> Layers(GeometryModel model)
        {
            EfficiencySimulator sim = new EfficiencySimulator(model) { Histories = 1 };
            // Сцена собирается лениво — заставляем собраться одним счётом.
            double err;
            sim.Efficiency(662.0, out err);

            FieldInfo field = typeof(EfficiencySimulator).GetField(
                "regions", BindingFlags.Instance | BindingFlags.NonPublic);
            IEnumerable list = (IEnumerable)field.GetValue(sim);

            List<string> lines = new List<string>();
            foreach (object region in list)
            {
                Type t = region.GetType();
                bool isBox = (bool)F(t, region, "IsBox");
                GeometryMaterial m = (GeometryMaterial)F(t, region, "Material");
                double zMin = (double)F(t, region, "ZMin");
                double zMax = (double)F(t, region, "ZMax");
                if (isBox)
                {
                    lines.Add(string.Format(CultureInfo.InvariantCulture,
                        "брус  a=({0,7:F4},{1,7:F4}) z=[{2,8:F4},{3,8:F4}] {4}",
                        F(t, region, "AX"), F(t, region, "AY"), zMin, zMax, m.Name));
                }
                else
                {
                    lines.Add(string.Format(CultureInfo.InvariantCulture,
                        "кольцо r=[{0,7:F4},{1,7:F4}] z=[{2,8:F4},{3,8:F4}] {4}",
                        F(t, region, "RIn"), F(t, region, "ROut"), zMin, zMax, m.Name));
                }
            }

            return lines;
        }

        static object F(Type t, object o, string name)
        {
            return t.GetField(name, BindingFlags.Instance | BindingFlags.Public
                                    | BindingFlags.NonPublic).GetValue(o);
        }

        // ------------------------------------------------------------------
        // 3. Вынос кристалла и цена задачи
        // ------------------------------------------------------------------

        static void Depth()
        {
            Head("Вынос кристалла и цена зазора");
            GeometryModel with = As80();
            GeometryModel without = As80();
            without.FrontGapThickness = 0.0;

            // Глубина кристалла под передним торцом корпуса — та величина, из
            // которой считается телесный угол точечной сцены.
            double dWith = with.FrontReflectorThickness + with.FrontGapThickness
                           + with.FrontCladdingThickness;
            double dWithout = without.FrontReflectorThickness + without.FrontCladdingThickness;
            Say("глубина кристалла под торцом: {0:F2} мм -> {1:F2} мм", dWithout, dWith);
            Check("глубина выросла ровно на зазор", 21.7, dWith - dWithout, 1e-9);

            Say("вынос детектора над пробой (GeometryScenes): {0:F2} мм -> {1:F2} мм",
                GeometryScenes.CrystalHeightAboveSampleMm(without),
                GeometryScenes.CrystalHeightAboveSampleMm(with));
            Check("вынос учитывает зазор", 21.7,
                  GeometryScenes.CrystalHeightAboveSampleMm(with)
                  - GeometryScenes.CrystalHeightAboveSampleMm(without), 1e-9);

            // Поперечник — от БОКОВОГО зазора, и у цилиндра он ноль. Проверяем
            // на выдуманном боковом: правило «бока нет» живёт в редакторе, а
            // формула сцены обязана боковой зазор считать.
            GeometryModel side = As80();
            side.FrontGapThickness = 0.0;
            side.SideGapThickness = 4.0;
            Say("поперечник детектора: {0:F2} мм -> {1:F2} мм (боковой зазор 4 мм)",
                GeometryScenes.DetectorOuterDiameterMm(without),
                GeometryScenes.DetectorOuterDiameterMm(side));
            Check("поперечник вырос на два зазора", 8.0,
                  GeometryScenes.DetectorOuterDiameterMm(side)
                  - GeometryScenes.DetectorOuterDiameterMm(without), 1e-9);

            // Цена задачи в эффективности. Историй немного — здесь важен не
            // четвёртый знак, а то, что падение ЕСТЬ и оно велико.
            //
            // ⛔ ДВЕ ПОСТАНОВКИ, и разница между ними — суть задачи. Заготовка
            // редактора держит точку в 100 мм от торца, и там зазор в 21.7 мм
            // — это добавка к сотне, то есть мелочь. Корпусные сцены `AS80_*`
            // сняты пробой НА ТОРЦЕ (`PointDistance = 0`), и там те же 21.7 мм
            // — это ВСЁ расстояние: кристалл уходит с 5 мм под торцом на 26.7.
            // Мерить одну постановку и говорить про обе — ровно та ошибка, из
            // которой родилась памятка «две ошибки в разные стороны».
            Fall(without, with, 100.0, "проба в 100 мм от торца");
            Fall(without, with, 0.0, "проба НА ТОРЦЕ (как в корпусе)");
        }

        /// <summary>Падение эффективности от зазора на заданном отстоянии пробы.</summary>
        static void Fall(GeometryModel without, GeometryModel with, double distanceMm,
                         string caption)
        {
            GeometryModel a = without.Clone();
            GeometryModel b = with.Clone();
            a.PointDistance = distanceMm;
            b.PointDistance = distanceMm;

            // Телесный угол диска радиуса R с расстояния h — то, чем падение
            // объясняется. Печатается рядом со счётом нарочно: совпадение двух
            // независимых способов и есть проверка, а одно число из симулятора
            // проверить нечем.
            double r = 0.5 * a.CrystalDiameter;
            double ha = distanceMm + a.FrontReflectorThickness + a.FrontCladdingThickness;
            double hb = distanceMm + b.FrontReflectorThickness + b.FrontGapThickness
                        + b.FrontCladdingThickness;
            Say("");
            Say("--- {0} ---", caption);
            Say("геометрический фактор диска: {0:F4} -> {1:F4} (глубина {2:F1} -> {3:F1} мм)",
                Omega(ha, r), Omega(hb, r), ha, hb);

            double[] energies = { 60.0, 662.0, 1461.0 };
            Say("{0,10} {1,14} {2,14} {3,10}", "E, кэВ", "без зазора", "с зазором", "отн.");
            foreach (double e in energies)
            {
                double err;
                EfficiencySimulator s1 = new EfficiencySimulator(a) { Histories = 200000 };
                EfficiencySimulator s2 = new EfficiencySimulator(b) { Histories = 200000 };
                double e1 = s1.Efficiency(e, out err);
                double e2 = s2.Efficiency(e, out err);
                Say("{0,10:F0} {1,14:E4} {2,14:E4} {3,10:F3}", e, e1, e2,
                    e1 > 0.0 ? e2 / e1 : 0.0);
                if (!(e2 < e1))
                {
                    Fail(string.Format(CultureInfo.InvariantCulture,
                        "{0}: зазор обязан УМЕНЬШАТЬ эффективность на {1:F0} кэВ",
                        caption, e));
                }
            }
        }

        /// <summary>Доля полного угла, под которой с расстояния h виден диск радиуса r.</summary>
        static double Omega(double h, double r)
        {
            return 0.5 * (1.0 - h / Math.Sqrt(h * h + r * r));
        }

        // ------------------------------------------------------------------
        // 4. Файл .in
        // ------------------------------------------------------------------

        static void FileRoundTrip()
        {
            Head("Файл .in: запись, чтение, старый файл");
            GeometryModel with = As80();
            with.SideGapThickness = 3.0;      // чтобы проверялись ОБА ключа
            string text = GeometryWriter.Render(with);

            Check("ключ торца в файле", true,
                  text.IndexOf("DS_CrystalFrontGapThickness", StringComparison.Ordinal) >= 0);
            Check("ключ бока в файле", true,
                  text.IndexOf("DS_CrystalSideGapThickness", StringComparison.Ordinal) >= 0);
            Check("вещество зазора в файле", true,
                  text.IndexOf("M_DS_Gap.MName = Air, dry", StringComparison.Ordinal) >= 0);

            GeometryModel back = LoadText(text);
            Say("прочитано: торец {0:F4} мм, бок {1:F4} мм, наполнитель {2} ({3:F6})",
                back.FrontGapThickness, back.SideGapThickness,
                back.Gap.Name, back.Gap.Density);
            Check("торец пережил запись", 21.7, back.FrontGapThickness, 1e-6);
            Check("бок пережил запись", 3.0, back.SideGapThickness, 1e-6);
            Check("наполнитель пережил запись", "Air, dry", back.Gap.Name);
            Check("состав наполнителя пережил запись", 2, back.Gap.Fractions.Count);

            // ⛔ Старый файл — тот, в котором ключей зазора нет вовсе. Он обязан
            // читаться нулём, иначе смысл сменили бы ВСЕ прежние геометрии.
            List<string> kept = new List<string>();
            foreach (string line in text.Split('\n'))
            {
                if (line.IndexOf("Gap", StringComparison.Ordinal) < 0) kept.Add(line);
            }

            GeometryModel old = LoadText(string.Join("\n", kept.ToArray()));
            Say("старый файл (ключи вырезаны): торец {0:F4}, бок {1:F4}, наполнитель \"{2}\"",
                old.FrontGapThickness, old.SideGapThickness, old.Gap.Name);
            Check("старый файл: торец ноль", 0.0, old.FrontGapThickness, 1e-12);
            Check("старый файл: бок ноль", 0.0, old.SideGapThickness, 1e-12);
            Check("старый файл: вещества нет", "", old.Gap.Name);
        }

        static GeometryModel LoadText(string text)
        {
            string path = Path.Combine(Path.GetTempPath(),
                                       "amber1_" + Guid.NewGuid().ToString("N") + ".in");
            try
            {
                File.WriteAllText(path, text);
                return GeometryModel.Load(path);
            }
            finally
            {
                try { File.Delete(path); } catch (IOException) { }
            }
        }

        // ------------------------------------------------------------------
        // 5. Клеймо
        // ------------------------------------------------------------------

        static void Stamp()
        {
            Head("Клеймо матрицы");
            GeometryModel with = As80();
            GeometryModel without = As80();
            without.FrontGapThickness = 0.0;

            ResponseMatrixOptions options = new ResponseMatrixOptions();
            string s1 = ResponseMatrix.ComputeStamp(without, options);
            string s2 = ResponseMatrix.ComputeStamp(with, options);
            Say("без зазора: {0}", s1);
            Say("с зазором : {0}", s2);
            Check("зазор меняет клеймо", true, !string.Equals(s1, s2, StringComparison.Ordinal));

            // Контроль: одна и та же геометрия обязана давать одно клеймо —
            // иначе «отличается» ничего не значит.
            Check("клеймо устойчиво", s2, ResponseMatrix.ComputeStamp(As80(), options));
        }

        // ------------------------------------------------------------------
        // 6. Чертёж
        // ------------------------------------------------------------------

        static void Sketch()
        {
            Head("Чертёж: слой зазора виден");
            Color gapColor = (Color)typeof(GeometrySketch).GetField(
                "GapColor", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            Say("цвет зазора: #{0:X2}{1:X2}{2:X2}", gapColor.R, gapColor.G, gapColor.B);

            GeometryModel with = As80();
            GeometryModel without = As80();
            without.FrontGapThickness = 0.0;

            int a = Pixels(with, gapColor);
            int b = Pixels(without, gapColor);
            Say("точек цвета зазора: с зазором {0}, без зазора {1}", a, b);
            Check("зазор нарисован", true, a > 0);
            Check("без зазора не нарисован", 0, b);
        }

        static int Pixels(GeometryModel model, Color want)
        {
            Application.EnableVisualStyles();
            using (GeometrySketch sketch = new GeometrySketch
                   { Mode = GeometrySketch.SketchMode.Detector })
            using (Bitmap bmp = new Bitmap(420, 460))
            {
                sketch.Bounds = new Rectangle(0, 0, 420, 460);
                sketch.SetModel(model);
                MethodInfo paint = typeof(GeometrySketch).GetMethod(
                    "OnPaint", BindingFlags.Instance | BindingFlags.NonPublic);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    paint.Invoke(sketch, new object[]
                        { new PaintEventArgs(g, sketch.ClientRectangle) });
                }

                int n = 0;
                for (int y = 0; y < bmp.Height; y++)
                {
                    for (int x = 0; x < bmp.Width; x++)
                    {
                        Color c = bmp.GetPixel(x, y);
                        if (c.R == want.R && c.G == want.G && c.B == want.B) n++;
                    }
                }

                return n;
            }
        }

        // ------------------------------------------------------------------
        // 7. Брусок и боковая постановка
        // ------------------------------------------------------------------

        static void BoxAndFacing()
        {
            Head("Брусок: зазор с двух сторон и разворот");
            GeometryModel g = GeometryEditorPanel.Blank();
            g.Shape = CrystalShape.Box;
            g.CrystalBoxX = 20.0;
            g.CrystalBoxY = 20.0;
            g.CrystalBoxZ = 20.0;
            g.FrontGapThickness = 3.0;
            g.SideGapThickness = 5.0;

            List<string> front = Layers(g);
            Say("--- торцом к пробе ---");
            foreach (string line in front) Say("  {0}", line);
            Check("у бруска зазор двумя брусами", 2, Count(front, "Air, dry"));

            g.Facing = GeometryDetectorFacing.Side;
            List<string> side = Layers(g);
            Say("--- боком к пробе ---");
            foreach (string line in side) Say("  {0}", line);
            Check("разворот зазор не потерял", 2, Count(side, "Air, dry"));

            // ⛔ Разворот обязан ПОМЕНЯТЬ числа, а не просто сохранить слои:
            // одинаковый список означал бы, что перестановка не доехала.
            bool same = string.Join("|", front.ToArray()) == string.Join("|", side.ToArray());
            Check("разворот меняет сцену", false, same);
        }

        // ------------------------------------------------------------------
        // 8. Корпусные геометрии AS80
        // ------------------------------------------------------------------

        static void Corpus(string root)
        {
            if (root == null)
            {
                return;
            }

            Head("Корпусные геометрии AS80");
            string dir = Path.Combine(root, "tools", "CORPUS", "corpus", "geometries");
            if (!Directory.Exists(dir))
            {
                Fail("нет каталога геометрий: " + dir);
                return;
            }

            foreach (string path in Directory.GetFiles(dir, "AS80*.in"))
            {
                GeometryModel g = GeometryModel.Load(path);
                Say("{0,-20} торец {1,7:F2} бок {2,7:F2}  наполнитель \"{3}\"",
                    Path.GetFileName(path), g.FrontGapThickness, g.SideGapThickness,
                    g.Gap.Name);
                Check(Path.GetFileName(path) + ": торец 21.7", 21.7, g.FrontGapThickness, 1e-6);
                Check(Path.GetFileName(path) + ": бок ноль", 0.0, g.SideGapThickness, 1e-12);
                Check(Path.GetFileName(path) + ": наполнитель воздух", "Air, dry", g.Gap.Name);
            }
        }

        // ------------------------------------------------------------------
        // Общее
        // ------------------------------------------------------------------

        /// <summary>Тот же прибор, что в пресете, — но своей копией на каждый раз.</summary>
        static GeometryModel As80()
        {
            GeometryModel g = GeometryEditorPanel.Blank();
            foreach (GeometryPresets.Preset preset in GeometryPresets.Items)
            {
                if (preset.Name == "Atom Spectra Pro 80x80")
                {
                    preset.Apply(g);
                    return g;
                }
            }

            throw new InvalidOperationException("пресета 80x80 нет");
        }

        static void Head(string title)
        {
            Say("");
            Say("=== {0} ===", title);
        }

        static void Check(string what, double expected, double got, double tol)
        {
            if (Math.Abs(expected - got) <= tol)
            {
                return;
            }

            Fail(string.Format(CultureInfo.InvariantCulture,
                "{0}: ожидалось {1:R}, вышло {2:R}", what, expected, got));
        }

        static void Check(string what, object expected, object got)
        {
            if (Equals(expected, got))
            {
                return;
            }

            Fail(string.Format(CultureInfo.InvariantCulture,
                "{0}: ожидалось {1}, вышло {2}", what, expected, got));
        }

        static void Fail(string message)
        {
            bad++;
            Say("  ⛔ {0}", message);
        }

        /// <summary>
        /// ⛔ Печать ТОЛЬКО инвариантом. `Console.WriteLine` берёт культуру
        /// потока, и на ru-RU числа вышли бы с запятой — правило Amber
        /// 05.09.2026 про разделитель одинаково касается и того, что видит
        /// человек, и того, что читает проба.
        /// </summary>
        static void Say(string format, params object[] args)
        {
            Console.WriteLine(args.Length == 0
                ? format
                : string.Format(CultureInfo.InvariantCulture, format, args));
        }
    }
}
