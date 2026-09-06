using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.Properties;
using MathNet.Numerics;
using MathNet.Numerics.Interpolation;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BecquerelMonitor
{
    // Token: 0x02000031 RID: 49
    public class DoseRate
    {
        // Token: 0x17000121 RID: 289
        // (get) Token: 0x060002AB RID: 683 RVA: 0x0000D1E4 File Offset: 0x0000B3E4
        // (set) Token: 0x060002AC RID: 684 RVA: 0x0000D1EC File Offset: 0x0000B3EC
        public double Rate
        {
            get
            {
                return this.rate;
            }
            set
            {
                this.rate = value;
            }
        }

        // Token: 0x17000122 RID: 290
        // (get) Token: 0x060002AD RID: 685 RVA: 0x0000D1F8 File Offset: 0x0000B3F8
        // (set) Token: 0x060002AE RID: 686 RVA: 0x0000D200 File Offset: 0x0000B400
        public double Error
        {
            get
            {
                return this.error;
            }
            set
            {
                this.error = value;
            }
        }

        public double Epsilon
        {
            get
            {
                if (this.rate == 0.0) return 0.0;
                return 100.0 * this.error / this.rate;
            }
        }

        /// <summary>
        /// Почему числа НЕТ. Пустая строка — число есть.
        ///
        /// ⚠ `C4(в)`. Прежде расчёт при негодном входе (спектр без калибровки,
        /// пустая шкала, нулевое время) возвращал ноль, и ноль уходил в строку
        /// состояния наравне с измеренным нулём: «0.000 ±0.000 (0.0%) мкЗв/ч».
        /// Отличить «фона нет» от «считать не по чему» было нельзя. Теперь
        /// причина едет вместе со значением и вытесняет его в `ToString`.
        /// </summary>
        public string Refusal
        {
            get { return this.refusal ?? ""; }
            set { this.refusal = value; }
        }

        /// <summary>
        /// Доля ОТСЧЁТОВ спектра, попавшая в откалиброванные диапазоны, 0..1;
        /// отрицательное — не считалась.
        ///
        /// ⚠ `C4(в)`. Точки калибровки покрывают не всю шкалу прибора: у
        /// поставочных конфигураций они кончаются на 40 и 3000 кэВ, и всё, что
        /// вне, в дозу не входило МОЛЧА. У америциевого спектра ASN16 это
        /// 12.9 % отсчётов снизу. Само по себе неполное покрытие — не ошибка
        /// (ниже 10 кэВ вклада в H*(10) и правда почти нет), но человек обязан
        /// видеть, что показание неполное.
        /// </summary>
        public double Coverage
        {
            get { return this.coverage; }
            set { this.coverage = value; }
        }

        public override string ToString()
        {
            if (!string.IsNullOrEmpty(this.refusal))
            {
                return this.refusal;
            }

            // ⚠ `A12`. Единица идёт в СТРОКУ СОСТОЯНИЯ главного окна
            // рядом с переведённой подписью `Resources.DoseRate`
            // («Мощность дозы:»), поэтому её место — тоже в ресурсах.
            string uom = Resources.UnitMicroSievertPerHour;
            double rate = this.Rate;
            double error = this.Error;
            double epsilon = this.Epsilon;
            if (rate > 1000000.0)
            {
                uom = Resources.UnitSievertPerHour;
                rate /= 1000000.0;
                error /= 1000000.0;
            }
            else if (rate > 1000.0)
            {
                uom = Resources.UnitMilliSievertPerHour;
                rate /= 1000.0;
                error /= 1000.0;
            }
            string rate_str;
            if (rate < 10.0)
            {
                rate_str = rate.ToString("f3", CultureInfo.InvariantCulture);
            }
            else if (rate < 100.0)
            {
                rate_str = rate.ToString("f2", CultureInfo.InvariantCulture);
            }
            else
            {
                rate_str = rate.ToString("f1", CultureInfo.InvariantCulture);
            }
            string error_str;
            string epsilon_str = epsilon.ToString("f1", CultureInfo.InvariantCulture);
            if (error < 10.0)
            {
                error_str = error.ToString("f3", CultureInfo.InvariantCulture);
            }
            else if (error < 100.0)
            {
                error_str = error.ToString("f2", CultureInfo.InvariantCulture);
            }
            else
            {
                error_str = error.ToString("f1", CultureInfo.InvariantCulture);
            }

            string text = String.Format(CultureInfo.InvariantCulture, "{0} ±{1} ({2}%) {3}", rate_str, error_str, epsilon_str, uom);

            // Приписка только когда есть о чём: полное покрытие молчит.
            if (this.coverage >= 0.0 && this.coverage < CoverageNoticeThreshold)
            {
                text += " " + string.Format(CultureInfo.InvariantCulture,
                                            DoseRateCoefficients.Text("DoseRatePartialCoverage",
                                                                      "(covers {0:f0} % of counts)"),
                                            100.0 * this.coverage);
            }

            return text;
        }

        /// <summary>Ниже этой доли покрытия показание получает приписку.</summary>
        public const double CoverageNoticeThreshold = 0.95;

        // Token: 0x040000F4 RID: 244
        double rate = 0.0;

        // Token: 0x040000F5 RID: 245
        double error = 0.0;

        string refusal;

        double coverage = -1.0;
    }

    /// <summary>
    /// Отказ расчёта дозы, названный словами. Бросается там, где раньше
    /// возвращался ноль или NaN (`C4(в)`).
    /// </summary>
    public class DoseRateRefusalException : Exception
    {
        public DoseRateRefusalException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// Коэффициенты перехода «отсчёт в диапазоне → мощность амбиентной дозы».
    ///
    /// ⛔ `C4(в)`. Раньше обе таблицы лежали безымянными массивами прямо в
    /// `DeviceConfigForm.CalculateDoseRateConfig`: шестнадцать значений μ_en/ρ и
    /// шестнадцать значений перевода, без источника, без единиц и без
    /// возможности посмотреть. Разобрано 05.09.2026:
    ///
    /// * первая таблица — массовый коэффициент ПОГЛОЩЕНИЯ энергии сухого
    ///   воздуха в **м²/кг** (NIST, Hubbell &amp; Seltzer; в привычных см²/г это
    ///   те же числа, умноженные на 10). Здесь она больше не лежит числами:
    ///   считается из сечений XCOM в `matdb.sqlite` — см.
    ///   <see cref="MassEnergyAbsorptionAir"/>;
    /// * вторая — отношение амбиентного эквивалента дозы к керме воздуха
    ///   h*(10)/K_air (ICRP 74, таблица A.21), умноженное на 0.876, то есть
    ///   выраженное в бэр/Р. Отсюда и имя `RToSv` в старом коде. Эта величина
    ///   из XCOM не выводится вовсе — она получена переносом в антропоморфном
    ///   фантоме, — поэтому таблица остаётся таблицей, но названной.
    ///
    /// ⚠ Обе величины входят в расчёт ТОЛЬКО формой кривой: чувствительность
    /// диапазона нормируется на объявленную мощность дозы эталона, и общий
    /// множитель сокращается. Поэтому h*(10)/K_air хранится здесь в
    /// опубликованном виде (Зв/Гр), без множителя 0.876.
    /// </summary>
    public static class DoseRateCoefficients
    {
        // ------------------------------------------------------------------
        // Границы применимости
        // ------------------------------------------------------------------

        /// <summary>
        /// Ниже этой энергии коэффициента перехода к H*(10) нет: ICRP 74
        /// начинает таблицу с 10 кэВ, и не по бедности — квант 5 кэВ до
        /// глубины 10 мм не доходит, вклад в амбиентный эквивалент физически
        /// нулевой.
        /// </summary>
        public const double MinEnergyKev = 10.0;

        /// <summary>Верх таблицы ICRP 74.</summary>
        public const double MaxEnergyKev = 10000.0;

        // ------------------------------------------------------------------
        // Воздух
        // ------------------------------------------------------------------

        /// <summary>Z элементов сухого воздуха.</summary>
        static readonly int[] AirZ = { 6, 7, 8, 18 };

        /// <summary>
        /// Весовые доли сухого воздуха у уровня моря (NIST, состав
        /// Hubbell &amp; Seltzer — тот же, по которому посчитаны опубликованные
        /// μ_en/ρ воздуха).
        /// </summary>
        static readonly double[] AirWeight = { 0.000124, 0.755267, 0.231781, 0.012827 };

        /// <summary>Энергия K-края и выход K-флуоресценции, кэВ.</summary>
        static readonly double[] AirKEdgeKev = { 0.284, 0.400, 0.532, 3.203 };

        /// <summary>Масса покоя электрона, кэВ.</summary>
        public const double ElectronMassKev = 510.99895;

        // ==================================================================
        // ⛔ ТАБЛИЦА ПЕРЕХОДА — РЕДАКЦИЯ НАЗВАНА ЗДЕСЬ (`A198`, решение Amber
        //    05.09.2026: принять ICRP 74).
        //
        //  * издание   — ICRP Publication 74 (1996), «Conversion Coefficients
        //                for use in Radiological Protection against External
        //                Radiation», Ann. ICRP 26(3/4); приложение A. Те же
        //                числа изданы совместно как ICRU Report 57 (1998).
        //  * таблица   — A.21, монохроматические фотоны, 25 узлов
        //                10 кэВ … 10 МэВ.
        //  * величина  — h*(10)/K_air: амбиентный эквивалент дозы на глубине
        //                10 мм в шаре ICRU, отнесённый к КЕРМЕ ВОЗДУХА в той
        //                же точке при отсутствии шара.
        //  * единицы   — Зв/Гр. Хранится в ОПУБЛИКОВАННОМ виде, без пересчёта.
        //  * множитель 0.876 — см. `RemPerRoentgenFactor` ниже: он НЕ входит в
        //                эту таблицу и в расчёт не входит вовсе.
        //
        // ⚠ ИЗВЕСТНОЕ РАСХОЖДЕНИЕ С ПРЕЖНИМИ ВЕРСИЯМИ. До 05.09.2026 здесь
        // лежала безымянная таблица `RToSv` из шестнадцати чисел (40…3000 кэВ),
        // выраженная в бэр/Р. Ниже 500 кэВ она сходится с ICRP 74 не хуже
        // 0.7 %, ВЫШЕ 600 кэВ систематически НИЖЕ опубликованных значений на
        // 1–2.2 % (худшее 800 кэВ, +2.20 %). Источник того сдвига в прежнем
        // коде назван не был и не разыскивался (решение Amber): взяты
        // опубликованные числа. Цена перехода для показаний измерена
        // 05.09.2026 развёрткой по одной величине и записана в
        // `handover/handover-2026-09-05-o2-doserate-coef.md`.
        //
        // ⛔ Порядок значений привязан к порядку энергий поштучно. Сверка всех
        // 25 узлов с опубликованными — `tools/effmaker/probes/DoseCoefProbeO2.cs`
        // (там же положительный контроль: испорченный узел обязан быть назван).
        // ==================================================================

        /// <summary>Энергии таблицы ICRP 74 (1996), таблица A.21, кэВ.</summary>
        static readonly double[] AmbientEnergyKev =
        {
            10, 15, 20, 30, 40, 50, 60, 80, 100, 150, 200, 300, 400, 500, 600,
            800, 1000, 1500, 2000, 3000, 4000, 5000, 6000, 8000, 10000,
        };

        /// <summary>
        /// h*(10)/K_air, Зв/Гр. ICRP Publication 74 (1996), таблица A.21;
        /// те же значения — ICRU Report 57 (1998).
        /// </summary>
        static readonly double[] AmbientConversion =
        {
            0.008, 0.26, 0.61, 1.10, 1.47, 1.67, 1.74, 1.72, 1.65, 1.49, 1.40,
            1.31, 1.26, 1.23, 1.21, 1.19, 1.17, 1.15, 1.14, 1.13, 1.12, 1.11,
            1.11, 1.11, 1.10,
        };

        /// <summary>
        /// Множитель перехода к бэр/Р: 1 Р = 0.00876 Гр в воздухе, бэр = 0.01 Зв.
        /// Нужен только для сверки со старой вшитой таблицей — в расчёте он
        /// сокращается.
        /// </summary>
        public const double RemPerRoentgenFactor = 0.876;

        // ------------------------------------------------------------------
        // Публичный вход
        // ------------------------------------------------------------------

        /// <summary>
        /// Массовый коэффициент поглощения энергии сухим воздухом, **м²/кг**.
        ///
        /// Считается из сечений XCOM: μ_tr/ρ = Σ w_i (τ f_фэ + σ_нк f_К + κ f_пар),
        /// где f — доля энергии кванта, доставшаяся заряженным частицам:
        /// * фотоэффект: 1 − ω_K·E_K/E, то есть вычитается энергия, унесённая
        ///   характеристическим рентгеном;
        /// * некогерентное: средняя доля по Клейну — Нишине (см.
        ///   <see cref="ComptonTransferFraction"/>);
        /// * пары: (E − 2m_e c²)/E, оба канала.
        ///
        /// ⚠ Радиационные потери (тормозное от вторичных электронов,
        /// аннигиляция на лету) НЕ вычитаются: μ_en/ρ = μ_tr/ρ·(1 − g), а g для
        /// воздуха ниже 3 МэВ меньше 0.2 %. Это и есть главный источник
        /// расхождения с опубликованной таблицей на верху шкалы.
        /// </summary>
        public static double MassEnergyAbsorptionAir(double energyKev)
        {
            if (!(energyKev > 0.0))
            {
                throw new DoseRateRefusalException(string.Format(
                    CultureInfo.InvariantCulture,
                    Text("DoseRateEnergyNotPositive", "Dose rate: energy {0} keV is not positive."),
                    energyKev));
            }

            double fCompton = ComptonTransferFraction(energyKev);
            double fPair = energyKev > 2.0 * ElectronMassKev
                ? (energyKev - 2.0 * ElectronMassKev) / energyKev
                : 0.0;

            double sum = 0.0;
            for (int i = 0; i < AirZ.Length; i++)
            {
                MaterialDatabase.Element element;
                if (!MaterialDatabase.TryGet(AirZ[i], out element))
                {
                    throw new DoseRateRefusalException(string.Format(
                        CultureInfo.InvariantCulture,
                        Text("DoseRateNoElement", "Dose rate: element Z={0} is missing from the material database."),
                        AirZ[i]));
                }

                // ⛔ Своя проверка границ, а не молчаливое удержание крайнего
                // значения интерполятором: за краем таблицы XCOM ответа НЕТ, и
                // подставленное туда крайнее значение выглядело бы измерением.
                double lowKev = element.EnergyKev[0];
                double highKev = element.EnergyKev[element.EnergyKev.Length - 1];
                if (energyKev < lowKev || energyKev > highKev)
                {
                    throw new DoseRateRefusalException(string.Format(
                        CultureInfo.InvariantCulture,
                        Text("DoseRateOutsideXcom",
                             "Dose rate: {0} keV is outside the XCOM table for Z={1} ({2}...{3} keV)."),
                        energyKev, AirZ[i], lowKev, highKev));
                }

                double incoherent = MaterialDatabase.Interpolate(element.EnergyKev, element.Channels[1], energyKev);
                double photo = MaterialDatabase.Interpolate(element.EnergyKev, element.Channels[2], energyKev);
                double pairNuclear = MaterialDatabase.Interpolate(element.EnergyKev, element.Channels[3], energyKev);
                double pairElectron = MaterialDatabase.Interpolate(element.EnergyKev, element.Channels[4], energyKev);

                double fPhoto = 1.0;
                MaterialDatabase.Fluorescence fluorescence = MaterialDatabase.FluorescenceOf(AirZ[i]);
                double edgeKev = AirKEdgeKev[i];
                if (fluorescence != null && energyKev > edgeKev)
                {
                    fPhoto = 1.0 - fluorescence.OmegaK * edgeKev / energyKev;
                }

                sum += AirWeight[i] * (photo * fPhoto
                                       + incoherent * fCompton
                                       + (pairNuclear + pairElectron) * fPair);
            }

            // см²/г → м²/кг: старая вшитая таблица была именно в м²/кг.
            return sum / 10.0;
        }

        /// <summary>
        /// h*(10)/K_air, Зв/Гр (ICRP 74, таблица A.21).
        ///
        /// **Интерполяция между узлами названа: значение — ЛИНЕЙНО, энергия —
        /// ЛОГАРИФМИЧЕСКИ** (`h = h_lo + t·(h_hi − h_lo)`, `t = ln(E/E_lo) /
        /// ln(E_hi/E_lo)`). Именно эта схема, а не сплайн: прежний код гнул по
        /// своим шестнадцати узлам монотонный кубический сплайн, и узловая
        /// сверка такой разницы не видит вовсе — между узлами схемы расходятся
        /// там, где в узлах совпадают. Разница двух схем на ОДНИХ узлах
        /// измерена и меньше 1 % (`A198`, проба `DoseCoefProbeO2`).
        ///
        /// За краями таблицы — ОТКАЗ, а не крайнее значение: ниже 10 кэВ
        /// величина падает на два порядка на декаду, и удержание края завысило
        /// бы дозу в сотни раз.
        /// </summary>
        public static double AmbientDoseConversion(double energyKev)
        {
            if (energyKev < MinEnergyKev || energyKev > MaxEnergyKev)
            {
                throw new DoseRateRefusalException(string.Format(
                    CultureInfo.InvariantCulture,
                    Text("DoseRateOutsideIcrp",
                         "Dose rate: {0} keV is outside the ICRP 74 h*(10)/Ka table ({1}...{2} keV)."),
                    energyKev, MinEnergyKev, MaxEnergyKev));
            }

            int n = AmbientEnergyKev.Length;
            if (energyKev <= AmbientEnergyKev[0])
            {
                return AmbientConversion[0];
            }

            if (energyKev >= AmbientEnergyKev[n - 1])
            {
                return AmbientConversion[n - 1];
            }

            int lo = 0;
            int hi = n - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (AmbientEnergyKev[mid] <= energyKev)
                {
                    lo = mid;
                }
                else
                {
                    hi = mid;
                }
            }

            double t = (Math.Log(energyKev) - Math.Log(AmbientEnergyKev[lo]))
                       / (Math.Log(AmbientEnergyKev[hi]) - Math.Log(AmbientEnergyKev[lo]));
            return AmbientConversion[lo] + t * (AmbientConversion[hi] - AmbientConversion[lo]);
        }

        /// <summary>
        /// Множитель диапазона: доза на один отсчёт с точностью до общего
        /// множителя (он сокращается при нормировке на эталон).
        /// </summary>
        public static double Factor(double energyKev)
        {
            return MassEnergyAbsorptionAir(energyKev) * AmbientDoseConversion(energyKev) * energyKev;
        }

        /// <summary>
        /// Средняя доля энергии кванта, переданная электрону при комптоновском
        /// рассеянии, по Клейну — Нишине: σ_tr/σ.
        ///
        /// Считается численным интегрированием по углу, а не замкнутой
        /// формулой: формулу Аттикса переписывают с ошибками, а квадратура
        /// проверяется опорными числами (0.1380 на 100 кэВ, 0.4400 на 1 МэВ,
        /// 0.6836 на 10 МэВ) и стоит микросекунды. Ответы кэшируются: на один
        /// разбор сетки их полтора десятка.
        /// </summary>
        public static double ComptonTransferFraction(double energyKev)
        {
            double cached;
            lock (comptonCache)
            {
                if (comptonCache.TryGetValue(energyKev, out cached))
                {
                    return cached;
                }
            }

            double alpha = energyKev / ElectronMassKev;
            const int Steps = 4096;
            double sigma = 0.0;
            double sigmaTransfer = 0.0;
            for (int i = 0; i < Steps; i++)
            {
                double theta = Math.PI * (i + 0.5) / Steps;
                double cos = Math.Cos(theta);
                double k = 1.0 / (1.0 + alpha * (1.0 - cos));   // E'/E
                // Дифференциальное сечение КН без общего множителя r_e²/2 — он
                // сокращается в отношении.
                double d = k * k * (k + 1.0 / k - (1.0 - cos * cos));
                double w = d * Math.Sin(theta);
                sigma += w;
                sigmaTransfer += w * (1.0 - k);
            }

            double value = sigma > 0.0 ? sigmaTransfer / sigma : 0.0;
            lock (comptonCache)
            {
                comptonCache[energyKev] = value;
            }

            return value;
        }

        /// <summary>
        /// Таблица «энергия — μ_en/ρ — h*(10)/K_air — множитель» на заданных
        /// энергиях. Ровно то, чего у вкладки не было: посмотреть, ЧЕМ считают.
        /// </summary>
        public static string Describe(IEnumerable<double> energiesKev)
        {
            var lines = new List<string>();
            lines.Add("E, keV\tmu_en/rho, m2/kg\th*(10)/Ka, Sv/Gy\tfactor");
            foreach (double e in energiesKev)
            {
                string mu, h, f;
                try
                {
                    mu = MassEnergyAbsorptionAir(e).ToString("g6", CultureInfo.InvariantCulture);
                    h = AmbientDoseConversion(e).ToString("g4", CultureInfo.InvariantCulture);
                    f = Factor(e).ToString("g6", CultureInfo.InvariantCulture);
                }
                catch (DoseRateRefusalException ex)
                {
                    mu = h = f = ex.Message;
                }

                lines.Add(string.Format(CultureInfo.InvariantCulture, "{0:g6}\t{1}\t{2}\t{3}", e, mu, h, f));
            }

            return string.Join(Environment.NewLine, lines.ToArray());
        }

        /// <summary>
        /// Строка ресурсов по имени с запасным текстом.
        ///
        /// ⚠ Так, а не `Resources.Имя`, нарочно: полоса, которая пишет этот код,
        /// не правит `Properties/Resources.resx` — пары ключей заводит
        /// координатор дерева. Пока ключа нет, работает запасной английский
        /// текст; как только пара (EN + RU) появится, подхватывается она, и
        /// править код не нужно.
        /// </summary>
        public static string Text(string key, string fallback)
        {
            try
            {
                string value = Resources.ResourceManager.GetString(key, Resources.Culture);
                return string.IsNullOrEmpty(value) ? fallback : value;
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        static readonly Dictionary<double, double> comptonCache = new Dictionary<double, double>();
    }

    /// <summary>
    /// Кривая эффективности, готовая к счёту: сама интерполяция плюс границы
    /// той области, где она что-то значит.
    ///
    /// ⚠ Обёртка не украшение. Во-первых, за крайними точками сплайн продолжает
    /// СВОЮ форму, а не эффективность, и границы обязаны ехать вместе со
    /// значениями — иначе делят на выдумку. Во-вторых, у проб нет ссылки на
    /// `MathNet.Numerics` (её не раздаёт `build_all.ps1`), и `IInterpolation` в
    /// открытом виде делал бы весь расчёт непроверяемым снаружи приложения.
    /// </summary>
    public sealed class DoseRateCurve
    {
        readonly IInterpolation interpolation;

        internal DoseRateCurve(IInterpolation interpolation, double minKev, double maxKev, int count)
        {
            this.interpolation = interpolation;
            this.MinKev = minKev;
            this.MaxKev = maxKev;
            this.Count = count;
        }

        /// <summary>Энергия первой точки, кэВ.</summary>
        public double MinKev { get; private set; }

        /// <summary>Энергия последней точки, кэВ.</summary>
        public double MaxKev { get; private set; }

        /// <summary>Сколько точек легло в кривую после отсева дублей.</summary>
        public int Count { get; private set; }

        /// <summary>Значение кривой. За её краями — отказ, а не продолжение формы.</summary>
        public double At(double energyKev)
        {
            if (energyKev < this.MinKev || energyKev > this.MaxKev)
            {
                throw new DoseRateRefusalException(string.Format(
                    CultureInfo.InvariantCulture,
                    DoseRateCoefficients.Text("DoseRateOutsideCurve",
                        "Dose rate: {0:f1} keV is outside the efficiency curve ({1:f1}...{2:f1} keV)."),
                    energyKev, this.MinKev, this.MaxKev));
            }

            return this.interpolation.Interpolate(energyKev);
        }
    }

    /// <summary>
    /// Спектр, предложенный вкладке «Dose Rate» без второго диалога открытия
    /// файла (`C4(б)`).
    /// </summary>
    public class DoseRateSpectrumChoice
    {
        public string Title { get; set; }

        public EnergySpectrum Spectrum { get; set; }

        /// <summary>Своя кривая эффективности спектра, если она у него есть.</summary>
        public EfficiencyConfigData Efficiency { get; set; }

        public override string ToString()
        {
            return this.Title ?? "";
        }
    }

    /// <summary>
    /// Оценка точек калибровки мощности дозы по эталонному спектру.
    ///
    /// Вынесено из `DeviceConfigForm` (`C4(в)`) по двум причинам. Первая: сетка
    /// и коэффициенты были вшиты в обработчик кнопки, и посмотреть на них можно
    /// было только в исходнике. Вторая: пока расчёт жил внутри формы, проверить
    /// его без окна было нельзя, а окно `BecqMoni` пробе запускать нечем.
    /// </summary>
    public static class DoseRateEstimator
    {
        /// <summary>
        /// Отношение соседних узлов сетки. Старая вшитая сетка (40, 50, 60, 80,
        /// 100, 150, ... 3000) идёт со средним отношением 1.325 — новая
        /// геометрическая сетка с тем же шагом даёт на 40–3000 кэВ шестнадцать
        /// диапазонов против прежних пятнадцати, то есть на глаз ту же
        /// подробность.
        /// </summary>
        public const double GridRatio = 1.325;

        /// <summary>
        /// Границы диапазонов от <paramref name="minKev"/> до
        /// <paramref name="maxKev"/>, обрезанные по применимости коэффициентов.
        ///
        /// ⛔ Обрезание ЗДЕСЬ — не то же самое, что прежнее молчаливое: за 10 кэВ
        /// снизу и 10 МэВ сверху коэффициента перехода к H*(10) не существует, а
        /// прежние 40 и 3000 кэВ не значили ничего, кроме длины двух массивов.
        /// </summary>
        public static double[] BuildGrid(double minKev, double maxKev)
        {
            double low = Math.Max(minKev, DoseRateCoefficients.MinEnergyKev);
            double high = Math.Min(maxKev, DoseRateCoefficients.MaxEnergyKev);
            if (!(low > 0.0) || !(high > low))
            {
                throw new DoseRateRefusalException(string.Format(
                    CultureInfo.InvariantCulture,
                    DoseRateCoefficients.Text("DoseRateEmptyRange",
                        "Dose rate: the device scale ({0:f0}...{1:f0} keV) does not overlap the range where the coefficients are defined ({2:f0}...{3:f0} keV)."),
                    minKev, maxKev, DoseRateCoefficients.MinEnergyKev, DoseRateCoefficients.MaxEnergyKev));
            }

            int bins = (int)Math.Ceiling(Math.Log(high / low) / Math.Log(GridRatio));
            if (bins < 1)
            {
                bins = 1;
            }

            var edges = new double[bins + 1];
            for (int i = 0; i <= bins; i++)
            {
                edges[i] = low * Math.Pow(high / low, (double)i / bins);
            }

            // Края ставятся точно, без накопленной ошибки степени.
            edges[0] = low;
            edges[bins] = high;
            return edges;
        }

        /// <summary>
        /// Шкала прибора в кэВ. Берётся у КОНФИГУРАЦИИ, а не у эталонного
        /// спектра: точки калибровки лягут в конфигурацию и будут применяться
        /// ко всем её измерениям. Если у конфигурации шкалы нет, остаётся
        /// шкала спектра.
        /// </summary>
        public static void DeviceRange(DeviceConfigInfo config, EnergySpectrum fallback,
                                       out double minKev, out double maxKev)
        {
            minKev = 0.0;
            maxKev = 0.0;

            EnergyCalibration calibration = null;
            int channels = 0;
            if (config != null && config.EnergyCalibration != null && config.NumberOfChannels > 1)
            {
                calibration = config.EnergyCalibration;
                channels = config.NumberOfChannels;
            }
            else if (fallback != null && fallback.EnergyCalibration != null && fallback.NumberOfChannels > 1)
            {
                calibration = fallback.EnergyCalibration;
                channels = fallback.NumberOfChannels;
            }

            if (calibration == null)
            {
                throw new DoseRateRefusalException(DoseRateCoefficients.Text(
                    "DoseRateNoScale",
                    "Dose rate: neither the device configuration nor the spectrum has an energy scale — there is nothing to build the ranges on."));
            }

            double a = calibration.ChannelToEnergy(0.0);
            double b = calibration.ChannelToEnergy(channels - 1);
            minKev = Math.Min(a, b);
            maxKev = Math.Max(a, b);
            if (!(maxKev > minKev) || double.IsNaN(minKev) || double.IsNaN(maxKev))
            {
                throw new DoseRateRefusalException(string.Format(
                    CultureInfo.InvariantCulture,
                    DoseRateCoefficients.Text("DoseRateBadScale",
                        "Dose rate: the energy scale is degenerate ({0}...{1} keV over {2} channels)."),
                    minKev, maxKev, channels));
            }
        }

        /// <summary>
        /// Точки калибровки по эталонному спектру с объявленной мощностью дозы.
        ///
        /// Отказывается ВИДИМО (исключением с текстом), а не возвращает ноль:
        /// нет спектра, нет калибровки, пустая шкала, нулевое время набора,
        /// эталон без отсчётов, неположительная объявленная доза, кривая
        /// эффективности с нулём или отрицательным значением.
        /// </summary>
        public static List<DoseRateCalibrationPoint> Estimate(
            EnergySpectrum spectrum, DoseRateCurve efficiency,
            double expectedDoseRate, double[] energies, IList<string> log)
        {
            if (spectrum == null)
            {
                throw new DoseRateRefusalException(DoseRateCoefficients.Text(
                    "DoseRateNoSpectrum", "Dose rate: no reference spectrum is selected."));
            }

            if (spectrum.EnergyCalibration == null)
            {
                throw new DoseRateRefusalException(DoseRateCoefficients.Text(
                    "DoseRateNoCalibration",
                    "Dose rate: the reference spectrum has no energy calibration — the channels cannot be turned into keV."));
            }

            if (spectrum.Spectrum == null || spectrum.NumberOfChannels < 2)
            {
                throw new DoseRateRefusalException(DoseRateCoefficients.Text(
                    "DoseRateEmptySpectrum", "Dose rate: the reference spectrum has no channels."));
            }

            if (!(spectrum.MeasurementTime > 0.0))
            {
                throw new DoseRateRefusalException(DoseRateCoefficients.Text(
                    "DoseRateNoTime", "Dose rate: the reference spectrum has zero measurement time."));
            }

            if (efficiency == null)
            {
                throw new DoseRateRefusalException(DoseRateCoefficients.Text(
                    "DoseRateNoEfficiency", "Dose rate: no efficiency curve is selected."));
            }

            if (!(expectedDoseRate > 0.0))
            {
                throw new DoseRateRefusalException(DoseRateCoefficients.Text(
                    "DoseRateNoExpected", "Dose rate: the declared dose rate of the source must be positive."));
            }

            if (energies == null || energies.Length < 2)
            {
                throw new DoseRateRefusalException(DoseRateCoefficients.Text(
                    "DoseRateNoGrid", "Dose rate: the energy grid is empty."));
            }

            int bins = energies.Length - 1;
            var rangeCps = new double[bins];
            var rangeEff = new double[bins];
            var rangeFactor = new double[bins];
            double modelDoseRate = 0.0;

            for (int i = 0; i < bins; i++)
            {
                double fromE = energies[i];
                double toE = energies[i + 1];
                double centerE = 0.5 * (fromE + toE);

                // Отказ ЗДЕСЬ, а не молчаливое нулевое слагаемое: коэффициент
                // вне таблицы значит, что диапазон построен неверно.
                rangeFactor[i] = DoseRateCoefficients.Factor(centerE);

                double eff = efficiency.At(centerE);
                if (!(eff > 0.0) || double.IsNaN(eff) || double.IsInfinity(eff))
                {
                    throw new DoseRateRefusalException(string.Format(
                        CultureInfo.InvariantCulture,
                        DoseRateCoefficients.Text("DoseRateBadEfficiency",
                            "Dose rate: the efficiency curve gives {0} at {1:f0} keV — division by it is meaningless."),
                        eff, centerE));
                }

                rangeEff[i] = eff;

                // ⛔ ОТБРАСЫВАНИЕ дробной части, а не округление (найдено
                // 05.09.2026, `C4`). `DoseRateManager` — потребитель этих точек
                // — приводит границу к каналу приведением `(int)`, то есть
                // отбрасыванием; здесь стояло `Convert.ToInt32`, то есть
                // округление. Генератор и потребитель расходились на полканала
                // у каждой границы, и обратный ход «посчитать точки по эталону,
                // потом померить тот же эталон» давал не объявленную дозу, а
                // 1.0005 от неё на пятнадцати диапазонах и 1.002 на
                // двадцати одном — ошибка росла с ЧИСЛОМ границ. Ровно та же
                // болезнь, что `W19`, и лечится так же: одно правило на обоих
                // концах.
                int fromChannel = (int)spectrum.EnergyCalibration.EnergyToChannel(
                    fromE, maxChannels: spectrum.NumberOfChannels);
                // ⚠ Верхний зажим — `NumberOfChannels - 1`, как у потребителя:
                // `DoseRateManager` последний канал не считает НИКОГДА (у него
                // `endch = Spectrum.Length - 1`, а цикл строгий). Стоило
                // генератору взять на канал больше — и на ASN16 обратный ход
                // разошёлся на 0.19 %: в последнем канале лежит переполнение,
                // 0.142 отсчёта в секунду против 0.012 во всём диапазоне
                // 2614–3453 кэВ. У переполнения энергии нет, и в дозу ему
                // нечего давать.
                int toChannel = Math.Min(
                    (int)spectrum.EnergyCalibration.EnergyToChannel(
                        toE, maxChannels: spectrum.NumberOfChannels),
                    spectrum.NumberOfChannels - 1);
                if (fromChannel < 0)
                {
                    fromChannel = 0;
                }

                double counts = 0.0;
                // Полуоткрыто, как и в DoseRateManager: диапазоны идут встык,
                // и граничный канал принадлежит следующему (W19).
                for (int j = fromChannel; j < toChannel && j < spectrum.Spectrum.Length; j++)
                {
                    counts += spectrum.Spectrum[j];
                }

                rangeCps[i] = counts / spectrum.MeasurementTime;
                modelDoseRate += rangeCps[i] * rangeFactor[i] / rangeEff[i];
            }

            if (!(modelDoseRate > 0.0))
            {
                throw new DoseRateRefusalException(DoseRateCoefficients.Text(
                    "DoseRateEtalonEmpty",
                    "Dose rate: the reference spectrum has no counts inside the ranges — there is nothing to calibrate against."));
            }

            double coefficient = expectedDoseRate / modelDoseRate;
            var points = new List<DoseRateCalibrationPoint>();
            for (int i = 0; i < bins; i++)
            {
                // Чувствительность диапазона — доза на один отсчёт; в точке она
                // лежит частным Etalon/CPS. Прежде сюда клали CPS = 1, и колонка
                // не значила ничего: посмотреть, сколько эталон реально дал в
                // этом диапазоне, было негде. Теперь в CPS идёт ИЗМЕРЕННАЯ
                // скорость счёта диапазона, а в Etalon — пришедшаяся на него
                // доза; частное, то есть сама чувствительность, прежнее
                // (C4(г), решение Amber 08.08.2026).
                double sensitivity = coefficient * rangeFactor[i] / rangeEff[i];
                if (!(rangeCps[i] > 0.0))
                {
                    // Диапазон, в котором эталон не дал ни одного отсчёта,
                    // откалибровать по нему НЕЛЬЗЯ: частное 0/0 не определено, а
                    // прежняя запись подставляла туда чистую модель, выдавая
                    // измерением то, что измерением не было.
                    if (log != null)
                    {
                        log.Add(string.Format(CultureInfo.InvariantCulture,
                            "Dose rate: диапазон {0:f1}–{1:f1} кэВ пуст в эталонном спектре, точка не заведена.",
                            energies[i], energies[i + 1]));
                    }

                    continue;
                }

                points.Add(new DoseRateCalibrationPoint
                {
                    LowerBound = energies[i],
                    UpperBound = energies[i + 1],
                    CPS = rangeCps[i],
                    EtalonDoseRateValue = sensitivity * rangeCps[i],
                });
            }

            return points;
        }

        // ------------------------------------------------------------------
        // Что предложить человеку вместо диалога открытия файла
        // ------------------------------------------------------------------

        /// <summary>
        /// Кривые эффективности, которые живут в САМОЙ конфигурации прибора
        /// (`C4(а)`). Годится та, у которой есть кривая: геометрия без
        /// посчитанной кривой делить не на что.
        /// </summary>
        public static List<EfficiencyConfigData> OfferedEfficiencies(DeviceConfigInfo config)
        {
            var list = new List<EfficiencyConfigData>();
            if (config == null || config.EfficiencyConfigs == null)
            {
                return list;
            }

            foreach (EfficiencyConfigData item in config.EfficiencyConfigs)
            {
                if (item != null && item.HasCurve)
                {
                    list.Add(item);
                }
            }

            return list;
        }

        /// <summary>
        /// Уже открытые спектры, годные в эталонные (`C4(б)`). Годен спектр с
        /// калибровкой, каналами и ненулевым временем набора: остальным вкладке
        /// нечего предложить, и молча подсовывать их нельзя.
        /// </summary>
        public static List<DoseRateSpectrumChoice> OfferedSpectra(IList<string> titles, IList<ResultData> results)
        {
            var list = new List<DoseRateSpectrumChoice>();
            if (results == null)
            {
                return list;
            }

            for (int i = 0; i < results.Count; i++)
            {
                ResultData data = results[i];
                if (data == null || data.EnergySpectrum == null)
                {
                    continue;
                }

                EnergySpectrum spectrum = data.EnergySpectrum;
                if (spectrum.EnergyCalibration == null || spectrum.NumberOfChannels < 2
                    || !(spectrum.MeasurementTime > 0.0))
                {
                    continue;
                }

                list.Add(new DoseRateSpectrumChoice
                {
                    Title = titles != null && i < titles.Count && !string.IsNullOrEmpty(titles[i])
                        ? titles[i]
                        : string.Format(CultureInfo.InvariantCulture, "#{0}", i + 1),
                    Spectrum = spectrum,
                    Efficiency = data.FileEfficiency ?? data.Efficiency,
                });
            }

            return list;
        }

        /// <summary>
        /// Кривая из точек — с проверкой входа, которой у вкладки не было
        /// (собственное `TODO: add input data validation`). Отказ называет, что
        /// именно не так.
        ///
        /// Вместе с кривой возвращаются её границы: сетку обязательно обрезать
        /// по ним. Поставочные кривые (`config/ROI/*`) идут ровно от 40 до
        /// 3000 кэВ — те самые два числа, что были вшиты; кривая, посчитанная
        /// из геометрии, шире.
        /// </summary>
        public static DoseRateCurve CurveOf(IList<ROIEfficiencyData> points)
        {
            if (points == null || points.Count < 2)
            {
                throw new DoseRateRefusalException(string.Format(
                    CultureInfo.InvariantCulture,
                    DoseRateCoefficients.Text("DoseRateCurveTooShort",
                        "Dose rate: the efficiency curve has {0} point(s), at least two are needed."),
                    points == null ? 0 : points.Count));
            }

            var sorted = points.Where(p => p != null).OrderBy(p => p.Energy).ToList();
            var energies = new List<double>();
            var values = new List<double>();
            foreach (ROIEfficiencyData point in sorted)
            {
                if (double.IsNaN(point.Energy) || double.IsInfinity(point.Energy) || !(point.Energy > 0.0))
                {
                    throw new DoseRateRefusalException(string.Format(
                        CultureInfo.InvariantCulture,
                        DoseRateCoefficients.Text("DoseRateCurveBadEnergy",
                            "Dose rate: the efficiency curve has a point at {0} keV."),
                        point.Energy));
                }

                if (double.IsNaN(point.Efficiency) || double.IsInfinity(point.Efficiency) || !(point.Efficiency > 0.0))
                {
                    throw new DoseRateRefusalException(string.Format(
                        CultureInfo.InvariantCulture,
                        DoseRateCoefficients.Text("DoseRateCurveBadValue",
                            "Dose rate: the efficiency curve gives {0} at {1:f1} keV."),
                        point.Efficiency, point.Energy));
                }

                // ⛔ ВЕРХНЯЯ ГРАНИЦА — ЕДИНИЦА (`A222`).
                //
                // Что лежит в колонке `Efficiency`: ДОЛЯ — сколько квантов из
                // испущенных пробой попало в пик полного поглощения. Величина
                // безразмерная, и больше единицы быть не может: детектор не
                // регистрирует больше событий, чем испущено. Это не догадка о
                // чужом формате, а объявленный смысл поля —
                // `EfficiencyConfigData.Curve` описан как «эффективность
                // долей», кривая из геометрии кладёт в него результат
                // `EfficiencySimulator.Efficiency` (зарегистрировано/испущено),
                // а `EfficiencyFitter` опирается на ту же границу ЧЕТЫРЕЖДЫ:
                // просеивает опорную кривую (`A222`), отвергает наблюдения с
                // ε > 1, режет выходную кривую по единице и отказывает на упоре
                // в потолок.
                //
                // ⚠ ЧТО ЭКСПОРТ ЛСРМ НЕСЁТ ТУ ЖЕ ВЕЛИЧИНУ В ТОЙ ЖЕ ШКАЛЕ — не
                // рассуждение, а СЛИЧЕНИЕ (06.09.2026): поставочный
                // `config/ROI/Obsidian Marinelli 0.5.xml` и есть тот самый
                // экспорт, ввезённый целиком, — все 150 точек совпадают до
                // последней значащей цифры (100 кэВ: 0.00378067 там и тут).
                // Значит колонка экспорта и поле `ROIEfficiencyData.Efficiency`
                // — одно и то же число, и граница у них общая.
                //
                // ⚠ Зачем отказ, если такую точку и так снимает правило
                // «погрешность выше 100 % не брать»: оно снимает её ПОБОЧНО и
                // ТОЛЬКО НА ВВОЗЕ. `Obsidian - marinelli 0.5.txt` несёт на
                // 20 кэВ 1.47185E+03 — 147 тысяч процентов, единственное такое
                // значение среди 1113 точек всех восьми экспортов, — и уходит
                // лишь потому, что ЛСРМ сам заявил ей 554 % погрешности. А в
                // ПОСТАВОЧНОЙ кривой она уже лежит: ввезена, когда правила ещё
                // не было, и `CurveOf` получает её напрямую из
                // `EfficiencyConfigData.Curve`, мимо всякого ввоза. Так что
                // проверка здесь не запасная — она единственная.
                //
                // ⚠ Отказ, а не пропуск точки: молча выброшенная точка меняет
                // форму кривой и остаётся невидимой, а кривая, у которой хоть
                // одна точка невозможна, целиком не заслуживает доверия. Здесь
                // это тем более так, что на кривую ДЕЛЯТ.
                //
                // ⚠ Цена, названная вслух: кривая, снятая «только по форме»
                // (`EfficiencyLevelSource.ShapeOnly`), по построению равна
                // единице на опорной энергии и ВЫШЕ единицы ниже неё —
                // `EfficiencyFitter.BuildCurve` там намеренно не режет. Такая
                // кривая теперь на вкладке мощности дозы отвергается, и это
                // верно: уровень у неё условный, а мощность дозы — величина
                // абсолютная. Прежде она давала число, ошибочное в неизвестное
                // число раз, и молча.
                if (!(point.Efficiency <= 1.0))
                {
                    throw new DoseRateRefusalException(string.Format(
                        CultureInfo.InvariantCulture,
                        DoseRateCoefficients.Text("DoseRateCurveAboveOne",
                            "Dose rate: the efficiency curve gives {0} at {1:f1} keV."
                            + " Efficiency is the fraction of the emitted photons registered"
                            + " and cannot exceed 1."),
                        point.Efficiency, point.Energy));
                }

                // Дубли по энергии сплайн валят: узлы обязаны строго расти.
                if (energies.Count > 0 && point.Energy <= energies[energies.Count - 1])
                {
                    continue;
                }

                energies.Add(point.Energy);
                values.Add(point.Efficiency);
            }

            if (energies.Count < 2)
            {
                throw new DoseRateRefusalException(string.Format(
                    CultureInfo.InvariantCulture,
                    DoseRateCoefficients.Text("DoseRateCurveTooShort",
                        "Dose rate: the efficiency curve has {0} point(s), at least two are needed."),
                    energies.Count));
            }

            // ⛔ ДВУХ УЗЛОВ МОНОТОННОМУ СПЛАЙНУ МАЛО — НУЖНО ТРИ (найдено
            // `BoundProbeF59` 06.09.2026, попутно к `A222`).
            //
            // Отказ выше обещает «хватит двух», и `DeviceConfigForm.LsrmMinPoints`
            // на это обещание прямо ссылается — а `Interpolate.CubicSplineMonotone`
            // на двух узлах бросает `ArgumentException` («The given array is too
            // small. It must be at least 3 long»). Это не отказ мощности дозы:
            // единственный вызывающий в приложении ловит `DoseRateRefusalException`,
            // и чужое исключение уходит НАВЕРХ из обработчика события — то есть
            // окном о падении вместо внятного «кривая не годится».
            //
            // ⚠ Лечится не подъёмом порога до трёх, а честной ЛОМАНОЙ на двух
            // узлах: обещание «двух хватает» дано потребителю и выполнимо —
            // через две точки прямая проходит, а монотонность у неё та же.
            // Поднять порог значило бы отвергать кривую, которая считается.
            //
            // ⚠ Кривых дерева это не касается ни одной: у поставочных 150 точек,
            // у экспортов ЛСРМ 59…150 после ввоза. Ветка нужна руками собранной
            // кривой — и именно она сегодня валилась.
            IInterpolation interpolation = energies.Count >= 3
                ? Interpolate.CubicSplineMonotone(energies, values)
                : Interpolate.Linear(energies, values);

            return new DoseRateCurve(interpolation,
                                     energies[0], energies[energies.Count - 1], energies.Count);
        }
    }
}
