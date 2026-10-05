using BecquerelMonitor.Properties;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Serialization;

namespace BecquerelMonitor
{
    public abstract class FwhmCalibration
    {
        public const int GaussianPeakType = 0;
        public const int ExpGaussExpPeakType = 1;
        public const int VoigtPeakType = 2;

        public static bool IsSupportedPeakType(int peakType)
        {
            return peakType == GaussianPeakType ||
                peakType == ExpGaussExpPeakType ||
                peakType == VoigtPeakType;
        }

        // Enum all siblings of abstract class FwhmCalibration
        public enum FwhmCalibrationCurve
        {
            [XmlEnum(Name = "Simple Square root")]
            SimpleSqrtFwhmCalibration,

            [XmlEnum(Name = "Square root polynomial")]
            SqrtFwhmCalibration,

            // V2: степенная FWHM = a * ch^p. Заведена ТРЕТЬЕЙ, а не взамен:
            // корпус измерил, что показатель у сцинтилляторов выше половины,
            // но у германия ниже, и одной формой оба класса не описать.
            [XmlEnum(Name = "Power law")]
            PowerFwhmCalibration
        }

        /// <summary>
        /// Умолчание модели разрешения по настройкам поиска пиков. Отдаёт
        /// <c>null</c>, когда его построить нельзя, — и ПРИЧИНУ этого больше не
        /// теряет: см. перегрузку с <c>out refusal</c>.
        /// </summary>
        public static SimpleSqrtFwhmCalibration DefaultCalibration(FWHMPeakDetectionMethodConfig fwhmConfig, EnergyCalibration energyCalibration)
        {
            string refusal;
            return DefaultCalibration(fwhmConfig, energyCalibration, out refusal);
        }

