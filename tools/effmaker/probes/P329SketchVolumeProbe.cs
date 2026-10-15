using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Serialization;

/// <summary>
/// П329 (15.10.2026), `AMBER225`: объём на чертеже редактора геометрии.
///
/// Постановка Amber 15.10.2026: на вкладке «Детектор» — объём детектора, на
/// вкладке «Источник» — объём образца; где объём определить нельзя (лунка,
/// поле, ISO) — пусто. Вид — словами («Объём кристалла 16.2 см³» /
/// «Объём пробы 491.2 см³»), решение Amber вопросником того же дня.
///
/// Две части.
///
/// (1) МЕРКА. Каждый вид источника рисуется в `Bitmap` прямым вызовом
/// `OnPaint` (как `EditorShot`), из подписей чертежа (`GeometrySketch.Labels`)
/// берётся та, что несёт «³». Ожидание — объём, посчитанный ЗДЕСЬ своей
/// формулой из полей модели, а не вызовом приложения: иначе проба сверяла бы
/// приложение с ним же. Где надписи быть не должно (точка, на земле, в лунке,
/// ISO, миниатюра), её наличие — дефект так же, как отсутствие там, где
/// должна быть. На коде ДО правки проба обязана упасть (надписей нет нигде) —
/// это её положительный контроль.
///
/// (2) ОКНО. `--device=&lt;xml&gt;` — конфигурация прибора (ТОЛЬКО ЧТЕНИЕ): каждая
/// кривая с геометрией ставится в настоящий `GeometryEditorPanel` на форме
/// ВНЕ ЭКРАНА, без активации (фокус у человека за машиной не отнимается),
/// обе вкладки снимаются в PNG (`--shots=&lt;каталог&gt;`), печатается надпись.
///
///     p329sketchvolumeprobe [--device=&lt;xml&gt;] [--shots=&lt;каталог&gt;]
///
/// Код возврата: 0 — всё сошлось; 1 — нет; 2 — плохие ключи.
/// </summary>
static class P329SketchVolumeProbe
{
    static int bad;

    [STAThread]
    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string device = null, shots = null;
        foreach (string a in args)
        {
            if (a.StartsWith("--device=", StringComparison.Ordinal)) device = a.Substring(9);
            else if (a.StartsWith("--shots=", StringComparison.Ordinal)) shots = a.Substring(8);
            else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
        }

        Application.EnableVisualStyles();
        GlobalConfigManager.GetInstance();

        Console.WriteLine("(1) мерка: надпись объёма по видам источника");
        Measure();

        if (device != null)
        {
            Console.WriteLine();
            Console.WriteLine("(2) окно: геометрии из «{0}»", Path.GetFileName(device));
            if (shots != null)
            {
                Directory.CreateDirectory(shots);
            }

            Window(device, shots);
        }

        Console.WriteLine();
        Console.WriteLine(bad == 0 ? "СОШЛОСЬ" : "НЕ СОШЛОСЬ: расхождений " + bad);
        return bad == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------
    // (1) мерка
    // ------------------------------------------------------------------

    static GeometryModel Base()
    {
        GeometryModel g = GeometryEditorPanel.Blank();
        GeometryPresets.Items[0].Apply(g);
        return g;
    }

