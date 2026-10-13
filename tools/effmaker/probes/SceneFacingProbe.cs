using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;

/// <summary>
/// `AMBER64`: разворачивает ли <see cref="GeometryScenes"/> ОБВЯЗКУ при
/// боковой постановке (<see cref="GeometryDetectorFacing.Side"/>).
///
/// Зачем проба, а не чтение кода. `EfficiencySimulator.Build` при `Side`
/// меняет местами торцевую и боковую обвязку (~~`E21`~~) — между пробой и
/// кристаллом ложится обвязка ТОЙ стороны, к которой проба обращена. Три
/// функции `GeometryScenes`, из которых считаются размеры ПОЛЕВЫХ сцен и
/// потолок дозы, читали `Front*`/`Side*` «как названо». Расхождение молчит:
/// сцена строится, счёт доходит до конца, кривая выходит чужой.
///
/// Мерка — не пересчёт полей руками, а САМА СЦЕНА: `DumpScene()` печатает
/// области в сантиметрах, и из них снимаются поперечник обвязанного
/// детектора, вынос кристалла и длина вдоль оси. Три функции сверяются с
/// этими числами.
///
/// Проверяется:
///   1. боковая постановка — три функции сходятся со сценой;
///   2. сцена «в лунке» (`Borehole`) даёт колодец шире прибора ровно на
///      2 × `BoreholeClearanceMm`, и точки пробы лежат ВНЕ корпуса;
///   3. `DoseRateGeometry.ProjectedAreaBoundCm2` — потолок пиковой кривой —
///      считается по той же описанной сфере, что и сцена;
///   4. контроль неизменности: `Facing.Front` — те же числа ПОБИТОВО;
///   5. контроль неизменности: равные торцевая и боковая обвязка при `Side` —
///      те же числа ПОБИТОВО (обмен на них ничего не меняет);
///   6. РЕДАКТОР: смена стороны ПЕРЕСЧИТЫВАЕТ полевую сцену. Поперечник
///      прибора у двух постановок разный, значит и колодец разный; если
///      `FacingChanged` сцену не трогает, в полях остаётся лунка от ПРЕЖНЕЙ
///      постановки — числа правдоподобны, а посчитаны по другому прибору.
///
///     scenefacingprobe
/// </summary>
static class SceneFacingProbe
{
    static int failed;

    [STAThread]
    static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        // ⛔ (`T247`) Культура ЦЕЛИКОМ инвариантная, приказ Amber 05.09.2026:
        //    разделитель дробной части — точка и в печати, и в разборе.
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        GlobalConfigManager.GetInstance();

        Console.WriteLine("AMBER64: разворот обвязки в GeometryScenes при Facing.Side");
        Console.WriteLine();

        SideIsWrapped();
        Borehole();
        DoseCeiling();
        FrontUnchanged();
        SymmetricUnchanged();
        EditorFollowsFacing();

        Console.WriteLine();
        Console.WriteLine(failed == 0 ? "СОШЛОСЬ" : "РАЗОШЛОСЬ: " + failed.ToString(CultureInfo.InvariantCulture));
        return failed == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------
    // Приборы
    // ------------------------------------------------------------------

    /// <summary>
    /// RC-103 из корпуса (`RC103_point0.in`), кристалл переписан РАВНОВЕЛИКИМ
    /// бруском 10 × 10 × 10 мм: у цилиндра боковая постановка запрещена
    /// (`FacingError`, ~~`E21`~~), а куб нарочно оставляет разницу ТОЛЬКО в
    /// обвязке — сам кристалл при развороте не меняется, и всё расхождение
    /// принадлежит торцевому зазору 3.5 мм против бокового 0.
    /// </summary>
    static GeometryModel Rc103(GeometryDetectorFacing facing)
    {
        GeometryModel g = GeometryEditorPanel.Blank();
        g.Shape = CrystalShape.Box;
        g.CrystalBoxX = 10.0;
        g.CrystalBoxY = 10.0;
        g.CrystalBoxZ = 10.0;
        g.FrontReflectorThickness = 1.0;
        g.SideReflectorThickness = 1.0;
        g.FrontGapThickness = 3.5;
        g.SideGapThickness = 0.0;
        g.FrontCladdingThickness = 1.0;
        g.SideCladdingThickness = 1.0;
        g.MountingThickness = 1.0;
        g.Facing = facing;
        // Точечный источник: у сцены не появляется области пробы, и в дампе
        // остаются ТОЛЬКО части прибора — иначе проба перед ним увела бы
        // передний срез сцены на себя.
        g.SourceType = GeometrySourceType.Point;
        g.Scene = GeometrySceneKind.None;
        g.PointDistance = 100.0;
        return g;
    }