        /// <summary>
        /// То же, но с ПРИЧИНОЙ ОТКАЗА СЛОВАМИ (`A235`, 05.09.2026).
        ///
        /// ⛔ Здесь стоял авторский <c>TODO</c> «может, чтобы избежать null,
        /// влепить некую дефолтную кривую по аналогии с y = x». Подставлять её
        /// НЕЛЬЗЯ, и это не осторожность, а арифметика: кривая разрешения
        /// задаёт ширину окна поиска пиков и форму образа в полноспектральном
        /// разборе, то есть выдуманная кривая даёт ЧИСЛА, неотличимые от
        /// измеренных. Отказ остаётся отказом; чинится не он, а его немота.
        ///
        /// ⛔ Немота стоила падения: <c>null</c> расползался по документу без
        /// единого слова и всплывал <c>NullReferenceException</c> в чужом месте
        /// (`A212`, импорт через SpecUtils). У отказа обязан быть читатель
        /// (`A140`, `A22`, `A95`), и <c>refusal</c> заведён затем, чтобы этот
        /// читатель у него был: обе двери импорта его цитируют.
        ///
        /// ⚠ Причина называет ТРИ ЧИСЛА настроек, а не «не получилось»:
        /// прямая через (0, <c>FWHM_AT_0</c>) и (<c>Ch_Fwhm</c>,
        /// <c>Width_Fwhm</c>) не проходит <c>PerformCalibration</c> ровно
        /// тогда, когда ширина вдоль шкалы УБЫВАЕТ
        /// (<c>SimpleSqrtFwhmCalibration.CheckCalibration</c>). Без этих трёх
        /// чисел человеку негде искать причину: на форме их нет вовсе, они
        /// приходят из <c>config\device\*.xml</c>.
        ///
        /// ⚠ Прежняя подпись сохранена и ведёт сюда же.
        ///
        /// ⚠ СКОЛЬКО ИХ ОСТАЛОСЬ — СЧЁТОМ ПО ДЕРЕВУ, а не по памяти (`A240`,
        /// полоса F62, 06.09.2026; счёт по ВЫЗОВАМ, не по строкам, — аргументы
        /// бывают на двух строках). В приложении было 9 вызовов старой
        /// подписью, стало 5; новой — было 2, стало 6. Из оставшихся пяти
        /// четыре оставлены НАМЕРЕННО, и у каждого причина написана на месте:
        /// три конструктора копии и заготовки (<c>DeviceConfigInfo</c>,
        /// <c>FWHMPeakDetectionMethodConfig</c> дважды) и
        /// <c>DocEnergySpectrum.CreateResultData</c>, у которого оба пути уже
        /// имеют своего читателя. Пятый — <c>DocumentManager.CheckDocument</c>.
        /// Довод у всех четырёх ОДИН и проверяемый: метод НИЧЕГО НЕ МЕНЯЕТ и
        /// зависит только от трёх чисел настроек и энергетической кривой,
        /// поэтому причину спрашивает заново тот, у кого есть человек, — так и
        /// устроен <c>DocumentManager.WhyNoFwhmCalibration</c>.
        ///
        /// ⚠ Сверх приложения старой подписью зовут 26 мест оснастки (пробы и
        /// <c>tools\pie</c>) — там читателя нет и не нужно, они мерят.
        /// </summary>
        public static SimpleSqrtFwhmCalibration DefaultCalibration(FWHMPeakDetectionMethodConfig fwhmConfig, EnergyCalibration energyCalibration, out string refusal)
        {
            refusal = null;
            SimpleSqrtFwhmCalibration simpleSqrtFwhmCalibration = new SimpleSqrtFwhmCalibration();
            CalibrationPeak peak = new CalibrationPeak
            {
                Channel = 0,
                Energy = energyCalibration.ChannelToEnergy(0),
                FWHM = fwhmConfig.FWHM_AT_0
            };
            simpleSqrtFwhmCalibration.CalibrationPeaks.Add(peak);

            peak = new CalibrationPeak
            {
                Channel = (int)fwhmConfig.Ch_Fwhm,
                Energy = energyCalibration.ChannelToEnergy(fwhmConfig.Ch_Fwhm),
                FWHM = fwhmConfig.Width_Fwhm
            };
            simpleSqrtFwhmCalibration.CalibrationPeaks.Add(peak);

            // set default peak shape as gauss
            simpleSqrtFwhmCalibration.PeakType = GaussianPeakType;
            simpleSqrtFwhmCalibration.ExpGaussExpLeftTail = 1.0;
            simpleSqrtFwhmCalibration.ExpGaussExpRightTail = 1.0;
            simpleSqrtFwhmCalibration.VoigtSigma = 1.0;
            simpleSqrtFwhmCalibration.VoigtGamma = 1.0;

            if (simpleSqrtFwhmCalibration.PerformCalibration(energyCalibration.MaxChannels()))
            {
                return simpleSqrtFwhmCalibration;
            }
            else
            {
                // ⛔ Кривая НЕ ВЫДУМЫВАЕТСЯ (см. заглавие метода). Возвращается
                //    null, но теперь вместе с причиной, которую есть кому
                //    прочесть.
                // ⛔ КУЛЬТУРА ИНВАРИАНТНАЯ, А НЕ ПОТОКА (`A242`, правило Amber
                //    05.09.2026: разделитель дробной части ВСЕГДА ТОЧКА).
                //    Инвариантной культурой печатаются ЧИСЛА, а текст остаётся
                //    из ресурса, то есть переведённым.
                //    ⚠ Измерено 05.09.2026 (`ImportEmptyConfigProbeF23`, плечо
                //    «УМОЛЧАНИЕ НЕ СТРОИТСЯ»): на потоке ru-RU без подпорки
                //    `MainForm.cs:158-160` строка выходила «ПШПВ 40,5 … ПШПВ 1,25»
                //    — с запятой. Подпорка держит точку только у окон; здесь же
                //    читатель бывает и безоконный, и вообще любой, а число обязано
                //    печататься точкой само по себе, а не по чужой милости.
                refusal = string.Format(CultureInfo.InvariantCulture,
                                        Resources.ERRFwhmDefaultNotMonotonic,
                                        fwhmConfig.FWHM_AT_0,
                                        fwhmConfig.Ch_Fwhm,
                                        fwhmConfig.Width_Fwhm);
                return null;
            }
        }

