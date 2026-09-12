using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace DoseRateFromCurveProbe
{
    /// <summary>
    /// Приёмка `AMBER18` (полоса П1, 12.09.2026): мощность дозы считается ОТ
    /// КРИВОЙ ЭФФЕКТИВНОСТИ, ВЫБРАННОЙ НА ПАНЕЛИ, а вкладка `Dose Rate` и
    /// `DoseRateConfig` прибора сняты целиком.
    ///
    ///  §1 ЕДИНИЦЫ. Ḣ*(10)/φ̇ на 662 кэВ против ICRP 74 (K_a/Φ из табл. A.1,
    ///     h*(10)/K_a из A.21): 3.70 пЗв·см² на квант. Эталона больше нет, и
    ///     это единственная опора уровня.
    ///
    ///  §2 ГЕОМЕТРИЧЕСКИЙ МНОЖИТЕЛЬ. Точка — ровно 1/(4πR²); сосуд, унесённый
    ///     далеко, сходится с точкой на том же расстоянии (закон обратных
    ///     квадратов — контроль квадратуры); маринелли считается.
    ///
    ///  §3 ТРИ ПЛЕЧА НА ЖИВОМ СПЕКТРЕ КОРПУСА (`AS80_Cs137_0cm`, сцена
    ///     `AS80_point0`): (A) кривая С МАТРИЦЕЙ → число по ПОЛНОЙ
    ///     эффективности, без пометки; (B) та же кривая БЕЗ матрицы → число со
    ///     знаком «≈» по пиковой; отличие A и B названо процентом; (C) кривая
    ///     ЛСРМ — те же точки БЕЗ геометрии → отказ с причиной (у такой кривой
    ///     нет масштаба; расхождение с буквой решения (3) названо в журнале);
    ///     (D) кривая без `Curve` → ПУСТО (null); (E) кривой нет → ПУСТО.
    ///     Второй спектр — `G1S16_Cs137_P5` (точка на 5 см).
    ///
    ///  §4 ЖИВОЙ СПЕКТР AMBER (`--live=` + `--store=`): число по её матрице
    ///     против прежнего показания 2.566 мкЗв/ч при ручных точках — только
    ///     печать, без приговора: прежнее число было ручной сверкой.
    ///
    ///  §5 ОТРАЖЕНИЕ ИЗ СБОРКИ: в `DeviceConfigForm` нет ни `tabPage7`, ни
    ///     `comboDoseRateEfficiency`; в `DeviceConfigInfo` нет `DoseRateConfig`;
    ///     типов `DoseRateConfig` и `DoseRateCalibrationPoint` в сборке нет.
    ///
    ///  §6 СТАРЫЙ XML с `&lt;DoseRateConfig&gt;` и 36 точками (поставочный
    ///     `config/device/RC-103.xml`, только чтение) читается без отказа, и при
    ///     пересохранении элемента нет.
    ///
    ///  §7 ПУТЬ ПРИЛОЖЕНИЯ: `DoseRateManager.Calculate(ResultData)` через склад
    ///     `ResponseMatrixStore` — матрица кладётся в склад каталога пробы на
    ///     время прогона и снимается после.
    ///
    ///  §8 СНИМОК СТРОКИ СОСТОЯНИЯ: та же строка, что кладёт `MainForm.ShowDoseRate`
    ///     (`Resources.DoseRate + " " + dose`), отрисована в `StatusStrip` без окна
    ///     `BecqMoni` (`DrawToBitmap`), PNG в `%TEMP%\doseratefromcurve-shots`;
    ///     снимок обязан быть не фоном.
    ///
    ///  §9 СЦЕНА ПОЛЯ `ISO` (`AMBER13` (б), полоса П6 12.09.2026) — ПОТРЕБИТЕЛЬ
    ///     отклика в см²: тот же спектр `G1S16_Cs137_P5` с кривой и матрицей сцены
    ///     `ISO` той же геометрии (`G1S_point5`, сфера R = 50 и 100 см; матрицы —
    ///     из `--iso=` каталога, кривая считается там же и кэшируется).
    ///     (1) доза по матрице R = 50 и R = 100 сходится в пределах статистики
    ///     матриц; (2) СВЕРКА РУКАМИ на диапазоне 662: φ̇ = N/A_own, Ḣ = φ̇ ×
    ///     Ḣ*(10)/φ̇ — число пробы равно ручному, и то же с константой 3.751
    ///     пЗв·см² (662 кэВ) в пределах хода коэффициента по диапазону; (3) G ≡ 1,
    ///     признак `PerUnitFluence`, «≈» только у кривой; (4) СМЕШЕНИЕ НОРМИРОВОК —
    ///     отказ словами: кривая в долях у геометрии поля, кривая с клеймом
    ///     `norm=fluence` у сцены с источником.
    ///     ⚠ ФИЗИЧЕСКОГО СМЫСЛА У ЧИСЛА НЕТ: спектр снят от ТОЧЕЧНОГО источника на
    ///     5 см, а делится на отклик ИЗОТРОПНОГО ПОЛЯ — это проверка МЕХАНИКИ
    ///     (единицы, G, признак, развёртка), и цитировать его как дозу нельзя.
    ///
    ///   doseratefromcurveprobe [--dir=&lt;корпус&gt;] [--live=&lt;спектр.xml&gt;] [--store=&lt;каталог .rmx&gt;] [--live-device=&lt;прибор.xml&gt;]
    ///                          [--iso=&lt;каталог .in/.rmx сцены ISO&gt;]
    ///   doseratefromcurveprobe --sabotage=zerorow|foreign|nogeom|mark|mixnorm|isoq   (ждёт ОТКАЗ)
    ///
    /// ⛔ Приёмка, которая проходит всегда, не мерит ничего. `--sabotage`
    /// портит РОВНО ОДНУ вещь и требует, чтобы проба ОТКАЗАЛА; коды у него
    /// перевёрнуты: 0 — отказ получен (проба смотрит), 1 — не получен (слепа).
    ///
    ///   zerorow — у матрицы обнулены строки узлов вокруг 662 кэВ: расчёт
    ///             обязан отказать «эффективность 0» на этом диапазоне;
    ///   foreign — кривой подсунута матрица ЧУЖОЙ сцены: вход обязан отказать;
    ///   nogeom  — у кривой с матрицей снята геометрия: вход обязан отказать;
    ///   mark    — плечу B (пиковая) подменён признак: ждём, что проба заметит
    ///             число без «≈»;
    ///   mixnorm — (§9) кривой сцены поля (`norm=fluence`) подсунута матрица с
    ///             признаком `PerEmittedQuantum` (клеймо сходится, признак нет):
    ///             вход обязан отказать СЛОВАМИ, а не выдать число;
    ///   isoq    — (§9) сцена поля пропущена через ветку ПРОБЫ: множитель G
    ///             подменён на 1/(4πR²) точки на радиусе сферы (так считал бы
    ///             потребитель без ветки `ISO`): сверка руками обязана поймать.
    ///
    /// Коды возврата обычного прогона: 0 — все проверки прошли, 1 — есть
    /// непрошедшие, 2 — отказ оснастки.
    /// </summary>
    static class Program
    {
        static int failed;
        static int checks;
        static string corpusDir = @"tools\CORPUS\corpus";
        static string livePath;
        static string liveStore;
        static string liveDevice;
        static string sabotage;
        static string isoDir = @"tools\effmaker\out\p6_iso";

        /// <summary>Прежнее показание ASN16 при ручных точках (`AMBER13`, 10.09.2026).</summary>
        const double FormerAsn16Reading = 2.566;

        /// <summary>Линия, на которой сверяются руками (§9), кэВ.</summary>
        const double CsLineKev = 661.657;

        /// <summary>
        /// Ḣ*(10)/φ̇ на 662 кэВ, названный в журнале П1 §1: 3.751 пЗв·см² на квант
        /// = 3.751e-12 Зв·см² × 3600 с/ч × 1e6 мкЗв/Зв — мкЗв/ч на квант/(см²·с).
        /// </summary>
        const double CsLineMicroSvPerHourPerFluenceRate = 3.751e-12 * 3600.0 * 1.0e6;

        [STAThread]
        static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch (IOException) { }
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            foreach (string a in args)
            {
                if (a.StartsWith("--dir=", StringComparison.Ordinal)) corpusDir = a.Substring(6);
                else if (a.StartsWith("--live=", StringComparison.Ordinal)) livePath = a.Substring(7);
                else if (a.StartsWith("--store=", StringComparison.Ordinal)) liveStore = a.Substring(8);
                else if (a.StartsWith("--live-device=", StringComparison.Ordinal)) liveDevice = a.Substring(14);
                else if (a.StartsWith("--sabotage=", StringComparison.Ordinal)) sabotage = a.Substring(11);
                else if (a.StartsWith("--iso=", StringComparison.Ordinal)) isoDir = a.Substring(6);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            if (sabotage != null && sabotage != "zerorow" && sabotage != "foreign"
                && sabotage != "nogeom" && sabotage != "mark" && sabotage != "mixnorm" && sabotage != "isoq")
            {
                Console.Error.WriteLine("--sabotage= принимает zerorow, foreign, nogeom, mark, mixnorm или isoq");
                return 2;
            }

            Console.WriteLine("ПРИЁМКА AMBER18 (П1, 12.09.2026): мощность дозы от кривой панели");
            Console.WriteLine("сборка под рукой: " + typeof(DoseRateManager).Assembly.Location);
            if (sabotage != null)
            {
                Console.WriteLine("⚠ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: испорчено «" + sabotage + "», ждём ОТКАЗ");
            }

            try
            {
                Units();
                GeometryFactor();
                ThreeArms();
                Live();
                Reflection();
                OldXml();
                AppPath();
                StatusBarShot();
                IsoField();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ОТКАЗ ОСНАСТКИ: " + ex);
                return 2;
            }

            Console.WriteLine();
            Console.WriteLine("проверок {0}, непрошедших {1}", checks, failed);
            if (sabotage != null)
            {
                if (failed > 0)
                {
                    Console.WriteLine("ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ ПРОЙДЕН: порча «" + sabotage + "» замечена");
                    return 0;
                }

                Console.WriteLine("ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ ПРОВАЛЕН: порча НЕ замечена — проба слепа");
                return 1;
            }

            Console.WriteLine(failed == 0 ? "ВСЁ СОШЛОСЬ" : "ПРОВАЛОВ " + failed);
            return failed > 0 ? 1 : 0;
        }

        static void Ok(bool condition, string what)
        {
            checks++;
            if (!condition) failed++;
            Console.WriteLine((condition ? "  [ок]   " : "  [НЕТ]  ") + what);
        }

        static void Head(string text)
        {
            Console.WriteLine();
            Console.WriteLine("──────────────────────────────────────────────────────────────");
            Console.WriteLine(text);
            Console.WriteLine("──────────────────────────────────────────────────────────────");
        }

        static string F(double v, string fmt)
        {
            return v.ToString(fmt, CultureInfo.InvariantCulture);
        }

        // ==================================================================
        // §1. Единицы
        // ==================================================================

        static void Units()
        {
            Head("§1. ЕДИНИЦЫ: Ḣ*(10)/φ̇ против ICRP 74");
            double perFluence = DoseRateCoefficients.DoseRatePerFluenceRate(662.0);
            // мкЗв/ч на квант/(см²·с) → пЗв·см² на квант: ÷3600 ÷1e6 ×1e12
            double pSvCm2 = perFluence / 3600.0 / 1.0e6 * 1.0e12;
            Console.WriteLine("  662 кэВ: μ_en/ρ = {0} м²/кг, h*(10)/K_a = {1}, Factor = {2}",
                F(DoseRateCoefficients.MassEnergyAbsorptionAir(662.0), "e4"),
                F(DoseRateCoefficients.AmbientDoseConversion(662.0), "f4"),
                F(DoseRateCoefficients.Factor(662.0), "f4"));
            Console.WriteLine("  Ḣ*(10)/φ̇ = {0} мкЗв/ч на квант/(см²·с) = {1} пЗв·см² на квант",
                F(perFluence, "e4"), F(pSvCm2, "f3"));
            // ICRP 74: K_a/Φ 600 кэВ 2.84, 800 кэВ 3.63 пГр·см² (табл. A.1);
            // логарифмически по энергии на 662 → 3.08; h*(10)/K_a = 1.20 (A.21).
            const double Anchor = 3.08 * 1.20;
            double diff = 100.0 * (pSvCm2 - Anchor) / Anchor;
            Ok(Math.Abs(diff) < 3.0, string.Format(CultureInfo.InvariantCulture,
                "опора ICRP 74 на 662 кэВ: {0:f3} пЗв·см², у нас {1:f3}, расхождение {2:+0.00;-0.00} % (допуск 3 %)",
                Anchor, pSvCm2, diff));
            Ok(Math.Abs(DoseRateCoefficients.MicroSievertPerHourPerFactor
                        - 10.0 * 1000.0 * 1.602176634e-16 * 1.0e6 * 3600.0) < 1e-20,
               "множитель единиц собран из названных сомножителей: " + F(DoseRateCoefficients.MicroSievertPerHourPerFactor, "e6"));
        }

        // ==================================================================
        // §2. Геометрический множитель
        // ==================================================================

        static void GeometryFactor()
        {
            Head("§2. ГЕОМЕТРИЧЕСКИЙ МНОЖИТЕЛЬ G = <1/(4πr²)> по объёму источника");

            // Точка: цилиндр Ø50×50 мм, обвязка 1+2 мм, источник на 100 мм.
            GeometryModel point = Bare();
            point.SourceType = GeometrySourceType.Point;
            point.PointDistance = 100.0;
            string note;
            double g = DoseRateGeometry.FluencePerPhoton(point, out note);
            double r = 0.5 * 5.0 + 0.1 + 0.2 + 10.0;   // см до центра кристалла
            double expected = 1.0 / (4.0 * Math.PI * r * r);
            Console.WriteLine("  " + note);
            Ok(Math.Abs(g - expected) < 1e-12 * expected,
               string.Format(CultureInfo.InvariantCulture, "точка: G = {0:e6}, 1/(4πR²) при R = {1:f3} см = {2:e6}", g, r, expected));

            // Сосуд, унесённый далеко: Ø20×20 мм на 1000 мм — сходится с точкой
            // на расстоянии до его центра лучше 0.1 %.
            GeometryModel far = Bare();
            far.SourceType = GeometrySourceType.Cylinder;
            far.BeakerDiameter = 20.0;
            far.BeakerSideWallThickness = 0.0;
            far.BeakerEndWallThickness = 0.0;
            far.SourceHeight = 20.0;
            far.BeakerToDetectorDistance = 1000.0;
            double gFar = DoseRateGeometry.FluencePerPhoton(far, out note);
            double rFar = 0.5 * 5.0 + 0.1 + 0.2 + 100.0 + 1.0;
            double expFar = 1.0 / (4.0 * Math.PI * rFar * rFar);
            Console.WriteLine("  " + note);
            Ok(Math.Abs(gFar - expFar) < 1e-3 * expFar,
               string.Format(CultureInfo.InvariantCulture, "далёкий сосуд: G = {0:e6}, точка в его центре {1:e6}, расхождение {2:e2}",
                             gFar, expFar, Math.Abs(gFar - expFar) / expFar));

            // Кювета той же массы на том же месте — те же 0.1 %.
            GeometryModel box = Bare();
            box.SourceType = GeometrySourceType.Box;
            box.BoxSourceX = 20.0;
            box.BoxSourceY = 20.0;
            box.BoxSourceHeight = 20.0;
            box.BoxToDetectorDistance = 1000.0;
            double gBox = DoseRateGeometry.FluencePerPhoton(box, out note);
            Console.WriteLine("  " + note);
            Ok(Math.Abs(gBox - expFar) < 1e-3 * expFar,
               string.Format(CultureInfo.InvariantCulture, "далёкая кювета: G = {0:e6}, расхождение с точкой {1:e2}",
                             gBox, Math.Abs(gBox - expFar) / expFar));

            // Маринелли 1 л вокруг Ø50 кристалла: считается, положителен,
            // и МЕНЬШЕ 1/(4π r_min²) при r_min — до стенки колодца.
            GeometryModel mar = Bare();
            mar.SourceType = GeometrySourceType.Marinelli;
            mar.MarinelliBeakerDiameter = 120.0;
            mar.MarinelliBeakerHeight = 120.0;
            mar.MarinelliHoleDiameter = 60.0;
            mar.MarinelliHoleHeight = 80.0;
            mar.MarinelliSideThickness = 1.0;
            mar.MarinelliEndWallThickness = 1.0;
            mar.MarinelliHoleSideThickness = 1.0;
            mar.MarinelliHoleEndWallThickness = 1.0;
            mar.MarinelliSourceHeight = 110.0;
            mar.MarinelliToDetectorDistance = 5.0;
            double gMar = DoseRateGeometry.FluencePerPhoton(mar, out note);
            double rMin = 3.0 + 0.1;
            Console.WriteLine("  " + note);
            Ok(gMar > 0.0 && gMar < 1.0 / (4.0 * Math.PI * rMin * rMin),
               string.Format(CultureInfo.InvariantCulture, "маринелли: G = {0:e6} < 1/(4π·{1:f1}²) = {2:e6}",
                             gMar, rMin, 1.0 / (4.0 * Math.PI * rMin * rMin)));

            // Отказы: источник в центре кристалла, проба без объёма.
            GeometryModel inside = Bare();
            inside.SourceType = GeometrySourceType.Point;
            inside.PointDistance = -(0.5 * 50.0 + 1.0 + 2.0);
            Refuses("точечный источник в центре кристалла", () => DoseRateGeometry.FluencePerPhoton(inside, out note));
            GeometryModel empty = Bare();
            empty.SourceType = GeometrySourceType.Cylinder;
            Refuses("сосуд без объёма", () => DoseRateGeometry.FluencePerPhoton(empty, out note));
        }

        static GeometryModel Bare()
        {
            var g = new GeometryModel();
            g.IsScintillator = true;
            g.Shape = CrystalShape.Cylinder;
            g.CrystalDiameter = 50.0;
            g.CrystalHeight = 50.0;
            g.FrontReflectorThickness = 1.0;
            g.FrontCladdingThickness = 2.0;
            g.SideReflectorThickness = 1.0;
            g.SideCladdingThickness = 2.0;
            return g;
        }

        static void Refuses(string what, Action action)
        {
            checks++;
            try
            {
                action();
                failed++;
                Console.WriteLine("  [НЕТ]  " + what + " — прошло МОЛЧА, числом");
            }
            catch (DoseRateRefusalException ex)
            {
                Console.WriteLine("  [ок]   " + what + " → «" + Short(ex.Message) + "»");
            }
        }

        static string Short(string text)
        {
            text = (text ?? "").Replace(Environment.NewLine, " ");
            return text.Length > 110 ? text.Substring(0, 107) + "..." : text;
        }

        // ==================================================================
        // §3. Три плеча на спектре корпуса
        // ==================================================================

        static void ThreeArms()
        {
            Head("§3. ТРИ ПЛЕЧА: кривая с матрицей / без матрицы (≈) / без геометрии (отказ) / без точек (пусто)");
            Scene("AS80_Cs137_0cm", "AS80_point0", "AS80_lu_front");
            Scene("G1S16_Cs137_P5", "G1S_point5", "G1S_point25");
        }

        static void Scene(string spectrumName, string scene, string foreignScene)
        {
            Console.WriteLine();
            Console.WriteLine("  == " + spectrumName + " (сцена " + scene + ") ==");
            ResultData data = LoadSpectrum(Path.Combine(corpusDir, "spectra", spectrumName + ".xml"));
            if (data == null || data.Efficiency == null)
            {
                Ok(false, "нет спектра корпуса или у него нет узла <Efficiency>: " + spectrumName);
                return;
            }

            EfficiencyConfigData curve = data.Efficiency;
            ResponseMatrix matrix = LoadMatrix(Path.Combine(corpusDir, "geometries", scene + ".rmx"), curve.Geometry);
            if (matrix == null)
            {
                Ok(false, "нет годной матрицы сцены " + scene);
                return;
            }

            if (sabotage == "zerorow")
            {
                int zeroed = 0;
                for (int i = 0; i < matrix.Energies.Length; i++)
                {
                    if (matrix.Energies[i] > 560.0 && matrix.Energies[i] < 780.0)
                    {
                        matrix.Rows[i] = new float[matrix.Rows[i].Length];
                        zeroed++;
                    }
                }

                Console.WriteLine("  ⚠ ПОРЧА: обнулены строки {0} узлов 560…780 кэВ", zeroed);
            }

            if (sabotage == "foreign")
            {
                ResponseMatrix other = LoadMatrix(Path.Combine(corpusDir, "geometries", foreignScene + ".rmx"), null);
                Console.WriteLine("  ⚠ ПОРЧА: подсунута матрица сцены " + foreignScene);
                matrix = other;
            }

            if (sabotage == "nogeom")
            {
                Console.WriteLine("  ⚠ ПОРЧА: у кривой снята геометрия, матрица подана как есть");
                curve.Geometry = null;
            }

            var manager = new DoseRateManager(Config());

            // (A) полная эффективность
            DoseRate full = null;
            DoseRateInput inputFull = null;
            try
            {
                inputFull = DoseRateInput.Of(curve, matrix);
                full = manager.Calculate(data, inputFull);
            }
            catch (DoseRateRefusalException ex)
            {
                full = new DoseRate { Refusal = ex.Message };
            }

            Console.WriteLine("  геометрия: " + (inputFull == null ? "(вход отказан)" : inputFull.GeometryNote));
            Console.WriteLine("  (A) с матрицей: " + full);
            Ok(full.Refusal.Length == 0 && full.Rate > 0.0 && !full.Approximate,
               "(A) число по ПОЛНОЙ эффективности, без пометки: " + Short(full.ToString()));
            if (full.Refusal.Length == 0)
            {
                PrintRanges(full);
            }

            // (B) пиковая, та же кривая без матрицы
            DoseRate peak;
            try
            {
                DoseRateInput inputPeak = DoseRateInput.Of(curve, null);
                peak = manager.Calculate(data, inputPeak);
            }
            catch (DoseRateRefusalException ex)
            {
                peak = new DoseRate { Refusal = ex.Message };
            }

            if (sabotage == "mark" && peak.Refusal.Length == 0)
            {
                Console.WriteLine("  ⚠ ПОРЧА: у плеча B снят признак Approximate");
                peak.Approximate = false;
            }

            Console.WriteLine("  (B) без матрицы: " + peak);
            Ok(peak.Refusal.Length == 0 && peak.Rate > 0.0 && peak.Approximate
               && peak.ToString().StartsWith(DoseRate.ApproximateMark, StringComparison.Ordinal),
               "(B) число по ПИКОВОЙ со знаком «≈»: " + Short(peak.ToString()));

            if (full.Refusal.Length == 0 && peak.Refusal.Length == 0)
            {
                double percent = 100.0 * (peak.Rate - full.Rate) / full.Rate;
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  A → B: {0:f4} → {1:f4} мкЗв/ч, пиковая выше полной на {2:+0.0;-0.0} %"
                    + " (пик/полное на 662 кэВ: {3:f4})",
                    full.Rate, peak.Rate, percent,
                    inputFull != null && full.Ranges.Count > 0
                        ? PeakOverFull(curve, matrix, 661.657) : double.NaN));
                Ok(peak.Rate > full.Rate,
                   string.Format(CultureInfo.InvariantCulture,
                       "пиковая ε меньше полной, значит доза по пиковой ВЫШЕ: {0:+0.0;-0.0} %", percent));
            }

            // (C) кривая ЛСРМ: те же точки, без геометрии → отказ с причиной
            var lsrm = new EfficiencyConfigData("ввоз ЛСРМ (без геометрии)")
            {
                Origin = EfficiencyOrigin.Lsrm,
                Curve = curve.Curve.Select(p => p.Clone()).ToList(),
            };
            string refusal = null;
            try
            {
                DoseRateInput.Of(lsrm, null);
            }
            catch (DoseRateRefusalException ex)
            {
                refusal = ex.Message;
            }

            Ok(refusal != null && (refusal.IndexOf("geometry", StringComparison.OrdinalIgnoreCase) >= 0
                                   || refusal.IndexOf("геометри", StringComparison.OrdinalIgnoreCase) >= 0),
               "(C) кривая без геометрии → отказ с причиной: «" + Short(refusal) + "»");

            // (D) кривая без точек → пусто; (E) кривой нет → пусто
            var bare = new EfficiencyConfigData("без точек") { Geometry = curve.Geometry };
            ResultData copy = new ResultData();
            copy.EnergySpectrum = data.EnergySpectrum;
            copy.Efficiency = bare;
            Ok(manager.Calculate(copy) == null, "(D) кривая без Curve → строка состояния ПУСТА (null)");
            copy.Efficiency = null;
            Ok(manager.Calculate(copy) == null, "(E) кривая не выбрана → строка состояния ПУСТА (null)");
        }

        static double PeakOverFull(EfficiencyConfigData curve, ResponseMatrix matrix, double energyKev)
        {
            try
            {
                double full = DoseRateInput.FullEfficiency(matrix, energyKev);
                double peak = DoseRateEstimator.CurveOf(curve.Curve).At(energyKev);
                return full > 0.0 ? peak / full : double.NaN;
            }
            catch (DoseRateRefusalException)
            {
                return double.NaN;
            }
        }

        static void PrintRanges(DoseRate dose)
        {
            Console.WriteLine("     диапазон, кэВ     отсчётов  объяснено  приписано   ε(строка)  ε(свой)   φ̇, 1/(см²·с)  мкЗв/ч   наивно");
            double sum = 0.0, naive = 0.0;
            double seconds = SecondsOf(dose), g = GOf(dose);
            foreach (DoseRateRange r in dose.Ranges)
            {
                sum += r.DoseRate;
                // «Наивно» — запись строки `AMBER18` буквально: все отсчёты
                // диапазона ÷ сумма строки, без вычитания континуума и без пола.
                double naiveRange = r.Efficiency > 0.0 && seconds > 0.0
                    ? r.Counts / seconds / r.Efficiency * g * r.DoseRatePerFluenceRate
                    : 0.0;
                naive += naiveRange;
                if (r.Counts <= 0.0) continue;
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "     {0,7:f1}…{1,-7:f1} {2,9:f0} {3,9:f0} {4,10:f0} {5,10:e2} {6,9:e2} {7,13:e3} {8,8:f4} {9,8:f4}{10}",
                    r.LowKev, r.HighKev, r.Counts, r.Explained, r.Attributed, r.Efficiency, r.OwnEfficiency,
                    r.FluenceRate, r.DoseRate, naiveRange, r.Skipped ? "  (ниже пола, вне покрытия)" : ""));
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "     диапазонов {0}, сумма {1:f4} мкЗв/ч (наивно, без развёртки и пола: {2:f4}), покрытие {3:f1} %",
                dose.Ranges.Count, sum, naive, 100.0 * dose.Coverage));
        }

        /// <summary>Время набора — из любого диапазона с приписанными отсчётами.</summary>
        static double SecondsOf(DoseRate dose)
        {
            foreach (DoseRateRange r in dose.Ranges)
            {
                if (r.Attributed > 0.0 && r.Cps > 0.0) return r.Attributed / r.Cps;
            }

            return double.NaN;
        }

        /// <summary>G — из любого приписанного диапазона: φ̇ = cps/ε_own · G.</summary>
        static double GOf(DoseRate dose)
        {
            foreach (DoseRateRange r in dose.Ranges)
            {
                if (!r.Skipped && r.Cps > 0.0 && r.OwnEfficiency > 0.0) return r.FluenceRate / (r.Cps / r.OwnEfficiency);
            }

            return double.NaN;
        }

        // ==================================================================
        // §4. Живой спектр Amber
        // ==================================================================

        static void Live()
        {
            Head("§4. ЖИВОЙ СПЕКТР (--live=, --store=): против прежних 2.566 мкЗв/ч при ручных точках");
            if (livePath == null)
            {
                Console.WriteLine("  ключ --live= не задан — раздел только печатный, проверок не добавляет");
                return;
            }

            ResultData data = LoadSpectrum(livePath);
            if (data == null || data.Efficiency == null)
            {
                Console.WriteLine("  спектр не прочитан или без <Efficiency>: " + livePath);
                return;
            }

            EfficiencyConfigData curve = data.Efficiency;
            if (liveDevice != null && File.Exists(liveDevice))
            {
                // Кривая, как её выбрали бы на панели из конфигурации прибора
                // (копией), а не снимок из файла спектра: снимок мог устареть
                // против матрицы, пересчитанной позже.
                DeviceConfigInfo device;
                using (var fs = new FileStream(liveDevice, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    device = (DeviceConfigInfo)new XmlSerializer(typeof(DeviceConfigInfo)).Deserialize(fs);
                }

                EfficiencyConfigData fromDevice = device.EfficiencyConfigs.FirstOrDefault(c => c.Guid == curve.Guid);
                Console.WriteLine("  кривая из конфигурации прибора «{0}»: {1}", device.Name,
                                  fromDevice == null ? "той же Guid НЕТ — берётся снимок из файла спектра" : "взята копией, как с панели");
                if (fromDevice != null) curve = fromDevice.Copy();
            }

            Console.WriteLine("  кривая «{0}», guid {1}, геометрия: {2}, источник {3}, InShield {4}",
                curve.Name, curve.Guid, curve.HasGeometry ? "есть" : "нет",
                curve.Geometry == null ? "?" : curve.Geometry.SourceType.ToString(),
                curve.Geometry != null && curve.Geometry.InShield);
            ResponseMatrix matrix = null;
            if (liveStore != null)
            {
                matrix = LoadMatrix(Path.Combine(liveStore, curve.Guid + ".rmx"), curve.Geometry);
            }

            var manager = new DoseRateManager(Config());
            foreach (bool withMatrix in new[] { true, false })
            {
                if (withMatrix && matrix == null)
                {
                    Console.WriteLine("  матрицы в складе нет или она негодна — плечо по полной пропущено");
                    continue;
                }

                try
                {
                    DoseRateInput input = DoseRateInput.Of(curve, withMatrix ? matrix : null);
                    DoseRate dose = manager.Calculate(data, input);
                    Console.WriteLine("  " + (withMatrix ? "по ПОЛНОЙ:  " : "по ПИКОВОЙ: ") + dose);
                    if (withMatrix)
                    {
                        Console.WriteLine("  " + input.GeometryNote);
                    }

                    if (dose.Refusal.Length == 0)
                    {
                        PrintRanges(dose);
                        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                            "  прежнее показание при ручных точках {0:f3} мкЗв/ч; отношение новое/прежнее = {1:f3}",
                            FormerAsn16Reading, dose.Rate / FormerAsn16Reading));
                    }
                }
                catch (DoseRateRefusalException ex)
                {
                    Console.WriteLine("  отказ: " + ex.Message);
                }
            }
        }

        // ==================================================================
        // §5. Отражение из сборки
        // ==================================================================

        static void Reflection()
        {
            Head("§5. ОТРАЖЕНИЕ ИЗ СБОРКИ: снятое снято");
            Assembly app = typeof(DoseRateManager).Assembly;
            Type form = app.GetType("BecquerelMonitor.DeviceConfigForm");
            Ok(form != null, "тип DeviceConfigForm найден");
            if (form != null)
            {
                foreach (string name in new[] { "tabPage7", "comboDoseRateEfficiency", "efficiencyCurve" })
                {
                    Ok(Field(form, name) == null, "у DeviceConfigForm нет поля " + name);
                }

                foreach (string name in new[] { "LoadDoseRateTab", "FillDoseRateEfficiencyCombo", "comboDoseRateEfficiency_SelectedIndexChanged" })
                {
                    Ok(form.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public) == null,
                       "у DeviceConfigForm нет метода " + name);
                }
            }

            Type info = typeof(DeviceConfigInfo);
            Ok(info.GetProperty("DoseRateConfig") == null, "у DeviceConfigInfo нет свойства DoseRateConfig");
            Ok(app.GetType("BecquerelMonitor.DoseRateConfig") == null, "типа DoseRateConfig в сборке нет");
            Ok(app.GetType("BecquerelMonitor.DoseRateCalibrationPoint") == null, "типа DoseRateCalibrationPoint в сборке нет");
            Ok(app.GetType("BecquerelMonitor.DoseRateSpectrumChoice") == null, "типа DoseRateSpectrumChoice в сборке нет");
            Ok(typeof(DoseRateEstimator).GetMethod("Estimate") == null, "у DoseRateEstimator нет Estimate");
            Ok(typeof(DoseRateEstimator).GetMethod("OfferedEfficiencies") == null, "у DoseRateEstimator нет OfferedEfficiencies");
            Ok(typeof(DoseRateManager).GetMethod("Calculate", new[] { typeof(ResultData) }) != null,
               "у DoseRateManager есть Calculate(ResultData) — вход от кривой панели");
        }

        static FieldInfo Field(Type t, string name)
        {
            for (; t != null; t = t.BaseType)
            {
                FieldInfo f = t.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
                if (f != null) return f;
            }

            return null;
        }

        // ==================================================================
        // §6. Старый XML читается
        // ==================================================================

        static void OldXml()
        {
            Head("§6. СТАРЫЙ XML С <DoseRateConfig> ЧИТАЕТСЯ, ПРИ ПЕРЕСОХРАНЕНИИ ЭЛЕМЕНТА НЕТ");
            var serializer = new XmlSerializer(typeof(DeviceConfigInfo));

            // Поставочный RC-103.xml лежит рядом с пробой (план оснастки кладёт
            // config\device\*.xml); только чтение.
            string supply = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "device", "RC-103.xml");
            if (File.Exists(supply))
            {
                string text = File.ReadAllText(supply, Encoding.UTF8);
                int points = CountOf(text, "<DoseRateCalibrationPoint>");
                DeviceConfigInfo cfg;
                using (var fs = new FileStream(supply, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    cfg = (DeviceConfigInfo)serializer.Deserialize(fs);
                }

                Ok(cfg != null && cfg.Name == "RC-103" && cfg.NumberOfChannels > 0,
                   string.Format(CultureInfo.InvariantCulture,
                       "поставочный RC-103.xml ({0} точек в тексте) прочитан: {1}, {2} каналов, кривых {3}",
                       points, cfg.Name, cfg.NumberOfChannels, cfg.EfficiencyConfigs.Count));
                Ok(points == 36, "в тексте 36 точек — их просто перестали читать (файл не тронут)");
                string back = Serialize(serializer, cfg);
                Ok(back.IndexOf("DoseRateConfig", StringComparison.Ordinal) < 0,
                   "при пересохранении элемента DoseRateConfig нет");
            }
            else
            {
                Console.WriteLine("  поставочного RC-103.xml рядом с пробой нет: " + supply);
            }

            // Подставной XML: элемент с точками между другими полями.
            string synthetic = "<?xml version=\"1.0\"?>\r\n<DeviceConfigInfo>\r\n  <Guid>p1</Guid>\r\n  <Name>old</Name>\r\n"
                + "  <NumberOfChannels>1024</NumberOfChannels>\r\n  <DoseRateConfig>\r\n    <DoseRateCalibrationPoints>\r\n"
                + "      <DoseRateCalibrationPoint><LowerBound>40</LowerBound><UpperBound>100</UpperBound><CPS>1</CPS>"
                + "<EtalonDoseRateValue>1</EtalonDoseRateValue></DoseRateCalibrationPoint>\r\n"
                + "    </DoseRateCalibrationPoints>\r\n  </DoseRateConfig>\r\n  <BackgroundSpectrumPathname>x</BackgroundSpectrumPathname>\r\n"
                + "</DeviceConfigInfo>";
            DeviceConfigInfo old;
            using (var reader = new StringReader(synthetic))
            {
                old = (DeviceConfigInfo)serializer.Deserialize(reader);
            }

            Ok(old.Name == "old" && old.NumberOfChannels == 1024 && old.BackgroundSpectrumPathname == "x",
               "подставной XML с <DoseRateConfig> посреди полей читается целиком: поля ДО и ПОСЛЕ элемента на месте");
        }

        static int CountOf(string text, string needle)
        {
            int n = 0;
            for (int i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = text.IndexOf(needle, i + 1, StringComparison.Ordinal))
            {
                n++;
            }

            return n;
        }

        static string Serialize(XmlSerializer serializer, DeviceConfigInfo cfg)
        {
            using (var writer = new StringWriter(CultureInfo.InvariantCulture))
            {
                serializer.Serialize(writer, cfg);
                return writer.ToString();
            }
        }

        // ==================================================================
        // §7. Путь приложения через склад матриц
        // ==================================================================

        static void AppPath()
        {
            Head("§7. ПУТЬ ПРИЛОЖЕНИЯ: DoseRateManager.Calculate(ResultData) через ResponseMatrixStore");
            ResultData data = LoadSpectrum(Path.Combine(corpusDir, "spectra", "AS80_Cs137_0cm.xml"));
            if (data == null || data.Efficiency == null)
            {
                Ok(false, "нет спектра AS80_Cs137_0cm");
                return;
            }

            string source = Path.Combine(corpusDir, "geometries", "AS80_point0.rmx");
            string target = ResponseMatrixStore.PathOf(data.Efficiency.Guid);
            Console.WriteLine("  склад пробы: " + ResponseMatrixStore.Directory);
            var manager = new DoseRateManager(Config());

            // Без файла в складе — «≈».
            if (File.Exists(target)) File.Delete(target);
            DoseRate without = manager.Calculate(data);
            Console.WriteLine("  без файла в складе: " + without + "  [" + manager.LastMatrixNote + "]");
            Ok(without != null && without.Refusal.Length == 0 && without.Approximate,
               "нет матрицы в складе → число со знаком «≈», причина: " + manager.LastMatrixNote);

            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.Copy(source, target, true);
            try
            {
                DoseRate with = manager.Calculate(data);
                Console.WriteLine("  с файлом в складе:  " + with + "  [" + manager.LastMatrixNote + "]");
                Ok(with != null && with.Refusal.Length == 0 && !with.Approximate,
                   "матрица в складе → число по полной, без пометки");

                // Кэш: тот же объект кривой — тот же вход; смена файла — новый.
                DoseRateInput first = manager.InputOf(data.Efficiency);
                DoseRateInput second = manager.InputOf(data.Efficiency);
                Ok(object.ReferenceEquals(first, second), "вход кэшируется по ссылке на кривую и отметке файла матрицы");
                File.SetLastWriteTimeUtc(target, DateTime.UtcNow.AddSeconds(5));
                DoseRateInput third = manager.InputOf(data.Efficiency);
                Ok(!object.ReferenceEquals(first, third), "смена отметки файла матрицы сбрасывает кэш");

                // Галка «пускать матрицу» снята — тот же путь, что у FSA: «≈».
                data.Efficiency.UseResponseMatrix = false;
                DoseRate off = manager.Calculate(data);
                Ok(off != null && off.Approximate, "UseResponseMatrix = false → как у FSA, матрица не читается: «≈» [" + manager.LastMatrixNote + "]");
                data.Efficiency.UseResponseMatrix = true;
            }
            finally
            {
                try { File.Delete(target); } catch (IOException) { }
            }
        }

        // ==================================================================
        // §8. Снимок строки состояния
        // ==================================================================

        static void StatusBarShot()
        {
            Head("§8. СНИМОК СТРОКИ СОСТОЯНИЯ (без окна BecqMoni)");
            ResultData data = LoadSpectrum(Path.Combine(corpusDir, "spectra", "AS80_Cs137_0cm.xml"));
            ResponseMatrix matrix = data == null || data.Efficiency == null
                ? null
                : LoadMatrix(Path.Combine(corpusDir, "geometries", "AS80_point0.rmx"), data.Efficiency.Geometry);
            if (data == null || matrix == null)
            {
                Ok(false, "нет спектра или матрицы для снимка");
                return;
            }

            var manager = new DoseRateManager(Config());
            string dir = Path.Combine(Path.GetTempPath(), "doseratefromcurve-shots");
            Directory.CreateDirectory(dir);
            foreach (bool withMatrix in new[] { true, false })
            {
                DoseRate dose = manager.Calculate(data, DoseRateInput.Of(data.Efficiency, withMatrix ? matrix : null));
                // Ровно та строка, что кладёт MainForm.ShowDoseRate.
                string text = BecquerelMonitor.Properties.Resources.DoseRate + " " + dose;
                string path = Path.Combine(dir, "status-" + (withMatrix ? "matrix" : "peak") + ".png");
                using (var form = new Form { Width = 700, Height = 60, ShowInTaskbar = false })
                using (var strip = new StatusStrip())
                {
                    var label = new ToolStripStatusLabel(text) { Spring = true, TextAlign = ContentAlignment.MiddleRight };
                    strip.Items.Add(label);
                    form.Controls.Add(strip);
                    IntPtr h = form.Handle;
                    GC.KeepAlive(h);
                    strip.PerformLayout();
                    using (var bmp = new Bitmap(strip.Width, strip.Height))
                    {
                        strip.DrawToBitmap(bmp, new Rectangle(0, 0, strip.Width, strip.Height));
                        bmp.Save(path, ImageFormat.Png);
                        int ink = 0;
                        Color ground = bmp.GetPixel(1, bmp.Height - 2);
                        for (int y = 0; y < bmp.Height; y++)
                        {
                            for (int x = 0; x < bmp.Width; x++)
                            {
                                if (bmp.GetPixel(x, y) != ground) ink++;
                            }
                        }

                        double share = 100.0 * ink / (bmp.Width * (double)bmp.Height);
                        Console.WriteLine("  «" + text + "»");
                        Ok(share > 0.5, string.Format(CultureInfo.InvariantCulture,
                            "снимок {0}: не фон {1:f1} % точек", path, share));
                    }
                }
            }
        }

        // ==================================================================
        // §9. Сцена поля ISO — потребитель отклика в см²
        // ==================================================================

        static void IsoField()
        {
            Head("§9. СЦЕНА ПОЛЯ ISO: доза от отклика в см² (AMBER13 (б), П6)");
            Console.WriteLine("  ⚠ У ЧИСЛА НЕТ ФИЗИЧЕСКОГО СМЫСЛА: спектр снят от ТОЧЕЧНОГО Cs-137 на 5 см,");
            Console.WriteLine("    а делится на отклик ИЗОТРОПНОГО ПОЛЯ. Проверяется МЕХАНИКА, не доза.");
            ResultData data = LoadSpectrum(Path.Combine(corpusDir, "spectra", "G1S16_Cs137_P5.xml"));
            if (data == null || data.Efficiency == null || !data.Efficiency.HasGeometry)
            {
                Ok(false, "нет спектра G1S16_Cs137_P5 или у него нет кривой с геометрией");
                return;
            }

            EfficiencyConfigData baseCurve = data.Efficiency;
            Directory.CreateDirectory(isoDir);
            var manager = new DoseRateManager(Config());

            // Сцена поля из ТОЙ ЖЕ геометрии, что у кривой спектра: только
            // сцена и радиус сферы; `GeometryScenes.Iso` ставит форму
            // источника и `pdistance = R`, как делает редактор.
            double[] radiiCm = { 50.0, 100.0 };
            var rates = new double[radiiCm.Length];
            var ownAreas = new double[radiiCm.Length];
            for (int i = 0; i < radiiCm.Length; i++)
            {
                double radiusCm = radiiCm[i];
                Console.WriteLine();
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  == G1S16_Cs137_P5, сцена ISO той же геометрии, R = {0:f0} см ==", radiusCm));
                GeometryModel g = baseCurve.Geometry.Clone();
                g.Scene = GeometrySceneKind.Iso;
                g.FieldRadius = radiusCm * GeometryModel.MmPerCm;
                GeometryScenes.Iso(g);
                Ok(ResponseMatrix.NormalizationOf(g) == ResponseMatrixNormalization.PerUnitFluence
                   && Math.Abs(g.FieldRadius - radiusCm * GeometryModel.MmPerCm) < 1e-9,
                   string.Format(CultureInfo.InvariantCulture,
                       "геометрия поля: сцена {0}, R = {1:f0} мм, нормировка PerUnitFluence", g.Scene, g.FieldRadius));

                string stem = string.Format(CultureInfo.InvariantCulture, "G1S_point5_iso{0:f0}", radiusCm);
                string inPath = Path.Combine(isoDir, stem + ".in");
                if (!File.Exists(inPath))
                {
                    GeometryWriter.Save(g, inPath);
                    Console.WriteLine("  записан файл сцены для corpusmatrixprobe: " + inPath);
                }

                ResponseMatrix matrix = LoadMatrix(Path.Combine(isoDir, stem + ".rmx"), g);
                if (matrix == null)
                {
                    Ok(false, "нет годной матрицы сцены поля " + stem + " — посчитать: corpusmatrixprobe --dir="
                              + isoDir + " --force (файл .in лежит рядом)");
                    continue;
                }

                Ok(matrix.Normalization == ResponseMatrixNormalization.PerUnitFluence,
                   "матрица поля несёт признак PerUnitFluence (хвост NORM): " + matrix.Normalization);

                EfficiencyConfigData isoCurve = IsoCurve(g, Path.Combine(isoDir, stem + "_curve.xml"), stem);
                if (isoCurve == null)
                {
                    Ok(false, "кривая сцены поля не посчиталась");
                    continue;
                }

                Ok(DoseRateInput.StampNormalization(isoCurve.ComputeStamp) == ResponseMatrixNormalization.PerUnitFluence,
                   "клеймо кривой поля несёт norm=fluence: " + isoCurve.ComputeStamp);
                double peakAt662 = DoseRateEstimator.CurveOf(isoCurve.Curve, ResponseMatrixNormalization.PerUnitFluence,
                                                             DoseRateGeometry.ProjectedAreaBoundCm2(g)).At(CsLineKev);
                double fullAt662 = DoseRateInput.FullEfficiency(matrix, CsLineKev);
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  на 662 кэВ: A_пик (кривая) = {0:f3} см², A_полн (сумма строки) = {1:f3} см², пик/полное {2:f4};"
                    + " потолок кривой πr² = {3:f1} см²",
                    peakAt662, fullAt662, peakAt662 / fullAt662, DoseRateGeometry.ProjectedAreaBoundCm2(g)));
                Ok(peakAt662 > 5.0 && peakAt662 < fullAt662 && fullAt662 < DoseRateGeometry.ProjectedAreaBoundCm2(g),
                   "площади в см²: 5 < A_пик < A_полн < πr² (доли тут были бы < 1)");

                if (sabotage == "mixnorm")
                {
                    Console.WriteLine("  ⚠ ПОРЧА: у матрицы поля признак подменён на PerEmittedQuantum (клеймо прежнее)");
                    matrix.Normalization = ResponseMatrixNormalization.PerEmittedQuantum;
                }

                // (A) по матрице поля — число без пометки, G ≡ 1
                DoseRate full;
                DoseRateInput inputFull = null;
                try
                {
                    inputFull = DoseRateInput.Of(isoCurve, matrix);
                    if (sabotage == "isoq")
                    {
                        // Сцена поля через ветку ПРОБЫ: G точки на радиусе
                        // сферы — ровно то, что дал бы `FluencePerPhoton` без
                        // ветки `ISO` (в файле поле записано точкой на R).
                        GeometryModel asPoint = g.Clone();
                        asPoint.Scene = GeometrySceneKind.None;
                        string pointNote;
                        double gPoint = DoseRateGeometry.FluencePerPhoton(asPoint, out pointNote);
                        typeof(DoseRateInput).GetProperty("FluencePerPhoton")
                            .SetValue(inputFull, gPoint, null);
                        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                            "  ⚠ ПОРЧА: G подменён множителем точки: {0:e4} 1/см² ({1})", gPoint, pointNote));
                    }

                    full = manager.Calculate(data, inputFull);
                }
                catch (DoseRateRefusalException ex)
                {
                    full = new DoseRate { Refusal = ex.Message };
                }

                Console.WriteLine("  вход: " + (inputFull == null ? "(отказан)" : inputFull.GeometryNote));
                Console.WriteLine("  (A) с матрицей поля: " + full);
                Ok(inputFull != null && inputFull.Normalization == ResponseMatrixNormalization.PerUnitFluence
                   && inputFull.FluencePerPhoton == 1.0,
                   "вход сцены поля: Normalization = PerUnitFluence, G = 1 ровно"
                   + (inputFull == null ? "" : string.Format(CultureInfo.InvariantCulture, " (G = {0})", inputFull.FluencePerPhoton)));
                Ok(full.Refusal.Length == 0 && full.Rate > 0.0 && !full.Approximate,
                   "(A) число по матрице поля, без пометки: " + Short(full.ToString()));
                if (full.Refusal.Length == 0)
                {
                    PrintRanges(full);
                    rates[i] = full.Rate;
                    ownAreas[i] = HandCheck(data, matrix, full);
                }

                // (B) только кривая поля — «≈» по A_пик
                DoseRate peak;
                try
                {
                    peak = manager.Calculate(data, DoseRateInput.Of(isoCurve, null));
                }
                catch (DoseRateRefusalException ex)
                {
                    peak = new DoseRate { Refusal = ex.Message };
                }

                Console.WriteLine("  (B) только кривая поля: " + peak);
                Ok(peak.Refusal.Length == 0 && peak.Rate > 0.0 && peak.Approximate
                   && peak.ToString().StartsWith(DoseRate.ApproximateMark, StringComparison.Ordinal),
                   "(B) число по A_пик со знаком «≈»: " + Short(peak.ToString()));
                if (full.Refusal.Length == 0 && peak.Refusal.Length == 0)
                {
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "  A → B: {0:f4} → {1:f4} мкЗв/ч, по кривой выше на {2:+0.0;-0.0} %",
                        full.Rate, peak.Rate, 100.0 * (peak.Rate - full.Rate) / full.Rate));
                    Ok(peak.Rate > full.Rate, "A_пик < A_полн(свой), значит доза по кривой ВЫШЕ");
                }

                // (4) смешение нормировок — отказ словами, обе стороны
                if (i == 0)
                {
                    var fractions = new EfficiencyConfigData("кривая в долях у геометрии поля")
                    {
                        Origin = EfficiencyOrigin.Simulation,
                        Geometry = g,
                        Curve = baseCurve.Curve.Select(p => p.Clone()).ToList(),
                        ComputeStamp = baseCurve.ComputeStamp,
                    };
                    RefusesAbout("кривая в ДОЛЯХ (клеймо без norm=fluence) у геометрии поля → отказ о нормировке",
                                 () => DoseRateInput.Of(fractions, null));

                    var stamped = new EfficiencyConfigData("кривая с norm=fluence у сцены с источником")
                    {
                        Origin = EfficiencyOrigin.Simulation,
                        Geometry = baseCurve.Geometry,
                        Curve = baseCurve.Curve.Select(p => p.Clone()).ToList(),
                        ComputeStamp = (baseCurve.ComputeStamp ?? "") + "; " + DoseRateInput.FluenceStampMark,
                    };
                    RefusesAbout("кривая с клеймом norm=fluence у сцены с ИСТОЧНИКОМ → отказ о нормировке",
                                 () => DoseRateInput.Of(stamped, null));

                    var tooBig = new EfficiencyConfigData("кривая поля выше потолка πr²")
                    {
                        Origin = EfficiencyOrigin.Simulation,
                        Geometry = g,
                        Curve = isoCurve.Curve.Select(p => p.Clone()).ToList(),
                        ComputeStamp = isoCurve.ComputeStamp,
                    };
                    tooBig.Curve[tooBig.Curve.Count / 2].Efficiency = 2.0 * DoseRateGeometry.ProjectedAreaBoundCm2(g);
                    Refuses("точка кривой поля выше πr² описанной сферы", () => DoseRateInput.Of(tooBig, null));
                }
            }

            // (1) ответ не зависит от радиуса сферы
            if (rates[0] > 0.0 && rates[1] > 0.0)
            {
                double diff = 100.0 * (rates[1] - rates[0]) / rates[0];
                Ok(Math.Abs(diff) < 1.0, string.Format(CultureInfo.InvariantCulture,
                    "(1) доза по матрице поля R = 50 и R = 100 см: {0:f4} и {1:f4} мкЗв/ч, расхождение {2:+0.00;-0.00} % (допуск 1 %;"
                    + " A_own(662) {3:f3} и {4:f3} см²)",
                    rates[0], rates[1], diff, ownAreas[0], ownAreas[1]));
            }
        }

        /// <summary>
        /// Кривая сцены поля: из кэша <paramref name="cachePath"/> (XML
        /// `EfficiencyConfigData`), иначе считается `EfficiencyCalculation.Run`
        /// (200 000 историй на узел, как в форме) и кладётся туда. Геометрия
        /// берётся ЖИВАЯ (<paramref name="g"/>), из кэша — только точки и клеймо;
        /// кэш с чужим клеймом (другая физика) пересчитывается.
        /// </summary>
        static EfficiencyConfigData IsoCurve(GeometryModel g, string cachePath, string name)
        {
            var serializer = new XmlSerializer(typeof(EfficiencyConfigData));
            if (File.Exists(cachePath))
            {
                EfficiencyConfigData cached;
                using (var fs = new FileStream(cachePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    cached = (EfficiencyConfigData)serializer.Deserialize(fs);
                }

                // Кэш годен, когда версия физики та же и геометрия — та же
                // ТЕКСТОМ (тем же `Render`, из которого считается клеймо).
                if (cached.HasCurve && cached.HasGeometry
                    && cached.ComputeStamp.StartsWith(
                        "phys=" + ResponseMatrix.PhysicsVersion.ToString(CultureInfo.InvariantCulture) + ";", StringComparison.Ordinal)
                    && string.Equals(GeometryWriter.Render(cached.Geometry), GeometryWriter.Render(g), StringComparison.Ordinal))
                {
                    Console.WriteLine("  кривая поля из кэша {0}: {1} точек, клеймо {2}", Path.GetFileName(cachePath),
                                      cached.Curve.Count, cached.ComputeStamp);
                    return new EfficiencyConfigData(name)
                    {
                        Origin = EfficiencyOrigin.Simulation,
                        Geometry = g,
                        Curve = cached.Curve,
                        ComputeStamp = cached.ComputeStamp,
                        UseResponseMatrix = true,
                    };
                }

                Console.WriteLine("  кэш кривой поля устарел (клеймо {0}) — пересчёт", cached.ComputeStamp);
            }

            var watch = System.Diagnostics.Stopwatch.StartNew();
            EfficiencyFitResult result = EfficiencyCalculation.Run(g, new EfficiencyCalculationOptions(), null, null);
            if (!string.IsNullOrEmpty(result.Error) || result.Curve.Count < 2)
            {
                Console.WriteLine("  расчёт кривой поля не пошёл: " + result.Error);
                return null;
            }

            var curve = new EfficiencyConfigData(name)
            {
                Origin = EfficiencyOrigin.Simulation,
                Geometry = g,
                Curve = result.Curve,
                ComputeStamp = result.ComputeStamp ?? "",
                UseResponseMatrix = true,
            };
            using (var fs = new FileStream(cachePath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                serializer.Serialize(fs, curve);
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  кривая поля посчитана за {0:f1} с: {1} точек, клеймо {2} → {3}",
                watch.Elapsed.TotalSeconds, curve.Curve.Count, curve.ComputeStamp, cachePath));
            return curve;
        }

        /// <summary>
        /// (2) СВЕРКА РУКАМИ на диапазоне линии 662: отсчёты диапазона суммируются
        /// по каналам спектра напрямую, A_own — из строки матрицы на середине
        /// диапазона, сложенной по бинам внутри него; φ̇ = N/A_own; Ḣ = φ̇ ×
        /// Ḣ*(10)/φ̇. Сравнивается с вкладом того же диапазона у пробы (допуск 1 %:
        /// у пробы из отсчётов ещё вычтен континуум линий выше — у Cs-137 их
        /// почти нет, доля печатается) и с константой 3.751 пЗв·см² на 662 кэВ
        /// (допуск 3 %: диапазон представлен серединой, коэффициент там другой).
        /// Возвращает A_own, см².
        /// </summary>
        static double HandCheck(ResultData data, ResponseMatrix matrix, DoseRate dose)
        {
            DoseRateRange r = dose.Ranges.FirstOrDefault(x => x.LowKev <= CsLineKev && CsLineKev < x.HighKev);
            if (r == null)
            {
                Ok(false, "диапазона с 662 кэВ в разбивке нет");
                return double.NaN;
            }

            EnergySpectrum spectrum = data.EnergySpectrum;
            int[] counts = spectrum.Spectrum;
            bool[] overflow = OverflowChannel.Mask(counts);
            int startch = (int)spectrum.EnergyCalibration.EnergyToChannel(r.LowKev, spectrum.NumberOfChannels);
            int endch = (int)spectrum.EnergyCalibration.EnergyToChannel(r.HighKev, spectrum.NumberOfChannels);
            if (startch < 0) startch = 0;
            if (endch > counts.Length) endch = counts.Length;
            double n = 0.0;
            for (int i = startch; i < endch; i++)
            {
                if (!overflow[i]) n += counts[i];
            }

            double seconds = spectrum.MeasurementTime;
            double step = matrix.BinKev;
            int cells = (int)Math.Ceiling(r.HighKev / step) + 2;
            double[] row = matrix.Evaluate(r.CenterKev, cells);
            double own = 0.0, rowSum = 0.0;
            for (int b = 0; b < row.Length; b++)
            {
                rowSum += row[b];
                double e = (b + 0.5) * step;
                if (e >= r.LowKev && e < r.HighKev) own += row[b];
            }

            double fluenceRate = n / seconds / own;
            double coefficient = DoseRateCoefficients.DoseRatePerFluenceRate(r.CenterKev);
            double handRate = fluenceRate * coefficient;
            double hand662 = fluenceRate * CsLineMicroSvPerHourPerFluenceRate;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  (2) руками, диапазон {0:f1}…{1:f1} кэВ (середина {2:f1}): N = {3:f0} отсч / {4:f0} с = {5:f2} отсч/с;"
                + " A_own = {6:f3} см² (сумма строки {7:f3}); φ̇ = {8:f4} квант/(см²·с)",
                r.LowKev, r.HighKev, r.CenterKev, n, seconds, n / seconds, own, rowSum, fluenceRate));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "      Ḣ = φ̇ × {0:e4} (коэффициент на {1:f1} кэВ) = {2:f4} мкЗв/ч; проба на этом диапазоне {3:f4}"
                + " (у пробы вычтен континуум сверху: {4:f2} % отсчётов); с 3.751 пЗв·см² (662 кэВ): {5:f4}",
                coefficient, r.CenterKev, handRate, r.DoseRate, 100.0 * r.Explained / Math.Max(r.Counts, 1.0), hand662));
            Ok(r.DoseRate > 0.0 && Math.Abs(handRate - r.DoseRate) <= 0.01 * r.DoseRate,
               string.Format(CultureInfo.InvariantCulture,
                   "(2) число пробы на диапазоне 662 равно ручному: {0:f4} против {1:f4} мкЗв/ч ({2:+0.000;-0.000} %, допуск 1 %)",
                   r.DoseRate, handRate, 100.0 * (r.DoseRate - handRate) / handRate));
            Ok(r.DoseRate > 0.0 && Math.Abs(hand662 - r.DoseRate) <= 0.03 * r.DoseRate,
               string.Format(CultureInfo.InvariantCulture,
                   "(2) то же с константой 3.751 пЗв·см² на 662 кэВ: {0:f4} против {1:f4} ({2:+0.00;-0.00} %, допуск 3 %)",
                   r.DoseRate, hand662, 100.0 * (r.DoseRate - hand662) / hand662));
            Ok(Math.Abs(r.FluenceRate - r.Cps / r.OwnEfficiency) <= 1e-9 * Math.Max(r.FluenceRate, 1e-300),
               string.Format(CultureInfo.InvariantCulture,
                   "(3) у пробы φ̇ = cps / A_own без множителя: {0:e6} = {1:e6}", r.FluenceRate, r.Cps / r.OwnEfficiency));
            return own;
        }

        static void RefusesAbout(string what, Action action)
        {
            checks++;
            try
            {
                action();
                failed++;
                Console.WriteLine("  [НЕТ]  " + what + " — прошло МОЛЧА, числом");
            }
            catch (DoseRateRefusalException ex)
            {
                bool about = ex.Message.IndexOf("normalis", StringComparison.OrdinalIgnoreCase) >= 0
                             || ex.Message.IndexOf("нормиров", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!about) failed++;
                Console.WriteLine((about ? "  [ок]   " : "  [НЕТ]  ") + what + " → «" + Short(ex.Message) + "»");
            }
        }

        // ==================================================================
        // Оснастка
        // ==================================================================

        static ResultData LoadSpectrum(string path)
        {
            if (!File.Exists(path))
            {
                Console.WriteLine("  нет " + path);
                return null;
            }

            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var file = (ResultDataFile)new XmlSerializer(typeof(ResultDataFile)).Deserialize(fs);
                return file.ResultDataList.Count > 0 ? file.ResultDataList[0] : null;
            }
        }

        static ResponseMatrix LoadMatrix(string path, GeometryModel geometry)
        {
            MatrixRefusal refusal;
            int fileFormat;
            ResponseMatrix matrix = ResponseMatrix.Load(path, out refusal, out fileFormat);
            if (matrix == null)
            {
                Console.WriteLine("  матрица {0}: {1} (формат {2})", path, refusal, fileFormat);
                return null;
            }

            if (geometry != null && !matrix.IsValidFor(geometry))
            {
                Console.WriteLine("  матрица {0}: клеймо не сходится с геометрией кривой", path);
                return null;
            }

            Console.WriteLine("  матрица {0}: узлов {1}, {2:f1}…{3:f1} кэВ, историй {4}, каналов {5}",
                Path.GetFileName(path), matrix.Energies.Length, matrix.Energies[0],
                matrix.Energies[matrix.Energies.Length - 1], matrix.Histories,
                matrix.HasChannels ? matrix.ChannelRows.Length : 0);
            return matrix;
        }

        static GlobalConfigManager Config()
        {
            var manager = new GlobalConfigManager();
            var info = new GlobalConfigInfo();
            if (info.ColorConfig != null
                && (info.ColorConfig.SpectrumColorList == null || info.ColorConfig.SpectrumColorList.Count == 0))
            {
                info.ColorConfig.InitializeSpectrumColor();
            }

            manager.GlobalConfig = info;
            return manager;
        }
    }
}