    /// <summary>Тот же прибор, но торцевая и боковая обвязка РАВНЫ — обмен на
    /// нём тождественен, и числа обязаны остаться прежними побитово.</summary>
    static GeometryModel Symmetric(GeometryDetectorFacing facing)
    {
        GeometryModel g = Rc103(facing);
        g.FrontGapThickness = 2.0;
        g.SideGapThickness = 2.0;
        return g;
    }

    // ------------------------------------------------------------------
    // Мерка: что построил симулятор
    // ------------------------------------------------------------------

    sealed class Scene
    {
        /// <summary>Передний срез прибора (самая близкая к пробе плоскость), мм.</summary>
        public double FaceMm;

        /// <summary>Задний срез прибора, мм.</summary>
        public double BackMm;

        /// <summary>Наружный поперечник обвязанного прибора, мм (у бруска — диагональ).</summary>
        public double OuterDiameterMm;

        /// <summary>Глубина кристалла вдоль оси, мм.</summary>
        public double CrystalDepthMm;

        /// <summary>Вынос середины кристалла от переднего среза, мм.</summary>
        public double HeightAboveSampleMm
        {
            get { return -this.FaceMm + 0.5 * this.CrystalDepthMm; }
        }

        /// <summary>Длина прибора вдоль оси, мм.</summary>
        public double LengthMm
        {
            get { return this.BackMm - this.FaceMm; }
        }

        /// <summary>Наименьший радиус поля по тем же словам, что и у сцены.</summary>
        public double MinFieldRadiusMm
        {
            get
            {
                double half = 0.5 * this.OuterDiameterMm;
                return GeometryScenes.FieldRadiusMargin
                       * Math.Sqrt(half * half + 0.25 * this.LengthMm * this.LengthMm);
            }
        }
    }

    /// <summary>
    /// Снять размеры ИЗ САМОЙ СЦЕНЫ. `DumpScene` печатает области в порядке
    /// поиска, в сантиметрах: `region box m ax ay z0 z1 crystal|-` и
    /// `region tub m rIn rOut z0 z1 crystal|-`.
    /// </summary>
    static Scene Measure(GeometryModel g)
    {
        string dump = new EfficiencySimulator(g).DumpScene();
        double zMin = double.MaxValue, zMax = double.MinValue;
        double ax = 0.0, ay = 0.0, rOut = 0.0;
        double crystalZ0 = 0.0, crystalZ1 = 0.0;
        bool seenCrystal = false;
        foreach (string raw in dump.Split('\n'))
        {
            string[] p = raw.Trim().Split(' ');
            if (p.Length < 7 || p[0] != "region")
            {
                continue;
            }

            double a = double.Parse(p[3], CultureInfo.InvariantCulture);
            double b = double.Parse(p[4], CultureInfo.InvariantCulture);
            double z0 = double.Parse(p[5], CultureInfo.InvariantCulture);
            double z1 = double.Parse(p[6], CultureInfo.InvariantCulture);
            if (p[1] == "box")
            {
                ax = Math.Max(ax, a);
                ay = Math.Max(ay, b);
            }
            else
            {
                rOut = Math.Max(rOut, b);
            }

            zMin = Math.Min(zMin, z0);
            zMax = Math.Max(zMax, z1);
            if (p.Length >= 8 && p[7] == "crystal")
            {
                crystalZ0 = z0;
                crystalZ1 = z1;
                seenCrystal = true;
            }
        }

        if (!seenCrystal)
        {
            throw new InvalidOperationException("в сцене нет кристалла: " + dump);
        }

        double outer = ax > 0.0 && ay > 0.0
            ? 2.0 * Math.Sqrt(ax * ax + ay * ay)
            : 2.0 * rOut;
        return new Scene
        {
            FaceMm = zMin * GeometryModel.MmPerCm,
            BackMm = zMax * GeometryModel.MmPerCm,
            OuterDiameterMm = outer * GeometryModel.MmPerCm,
            CrystalDepthMm = (crystalZ1 - crystalZ0) * GeometryModel.MmPerCm,
        };
    }

    // ------------------------------------------------------------------
    // Проверки
    // ------------------------------------------------------------------