    static void Measure()
    {
        Size size = new Size(232, 440);

        // Детектор: и цилиндр, и брусок; у брусков постановка «сбоку» объёма не
        // меняет. На полевых сценах вкладка детектора объём КРИСТАЛЛА показывает.
        GeometryModel cyl = Base();
        cyl.Shape = CrystalShape.Cylinder;
        cyl.CrystalDiameter = 63.0;
        cyl.CrystalHeight = 63.0;
        Expect("детектор: цилиндр 63x63", cyl, GeometrySketch.SketchMode.Detector, size,
               Math.PI * 31.5 * 31.5 * 63.0 / 1000.0);

        GeometryModel box = Base();
        box.Shape = CrystalShape.Box;
        box.CrystalBoxX = 15.0;
        box.CrystalBoxY = 18.0;
        box.CrystalBoxZ = 60.0;
        Expect("детектор: брусок 15x18x60", box, GeometrySketch.SketchMode.Detector, size, 16.2);

        GeometryModel side = Base();
        side.Shape = CrystalShape.Box;
        side.CrystalBoxX = 15.0;
        side.CrystalBoxY = 18.0;
        side.CrystalBoxZ = 60.0;
        side.Facing = GeometryDetectorFacing.Side;
        Expect("детектор: брусок сбоку", side, GeometrySketch.SketchMode.Detector, size, 16.2);

        GeometryModel ground = Base();
        GeometryScenes.Ground(ground, 3000.0);
        Expect("детектор: сцена «на земле»", ground, GeometrySketch.SketchMode.Detector, size,
               CrystalOf(ground));

        // Источник: объём ПРОБЫ — внутренняя полость сосуда.
        GeometryModel c = Base();
        c.SourceType = GeometrySourceType.Cylinder;
        double rc = 0.5 * c.BeakerDiameter - c.BeakerSideWallThickness;
        Expect("источник: цилиндр", c, GeometrySketch.SketchMode.Source, size,
               Math.PI * rc * rc * c.SourceHeight / 1000.0);

        GeometryModel m = Base();
        m.SourceType = GeometrySourceType.Marinelli;
        m.MarinelliBeakerDiameter = 114.0;
        m.MarinelliBeakerHeight = 89.0;
        m.MarinelliHoleDiameter = 61.0;
        m.MarinelliHoleHeight = 53.0;
        m.MarinelliSideThickness = 2.0;
        m.MarinelliEndWallThickness = 2.0;
        m.MarinelliHoleSideThickness = 2.0;
        m.MarinelliHoleEndWallThickness = 2.0;
        m.MarinelliSourceHeight = 85.0;
        // Кольцо между стенкой колодца и стенкой стакана на всю высоту пробы
        // плюс диск над дном колодца (85 − 53 мм) радиусом колодца со стенкой.
        double rIn = 30.5 + 2.0, rOut = 57.0 - 2.0;
        Expect("источник: маринелли (как у Amber)", m, GeometrySketch.SketchMode.Source, size,
               (Math.PI * (rOut * rOut - rIn * rIn) * 85.0 + Math.PI * rIn * rIn * 32.0) / 1000.0);

        GeometryModel b = Base();
        b.SourceType = GeometrySourceType.Box;
        b.BoxSourceX = 60.0;
        b.BoxSourceY = 40.0;
        b.BoxSourceHeight = 30.0;
        b.BoxSideWallThickness = 1.0;
        b.BoxEndWallThickness = 1.0;
        b.BoxToDetectorDistance = 5.0;
        Expect("источник: кювета", b, GeometrySketch.SketchMode.Source, size, 58.0 * 38.0 * 30.0 / 1000.0);

        // Большой сосуд: от 1000 см³ — целым, без «E+04».
        GeometryModel big = Base();
        big.SourceType = GeometrySourceType.Box;
        big.BoxSourceX = 300.0;
        big.BoxSourceY = 200.0;
        big.BoxSourceHeight = 200.0;
        big.BoxSideWallThickness = 0.0;
        big.BoxEndWallThickness = 1.0;
        big.BoxToDetectorDistance = 5.0;
        Expect("источник: кювета 12 л", big, GeometrySketch.SketchMode.Source, size, 12000.0);

        GeometryModel p = Base();
        p.SourceType = GeometrySourceType.Point;
        ExpectNone("источник: точечный", p, GeometrySketch.SketchMode.Source, size);

        GeometryModel g = Base();
        GeometryScenes.Ground(g, 3000.0);
        ExpectNone("источник: на земле (поле)", g, GeometrySketch.SketchMode.Source, size);

        GeometryModel h = Base();
        GeometryScenes.Borehole(h, 3000.0);
        ExpectNone("источник: в лунке", h, GeometrySketch.SketchMode.Source, size);

        GeometryModel iso = Base();
        GeometryScenes.Iso(iso);
        ExpectNone("источник: ISO", iso, GeometrySketch.SketchMode.Source, size);

        ExpectNone("миниатюра", box, GeometrySketch.SketchMode.Overview, new Size(300, 200));
    }

    static double CrystalOf(GeometryModel g)
    {
        if (g.Shape == CrystalShape.Box)
        {
            return g.CrystalBoxX * g.CrystalBoxY * g.CrystalBoxZ / 1000.0;
        }

        return Math.PI * 0.25 * g.CrystalDiameter * g.CrystalDiameter * g.CrystalHeight / 1000.0;
    }

    static string VolumeLabel(GeometrySketch.SketchLabel[] labels)
    {
        string found = null;
        foreach (GeometrySketch.SketchLabel l in labels)
        {
            if (l.Text != null && l.Text.IndexOf('³') >= 0)
            {
                if (found != null)
                {
                    return found + " | " + l.Text;      // две надписи — тоже дефект
                }

                found = l.Text;
            }
        }

        return found;
    }

    static string Number(double v)
    {
        return v < 1000.0 ? v.ToString("G4", CultureInfo.InvariantCulture)
                          : v.ToString("F0", CultureInfo.InvariantCulture);
    }

    static void Expect(string name, GeometryModel g, GeometrySketch.SketchMode mode, Size size, double cm3)
    {
        string text = VolumeLabel(Draw(g, mode, size));
        string want = Number(cm3);
        // Число стоит отдельным словом: «16.2» не должно сойтись с «116.2».
        bool ok = text != null && text.IndexOf('|') < 0
                  && (" " + text + " ").Contains(" " + want + " ")
                  && (text.Contains("см³") || text.Contains("cm³"));
        if (!ok) bad++;
        Console.WriteLine("  {0,-4} {1,-36} ждали {2,-8} см³  надпись: {3}",
                          ok ? "ок" : "НЕТ", name, want, text ?? "(нет)");
    }

