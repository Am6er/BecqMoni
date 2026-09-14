using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace DoseAirProbeF63
{
    /// <summary>
    /// Строка `A224` — μ_tr/ρ сухого воздуха, считаемая из XCOM, против
    /// опубликованной K_a/Φ (ICRP 119 (2012), приложение I, таблица I.1).
    ///
    /// Проба судит ЖИВОЙ метод сборки приложения
    /// (<c>DoseRateCoefficients.MassEnergyAbsorptionAir</c>) и разводит три
    /// названные в строке причины по ОДНОЙ:
    ///
    ///  1. **Сверка по всем 23 узлам**, а не по трём удобным. Приёмка строки —
    ///     |Δ| ≤ 0.5 % на всех узлах ниже 3 МэВ.
    ///
    ///  2. **Положительный контроль.** Прежняя схема — сечение СВЯЗАННОГО
    ///     электрона из XCOM, умноженное на долю переноса СВОБОДНОГО, — здесь
    ///     переписана заново по тем же данным, той же сетке и той же
    ///     интерполяции. Она обязана ОТКАЗАТЬ ту же проверку; иначе приёмка не
    ///     меряет ничего.
    ///
    ///  3. **Сетка и интерполяция — отдельным опытом.** Строка называет их
    ///     вероятной причиной. Проба показывает числом, что это не так:
    ///     узлы 40/50/60/80/100/150 кэВ — СОБСТВЕННЫЕ узлы сетки XCOM, где
    ///     интерполяции нет вовсе, а на 70 кэВ (единственный узел ICRP не на
    ///     сетке) фотоэффект воздуха — степенной закон с показателем 3.246…3.276
    ///     на всём участке 30…150 кэВ, и разница «хорда против кубики в лог-лог»
    ///     меньше 0.02 %.
    ///
    ///  4. **Квадратура Клейна — Нишины** сверяется с замкнутой формулой и —
    ///     отдельно, чтобы одна и та же ошибка множителя не спряталась в обеих —
    ///     с томсоновским пределом (8/3)π r_e² на 0.01 кэВ.
    ///
    ///  5. **Цена для показания.** Общий множитель в расчёте дозы сокращается
    ///     нормировкой на эталон, поэтому мерится не сам множитель, а его ФОРМА:
    ///     отношение «новое/прежнее», приведённое к 1 на 661.66 кэВ.
    ///
    ///   doseairprobef63 [--csv=&lt;каталог&gt;]
    /// </summary>
    static class Program
    {
        static int failed;
        static int checks;
        static string csvDir;

        // ------------------------------------------------------------------
        // ICRP 119 (2012), приложение I, таблица I.1, колонка K_a/Φ, пГр·см².
        // Числа взяты дословно из `DoseCoefProbeO2` (та же редакция).
        // ------------------------------------------------------------------
        static readonly double[] KaPhiEnergyKev =
            { 10, 15, 20, 30, 40, 50, 60, 70, 80, 100, 150, 200, 300, 400, 500,
              600, 800, 1000, 2000, 4000, 6000, 8000, 10000 };

        static readonly double[] KaPhiPGyCm2 =
            { 7.60, 3.21, 1.73, 0.739, 0.438, 0.328, 0.292, 0.297, 0.308, 0.372,
              0.600, 0.856, 1.38, 1.89, 2.38, 2.84, 3.69, 4.47, 7.51, 12.0,
              15.8, 19.5, 23.2 };

        /// <summary>пГр·см² на (кэВ · см²/г): 1.602176634e-16 Дж/кэВ × 1e15.</summary>
        const double KaPhiUnit = 0.1602176634;

        /// <summary>Приёмка строки `A224`.</summary>
        const double Tolerance = 0.5;

        /// <summary>Состав сухого воздуха — тот же, что в `DoseRateCoefficients`.</summary>
        static readonly int[] AirZ = { 6, 7, 8, 18 };
        static readonly double[] AirWeight = { 0.000124, 0.755267, 0.231781, 0.012827 };
        static readonly double[] AirKEdgeKev = { 0.284, 0.400, 0.532, 3.203 };

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            // ⛔ (`T247`) Культура ЦЕЛИКОМ инвариантная, приказ Amber 05.09.2026. Проба
            //    не ставила её ВОВСЕ, и на русской машине часть её чисел шла с ЗАПЯТОЙ
            //    (замер 10.09.2026, полоса П8: мест без поставщика культуры — 3).
            //    Инвариант ЦЕЛИКОМ, а не клон с подменённым разделителем: клон
            //    чинит печать и оставляет РАЗБОР системным (`T245`).
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            foreach (string a in args)
            {
                if (a.StartsWith("--csv=", StringComparison.Ordinal))
                {
                    csvDir = a.Substring(6);
                }
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: " + a);
                    return 2;
                }
            }

            try
            {
                Quadrature();
                double worstNow = Nodes();
                double worstWas = PositiveControl();
                GridExperiment();
                Price();

                Console.WriteLine();
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "== итог: было худшее {0:+0.00;-0.00} %, стало {1:+0.00;-0.00} % (ниже 3 МэВ) ==",
                    worstWas, worstNow));
            }
            catch (Exception ex)
            {
                Console.WriteLine("!! проба сорвалась: " + ex);
                return 3;
            }

            Console.WriteLine();
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "проверок {0}, провалов {1}", checks, failed));
            return failed == 0 ? 0 : 1;
        }

        // ==================================================================
        // 1. Квадратура Клейна — Нишины
        // ==================================================================

        static void Quadrature()
        {
            Console.WriteLine("== квадратура Клейна — Нишины против замкнутой формулы ==");
            Console.WriteLine("   E, кэВ    σ квадратурой    σ формулой   расх., %   σ_tr/σ");

            double worst = 0.0;
            foreach (double e in new double[] { 10, 50, 100, 511, 1000, 2614, 10000 })
            {
                double mine = DoseRateCoefficients.ComptonCrossSection(e);
                double closed = KleinNishinaClosed(e);
                double d = 100.0 * (mine - closed) / closed;
                if (Math.Abs(d) > Math.Abs(worst)) worst = d;
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,7:f0}   {1,14:g6}   {2,11:g6}   {3,8:+0.000;-0.000}   {4:f5}",
                    e, mine, closed, d, DoseRateCoefficients.ComptonTransferFraction(e)));
            }

            Ok(Math.Abs(worst) < 0.01, string.Format(CultureInfo.InvariantCulture,
                "квадратура сходится с замкнутой формулой (худшее {0:+0.000;-0.000} %)", worst));

            // ⛔ Опора КВАДРАТУРЫ, а не формулы: у обеих одна и та же ошибка
            // множителя была бы невидима, если сверять их только друг с другом.
            // Предел Томсона σ_T = (8/3)π r_e² = 0.665246 барн — число, которое
            // не зависит ни от квадратуры, ни от замкнутой формулы КН.
            double thomson = 8.0 / 3.0 * Math.PI * DoseRateCoefficients.ElectronRadiusSquaredCm2 * 1e24;
            double low = DoseRateCoefficients.ComptonCrossSection(0.01) * 1e24;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  σ(0.01 кэВ) = {0:f6} барн/электрон, предел Томсона {1:f6} ({2:+0.000;-0.000} %)",
                low, thomson, 100.0 * (low - thomson) / thomson));
            Ok(Math.Abs(100.0 * (low - thomson) / thomson) < 0.02,
               "квадратура выходит на томсоновский предел — общий множитель r_e² на месте");

            // ⛔ Положительный контроль самой сверки: заведомо неверный множитель
            // обязан её ПРОБИТЬ.
            double bad = 100.0 * (DoseRateCoefficients.ComptonCrossSection(511.0) * 2.0
                                  - KleinNishinaClosed(511.0)) / KleinNishinaClosed(511.0);
            Ok(Math.Abs(bad) >= 0.01, string.Format(CultureInfo.InvariantCulture,
                "удвоенное сечение сверку ПРОБИВАЕТ ({0:+0.0;-0.0} %) — она смотрит", bad));
        }

        /// <summary>Полное сечение Клейна — Нишины замкнутой формулой, см²/электрон.</summary>
        static double KleinNishinaClosed(double energyKev)
        {
            double a = energyKev / DoseRateCoefficients.ElectronMassKev;
            double re2 = DoseRateCoefficients.ElectronRadiusSquaredCm2;
            return 2.0 * Math.PI * re2 * (
                (1.0 + a) / (a * a) * (2.0 * (1.0 + a) / (1.0 + 2.0 * a) - Math.Log(1.0 + 2.0 * a) / a)
                + Math.Log(1.0 + 2.0 * a) / (2.0 * a)
                - (1.0 + 3.0 * a) / ((1.0 + 2.0 * a) * (1.0 + 2.0 * a)));
        }

        // ==================================================================
        // 2. Все узлы ICRP 119
        // ==================================================================

        static double Nodes()
        {
            Console.WriteLine();
            Console.WriteLine("== K_a/Φ воздуха: приложение против ICRP 119, табл. I.1, ВСЕ 23 узла ==");
            Console.WriteLine("   E, кэВ   ICRP 119   приложение   расх., %   прежняя схема, %");

            var csv = new StringBuilder();
            csv.AppendLine("E_keV;icrp119_Ka_over_phi_pGycm2;app_now_pGycm2;diff_now_percent;"
                           + "app_was_pGycm2;diff_was_percent;app_mu_tr_rho_m2kg");
            double worst = 0.0, at = 0.0;
            int over = 0;
            var overNames = new List<string>();
            for (int i = 0; i < KaPhiEnergyKev.Length; i++)
            {
                double e = KaPhiEnergyKev[i];
                double mu = DoseRateCoefficients.MassEnergyAbsorptionAir(e);      // м²/кг
                double now = e * (mu * 10.0) * KaPhiUnit;
                double was = e * (OldScheme(e) * 10.0) * KaPhiUnit;
                double d = 100.0 * (now - KaPhiPGyCm2[i]) / KaPhiPGyCm2[i];
                double dWas = 100.0 * (was - KaPhiPGyCm2[i]) / KaPhiPGyCm2[i];
                if (e < 3000.0)
                {
                    if (Math.Abs(d) > Math.Abs(worst)) { worst = d; at = e; }
                    if (Math.Abs(d) > Tolerance)
                    {
                        over++;
                        overNames.Add(string.Format(CultureInfo.InvariantCulture,
                            "{0:f0} кэВ ({1:+0.00;-0.00} %)", e, d));
                    }
                }

                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,7:f0}   {1,8:g4}   {2,10:g4}   {3,8:+0.00;-0.00}   {4,16:+0.00;-0.00}",
                    e, KaPhiPGyCm2[i], now, d, dWas));
                csv.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0:g6};{1:g6};{2:g6};{3:g4};{4:g6};{5:g4};{6:g6}",
                    e, KaPhiPGyCm2[i], now, d, was, dWas, mu));
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  худший узел ниже 3 МэВ: {0:f0} кэВ, {1:+0.00;-0.00} %; вне допуска {2:f1} % — {3} из 19",
                at, worst, Tolerance, over));

            Ok(over == 0, string.Format(CultureInfo.InvariantCulture,
                "приёмка `A224`: |Δ| ≤ {0:f1} % на всех узлах ниже 3 МэВ{1}",
                Tolerance, over == 0 ? "" : " — вне допуска: " + string.Join(", ", overNames.ToArray())));

            // Тот же счёт без 70 кэВ: узел держится ЧИСЛОМ ИСТОЧНИКА, а не кодом
            // (см. раздел 4 — там опыт, разводящий сетку и интерполяцию).
            int overNo70 = 0;
            double worstNo70 = 0.0, atNo70 = 0.0;
            for (int i = 0; i < KaPhiEnergyKev.Length; i++)
            {
                double e = KaPhiEnergyKev[i];
                if (e >= 3000.0 || e == 70.0) continue;
                double d = 100.0 * (e * DoseRateCoefficients.MassEnergyAbsorptionAir(e) * 10.0 * KaPhiUnit
                                    - KaPhiPGyCm2[i]) / KaPhiPGyCm2[i];
                if (Math.Abs(d) > Math.Abs(worstNo70)) { worstNo70 = d; atNo70 = e; }
                if (Math.Abs(d) > Tolerance) overNo70++;
            }

            Ok(overNo70 == 0, string.Format(CultureInfo.InvariantCulture,
                "то же без узла 70 кэВ: {0} вне допуска, худший {1:f0} кэВ {2:+0.00;-0.00} %",
                overNo70, atNo70, worstNo70));

            Write("f63-a224-air-kaphi.csv", csv.ToString());
            return worst;
        }

        // ==================================================================
        // 3. Положительный контроль: прежняя схема на тех же данных
        // ==================================================================

        /// <summary>
        /// μ_tr/ρ воздуха ПРЕЖНЕЙ схемой (до 06.09.2026): сечение связанного
        /// электрона из XCOM × доля переноса свободного. Переписано здесь по
        /// тем же данным, той же сетке и той же интерполяции, что у живого
        /// метода: меняется РОВНО ОДНА величина — комптоновский член.
        /// </summary>
        static double OldScheme(double energyKev)
        {
            double fCompton = DoseRateCoefficients.ComptonTransferFraction(energyKev);
            double fPair = energyKev > 2.0 * DoseRateCoefficients.ElectronMassKev
                ? (energyKev - 2.0 * DoseRateCoefficients.ElectronMassKev) / energyKev
                : 0.0;

            double sum = 0.0;
            for (int i = 0; i < AirZ.Length; i++)
            {
                MaterialDatabase.Element element;
                if (!MaterialDatabase.TryGet(AirZ[i], out element))
                {
                    throw new Exception("нет элемента Z=" + AirZ[i]);
                }

                double incoherent = MaterialDatabase.Interpolate(element.EnergyKev, element.Channels[1], energyKev);
                double photo = MaterialDatabase.Interpolate(element.EnergyKev, element.Channels[2], energyKev);
                double pairNuclear = MaterialDatabase.Interpolate(element.EnergyKev, element.Channels[3], energyKev);
                double pairElectron = MaterialDatabase.Interpolate(element.EnergyKev, element.Channels[4], energyKev);

                double fPhoto = 1.0;
                MaterialDatabase.Fluorescence fluorescence = MaterialDatabase.FluorescenceOf(AirZ[i]);
                if (fluorescence != null && energyKev > AirKEdgeKev[i])
                {
                    fPhoto = 1.0 - fluorescence.OmegaK * AirKEdgeKev[i] / energyKev;
                }

                sum += AirWeight[i] * (photo * fPhoto
                                       + incoherent * fCompton
                                       + (pairNuclear + pairElectron) * fPair);
            }

            return sum / 10.0;
        }

        static double PositiveControl()
        {
            Console.WriteLine();
            Console.WriteLine("== положительный контроль: прежняя схема обязана ОТКАЗАТЬ ==");

            double worst = 0.0, at = 0.0;
            int over = 0;
            for (int i = 0; i < KaPhiEnergyKev.Length; i++)
            {
                double e = KaPhiEnergyKev[i];
                if (e >= 3000.0) continue;
                double was = e * (OldScheme(e) * 10.0) * KaPhiUnit;
                double d = 100.0 * (was - KaPhiPGyCm2[i]) / KaPhiPGyCm2[i];
                if (Math.Abs(d) > Math.Abs(worst)) { worst = d; at = e; }
                if (Math.Abs(d) > Tolerance) over++;
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  прежняя схема: вне допуска {0} узлов из 19, худший {1:f0} кэВ {2:+0.00;-0.00} %",
                over, at, worst));

            Ok(over > 0 && Math.Abs(worst) > Tolerance, string.Format(CultureInfo.InvariantCulture,
                "прежняя схема приёмку ПРОБИВАЕТ ({0} узлов вне {1:f1} %) — приёмка смотрит",
                over, Tolerance));

            // ⛔ Второй положительный контроль — заведомо неверная единица.
            double worstBad = 0.0;
            for (int i = 0; i < KaPhiEnergyKev.Length; i++)
            {
                double e = KaPhiEnergyKev[i];
                double bad = e * DoseRateCoefficients.MassEnergyAbsorptionAir(e) * KaPhiUnit;  // без ×10
                double d = 100.0 * (bad - KaPhiPGyCm2[i]) / KaPhiPGyCm2[i];
                if (Math.Abs(d) > Math.Abs(worstBad)) worstBad = d;
            }

            Ok(Math.Abs(worstBad) > Tolerance, string.Format(CultureInfo.InvariantCulture,
                "подставленная неверная единица (см²/г вместо м²/кг) приёмку ПРОБИВАЕТ: {0:+0.0;-0.0} %",
                worstBad));

            return worst;
        }

        // ==================================================================
        // 4. Сетка и интерполяция — по одной причине за раз
        // ==================================================================

        static void GridExperiment()
        {
            Console.WriteLine();
            Console.WriteLine("== сетка и интерполяция: опыт по одной причине ==");

            // 4.1 Какие узлы ICRP лежат НА сетке XCOM. Там интерполяции нет
            //     вовсе, и списать на неё расхождение нельзя.
            MaterialDatabase.Element nitrogen;
            if (!MaterialDatabase.TryGet(7, out nitrogen))
            {
                throw new Exception("нет азота в matdb");
            }

            var onGrid = new List<string>();
            var offGrid = new List<string>();
            for (int i = 0; i < KaPhiEnergyKev.Length; i++)
            {
                double e = KaPhiEnergyKev[i];
                if (e >= 3000.0) continue;
                bool hit = false;
                for (int j = 0; j < nitrogen.EnergyKev.Length; j++)
                {
                    if (Math.Abs(nitrogen.EnergyKev[j] - e) < 1e-9) { hit = true; break; }
                }

                (hit ? onGrid : offGrid).Add(e.ToString("f0", CultureInfo.InvariantCulture));
            }

            Console.WriteLine("  на сетке XCOM: " + string.Join(", ", onGrid.ToArray()));
            Console.WriteLine("  вне сетки:     " + string.Join(", ", offGrid.ToArray()));
            Ok(offGrid.Count == 1 && offGrid[0] == "70",
               "единственный узел ICRP вне сетки XCOM ниже 3 МэВ — 70 кэВ");

            // 4.2 Показатель степенного закона фотоэффекта воздуха по СОСЕДНИМ
            //     узлам сетки. Постоянный показатель = лог-лог хорда точна.
            double[] grid = { 30, 40, 50, 60, 80, 100, 150 };
            var tau = new double[grid.Length];
            for (int i = 0; i < grid.Length; i++)
            {
                tau[i] = PhotoAir(grid[i]);
            }

            double minExp = double.MaxValue, maxExp = double.MinValue;
            var sb = new StringBuilder();
            for (int i = 0; i + 1 < grid.Length; i++)
            {
                double n = -Math.Log(tau[i + 1] / tau[i]) / Math.Log(grid[i + 1] / grid[i]);
                minExp = Math.Min(minExp, n);
                maxExp = Math.Max(maxExp, n);
                sb.Append(string.Format(CultureInfo.InvariantCulture, " {0:f0}-{1:f0}:{2:f4}",
                    grid[i], grid[i + 1], n));
            }

            Console.WriteLine("  показатель τ_воздуха:" + sb.ToString());
            Ok(maxExp - minExp < 0.05, string.Format(CultureInfo.InvariantCulture,
                "показатель постоянен в пределах {0:f4} на 30…150 кэВ — τ степенной, хорда лог-лог точна",
                maxExp - minExp));

            // 4.3 Хорда против кубики в лог-лог на 70 кэВ — та же сетка,
            //     МЕНЯЕТСЯ ТОЛЬКО СХЕМА.
            double chord = PhotoAir(70.0);
            double cubic = PhotoAirCubic(70.0);
            double schemeCost = 100.0 * (cubic - chord) / chord;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  τ(70 кэВ): хорда {0:g7}, кубика по 50/60/80/100 {1:g7}, разница {2:+0.000;-0.000} %",
                chord, cubic, schemeCost));

            // 4.4 Сколько НУЖНО прибавить τ, чтобы закрыть остаток на 70 кэВ.
            int at70 = Array.IndexOf(KaPhiEnergyKev, 70.0);
            double mu70 = DoseRateCoefficients.MassEnergyAbsorptionAir(70.0) * 10.0;
            double need = (KaPhiPGyCm2[at70] / (70.0 * KaPhiUnit)) - mu70;   // см²/г
            double needPercent = 100.0 * need / chord;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  чтобы дойти до ICRP на 70 кэВ, τ должен быть выше на {0:+0.00;-0.00} %"
                + " — это в {1:f0} раз больше цены схемы", needPercent, Math.Abs(needPercent / schemeCost)));

            Ok(Math.Abs(schemeCost) < 0.1 && Math.Abs(needPercent) > 1.0,
               "остаток на 70 кэВ НЕ объясняется ни сеткой, ни схемой интерполяции");
        }

        static double PhotoAir(double energyKev)
        {
            double sum = 0.0;
            for (int i = 0; i < AirZ.Length; i++)
            {
                MaterialDatabase.Element element;
                if (!MaterialDatabase.TryGet(AirZ[i], out element)) throw new Exception("нет Z=" + AirZ[i]);
                sum += AirWeight[i] * MaterialDatabase.Interpolate(element.EnergyKev, element.Channels[2], energyKev);
            }

            return sum;
        }

        /// <summary>Лагранж по четырём соседним узлам сетки в лог-лог — та же сетка, иная схема.</summary>
        static double PhotoAirCubic(double energyKev)
        {
            double[] nodes = { 50, 60, 80, 100 };
            var lx = new double[nodes.Length];
            var ly = new double[nodes.Length];
            for (int i = 0; i < nodes.Length; i++)
            {
                lx[i] = Math.Log(nodes[i]);
                ly[i] = Math.Log(PhotoAir(nodes[i]));
            }

            double x = Math.Log(energyKev);
            double s = 0.0;
            for (int a = 0; a < nodes.Length; a++)
            {
                double p = ly[a];
                for (int b = 0; b < nodes.Length; b++)
                {
                    if (a != b) p *= (x - lx[b]) / (lx[a] - lx[b]);
                }

                s += p;
            }

            return Math.Exp(s);
        }

        // ==================================================================
        // 5. Цена для показания: ФОРМА множителя
        // ==================================================================

        static void Price()
        {
            Console.WriteLine();
            Console.WriteLine("== цена для показания: форма множителя, приведённая к 1 на 661.66 кэВ ==");

            double refNow = DoseRateCoefficients.Factor(661.657);
            double refWas = 661.657 * OldScheme(661.657) * DoseRateCoefficients.AmbientDoseConversion(661.657);

            var csv = new StringBuilder();
            csv.AppendLine("E_keV;shape_was;shape_now;ratio_now_over_was");
            double lo = double.MaxValue, hi = double.MinValue, atLo = 0.0, atHi = 0.0;
            for (double e = 10.0; e <= 3000.001; e *= 1.15)
            {
                double now = DoseRateCoefficients.Factor(e) / refNow;
                double was = e * OldScheme(e) * DoseRateCoefficients.AmbientDoseConversion(e) / refWas;
                double r = now / was;
                if (r < lo) { lo = r; atLo = e; }
                if (r > hi) { hi = r; atHi = e; }
                csv.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0:g6};{1:g6};{2:g6};{3:g6}", e, was, now, r));
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  форма изменилась от {0:+0.00;-0.00} % ({1:f0} кэВ) до {2:+0.00;-0.00} % ({3:f0} кэВ)"
                + " — размах {4:f2} п.п.",
                100.0 * (lo - 1.0), atLo, 100.0 * (hi - 1.0), atHi, 100.0 * (hi - lo)));
            Console.WriteLine("  ⚠ показание меняется РАЗМАХОМ, а не уровнем: общий множитель"
                              + " сокращается нормировкой на эталон.");

            // На именованных линиях — то, что увидит человек, если эталон
            // Cs-137 662 кэВ, а мерится однолинейный источник.
            double[] lines = { 59.54, 122.06, 356.01, 661.657, 1173.2, 1332.5, 1460.8, 2614.5 };
            string[] names = { "Am-241", "Co-57", "Ba-133", "Cs-137 (эталон)", "Co-60", "Co-60", "K-40", "Th-228" };
            for (int i = 0; i < lines.Length; i++)
            {
                double now = DoseRateCoefficients.Factor(lines[i]) / refNow;
                double was = lines[i] * OldScheme(lines[i])
                             * DoseRateCoefficients.AmbientDoseConversion(lines[i]) / refWas;
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "    {0,-16} {1,8:f2} кэВ: {2:+0.000;-0.000} %", names[i], lines[i],
                    100.0 * (now / was - 1.0)));
            }

            Write("f63-a224-factor-shape.csv", csv.ToString());
        }

        // ==================================================================
        // Оснастка
        // ==================================================================

        static void Ok(bool condition, string what)
        {
            checks++;
            if (!condition) failed++;
            Console.WriteLine("  {0} {1}", condition ? "ok  " : "ПРОВАЛ", what);
        }

        static void Write(string name, string text)
        {
            if (string.IsNullOrEmpty(csvDir)) return;
            Directory.CreateDirectory(csvDir);
            string path = Path.Combine(csvDir, name);
            File.WriteAllText(path, text, new UTF8Encoding(false));
            Console.WriteLine("  записано: " + path);
        }
    }
}