    static void SideIsWrapped()
    {
        Console.WriteLine("== 1. боковая постановка: три функции против сцены ==");
        GeometryModel g = Rc103(GeometryDetectorFacing.Side);
        Scene s = Measure(g);
        Console.WriteLine("   сцена: срез {0:F3} мм, зад {1:F3} мм, поперечник {2:F3} мм, кристалл {3:F3} мм",
                          s.FaceMm, s.BackMm, s.OuterDiameterMm, s.CrystalDepthMm);

        Three("поперечник, мм", GeometryScenes.DetectorOuterDiameterMm(g), s.OuterDiameterMm);
        Three("вынос середины кристалла, мм", GeometryScenes.CrystalHeightAboveSampleMm(g), s.HeightAboveSampleMm);
        Three("наименьший радиус поля, мм", GeometryScenes.MinFieldRadiusMm(g), s.MinFieldRadiusMm);
    }

    static void Borehole()
    {
        Console.WriteLine();
        Console.WriteLine("== 2. сцена «в лунке»: колодец и просвет до угла корпуса ==");
        GeometryModel g = Rc103(GeometryDetectorFacing.Side);
        Scene s = Measure(g);
        GeometryScenes.Borehole(g, 2614.0);
        double clearance = 0.5 * g.MarinelliHoleDiameter - 0.5 * s.OuterDiameterMm;
        Console.WriteLine("   колодец Ø {0:F3} мм; обещано {1:F3} мм просвета на сторону, вышло {2:F3} мм",
                          g.MarinelliHoleDiameter, GeometryScenes.BoreholeClearanceMm, clearance);
        Check("колодец шире прибора ровно на два просвета",
              Math.Abs(g.MarinelliHoleDiameter - (s.OuterDiameterMm + 2.0 * GeometryScenes.BoreholeClearanceMm)) < 1e-9,
              string.Format(CultureInfo.InvariantCulture, "разница {0:F3} мм",
                            g.MarinelliHoleDiameter - (s.OuterDiameterMm + 2.0 * GeometryScenes.BoreholeClearanceMm)));
        Check("точки пробы лежат ВНЕ корпуса", clearance > 0.0,
              string.Format(CultureInfo.InvariantCulture, "просвет {0:F3} мм", clearance));

        List<GeometryScenes.Issue> issues = GeometryScenes.Inconsistencies(g);
        Console.WriteLine("   несогласованных размеров: {0}", issues.Count);
        foreach (GeometryScenes.Issue issue in issues)
        {
            Console.WriteLine("      {0}: {1}", issue.Field, issue.Resource);
        }
    }

    static void DoseCeiling()
    {
        Console.WriteLine();
        Console.WriteLine("== 3. потолок пиковой кривой в дозе (ProjectedAreaBoundCm2) ==");
        GeometryModel g = Rc103(GeometryDetectorFacing.Side);
        Scene s = Measure(g);
        double got = DoseRateGeometry.ProjectedAreaBoundCm2(g);
        double halfDiagonalCm = s.MinFieldRadiusMm / GeometryScenes.FieldRadiusMargin / GeometryModel.MmPerCm;
        double want = Math.PI * halfDiagonalCm * halfDiagonalCm;
        Three("потолок, см²", got, want);
    }

    static void FrontUnchanged()
    {
        Console.WriteLine();
        Console.WriteLine("== 4. КОНТРОЛЬ НЕИЗМЕННОСТИ: Facing.Front — правка его не касается ==");
        Unchanged(Rc103(GeometryDetectorFacing.Front));
    }

    static void SymmetricUnchanged()
    {
        Console.WriteLine();
        Console.WriteLine("== 5. КОНТРОЛЬ НЕИЗМЕННОСТИ: Side при РАВНОЙ обвязке — обмен тождественен ==");
        Unchanged(Symmetric(GeometryDetectorFacing.Side));
    }

    /// <summary>
    /// Числа трёх функций печатаются круговым видом `R`: он восстанавливает
    /// double ПОБИТОВО, и сравнение прогонов ДО и ПОСЛЕ правки — сравнение
    /// этих строк. Рядом — сверка со сценой с допуском: числа считаются
    /// разным порядком действий (сцена в сантиметрах), и требовать от них
    /// совпадения последнего разряда нечестно.
    /// </summary>
    static void Unchanged(GeometryModel g)
    {
        Scene s = Measure(g);
        double outer = GeometryScenes.DetectorOuterDiameterMm(g);
        double height = GeometryScenes.CrystalHeightAboveSampleMm(g);
        double radius = GeometryScenes.MinFieldRadiusMm(g);
        Console.WriteLine("   ПОБИТОВО поперечник {0:R}", outer);
        Console.WriteLine("   ПОБИТОВО вынос      {0:R}", height);
        Console.WriteLine("   ПОБИТОВО радиус     {0:R}", radius);
        Three("поперечник, мм", outer, s.OuterDiameterMm);
        Three("вынос середины кристалла, мм", height, s.HeightAboveSampleMm);
        Three("наименьший радиус поля, мм", radius, s.MinFieldRadiusMm);
    }