        public abstract double ChannelToFwhm(double channel);

        public abstract double FwhmToChannel(double fwhm);

        [XmlArrayItem("Peak")]
        public abstract List<CalibrationPeak> CalibrationPeaks { get; set; }

        [XmlArrayItem("Coefficient")]
        public abstract double[] Coefficients { get; set; }

        public abstract bool PerformCalibration(int maxchannels);

        /// <summary>
        /// Почему последняя <see cref="PerformCalibration"/> отказала (`AMBER189`,
        /// 05.10.2026). Нужен виду калибровки: отказ «ширина ≤ 0 на нижних каналах»
        /// получает ПОДСКАЗКУ (решение Amber 05.10.2026, вопросником, дословно:
        /// «Отвергать с подсказкой (Рекомендую)»), прочие — прежнее окно.
        /// Состояние расчёта, не кривой: в файл не пишется.
        /// </summary>
        public enum FwhmCheckResult
        {
            Ok,
            /// <summary>Коэффициенты не числа или бесконечны (две опоры на одном канале).</summary>
            NotFinite,
            /// <summary>Ширина убывает вдоль шкалы, мало опор, степень вне (0, 1).</summary>
            Rejected,
            /// <summary>Кривая не убывает, но на нижних каналах её ширина ≤ 0.</summary>
            NonPositiveWidth
        }

        [XmlIgnore]
        public FwhmCheckResult LastCheck { get; protected set; } = FwhmCheckResult.Ok;

        /// <summary>
        /// Все коэффициенты — конечные числа (`AMBER189` (б)). Сравнение с NaN
        /// ложно, поэтому перебор каналов «ширина не убывает» NaN ПРОПУСКАЛ: две
        /// опоры на одном канале давали кривую из бесконечностей, ПШПВ = NaN на
        /// всех каналах и `INF`/`NaN` в XML. У шкалы энергии такой заслон есть.
        /// </summary>
        protected static bool CoefficientsFinite(double[] c, int minLength)
        {
            if (c == null || c.Length < minLength) return false;
            foreach (double v in c)
            {
                if (double.IsNaN(v) || double.IsInfinity(v)) return false;
            }
            return true;
        }

        public abstract FwhmCalibration Clone();

        /// <summary>
        /// Пересчёт коэффициентов кривой под другой масштаб канала:
        /// <c>mul</c> — во сколько раз новый канал ШИРЕ старого. Реализация
        /// зависит от формы кривой, поэтому метод абстрактный, а не общий:
        /// формула корневых для степенной неверна, и наоборот.
        /// </summary>
        public abstract void RescaleCoefficients(double mul);

        /// <summary>
        /// То же со СДВИГОМ ЦЕНТРА канала (`AMBER171`, остаток, 05.10.2026): новый
        /// канал j лежит на старом x = mul·j + shift, ширина F'(j) = F(mul·j + shift)/mul.
        /// При смене числа каналов shift = (mul − 1)/2 (соглашение «номер канала —
        /// центр», `AMBER73`). Корневые формы переносятся ТОЧНО (переопределено);
        /// степенная сдвигом не замкнута (a·(mul·j + s)^p не есть a'·j^p), у неё
        /// остаётся прежний множитель — см. <see cref="PowerFwhmCalibration"/>.
        /// Однопараметрический <see cref="RescaleCoefficients(double)"/> оставлен
        /// как был: им пробы мерят чистое растяжение шкалы x = k·x' без сдвига.
        /// </summary>
        public virtual void RescaleCoefficients(double mul, double shift)
        {
            RescaleCoefficients(mul);
        }

