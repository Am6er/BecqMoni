using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace LightScaleProbe
{
    /// <summary>
    /// Поверка непропорциональности светового выхода (F11): фотонная кривая
    /// модели против измеренной.
    ///
    /// Симулятор взвешивает электронные вклады кривой L(E) из nucdb и после
    /// прогона отдаёт <see cref="EfficiencySimulator.LastPhotonLightScale"/> —
    /// средний свет пика полного поглощения на кэВ линии. Это ровно та
    /// величина, которую меряли Ходюк и Доренбос (photon-nPR, 100 % на
    /// 662 кэВ): у CsI:Tl 112 % на 10 кэВ, горб к 10–20 кэВ, провал у
    /// K-краёв иода (33.17 кэВ) и цезия (35.98 кэВ) — arXiv:1204.4350,
    /// таблица I и текст к рис. 15. Электронная кривая в базе НЕ содержит
    /// краёв — провал обязан родиться в переносе, из смены разбиения энергии
    /// между фотоэлектроном и каскадом на крае. Появился — значит, связка
    /// «кривая + перенос» работает; его глубина и положение — сверка модели.
    ///
    ///     lightscaleprobe --geometry=X.in [--energies=10,20,...]
    ///                     [--n=400000] [--bin=1] [--off]
    ///                     [--kdip=0|1|2|3] [--eta=η] [--eq=кэВ] [--curve=файл]
    ///                     [--ecomp=0|1]
    ///
    /// ⛔ УМОЛЧАНИЕ `--kdip=` — СКЛАДА (`ResponseMatrixOptions.KDipLight`, 1 с
    /// 12.09.2026), половины — той же раскладкой `KDipCurveHalf`/`KDipCascadeHalf`,
    /// что у построителя (правило I; остаток П40 закрыт П44 13.09.2026: до того
    /// проба ставила половины сама от своего `kdip = 0`). Прежние числа —
    /// явным `--kdip=0`.
    ///
    /// `--ecomp=1` (`F11` (г), П44): кривая света сцинтиллятора БЕЗ таблицы
    /// NIST (LaBr₃:Ce, CeBr₃) — из тормозной способности ESTAR по составу
    /// (`EstarCalculator.Stopping`); без ключа у них «шкала пропорциональна».
    /// Умолчание — склада (ВЫКЛ).
    ///
    /// --off считает с выключенным ключом: колонка света обязана стать
    /// пустой, а кривая эффективности — не измениться ни на бит.
    ///
    /// (`F11` (а), П17) --kdip= — K-провал: 1 обе половины (кривая в коде с
    /// продолжением ниже 1 кэВ и обрывом короткого трека + раздельный
    /// оже-каскад EADL), 2 только кривая, 3 только каскад; --eta= — η Пейна
    /// вместо табличного; --eq= — E_q обрыва, кэВ (умолчание — калиброванное
    /// в симуляторе, 0 — без обрыва); --curve=файл — выписать саму кривую
    /// электронов прогона (E, L/E) для сверки с таблицей базы.
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            string geometryPath = null;
            int histories = 400000;
            double binKev = 1.0;
            bool off = false;
            var store = new ResponseMatrixOptions();
            int kdip = store.KDipLight;                 // умолчание склада (правило I)
            bool ecomp = store.ElectronAnyMaterial;     // `F11` (г), П44
            double eta = 0.0;
            double trackEnd = double.NaN;
            string curvePath = null;
            var energies = new List<double>
            {
                10, 15, 20, 25, 30, 32, 33, 34, 35, 36, 37, 40, 45, 59.5,
                80, 100, 150, 200, 300, 450, 661.657, 1000, 1332.5,
            };

            foreach (string a in args)
            {
                if (a.StartsWith("--geometry=", StringComparison.Ordinal)) geometryPath = a.Substring(11);
                else if (a.StartsWith("--n=", StringComparison.Ordinal)) histories = int.Parse(a.Substring(4));
                else if (a.StartsWith("--bin=", StringComparison.Ordinal)) binKev = double.Parse(a.Substring(6), CultureInfo.InvariantCulture);
                else if (a == "--off") off = true;
                else if (a.StartsWith("--kdip=", StringComparison.Ordinal)) kdip = int.Parse(a.Substring(7), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--eta=", StringComparison.Ordinal)) eta = double.Parse(a.Substring(6), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--eq=", StringComparison.Ordinal)) trackEnd = double.Parse(a.Substring(5), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--curve=", StringComparison.Ordinal)) curvePath = a.Substring(8);
                else if (a.StartsWith("--ecomp=", StringComparison.Ordinal))
                {
                    string v = a.Substring(8);
                    if (v != "0" && v != "1")
                    {
                        Console.Error.WriteLine("--ecomp= принимает только 0 или 1: " + a);
                        return 2;
                    }

                    ecomp = v == "1";
                }
                else if (a.StartsWith("--energies=", StringComparison.Ordinal))
                {
                    energies.Clear();
                    foreach (string part in a.Substring(11).Split(','))
                    {
                        energies.Add(double.Parse(part.Trim(), CultureInfo.InvariantCulture));
                    }
                }
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: " + a);
                    return 2;
                }
            }

            if (geometryPath == null || !File.Exists(geometryPath))
            {
                Console.Error.WriteLine("нужен --geometry=<файл .in>");
                return 2;
            }

            GeometryModel geometry = GeometryModel.Load(geometryPath);
            Action<EfficiencySimulator> setup = sim =>
            {
                sim.LightNonproportionality = !off;
                sim.LightSubKevCurve = ResponseMatrixOptions.KDipCurveHalf(kdip);
                sim.LightCascadeSplit = ResponseMatrixOptions.KDipCascadeHalf(kdip);
                sim.LightEtaEh = eta;
                sim.ElectronAnyMaterial = ecomp;        // `F11` (г), П44
                if (!double.IsNaN(trackEnd))
                {
                    sim.LightTrackEndKev = trackEnd;
                }
            };
            var first = new EfficiencySimulator(geometry.Clone());
            setup(first);
            Console.WriteLine("геометрия: {0}", geometry.Describe());
            Console.WriteLine("кривая света: {0}; историй {1}, бин {2:F2} кэВ; kdip={3}{4}; ecomp={5}{6}",
                first.LightYieldName == "" ? "НЕТ (шкала пропорциональна)" : first.LightYieldName,
                histories, binKev, kdip, kdip == store.KDipLight ? " (умолчание склада)" : " (ключом)",
                ecomp ? 1 : 0, ecomp == store.ElectronAnyMaterial ? " (умолчание склада)" : " (ключом)");
            if (curvePath != null && first.LightYieldCurve != null)
            {
                // (`F11` (а), П17) Кривая электронов прогона — точками таблицы
                // базы (1…3000 кэВ, 20 на декаду) плюс подкэвные, чтобы сверить
                // расчёт в коде с импортёром и увидеть обрыв.
                using (var w = new StreamWriter(curvePath, false, new UTF8Encoding(false)))
                {
                    w.WriteLine("# {0}", first.LightYieldName);
                    w.WriteLine("E_kev\tyield_rel");
                    var points = new List<double>();
                    for (int i = 0; i < 41; i++)
                    {
                        points.Add(0.01 * Math.Pow(100.0, i / 40.0));   // 0.01…1 кэВ
                    }

                    for (int i = 1; i < 70; i++)
                    {
                        points.Add(Math.Pow(3000.0, i / 69.0));         // сетка таблицы базы
                    }

                    foreach (double pe in points)
                    {
                        w.WriteLine("{0}\t{1}",
                            pe.ToString("R", CultureInfo.InvariantCulture),
                            first.LightYieldCurve.Of(pe).ToString("R", CultureInfo.InvariantCulture));
                    }
                }

                Console.WriteLine("кривая электронов выписана: {0}", curvePath);
            }

            Console.WriteLine();
            Console.WriteLine("   E, кэВ    свет пика/E   ±статистика, %    (Khodyuk CsI:Tl: 1.12 на 10 кэВ, провал у 33.2/36.0)");

            foreach (double e in energies)
            {
                var sim = new EfficiencySimulator(geometry.Clone())
                {
                    Histories = histories,
                    PeakHalfWidthKev = 0.0,
                };
                setup(sim);
                sim.ResetStream((ulong)sim.Seed ^ (ulong)Math.Round(e * 64.0) * 0x9E3779B97F4A7C15UL);
                double err;
                sim.Response(e, binKev, out err);
                double scale = sim.LastPhotonLightScale;
                Console.WriteLine("  {0,7:F1}    {1}         {2,5:F2}{3}",
                    e,
                    scale > 0.0 ? scale.ToString("F4", CultureInfo.InvariantCulture) : "  —  ",
                    err,
                    sim.CountCascadeOverflow > 0
                        ? "   ⚠ каскад не сошёлся: " + sim.CountCascadeOverflow.ToString(CultureInfo.InvariantCulture)
                        : "");
            }

            return 0;
        }
    }
}