    /// <summary>
    /// Редактор геометрии: смена СТОРОНЫ обязана пересчитать полевую сцену.
    ///
    /// Форма кристалла это делает (`ShapeChanged` зовёт `RecomputeScene` — «в
    /// лунку брусок входит диагональю, цилиндр диаметром»), а сторона — тот же
    /// размер прибора, и меняет его СИЛЬНЕЕ: у RC-103 поперечник 19.799 против
    /// 29.698 мм. Список `SceneDetectorFields` тут не помогает — он сверяется
    /// с именем ТЕКСТОВОГО поля в `FieldLeft`, а сторона стоит списком.
    /// </summary>
    static void EditorFollowsFacing()
    {
        Console.WriteLine();
        Console.WriteLine("== 6. РЕДАКТОР: смена стороны пересчитывает полевую сцену ==");
        using (var panel = new GeometryEditorPanel())
        {
            panel.SetModel(Rc103(GeometryDetectorFacing.Side));
            var source = (System.Windows.Forms.ComboBox)Field(panel, "sourceTypeCombo");
            var facing = (System.Windows.Forms.ComboBox)Field(panel, "facingCombo");
            if (source == null || facing == null)
            {
                Check("поля редактора на месте", false, "нет sourceTypeCombo/facingCombo");
                return;
            }

            // Строка 5 списка источников — маринелли + сцена «в лунке»
            // (`SourceKinds`); выбор её и запускает счёт сцены.
            source.SelectedIndex = 5;
            double outerSide = GeometryScenes.DetectorOuterDiameterMm(Model(panel));
            double holeSide = Model(panel).MarinelliHoleDiameter;
            Console.WriteLine("   боком : поперечник {0:F3} мм, колодец {1:F3} мм", outerSide, holeSide);

            facing.SelectedIndex = 0;      // переключили на «торцом»
            double outerFront = GeometryScenes.DetectorOuterDiameterMm(Model(panel));
            double holeFront = Model(panel).MarinelliHoleDiameter;
            Console.WriteLine("   торцом: поперечник {0:F3} мм, колодец {1:F3} мм", outerFront, holeFront);

            Check("сторона доехала до модели", Math.Abs(outerFront - outerSide) > 1e-6,
                  string.Format(CultureInfo.InvariantCulture, "разница поперечника {0:F3} мм",
                                outerFront - outerSide));
            Check("колодец пересчитан по НОВОЙ стороне",
                  Math.Abs(holeFront - (outerFront + 2.0 * GeometryScenes.BoreholeClearanceMm)) < 1e-6,
                  string.Format(CultureInfo.InvariantCulture, "колодец {0:F3}, ждём {1:F3}",
                                holeFront, outerFront + 2.0 * GeometryScenes.BoreholeClearanceMm));
        }
    }

    static object Field(object o, string name)
    {
        System.Reflection.FieldInfo f = o.GetType().GetField(name,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic);
        return f == null ? null : f.GetValue(o);
    }

    static GeometryModel Model(object panel)
    {
        return (GeometryModel)Field(panel, "model");
    }

    // ------------------------------------------------------------------

    /// <summary>Сравнение с допуском: сцена печатается в сантиметрах, и
    /// последние разряды у двух порядков действий расходятся законно.</summary>
    static void Three(string what, double got, double want)
    {
        double diff = want != 0.0 ? 100.0 * (got - want) / want : 0.0;
        Console.WriteLine("   {0,-34} функция {1,10:F3}   сцена {2,10:F3}   расхождение {3,8:F2} %",
                          what, got, want, diff);
        Check(what + " — функция сошлась со сценой",
              Math.Abs(got - want) <= 1e-9 * Math.Max(1.0, Math.Abs(want)), null);
    }

    static void Check(string what, bool ok, string detail)
    {
        Console.WriteLine("   {0,-52} {1}{2}", what, ok ? "ок" : "ПРОВАЛ",
                          detail != null ? "  " + detail : "");
        if (!ok)
        {
            failed++;
        }
    }
}