        /// <summary>
        /// Кривая ПШПВ под другое число каналов.
        ///
        /// ⚠ `S54`: раньше здесь пересчитывались ТОЛЬКО опорные точки, а
        /// результат <see cref="PerformCalibration"/> отбрасывался — и у
        /// кривой БЕЗ опорных точек (а корпус пишет именно такие,
        /// <c>&lt;CalibrationPeaks /&gt;</c>) наружу МОЛЧА уходила кривая для
        /// ПРЕЖНЕГО числа каналов. Хуже: неудачная подгонка успевала записать
        /// в клон мусор от решателя — <c>PerformCalibration</c> кладёт ответ
        /// решателя ДО проверки, — то есть «ничего не поменялось» было не
        /// худшим исходом.
        ///
        /// Теперь: есть точки и подгонка прошла — берём подгонку, как раньше;
        /// иначе считаем коэффициенты ТОЧНО, от ИСХОДНОЙ кривой (клон к этому
        /// моменту мог быть испорчен). Пересчёт точный и приближением не
        /// является: и канал, и ширина меряются в каналах, значит
        /// F'(ch') = F(ch'·mul)/mul.
        ///
        /// ⚠ (`AMBER171`, 05.10.2026) Со сдвигом центра канала: при соглашении
        /// «номер канала — центр» новый канал ch' лежит на старом ch'·mul + (mul − 1)/2.
        /// Опорные точки ниже переносятся с этим сдвигом, ветвь без точек — через
        /// <see cref="RescaleCoefficients(double, double)"/>: корневые формы точно
        /// (прежде без сдвига, ≈ (mul − 1)/(4·ch) по ширине, 0.06 % на старом канале
        /// 3000 при 8192 → 1024), степенная — прежним множителем (сдвигом форма не
        /// замкнута, ошибка ≈ p·(mul − 1)/(2·ch_стар)). Энергетическая шкала
        /// пересчитывается со сдвигом (<c>SpectrumAriphmetics.RescaleCalibration</c>).
        /// </summary>
        public FwhmCalibration RecalcWithNewChannelNum(int oldchannelnum, int newchannelnum)
        {
            FwhmCalibration newFwhmCalibration = Clone();
            double mul = (double)oldchannelnum / (double)newchannelnum;
            foreach (CalibrationPeak peak in newFwhmCalibration.CalibrationPeaks)
            {
                // Round the channel instead of truncating, and keep FWHM as double:
                // the old (int) cast turned e.g. FWHM 12.7/8 into 1 (and anything < mul
                // into 0), so the curve was fitted to ruined points.
                // (`AMBER171`, 05.10.2026) номер канала — ЦЕНТР канала (`AMBER73`):
                // старый n = m·j + (m − 1)/2, то есть j = (n + ½)/m − ½. Прежнее n/m
                // сдвигало опору на (m − 1)/2 старого канала (8192 → 1024: 3.5).
                peak.Channel = (int)Math.Round((peak.Channel + 0.5) / mul - 0.5);
                peak.FWHM = peak.FWHM / mul;
            }

            if (newFwhmCalibration.CalibrationPeaks.Count >= MinPeaksRequirement()
                && newFwhmCalibration.PerformCalibration(newchannelnum))
            {
                return newFwhmCalibration;
            }

            // Точек нет или подгонка не прошла: коэффициенты берём у СЕБЯ, а не
            // у клона, и пересчитываем по форме кривой.
            newFwhmCalibration.Coefficients = (double[])this.Coefficients.Clone();
            newFwhmCalibration.RescaleCoefficients(mul, (mul - 1.0) / 2.0);
            return newFwhmCalibration;
        }

        public List<CalibrationPeak> ClonePeaks()
        {
            return new List<CalibrationPeak>(CalibrationPeaks);
        }

        public abstract string GetFormula();

        public override abstract string ToString();

        public abstract bool NotCalibrated();

        public abstract int MinPeaksRequirement();

        public abstract int PeakType { get; set; }

        public abstract double ExpGaussExpLeftTail { get; set; }

        public abstract double ExpGaussExpRightTail { get; set; }

        public abstract double VoigtSigma { get; set; }

        public abstract double VoigtGamma { get; set; }

        public abstract double GaussianChi2Total { get; set; }

        public abstract double ExpGaussExpChi2Total { get; set; }

        public abstract double VoigtChi2Total { get; set; }

        public abstract double Chi2pNdp {  get; set; }
    }
}
