using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

/// <summary>
/// E21: разворачивает ли сцена детектор к пробе БОКОВОЙ гранью.
///
/// Зачем проба. Ошибка в постановке идёт в РАЗЫ и молчит: у спектра Lu₂O₃ на
/// Nano 16 Pro постановка «с торца» дала отношение сумм-пика к одиночному
/// 0.0112 против измеренных 0.0390 у контрольной съёмки и 0.1182 у той, что
/// снята боком (§13и журнала матрицы). Проверять глазами тут нечего — сцена
/// строится числами, и числа надо предъявить.
///
/// Проверяется ровно то, что должно быть верно по построению:
///   1. у бруска 15 × 18 × 60 торцом к пробе смотрит грань 15 × 18, а глубина
///      60 — то есть прежнее поведение сохранено;
///   2. боком — грань 18 × 60 (самая широкая), глубина 15;
///   3. ОБЪЁМ кристалла одинаков: это тот же кристалл, только повёрнут;
///   4. между пробой и кристаллом лежит обвязка ТОЙ стороны, к которой она
///      обращена: спереди 1.3 + 1.8 мм, сбоку 1.0 + 2.0 мм;
///   5. у ЦИЛИНДРИЧЕСКОГО кристалла боковая постановка запрещена и говорит об
///      этом словами (`FacingError`), а не считает молча.
///
/// Сборка — `probes/build_all.ps1`. Пробе нужен свой `FacingProbe.exe.config`
/// (копия `BecquerelMonitor.exe.config`) — она дотягивается до matdb (T32).
/// </summary>
static class FacingProbe
{
    [STAThread]
    static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        GlobalConfigManager.GetInstance();

        bool ok = true;
        ok &= Boxes();
        ok &= Wrapping();
        ok &= CylinderRefused();
        ok &= RoundTrip();
        ok &= Sketch();
        ok &= Editor();

