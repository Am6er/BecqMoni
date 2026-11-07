using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace FixGdml2Probe
{
    /// <summary>
    /// ПРИЁМКА полосы fixGdml2 (07.11.2026): дефекты импорта GDML и чертежей редактора, найденные проверкой экраном.
    ///
    ///     fixgdml2probe [--tmp=&lt;каталог&gt;] [--lht=&lt;lht.gdml&gt;] [--real=&lt;каталог&gt;] [--shots=&lt;каталог&gt;]
    ///
    ///   --tmp   куда писать свои входы GDML, по умолчанию `tmp_fixgdml2` рядом с пробой;
    ///   --lht   пример Geant4 `lht.gdml` (в git его нет): те же проверки на настоящем файле;
    ///   --real  каталог настоящих файлов приборов (RC103, RC110, GS2020; в git их нет): ни одной оболочки;
    ///   --shots снимки чертежей, на диск, не в git.
    ///
    /// ⛔ ОКОН НЕТ: `OnPaint` зовётся отражением на `Graphics` от `Bitmap`.
    ///
    /// (1) `AMBER301`: рамка чертежа — по сцене, а не по оболочке вокруг всей сцены (копии мира). Второй заход (критик
    ///     `critGdml2`): (1б) точный габарит повёрнутых тел вращения и булевых тел; (1в) том с ролью — не оболочка
    ///     (входы критика f1–f8, `w05` набора `gdml_a303`); функции выражений GDML (решение Amber 07.11.2026).
    ///     Отрицательные контроли: корпус из металла вокруг всего — не оболочка; два верхних тома — оболочки нет.
    /// (2) Сводка и чертёж — одни числа: неподдержанные тела и непрочитанное положение порознь, деталями.
    /// (3) Подпись ⌀ — собственный размер тела вращения, а не габарит повёрнутого тела.
    /// (4) `AMBER307`: RadiaCode-110 у широкой боковой грани — чертёж ставит слои туда же, куда их ставит перенос.
    ///     Контроль: при постановке у торца те же ключи стоят иначе.
    /// (5) `AMBER305`: отпечаток тела — 16 знаков и многоточие U+2026, не обрезка.
    /// (6) Третий заход: `replicavol` вдоль x/y/z размещается копиями; вдоль rho/phi — подпись «содержимое не показано» и
    ///     вещество копий строкой «не учтено»; два газа — два оттенка (решение Amber 07.11.2026 «Оттенки газа (Рекомендую)»).
    /// (7) Четвёртый заход: значки газов в сводке — точки своих цветов (RTF окна, отрисованный `EM_FORMATRANGE`), оттенки
    ///     попарно ΔE*ab ≥ 8, повёрнутая коробка подписана своими размерами, `replicavol` без `number` — одна копия.
    ///
    /// Проба собирается и против прежнего приложения: новые члены — только отражением. Ожидание на новом коде:
    /// «ВСЕ СОШЛИСЬ», код 0; на прежнем — отказ по (1)–(3), код 1. Ключи и файлы — код 2.
    /// </summary>
    static class Program
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        static int bad;
        static string tmp, lht, shots, real;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;
            tmp = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tmp_fixgdml2");
            foreach (string a in args)
            {
                if (a.StartsWith("--tmp=", StringComparison.Ordinal)) tmp = a.Substring(6);
                else if (a.StartsWith("--lht=", StringComparison.Ordinal)) lht = a.Substring(6);
                else if (a.StartsWith("--shots=", StringComparison.Ordinal)) shots = a.Substring(8);
                else if (a.StartsWith("--real=", StringComparison.Ordinal)) real = a.Substring(7);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            if (lht != null && !File.Exists(lht))
            {
                Console.Error.WriteLine("нет файла: " + lht);
                return 2;
            }

            Directory.CreateDirectory(tmp);
            if (shots != null)
            {
                Directory.CreateDirectory(shots);
            }

            Scene();
            Negatives();
            Critic();
            if (lht != null)
            {
                Lht();
            }

            if (real != null)
            {
                Real();
            }

            Third();
            Fourth();
            SideFace();
            Fingerprint();

            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "РАСХОЖДЕНИЙ: " + bad);
            return bad == 0 ? 0 : 1;
        }

        static void Check(string name, bool ok, string detail)
        {
            Console.WriteLine((ok ? "  ок   " : "  ОТКАЗ ") + name + (ok ? "" : " — " + detail));
            if (!ok)
            {
                bad++;
            }
        }

        // ------------------------------------------------------------------
        // Входы
        // ------------------------------------------------------------------

        const string Head = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<gdml>\n";
        const string Tail = "<setup name=\"Default\" version=\"1.0\"><world ref=\"World\"/></setup>\n</gdml>\n";

        /// <summary>
        /// Повторяет `lht.gdml` в малом: мир, его копия из воздуха вокруг всего, в ней труба и кольцо, повёрнутые как `TargetRot`
        /// (x 90°, z −40°), две детали с положением через степень `^` (её читатель не знает; функции с 07.11.2026 знает), шар (тело не поддержано), пластинка поодаль.
        /// </summary>
        static string SceneFile()
        {
            return Write("scene_envelope.gdml", Head
                + "<define>\n"
                + " <rotation name=\"rT\" x=\"90\" y=\"0\" z=\"-40\" unit=\"deg\"/>\n"
                + " <position name=\"pBad\" x=\"2^3\" y=\"0\" z=\"0\"/>\n"
                + " <position name=\"pRing\" x=\"0\" y=\"80\" z=\"0\"/>\n"
                + " <position name=\"pMon\" x=\"0\" y=\"0\" z=\"-200\"/>\n"
                + " <position name=\"pS\" x=\"0\" y=\"0\" z=\"200\"/>\n"
                + "</define>\n<materials/>\n<solids>\n"
                + " <box name=\"sW\" x=\"3000\" y=\"500\" z=\"3000\" lunit=\"mm\"/>\n"
                + " <tube name=\"sT\" rmin=\"0\" rmax=\"50\" z=\"100\" deltaphi=\"360\" aunit=\"deg\" lunit=\"mm\"/>\n"
                + " <tube name=\"sR\" rmin=\"100\" rmax=\"150\" z=\"30\" deltaphi=\"360\" aunit=\"deg\" lunit=\"mm\"/>\n"
                + " <box name=\"sB\" x=\"10\" y=\"10\" z=\"10\" lunit=\"mm\"/>\n"
                + " <sphere name=\"sS\" rmax=\"5\" deltaphi=\"360\" deltatheta=\"180\" aunit=\"deg\" lunit=\"mm\"/>\n"
                + "</solids>\n<structure>\n"
                + " <volume name=\"vT\"><materialref ref=\"G4_Fe\"/><solidref ref=\"sT\"/></volume>\n"
                + " <volume name=\"vR\"><materialref ref=\"G4_Al\"/><solidref ref=\"sR\"/></volume>\n"
                + " <volume name=\"vB\"><materialref ref=\"G4_Cu\"/><solidref ref=\"sB\"/></volume>\n"
                + " <volume name=\"vS\"><materialref ref=\"G4_Cu\"/><solidref ref=\"sS\"/></volume>\n"
                + " <volume name=\"vM\"><materialref ref=\"G4_Cu\"/><solidref ref=\"sB\"/></volume>\n"
                + " <volume name=\"vEnv\"><materialref ref=\"G4_AIR\"/><solidref ref=\"sW\"/>\n"
                + "  <physvol name=\"T\"><volumeref ref=\"vT\"/><rotationref ref=\"rT\"/></physvol>\n"
                + "  <physvol name=\"R\"><volumeref ref=\"vR\"/><positionref ref=\"pRing\"/><rotationref ref=\"rT\"/></physvol>\n"
                + "  <physvol name=\"B1\"><volumeref ref=\"vB\"/><positionref ref=\"pBad\"/></physvol>\n"
                + "  <physvol name=\"B2\"><volumeref ref=\"vB\"/><positionref ref=\"pBad\"/></physvol>\n"
                + "  <physvol name=\"S\"><volumeref ref=\"vS\"/><positionref ref=\"pS\"/></physvol>\n"
                + "  <physvol name=\"M\"><volumeref ref=\"vM\"/><positionref ref=\"pMon\"/></physvol>\n"
                + " </volume>\n"
                + " <volume name=\"World\"><materialref ref=\"G4_AIR\"/><solidref ref=\"sW\"/>\n"
                + "  <physvol name=\"Env\"><volumeref ref=\"vEnv\"/></physvol>\n"
                + " </volume>\n</structure>\n" + Tail);
        }

        static string Write(string name, string text)
        {
            string path = Path.Combine(tmp, name);
            File.WriteAllText(path, text, new UTF8Encoding(false));
            return path;
        }

        // ------------------------------------------------------------------
        // Чертёж
        // ------------------------------------------------------------------

        sealed class Drawing
        {
            public GdmlGeometry Geometry;
            public List<string> Notes;
            public string Failure;
            public string Summary;
            public double[] Spans = new double[2];
            public List<string> Dims = new List<string>();
        }

        static Drawing Draw(string path, string shot)
        {
            Drawing d = new Drawing { Geometry = GdmlGeometry.Read(path) };
            using (GdmlSketch sketch = new GdmlSketch())
            using (Bitmap bitmap = new Bitmap(1000, 640, PixelFormat.Format32bppArgb))
            {
                sketch.Size = bitmap.Size;
                sketch.SetGeometry(d.Geometry);
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    typeof(GdmlSketch).GetMethod("OnPaint", Any).Invoke(sketch, new object[]
                    {
                        new PaintEventArgs(graphics, new Rectangle(Point.Empty, bitmap.Size)),
                    });
                }

                d.Notes = new List<string>((List<string>)typeof(GdmlSketch).GetField("paintNotes", Any).GetValue(sketch));
                d.Failure = (string)typeof(GdmlSketch).GetField("paintFailure", Any).GetValue(sketch);
                foreach (GeometrySketch.DimLine line in sketch.DimensionLines)
                {
                    if (line.Kind == GeometrySketch.DimLineKind.Dimension)
                    {
                        d.Dims.Add(line.Key);
                    }
                }

                // Рамка видов — тем же кодом, что и чертёж: поле каждого вида и его оси.
                IList slots = (IList)typeof(GdmlSketch).GetField("paintSlots", Any).GetValue(sketch);
                for (int i = 0; i < slots.Count && i < 2; i++)
                {
                    object slot = slots[i];
                    Rectangle plot = (Rectangle)slot.GetType().GetField("Plot").GetValue(slot);
                    int u = (int)slot.GetType().GetField("U").GetValue(slot);
                    int v = (int)slot.GetType().GetField("V").GetValue(slot);
                    object frame = typeof(GdmlSketch).GetMethod("MakeFrame", Any).Invoke(sketch, new object[] { plot, u, v });
                    if (frame != null)
                    {
                        Func<string, double> f = n => Convert.ToDouble(frame.GetType().GetField(n).GetValue(frame), CultureInfo.InvariantCulture);
                        d.Spans[i] = Math.Max(f("UMax") - f("UMin"), f("VMax") - f("VMin"));
                    }
                }

                if (shots != null && shot != null)
                {
                    bitmap.Save(Path.Combine(shots, shot + ".png"), ImageFormat.Png);
                }
            }

            d.Summary = OverviewOf(d.Geometry).SummaryText();
            return d;
        }

        static GdmlOverview OverviewOf(GdmlGeometry g)
        {
            return GdmlOverview.Of(g);
        }

        static GdmlVolumeInstance Volume(GdmlGeometry g, string name)
        {
            foreach (GdmlVolumeInstance volume in g.Volumes)
            {
                if (volume.VolumeName == name)
                {
                    return volume;
                }
            }

            return null;
        }

        /// <summary>Число деталей из подписи чертежа, оканчивающейся этим хвостом; −1 — подписи нет.</summary>
        static int NoteCount(Drawing d, string tail)
        {
            foreach (string note in d.Notes)
            {
                Match m = Regex.Match(note, @"^Not drawn \((\d+)\):.* - " + Regex.Escape(tail) + "$");
                if (m.Success)
                {
                    return int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                }
            }

            return -1;
        }

        static int SummaryNumber(string summary, string label)
        {
            Match m = Regex.Match(summary, Regex.Escape(label) + @": (\d+)");
            return m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : -1;
        }

        static int Unplaced(GdmlGeometry g)
        {
            FieldInfo field = typeof(GdmlGeometry).GetField("UnplacedVolumes");
            return field == null ? -1 : (int)field.GetValue(g);
        }

        // ------------------------------------------------------------------
        // (1)–(3) сцена в оболочке
        // ------------------------------------------------------------------

        static void Scene()
        {
            Console.WriteLine("(1)–(3) сцена в копии мира, повёрнутые тела, `^` в положении, шар");
            Drawing d = Draw(SceneFile(), "scene_envelope");
            string notes = string.Join(" | ", d.Notes);
            Check("чертёж без заслона", d.Failure == null, d.Failure);

            // (1) Рамка. Сцена без оболочки: кольцо ⌀300 × 30, ось — мировая y (поворот x 90° уводит z в y, поворот z −40° —
            // вокруг собственной оси кольца). Точный габарит: x и z — 300, y — 65…95; труба ⌀100 × 100 — ±50; пластинка 10 мм на
            // z = −200; шар (не поддержан) габарита не имеет. Сцена 300 × 145 × 355 мм. (П1б, критик `critGdml2`) Прежний
            // габарит — по углам локальной коробки: 300·(cos 40° + sin 40°) = 422.6 мм, его закрепляла и первая редакция пробы.
            Check("(1) рамка разреза по сцене, а не по коробу 3000 мм", d.Spans[0] > 0.0 && d.Spans[0] < 500.0,
                  "размах " + d.Spans[0].ToString("F1", CultureInfo.InvariantCulture));
            Check("(1) рамка торца по сцене", d.Spans[1] > 0.0 && d.Spans[1] < 500.0,
                  "размах " + d.Spans[1].ToString("F1", CultureInfo.InvariantCulture));
            Check("(1) ни один размер чертежа не 3000 мм", !d.Dims.Exists(x => x.StartsWith("3000", StringComparison.Ordinal)), string.Join(", ", d.Dims));
            Check("(1б) габарит размерами — кольцо 300 мм, не 422.6", d.Dims.Exists(x => x == "300 mm") && !d.Dims.Exists(x => x == "422.6 mm"), string.Join(", ", d.Dims));
            GdmlVolumeInstance ring = Volume(d.Geometry, "vR"), tube = Volume(d.Geometry, "vT");
            Check("(1б) габарит кольца точный: x ±150, z ±150, y 65…95", ring != null && ring.BoundsKnown
                  && Near(ring.Min[0], -150.0) && Near(ring.Max[0], 150.0) && Near(ring.Min[2], -150.0) && Near(ring.Max[2], 150.0)
                  && Near(ring.Min[1], 65.0) && Near(ring.Max[1], 95.0), Box(ring));
            Check("(1б) габарит трубы точный: ±50 по всем осям", tube != null && tube.BoundsKnown
                  && Near(tube.Min[0], -50.0) && Near(tube.Max[0], 50.0) && Near(tube.Min[2], -50.0) && Near(tube.Max[2], 50.0), Box(tube));
            Check("(1) подпись об оболочке", d.Notes.Exists(n => n.StartsWith("Not shown, an envelope around the whole scene (1): vEnv", StringComparison.Ordinal)), notes);
            Check("(1) сводка: габарит сцены, а не мира", d.Summary.Contains("overall size 300 × 145 × 355 mm"), FirstLines(d.Summary));
            Check("(1) сводка называет оболочку", d.Summary.Contains("vEnv 3000 × 500 × 3000 mm"), FirstLines(d.Summary));

            // (2) Одни числа. Шар — неподдержанное тело (1 деталь); vB размещён дважды через `2^3` — 2 детали без положения.
            int unsupported = SummaryNumber(d.Summary, "Unsupported bodies");
            int placedNot = SummaryNumber(d.Summary, "placement or extent not read");
            Check("(2) читатель: неподдержанных 1", d.Geometry.UnknownSolids == 1, d.Geometry.UnknownSolids.ToString(CultureInfo.InvariantCulture));
            Check("(2) читатель: без положения 2", Unplaced(d.Geometry) == 2, Unplaced(d.Geometry).ToString(CultureInfo.InvariantCulture));
            Check("(2) сводка: неподдержанных 1", unsupported == 1, FirstLines(d.Summary));
            Check("(2) сводка: без положения 2", placedNot == 2, FirstLines(d.Summary));
            Check("(2) чертёж: неподдержанное тело — 1, как в сводке", NoteCount(d, "unsupported body") == unsupported && unsupported >= 0, notes);
            Check("(2) чертёж: без положения — 2, как в сводке", NoteCount(d, "placement or extent not read") == placedNot && placedNot >= 0, notes);
            Check("(2) чертёж называет повтор детали", d.Notes.Exists(n => n.Contains("vB ×2")), notes);

            // (3) ⌀ — собственный размер. Старый код: габарит повёрнутого тела, 100·(cos 40° + sin 40°) = 140.9.
            Check("(3) труба: ⌀100 × 100", GdmlOverview.SizeText(Volume(d.Geometry, "vT"), d.Geometry.Axis) == "⌀100 × 100 mm",
                  GdmlOverview.SizeText(Volume(d.Geometry, "vT"), d.Geometry.Axis));
            Check("(3) кольцо: ⌀300 × 30", GdmlOverview.SizeText(Volume(d.Geometry, "vR"), d.Geometry.Axis) == "⌀300 × 30 mm",
                  GdmlOverview.SizeText(Volume(d.Geometry, "vR"), d.Geometry.Axis));
            Check("(3) коробка — по-прежнему три числа", GdmlOverview.SizeText(Volume(d.Geometry, "vM"), d.Geometry.Axis) == "10 × 10 × 10 mm",
                  GdmlOverview.SizeText(Volume(d.Geometry, "vM"), d.Geometry.Axis));
        }

        static bool Near(double value, double expected)
        {
            return Math.Abs(value - expected) <= 1e-9 * Math.Max(1.0, Math.Abs(expected));
        }

        static string Box(GdmlVolumeInstance v)
        {
            if (v == null || !v.BoundsKnown)
            {
                return "габарита нет";
            }

            Func<double, string> f = x => x.ToString("G6", CultureInfo.InvariantCulture);
            return f(v.Min[0]) + ".." + f(v.Max[0]) + ", " + f(v.Min[1]) + ".." + f(v.Max[1]) + ", " + f(v.Min[2]) + ".." + f(v.Max[2]);
        }

        static string FirstLines(string text)
        {
            string[] lines = text.Split('\n');
            return string.Join(" / ", lines, 0, Math.Min(4, lines.Length)).Replace("\r", "");
        }

        // ------------------------------------------------------------------
        // Отрицательные контроли оболочки
        // ------------------------------------------------------------------

        static void Negatives()
        {
            Console.WriteLine("(1к) отрицательные контроли: корпус вокруг всего, два верхних тома, труба без поворота");

            // Корпус из алюминия, в нём всё: это прибор, а не копия мира.
            string housing = Write("housing_all.gdml", Head
                + "<define/>\n<materials/>\n<solids>\n"
                + " <box name=\"sW\" x=\"3000\" y=\"3000\" z=\"3000\" lunit=\"mm\"/>\n"
                + " <box name=\"sH\" x=\"100\" y=\"100\" z=\"100\" lunit=\"mm\"/>\n"
                + " <tube name=\"sC\" rmin=\"0\" rmax=\"20\" z=\"40\" deltaphi=\"360\" aunit=\"deg\" lunit=\"mm\"/>\n"
                + "</solids>\n<structure>\n"
                + " <volume name=\"vC\"><materialref ref=\"G4_SODIUM_IODIDE\"/><solidref ref=\"sC\"/></volume>\n"
                + " <volume name=\"vH\"><materialref ref=\"G4_Al\"/><solidref ref=\"sH\"/>\n"
                + "  <physvol name=\"C\"><volumeref ref=\"vC\"/></physvol>\n"
                + " </volume>\n"
                + " <volume name=\"World\"><materialref ref=\"G4_AIR\"/><solidref ref=\"sW\"/>\n"
                + "  <physvol name=\"H\"><volumeref ref=\"vH\"/></physvol>\n"
                + " </volume>\n</structure>\n" + Tail);
            Drawing d = Draw(housing, "housing_all");
            Check("корпус вокруг всего — не оболочка: рамка 100 мм", Math.Abs(d.Spans[0] - 100.0) < 1e-6 && Math.Abs(d.Spans[1] - 100.0) < 1e-6,
                  d.Spans[0].ToString("F1", CultureInfo.InvariantCulture) + " / " + d.Spans[1].ToString("F1", CultureInfo.InvariantCulture));
            Check("корпус вокруг всего — подписи об оболочке нет", !d.Notes.Exists(n => n.StartsWith("Not shown, an envelope", StringComparison.Ordinal)), string.Join(" | ", d.Notes));
            Check("труба без поворота: ⌀40 × 40", GdmlOverview.SizeText(Volume(d.Geometry, "vC"), d.Geometry.Axis) == "⌀40 × 40 mm",
                  GdmlOverview.SizeText(Volume(d.Geometry, "vC"), d.Geometry.Axis));

            // Воздушный короб с прибором и рядом с ним, вне короба, ещё тело: верхних томов два — короб в рамке.
            string twoTops = Write("two_tops.gdml", Head
                + "<define><position name=\"pFar\" x=\"400\" y=\"0\" z=\"0\"/></define>\n<materials/>\n<solids>\n"
                + " <box name=\"sW\" x=\"3000\" y=\"3000\" z=\"3000\" lunit=\"mm\"/>\n"
                + " <box name=\"sA\" x=\"300\" y=\"300\" z=\"300\" lunit=\"mm\"/>\n"
                + " <box name=\"sB\" x=\"10\" y=\"10\" z=\"10\" lunit=\"mm\"/>\n"
                + "</solids>\n<structure>\n"
                + " <volume name=\"vB\"><materialref ref=\"G4_Cu\"/><solidref ref=\"sB\"/></volume>\n"
                + " <volume name=\"vA\"><materialref ref=\"G4_AIR\"/><solidref ref=\"sA\"/>\n"
                + "  <physvol name=\"B\"><volumeref ref=\"vB\"/></physvol>\n"
                + " </volume>\n"
                + " <volume name=\"World\"><materialref ref=\"G4_AIR\"/><solidref ref=\"sW\"/>\n"
                + "  <physvol name=\"A\"><volumeref ref=\"vA\"/></physvol>\n"
                + "  <physvol name=\"F\"><volumeref ref=\"vB\"/><positionref ref=\"pFar\"/></physvol>\n"
                + " </volume>\n</structure>\n" + Tail);
            d = Draw(twoTops, "two_tops");
            Check("два верхних тома — оболочки нет, короб в рамке (−150…405 мм)", Math.Abs(d.Spans[0] - 555.0) < 1e-6 || Math.Abs(d.Spans[1] - 555.0) < 1e-6,
                  d.Spans[0].ToString("F1", CultureInfo.InvariantCulture) + " / " + d.Spans[1].ToString("F1", CultureInfo.InvariantCulture));
            Check("два верхних тома — подписи об оболочке нет", !d.Notes.Exists(n => n.StartsWith("Not shown, an envelope", StringComparison.Ordinal)), string.Join(" | ", d.Notes));
        }

        // ------------------------------------------------------------------
        // Входы критика `critGdml2` (П1б, П1в): ложные оболочки и точный габарит
        // ------------------------------------------------------------------

        const string CriticSolids = "<solids>\n"
            + "<box name=\"sW\" x=\"3000\" y=\"3000\" z=\"3000\" lunit=\"mm\"/>\n"
            + "<tube name=\"sC\" rmin=\"0\" rmax=\"20\" z=\"40\" deltaphi=\"360\" aunit=\"deg\" lunit=\"mm\"/>\n"
            + "<box name=\"sGap\" x=\"60\" y=\"60\" z=\"60\" lunit=\"mm\"/>\n"
            + "<box name=\"sH\" x=\"80\" y=\"80\" z=\"80\" lunit=\"mm\"/>\n"
            + "<box name=\"sRoom\" x=\"1000\" y=\"1000\" z=\"1000\" lunit=\"mm\"/>\n"
            + "<box name=\"sPb\" x=\"50\" y=\"400\" z=\"400\" lunit=\"mm\"/>\n"
            + "<tube name=\"sR\" rmin=\"100\" rmax=\"150\" z=\"30\" deltaphi=\"360\" aunit=\"deg\" lunit=\"mm\"/>\n"
            + "<tube name=\"sPart\" rmin=\"0\" rmax=\"50\" z=\"40\" deltaphi=\"270\" aunit=\"deg\" lunit=\"mm\"/>\n"
            + "<cone name=\"sK\" rmin1=\"0\" rmax1=\"10\" rmin2=\"0\" rmax2=\"30\" z=\"50\" deltaphi=\"360\" aunit=\"deg\" lunit=\"mm\"/>\n"
            + "</solids>\n";

        const string CriticCrystal = "<volume name=\"vCrystal\"><materialref ref=\"G4_SODIUM_IODIDE\"/><solidref ref=\"sC\"/></volume>\n";

        /// <summary>Вход критика: общие тела, своя структура; мир кладёт один том `vTop`.</summary>
        static string CriticFile(string name, string define, string structure, string top)
        {
            return Write(name + ".gdml", Head + "<define>" + define + "</define>\n<materials/>\n" + CriticSolids
                + "<structure>\n" + structure
                + "<volume name=\"World\"><materialref ref=\"G4_AIR\"/><solidref ref=\"sW\"/>" + top + "</volume>\n"
                + "</structure>\n" + Tail);
        }

        const string TopOnly = "<physvol name=\"T\"><volumeref ref=\"vTop\"/></physvol>";

        static bool Envelope(Drawing d, string name)
        {
            return d.Notes.Exists(n => n.StartsWith("Not shown, an envelope around the whole scene (1): " + name + ";", StringComparison.Ordinal));
        }

        static bool NoEnvelope(Drawing d)
        {
            return !d.Notes.Exists(n => n.StartsWith("Not shown, an envelope", StringComparison.Ordinal));
        }

        static string Spans(Drawing d)
        {
            return d.Spans[0].ToString("G6", CultureInfo.InvariantCulture) + " / " + d.Spans[1].ToString("G6", CultureInfo.InvariantCulture) + " | " + string.Join(" | ", d.Notes);
        }

        static void Critic()
        {
            Console.WriteLine("(1в) входы критика: оболочка не берёт том с ролью; (1б) точный габарит повёрнутых тел");
            Drawing d = Draw(CriticFile("f1_airgap_top", "",
                CriticCrystal + "<volume name=\"vTop\"><materialref ref=\"G4_AIR\"/><solidref ref=\"sGap\"/><physvol name=\"C\"><volumeref ref=\"vCrystal\"/></physvol></volume>\n", TopOnly), "f1");
            Check("f1 воздушный зазор вокруг кристалла — не оболочка, рамка 60", NoEnvelope(d) && Near(d.Spans[0], 60.0), Spans(d));

            d = Draw(CriticFile("f2_air_sample", "<position name=\"pPb\" x=\"300\" y=\"0\" z=\"0\"/>",
                CriticCrystal + "<volume name=\"vHousing\"><materialref ref=\"G4_Al\"/><solidref ref=\"sH\"/><physvol name=\"C\"><volumeref ref=\"vCrystal\"/></physvol></volume>\n"
                + "<volume name=\"vShield\"><materialref ref=\"G4_Pb\"/><solidref ref=\"sPb\"/></volume>\n"
                + "<volume name=\"vSampleAir\"><materialref ref=\"G4_AIR\"/><solidref ref=\"sRoom\"/><physvol name=\"H\"><volumeref ref=\"vHousing\"/></physvol><physvol name=\"P\"><volumeref ref=\"vShield\"/><positionref ref=\"pPb\"/></physvol></volume>\n",
                "<physvol name=\"S\"><volumeref ref=\"vSampleAir\"/></physvol>"), "f2");
            Check("f2 воздух пробы (радон) вокруг прибора и защиты — не оболочка, рамка 1000", NoEnvelope(d) && Near(d.Spans[0], 1000.0), Spans(d));

            d = Draw(CriticFile("f3_vacuum_container", "",
                CriticCrystal + "<volume name=\"vTop\"><materialref ref=\"G4_Galactic\"/><solidref ref=\"sGap\"/><physvol name=\"C\"><volumeref ref=\"vCrystal\"/></physvol></volume>\n", TopOnly), "f3");
            Check("f3 банка G4_Galactic с кристаллом — не оболочка и не корпус: прибора в сводке нет", NoEnvelope(d) && Near(d.Spans[0], 60.0)
                  && !d.Summary.Contains("Device:") && !d.Summary.Contains("(housing)") && d.Summary.Contains("Galactic (gas)"), Spans(d) + " | " + FirstLines(d.Summary));

            d = Draw(CriticFile("f4_housing_gap", "",
                CriticCrystal + "<volume name=\"vGap\"><materialref ref=\"G4_AIR\"/><solidref ref=\"sGap\"/><physvol name=\"C\"><volumeref ref=\"vCrystal\"/></physvol></volume>\n"
                + "<volume name=\"vTop\"><materialref ref=\"G4_Al\"/><solidref ref=\"sH\"/><physvol name=\"G\"><volumeref ref=\"vGap\"/></physvol></volume>\n", TopOnly), "f4");
            Check("f4 корпус с зазором — оболочки нет, рамка 80", NoEnvelope(d) && Near(d.Spans[0], 80.0), Spans(d));

            d = Draw(CriticFile("f5_env_gap", "",
                CriticCrystal + "<volume name=\"vGap\"><materialref ref=\"G4_AIR\"/><solidref ref=\"sGap\"/><physvol name=\"C\"><volumeref ref=\"vCrystal\"/></physvol></volume>\n"
                + "<volume name=\"vTop\"><materialref ref=\"G4_AIR\"/><solidref ref=\"sRoom\"/><physvol name=\"G\"><volumeref ref=\"vGap\"/></physvol></volume>\n", TopOnly), "f5");
            Check("f5 зал — оболочка, зазор вторым слоем — нет: рамка 60", Envelope(d, "vTop") && Near(d.Spans[0], 60.0), Spans(d));

            d = Draw(CriticFile("f6_env_two", "<position name=\"pPb\" x=\"300\" y=\"0\" z=\"0\"/>",
                CriticCrystal + "<volume name=\"vHousing\"><materialref ref=\"G4_Al\"/><solidref ref=\"sH\"/><physvol name=\"C\"><volumeref ref=\"vCrystal\"/></physvol></volume>\n"
                + "<volume name=\"vShield\"><materialref ref=\"G4_Pb\"/><solidref ref=\"sPb\"/></volume>\n"
                + "<volume name=\"vTop\"><materialref ref=\"G4_AIR\"/><solidref ref=\"sRoom\"/><physvol name=\"H\"><volumeref ref=\"vHousing\"/></physvol><physvol name=\"P\"><volumeref ref=\"vShield\"/><positionref ref=\"pPb\"/></physvol></volume>\n", TopOnly), "f6");
            Check("f6 зал вокруг прибора и защиты — оболочка, рамка 400", Envelope(d, "vTop") && Near(Math.Max(d.Spans[0], d.Spans[1]), 400.0), Spans(d));

            d = Draw(CriticFile("f7_envelope_child_partly", "<position name=\"pBad\" x=\"2^3\" y=\"0\" z=\"0\"/>",
                CriticCrystal + "<volume name=\"vTop\"><materialref ref=\"G4_AIR\"/><solidref ref=\"sRoom\"/><physvol name=\"C\"><volumeref ref=\"vCrystal\"/><positionref ref=\"pBad\"/></physvol></volume>\n", TopOnly), "f7");
            Check("f7 у оболочки нет измеренных потомков — не оболочка, рамка 1000", NoEnvelope(d) && Near(d.Spans[0], 1000.0), Spans(d));

            // Повёрнутые тела вращения: кольцо ⌀300 — 300 мм; неполный оборот 270° радиуса 50 и конус ⌀60 — точный габарит
            // (сверено с выгрузкой критика `fp_new.txt`, графа tight: 88.302 × 40 × 100 и 60 × 50 × 60).
            d = Draw(CriticFile("f8_rot_ring", "<rotation name=\"rT\" x=\"90\" y=\"0\" z=\"-40\" unit=\"deg\"/>",
                "<volume name=\"vRing\"><materialref ref=\"G4_Al\"/><solidref ref=\"sR\"/></volume>\n"
                + "<volume name=\"vPart\"><materialref ref=\"G4_Al\"/><solidref ref=\"sPart\"/></volume>\n"
                + "<volume name=\"vCone\"><materialref ref=\"G4_Al\"/><solidref ref=\"sK\"/></volume>\n",
                "<physvol name=\"R\"><volumeref ref=\"vRing\"/><rotationref ref=\"rT\"/></physvol>"
                + "<physvol name=\"P\"><volumeref ref=\"vPart\"/><position name=\"pp\" x=\"0\" y=\"0\" z=\"300\"/><rotationref ref=\"rT\"/></physvol>"
                + "<physvol name=\"K\"><volumeref ref=\"vCone\"/><position name=\"pk\" x=\"0\" y=\"0\" z=\"-300\"/><rotationref ref=\"rT\"/></physvol>"), "f8");
            GdmlVolumeInstance part = Volume(d.Geometry, "vPart"), cone = Volume(d.Geometry, "vCone"), ring = Volume(d.Geometry, "vRing");
            Check("f8 кольцо ⌀300: габарит 300 × 30 × 300", ring != null && ring.BoundsKnown && Near(ring.Max[0] - ring.Min[0], 300.0) && Near(ring.Max[2] - ring.Min[2], 300.0), Box(ring));
            Check("f8 неполный оборот: 88.302 × 40 × 100", part != null && part.BoundsKnown && Math.Abs(part.Max[0] - part.Min[0] - 88.302) < 1e-3
                  && Near(part.Max[1] - part.Min[1], 40.0) && Near(part.Max[2] - part.Min[2], 100.0), Box(part));
            Check("f8 конус: 60 × 50 × 60", cone != null && cone.BoundsKnown && Near(cone.Max[0] - cone.Min[0], 60.0) && Near(cone.Max[1] - cone.Min[1], 50.0)
                  && Near(cone.Max[2] - cone.Min[2], 60.0), Box(cone));
            Check("f8 на чертеже 300 мм, а не 422.6", d.Dims.Exists(x => x == "300 mm") && !d.Dims.Exists(x => x == "422.6 mm"), string.Join(", ", d.Dims));

            // w05 набора `gdml_a303`: воздух 10 мм вокруг железа 10.00000001 мм — рамку не раздувает, не оболочка.
            string w05 = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "data", "gdml_a303", "w05_almost_fill_over2.gdml");
            if (File.Exists(w05))
            {
                d = Draw(w05, "w05");
                Check("w05 тесный воздух вокруг железа — не оболочка", NoEnvelope(d), Spans(d));
            }
            else
            {
                Check("w05 найден", false, w05);
            }
        }

        // ------------------------------------------------------------------
        // Третий заход: репликация, «не учтено», оттенки газа
        // ------------------------------------------------------------------

        static GdmlMaterialRow Row(GdmlOverview o, string name)
        {
            foreach (GdmlMaterialRow row in o.Materials)
            {
                if (row.Name == name)
                {
                    return row;
                }
            }

            return null;
        }

        static void Third()
        {
            Console.WriteLine("(6) третий заход: копии <replicavol>, вещества непрочитанных копий, оттенки газа");
            string data = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "data", "gdml_a303");

            // Репликация вдоль x: 10 копий Seg (Pb 10 × 100 × 100 мм) в A; на чертеже — десять плит, подписи «не показано» нет.
            Drawing d = Draw(Path.Combine(data, "08_replica.gdml"), "replica_x");
            int copies = 0;
            foreach (GdmlVolumeInstance v in d.Geometry.Volumes)
            {
                copies += v.VolumeName == "Seg" ? 1 : 0;
            }

            GdmlMaterialRow pb = Row(GdmlOverview.Of(d.Geometry), "G4_Pb");
            Check("replicavol вдоль x: 10 копий Seg, свинец 10 × 1135 г", copies == 10 && pb != null && !pb.MassPartly && Math.Abs(pb.MassG - 11350.0) < 1e-6,
                  copies + " копий, " + (pb == null ? "свинца нет" : pb.MassText()));
            Check("replicavol вдоль x: подписи «содержимое не показано» нет", !d.Notes.Exists(n => n.StartsWith("Content not shown", StringComparison.Ordinal)), string.Join(" | ", d.Notes));

            // Вдоль phi Geant4 режет по углу — не читается: том нарисован, подпись говорит, что внутри пусто не по правде, а
            // свинец копий — строкой «не учтено» в сводке и легенде, массой «?».
            d = Draw(Path.Combine(data, "a_replica_phi.gdml"), "replica_phi");
            Check("replicavol вдоль phi: подпись «Content not shown (1): A - not read: <replicavol>»",
                  d.Notes.Exists(n => n == "Content not shown (1): A - not read: <replicavol>"), string.Join(" | ", d.Notes));
            pb = Row(GdmlOverview.Of(d.Geometry), "G4_Pb");
            Check("replicavol вдоль phi: свинец в веществах, «? g, not counted: <replicavol>»", pb != null && pb.LegendText() == "Pb: ? g, not counted: <replicavol>"
                  && d.Summary.Contains("Pb: ? g, not counted: <replicavol>"), pb == null ? "свинца нет" : pb.LegendText());

            // Два газа: у каждого свой оттенок и своя строка легенды (решение Amber 07.11.2026 «Оттенки газа (Рекомендую)»).
            string gases = Write("two_gases.gdml", Head
                + "<define><position name=\"pB\" x=\"200\" y=\"0\" z=\"0\"/></define>\n<materials/>\n<solids>\n"
                + " <box name=\"sW\" x=\"3000\" y=\"3000\" z=\"3000\" lunit=\"mm\"/>\n"
                + " <box name=\"sA\" x=\"100\" y=\"100\" z=\"100\" lunit=\"mm\"/>\n"
                + "</solids>\n<structure>\n"
                + " <volume name=\"vAir\"><materialref ref=\"G4_AIR\"/><solidref ref=\"sA\"/></volume>\n"
                + " <volume name=\"vArgon\"><materialref ref=\"G4_Ar\"/><solidref ref=\"sA\"/></volume>\n"
                + " <volume name=\"World\"><materialref ref=\"G4_AIR\"/><solidref ref=\"sW\"/>\n"
                + "  <physvol name=\"A\"><volumeref ref=\"vAir\"/></physvol>\n"
                + "  <physvol name=\"B\"><volumeref ref=\"vArgon\"/><positionref ref=\"pB\"/></physvol>\n"
                + " </volume>\n</structure>\n" + Tail);
            d = Draw(gases, "two_gases");
            GdmlOverview o = GdmlOverview.Of(d.Geometry);
            GdmlMaterialRow air = Row(o, "G4_AIR"), argon = Row(o, "G4_Ar");
            Check("два газа: обе строки с ролью «газ»", air != null && argon != null && air.Role == GdmlRole.Gas && argon.Role == GdmlRole.Gas,
                  (air == null ? "-" : air.Role.ToString()) + " / " + (argon == null ? "-" : argon.Role.ToString()));
            Check("два газа: разные оттенки", air != null && argon != null && air.Color.ToArgb() != argon.Color.ToArgb(),
                  (air == null ? "-" : air.Color.Name) + " / " + (argon == null ? "-" : argon.Color.Name));
            Check("два газа: две строки легенды", o.Materials.Count == 2, o.Materials.Count.ToString(CultureInfo.InvariantCulture));

            if (lht == null)
            {
                return;
            }

            // `lht.gdml`: калориметр — 4 ряда по 6 кристаллов CsI 6 × 6 × 30 см, два калориметра — 48 кристаллов,
            // 48 · 1080 см³ · 4.5 г/см³ = 233280 г; прежде ни одного кристалла и вещества CsI_Crystal в списке не было.
            d = Draw(lht, null);
            int crystals = 0;
            foreach (GdmlVolumeInstance v in d.Geometry.Volumes)
            {
                crystals += v.VolumeName == "vCrystal" && v.BoundsKnown ? 1 : 0;
            }

            GdmlMaterialRow csi = Row(GdmlOverview.Of(d.Geometry), "CsI_Crystal");
            Check("lht: 48 кристаллов калориметров на местах", crystals == 48, crystals.ToString(CultureInfo.InvariantCulture));
            Check("lht: CsI_Crystal 233280 г, без «≥»", csi != null && !csi.MassPartly && Math.Abs(csi.MassG - 233280.0) < 1e-3, csi == null ? "нет строки" : csi.MassText());
            Check("lht: подписи «содержимое не показано» нет", !d.Notes.Exists(n => n.StartsWith("Content not shown", StringComparison.Ordinal)), string.Join(" | ", d.Notes));
            Check("lht: AIR и Galactic — разные оттенки газа", Row(GdmlOverview.Of(d.Geometry), "G4_AIR") != null && Row(GdmlOverview.Of(d.Geometry), "G4_Galactic") != null
                  && Row(GdmlOverview.Of(d.Geometry), "G4_AIR").Color.ToArgb() != Row(GdmlOverview.Of(d.Geometry), "G4_Galactic").Color.ToArgb(), "одного цвета");
        }

        // ------------------------------------------------------------------
        // Четвёртый заход: значки газов в сводке пикселями, размеры повёрнутой коробки
        // ------------------------------------------------------------------

        [StructLayout(LayoutKind.Sequential)]
        struct RectTw
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct CharRange
        {
            public int Min, Max;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct FormatRange
        {
            public IntPtr Hdc, HdcTarget;
            public RectTw Rc, RcPage;
            public CharRange Chrg;
        }

        [DllImport("user32.dll", EntryPoint = "SendMessageW")]
        static extern IntPtr SendFormat(IntPtr hwnd, int msg, IntPtr wParam, ref FormatRange lParam);

        [DllImport("user32.dll", EntryPoint = "SendMessageW")]
        static extern IntPtr SendPlain(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

        /// <summary>
        /// Сводка так, как её рисует окно редактора: тот же RTF в `RichTextBox`, отрисованный `EM_FORMATRANGE` на `Bitmap`.
        /// Окно не показывается (контрол без родителя, только дескриптор), `DrawToBitmap` не зовётся.
        /// </summary>
        static Bitmap RenderSummary(GdmlOverview o, string shot)
        {
            const int EmFormatRange = 0x0439;
            Bitmap bitmap = new Bitmap(700, 1400);
            using (RichTextBox box = new RichTextBox())
            {
                box.Rtf = o.SummaryRtf("Segoe UI", 9f);
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.White);
                    IntPtr hdc = graphics.GetHdc();
                    try
                    {
                        FormatRange range = new FormatRange { Hdc = hdc, HdcTarget = hdc };
                        range.Rc = new RectTw { Left = 0, Top = 0, Right = 700 * 15, Bottom = 1400 * 15 };
                        range.RcPage = range.Rc;
                        range.Chrg = new CharRange { Min = 0, Max = -1 };
                        SendFormat(box.Handle, EmFormatRange, (IntPtr)1, ref range);
                        SendPlain(box.Handle, EmFormatRange, IntPtr.Zero, IntPtr.Zero);
                    }
                    finally
                    {
                        graphics.ReleaseHdc(hdc);
                    }
                }
            }

            if (shots != null && shot != null)
            {
                bitmap.Save(Path.Combine(shots, shot + ".png"), ImageFormat.Png);
            }

            return bitmap;
        }

        /// <summary>Сколько точек ровно этого цвета: внутренность значка «■» залита цветом вещества без сглаживания.</summary>
        static int PixelsOf(Bitmap bitmap, Color color)
        {
            int count = 0;
            int want = color.ToArgb();
            for (int x = 0; x < bitmap.Width; x++)
            {
                for (int y = 0; y < bitmap.Height; y++)
                {
                    count += bitmap.GetPixel(x, y).ToArgb() == want ? 1 : 0;
                }
            }

            return count;
        }

        static void Fourth()
        {
            Console.WriteLine("(7) четвёртый заход: значки газов в сводке пикселями, коробка своими размерами, replicavol без number");

            // Два газа (AIR и Ar): в сводке у каждого значок своего цвета. Прежде бледный значок подменялся на C4CCD6, и оба
            // газа выходили одного цвета.
            Drawing d = Draw(Path.Combine(tmp, "two_gases.gdml"), null);
            GdmlOverview o = GdmlOverview.Of(d.Geometry);
            GdmlMaterialRow air = Row(o, "G4_AIR"), argon = Row(o, "G4_Ar");
            using (Bitmap summary = RenderSummary(o, "summary_two_gases"))
            {
                int airPixels = air == null ? 0 : PixelsOf(summary, air.Color), argonPixels = argon == null ? 0 : PixelsOf(summary, argon.Color);
                int substitute = PixelsOf(summary, Color.FromArgb(0xC4, 0xCC, 0xD6));
                Check("сводка: значки AIR и Ar — точки своих цветов, разных", airPixels > 10 && argonPixels > 10 && air.Color.ToArgb() != argon.Color.ToArgb() && substitute == 0,
                      "AIR " + airPixels + ", Ar " + argonPixels + ", подменного C4CCD6 " + substitute);
            }

            // Оттенки газа попарно различимы: ΔE*ab не меньше 8 (CIE 1976 по sRGB, белая точка D65).
            FieldInfo shadesField = typeof(GdmlOverview).GetField("GasShades", Any);
            Color[] shades = shadesField == null ? new Color[0] : (Color[])shadesField.GetValue(null);
            double least = shades.Length < 2 ? 0.0 : double.MaxValue;
            for (int i = 0; i < shades.Length; i++)
            {
                for (int k = i + 1; k < shades.Length; k++)
                {
                    least = Math.Min(least, DeltaE(shades[i], shades[k]));
                }
            }

            Check("оттенки газа: попарно ΔE*ab ≥ 8", least >= 8.0, least.ToString("F2", CultureInfo.InvariantCulture));

            // `replicavol` без `number`: одна копия, как у Geant4.
            string data = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "data", "gdml_a303");
            d = Draw(Path.Combine(data, "a_replica_nonumber.gdml"), null);
            int copies = 0;
            foreach (GdmlVolumeInstance v in d.Geometry.Volumes)
            {
                copies += v.VolumeName == "Seg" ? 1 : 0;
            }

            Check("replicavol без number: одна копия", copies == 1, copies.ToString(CultureInfo.InvariantCulture));

            // Вещество только непрочитанных копий: «0 vol.» в сводке нет.
            d = Draw(Path.Combine(data, "a_replica_phi.gdml"), null);
            Check("неучтённое вещество без «0 vol.»", d.Summary.Contains("Pb: ? g, not counted: <replicavol>") && !d.Summary.Contains("<replicavol>, 0 vol."), FirstLines(d.Summary));

            // Повёрнутая коробка: подпись — свои размеры x × y × z, а не габарит в осях мира.
            string box = Write("rot_box.gdml", Head
                + "<define><rotation name=\"r\" x=\"0\" y=\"40\" z=\"0\" unit=\"deg\"/></define>\n<materials/>\n<solids>\n"
                + " <box name=\"sW\" x=\"3000\" y=\"3000\" z=\"3000\" lunit=\"mm\"/>\n"
                + " <box name=\"sB\" x=\"20\" y=\"10\" z=\"30\" lunit=\"mm\"/>\n"
                + "</solids>\n<structure>\n"
                + " <volume name=\"vB\"><materialref ref=\"G4_Al\"/><solidref ref=\"sB\"/></volume>\n"
                + " <volume name=\"World\"><materialref ref=\"G4_AIR\"/><solidref ref=\"sW\"/>\n"
                + "  <physvol name=\"B\"><volumeref ref=\"vB\"/><rotationref ref=\"r\"/></physvol>\n"
                + " </volume>\n</structure>\n" + Tail);
            d = Draw(box, null);
            Check("повёрнутая коробка: «20 × 10 × 30 mm»", GdmlOverview.SizeText(Volume(d.Geometry, "vB"), d.Geometry.Axis) == "20 × 10 × 30 mm",
                  GdmlOverview.SizeText(Volume(d.Geometry, "vB"), d.Geometry.Axis));

            if (lht == null)
            {
                return;
            }

            d = Draw(lht, null);
            o = GdmlOverview.Of(d.Geometry);
            GdmlVolumeInstance crystal = Volume(d.Geometry, "vCrystal");
            string crystalSize = crystal == null ? "тома vCrystal нет" : GdmlOverview.SizeText(crystal, d.Geometry.Axis);
            Check("lht: кристалл «60 × 60 × 300 mm», а не 297.2 × 60 × 181.2", crystalSize == "60 × 60 × 300 mm", crystalSize);
            GdmlMaterialRow lair = Row(o, "G4_AIR"), galactic = Row(o, "G4_Galactic");
            using (Bitmap summary = RenderSummary(o, "summary_lht"))
            {
                int airPixels = lair == null ? 0 : PixelsOf(summary, lair.Color), galacticPixels = galactic == null ? 0 : PixelsOf(summary, galactic.Color);
                Check("lht: в сводке значки AIR и Galactic — точки своих разных цветов", airPixels > 10 && galacticPixels > 10 && lair.Color.ToArgb() != galactic.Color.ToArgb(),
                      "AIR " + airPixels + ", Galactic " + galacticPixels);
            }
        }

        static double DeltaE(Color a, Color b)
        {
            double[] p = Lab(a), q = Lab(b);
            return Math.Sqrt((p[0] - q[0]) * (p[0] - q[0]) + (p[1] - q[1]) * (p[1] - q[1]) + (p[2] - q[2]) * (p[2] - q[2]));
        }

        static double[] Lab(Color c)
        {
            Func<double, double> lin = v => v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
            double r = lin(c.R / 255.0), g = lin(c.G / 255.0), b = lin(c.B / 255.0);
            double x = (0.4124 * r + 0.3576 * g + 0.1805 * b) / 0.95047, y = 0.2126 * r + 0.7152 * g + 0.0722 * b, z = (0.0193 * r + 0.1192 * g + 0.9505 * b) / 1.08883;
            Func<double, double> f = t => t > 0.008856 ? Math.Pow(t, 1.0 / 3.0) : 7.787 * t + 16.0 / 116.0;
            return new[] { 116.0 * f(y) - 16.0, 500.0 * (f(x) - f(y)), 200.0 * (f(y) - f(z)) };
        }

        // ------------------------------------------------------------------
        // Настоящий файл
        // ------------------------------------------------------------------

        /// <summary>Настоящие файлы приборов (RC103, RC110, GS2020; в git их нет): ни одной оболочки — воздушный `*_device_log` это прибор.</summary>
        static void Real()
        {
            Console.WriteLine("(1в) настоящие файлы: " + real);
            foreach (string path in Directory.GetFiles(real, "*.gdml"))
            {
                Drawing d = Draw(path, null);
                Check(Path.GetFileName(path) + ": оболочки нет, заслона нет", NoEnvelope(d) && d.Failure == null, Spans(d));
            }
        }

        static void Lht()
        {
            Console.WriteLine("(1)–(3) настоящий файл: " + Path.GetFileName(lht));
            Drawing d = Draw(lht, "lht");
            string notes = string.Join(" | ", d.Notes);
            Check("lht: чертёж без заслона", d.Failure == null, d.Failure);
            // С функциями на место встают калориметры на 60 см от мишени: сцена 1783.5 мм по x (±891.748), а не мир 3000.
            Check("lht: рамка по сцене 1783.5 мм, а не по миру 3000", Math.Abs(Math.Max(d.Spans[0], d.Spans[1]) - 1783.496) < 0.01 && Envelope(d, "vWorldVisible"),
                  d.Spans[0].ToString("F1", CultureInfo.InvariantCulture) + " / " + d.Spans[1].ToString("F1", CultureInfo.InvariantCulture));
            // (D) Функции читаются: все 11 деталей с положением через sin()/cos()/pi получили его.
            Check("lht: без положения — 0 деталей (с функциями)", SummaryNumber(d.Summary, "placement or extent not read") == 0 && Unplaced(d.Geometry) == 0, FirstLines(d.Summary));
            Check("lht: подписи «не нарисовано» нет", NoteCount(d, "placement or extent not read") == -1 && NoteCount(d, "unsupported body") == -1, notes);
            Check("lht: колонна мишени на месте: x 70.107…87.107", Volume(d.Geometry, "vTargetColumn") != null && Volume(d.Geometry, "vTargetColumn").BoundsKnown
                  && Math.Abs(Volume(d.Geometry, "vTargetColumn").Min[0] - 70.107) < 1e-3, Box(Volume(d.Geometry, "vTargetColumn")));
            Check("lht: оболочка мишени ⌀100.2 — габарит 100.2, а не 141.2", Volume(d.Geometry, "vTargetInnerCover") != null
                  && Near(Volume(d.Geometry, "vTargetInnerCover").Max[0] - Volume(d.Geometry, "vTargetInnerCover").Min[0], 100.2), Box(Volume(d.Geometry, "vTargetInnerCover")));
            Check("lht: vTarget ⌀100 × 100", GdmlOverview.SizeText(Volume(d.Geometry, "vTarget"), d.Geometry.Axis) == "⌀100 × 100 mm",
                  GdmlOverview.SizeText(Volume(d.Geometry, "vTarget"), d.Geometry.Axis));
            Check("lht: vTargetInnerCover ⌀100.2 × 100.2", GdmlOverview.SizeText(Volume(d.Geometry, "vTargetInnerCover"), d.Geometry.Axis) == "⌀100.2 × 100.2 mm",
                  GdmlOverview.SizeText(Volume(d.Geometry, "vTargetInnerCover"), d.Geometry.Axis));
        }

        // ------------------------------------------------------------------
        // (4) RadiaCode-110 у широкой боковой грани
        // ------------------------------------------------------------------

        static GeometryModel Rc110(GeometryDetectorFacing facing)
        {
            GeometryModel g = GeometryEditorPanel.Blank();
            foreach (GeometryPresets.Preset preset in GeometryPresets.Items)
            {
                if (preset.Name == "RadiaCode-110")
                {
                    preset.Apply(g);
                }
            }

            g.Facing = facing;
            return g;
        }

        /// <summary>
        /// Грань, у которой стоит размер вида сбоку: вертикальный размер выше кристалла — «Top» (сторона пробы, `zFace`),
        /// ниже — «Bottom»; горизонтальный — «Lateral» (боковая грань). Кристалл — по размеру его глубины (ключ `keyDepth`
        /// из <see cref="GeometryModel.CrystalBoxInScene(out double, out double, out double, out string, out string, out string)"/>).
        /// Сторона ряда (`DimSide`) тут не годится: она говорит, где стоит цепочка размеров, а не какую грань она мерит.
        /// </summary>
        static Dictionary<string, string> FacesOf(GeometryModel g, string shot)
        {
            Dictionary<string, string> faces = new Dictionary<string, string>(StringComparer.Ordinal);
            double hx, hy, depth;
            string kx, ky, kd;
            g.CrystalBoxInScene(out hx, out hy, out depth, out kx, out ky, out kd);
            using (GeometrySketch sketch = new GeometrySketch { Mode = GeometrySketch.SketchMode.Detector })
            using (Bitmap bitmap = new Bitmap(420, 520))
            {
                sketch.Bounds = new Rectangle(Point.Empty, bitmap.Size);
                sketch.SetModel(g);
                using (Graphics graphics = Graphics.FromImage(bitmap))
                using (PaintEventArgs e = new PaintEventArgs(graphics, new Rectangle(Point.Empty, bitmap.Size)))
                {
                    typeof(GeometrySketch).GetMethod("OnPaint", Any).Invoke(sketch, new object[] { e });
                }

                List<GeometrySketch.DimLine> lines = new List<GeometrySketch.DimLine>();
                float crystalTop = float.NaN, crystalBottom = float.NaN;
                foreach (GeometrySketch.DimLine line in sketch.DimensionLines)
                {
                    if (line.Kind != GeometrySketch.DimLineKind.Dimension || line.OnTopView || line.Key == null)
                    {
                        continue;
                    }

                    bool vertical = Math.Abs(line.A.X - line.B.X) < 0.5f;
                    if (vertical && line.Key == kd)
                    {
                        crystalTop = Math.Min(line.SpanA, line.SpanB);
                        crystalBottom = Math.Max(line.SpanA, line.SpanB);
                    }

                    lines.Add(line);
                }

                foreach (GeometrySketch.DimLine line in lines)
                {
                    if (faces.ContainsKey(line.Key) || line.Key == kd)
                    {
                        continue;
                    }

                    bool vertical = Math.Abs(line.A.X - line.B.X) < 0.5f;
                    float middle = 0.5f * (line.SpanA + line.SpanB);
                    faces[line.Key] = !vertical ? "Lateral"
                                    : middle <= crystalTop ? "Top"
                                    : middle >= crystalBottom ? "Bottom"
                                    : "?";
                }

                if (shots != null)
                {
                    bitmap.Save(Path.Combine(shots, shot + ".png"), ImageFormat.Png);
                }
            }

            return faces;
        }

        static void SideFace()
        {
            Console.WriteLine("(4) RadiaCode-110: слои вида сбоку там же, где их кладёт перенос");
            GeometryModel side = Rc110(GeometryDetectorFacing.Side);
            GeometryModel front = Rc110(GeometryDetectorFacing.Front);
            Check("шаблон RadiaCode-110 найден", side.CrystalBoxX == 14.0 && side.SideGapThickness == 2.985 && side.FrontGapThickness == 0.785 && side.MountingThickness == 1.5,
                  side.CrystalBoxX + " " + side.SideGapThickness + " " + side.FrontGapThickness + " " + side.MountingThickness);

            // Модель: к пробе — слой той стороны, которой прибор повёрнут (`GeometryScenes.WrappingInScene` —
            // та же перестановка, что в `EfficiencySimulator.Build`), на остальных гранях — другой, оправа — за кристаллом.
            double toSample, lateral;
            GeometryScenes.WrappingInScene(side, out toSample, out lateral);
            Check("перенос: к пробе боковой стек 0.065 + 2.985 + 3 = 6.05 мм", Math.Abs(toSample - 6.05) < 1e-9, toSample.ToString("R", CultureInfo.InvariantCulture));
            Check("перенос: на прочих гранях торцевой стек 0.065 + 0.785 + 3 = 3.85 мм", Math.Abs(lateral - 3.85) < 1e-9, lateral.ToString("R", CultureInfo.InvariantCulture));

            Dictionary<string, string> s = FacesOf(side, "rc110_side");
            Dictionary<string, string> f = FacesOf(front, "rc110_front");
            Func<Dictionary<string, string>, string, string> at = (d, k) => d.ContainsKey(k) ? d[k] : "нет";
            string seen = "сбоку: SideGap " + at(s, "SideGapThickness") + ", FrontGap " + at(s, "FrontGapThickness") + ", Mount " + at(s, "MountingThickness")
                        + "; у торца: SideGap " + at(f, "SideGapThickness") + ", FrontGap " + at(f, "FrontGapThickness") + ", Mount " + at(f, "MountingThickness");
            Console.WriteLine("        " + seen);

            // Проба вверху вида сбоку (`zFace` чертежа — сторона пробы); оправа — внизу, за кристаллом.
            Check("чертёж: боковой зазор 2.985 — к пробе (сверху), как у переноса", at(s, "SideGapThickness") == "Top", seen);
            Check("чертёж: торцевой зазор 0.785 — на боковой грани, как у переноса", at(s, "FrontGapThickness") == "Lateral", seen);
            Check("чертёж: оправа 1.5 — за кристаллом (снизу), как у переноса", at(s, "MountingThickness") == "Bottom", seen);
            Check("контроль у торца: торцевой зазор сверху, боковой сбоку, оправа снизу", at(f, "FrontGapThickness") == "Top" && at(f, "SideGapThickness") == "Lateral" && at(f, "MountingThickness") == "Bottom", seen);
        }

        // ------------------------------------------------------------------
        // (5) Отпечаток тела
        // ------------------------------------------------------------------

        static void Fingerprint()
        {
            Console.WriteLine("(5) отпечаток тела в окне матрицы");
            ResponseMatrix matrix = (ResponseMatrix)FormatterServices.GetUninitializedObject(typeof(ResponseMatrix));
            string full = "345cd9aa5dcbcc19" + new string('0', 48);
            matrix.BodyFingerprint = full;
            matrix.StoredBodyFingerprint = full;
            string text = (string)typeof(ResponseMatrixForm).GetMethod("DescribeFingerprint", Any).Invoke(null, new object[] { matrix });
            Check("строка: 16 знаков и многоточие U+2026, подчёркивания нет", text.TrimEnd().EndsWith(": 345cd9aa5dcbcc19…", StringComparison.Ordinal) && text.IndexOf('_') < 0,
                  text.Trim());

            // Многоточие внизу строки на мелком шрифте сливается в черту. Сколько отдельных пятен рисует глиф: 3 — точки.
            foreach (string family in new[] { "Segoe UI", "Microsoft Sans Serif", "Tahoma" })
            {
                using (Font font = new Font(family, 9f))
                {
                    int blobs = Blobs("…", font);
                    Check("«…» шрифтом " + family + " 9 — три точки, а не черта", blobs == 3, blobs.ToString(CultureInfo.InvariantCulture) + " пятен");
                }
            }
        }

        static int Blobs(string text, Font font)
        {
            using (Bitmap bitmap = new Bitmap(60, 40))
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.White);
                    TextRenderer.DrawText(graphics, text, font, new Point(10, 5), Color.Black, Color.White);
                }

                bool[,] seen = new bool[bitmap.Width, bitmap.Height];
                int blobs = 0;
                for (int x = 0; x < bitmap.Width; x++)
                {
                    for (int y = 0; y < bitmap.Height; y++)
                    {
                        if (seen[x, y] || bitmap.GetPixel(x, y).GetBrightness() > 0.6f)
                        {
                            continue;
                        }

                        blobs++;
                        Stack<Point> stack = new Stack<Point>();
                        stack.Push(new Point(x, y));
                        seen[x, y] = true;
                        while (stack.Count > 0)
                        {
                            Point p = stack.Pop();
                            foreach (Point q in new[] { new Point(p.X + 1, p.Y), new Point(p.X - 1, p.Y), new Point(p.X, p.Y + 1), new Point(p.X, p.Y - 1) })
                            {
                                if (q.X >= 0 && q.Y >= 0 && q.X < bitmap.Width && q.Y < bitmap.Height && !seen[q.X, q.Y]
                                    && bitmap.GetPixel(q.X, q.Y).GetBrightness() <= 0.6f)
                                {
                                    seen[q.X, q.Y] = true;
                                    stack.Push(q);
                                }
                            }
                        }
                    }
                }

                return blobs;
            }
        }
    }
}
