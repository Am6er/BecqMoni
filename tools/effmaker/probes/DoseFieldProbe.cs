using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DoseFieldProbe
{
    /// <summary>
    /// `AMBER93` (полоса П145, 24.09.2026): мощность дозы по кривой ПОЛЕВОЙ
    /// сцены — «на земле» (`GeometryScenes.Ground`) и «в лунке»
    /// (`GeometryScenes.Borehole`).
    ///
    /// Геометрический множитель дозы `G = ⟨1/(4πr²)⟩` по объёму пробы
    /// (<see cref="DoseRateGeometry.FluencePerPhoton"/>) считается БЕЗ ослабления
    /// в самой пробе. Для полевой сцены проба — грунт на три-пятнадцать
    /// пробегов, и `∫dV/(4πr²)` растёт с размером сцены как ln R, тогда как
    /// нерассеянный поток от полупространства ограничен: `Φ = S_v/(2μ)`
    /// (Beck, DeCampo, Gogolak 1972, HASL-258; ICRU 53).
    ///
    /// Решение Amber 24.09.2026 (вопросником): «Честная формула + мерка» —
    /// считать поток с ослаблением грунта и сверить с коэффициентами UNSCEAR;
    /// не сойдётся в ~10 % — отказ словами для полевых сцен.
    ///
    ///   §1 ЧЕСТНАЯ ФОРМУЛА: `I(μ) = ∫dV e^{−μ·l}/(4πr²)`, l — путь в грунте
    ///      от точки пробы до центра кристалла; по направлениям из центра
    ///      кристалла `I = ½∫dcosθ (1 − e^{−μL(θ)})/μ`, L — хорда грунта по
    ///      лучу. Положительный контроль: при μ → 0 это `G·V` приложения (то
    ///      же тело, другой способ интегрирования); и при бесконечной сцене —
    ///      `1/(2μ)` над грунтом и `1/μ` в лунке (грунт со всех сторон).
    ///   §2 МЕРКА: воздушная керма на 1 Бк/кг K-40 (1460.822 кэВ, выход
    ///      0.1066) против UNSCEAR 2000, приложение B: 0.0417 нГр/ч на Бк/кг
    ///      (однородный грунт, 1 м, С рассеянными).
    ///   §3 ПРИЛОЖЕНИЕ: вход дозы по кривой полевой сцены обязан отказать
    ///      словами; точка, сосуд и сцена поля `ISO` — пройти числом.
    ///
    ///     dosefieldprobe [--preset=«имя»] [--energy=3000]
    /// </summary>
    static class Program
    {
        static int failed;
        static int checks;

        /// <summary>UNSCEAR 2000, том I, приложение B: K-40, нГр/ч на Бк/кг, 1 м над грунтом.</summary>
        const double UnscearK40 = 0.0417;

        const double K40Kev = 1460.822;

        /// <summary>Выход линии 1460.822 на распад K-40 (ENSDF: 10.66 %).</summary>
        const double K40Yield = 0.1066;

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            string only = "Gamma-1S UDS-GC 63x63";
            double top = 3000.0;
            foreach (string a in args)
            {
                if (a.StartsWith("--preset=", StringComparison.Ordinal)) only = a.Substring(9);
                else if (a.StartsWith("--energy=", StringComparison.Ordinal))
                    top = double.Parse(a.Substring(9), CultureInfo.InvariantCulture);
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: " + a);
                    return 2;
                }
            }

            GlobalConfigManager.GetInstance();
            Console.WriteLine("AMBER93: доза по кривой полевой сцены");

            foreach (GeometryPresets.Preset preset in GeometryPresets.Items)
            {
                if (only != "all" && preset.Name != only)
                {
                    continue;
                }

                GeometryModel blank = GeometryEditorPanel.Blank();
                preset.Apply(blank);
                GeometryModel ground = blank.Clone();
                GeometryScenes.Ground(ground, top);
                GeometryModel hole = blank.Clone();
                GeometryScenes.Borehole(hole, top);

                Scene(preset.Name + " — НА ЗЕМЛЕ", ground, false);
                Scene(preset.Name + " — В ЛУНКЕ", hole, true);
                Application(preset.Name, ground, hole, blank);
            }

            Console.WriteLine();
            Console.WriteLine(failed == 0
                ? "СОШЛОСЬ (" + checks.ToString(CultureInfo.InvariantCulture) + ")"
                : "РАЗОШЛОСЬ: " + failed.ToString(CultureInfo.InvariantCulture)
                  + " из " + checks.ToString(CultureInfo.InvariantCulture));
            return failed == 0 ? 0 : 1;
        }

        // ==================================================================
        // §1, §2
        // ==================================================================

        sealed class Body
        {
            public double Zc;          // центр кристалла на оси, см
            public double ZTop;        // поверхность грунта, см
            public double ZBottom;     // низ грунта, см
            public double ROut;        // радиус сцены, см
            public double RHole;       // радиус лунки (0 — лунки нет), см
            public double ZHoleBottom; // дно лунки, см
            public double Volume;      // объём грунта, см³
        }

        /// <summary>
        /// Тело сцены в той же системе, что у <see cref="DoseRateGeometry.FluencePerPhoton"/>:
        /// ось к детектору, торец корпуса на `z = −(обвязка спереди)`, центр
        /// кристалла на `z = h/2`.
        /// </summary>
        static Body BodyOf(GeometryModel model, bool borehole)
        {
            GeometryModel g = model.InCentimeters();
            double hc = g.CrystalHeight;
            if (g.Shape == CrystalShape.Box)
            {
                double ax, ay;
                g.CrystalBoxInScene(out ax, out ay, out hc);
            }

            double tfr = g.FrontReflectorThickness, tfc = g.FrontCladdingThickness;
            double tfg = Math.Max(0.0, g.FrontGapThickness);
            if (g.Facing == GeometryDetectorFacing.Side)
            {
                tfr = g.SideReflectorThickness;
                tfc = g.SideCladdingThickness;
                tfg = Math.Max(0.0, g.SideGapThickness);
            }

            double zFace = -(tfr + tfg + tfc);
            var b = new Body { Zc = 0.5 * hc };
            if (!borehole)
            {
                b.ROut = Math.Max(0.0, 0.5 * g.BeakerDiameter - g.BeakerSideWallThickness);
                b.ZTop = zFace - g.BeakerToDetectorDistance - g.BeakerEndWallThickness;
                b.ZBottom = b.ZTop - g.SourceHeight;
                b.RHole = 0.0;
                b.ZHoleBottom = b.ZTop;
                b.Volume = Math.PI * b.ROut * b.ROut * (b.ZTop - b.ZBottom);
            }
            else
            {
                double rh = 0.5 * g.MarinelliHoleDiameter;
                double rOut = Math.Max(0.5 * g.MarinelliBeakerDiameter, rh + 0.1);
                double zCeiling = zFace - g.MarinelliToDetectorDistance;
                double cap = Math.Max(0.0, g.MarinelliSourceHeight - g.MarinelliHoleHeight);
                b.RHole = rh;
                b.ROut = rOut;
                b.ZHoleBottom = zCeiling;
                b.ZBottom = zCeiling - cap;
                b.ZTop = b.ZBottom + g.MarinelliSourceHeight;
                b.Volume = Math.PI * rh * rh * cap + Math.PI * (rOut * rOut - rh * rh) * g.MarinelliSourceHeight;
            }

            return b;
        }

        /// <summary>
        /// `I(μ) = ∫dV e^{−μl}/(4πr²)`, см, по направлениям из центра кристалла.
        /// μ = 0 — без ослабления (`½∫L dcosθ`).
        /// </summary>
        static double Integral(Body b, double mu)
        {
            const int steps = 400000;
            double sum = 0.0;
            double dc = 2.0 / steps;
            for (int i = 0; i < steps; i++)
            {
                double c = -1.0 + (i + 0.5) * dc;      // косинус угла к оси ВНИЗ (к грунту)
                double s = Math.Sqrt(Math.Max(0.0, 1.0 - c * c));
                double L = Chord(b, c, s);
                if (!(L > 0.0)) continue;
                sum += mu > 0.0 ? (1.0 - Math.Exp(-mu * L)) / mu : L;
            }

            return 0.5 * sum * dc;
        }

        /// <summary>Хорда грунта по лучу из центра кристалла: направление вниз по оси — c &gt; 0.</summary>
        static double Chord(Body b, double c, double s)
        {
            // Луч: ρ = r·s, z = Zc − r·c.
            double inf = double.PositiveInfinity;
            // Вход в грунт.
            double rin;
            if (b.RHole <= 0.0)
            {
                // Детектор над поверхностью: грунт — ниже ZTop.
                if (!(c > 0.0)) return 0.0;
                rin = (b.Zc - b.ZTop) / c;
                if (rin < 0.0) rin = 0.0;
            }
            else
            {
                // Детектор в лунке: выход из лунки — стенка, дно или устье.
                double rSide = s > 0.0 ? b.RHole / s : inf;
                double rBot = c > 0.0 ? (b.Zc - b.ZHoleBottom) / c : inf;
                double rTop = c < 0.0 ? (b.ZTop - b.Zc) / (-c) : inf;
                rin = Math.Min(rSide, Math.Min(rBot, rTop));
                if (rTop <= rin) return 0.0;             // ушёл в воздух над землёй
            }

            // Выход из сцены: боковая стенка, низ, поверхность.
            double rOutSide = s > 0.0 ? b.ROut / s : inf;
            double rOutBot = c > 0.0 ? (b.Zc - b.ZBottom) / c : inf;
            double rOutTop = c < 0.0 ? (b.ZTop - b.Zc) / (-c) : inf;
            double rout = Math.Min(rOutSide, Math.Min(rOutBot, rOutTop));
            return Math.Max(0.0, rout - rin);
        }

        static void Scene(string name, GeometryModel model, bool borehole)
        {
            Head("§1–§2. " + name);
            Body b = BodyOf(model, borehole);
            string note;
            double gApp = double.NaN;
            try
            {
                gApp = DoseRateGeometry.FluencePerPhoton(model, out note);
                Console.WriteLine("  приложение: G = {0:E5} 1/см² ({1})", gApp, note);
            }
            catch (DoseRateRefusalException ex)
            {
                Console.WriteLine("  приложение: ОТКАЗ «{0}»", Short(ex.Message));
                // Прежний множитель — то же тело со сценой «нет»: так считало
                // приложение до П145, и так же его считает сосуд, набранный руками.
                GeometryModel asVessel = model.Clone();
                asVessel.Scene = GeometrySceneKind.None;
                gApp = DoseRateGeometry.FluencePerPhoton(asVessel, out note);
                Console.WriteLine("  до П145 (то же тело, сцена «нет»): G = {0:E5} 1/см²", gApp);
            }

            double i0 = Integral(b, 0.0);
            Console.WriteLine("  тело: R {0:F1} см, грунт z {1:F2}…{2:F2} см, лунка r {3:F2} см до z {4:F2}, центр кристалла z {5:F2};"
                              + " V = {6:E4} см³", b.ROut, b.ZBottom, b.ZTop, b.RHole, b.ZHoleBottom, b.Zc, b.Volume);
            Console.WriteLine("  без ослабления: I(0) = {0:F3} см; у приложения G·V = {1:F3} см", i0, gApp * b.Volume);
            if (!double.IsNaN(gApp))
            {
                Ok(Math.Abs(i0 / (gApp * b.Volume) - 1.0) < 2e-3,
                   string.Format(CultureInfo.InvariantCulture,
                       "положительный контроль тела: I(0) по направлениям = G·V приложения ({0:+0.0000;-0.0000} %)",
                       100.0 * (i0 / (gApp * b.Volume) - 1.0)));
            }

            GeometryMaterial soil = model.Source;
            foreach (double e in new[] { 661.657, K40Kev, 2614.511 })
            {
                double mu = soil.LinearAttenuation(e);               // 1/см
                double ia = Integral(b, mu);
                double infinite = (borehole ? 1.0 : 0.5) / mu;
                Console.WriteLine("  {0,8:F3} кэВ: μ = {1:F5} 1/см; I(μ) = {2:F3} см; {3} = {4:F3} см (охват {5:F2} %);"
                                  + " без ослабления ×{6:F2}",
                                  e, mu, ia, borehole ? "1/μ" : "1/(2μ)", infinite, 100.0 * ia / infinite, i0 / ia);
            }

            // §2 мерка: K-40.
            double muK = soil.LinearAttenuation(K40Kev);
            double iK = Integral(b, muK);
            double sv = 1e-3 * soil.Density * K40Yield;            // квант/(с·см³) на 1 Бк/кг
            double kermaPerFluence = DoseRateCoefficients.DoseRatePerFluenceRate(K40Kev)
                                     / DoseRateCoefficients.AmbientDoseConversion(K40Kev);   // мкГр/ч на квант/(см²·с)
            double honest = sv * iK * kermaPerFluence * 1000.0;     // нГр/ч
            double asApp = double.IsNaN(gApp) ? double.NaN : sv * gApp * b.Volume * kermaPerFluence * 1000.0;
            // Предел бесконечной сцены: у прибора НАД грунтом — полупространство,
            // `S_v/(2μ)`; у прибора В ЛУНКЕ грунт почти со всех сторон — полное
            // пространство, `S_v/μ`, и мерка UNSCEAR (1 м над полупространством)
            // для него — вдвое больше (ICRU 53: геометрия 4π против 2π).
            double solid = borehole ? 2.0 : 1.0;
            double limit = sv * 0.5 * solid / muK * kermaPerFluence * 1000.0;
            double reference = UnscearK40 * solid;
            Console.WriteLine("  K-40, 1 Бк/кг, грунт {0} {1:F2} г/см³: керма нерассеянных — честная {2:F4} нГр/ч,"
                              + " предел бесконечной сцены ({3}) {4:F4}, как у приложения {5:F4}; UNSCEAR{6} (с рассеянными) {7:F4}",
                              soil.Name, soil.Density, honest, borehole ? "S_v/μ" : "S_v/(2μ)", limit, asApp,
                              borehole ? " ×2 (4π)" : "", reference);
            Console.WriteLine("    честная / UNSCEAR = {0:F3}; приложение / UNSCEAR = {1:F2}", honest / reference, asApp / reference);
            Ok(honest / limit > 0.95 && honest / limit <= 1.0 + 1e-9,
               string.Format(CultureInfo.InvariantCulture,
                   "положительный контроль формулы: честная ≤ предела бесконечной сцены и не ниже 95 % его ({0:F4})",
                   honest / limit));
            Console.WriteLine("    сходится ли честная формула с UNSCEAR в 10 %: {0}",
                              Math.Abs(honest / reference - 1.0) <= 0.10 ? "ДА" : "НЕТ");
        }

        // ==================================================================
        // §3. Приложение: вход дозы по кривой
        // ==================================================================

        static void Application(string name, GeometryModel ground, GeometryModel hole, GeometryModel blank)
        {
            Head("§3. " + name + ": вход дозы по кривой");
            Refuses("кривая сцены «на земле»", ground);
            Refuses("кривая сцены «в лунке»", hole);

            GeometryModel point = blank.Clone();
            point.Scene = GeometrySceneKind.None;
            point.SourceType = GeometrySourceType.Point;
            point.PointDistance = 100.0;
            Passes("точка 100 мм (обычная сцена)", point);

            GeometryModel iso = blank.Clone();
            GeometryScenes.Iso(iso);
            PassesIso("сцена поля ISO", iso);

            // Текст отказа — по-русски при русском интерфейсе (пара в
            // `Resources.ru.resx`), по-английски — иначе.
            System.Globalization.CultureInfo ui = System.Threading.Thread.CurrentThread.CurrentUICulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentUICulture = new CultureInfo("ru-RU");
                checks++;
                try
                {
                    DoseRateInput.Of(Curve(ground), null);
                    failed++;
                    Console.WriteLine("  [НЕТ]  ru: отказа нет");
                }
                catch (DoseRateRefusalException ex)
                {
                    bool ru = ex.Message.StartsWith("Мощность дозы:", StringComparison.Ordinal)
                              && ex.Message.IndexOf("ISO", StringComparison.Ordinal) >= 0;
                    if (!ru) failed++;
                    Console.WriteLine("  [{0}]   ru: «{1}»", ru ? "ок" : "НЕТ", ex.Message);
                }
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentUICulture = ui;
            }

            // Тот же грунт тем же телом, но сцена снята руками — не поле: проходит.
            GeometryModel manual = ground.Clone();
            manual.Scene = GeometrySceneKind.None;
            Passes("то же тело грунта со сценой «нет» (сосуд руками)", manual);
        }

        static EfficiencyConfigData Curve(GeometryModel geometry)
        {
            var points = new List<ROIEfficiencyData>();
            for (double e = 20.0; e <= 3000.0; e *= 1.5)
            {
                points.Add(new ROIEfficiencyData { Energy = e, Efficiency = 0.01, ErrorPercent = 1.0 });
            }

            points.Add(new ROIEfficiencyData { Energy = 3000.0, Efficiency = 0.01, ErrorPercent = 1.0 });
            return new EfficiencyConfigData("поле") { Curve = points, Geometry = geometry };
        }

        static void Refuses(string what, GeometryModel geometry)
        {
            checks++;
            try
            {
                DoseRateInput input = DoseRateInput.Of(Curve(geometry), null);
                failed++;
                Console.WriteLine("  [НЕТ]  {0}: прошло числом, G = {1:E4} 1/см² — отказа нет", what, input.FluencePerPhoton);
            }
            catch (DoseRateRefusalException ex)
            {
                bool about = ex.Message.IndexOf("ISO", StringComparison.Ordinal) >= 0;
                if (!about) failed++;
                Console.WriteLine("  [{0}]   {1}: отказ «{2}»", about ? "ок" : "НЕТ", what, ex.Message);
            }
        }

        static void PassesIso(string what, GeometryModel geometry)
        {
            checks++;
            try
            {
                EfficiencyConfigData curve = Curve(geometry);
                curve.ComputeStamp = DoseRateInput.FluenceStampMark;
                DoseRateInput input = DoseRateInput.Of(curve, null);
                bool one = input.FluencePerPhoton == 1.0;
                if (!one) failed++;
                Console.WriteLine("  [{0}]   {1}: числом, G = {2:R} (ждём ровно 1)", one ? "ок" : "НЕТ", what, input.FluencePerPhoton);
            }
            catch (DoseRateRefusalException ex)
            {
                failed++;
                Console.WriteLine("  [НЕТ]  {0}: отказ «{1}»", what, ex.Message);
            }
        }

        static void Passes(string what, GeometryModel geometry)
        {
            checks++;
            try
            {
                DoseRateInput input = DoseRateInput.Of(Curve(geometry), null);
                Console.WriteLine("  [ок]   {0}: числом, G = {1:E4} 1/см²", what, input.FluencePerPhoton);
            }
            catch (DoseRateRefusalException ex)
            {
                failed++;
                Console.WriteLine("  [НЕТ]  {0}: отказ «{1}»", what, ex.Message);
            }
        }

        static string Short(string text)
        {
            return text.Length > 90 ? text.Substring(0, 90) + "…" : text;
        }

        static void Head(string text)
        {
            Console.WriteLine();
            Console.WriteLine(text);
            Console.WriteLine(new string('-', Math.Min(100, text.Length)));
        }

        static void Ok(bool ok, string what)
        {
            checks++;
            if (!ok) failed++;
            Console.WriteLine("  [" + (ok ? "ок" : "НЕТ") + "]   " + what);
        }
    }
}