    static void ExpectNone(string name, GeometryModel g, GeometrySketch.SketchMode mode, Size size)
    {
        // «Пусто» сходится и тогда, когда чертёж не нарисовался вовсе. Поэтому
        // требуется, чтобы прочие подписи (размеры) на нём были.
        GeometrySketch.SketchLabel[] labels = Draw(g, mode, size);
        string text = VolumeLabel(labels);
        bool ok = text == null && labels.Length > 0;
        if (!ok) bad++;
        Console.WriteLine("  {0,-4} {1,-36} ждали пусто      надпись: {2} (подписей {3})",
                          ok ? "ок" : "НЕТ", name, text ?? "(нет)", labels.Length);
    }

    static GeometrySketch.SketchLabel[] Draw(GeometryModel model, GeometrySketch.SketchMode mode, Size size)
    {
        using (GeometrySketch sketch = new GeometrySketch { Mode = mode })
        using (Bitmap bmp = new Bitmap(size.Width, size.Height))
        {
            sketch.Bounds = new Rectangle(Point.Empty, size);
            sketch.SetModel(model);
            MethodInfo paint = typeof(GeometrySketch).GetMethod(
                "OnPaint", BindingFlags.Instance | BindingFlags.NonPublic);
            using (Graphics gr = Graphics.FromImage(bmp))
            using (PaintEventArgs e = new PaintEventArgs(gr, new Rectangle(Point.Empty, size)))
            {
                paint.Invoke(sketch, new object[] { e });
            }

            return sketch.Labels;
        }
    }

    // ------------------------------------------------------------------
    // (2) окно
    // ------------------------------------------------------------------

    /// <summary>Форма, которая показывается, НЕ отнимая фокус, и стоит вне экрана.</summary>
    sealed class QuietForm : Form
    {
        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }
    }

    static void Window(string device, string shots)
    {
        XmlDocument doc = new XmlDocument();
        doc.Load(device);
        XmlSerializer ser = new XmlSerializer(typeof(GeometryModel), new XmlRootAttribute("Geometry"));
        XmlNodeList nodes = doc.SelectNodes("//EfficiencyConfigData[Geometry]");
        int n = 0;
        foreach (XmlNode node in nodes)
        {
            XmlNode nameNode = node.SelectSingleNode("Name");
            string curve = nameNode != null ? nameNode.InnerText : "?";
            GeometryModel g;
            using (XmlReader r = new XmlNodeReader(node.SelectSingleNode("Geometry")))
            {
                g = (GeometryModel)ser.Deserialize(r);
            }

            n++;
            using (QuietForm form = new QuietForm())
            using (GeometryEditorPanel panel = new GeometryEditorPanel { Dock = DockStyle.Fill })
            {
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-20000, -20000);
                form.ShowInTaskbar = false;
                form.ClientSize = new Size(960, 760);
                form.Controls.Add(panel);
                form.Show();
                panel.SetModel(g);

                TabControl tabs = FindTabs(panel);
                GeometrySketch det = Field(panel, "detectorSketch");
                GeometrySketch src = Field(panel, "sourceSketch");
                string[] texts = new string[2];
                for (int tab = 0; tab < 2; tab++)
                {
                    tabs.SelectedIndex = tab;
                    Application.DoEvents();
                    using (Bitmap bmp = new Bitmap(form.ClientSize.Width, form.ClientSize.Height))
                    {
                        // DrawToBitmap зовёт OnPaint чертежа — Labels после него свежие.
                        panel.DrawToBitmap(bmp, new Rectangle(Point.Empty, form.ClientSize));
                        if (shots != null)
                        {
                            bmp.Save(Path.Combine(shots, string.Format(CultureInfo.InvariantCulture,
                                "window_{0}_{1}.png", n, tab == 0 ? "det" : "src")), ImageFormat.Png);
                        }
                    }

                    texts[tab] = VolumeLabel((tab == 0 ? det : src).Labels);
                }

                double crystal = CrystalOf(g);
                bool detOk = texts[0] != null && (" " + texts[0] + " ").Contains(" " + Number(crystal) + " ");
                bool srcWant = g.Scene == GeometrySceneKind.None && g.SourceType != GeometrySourceType.Point;
                bool srcOk = srcWant ? texts[1] != null : texts[1] == null;
                if (!detOk) bad++;
                if (!srcOk) bad++;
                Console.WriteLine("  «{0}» ({1}, {2}): детектор — {3} [{4}]; источник — {5} [{6}]",
                                  curve, g.SourceType, g.Scene,
                                  texts[0] ?? "(нет)", detOk ? "ок" : "НЕТ",
                                  texts[1] ?? "(нет)", srcOk ? "ок" : "НЕТ");
                form.Hide();
            }
        }

        if (n == 0)
        {
            Console.WriteLine("  в файле нет кривых с геометрией — окно не мерено");
            bad++;
        }
    }

    static GeometrySketch Field(GeometryEditorPanel panel, string name)
    {
        FieldInfo f = typeof(GeometryEditorPanel).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        return (GeometrySketch)f.GetValue(panel);
    }

    static TabControl FindTabs(Control root)
    {
        foreach (Control c in root.Controls)
        {
            TabControl tabs = c as TabControl;
            if (tabs != null)
            {
                return tabs;
            }

            tabs = FindTabs(c);
            if (tabs != null)
            {
                return tabs;
            }
        }

        return null;
    }
}