        Console.WriteLine();
        Console.WriteLine(ok ? "СОШЛОСЬ" : "РАЗОШЛОСЬ");
        return ok ? 0 : 1;
    }

    static GeometryModel Bar(GeometryDetectorFacing facing)
    {
        GeometryModel g = GeometryEditorPanel.Blank();
        GeometryPresets.Preset preset =
            GeometryPresets.Items.FirstOrDefault(p => p.Name == "Atom Spectra Nano 16");
        if (preset != null)
        {
            preset.Apply(g);
        }

        g.Shape = CrystalShape.Box;
        g.CrystalBoxX = 15.0;
        g.CrystalBoxY = 18.0;
        g.CrystalBoxZ = 60.0;
        g.FrontReflectorThickness = 1.3;
        g.SideReflectorThickness = 1.0;
        g.FrontCladdingThickness = 1.8;
        g.SideCladdingThickness = 2.0;
        g.Facing = facing;
        return g;
    }

    // ------------------------------------------------------------------
    static bool Boxes()
    {
        Console.WriteLine("== грань, обращённая к пробе, и глубина ==");
        double fx, fy, fd, sx, sy, sd;
        Bar(GeometryDetectorFacing.Front).CrystalBoxInScene(out fx, out fy, out fd);
        Bar(GeometryDetectorFacing.Side).CrystalBoxInScene(out sx, out sy, out sd);
        Console.WriteLine("   торцом : грань {0:F0} x {1:F0} мм, глубина {2:F0} мм",
                          2 * fx, 2 * fy, fd);
        Console.WriteLine("   боком  : грань {0:F0} x {1:F0} мм, глубина {2:F0} мм",
                          2 * sx, 2 * sy, sd);

        bool front = Near(2 * fx, 15) && Near(2 * fy, 18) && Near(fd, 60);
        bool side = Near(2 * sx, 18) && Near(2 * sy, 60) && Near(sd, 15);
        double vFront = 2 * fx * 2 * fy * fd, vSide = 2 * sx * 2 * sy * sd;
        bool volume = Near(vFront, vSide);
        double areaFront = 2 * fx * 2 * fy, areaSide = 2 * sx * 2 * sy;
        Console.WriteLine("   площадь грани: {0:F0} -> {1:F0} мм2, в {2:F2} раза",
                          areaFront, areaSide, areaSide / areaFront);
        Console.WriteLine("   объём {0:F0} против {1:F0} мм3 — {2}",
                          vFront, vSide, volume ? "СОВПАЛ" : "РАЗОШЁЛСЯ");
        Console.WriteLine("   торцом верно: {0}, боком верно: {1}", front, side);
        return front && side && volume;
    }

    // ------------------------------------------------------------------
    static bool Wrapping()
    {
        Console.WriteLine();
        Console.WriteLine("== обвязка между пробой и кристаллом ==");
        double front = Gap(Bar(GeometryDetectorFacing.Front));
        double side = Gap(Bar(GeometryDetectorFacing.Side));
        Console.WriteLine("   торцом : {0:F2} мм (ждём 1.3 + 1.8 = 3.1)", front);
        Console.WriteLine("   боком  : {0:F2} мм (ждём 1.0 + 2.0 = 3.0)", side);
        bool ok = Near(front, 3.1, 0.02) && Near(side, 3.0, 0.02);
        Console.WriteLine("   {0}", ok ? "обе стороны берут СВОЮ обвязку" : "ОБВЯЗКА НЕ ТА");
        return ok;
    }

    /// <summary>Толщина вещества от передней плоскости кристалла до пробы, мм.
    /// Снимается из САМОЙ СЦЕНЫ: её печатает `Simulate --dump-scene`, и здесь
    /// берётся тем же путём — через симулятор, а не пересчётом полей.</summary>
    static double Gap(GeometryModel g)
    {
        var sim = new EfficiencySimulator(g);
        string scene = sim.DumpScene();
        double zMin = 0.0;
        foreach (string raw in scene.Split('\n'))
        {
            string[] p = raw.Trim().Split(' ');
            if (p.Length >= 7 && p[0] == "region" && p[1] == "box")
            {
                double z0 = double.Parse(p[5], CultureInfo.InvariantCulture);
                zMin = Math.Min(zMin, z0);
            }
        }

        return -zMin * 10.0;      // сцена в сантиметрах
    }

    // ------------------------------------------------------------------
    static bool CylinderRefused()
    {
        Console.WriteLine();
        Console.WriteLine("== цилиндр боком — обязан отказаться словами ==");
        GeometryModel g = Bar(GeometryDetectorFacing.Side);
        g.Shape = CrystalShape.Cylinder;
        string err = g.FacingError;
        Console.WriteLine("   FacingError: {0}", string.IsNullOrEmpty(err) ? "(пусто)" : err);
        GeometryModel box = Bar(GeometryDetectorFacing.Side);
        Console.WriteLine("   у бруска боком FacingError: {0}",
                          string.IsNullOrEmpty(box.FacingError) ? "(пусто, верно)" : box.FacingError);
        bool ok = !string.IsNullOrEmpty(err) && string.IsNullOrEmpty(box.FacingError);
        Console.WriteLine("   {0}", ok ? "отказ на месте" : "ОТКАЗА НЕТ — цилиндр посчитался бы молча");
        return ok;
    }

    // ------------------------------------------------------------------
    static bool RoundTrip()
    {
        Console.WriteLine();
        Console.WriteLine("== ключ DS_Facing переживает запись и чтение ==");
        string path = Path.Combine(Path.GetTempPath(), "facing_probe.in");
        GeometryWriter.Save(Bar(GeometryDetectorFacing.Side), path);
        string text = File.ReadAllText(path, Encoding.GetEncoding(1251));
        bool written = text.Contains("DS_Facing = SIDE");
        GeometryModel back = GeometryModel.Load(path);
        Console.WriteLine("   ключ записан: {0}, прочитан как: {1}", written, back.Facing);

        // Файл БЕЗ ключа обязан читаться как прежде — иначе все геометрии,
        // снятые до 15.08.2026, молча сменят постановку.
        string plain = Path.Combine(Path.GetTempPath(), "facing_probe_front.in");
        GeometryWriter.Save(Bar(GeometryDetectorFacing.Front), plain);
        string plainText = File.ReadAllText(plain, Encoding.GetEncoding(1251));
        GeometryModel old = GeometryModel.Load(plain);
        Console.WriteLine("   у передней постановки ключа нет: {0}, читается как: {1}",
                          !plainText.Contains("DS_Facing"), old.Facing);

        bool ok = written && back.Facing == GeometryDetectorFacing.Side
                  && !plainText.Contains("DS_Facing")
                  && old.Facing == GeometryDetectorFacing.Front;
        Console.WriteLine("   {0}", ok ? "круг замкнулся" : "КРУГ РАЗОМКНУТ");
        return ok;
    }

    // ------------------------------------------------------------------
    /// <summary>
    /// Чертёж ОБЯЗАН меняться. Проверка не «нарисовалось без исключения», а
    /// «две постановки дают РАЗНЫЕ картинки»: молча рисовать прежний брусок,
    /// пока считается развёрнутый, — худший из возможных исходов, потому что
    /// человек в этом случае видит подтверждение своей ошибки.
    /// </summary>
    static bool Sketch()
    {
        Console.WriteLine();
        Console.WriteLine("== чертёж следует за разворотом ==");
        string front = Render(Bar(GeometryDetectorFacing.Front));
        string side = Render(Bar(GeometryDetectorFacing.Side));
        Console.WriteLine("   отпечаток торцом : {0}", front);
        Console.WriteLine("   отпечаток боком  : {0}", side);
        bool ok = front != side;
        Console.WriteLine("   {0}", ok ? "картинки РАЗНЫЕ" : "КАРТИНКИ ОДИНАКОВЫЕ — чертёж врёт");
        return ok;
    }

    /// <summary>Отпечаток картинки: доли цветных точек, огрублённые до сотых.
    /// Сравнивать попиксельно нельзя — сглаживание даёт шум по краям.</summary>
    static string Render(GeometryModel g)
    {
        using (var sketch = new GeometrySketch { Size = new Size(420, 320) })
        using (var bmp = new Bitmap(420, 320))
        {
            sketch.Mode = GeometrySketch.SketchMode.Detector;
            sketch.SetModel(g);
            sketch.DrawToBitmap(bmp, new Rectangle(0, 0, 420, 320));
            int crystal = 0, total = 0;
            Color mark = Color.FromArgb(0x35, 0xA5, 0xAD);      // цвет кристалла
            for (int x = 0; x < bmp.Width; x += 2)
            {
                for (int y = 0; y < bmp.Height; y += 2)
                {
                    Color c = bmp.GetPixel(x, y);
                    total++;
                    if (Math.Abs(c.R - mark.R) < 24 && Math.Abs(c.G - mark.G) < 24
                        && Math.Abs(c.B - mark.B) < 24)
                    {
                        crystal++;
                    }
                }
            }

            // Доля площади кристалла и его габариты в точках — три числа,
            // которых достаточно, чтобы отличить брусок от развёрнутого.
            int minX = int.MaxValue, maxX = -1, minY = int.MaxValue, maxY = -1;
            for (int x = 0; x < bmp.Width; x++)
            {
                for (int y = 0; y < bmp.Height; y++)
                {
                    Color c = bmp.GetPixel(x, y);
                    if (Math.Abs(c.R - mark.R) < 24 && Math.Abs(c.G - mark.G) < 24
                        && Math.Abs(c.B - mark.B) < 24)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }

            double share = total > 0 ? 100.0 * crystal / total : 0.0;
            return maxX < 0
                ? "кристалл не нарисован"
                : string.Format(CultureInfo.InvariantCulture, "{0:F2} %, {1} x {2} точек",
                                share, maxX - minX + 1, maxY - minY + 1);
        }
    }

    // ------------------------------------------------------------------
    /// <summary>Редактор: выбор есть, включён только у бруска и не залипает.</summary>
    static bool Editor()
    {
        Console.WriteLine();
        Console.WriteLine("== выбор стороны в редакторе ==");
        using (var panel = new GeometryEditorPanel())
        {
            panel.SetModel(Bar(GeometryDetectorFacing.Side));
            var combo = (ComboBox)Field(panel, "facingCombo");
            var boxRadio = (RadioButton)Field(panel, "boxRadio");
            var cylRadio = (RadioButton)Field(panel, "cylinderRadio");
            Console.WriteLine("   боковая геометрия -> выбрано {0}, включён {1}",
                              combo.SelectedIndex, combo.Enabled);
            bool loaded = combo.SelectedIndex == 1 && combo.Enabled;

            GeometryModel built = (GeometryModel)typeof(GeometryEditorPanel)
                .GetMethod("BuildModel", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(panel, null);
            Console.WriteLine("   собранная модель: {0}", built.Facing);
            bool round = built.Facing == GeometryDetectorFacing.Side;

            // Переключение на цилиндр обязано СНЯТЬ боковую постановку, иначе
            // в поле останется «сбоку», а сцена соберётся передней.
            cylRadio.Checked = true;
            GeometryModel afterCyl = (GeometryModel)typeof(GeometryEditorPanel)
                .GetMethod("BuildModel", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(panel, null);
            Console.WriteLine("   после переключения на цилиндр: выбрано {0}, включён {1}, модель {2}",
                              combo.SelectedIndex, combo.Enabled, afterCyl.Facing);
            bool cleared = combo.SelectedIndex == 0 && !combo.Enabled
                           && afterCyl.Facing == GeometryDetectorFacing.Front;

            boxRadio.Checked = true;
            bool back = combo.Enabled;
            Console.WriteLine("   возврат к бруску: выбор снова доступен {0}", back);

            bool ok = loaded && round && cleared && back;
            Console.WriteLine("   {0}", ok ? "редактор ведёт себя верно" : "РЕДАКТОР НЕ СОГЛАСОВАН");
            return ok;
        }
    }

    static object Field(object target, string name)
    {
        FieldInfo f = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        if (f == null)
        {
            throw new InvalidOperationException("нет поля " + name);
        }

        return f.GetValue(target);
    }

    static bool Near(double a, double b, double eps = 1e-6)
    {
        return Math.Abs(a - b) <= eps;
    }
}
