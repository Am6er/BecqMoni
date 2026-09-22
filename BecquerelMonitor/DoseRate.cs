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

        /// <summary>
        /// Число посчитано по ПИКОВОЙ эффективности, а не по полной —
        /// решение (3) Amber 11.09.2026 (`AMBER18`), дословно: «По пиковой с
        /// пометкой». Пометка — знак «≈» перед числом в строке состояния.
        ///
        /// Почему это «≈», а не число: все отсчёты диапазона делятся на
        /// эффективность ПИКА ПОЛНОГО ПОГЛОЩЕНИЯ, а в диапазон попадает и
        /// комптоновский континуум чужих линий; отношение пик/полное
        /// энергозависимо, и одним множителем это не выправляется (дефект (1)
        /// `AMBER13`). Полная эффективность — сумма строки матрицы отклика — у
        /// кривой без матрицы взяться неоткуда, и человек с одной кривой ЛСРМ
        /// без дозы не остаётся.
        /// </summary>
        public bool Approximate
        {
            get { return this.approximate; }
            set { this.approximate = value; }
        }

        /// <summary>
        /// Разбивка по диапазонам сетки — то, из чего сложилось число.
        /// Пусто у отказа. Нужна пробе и журналу: «сколько дал каждый
        /// диапазон» иначе не посмотреть.
        /// </summary>
        public List<DoseRateRange> Ranges
        {
            get { return this.ranges; }
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

            // Пометка «по пиковой» — знаком ПЕРЕД числом, чтобы её нельзя было
            // не заметить: число со знаком «≈» и число без него — разные величины.
            if (this.approximate)
            {
                text = ApproximateMark + " " + text;
            }

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

        /// <summary>Знак «по пиковой с пометкой» (решение (3) `AMBER18`).</summary>
        public const string ApproximateMark = "≈";

        // Token: 0x040000F4 RID: 244
        double rate = 0.0;

        // Token: 0x040000F5 RID: 245
        double error = 0.0;

        string refusal;

        double coverage = -1.0;

        bool approximate;

        readonly List<DoseRateRange> ranges = new List<DoseRateRange>();
    }

    /// <summary>
    /// Один диапазон сетки мощности дозы: что в нём насчитано и во что это
    /// превратилось. Все величины названы своей единицей — здесь нет
    /// «множителя с точностью до общего», эталон снят (`AMBER18`).
    /// </summary>
    public sealed class DoseRateRange
    {
        /// <summary>Границы диапазона, кэВ; счёт полуоткрытый, [низ, верх).</summary>
        public double LowKev;

        public double HighKev;

        /// <summary>Энергия, которой представлен диапазон (середина), кэВ.</summary>
        public double CenterKev;

        /// <summary>Отсчётов в диапазоне без каналов переполнения — как есть.</summary>
        public double Counts;

        /// <summary>
        /// Сколько из них объяснено континуумом линий, приписанных диапазонам
        /// ВЫШЕ (только с матрицей; у пиковой кривой — 0).
        /// </summary>
        public double Explained;

        /// <summary>Отсчёты, приписанные квантам ЭТОГО диапазона: `Counts − Explained`, не ниже нуля.</summary>
        public double Attributed;

        /// <summary>Скорость счёта приписанных отсчётов, отсчёт/с.</summary>
        public double Cps;

        /// <summary>
        /// Эффективность на <see cref="CenterKev"/>: доля квантов, испущенных
        /// источником в 4π, что дали отсчёт ГДЕ УГОДНО, — ПОЛНАЯ (сумма строки
        /// матрицы) либо ПИКОВАЯ (кривая), см. <see cref="DoseRate.Approximate"/>.
        /// У сцены поля `ISO` (<see cref="DoseRateInput.Normalization"/> =
        /// <see cref="ResponseMatrixNormalization.PerUnitFluence"/>) — та же
        /// величина в **см²**: эффективная площадь на квант/см² поля.
        /// </summary>
        public double Efficiency;

        /// <summary>
        /// Знаменатель расчёта: доля квантов диапазона, давших отсчёт В ЭТОМ
        /// ЖЕ диапазоне (с матрицей); у пиковой кривой — та же <see cref="Efficiency"/>.
        /// У сцены поля — в см², как и <see cref="Efficiency"/>.
        /// </summary>
        public double OwnEfficiency;

        /// <summary>
        /// Диапазон не приписан никому: эффективность ниже пола
        /// <see cref="DoseRateManager.MinOwnEfficiencyFraction"/>; его отсчёты
        /// вне покрытия.
        /// </summary>
        public bool Skipped;

        /// <summary>
        /// Плотность потока, квант/(см²·с): у сцены с источником — в центре
        /// кристалла (<see cref="DoseRateInput.FluencePerPhoton"/>), у сцены
        /// поля `ISO` — само однородное поле (`Cps / A_эфф`, G ≡ 1).
        /// </summary>
        public double FluenceRate;

        /// <summary>Коэффициент перехода, мкЗв/ч на квант/(см²·с).</summary>
        public double DoseRatePerFluenceRate;

        /// <summary>Вклад диапазона в мощность дозы, мкЗв/ч.</summary>
        public double DoseRate;
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
    /// ⛔ ЭТАЛОНА БОЛЬШЕ НЕТ, И МНОЖИТЕЛЬ НЕ СОКРАЩАЕТСЯ (`AMBER18`, 12.09.2026).
    /// До 12.09.2026 обе величины входили в расчёт только формой кривой:
    /// чувствительность диапазона нормировалась на объявленную мощность дозы
    /// эталона, и общий множитель сокращался. Ручные точки и эталон сняты
    /// решениями Amber 10–11.09.2026, и доза стала АБСОЛЮТНОЙ: из
    /// эффективности, геометрии сцены и этих двух величин. Поэтому здесь
    /// появился явный перевод единиц — <see cref="DoseRatePerFluenceRate"/>;
    /// h*(10)/K_air по-прежнему хранится в опубликованном виде (Зв/Гр), без
    /// множителя 0.876 — он в расчёт не входит.
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

        /// <summary>Число Авогадро, 1/моль (CODATA, точное).</summary>
        public const double AvogadroPerMole = 6.02214076e23;

        /// <summary>
        /// Квадрат классического радиуса электрона, см². r_e = 2.8179403262e-13 см
        /// (CODATA 2018).
        /// </summary>
        public const double ElectronRadiusSquaredCm2 = 2.8179403262e-13 * 2.8179403262e-13;

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
        /// * некогерентное: полное сечение переноса Клейна — Нишины на
        ///   СВОБОДНОМ электроне, σ_tr, умноженное на число электронов в грамме
        ///   воздуха (см. <see cref="ComptonTransferCrossSection"/>);
        /// * пары: (E − 2m_e c²)/E, оба канала.
        ///
        /// ⛔ `A224`. Комптоновский член НЕ берёт сечение из XCOM. Раньше брал:
        /// σ_нк(XCOM) — сечение СВЯЗАННОГО электрона, с поправкой на
        /// некогерентную функцию рассеяния S(q,Z), — умножалось на среднюю долю
        /// переноса СВОБОДНОГО электрона f_КН. Это смесь двух моделей, и она
        /// занижает: связь гасит рассеяние вперёд, то есть ровно те события,
        /// где переноса почти нет, поэтому у связанного электрона средняя доля
        /// переноса ВЫШЕ свободной, а произведение σ_связ·f_своб выходит меньше
        /// обоих согласованных вариантов. Замер 06.09.2026 (одна перемена,
        /// та же сетка, та же интерполяция): расхождение с K_a/Φ ICRP 119 в
        /// области 40…150 кэВ ушло с −0.66…−1.35 % на +0.02…+0.45 %.
        /// Опубликованные μ_tr/ρ (Hubbell &amp; Seltzer) считаны так же — на
        /// свободном электроне; ниже 20 кэВ комптоновский член даёт меньше
        /// 0.1 % величины, и связь там роли не играет.
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

            double comptonTransfer = ComptonTransferCrossSection(energyKev);
            double fPair = energyKev > 2.0 * ElectronMassKev
                ? (energyKev - 2.0 * ElectronMassKev) / energyKev
                : 0.0;

            double sum = 0.0;
            double electronsPerGram = 0.0;
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
                                       + (pairNuclear + pairElectron) * fPair);

                // ⛔ Электроны считаются по ТОМУ ЖЕ атомному весу, каким
                // `MaterialDatabase` перевела барны в см²/г. Взять вес из
                // другого места значило бы сложить два разных воздуха.
                if (element.AtomicWeight > 0.0)
                {
                    electronsPerGram += AirWeight[i] * AvogadroPerMole * AirZ[i] / element.AtomicWeight;
                }
            }

            sum += electronsPerGram * comptonTransfer;

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
        /// Множитель (μ_en/ρ)_air · h*(10)/K_air · E — в единицах
        /// **м²/кг · Зв/Гр · кэВ**, то есть БЕЗ перевода в мкЗв/ч. Ровно то,
        /// что здесь лежало и раньше; перевод — отдельным множителем в
        /// <see cref="DoseRatePerFluenceRate"/>, чтобы прежние сверки
        /// (`DoseCoefProbeO2`, `DoseAirProbeF63`) читали прежнее число.
        /// </summary>
        public static double Factor(double energyKev)
        {
            return MassEnergyAbsorptionAir(energyKev) * AmbientDoseConversion(energyKev) * energyKev;
        }

        // ------------------------------------------------------------------
        // ⛔ ЯВНЫЕ ЕДИНИЦЫ (`AMBER18`, решение (3) Amber 10.09.2026 в `AMBER13`:
        //    «`Factor` получает явный множитель единиц»). Размерность каждой
        //    величины расчёта, чтобы её нельзя было потерять молча:
        //
        //    φ̇   плотность потока квантов в точке, где стоит центр
        //         кристалла, квант/(см²·с);
        //    μ_en/ρ  массовый коэффициент поглощения энергии воздуха, м²/кг
        //         (<see cref="MassEnergyAbsorptionAir"/>; ×10 — см²/г);
        //    E    энергия кванта, кэВ; 1 кэВ = 1.602176634e-16 Дж (точно, СИ 2019);
        //    K̇_air = φ̇ · E · (μ_en/ρ) — мощность кермы воздуха, Гр/с
        //         (Гр = Дж/кг; см²/г · 1000 г/кг · Дж · 1/(см²·с) = Дж/(кг·с));
        //    h*(10)/K_air — Зв/Гр (<see cref="AmbientDoseConversion"/>);
        //    Ḣ*(10) = K̇_air · h*(10)/K_air — Зв/с; ×1e6 мкЗв/Зв ×3600 с/ч → мкЗв/ч.
        //
        //    Сверка уровня (проба `DoseRateFromCurveProbe` §1, 12.09.2026): на
        //    662 кэВ μ_en/ρ = 2.9395e-3 м²/кг, h*(10)/K_a = 1.2032, и Ḣ*(10)/φ̇
        //    выходит 1.3504e-2 мкЗв/ч на квант/(см²·с) = 3.751 пЗв·см² на
        //    квант; ICRP 74, табл. A.1 и A.21, дают K_a/Φ = 3.08 пГр·см²
        //    (600…800 кэВ логарифмически) × h*(10)/K_a = 1.20 → 3.696 пЗв·см²,
        //    расхождение +1.49 % — на уровне интерполяции таблицы.
        // ------------------------------------------------------------------

        /// <summary>1 кэВ в джоулях — точное значение (СИ 2019).</summary>
        public const double JoulePerKev = 1.602176634e-16;

        /// <summary>
        /// Перевод произведения <see cref="Factor"/> (м²/кг · Зв/Гр · кэВ) в
        /// мкЗв/ч на квант/(см²·с): 10 (м²/кг → см²/г) · 1000 (г/кг) ·
        /// <see cref="JoulePerKev"/> · 1e6 (мкЗв/Зв) · 3600 (с/ч).
        /// </summary>
        public const double MicroSievertPerHourPerFactor =
            10.0 * 1000.0 * JoulePerKev * 1.0e6 * 3600.0;

        /// <summary>
        /// Мощность амбиентного эквивалента дозы на единичную плотность потока
        /// квантов энергии <paramref name="energyKev"/>: **мкЗв/ч на
        /// квант/(см²·с)**. Единственное место, где доза получает единицы.
        /// </summary>
        public static double DoseRatePerFluenceRate(double energyKev)
        {
            return Factor(energyKev) * MicroSievertPerHourPerFactor;
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
            double sigma, sigmaTransfer;
            KleinNishina(energyKev, out sigma, out sigmaTransfer);
            return sigma > 0.0 ? sigmaTransfer / sigma : 0.0;
        }

        /// <summary>
        /// Полное сечение ПЕРЕНОСА энергии при комптоновском рассеянии на
        /// свободном электроне, σ_tr = ∫ (T/hν) dσ_КН, **см²/электрон**.
        ///
        /// ⛔ `A224`. Именно эта величина, а не «сечение из XCOM, умноженное на
        /// долю», входит в μ_tr/ρ: XCOM отдаёт сечение СВЯЗАННОГО электрона, а
        /// доля переноса считается для СВОБОДНОГО, и перемножать их нельзя —
        /// см. <see cref="MassEnergyAbsorptionAir"/>.
        ///
        /// Та же квадратура, что у <see cref="ComptonTransferFraction"/>, но с
        /// общим множителем r_e²/2, который в отношении сокращался. Опора
        /// множителя — томсоновский предел: <see cref="ComptonCrossSection"/>
        /// на 0.01 кэВ обязано выйти на (8/3)π r_e² = 0.665246 барн/электрон
        /// (`DoseAirProbeF63`).
        /// </summary>
        public static double ComptonTransferCrossSection(double energyKev)
        {
            double sigma, sigmaTransfer;
            KleinNishina(energyKev, out sigma, out sigmaTransfer);
            return sigmaTransfer;
        }

        /// <summary>
        /// Полное сечение Клейна — Нишины на свободном электроне, см²/электрон.
        /// Нужно как опора самой квадратуры: у него есть замкнутая формула и
        /// табличные значения, у σ_tr — нет.
        /// </summary>
        public static double ComptonCrossSection(double energyKev)
        {
            double sigma, sigmaTransfer;
            KleinNishina(energyKev, out sigma, out sigmaTransfer);
            return sigma;
        }

        /// <summary>
        /// Квадратура Клейна — Нишины: σ и σ_tr разом, см²/электрон.
        /// Ответы кэшируются парой: на один разбор сетки их полтора десятка.
        /// </summary>
        static void KleinNishina(double energyKev, out double sigma, out double sigmaTransfer)
        {
            double[] cached;
            lock (comptonCache)
            {
                if (comptonCache.TryGetValue(energyKev, out cached))
                {
                    sigma = cached[0];
                    sigmaTransfer = cached[1];
                    return;
                }
            }

            double alpha = energyKev / ElectronMassKev;
            const int Steps = 4096;
            double s = 0.0;
            double st = 0.0;
            for (int i = 0; i < Steps; i++)
            {
                double theta = Math.PI * (i + 0.5) / Steps;
                double cos = Math.Cos(theta);
                double k = 1.0 / (1.0 + alpha * (1.0 - cos));   // E'/E
                // Дифференциальное сечение КН без общего множителя r_e²/2.
                double d = k * k * (k + 1.0 / k - (1.0 - cos * cos));
                double w = d * Math.Sin(theta);
                s += w;
                st += w * (1.0 - k);
            }

            // ∫…dθ ≈ (π/Steps)·Σ, dΩ = 2π sinθ dθ, множитель r_e²/2.
            double scale = Math.PI * Math.PI * ElectronRadiusSquaredCm2 / Steps;
            sigma = s * scale;
            sigmaTransfer = st * scale;

            lock (comptonCache)
            {
                comptonCache[energyKev] = new double[] { sigma, sigmaTransfer };
            }
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

        static readonly Dictionary<double, double[]> comptonCache = new Dictionary<double, double[]>();
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
    /// Сетка диапазонов, шкала и кривая — то, из чего собирается расчёт
    /// мощности дозы. Вынесено из `DeviceConfigForm` (`C4(в)`), чтобы это
    /// можно было посмотреть и проверить без окна.
    ///
    /// ⛔ ЭТАЛОНА И ТОЧЕК КАЛИБРОВКИ ЗДЕСЬ БОЛЬШЕ НЕТ (`AMBER18`, 12.09.2026).
    /// Прежний `Estimate` строил по эталонному спектру с объявленной дозой
    /// таблицу `DoseRateCalibrationPoint`, которую хранила конфигурация
    /// прибора и по которой считал `DoseRateManager`. Решениями Amber
    /// 10–11.09.2026 таблица, эталон и вкладка сняты целиком; расчёт идёт от
    /// кривой, ВЫБРАННОЙ НА ПАНЕЛИ (<see cref="DoseRateInput"/>), в один
    /// проход — <see cref="DoseRateManager.Calculate(ResultData, DoseRateInput)"/>.
    /// Списки «что предложить вкладке» (`OfferedEfficiencies`, `OfferedSpectra`,
    /// `DoseRateSpectrumChoice`) ушли вместе с ней.
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

            // ⛔ Верх шкалы — ВЕРХНЯЯ ГРАНИЦА ПОСЛЕДНЕГО КАНАЛА, `E(N)`, а не
            // `E(N − 1)` (12.09.2026, `AMBER18`, поймано `DoseRateProbe`). Канал
            // `N − 1` занимает [E(N−1), E(N)); сетка, обрезанная по E(N−1),
            // до него не доходила, и последний канал не считался НИКОГДА —
            // ровно та беда `A203`, которую зажим `Length − 1` у потребителя
            // уже лечил: у `G1S24_Th228_P5` терялись 127 отсчётов обычного
            // последнего канала. Прежде это скрывал генератор точек («его
            // собственный toChannel до N не доходит»), теперь генератора нет.
            double a = calibration.ChannelToEnergy(0.0);
            double b = calibration.ChannelToEnergy(channels);
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
        /// Кривая из точек — с проверкой входа, которой у вкладки не было
        /// (собственное `TODO: add input data validation`). Отказ называет, что
        /// именно не так.
        ///
        /// Вместе с кривой возвращаются её границы: сетку обязательно обрезать
        /// по ним. Поставочные кривые (`config/ROI/*`) идут ровно от 40 до
        /// 3000 кэВ — те самые два числа, что были вшиты; кривая, посчитанная
        /// из геометрии, шире.
        ///
        /// Эта запись — для кривой В ДОЛЯХ (нормировка «на квант источника»,
        /// потолок единица). Кривая сцены поля `ISO` — в см², и её ведут через
        /// <see cref="CurveOf(IList{ROIEfficiencyData}, ResponseMatrixNormalization, double)"/>.
        /// </summary>
        public static DoseRateCurve CurveOf(IList<ROIEfficiencyData> points)
        {
            return CurveOf(points, ResponseMatrixNormalization.PerEmittedQuantum, 1.0);
        }

        /// <summary>
        /// То же с явной нормировкой кривой (`AMBER13` (б), 12.09.2026).
        /// <paramref name="upperBound"/> — потолок значения: у кривой в долях
        /// единица (`A222`), у кривой сцены поля — площадь проекции сферы,
        /// описанной вокруг обвязанного детектора, см²
        /// (<see cref="DoseRateGeometry.ProjectedAreaBoundCm2"/>): больше
        /// неё эффективная площадь выпуклого тела быть не может (Коши), и
        /// такая точка — та же невозможная величина, что доля выше единицы.
        /// </summary>
        public static DoseRateCurve CurveOf(IList<ROIEfficiencyData> points,
                                            ResponseMatrixNormalization normalization,
                                            double upperBound)
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
                // а снятый 13.09.2026 фит по спектрам (`AMBER25`) опирался на
                // ту же границу четырежды: просеивал опорную кривую (`A222`),
                // отвергал наблюдения с ε > 1, резал выходную кривую по единице
                // и отказывал на упоре в потолок.
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
                // прежним эмпирическим восстановлением по спектрам (уровень
                // `ShapeOnly`; сам путь снят 13.09.2026, `AMBER25`, но такие
                // кривые могут лежать в старых конфигурациях), по построению
                // равна единице на опорной энергии и ВЫШЕ единицы ниже неё.
                // Такая кривая на вкладке мощности дозы отвергается, и это
                // верно: уровень у неё условный, а мощность дозы — величина
                // абсолютная. Прежде она давала число, ошибочное в неизвестное
                // число раз, и молча.
                //
                // У сцены поля `ISO` (`AMBER13` (б)) кривая — эффективная
                // площадь в см², и единица ей не потолок (у Ø63×63 NaI на
                // 662 кэВ A_пик = 13.8 см²); её потолок — площадь проекции
                // описанной сферы, тем же правом: больше неё выпуклое тело
                // в изотропном поле не собирает (Коши, S/4 ≤ πr²).
                if (normalization == ResponseMatrixNormalization.PerEmittedQuantum
                    && !(point.Efficiency <= 1.0))
                {
                    throw new DoseRateRefusalException(string.Format(
                        CultureInfo.InvariantCulture,
                        DoseRateCoefficients.Text("DoseRateCurveAboveOne",
                            "Dose rate: the efficiency curve gives {0} at {1:f1} keV."
                            + " Efficiency is the fraction of the emitted photons registered"
                            + " and cannot exceed 1."),
                        point.Efficiency, point.Energy));
                }

                if (normalization == ResponseMatrixNormalization.PerUnitFluence
                    && !(point.Efficiency <= upperBound))
                {
                    throw new DoseRateRefusalException(string.Format(
                        CultureInfo.InvariantCulture,
                        DoseRateCoefficients.Text("DoseRateAreaAboveBound",
                            "Dose rate: the field-scene curve gives {0} cm² at {1:f1} keV —"
                            + " more than the projected area of the sphere around the detector ({2:f1} cm²)."),
                        point.Efficiency, point.Energy, upperBound));
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

    /// <summary>
    /// ВХОД расчёта мощности дозы, собранный из кривой эффективности, ВЫБРАННОЙ
    /// НА ПАНЕЛИ (`AMBER18`, задача Amber 11.09.2026: «Привязаться к текущей
    /// выбранной эффективности на ControlPanel»). Чем делить отсчёты и чем
    /// превращать «квантов в секунду» в плотность потока.
    ///
    /// Три состояния, по решениям Amber 11.09.2026:
    ///
    ///  * у кривой есть геометрия и годная матрица отклика — эффективность
    ///    ПОЛНАЯ: сумма строки матрицы на энергии линии (строка нормирована «на
    ///    квант, испущенный источником в 4π», <see cref="ResponseMatrix"/>),
    ///    для ЛЮБОЙ сцены — решение (2): «Показывать мощность дозы из текущей
    ///    геометрии, пусть даже проба»;
    ///  * геометрия есть, матрицы нет (не посчитана, старого формата,
    ///    выключена галкой) — эффективность ПИКОВАЯ, из кривой, число идёт с
    ///    пометкой «≈» — решение (3): «По пиковой с пометкой»;
    ///  * геометрии нет (ввоз ЛСРМ одним экспортом, кривая руками) — ОТКАЗ с
    ///    причиной: у кривой без геометрии нет и масштаба, см.
    ///    <see cref="DoseRateGeometry"/>. ⚠ Это расхождение с буквой решения
    ///    (3), которое обещало «≈» и такой кривой; названо в журнале полосы
    ///    П1 12.09.2026 и вынесено Amber вопросом.
    ///
    /// Сцена поля `ISO` (`AMBER13` (б), полоса П6 12.09.2026) — те же три
    /// состояния, но нормировка другая: строка матрицы и кривая — ЭФФЕКТИВНАЯ
    /// ПЛОЩАДЬ в см² на квант/см² изотропного поля
    /// (<see cref="ResponseMatrixNormalization.PerUnitFluence"/>), и тогда
    /// `отсчёт/с ÷ A_эфф(E)` — уже плотность потока, G ≡ 1. Признак
    /// (<see cref="Normalization"/>) читается с трёх сторон — у геометрии
    /// (сцена), у кривой (клеймо `norm=fluence`) и у матрицы (хвост `NORM`) —
    /// и обязан сойтись: см² с долями не смешиваются, расхождение — отказ
    /// словами, а не число.
    /// </summary>
    public sealed class DoseRateInput
    {
        DoseRateInput()
        {
        }

        /// <summary>Имя кривой — в отказы и журнал.</summary>
        public string Name { get; private set; }

        /// <summary>Пиковая кривая; null, когда считают по матрице.</summary>
        public DoseRateCurve PeakCurve { get; private set; }

        /// <summary>Матрица отклика; null — считают по пиковой.</summary>
        public ResponseMatrix Matrix { get; private set; }

        /// <summary>
        /// На что нормированы эффективность и матрица этого входа: на квант
        /// источника (доля) либо на единичный флюенс (см², сцена поля `ISO`).
        /// Положена геометрией кривой (<see cref="ResponseMatrix.NormalizationOf"/>)
        /// и сверена с клеймом кривой и признаком матрицы в <see cref="Of"/>.
        /// </summary>
        public ResponseMatrixNormalization Normalization { get; private set; }

        /// <summary>
        /// Геометрический множитель сцены: плотность потока в точке, где стоит
        /// центр кристалла, на один квант, испущенный источником в 4π,
        /// **1/см²** (<see cref="DoseRateGeometry.FluencePerPhoton"/>).
        /// У сцены поля `ISO` — ровно 1: отклик там уже на единицу флюенса.
        /// </summary>
        public double FluencePerPhoton { get; private set; }

        /// <summary>Как получен множитель — для журнала и пробы.</summary>
        public string GeometryNote { get; private set; }

        /// <summary>Низ области, где вход что-то значит, кэВ.</summary>
        public double MinKev { get; private set; }

        /// <summary>Верх области, где вход что-то значит, кэВ.</summary>
        public double MaxKev { get; private set; }

        /// <summary>Считают по пиковой — число пойдёт с пометкой «≈».</summary>
        public bool Approximate
        {
            get { return this.Matrix == null; }
        }

        /// <summary>
        /// Собрать вход из кривой. <paramref name="matrix"/> — матрица этой же
        /// кривой или null; матрица чужой геометрии — отказ, а не молчаливый
        /// откат на пиковую: подсунуть чужую матрицу можно только нарочно.
        /// </summary>
        public static DoseRateInput Of(EfficiencyConfigData curve, ResponseMatrix matrix)
        {
            if (curve == null)
            {
                throw new DoseRateRefusalException(DoseRateCoefficients.Text(
                    "DoseRateNoEfficiency", "Dose rate: no efficiency curve is selected."));
            }

            string name = string.IsNullOrEmpty(curve.Name) ? "?" : curve.Name;
            if (!curve.HasCurve)
            {
                throw new DoseRateRefusalException(string.Format(
                    CultureInfo.InvariantCulture,
                    DoseRateCoefficients.Text("DoseRateCurveTooShort",
                        "Dose rate: the efficiency curve has {0} point(s), at least two are needed."),
                    curve.Curve == null ? 0 : curve.Curve.Count));
            }

            if (!curve.HasGeometry)
            {
                throw new DoseRateRefusalException(string.Format(
                    CultureInfo.InvariantCulture,
                    DoseRateCoefficients.Text("DoseRateNoGeometry",
                        "Dose rate: the curve \"{0}\" has no geometry — without it the emission rate"
                        + " cannot be turned into a fluence at the detector, and the dose has no scale."),
                    name));
            }

            if (matrix != null && !matrix.IsValidFor(curve.Geometry))
            {
                throw new DoseRateRefusalException(string.Format(
                    CultureInfo.InvariantCulture,
                    DoseRateCoefficients.Text("DoseRateForeignMatrix",
                        "Dose rate: the response matrix does not match the geometry of the curve \"{0}\"."),
                    name));
            }

            // ⛔ НОРМИРОВКА — С ТРЁХ СТОРОН, И ВСЕ ТРИ ОБЯЗАНЫ СОЙТИСЬ
            // (`AMBER13` (б)). Правило кладёт геометрия (сцена поля → см²,
            // всё прочее → доли); кривая несёт его клеймом `norm=fluence`
            // (`EfficiencyCalculation.Run`), матрица — хвостом `NORM`
            // (`ResponseMatrix.Normalization`). Кривая в долях у геометрии,
            // переключённой в поле руками, или кривая в см² у сцены с
            // источником делила бы отсчёты не на ту величину на порядки — и
            // молча, потому что число выходит «какое-то». Поэтому отказ.
            ResponseMatrixNormalization normalization = ResponseMatrix.NormalizationOf(curve.Geometry);
            ResponseMatrixNormalization curveNormalization = StampNormalization(curve.ComputeStamp);
            if (curveNormalization != normalization)
            {
                throw new DoseRateRefusalException(string.Format(
                    CultureInfo.InvariantCulture,
                    DoseRateCoefficients.Text("DoseRateCurveNormalizationMismatch",
                        "Dose rate: the curve \"{0}\" is normalised {1}, but its geometry ({2}) requires {3}"
                        + " — recompute the curve from the geometry."),
                    name, Describe(curveNormalization), curve.Geometry.Scene, Describe(normalization)));
            }

            if (matrix != null && matrix.Normalization != normalization)
            {
                throw new DoseRateRefusalException(string.Format(
                    CultureInfo.InvariantCulture,
                    DoseRateCoefficients.Text("DoseRateMatrixNormalizationMismatch",
                        "Dose rate: the response matrix is normalised {1}, but the curve \"{0}\" is {2}"
                        + " — square centimetres and fractions do not mix; recompute the matrix."),
                    name, Describe(matrix.Normalization), Describe(normalization)));
            }

            string note;
            double fluence = DoseRateGeometry.FluencePerPhoton(curve.Geometry, out note);

            var input = new DoseRateInput
            {
                Name = name,
                Matrix = matrix,
                Normalization = normalization,
                FluencePerPhoton = fluence,
                GeometryNote = note,
            };

            if (matrix != null)
            {
                if (matrix.Energies == null || matrix.Energies.Length == 0 || matrix.Rows == null)
                {
                    throw new DoseRateRefusalException(string.Format(
                        CultureInfo.InvariantCulture,
                        DoseRateCoefficients.Text("DoseRateEmptyMatrix",
                            "Dose rate: the response matrix of the curve \"{0}\" has no rows."),
                        name));
                }

                input.MinKev = matrix.Energies[0];
                input.MaxKev = matrix.Energies[matrix.Energies.Length - 1];

                // ⚡ (`AMBER71`) ПЕРЕНОС СТРОКИ УЗЛА НА ЭНЕРГИЮ ЛИНИИ — ПО
                // КАНАЛАМ, тем же правилом, что у разбора
                // (`FsaAnalyzer.MatrixTransferByChannel`, `AMBER16` п. 4,
                // 11.09.2026). Правило 11.09 до дозы не
                // доехало: она читала свежий экземпляр из склада с умолчанием
                // «общий масштаб», и пик узла, умноженный на `E/E_узла`, падал
                // МЕЖДУ бинами приёмника — «своя доля» диапазона у границы
                // теряла проценты на ровном месте.
                //
                // Ставится ЗДЕСЬ, в единственном месте, где матрица становится
                // входом дозы: так приложение и пробы читают её одинаково.
                // Это ключ ЧТЕНИЯ — в клеймо не входит, файл не меняет, склад
                // пересчитывать не требует (см. `ResponseMatrix.TransferByChannel`).
                matrix.TransferByChannel = true;
            }
            else
            {
                input.PeakCurve = DoseRateEstimator.CurveOf(
                    curve.Curve, normalization,
                    normalization == ResponseMatrixNormalization.PerUnitFluence
                        ? DoseRateGeometry.ProjectedAreaBoundCm2(curve.Geometry)
                        : 1.0);
                input.MinKev = input.PeakCurve.MinKev;
                input.MaxKev = input.PeakCurve.MaxKev;
            }

            return input;
        }

        /// <summary>Строка `norm=fluence` в клейме кривой — единицы см².</summary>
        public const string FluenceStampMark = "norm=fluence";

        /// <summary>
        /// Нормировка, объявленная клеймом кривой: `norm=fluence` пишет ТОЛЬКО
        /// кривая сцены поля (`EfficiencyCalculation.Run`, тем же правилом
        /// `T42`, что клеймо матрицы); пустое клеймо (ввоз ЛСРМ, кривая
        /// руками) и клеймо без строки — доли.
        /// </summary>
        public static ResponseMatrixNormalization StampNormalization(string computeStamp)
        {
            return computeStamp != null && computeStamp.IndexOf(FluenceStampMark, StringComparison.Ordinal) >= 0
                ? ResponseMatrixNormalization.PerUnitFluence
                : ResponseMatrixNormalization.PerEmittedQuantum;
        }

        /// <summary>Нормировка словами — в отказы (инвариантно, как клеймо).</summary>
        static string Describe(ResponseMatrixNormalization normalization)
        {
            return normalization == ResponseMatrixNormalization.PerUnitFluence
                ? "per unit fluence (cm²)"
                : "per emitted quantum (fraction)";
        }

        /// <summary>
        /// Эффективность на энергии: доля квантов, испущенных источником в 4π,
        /// что дали отсчёт ГДЕ УГОДНО по шкале (матрица) либо в пике (кривая);
        /// у сцены поля — то же в см² (<see cref="Normalization"/>).
        /// За краями области — отказ, как у <see cref="DoseRateCurve.At"/>:
        /// матрица за краями сетки удерживает крайний узел, и это была бы
        /// выдумка, выданная измерением.
        /// </summary>
        public double EfficiencyAt(double energyKev)
        {
            if (this.Matrix == null)
            {
                return this.PeakCurve.At(energyKev);
            }

            if (energyKev < this.MinKev || energyKev > this.MaxKev)
            {
                throw new DoseRateRefusalException(string.Format(
                    CultureInfo.InvariantCulture,
                    DoseRateCoefficients.Text("DoseRateOutsideMatrix",
                        "Dose rate: {0:f1} keV is outside the response matrix ({1:f1}...{2:f1} keV)."),
                    energyKev, this.MinKev, this.MaxKev));
            }

            return FullEfficiency(this.Matrix, energyKev);
        }

        /// <summary>
        /// ПОЛНАЯ эффективность регистрации на энергии линии — сумма строки
        /// матрицы, интерполированной между узлами ТЕМ ЖЕ кодом, что и разбор
        /// (<see cref="ResponseMatrix.Evaluate"/>). Один бин приёмника: перенос
        /// сохраняет площадь, вклады за краем зажимаются в крайний бин, и в
        /// единственный бин ложится вся строка целиком — ровно её сумма.
        /// </summary>
        public static double FullEfficiency(ResponseMatrix matrix, double energyKev)
        {
            double[] one = matrix.Evaluate(energyKev, 1);
            return one.Length > 0 ? one[0] : 0.0;
        }
    }

    /// <summary>
    /// ⛔ ЕДИНСТВЕННОЕ МЕСТО, ГДЕ НОРМИРОВКА СТРОКИ ПРЕВРАЩАЕТСЯ В ДОЗУ
    /// (`AMBER18`, 12.09.2026).
    ///
    /// Что дано. Эффективность — и пиковая, и строка матрицы — нормирована
    /// «на квант, испущенный источником в 4π» (<see cref="ResponseMatrix"/>,
    /// <see cref="EfficiencySimulator.Efficiency"/>). Значит
    /// `отсчёт/с ÷ ε(E)` — это N(E), квантов в секунду, испущенных
    /// источником сцены. Доза же — свойство ПОТОКА в точке: K̇_air =
    /// φ̇ · E · μ_en/ρ. Между N и φ̇ стоит геометрия: φ̇ = N · G, где G —
    /// плотность потока в точке от одного испущенного кванта, 1/см².
    ///
    /// Что считается здесь. G = ⟨1/(4π r²)⟩ по объёму источника, r — от
    /// ЦЕНТРА КРИСТАЛЛА до точки источника, БЕЗ ослабления: у точечного
    /// источника это 1/(4π R²), у сосуда и маринелли — интеграл по телу
    /// (по ρ аналитически, по z квадратурой), у кюветы — квадратура по трём
    /// осям. Сцена строится ТЕМИ ЖЕ правилами, что у симулятора
    /// (`EfficiencySimulator.Build`): расстояния от переднего торца корпуса,
    /// торцевые толщины обвязки, разворот бруска боком к пробе.
    ///
    /// ⚠ ЦЕНА, названная вслух. (1) Ослабление в самой пробе сюда не входит:
    /// N посчитан верно (ε его несёт), а поток в центре от НЕослабляющей
    /// пробы выше настоящего — у объёмной пробы доза ЗАВЫШЕНА на её
    /// самопоглощение (вода в маринелли на 662 кэВ — десятки процентов, оксид
    /// лютеция на 200 кэВ — в разы). (2) Точка отсчёта — центр кристалла; у
    /// источника на торце большого кристалла поток по кристаллу меняется в
    /// разы, и «доза в центре» — соглашение. (3) Обвязка и оправа поток не
    /// ослабляют (доли процента у сцинтиллятора). Всё это снимает сцена ПОЛЯ
    /// (`AMBER13` (б), `ISO`): её строка нормируется на ЕДИНИЧНЫЙ ФЛЮЕНС, и
    /// тогда G ≡ 1, а самопоглощения и центра нет по построению.
    ///
    /// ✅ Сцена поля `ISO` (решение Amber 12.09.2026 «Оставь только ISO»;
    /// производитель — полоса П2, потребитель — П6 того же дня): для неё
    /// этот метод возвращает ровно 1.0 — отклик уже на единицу флюенса
    /// (<see cref="ResponseMatrixNormalization.PerUnitFluence"/>), и в самом
    /// расчёте ничего больше не меняется: `отсчёт/с ÷ A_эфф(E) [см²]` есть
    /// плотность потока квант/(см²·с). Прочие виды ICRP (`AP`/`PA`/`ROT`)
    /// не заводятся тем же решением.
    /// </summary>
    public static class DoseRateGeometry
    {
        /// <summary>Шагов квадратуры вдоль оси (и по каждой оси кюветы).</summary>
        const int AxialSteps = 2000;

        const int BoxSteps = 48;

        /// <summary>
        /// Плотность потока в центре кристалла на один квант, испущенный
        /// источником в 4π, **1/см²**. Отказ — на сцене, которой нельзя
        /// поверить: без кристалла, без объёма пробы, с источником в центре
        /// кристалла. У сцены поля `ISO` — ровно 1 (см. выше).
        /// </summary>
        public static double FluencePerPhoton(GeometryModel model, out string note)
        {
            if (model == null)
            {
                throw new DoseRateRefusalException(DoseRateCoefficients.Text(
                    "DoseRateNoGeometryModel", "Dose rate: the curve has no geometry."));
            }

            // Сцена поля: источника нет, отклик — эффективная площадь на
            // единичный флюенс, и множителю здесь взяться неоткуда. ⛔ Ветка
            // стоит ДО разбора формы источника нарочно: в файле поле записано
            // точкой на расстоянии радиуса сферы (`GeometryScenes.Iso`,
            // `pdistance = R`), и общий ход дал бы 1/(4πR²) — множитель,
            // которого у поля нет, — молча и на четыре порядка.
            if (ResponseMatrix.NormalizationOf(model) == ResponseMatrixNormalization.PerUnitFluence)
            {
                note = string.Format(CultureInfo.InvariantCulture,
                    "iso field: response per unit fluence (cm²), G = 1; sphere R = {0:f1} cm (answer independent of it)",
                    model.FieldRadius / GeometryModel.MmPerCm);
                return 1.0;
            }

            // Сантиметры — на той же границе, что у симулятора (`InCentimeters`).
            GeometryModel g = model.InCentimeters();

            double hc;
            double ax = 0.0, ay = 0.0;
            if (g.Shape == CrystalShape.Box)
            {
                g.CrystalBoxInScene(out ax, out ay, out hc);
            }
            else
            {
                hc = g.CrystalHeight;
            }

            if (!(hc > 0.0))
            {
                throw new DoseRateRefusalException(DoseRateCoefficients.Text(
                    "DoseRateNoCrystal", "Dose rate: the geometry has a crystal of zero depth."));
            }

            // Торцевые толщины обвязки — с разворотом при боковой постановке,
            // как в `EfficiencySimulator.Build`. Оправа стоит ЗА кристаллом
            // (умолчание симулятора) и на расстояния не влияет.
            double tfr = g.FrontReflectorThickness, tsr = g.SideReflectorThickness;
            double tfc = g.FrontCladdingThickness, tsc = g.SideCladdingThickness;
            double tfg = Math.Max(0.0, g.FrontGapThickness);
            double tsg = Math.Max(0.0, g.SideGapThickness);
            if (g.Facing == GeometryDetectorFacing.Side)
            {
                double t = tfr; tfr = tsr; tsr = t;
                t = tfc; tfc = tsc; tsc = t;
                t = tfg; tfg = tsg; tsg = t;
            }

            double zFace = -(tfr + tfg + tfc);
            double zc = 0.5 * hc;                     // центр кристалла

            switch (g.SourceType)
            {
                case GeometrySourceType.Point:
                {
                    double r = zc - (zFace - g.PointDistance);
                    if (!(r > 0.0))
                    {
                        throw new DoseRateRefusalException(DoseRateCoefficients.Text(
                            "DoseRateSourceInCrystal",
                            "Dose rate: the point source sits at the crystal centre — the fluence is undefined."));
                    }

                    note = string.Format(CultureInfo.InvariantCulture,
                        "point: R = {0:f3} cm to crystal centre, G = 1/(4πR²)", r);
                    return 1.0 / (4.0 * Math.PI * r * r);
                }

                case GeometrySourceType.Cylinder:
                {
                    double rOut = 0.5 * g.BeakerDiameter;
                    double rIn = Math.Max(0.0, rOut - g.BeakerSideWallThickness);
                    double zTop = zFace - g.BeakerToDetectorDistance - g.BeakerEndWallThickness;
                    double zBottom = zTop - g.SourceHeight;
                    double volume, integral;
                    Revolution(0.0, rIn, zBottom, zTop, zc, out volume, out integral);
                    return Finish(volume, integral, out note,
                        string.Format(CultureInfo.InvariantCulture,
                            "cylinder: r ≤ {0:f3} cm, z {1:f3}…{2:f3} cm from crystal centre",
                            rIn, zBottom - zc, zTop - zc));
                }

                case GeometrySourceType.Marinelli:
                {
                    double rh = 0.5 * g.MarinelliHoleDiameter;
                    double ths = g.MarinelliHoleSideThickness;
                    double the = g.MarinelliHoleEndWallThickness;
                    double rOut = Math.Max(0.5 * g.MarinelliBeakerDiameter, rh + ths + 0.1);
                    double rSrcOut = Math.Max(rh + ths, rOut - g.MarinelliSideThickness);
                    double hs = g.MarinelliSourceHeight;
                    double hh = g.MarinelliHoleHeight;
                    double zCeiling = zFace - g.MarinelliToDetectorDistance;
                    double cap = Math.Max(0.0, hs - hh);
                    double zSrc0 = zCeiling - the - cap;

                    double v1, i1, v2, i2;
                    // Проба над потолком колодца (под детектором) …
                    Revolution(0.0, rh + ths, zSrc0, zCeiling - the, zc, out v1, out i1);
                    // … и кольцо вокруг колодца.
                    Revolution(rh + ths, rSrcOut, zSrc0, zSrc0 + hs, zc, out v2, out i2);
                    return Finish(v1 + v2, i1 + i2, out note,
                        string.Format(CultureInfo.InvariantCulture,
                            "marinelli: ring {0:f3}…{1:f3} cm, height {2:f3} cm, cap {3:f3} cm",
                            rh + ths, rSrcOut, hs, cap));
                }

                default:
                {
                    // Прямоугольная кювета (`GeometrySourceType.Box`).
                    double axOut = 0.5 * g.BoxSourceX, ayOut = 0.5 * g.BoxSourceY;
                    double axIn = Math.Max(0.0, axOut - g.BoxSideWallThickness);
                    double ayIn = Math.Max(0.0, ayOut - g.BoxSideWallThickness);
                    double zTop = zFace - g.BoxToDetectorDistance - g.BoxEndWallThickness;
                    double zBottom = zTop - g.BoxSourceHeight;
                    double volume, integral;
                    Box(axIn, ayIn, zBottom, zTop, zc, out volume, out integral);
                    return Finish(volume, integral, out note,
                        string.Format(CultureInfo.InvariantCulture,
                            "box: ±{0:f3} × ±{1:f3} cm, z {2:f3}…{3:f3} cm from crystal centre",
                            axIn, ayIn, zBottom - zc, zTop - zc));
                }
            }
        }

        /// <summary>
        /// Потолок эффективной площади сцены поля, см²: площадь проекции
        /// сферы, описанной вокруг обвязанного детектора (половина диагонали
        /// того же цилиндра, из которого `GeometryScenes.MinFieldRadiusMm`
        /// берёт нижнюю границу радиуса поля). По Коши средняя проекция
        /// выпуклого тела S/4 не больше проекции описанной сферы πr², а
        /// собрать квантов больше, чем на него падает, детектор не может;
        /// у Ø63×63 NaI в обвязке это 73.8 см² при S/4 = 46.8 голого кристалла.
        /// </summary>
        public static double ProjectedAreaBoundCm2(GeometryModel model)
        {
            if (model == null)
            {
                throw new DoseRateRefusalException(DoseRateCoefficients.Text(
                    "DoseRateNoGeometryModel", "Dose rate: the curve has no geometry."));
            }

            double halfDiagonalCm = GeometryScenes.MinFieldRadiusMm(model)
                                    / GeometryScenes.FieldRadiusMargin / GeometryModel.MmPerCm;
            return Math.PI * halfDiagonalCm * halfDiagonalCm;
        }

        static double Finish(double volume, double integral, out string note, string what)
        {
            if (!(volume > 0.0))
            {
                throw new DoseRateRefusalException(DoseRateCoefficients.Text(
                    "DoseRateNoSampleVolume",
                    "Dose rate: the sample in the geometry has no volume — there is nothing to average the fluence over."));
            }

            double g = integral / volume;
            note = string.Format(CultureInfo.InvariantCulture,
                "{0}, V = {1:f3} cm³, G = <1/(4πr²)> = {2:e4} 1/cm²", what, volume, g);
            return g;
        }

        /// <summary>
        /// Тело вращения ρ ∈ [ρ0, ρ1], z ∈ [z0, z1] вокруг оси сцены; точка
        /// отсчёта на оси в z = zc. Объём и ∫ dV / (4π r²): по ρ — аналитически
        /// (∫ ρ dρ / (2(ρ² + d²)) = ¼ ln((ρ1² + d²)/(ρ0² + d²))), по z —
        /// серединная квадратура.
        /// </summary>
        static void Revolution(double r0, double r1, double z0, double z1, double zc,
                               out double volume, out double integral)
        {
            volume = 0.0;
            integral = 0.0;
            if (!(r1 > r0) || !(z1 > z0))
            {
                return;
            }

            volume = Math.PI * (r1 * r1 - r0 * r0) * (z1 - z0);
            double dz = (z1 - z0) / AxialSteps;
            double sum = 0.0;
            for (int i = 0; i < AxialSteps; i++)
            {
                double d = z0 + (i + 0.5) * dz - zc;
                double d2 = d * d;
                sum += 0.25 * Math.Log((r1 * r1 + d2) / (r0 * r0 + d2));
            }

            integral = sum * dz;
        }

        /// <summary>Кювета |x| ≤ ax, |y| ≤ ay, z ∈ [z0, z1]; та же величина квадратурой по трём осям.</summary>
        static void Box(double ax, double ay, double z0, double z1, double zc,
                        out double volume, out double integral)
        {
            volume = 0.0;
            integral = 0.0;
            if (!(ax > 0.0) || !(ay > 0.0) || !(z1 > z0))
            {
                return;
            }

            volume = 4.0 * ax * ay * (z1 - z0);
            int nz = AxialSteps / 4;
            double dx = 2.0 * ax / BoxSteps, dy = 2.0 * ay / BoxSteps, dz = (z1 - z0) / nz;
            double sum = 0.0;
            for (int k = 0; k < nz; k++)
            {
                double d = z0 + (k + 0.5) * dz - zc;
                double d2 = d * d;
                for (int i = 0; i < BoxSteps; i++)
                {
                    double x = -ax + (i + 0.5) * dx;
                    for (int j = 0; j < BoxSteps; j++)
                    {
                        double y = -ay + (j + 0.5) * dy;
                        sum += 1.0 / (4.0 * Math.PI * (x * x + y * y + d2));
                    }
                }
            }

            integral = sum * dx * dy * dz;
        }
    }
}
