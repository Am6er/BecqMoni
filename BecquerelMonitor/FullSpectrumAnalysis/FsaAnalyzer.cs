using System;
using System.Collections.Generic;
using System.Linq;
using BecquerelMonitor.Utils;

namespace BecquerelMonitor.FullSpectrumAnalysis
{
    /// <summary>
    /// Полноспектральная декомпозиция (full-spectrum analysis).
    ///
    /// В отличие от поиска пиков вопрос ставится не «есть ли пик на 583 кэВ», а
    /// «какая смесь откликов Th-232, Ra-226, K-40... и фона лучше всего
    /// объясняет весь спектр сразу». Модель:
    ///
    ///     S(i) ~ sum_j A_j * F_j(i; a, b) + B(i) + C(i)
    ///
    /// F_j — образ компонента (сумма профилей единичной площади по таблице
    /// линий: положение из энергетической калибровки, ширина и форма из
    /// ПШПВ-калибровки, вес из выхода на распад родителя и кривой
    /// эффективности);
    /// a, b — дрейф шкалы (усиление и ноль), общие нелинейные параметры,
    /// перебираются сеткой; B — измеренный фон, вычитается с коэффициентом 1;
    /// C — континуум: неотрицательный кусочно-линейный базис («шапки») с шагом
    /// узлов не меньше 4 ПШПВ, чтобы шапка не могла съесть одиночный пик.
    ///
    /// Линейная часть — взвешенный NNLS (активное множество на нормальных
    /// уравнениях) с двумя итерациями хуберовского перевзвешивания, чтобы
    /// систематические расхождения формы не утаскивали оценки. После первого
    /// решения компоненты со значимостью ниже порога выбрасываются и модель
    /// пересчитывается заново.
    /// </summary>
    public sealed class FsaAnalyzer
    {
        /// <summary>Модель континуума.</summary>
        public enum ContinuumMode
        {
            /// <summary>Континуум оценивается SNIP-ом заранее и вычитается.</summary>
            Snip,
            /// <summary>Континуум входит в общую систему уравнений («шапки»).</summary>
            Spline
        }

        /// <summary>
        /// Считать по всему спектру, не обрезая по MinEnergy/MaxEnergy: от
        /// первого канала до предпоследнего (последний — канал переполнения).
        /// Разложение должно быть нарисовано на всём спектре: обрыв стека на
        /// границе диапазона поиска пиков читается как дефект отрисовки.
        /// Цена известна и измерена: на 8192-канальном NaI с порогом у самого
        /// начала шкалы χ²/ndf 20.7 (от 40 кэВ) против 28.0 (от нулевого
        /// канала) — шум порога модели описывать нечем, и его берёт на себя
        /// континуум.
        /// </summary>
        public bool FitWholeSpectrum { get; set; }

        public ContinuumMode Mode { get; set; }

        public double MinEnergy { get; set; }

        public double MaxEnergy { get; set; }

        /// <summary>Доля погрешности континуума в весах.</summary>
        public double Xi { get; set; }

        /// <summary>Порог хуберовского перевзвешивания, в сигмах. 0 — выключено.</summary>
        public double HuberM { get; set; }

        /// <summary>Порог значимости для отсева перед вторым проходом. 0 — без отсева.</summary>
        public double RefitZ { get; set; }

        public double GainRange { get; set; }

        public int GainSteps { get; set; }

        /// <summary>Диапазон сдвига нуля шкалы, кэВ.</summary>
        public double OffsetRangeKev { get; set; }

        public int OffsetSteps { get; set; }

        /// <summary>
        /// Добавлять образы обратного рассеяния, выведенные из найденного
        /// состава. Мерено дважды, и числа принадлежат РАЗНОМУ коду:
        ///
        ///   * харнесс `tools/pie`, 46 спектров, состав цепочками:
        ///     recall 86 % → 89 %, Σχ²/ndf 559 → 547;
        ///   * этот код, весь корпус, состав дочерними (13.08.2026,
        ///     `CorpusFsaProbe --no-backscatter`): понятная часть Σχ²/ndf
        ///     99.7 → 95.9, непонятная 716.6 → 685.4 (−4.4 %), recall
        ///     70 % → 71 %, фантомов 26 → 25. Крупнее всего на
        ///     `ASN16_Cs137` (−13 %) и `OBS_Background` (−15 %).
        ///
        /// Сравнивать две строки между собой нельзя — разный код, разный
        /// корпус и разное правило счёта; каждая верна для своего.
        /// </summary>
        public bool Backscatter { get; set; }

        /// <summary>
        /// Матрица отклика геометрии, если она посчитана и годна. С ней образ
        /// компонента — это сумма ОТКЛИКОВ его линий, то есть пик вместе с
        /// комптоновским плато, краем и пиками вылета; без неё — только пики, а
        /// всё плато достаётся свободной подложке.
        ///
        /// Матрица уже несёт эффективность (она посчитана как доля на квант,
        /// испущенный источником), поэтому кривая эффективности к её весам
        /// ВТОРОЙ раз не применяется.
        /// </summary>
        public EfficiencyMaker.ResponseMatrix ResponseMatrix { get; set; }

        /// <summary>
        /// Вносить каскадное суммирование (<see cref="FsaCascadeSummer"/>):
        /// множитель CF на площадь пика и отдельные сумм-пики. Работает только
        /// вместе с матрицей отклика — эффективности берутся из неё.
        ///
        /// Выключатель нужен не для пользователя, а для измерения: без него
        /// «с поправкой» и «без поправки» на одном спектре не снять, а
        /// утверждение о пользе, не подкреплённое таким A/B, уже однажды
        /// оказалось непроверяемым (S13, порог континуума).
        /// </summary>
        public bool CascadeSumming { get; set; }

        /// <summary>
        /// Ставить ли СУММ-ПИКИ (E_i + E_j) — вторая половина суммирования,
        /// отдельно от множителя на пик. Две половины делают разное: множитель
        /// срезает пик, сумм-пик добавляет структуру там, где её в образе не
        /// было, — и мерить их надо порознь, иначе выигрыш одной спишется на
        /// проигрыш другой.
        /// </summary>
        public bool CascadeSumPeaks { get; set; }

        /// <summary>
        /// Идёт ли сумм-КОНТИНУУМ (S19) в подслой отрисовки. В МОДЕЛЬ он идёт
        /// всегда — это вопрос только про штриховку внутри ленты нуклида.
        ///
        /// Техническая преграда снята: подслой строится тем же кодом, что и
        /// лента, и выше неё не поднимается по построению (S37). Осталась
        /// смысловая, и она не про счёт: подслой читается как «вот эти пики —
        /// суммы», а широкая полка, нарисованная так же, читалась бы как пик,
        /// которого нет. Поэтому умолчание — только пики; поле оставлено
        /// ручкой для проб, в UI не выводится (как
        /// <see cref="ResponseContinuumTrustFloorKev"/>).
        /// </summary>
        public bool SumLayerIncludesContinuum;

        /// <summary>
        /// Добавлять образ случайных наложений (pile-up) — автосвёртку самого
        /// спектра со свободной амплитудой. См.
        /// <see cref="BuildPileUpComponent"/>: это НЕ каскад, а свойство
        /// загрузки, и без него пик 662+662 у Cs-137 модели описать нечем.
        ///
        /// Цена измерена по корпусу 13.08.2026 (`CorpusFsaProbe --no-pileup`) и
        /// она МАЛА: χ²/ndf меняется больше чем на 0.1 % только у 4 спектров из
        /// 61, крупнейший сдвиг −0.88 % — как раз на `ASN16_Cs137`, то есть
        /// направление верное. Но объяснить этот спектр наложения не могут: он
        /// остаётся худшим в корпусе (χ²/ndf 45.2) и с ними, и без них. Причина
        /// в другом — горб 662+662 модель ставит по сумме ЭНЕРГИЙ, а измерен он
        /// по сумме СВЕТА и стоит на ~23 кэВ выше (TODO S20).
        /// </summary>
        public bool PileUp { get; set; }

        /// <summary>
        /// Вещество кристалла в именах таблицы кривых света («CsI:Tl», «NaI:Tl»).
        /// Нужно каскадному суммированию: сумм-пик встаёт по сумме СВЕТА, а не
        /// энергий (S20). Пусто — суммы ставятся по энергии, как до 13.08.2026.
        ///
        /// Задаётся снаружи вместе с матрицей: у матрицы вещества нет, от
        /// геометрии в ней остаётся только необратимый отпечаток. Берётся тем
        /// же путём, что у симулятора —
        /// <see cref="EfficiencyMaker.EfficiencySimulator.ScintillatorNameOf(EfficiencyMaker.GeometryModel)"/>.
        /// </summary>
        public string ScintillatorMaterial { get; set; }

        /// <summary>Живёт один разбор: матрицу могли подменить между вызовами.</summary>
        FsaCascadeSummer cascade;

        /// <summary>
        /// Поправка хоть где-то СРАБОТАЛА — не «включена», а изменила образ.
        /// Это разные вещи: суммирователь молча возвращает единицы, когда у
        /// нуклидов состава нет каскадов вовсе (Cs-137, K-40), и пометка
        /// «с суммированием» на таком спектре была бы враньём.
        /// </summary>
        bool cascadeApplied;

        /// <summary>
        /// Гистограммы поглощения компонентов, посчитанные ОДИН раз на разбор.
        ///
        /// От узла сетки дрейфа гистограмма не зависит вовсе: усиление и ноль
        /// шкалы накладываются позже, при уширении, а сложение откликов линий,
        /// сумм-пиков и сумм-континуума знает только энергии. До 13.08.2026 её
        /// тем не менее строили заново на каждом из 81 узла — и с приходом
        /// тройных сумм и сумм-континуума (S19) это стало главной статьёй
        /// расхода: корпусный прогон подорожал с 50 с до 236 с.
        ///
        /// Живёт ровно один разбор, как и остальные кэши: между вызовами могли
        /// смениться и матрица, и калибровка.
        /// </summary>
        readonly Dictionary<FsaComponent, Deposit> deposits = new Dictionary<FsaComponent, Deposit>();

        /// <summary>
        /// Гистограмма поглощения компонента и всё, что от неё отрезано:
        /// подпороговый хвост отдельной колонкой и доля сумм для подслоя.
        /// Ножи применены ДО кэширования, поэтому все три части согласованы
        /// поканально при любом числе обращений.
        /// </summary>
        sealed class Deposit
        {
            public double[] Values;
            public double[] Tail;
            public double[] SumPart;
            public bool CascadeApplied;
        }

        public FsaAnalyzer()
        {
            this.Mode = ContinuumMode.Spline;
            this.FitWholeSpectrum = true;
            this.MinEnergy = 40.0;
            this.MaxEnergy = 2800.0;
            this.Xi = 0.03;
            this.HuberM = 3.0;
            this.RefitZ = 3.0;
            this.GainRange = 0.008;
            this.GainSteps = 9;
            this.OffsetRangeKev = 3.0;
            this.OffsetSteps = 9;
            this.Backscatter = true;
            this.CascadeSumming = true;
            this.CascadeSumPeaks = true;
            this.PileUp = true;
        }

        /// <summary>
        /// Разложить спектр. Возвращает null, если разложение невозможно:
        /// нет калибровок, вырожденный диапазон или пустая библиотека.
        /// </summary>
        public FsaResult Analyze(
            EnergySpectrum spectrum,
            EnergySpectrum backgroundSpectrum,
            FwhmCalibration fwhmCalibration,
            List<FsaComponent> originalLibrary,
            FsaEfficiency efficiency)
        {
            if (spectrum == null || spectrum.Spectrum == null || fwhmCalibration == null
                || spectrum.EnergyCalibration == null || originalLibrary == null || originalLibrary.Count == 0)
            {
                return null;
            }

            int channels = spectrum.NumberOfChannels;
            if (channels < 32)
            {
                return null;
            }

            // Кэши уширения живут ровно один разбор: калибровку могли изменить
            // на месте, и тогда ссылка та же, а числа другие.
            this.depositChannels = null;
            this.depositChannelsCalibration = null;
            this.kernelBank = null;
            this.deposits.Clear();

            // Каскадные поправки — на ту же матрицу, что и образы: она даёт им
            // обе эффективности. Кэш поправок внутри живёт один разбор, потому
            // что матрица между вызовами могла смениться.
            this.cascade = this.CascadeSumming
                ? FsaCascadeSummer.Create(this.ResponseMatrix, this.ScintillatorMaterial)
                : null;
            this.cascadeApplied = false;

            EnergyCalibration calibration = spectrum.EnergyCalibration;
            int chLo = this.FitWholeSpectrum
                ? 0
                : ClampChannel(EnergyToChannelSafe(calibration, this.MinEnergy, channels), channels);
            int chHi = this.FitWholeSpectrum
                ? channels - 1
                : ClampChannel(EnergyToChannelSafe(calibration, this.MaxEnergy, channels), channels);
            if (chHi < chLo)
            {
                int swap = chLo;
                chLo = chHi;
                chHi = swap;
            }

            // Последний канал АЦП — канал переполнения: в него падает всё, что
            // выше шкалы. Образа у такой структуры нет, объяснить её фит не
            // может — верхний канал исключается, когда диапазон дошёл до края.
            if (chHi >= channels - 1)
            {
                chHi = channels - 2;
            }

            if (chHi <= chLo + 10)
            {
                return null;
            }

            double liveTime = spectrum.LiveTime > 0.0 ? spectrum.LiveTime : spectrum.MeasurementTime;
            if (liveTime <= 0.0)
            {
                liveTime = 1.0;
            }

            int[] raw = spectrum.Spectrum;
            double[] y = new double[channels];
            double[] variance = new double[channels];
            double[] backgroundCurve = new double[channels];
            int[] snipContinuum = null;

            EnergySpectrum background = backgroundSpectrum;
            if (background != null && (background.Spectrum == null || background.NumberOfChannels != channels))
            {
                background = null;
            }

            double backgroundScale = 0.0;
            if (background != null)
            {
                double backgroundLive = background.LiveTime > 0.0 ? background.LiveTime : background.MeasurementTime;
                if (backgroundLive > 0.0)
                {
                    backgroundScale = liveTime / backgroundLive;
                }
                else
                {
                    background = null;
                }
            }

            if (this.Mode == ContinuumMode.Snip)
            {
                snipContinuum = Snip(fwhmCalibration, spectrum);
                for (int i = 0; i < channels; i++)
                {
                    double continuum = snipContinuum != null ? snipContinuum[i] : 0.0;
                    y[i] = raw[i] - continuum;
                    double c = this.Xi * continuum;
                    variance[i] = Math.Max(raw[i], 1.0) + c * c;
                }
            }
            else
            {
                for (int i = 0; i < channels; i++)
                {
                    y[i] = raw[i];
                    variance[i] = Math.Max(raw[i], 1.0);
                }
            }

            // Фон измерен и нормирован по живому времени, поэтому он не
            // подбирается, а вычитается: свободный коэффициент фона вырожден с
            // компонентами пробы (комнатный K-40 против K-40 в образце), и NNLS
            // раздувает фон вместо образца.
            if (background != null)
            {
                int[] backgroundSnip = this.Mode == ContinuumMode.Snip ? Snip(fwhmCalibration, background) : null;
                for (int i = 0; i < channels; i++)
                {
                    double full = background.Spectrum[i] * backgroundScale;
                    double value = full;
                    if (backgroundSnip != null)
                    {
                        // в режиме SNIP континуум фона уже сидит внутри оценки
                        // континуума переднего спектра — вычитается только пиковая часть
                        value = Math.Max(0.0, background.Spectrum[i] - backgroundSnip[i]) * backgroundScale;
                    }

                    backgroundCurve[i] = value;
                    y[i] -= value;
                    // Дисперсия — от ПОЛНОГО фона, как в харнессе: шум отсчёта
                    // фона не уменьшается оттого, что его континуум учтён в
                    // другом слагаемом модели.
                    variance[i] += Math.Max(Math.Abs(full) * backgroundScale, backgroundScale * backgroundScale);
                }
            }

            List<double[]> fixedColumns = new List<double[]>();
            if (this.Mode == ContinuumMode.Spline)
            {
                fixedColumns.AddRange(BuildHatBasis(fwhmCalibration, chLo, chHi, channels));
            }

            // Наложения участвуют с САМОГО начала, вместе с сеткой дрейфа:
            // на загруженном спектре они держат целый пик (662+662 у цезия), и
            // подбирать по нему дрейф без образа значит подбирать по мусору.
            // Список вызывающего не трогаем — он его собирал и переиспользует.
            List<FsaComponent> library = originalLibrary;
            if (this.PileUp)
            {
                FsaComponent pileUp = this.BuildPileUpComponent(raw, calibration, chLo, chHi, channels);
                if (pileUp != null)
                {
                    library = new List<FsaComponent>(originalLibrary);
                    library.Add(pileUp);
                }
            }

            double[] baseWeights = new double[channels];
            for (int i = 0; i < channels; i++)
            {
                baseWeights[i] = 1.0 / variance[i];
            }

            // --offset задан в кэВ; в каналы переводится по фактическому наклону
            // шкалы на границах фита, иначе одна и та же величина означала бы
            // разное для 1024- и 8192-канальных приборов.
            double energyLo = calibration.ChannelToEnergy(chLo);
            double energyHi = calibration.ChannelToEnergy(chHi);
            double channelsPerKev = energyHi > energyLo
                ? (chHi - chLo) / (energyHi - energyLo)
                : (chHi - chLo) / Math.Max(1.0, this.MaxEnergy - this.MinEnergy);
            double offsetRangeChannels = this.OffsetRangeKev * channelsPerKev;

            int gainSteps = Math.Max(1, this.GainSteps);
            int offsetSteps = Math.Max(1, this.OffsetSteps);
            double bestGain = 1.0;
            double bestOffset = 0.0;
            double bestChi2 = Double.MaxValue;
            int bestGainIndex = 0;
            int bestOffsetIndex = 0;
            for (int gi = 0; gi < gainSteps; gi++)
            {
                double gain = gainSteps == 1
                    ? 1.0
                    : 1.0 - this.GainRange + 2.0 * this.GainRange * gi / (gainSteps - 1);
                for (int oi = 0; oi < offsetSteps; oi++)
                {
                    double offset = offsetSteps == 1
                        ? 0.0
                        : -offsetRangeChannels + 2.0 * offsetRangeChannels * oi / (offsetSteps - 1);
                    FitResult probe = FitOnce(library, fixedColumns, calibration, fwhmCalibration, efficiency,
                                              gain, offset, chLo, chHi, channels, y, baseWeights, null);
                    if (probe != null && probe.Chi2 < bestChi2)
                    {
                        bestChi2 = probe.Chi2;
                        bestGain = gain;
                        bestOffset = offset;
                        bestGainIndex = gi;
                        bestOffsetIndex = oi;
                    }
                }
            }

            if (bestChi2 == Double.MaxValue)
            {
                return null;
            }

            FitResult best = FitHuber(library, fixedColumns, calibration, fwhmCalibration, efficiency,
                                      bestGain, bestOffset, chLo, chHi, channels, y, variance, baseWeights, null);
            if (best == null)
            {
                return null;
            }

            // Образ обратного рассеяния по найденному составу — до отсева по z:
            // отсев решает, какие компоненты дожили, и решать это надо уже при
            // закрытой области рассеяния.
            List<FsaComponent> working = library;
            if (this.Backscatter)
            {
                List<FsaComponent> derived = BuildBackscatterComponents(best, efficiency);
                if (derived.Count > 0)
                {
                    List<FsaComponent> extended = new List<FsaComponent>(library);
                    extended.AddRange(derived);
                    FitResult refit = FitHuber(extended, fixedColumns, calibration, fwhmCalibration, efficiency,
                                               bestGain, bestOffset, chLo, chHi, channels, y, variance, baseWeights, null);
                    if (refit != null)
                    {
                        best = refit;
                        working = extended;
                    }
                }
            }

            // «Предварительный анализ состава»: второй проход без компонентов,
            // не прошедших порог значимости в первом.
            if (this.RefitZ > 0.0)
            {
                List<FsaComponent> keep = new List<FsaComponent>();
                int total = 0;
                for (int k = 0; k < best.Columns.Count; k++)
                {
                    FsaComponent component = best.Columns[k].Component;
                    if (component == null)
                    {
                        continue;
                    }

                    total++;
                    if (best.Z[k] >= this.RefitZ)
                    {
                        keep.Add(component);
                    }
                }

                if (keep.Count > 0 && keep.Count < total)
                {
                    FitResult refit = FitHuber(working, fixedColumns, calibration, fwhmCalibration, efficiency,
                                               bestGain, bestOffset, chLo, chHi, channels, y, variance, baseWeights, keep);
                    if (refit != null)
                    {
                        best = refit;
                    }
                }
            }

            // Второй круг: форма образа задаётся составом, а состав только что
            // изменился отсевом.
            if (this.Backscatter)
            {
                List<FsaComponent> survivors = new List<FsaComponent>();
                for (int k = 0; k < best.Columns.Count; k++)
                {
                    FsaComponent component = best.Columns[k].Component;
                    if (component != null && !component.Derived)
                    {
                        survivors.Add(component);
                    }
                }

                List<FsaComponent> derived = BuildBackscatterComponents(best, efficiency);
                if (derived.Count > 0)
                {
                    survivors.AddRange(derived);
                    FitResult refit = FitHuber(survivors, fixedColumns, calibration, fwhmCalibration, efficiency,
                                               bestGain, bestOffset, chLo, chHi, channels, y, variance, baseWeights, null);
                    if (refit != null)
                    {
                        best = refit;
                    }
                }
            }

            return BuildResult(best, spectrum, fwhmCalibration, backgroundCurve, snipContinuum,
                               chLo, chHi, channels,
                               bestGain, bestOffset, liveTime, efficiency != null,
                               gainSteps > 1 && (bestGainIndex == 0 || bestGainIndex == gainSteps - 1),
                               offsetSteps > 1 && (bestOffsetIndex == 0 || bestOffsetIndex == offsetSteps - 1));
        }

        FsaResult BuildResult(FitResult fit, EnergySpectrum spectrum, FwhmCalibration fwhmCalibration,
                              double[] backgroundCurve, int[] snipContinuum,
                              int chLo, int chHi, int channels, double gain, double offset, double liveTime,
                              bool efficiencyUsed, bool gainOnEdge, bool offsetOnEdge)
        {
            // Калибровка — та же, по которой строились образы; берётся у
            // спектра, как в Analyze. ПШПВ приходит параметром: у спектра её
            // может не быть вовсе, она достраивается выше.
            EnergyCalibration calibration = spectrum.EnergyCalibration;

            FsaResult result = new FsaResult
            {
                FirstChannel = chLo,
                LastChannel = chHi,
                Chi2Ndf = fit.Chi2Ndf,
                Gain = gain,
                OffsetChannels = offset,
                LiveTime = liveTime,
                EfficiencyUsed = efficiencyUsed,
                ResponseMatrixUsed = fit.FromResponseMatrix,
                CascadeSummingUsed = this.cascadeApplied,
                GainOnGridEdge = gainOnEdge,
                OffsetOnGridEdge = offsetOnEdge,
                Background = backgroundCurve,
                Continuum = new double[channels],
                Model = new double[channels]
            };

            if (snipContinuum != null)
            {
                for (int i = chLo; i <= chHi; i++)
                {
                    result.Continuum[i] = snipContinuum[i];
                }
            }

            double totalPeakCounts = 0.0;
            for (int k = 0; k < fit.Columns.Count; k++)
            {
                FitColumn column = fit.Columns[k];
                double amplitude = fit.Amplitude[k];
                if (column.Component == null)
                {
                    // шапки континуума схлопываются в одну кривую
                    if (amplitude > 0.0)
                    {
                        for (int i = chLo; i <= chHi; i++)
                        {
                            result.Continuum[i] += amplitude * column.Values[i];
                        }
                    }

                    continue;
                }

                if (amplitude <= 0.0)
                {
                    continue;
                }

                double[] curve = new double[channels];
                for (int i = chLo; i <= chHi; i++)
                {
                    curve[i] = amplitude * column.Values[i];
                }

                // Та часть кривой, что пришла от сумм-пиков: та же амплитуда,
                // тот же дрейф — отличается только состав образа.
                double[] sumOnly = this.BuildSumPeakCurve(column.Component, calibration, fwhmCalibration,
                                                          gain, offset, chLo, chHi, channels);
                if (sumOnly != null)
                {
                    for (int i = 0; i < channels; i++)
                    {
                        sumOnly[i] *= amplitude;
                    }
                }

                FsaComponentResult component = new FsaComponentResult
                {
                    Name = column.Component.Name,
                    Kind = column.Component.Kind,
                    Curve = curve,
                    SumPeakCurve = sumOnly,
                    PeakCounts = this.PeakWindowCounts(column.Component, curve, calibration,
                                                       fwhmCalibration, gain, offset,
                                                       chLo, chHi, channels),
                    CountRate = amplitude / liveTime,
                    Z = fit.Z[k]
                };

                result.Components.Add(component);
                if (component.Kind != FsaComponentKind.Nuisance)
                {
                    totalPeakCounts += component.PeakCounts;
                }
            }

            foreach (FsaComponentResult component in result.Components)
            {
                component.SharePercent = component.Kind != FsaComponentKind.Nuisance && totalPeakCounts > 0.0
                    ? 100.0 * component.PeakCounts / totalPeakCounts
                    : 0.0;
            }

            for (int i = chLo; i <= chHi; i++)
            {
                double sum = result.Continuum[i];
                foreach (FsaComponentResult component in result.Components)
                {
                    sum += component.Curve[i];
                }

                result.Model[i] = sum;
            }

            return result;
        }

        /// <summary>
        /// Отсчёты компонента в его ПИКОВЫХ ОКНАХ (±2 ПШПВ вокруг каждой линии
        /// и каждого сумм-пика), а не по всему образу.
        ///
        /// Так решено (S24в, Amber 08.08.2026), потому что по всему образу доля
        /// в «пироге» несравнима между нуклидами: подпороговый континуум образа
        /// отвязывается в свою колонку со своей свободной амплитудой
        /// (<see cref="ResponseContinuumTrustFloorKev"/>), и урезано у разных
        /// нуклидов по-разному — у высокоэнергичных под порогом континуума
        /// больше. Приписать хвост обратно нельзя: его амплитуда своя. Пиковые
        /// окна разрезом НЕ трогаются никогда (они нарочно остаются в основном
        /// образе), поэтому счёт по ним от порога не зависит вовсе и правило
        /// выходит одинаковым для всех строк.
        ///
        /// Число перестало быть «долей спектра» и стало «долей пиковых
        /// отсчётов» — это разные величины, и подпись у него своя.
        ///
        /// Окна берутся тем же ±2 ПШПВ, что и в
        /// <see cref="SplitContinuumBelowTrustFloor"/>: два места, считающие
        /// «пиковое окно» по-своему, однажды разойдутся. Перекрытия
        /// складываются один раз — маска по каналам, а не сумма по линиям.
        /// </summary>
        double PeakWindowCounts(FsaComponent component, double[] curve, EnergyCalibration calibration,
                                FwhmCalibration fwhmCalibration, double gain, double offset,
                                int chLo, int chHi, int channels)
        {
            if (component == null || curve == null)
            {
                return 0.0;
            }

            bool[] inWindow = new bool[channels];
            bool any = false;

            foreach (FsaLine line in component.Lines)
            {
                if (line.Intensity > 0.0
                    && MarkPeakWindow(inWindow, line.Energy, calibration, fwhmCalibration,
                                      gain, offset, chLo, chHi, channels))
                {
                    any = true;
                }
            }

            // Сумм-пик принадлежит своему нуклиду и стоит там, где линии нет
            // вовсе; без него у плотных каскадов (Lu-176) пиковый счёт терял бы
            // то, что каскадная поправка вынесла из линий.
            FsaCascadeSummer.Correction correction =
                this.cascade != null && this.CascadeSumPeaks ? this.cascade.For(component) : null;
            if (correction != null && correction.SumPeaks != null)
            {
                foreach (FsaCascadeSummer.SumPeak peak in correction.SumPeaks)
                {
                    if (MarkPeakWindow(inWindow, peak.Energy, calibration, fwhmCalibration,
                                       gain, offset, chLo, chHi, channels))
                    {
                        any = true;
                    }
                }
            }

            // Ни одна линия не легла в окно фита (весь компонент за краем) —
            // счёт по всему образу был бы не «пиковым», а случайным остатком.
            if (!any)
            {
                return 0.0;
            }

            double counts = 0.0;
            for (int i = chLo; i <= chHi; i++)
            {
                if (inWindow[i])
                {
                    counts += curve[i];
                }
            }

            return counts;
        }

        /// <summary>
        /// Пометить в маске каналы окна ±2 ПШПВ вокруг линии с учётом дрейфа
        /// шкалы. Возвращает false, если линия за краем окна фита или ПШПВ там
        /// не определена.
        /// </summary>
        bool MarkPeakWindow(bool[] inWindow, double energy, EnergyCalibration calibration,
                            FwhmCalibration fwhmCalibration, double gain, double offset,
                            int chLo, int chHi, int channels)
        {
            if (!(energy > 0.0))
            {
                return false;
            }

            double position = EnergyToChannelSafe(calibration, energy, channels);
            if (Double.IsNaN(position))
            {
                return false;
            }

            double p = gain * position + offset;
            double fwhm = fwhmCalibration.ChannelToFwhm(p);
            if (!(fwhm > 0.0) || Double.IsNaN(fwhm))
            {
                return false;
            }

            int from = (int)Math.Floor(p - 2.0 * fwhm);
            int to = (int)Math.Ceiling(p + 2.0 * fwhm);
            if (from < chLo)
            {
                from = chLo;
            }

            if (to > chHi)
            {
                to = chHi;
            }

            if (from > to)
            {
                return false;
            }

            for (int i = from; i <= to; i++)
            {
                inWindow[i] = true;
            }

            return true;
        }

        // ------------------------------------------------------------------
        // Модель и решатель
        // ------------------------------------------------------------------

        sealed class FitColumn
        {
            public FsaComponent Component;   // null у колонок континуума
            public double[] Values;
        }

        sealed class FitResult
        {
            public List<FitColumn> Columns;
            public double[] Amplitude;
            public double[] Sigma;
            public double[] Z;
            public double Chi2;
            public double Chi2Ndf;
            public double[] Residual;

            /// <summary>Хоть один образ этого фита построен матрицей отклика.</summary>
            public bool FromResponseMatrix;
        }

        FitResult FitHuber(List<FsaComponent> library, List<double[]> fixedColumns,
                           EnergyCalibration calibration, FwhmCalibration fwhmCalibration, FsaEfficiency efficiency,
                           double gain, double offset, int chLo, int chHi, int channels,
                           double[] y, double[] variance, double[] baseWeights, List<FsaComponent> subset)
        {
            double[] weights = (double[])baseWeights.Clone();
            FitResult best = null;
            int passes = this.HuberM > 0.0 ? 3 : 1;
            for (int pass = 0; pass < passes; pass++)
            {
                best = FitOnce(library, fixedColumns, calibration, fwhmCalibration, efficiency,
                               gain, offset, chLo, chHi, channels, y, weights, subset);
                if (best == null || pass + 1 == passes)
                {
                    break;
                }

                for (int i = chLo; i <= chHi; i++)
                {
                    double sigma = Math.Sqrt(variance[i]);
                    double residual = Math.Abs(best.Residual[i]);
                    double m = this.HuberM * sigma;
                    weights[i] = residual > m ? (1.0 / variance[i]) * (m / residual) : 1.0 / variance[i];
                }
            }

            return best;
        }

        FitResult FitOnce(List<FsaComponent> library, List<double[]> fixedColumns,
                          EnergyCalibration calibration, FwhmCalibration fwhmCalibration, FsaEfficiency efficiency,
                          double gain, double offset, int chLo, int chHi, int channels,
                          double[] y, double[] weights, List<FsaComponent> subset)
        {
            List<FitColumn> columns = new List<FitColumn>();
            bool fromMatrix = false;
            foreach (FsaComponent component in library)
            {
                if (subset != null && !subset.Contains(component))
                {
                    continue;
                }

                double[] template;
                double[] lowTail = null;
                if (component.FixedTemplate != null)
                {
                    // Готовый образ (наложения): ни линий, ни дрейфа.
                    template = component.FixedTemplate;
                }
                else if (this.ResponseMatrix != null && !component.WeightsAreFinal)
                {
                    template = this.BuildTemplateFromResponse(component, calibration, fwhmCalibration,
                                                              gain, offset, chLo, chHi, channels, out lowTail);
                    // Пометка «· матрица» ставится по ФАКТУ построенного матрицей
                    // образа, а не по наличию матрицы: библиотека из одних
                    // готовых образов (наложения) и производных компонентов
                    // (обратное рассеяние) матрицу не трогает вовсе.
                    fromMatrix |= template != null;
                }
                else
                {
                    template = BuildTemplate(component, calibration, fwhmCalibration, efficiency,
                                             gain, offset, chLo, chHi, channels);
                }

                if (template != null)
                {
                    columns.Add(new FitColumn { Component = component, Values = template });

                    // Подпороговый хвост матричного образа — своя колонка со
                    // свободной амплитудой и БЕЗ компонента: в результате она
                    // сливается с подложкой, в «пирог» и отсев по z не входит.
                    // Живёт и умирает вместе с компонентом: при отсеве subset
                    // выкидывает обоих ещё до этой ветки.
                    if (lowTail != null)
                    {
                        columns.Add(new FitColumn { Component = null, Values = lowTail });
                    }
                }
            }

            foreach (double[] column in fixedColumns)
            {
                columns.Add(new FitColumn { Component = null, Values = column });
            }

            int m = columns.Count;
            if (m == 0)
            {
                return null;
            }

            int n = chHi - chLo + 1;

            double[,] gram = new double[m, m];
            double[] c = new double[m];
            for (int a = 0; a < m; a++)
            {
                double[] ta = columns[a].Values;
                double dot = 0.0;
                for (int i = chLo; i <= chHi; i++)
                {
                    dot += ta[i] * weights[i] * y[i];
                }

                c[a] = dot;
                for (int b = a; b < m; b++)
                {
                    double[] tb = columns[b].Values;
                    double value = 0.0;
                    for (int i = chLo; i <= chHi; i++)
                    {
                        value += ta[i] * weights[i] * tb[i];
                    }

                    gram[a, b] = value;
                    gram[b, a] = value;
                }
            }

            bool[] active;
            double[] x = NnlsSolve(gram, c, m, out active);

            double[] model = new double[channels];
            for (int k = 0; k < m; k++)
            {
                if (x[k] <= 0.0)
                {
                    continue;
                }

                double[] t = columns[k].Values;
                for (int i = chLo; i <= chHi; i++)
                {
                    model[i] += x[k] * t[i];
                }
            }

            double chi2 = 0.0;
            double[] residual = new double[channels];
            for (int i = chLo; i <= chHi; i++)
            {
                double r = y[i] - model[i];
                residual[i] = r;
                chi2 += r * r * weights[i];
            }

            int activeCount = 0;
            for (int k = 0; k < m; k++)
            {
                if (active[k])
                {
                    activeCount++;
                }
            }

            double chi2ndf = chi2 / Math.Max(1, n - activeCount);

            // Погрешности — из обратной матрицы нормальных уравнений активного
            // множества, надутые на sqrt(chi2/ndf): когда модель не дотягивает
            // до статистики, «сырая» погрешность занижена.
            double[] sigma = new double[m];
            double[] z = new double[m];
            double inflate = Math.Sqrt(Math.Max(1.0, chi2ndf));
            List<int> activeIndices = new List<int>();
            for (int k = 0; k < m; k++)
            {
                if (active[k])
                {
                    activeIndices.Add(k);
                }
            }

            if (activeIndices.Count > 0)
            {
                double[,] activeGram = new double[activeIndices.Count, activeIndices.Count];
                for (int a = 0; a < activeIndices.Count; a++)
                {
                    for (int b = 0; b < activeIndices.Count; b++)
                    {
                        activeGram[a, b] = gram[activeIndices[a], activeIndices[b]];
                    }
                }

                double[,] inverse = InvertSymmetric(activeGram, activeIndices.Count);
                if (inverse != null)
                {
                    for (int a = 0; a < activeIndices.Count; a++)
                    {
                        double d = inverse[a, a];
                        sigma[activeIndices[a]] = d > 0.0 ? Math.Sqrt(d) * inflate : 0.0;
                    }
                }
            }

            for (int k = 0; k < m; k++)
            {
                if (!active[k] && gram[k, k] > 0.0)
                {
                    sigma[k] = inflate / Math.Sqrt(gram[k, k]);
                }

                z[k] = sigma[k] > 0.0 ? x[k] / sigma[k] : 0.0;
            }

            // Отсчёты компонента считаются НЕ здесь: по всему образу они
            // несравнимы между нуклидами (S24в), и единственное место, где они
            // берутся, — PeakWindowCounts на готовом результате.
            return new FitResult
            {
                Columns = columns,
                Amplitude = x,
                Sigma = sigma,
                Z = z,
                Chi2 = chi2,
                Chi2Ndf = chi2ndf,
                Residual = residual,
                FromResponseMatrix = fromMatrix
            };
        }

        /// <summary>
        /// Образ компонента: профили единичной площади в позициях линий с
        /// учётом дрейфа шкалы, весами по выходу и кривой эффективности.
        ///
        /// Форма берётся из ПШПВ-калибровки через <see cref="PeakShapeModel"/> —
        /// та же, которой рисует и меряет пики всё остальное приложение. Своя
        /// гауссиана здесь была прямой ошибкой: у приборов с PeakType = 1
        /// (ExpGaussExp 1.5/5 у ASN16) образ не имел левого хвоста, и невязка
        /// уходила в континуум.
        ///
        /// Нормировка — на площадь самого профиля, посчитанную по его полному
        /// носителю (GetLeftSupport/GetRightSupport), а не на площадь
        /// гауссианы: у профиля с хвостами интеграл другой, и гауссова норма
        /// поднимала бы вершину модели над данными при недоборе площади.
        /// Носитель считается целиком, даже если часть его вышла за границы
        /// фита, — иначе линия у края шкалы получила бы вес больше своего.
        /// </summary>
        const double ElectronMassKev = 510.99895;

        /// <summary>
        /// Образы обратного рассеяния по составу, найденному предыдущим проходом.
        ///
        /// Фотон энергии E, рассеявшийся назад в веществе ВНЕ кристалла (защита,
        /// сама проба, стены), приходит в детектор с энергией E/(1+2E/511). Без
        /// такого образа эти пики достаются чужим линиям: 662 → 184 кэВ садится
        /// на U-235 185.7, мультиплет 300-340 → ~145, ХРИ W 59 → 48. На корпусе
        /// (58 спектров сцинтилляторов, свой нуклидный сет под каждый) образ дал
        /// recall 86 % → 89 % и Σχ²/ndf 559 → 547; в частности вернул U-235 на
        /// граните и K-40 на плитке и снял U-235 с цезиевого спектра.
        ///
        /// Колонки две, и это не избыточность: прогон показал, что они чинят
        /// разные спектры. Узкая (строго назад) снимает U-235 с Cs-137, широкая
        /// (интеграл по задней полусфере с сечением Клейна — Нишины) снимает
        /// Ba-133 с европиевых. Доля однократного рассеяния строго назад против
        /// интеграла задаётся геометрией рассеивателя, которой мы не знаем, —
        /// поэтому обе свободны, а смесь выбирает NNLS.
        ///
        /// Вес линии берётся на энергии ИСХОДНОГО фотона (амплитуда × выход ×
        /// эффективность) — это поток, которому есть чем рассеиваться, — и
        /// второй раз эффективность не применяется (WeightsAreFinal).
        /// </summary>
        /// <summary>
        /// Образ случайных наложений (pile-up, TODO S22): два кванта РАЗНЫХ
        /// распадов пришли в пределах разрешающего времени тракта и сложились
        /// в один импульс.
        ///
        /// ПОЧЕМУ ЭТО НЕ КАСКАД. Каскадное суммирование — свойство одного
        /// распада, оно считается по схеме уровней и геометрии
        /// (<see cref="FsaCascadeSummer"/>). Наложение — свойство ЗАГРУЗКИ: у
        /// Cs-137 на распад вылетает один квант, каскадных пар нет вовсе, а
        /// пик 662+662 в спектре есть. Ни фотонная матрица, ни нуклидная его
        /// дать не могут.
        ///
        /// ФОРМА БЕРЁТСЯ ИЗ САМОГО СПЕКТРА. Распределение суммы двух
        /// независимых событий есть свёртка распределения с самим собой,
        /// поэтому образ наложений — АВТОСВЁРТКА измеренного спектра. Никакой
        /// новой физики не нужно, и ширина получается правильной сама:
        /// свёртка двух пиков с ПШПВ w даёт w·√2, как и положено сумме.
        ///
        /// СОБЫТИЯ ПЕРЕНОСЯТСЯ, А НЕ ДОБАВЛЯЮТСЯ. Наложившаяся пара уходит с
        /// обеих своих энергий и приходит на сумму, поэтому образ равен
        /// `(s⊗s)/N − s`: приход автосвёрткой И убыль самим спектром. Колонка
        /// получается знакопеременной с НУЛЕВЫМ интегралом — как и положено
        /// переносу. Одна убыль без прихода (или наоборот) — не приближение, а
        /// другая физика: первая версия считала только приход, и фит,
        /// получив колонку, которая умеет только добавлять счёт, разъехался —
        /// χ²/ndf 34.1 → 87.6, а τ вышло 1.37 мкс вместо измеренных 0.37.
        ///
        /// СВОБОДНАЯ АМПЛИТУДА ЗДЕСЬ ПРАВОМЕРНА — в отличие от сумм-пиков
        /// каскада, которым свободная колонка запрещена. Разница в том, чем
        /// задана величина: у каскада геометрией и схемой распада (значит она
        /// ИЗВЕСТНА, и свобода позволила бы подогнать её под континуум), у
        /// наложений — произведением 2τR, где разрешающее время τ не записано
        /// нигде. Фит его и находит: амплитуда колонки равна ровно 2τR.
        ///
        /// Считается ОДИН раз на разбор: от сетки дрейфа не зависит.
        /// </summary>
        FsaComponent BuildPileUpComponent(int[] raw, EnergyCalibration calibration,
                                          int chLo, int chHi, int channels)
        {
            const double BinKev = 4.0;

            double topEnergy = EnergyAt(calibration, channels - 1);
            if (!(topEnergy > 0.0))
            {
                return null;
            }

            // Спектр в равномерную шкалу энергии: свёртка складывает ЭНЕРГИИ, а
            // шкала каналов у нелинейной калибровки неравномерна, и складывать
            // номера каналов было бы просто неверно.
            int bins = (int)(topEnergy / BinKev) + 1;
            double[] byEnergy = new double[bins];
            double total = 0.0;
            for (int ch = 0; ch < channels; ch++)
            {
                if (raw[ch] <= 0)
                {
                    continue;
                }

                int bin = (int)(EnergyAt(calibration, ch) / BinKev);
                if (bin >= 0 && bin < bins)
                {
                    byEnergy[bin] += raw[ch];
                    total += raw[ch];
                }
            }

            if (!(total > 0.0))
            {
                return null;
            }

            // Автосвёртка. Отсекаем пустые бины: у спектра с 8192 каналами
            // заполненных бинов немного, а квадрат их числа — это вся цена.
            var filled = new List<int>();
            double floor = total * 1.0E-9;
            for (int k = 0; k < bins; k++)
            {
                if (byEnergy[k] > floor)
                {
                    filled.Add(k);
                }
            }

            double[] pile = new double[bins];
            foreach (int a in filled)
            {
                double va = byEnergy[a] / total;
                foreach (int b in filled)
                {
                    int s = a + b;
                    if (s >= bins)
                    {
                        break;      // filled упорядочен, дальше только выше
                    }

                    pile[s] += va * byEnergy[b];
                }
            }

            // Убыль: пара ушла с обеих своих энергий. Интеграл колонки после
            // этого равен нулю — перенос, а не добавка.
            //
            // И сразу ДЕЛИМ НА ПОЛНЫЙ СЧЁТ. Без этого колонка идёт в единицах
            // отсчётов (до миллиона на канал), а образы линий — в долях на
            // распад (порядка 1e-3): матрица Грама получает разброс норм в
            // десятки порядков и NNLS разъезжается. Первая версия без деления
            // давала χ²/ndf 34.1 → 87.8 на ровном месте. Цена нормировки —
            // амплитуда колонки равна теперь 2τR·N, а не 2τR.
            for (int k = 0; k < bins; k++)
            {
                pile[k] = (pile[k] - byEnergy[k]) / total;
            }

            // Обратно на шкалу каналов, с сохранением площади: в канал идёт та
            // доля бина, которая на него приходится.
            double[] template = new double[channels];
            bool any = false;
            for (int ch = chLo; ch <= chHi; ch++)
            {
                double lo = EnergyAt(calibration, ch - 0.5);
                double hi = EnergyAt(calibration, ch + 0.5);
                if (!(hi > lo))
                {
                    continue;
                }

                double sum = 0.0;
                int first = (int)(lo / BinKev);
                int last = (int)(hi / BinKev);
                for (int k = first; k <= last; k++)
                {
                    if (k < 0 || k >= bins || pile[k] == 0.0)
                    {
                        continue;
                    }

                    double binLo = k * BinKev, binHi = binLo + BinKev;
                    double overlap = Math.Min(hi, binHi) - Math.Max(lo, binLo);
                    if (overlap > 0.0)
                    {
                        sum += pile[k] * overlap / BinKev;
                    }
                }

                // Ноль здесь не «пусто», а «приход сошёлся с убылью»: значения
                // колонки знакопеременные, и отбрасывать отрицательные нельзя.
                if (sum != 0.0)
                {
                    template[ch] = sum;
                    any = true;
                }
            }

            if (!any)
            {
                return null;
            }

            var component = new FsaComponent(FsaResult.PileUpLayerName, FsaComponentKind.Nuisance)
            {
                FixedTemplate = template,
                WeightsAreFinal = true
            };
            return component;
        }

        /// <summary>Энергия канала, с защитой от вырожденной калибровки.</summary>
        static double EnergyAt(EnergyCalibration calibration, double channel)
        {
            double energy = calibration.ChannelToEnergy(channel);
            return energy > 0.0 ? energy : 0.0;
        }

        static List<FsaComponent> BuildBackscatterComponents(FitResult fit, FsaEfficiency efficiency)
        {
            List<FsaComponent> made = new List<FsaComponent>();
            FsaComponent broad = BuildBackscatter(fit, efficiency, "Backscatter", 110.0);
            if (broad != null)
            {
                made.Add(broad);
            }

            FsaComponent sharp = BuildBackscatter(fit, efficiency, "Backscatter180", 179.0);
            if (sharp != null)
            {
                made.Add(sharp);
            }

            return made;
        }

        static FsaComponent BuildBackscatter(FitResult fit, FsaEfficiency efficiency,
                                             string name, double thetaMinDegrees)
        {
            const int Steps = 24;
            const double BinKev = 1.0;
            double thetaMin = thetaMinDegrees * Math.PI / 180.0;
            Dictionary<int, double> histogram = new Dictionary<int, double>();

            for (int k = 0; k < fit.Columns.Count; k++)
            {
                FsaComponent source = fit.Columns[k].Component;
                if (source == null || source.Kind == FsaComponentKind.Nuisance
                    || source.Lines.Count == 0)
                {
                    continue;
                }

                double amplitude = fit.Amplitude[k];
                if (!(amplitude > 0.0))
                {
                    continue;
                }

                foreach (FsaLine line in source.Lines)
                {
                    if (!(line.Energy > 0.0) || !(line.Intensity > 0.0))
                    {
                        continue;
                    }

                    double flux = amplitude * line.Intensity;
                    if (efficiency != null)
                    {
                        double e = efficiency.Eval(line.Energy);
                        if (!(e > 0.0))
                        {
                            continue;
                        }

                        flux *= e;
                    }

                    double alpha = line.Energy / ElectronMassKev;
                    for (int s = 0; s < Steps; s++)
                    {
                        double theta = thetaMin + (Math.PI - thetaMin) * (s + 0.5) / Steps;
                        double sin = Math.Sin(theta);
                        double ratio = 1.0 / (1.0 + alpha * (1.0 - Math.Cos(theta)));
                        // Клейн — Нишина на телесный угол без общего множителя,
                        // умноженная на sin(θ) от самого телесного угла.
                        double weight = flux * ratio * ratio
                            * (ratio + 1.0 / ratio - sin * sin) * sin;
                        double scattered = line.Energy * ratio;
                        if (!(weight > 0.0) || !(scattered > 0.0))
                        {
                            continue;
                        }

                        int bin = (int)(scattered / BinKev);
                        double have;
                        histogram.TryGetValue(bin, out have);
                        histogram[bin] = have + weight;
                    }
                }
            }

            if (histogram.Count == 0)
            {
                return null;
            }

            double top = 0.0;
            foreach (double value in histogram.Values)
            {
                if (value > top)
                {
                    top = value;
                }
            }

            if (!(top > 0.0))
            {
                return null;
            }

            FsaComponent component = new FsaComponent(name, FsaComponentKind.Nuisance)
            {
                WeightsAreFinal = true,
                Derived = true,
            };
            foreach (KeyValuePair<int, double> pair in histogram)
            {
                // Нормировка на максимум: амплитуда колонки должна получиться
                // того же порядка, что у остальных, иначе NNLS работает на
                // плохо обусловленной матрице.
                component.Lines.Add(new FsaLine("bs", (pair.Key + 0.5) * BinKev,
                                                100.0 * pair.Value / top));
            }

            component.Lines.Sort((a, b) => a.Energy.CompareTo(b.Energy));
            return component;
        }


        /// <summary>
        /// Нижняя граница доверия континууму матрицы, кэВ.
        ///
        /// Ниже неё в спектре живёт то, чего матрица знать не может: порог АЦП,
        /// флуоресценция защиты и обстановки (K-серии W и Pb), возврат из
        /// свинцового домика — матрица считает только пробу с кюветой и
        /// кристалл. Треть статистики спектра стоит именно там, и жёсткая
        /// связка «пик + континуум» матричного образа позволяла этому
        /// неописуемому окну занулять компоненты с ОБНАРУЖЕННЫМИ пиками:
        /// на граните (ASN16-цилиндр) NNLS выбрасывал Pb-212/Pb-214/Tl-208
        /// целиком, потому что любая амплитуда, закрывающая пик, тянула свой
        /// континуум в окно, где модель не сходится, и штраф перевешивал.
        ///
        /// Ниже порога континуум образа не выбрасывается, а ОТВЯЗЫВАЕТСЯ:
        /// уходит отдельной колонкой со свободной амплитудой (в отрисовке она
        /// сливается с подложкой). Там, где матрица права (цезий: плато
        /// комптона ниже 100 кэВ настоящее), NNLS берёт хвост почти с той же
        /// амплитудой и качество матрицы сохраняется — жёсткая отсечка стоила
        /// на цезиевом спектре χ²/ndf 35.9 → 42.6. Там, где окно занято чужим
        /// (гранит), хвост зануляется сам, не утаскивая пик.
        ///
        /// Величина нечувствительна: 80, 100, 120 и 150 кэВ дают один состав
        /// с точностью до долей процента (проба S11, 07.08.2026); взято 100.
        /// Пиковые окна линий (±2 ПШПВ) остаются в основном образе и ниже
        /// порога — пик под порогом (рентген свинца 75 кэВ) обязан выжить.
        ///
        /// ПОЛЕ, а не константа (S13, 08.08.2026): это программная ручка для
        /// проб — A/B «жёсткая связка против отвязки» (0 — отвязки нет, весь
        /// континуум образа остаётся привязанным к пику) и скан порога 80–150
        /// на матрице физики 6. В UI и конфигурацию НЕ выводится сознательно:
        /// это инструмент замера, а не настройка пользователя, и приложение
        /// всегда работает с умолчанием.
        /// </summary>
        public double ResponseContinuumTrustFloorKev = 100.0;

        /// <summary>
        /// Вынуть из гистограммы поглощения бины ниже порога доверия, кроме
        /// пиковых окон линий компонента (см. <see cref="ResponseContinuumTrustFloorKev"/>).
        /// Возвращает вынутое отдельной гистограммой или null, если ниже
        /// порога ничего не было.
        /// </summary>
        double[] SplitContinuumBelowTrustFloor(double[] deposit, double bin, FsaComponent component,
                                               EnergyCalibration calibration, FwhmCalibration fwhmCalibration,
                                               int channels)
        {
            return this.SplitContinuumBelowTrustFloor(deposit, null, bin, component, calibration,
                                                      fwhmCalibration, channels);
        }

        /// <summary>
        /// То же, но заодно вынимает те же бины из ПАРАЛЛЕЛЬНОЙ гистограммы
        /// <paramref name="part"/> — доли образа, которую потом рисуют подслоем.
        /// Нож обязан быть один: бин, ушедший из ленты в отдельную колонку,
        /// обязан уйти и из подслоя, иначе подслой окажется выше ленты ровно на
        /// вынутое (S37).
        /// </summary>
        double[] SplitContinuumBelowTrustFloor(double[] deposit, double[] part, double bin,
                                               FsaComponent component,
                                               EnergyCalibration calibration, FwhmCalibration fwhmCalibration,
                                               int channels)
        {
            int floorBins = (int)(this.ResponseContinuumTrustFloorKev / bin);
            if (floorBins <= 0)
            {
                return null;
            }

            // Окна линий в кэВ: линия чуть выше порога свешивает левое плечо
            // под порог, поэтому окна считаются для всех линий, а не только
            // для лежащих ниже.
            List<double> lows = new List<double>();
            List<double> highs = new List<double>();
            foreach (FsaLine line in component.Lines)
            {
                if (!(line.Energy > 0.0) || !(line.Intensity > 0.0))
                {
                    continue;
                }

                double channel = EnergyToChannelSafe(calibration, line.Energy, channels);
                double half = 3.0 * bin;
                if (!Double.IsNaN(channel))
                {
                    double fwhmChannels = fwhmCalibration.ChannelToFwhm(channel);
                    if (fwhmChannels > 0.0 && !Double.IsNaN(fwhmChannels))
                    {
                        double fwhmKev = calibration.ChannelToEnergy(channel + fwhmChannels / 2.0)
                                         - calibration.ChannelToEnergy(channel - fwhmChannels / 2.0);
                        if (fwhmKev > 0.0)
                        {
                            half = Math.Max(half, 2.0 * fwhmKev);
                        }
                    }
                }

                lows.Add(line.Energy - half);
                highs.Add(line.Energy + half);
            }

            double[] tail = null;
            int limit = Math.Min(floorBins, deposit.Length);
            for (int i = 0; i < limit; i++)
            {
                if (deposit[i] <= 0.0)
                {
                    continue;
                }

                // Центр бина — i·bin: сетка поглощения округляющая (PeakBin и
                // DepositChannels считают от целого номера), а не floor-овая.
                double energy = i * bin;
                bool inPeakWindow = false;
                for (int k = 0; k < lows.Count; k++)
                {
                    if (energy >= lows[k] && energy <= highs[k])
                    {
                        inPeakWindow = true;
                        break;
                    }
                }

                if (!inPeakWindow)
                {
                    if (tail == null)
                    {
                        tail = new double[limit];
                    }

                    tail[i] = deposit[i];
                    deposit[i] = 0.0;
                    if (part != null)
                    {
                        part[i] = 0.0;
                    }
                }
            }

            return tail;
        }

        /// <summary>
        /// Образ компонента по матрице отклика: сумма откликов его линий,
        /// уширенная разрешением спектра.
        ///
        /// Порядок важен для цены. Сначала складываются отклики ВСЕХ линий в
        /// одну гистограмму по энергии поглощения — это дёшево, отклик уже
        /// посчитан. И только потом гистограмма уширяется, один раз на
        /// компонент, а не на линию: уширение стоит на два порядка дороже
        /// сложения, и делать его полсотни раз вместо одного значило бы
        /// оплатить разложение секундами вместо миллисекунд.
        ///
        /// Бины ниже порога пропускаются: у отклика длинный хвост, вклад
        /// которого в канал меньше шума отсчёта, а уширение каждого стоит
        /// столько же, сколько уширение пика.
        ///
        /// Уширение сделано СВЁРТКОЙ, а не суммой отдельных пиков. Гистограмма
        /// поглощения переводится в шкалу каналов и раскладывается по ЦЕЛЫМ
        /// каналам (площадь делится между двумя соседними линейно, отчего и
        /// площадь, и центр тяжести сохраняются точно), а потом каждый
        /// ненулевой канал размазывается готовым ядром из банка. Ядро зависит
        /// только от ПШПВ, и при целом центре одно и то же ядро годится для
        /// любого положения — поэтому профиль считается один раз на значение
        /// ПШПВ за весь разбор, а не заново на каждую группу, каждый компонент
        /// и каждый узел сетки дрейфа. Именно на это уходило восемь секунд из
        /// девяти: двадцать миллионов вычислений профиля против ста тысяч.
        ///
        /// Подпороговый континуум образа уходит отдельной колонкой (см.
        /// <see cref="ResponseContinuumTrustFloorKev"/>) — она отдаётся
        /// вторым выходом <paramref name="lowTail"/>.
        /// </summary>
        double[] BuildTemplateFromResponse(FsaComponent component, EnergyCalibration calibration,
                                           FwhmCalibration fwhmCalibration,
                                           double gain, double offset, int chLo, int chHi, int channels,
                                           out double[] lowTail)
        {
            lowTail = null;
            Deposit deposit = this.DepositOf(component, calibration, fwhmCalibration, channels);
            if (deposit == null)
            {
                return null;
            }

            double bin = this.ResponseMatrix.BinKev;
            double[] template = this.BroadenResponseDeposit(deposit.Values, calibration, fwhmCalibration,
                                                            bin, gain, offset, chLo, chHi, channels);
            if (template != null && deposit.Tail != null)
            {
                lowTail = this.BroadenResponseDeposit(deposit.Tail, calibration, fwhmCalibration,
                                                      bin, gain, offset, chLo, chHi, channels);
            }

            return template;
        }

        /// <summary>
        /// Гистограмма поглощения компонента — из кэша или посчитанная сейчас.
        /// Оба ножа (подпороговый хвост и доля сумм) применяются ЗДЕСЬ, до
        /// того как её положат в кэш: разрезать её потом означало бы снова
        /// завести два места, режущих одно и то же по своей копии (S37).
        /// </summary>
        Deposit DepositOf(FsaComponent component, EnergyCalibration calibration,
                          FwhmCalibration fwhmCalibration, int channels)
        {
            Deposit cached;
            if (this.deposits.TryGetValue(component, out cached))
            {
                if (cached != null && cached.CascadeApplied)
                {
                    this.cascadeApplied = true;
                }

                return cached;
            }

            double bin = this.ResponseMatrix.BinKev;
            bool before = this.cascadeApplied;
            this.cascadeApplied = false;

            double[] sumPart;
            double[] values = this.BuildResponseDeposit(component, true, out sumPart);
            Deposit deposit = null;
            if (values != null)
            {
                // Подпороговый континуум отвязывается в отдельную колонку;
                // пиковые окна линий остаются в основном образе (см.
                // комментарий у ResponseContinuumTrustFloorKev). Хвост уширяется
                // тем же путём: он короткий (полсотни бинов), вторая свёртка
                // почти бесплатна.
                deposit = new Deposit
                {
                    Values = values,
                    SumPart = sumPart,
                    Tail = this.SplitContinuumBelowTrustFloor(values, sumPart, bin, component,
                                                              calibration, fwhmCalibration, channels),
                    CascadeApplied = this.cascadeApplied
                };
            }

            this.cascadeApplied = before || this.cascadeApplied;
            this.deposits[component] = deposit;
            return deposit;
        }

        /// <summary>
        /// Гистограмма поглощения компонента — ОДНА на оба пути: образ, который
        /// идёт в фит, и подслой сумм, который рисуется внутри его ленты.
        ///
        /// Раздельные копии этого кода и были дефектом S37: подслой строился
        /// своим массивом, своим порогом и своей группировкой, и выходил ВЫШЕ
        /// собственной ленты — на 0.01 отсчёта в зачаточном виде и на 5–11,
        /// когда в него добавили сумм-континуум. Подслой не может быть «почти
        /// частью» ленты: он либо построен тем же ножом, либо врёт.
        ///
        /// <paramref name="sumPart"/> — та же гистограмма, но в ней ТОЛЬКО
        /// каскадные добавки; по построению она поканально не больше основной,
        /// потому что в основную кладётся то же самое плюс неотрицательные
        /// линии.
        /// </summary>
        double[] BuildResponseDeposit(FsaComponent component, bool needSumPart, out double[] sumPart)
        {
            sumPart = null;
            EfficiencyMaker.ResponseMatrix matrix = this.ResponseMatrix;
            double bin = matrix.BinKev;
            if (!(bin > 0.0))
            {
                return null;
            }

            // Каскадные поправки компонента считаются один раз и переживают всю
            // сетку дрейфа: от усиления и нуля шкалы они не зависят.
            FsaCascadeSummer.Correction correction =
                this.cascade != null ? this.cascade.For(component) : null;
            if (correction != null && !correction.Any)
            {
                correction = null;
            }

            double topEnergy = TopLineEnergy(component);
            bool sumPeaks = correction != null && this.CascadeSumPeaks;
            if (sumPeaks)
            {
                topEnergy = SumTopEnergy(correction, topEnergy);
            }

            if (!(topEnergy > 0.0))
            {
                return null;
            }

            double[] deposit = new double[(int)(topEnergy / bin + 0.5) + 1];
            bool anyLine = false;
            for (int i = 0; i < component.Lines.Count; i++)
            {
                FsaLine line = component.Lines[i];
                if (!(line.Energy > 0.0) || !(line.Intensity > 0.0))
                {
                    continue;
                }

                // Эффективность НЕ применяется: она уже внутри отклика.
                double weight = line.Intensity / 100.0;
                double cf = correction != null ? correction.LineFactors[i] : 1.0;
                if (Math.Abs(cf - 1.0) < 1.0E-6)
                {
                    matrix.Accumulate(deposit, line.Energy, weight);
                }
                else
                {
                    // Поправка ложится ТОЛЬКО на канал пика: вынос из пика —
                    // чистая потеря, а континуум столько же теряет своих
                    // событий, сколько получает чужих сумм, и в первом порядке
                    // остаётся при своём.
                    for (int c = 0; c < EfficiencyMaker.EfficiencySimulator.ResponseChannelCount; c++)
                    {
                        bool peakChannel = c == (int)EfficiencyMaker.EfficiencySimulator.ResponseChannel.Peak;
                        matrix.AccumulateChannel(deposit, line.Energy,
                                                 peakChannel ? weight * cf : weight, c);
                    }

                    this.cascadeApplied = true;
                }

                anyLine = true;
            }

            if (!anyLine)
            {
                return null;
            }

            if (sumPeaks && this.AccumulateSumPeaks(deposit, correction))
            {
                this.cascadeApplied = true;
            }

            if (sumPeaks && needSumPart)
            {
                // Второй проход тем же методом по пустому массиву той же длины:
                // разность «с суммами минус без» дала бы то же число, но
                // повторный вызов честнее — он показывает, что подслой кладёт
                // РОВНО то же, что лента, и отличается только составом.
                sumPart = new double[deposit.Length];
                this.AccumulateSumPeaks(sumPart, correction, this.SumLayerIncludesContinuum);
            }

            return deposit;
        }

        /// <summary>Самая верхняя линия компонента с ненулевым выходом, кэВ.</summary>
        static double TopLineEnergy(FsaComponent component)
        {
            double top = 0.0;
            foreach (FsaLine line in component.Lines)
            {
                if (line.Energy > top && line.Intensity > 0.0)
                {
                    top = line.Energy;
                }
            }

            return top;
        }

        /// <summary>
        /// Докуда обязан доставать массив поглощения, чтобы каскадные добавки в
        /// него поместились: выше самого высокого сумм-пика И выше верха
        /// сумм-континуума (сумма пары плюс вся энергия третьего кванта).
        ///
        /// `Add` в матрице зажимает выход за край, и всё, что не поместилось,
        /// встаёт ложным пиком в последнем бине — поэтому верх считается ОДНИМ
        /// методом. С тех пор как гистограмму заводит один
        /// <see cref="BuildResponseDeposit"/>, разъехаться ему уже не с чем.
        /// </summary>
        static double SumTopEnergy(FsaCascadeSummer.Correction correction, double start)
        {
            double top = start;
            if (correction == null)
            {
                return top;
            }

            if (correction.SumPeaks != null)
            {
                foreach (FsaCascadeSummer.SumPeak peak in correction.SumPeaks)
                {
                    if (peak.Energy > top)
                    {
                        top = peak.Energy;
                    }
                }
            }

            if (correction.SumContinua != null)
            {
                foreach (FsaCascadeSummer.SumContinuum band in correction.SumContinua)
                {
                    double edge = band.ShiftKev + band.ThirdKev;
                    if (edge > top)
                    {
                        top = edge;
                    }
                }
            }

            return top;
        }

        /// <summary>
        /// Уложить сумм-пики в гистограмму поглощения. Общий для образа и для
        /// его отдельной сумм-кривой (отрисовка): два места, кладущие одно и то
        /// же по своей копии кода, однажды разойдутся, и на графике окажется
        /// не то, что в фите.
        ///
        /// Площадь сумм-пика посчитана целиком — обе пиковые эффективности уже
        /// внутри неё. Вес подбирается так, чтобы канал пика дал ровно её и ни
        /// на что больше не разошёлся.
        /// </summary>
        bool AccumulateSumPeaks(double[] deposit, FsaCascadeSummer.Correction correction)
        {
            return this.AccumulateSumPeaks(deposit, correction, true);
        }

        bool AccumulateSumPeaks(double[] deposit, FsaCascadeSummer.Correction correction,
                                bool withContinuum)
        {
            bool any = false;
            foreach (FsaCascadeSummer.SumPeak peak in correction.SumPeaks)
            {
                double peakEfficiency = this.cascade.PeakEfficiency(peak.Energy);
                if (!(peakEfficiency > 0.0))
                {
                    continue;
                }

                this.ResponseMatrix.AccumulateChannel(
                    deposit, peak.Energy, peak.Area / peakEfficiency,
                    (int)EfficiencyMaker.EfficiencySimulator.ResponseChannel.Peak);
                any = true;
            }

            // Сумм-континуум (S19): пара поглощена целиком, третий квант оставил
            // часть себя. Кладётся откликом ТРЕТЬЕГО кванта без пикового канала,
            // сдвинутым на видимую сумму пары, — то есть тем же образом, каким
            // третий квант лёг бы сам по себе, только приподнятым по шкале.
            // Пиковый канал исключён нарочно: полное поглощение третьего уже
            // посчитано тройным сумм-пиком, и класть его сюда значило бы
            // задвоить.
            if (withContinuum && correction.SumContinua != null)
            {
                int channels = EfficiencyMaker.EfficiencySimulator.ResponseChannelCount;
                int peakChannel = (int)EfficiencyMaker.EfficiencySimulator.ResponseChannel.Peak;
                foreach (FsaCascadeSummer.SumContinuum band in correction.SumContinua)
                {
                    if (!(band.Weight > 0.0))
                    {
                        continue;
                    }

                    for (int c = 0; c < channels; c++)
                    {
                        if (c == peakChannel)
                        {
                            continue;
                        }

                        this.ResponseMatrix.AccumulateShifted(
                            deposit, band.ThirdKev, band.Weight, c, band.ShiftKev);
                    }

                    any = true;
                }
            }

            return any;
        }

        /// <summary>
        /// Образ из ОДНИХ сумм-пиков компонента — для отрисовки подслоем.
        /// В фит не идёт и идти не должен: доли сумм-пиков заданы геометрией и
        /// схемой распада, а свободная колонка позволила бы подогнать их под
        /// остаток континуума, оторвав от собственного нуклида (то же правило,
        /// что у каналов отклика). Строится ОДИН раз на готовый результат, на
        /// выигравшей точке дрейфа, — сетку дрейфа он не удорожает.
        /// </summary>
        double[] BuildSumPeakCurve(FsaComponent component, EnergyCalibration calibration,
                                   FwhmCalibration fwhmCalibration,
                                   double gain, double offset, int chLo, int chHi, int channels)
        {
            if (this.cascade == null || !this.CascadeSumPeaks || this.ResponseMatrix == null
                || component == null || component.WeightsAreFinal)
            {
                return null;
            }

            FsaCascadeSummer.Correction correction = this.cascade.For(component);
            if (correction == null || correction.SumPeaks == null || correction.SumPeaks.Count == 0)
            {
                return null;
            }

            double bin = this.ResponseMatrix.BinKev;
            if (!(bin > 0.0))
            {
                return null;
            }

            // Берётся ТА ЖЕ гистограмма, по которой строилась лента, — из кэша
            // разбора, целиком, с линиями и поправками, хотя рисовать их здесь
            // не собираются. Она нужна как мерка: по ней берутся группы бинов,
            // порог отсечки и центры тяжести, и только это делает подслой
            // действительно частью ленты, а не похожей на неё кривой (S37).
            Deposit deposit = this.DepositOf(component, calibration, fwhmCalibration, channels);
            if (deposit == null || deposit.SumPart == null)
            {
                return null;
            }

            double[] sumCurve;
            this.BroadenResponseDeposit(deposit.Values, deposit.SumPart, calibration, fwhmCalibration,
                                        bin, gain, offset, chLo, chHi, channels, out sumCurve);
            return sumCurve;
        }

        /// <summary>
        /// Уширить гистограмму поглощения в образ по шкале каналов — общий хвост
        /// пути <see cref="BuildTemplateFromResponse"/>, вынесенный ради второй
        /// свёртки подпорогового хвоста.
        /// </summary>
        double[] BroadenResponseDeposit(double[] deposit, EnergyCalibration calibration,
                                        FwhmCalibration fwhmCalibration, double bin,
                                        double gain, double offset, int chLo, int chHi, int channels)
        {
            double[] ignored;
            return this.BroadenResponseDeposit(deposit, null, calibration, fwhmCalibration, bin,
                                               gain, offset, chLo, chHi, channels, out ignored);
        }

        /// <summary>
        /// То же уширение, но заодно ведёт ЧАСТЬ гистограммы
        /// (<paramref name="part"/>) — ту, что рисуется подслоем внутри ленты.
        ///
        /// Часть не уширяется отдельно, и это главное. Группы бинов, порог
        /// отсечки, центр тяжести и ядро берутся у ПОЛНОЙ гистограммы, а часть
        /// лишь отдаёт в тот же центр свою площадь. Отсюда поканальное
        /// `подслой ≤ лента` следует само: веса неотрицательны, ядро общее,
        /// свёртка линейна. Порознь это не выполнялось — порог у части свой
        /// (`top·1e-5` от её собственного максимума, а он на порядки меньше),
        /// группы начинались с других бинов, и подслой вылезал за ленту (S37).
        /// </summary>
        double[] BroadenResponseDeposit(double[] deposit, double[] part, EnergyCalibration calibration,
                                        FwhmCalibration fwhmCalibration, double bin,
                                        double gain, double offset, int chLo, int chHi, int channels,
                                        out double[] partTemplate)
        {
            partTemplate = null;
            double top = 0.0;
            foreach (double v in deposit)
            {
                if (v > top)
                {
                    top = v;
                }
            }

            if (!(top > 0.0))
            {
                return null;
            }

            double threshold = top * 1.0E-5;
            double[] template = new double[channels];
            if (part != null)
            {
                partTemplate = new double[channels];
            }

            // Перевод «энергия → канал» не зависит ни от компонента, ни от узла
            // сетки дрейфа (усиление и ноль накладываются ПОСЛЕ), значит его
            // достаточно посчитать один раз на разбор. У нелинейной калибровки
            // это обращение полинома, и полторы тысячи обращений на компонент,
            // повторённые восемьдесят один раз, стоили заметной доли счёта.
            double[] positions = this.DepositChannels(calibration, bin, deposit.Length + 1, channels);
            ShapeKernelBank bank = this.Kernels(fwhmCalibration);

            // Источники кладутся с запасом по обе стороны шкалы: линия выше
            // верхнего канала образа не имеет, но её левый хвост в окно фита
            // попадает, и терять его нельзя.
            int pad = SourcePad(fwhmCalibration, channels);
            int size = channels + 2 * pad;
            double[] source = this.sourceBuffer;
            int[] bands = this.sourceBands;
            if (source == null || source.Length < size)
            {
                source = this.sourceBuffer = new double[size];
                bands = this.sourceBands = new int[size];
            }

            // Буфер части — свой и одноразовый: подслой строится один раз на
            // готовый результат, а не на каждом узле сетки дрейфа, и делить
            // ради него общий буфер незачем.
            double[] partSource = part != null ? new double[size] : null;

            int srcLo = Int32.MaxValue;
            int srcHi = Int32.MinValue;

            // Бины СЛИВАЮТСЯ в группы шириной около четверти ПШПВ, и площадь
            // группы кладётся в её центр тяжести.
            //
            // Это не оптимизация ради оптимизации, а приведение шага к смыслу.
            // Держать шаг 2 кэВ там, где ПШПВ 44 кэВ, незачем: результат всё
            // равно размажется, а свёртку пришлось бы вести по полутора тысячам
            // источников на компонент вместо полусотни. Площадь и центр тяжести
            // группа сохраняет, значит уширенная картина не меняется.
            int b = 1;
            while (b < deposit.Length)
            {
                if (deposit[b] <= threshold)
                {
                    b++;
                    continue;
                }

                double position = positions[b];
                double nextPosition = positions[b + 1];
                if (Double.IsNaN(position))
                {
                    b++;
                    continue;
                }

                double p = gain * position + offset;
                double fwhm = fwhmCalibration.ChannelToFwhm(p);
                if (!(fwhm > 0.0) || Double.IsNaN(fwhm))
                {
                    b++;
                    continue;
                }

                double perBin = Double.IsNaN(nextPosition) ? 0.0 : Math.Abs(nextPosition - position);
                int group = perBin > 0.0 ? (int)(0.25 * fwhm / perBin) : 1;
                if (group < 1)
                {
                    group = 1;
                }

                double area = 0.0;
                double moment = 0.0;
                double partArea = 0.0;
                int end = Math.Min(deposit.Length, b + group);
                for (int k = b; k < end; k++)
                {
                    double v = deposit[k];
                    if (v <= 0.0)
                    {
                        continue;
                    }

                    double q = positions[k];
                    if (Double.IsNaN(q))
                    {
                        continue;
                    }

                    area += v;
                    moment += v * (gain * q + offset);
                    if (part != null && k < part.Length)
                    {
                        partArea += part[k];
                    }
                }

                b = end;
                if (!(area > 0.0))
                {
                    continue;
                }

                double center = moment / area;
                if (Double.IsNaN(center))
                {
                    continue;
                }

                int band = ShapeKernelBank.Band(fwhm);
                int channel = (int)Math.Floor(center);
                double frac = center - channel;
                Splat(source, bands, pad, channels, channel, area * (1.0 - frac), band, ref srcLo, ref srcHi);
                Splat(source, bands, pad, channels, channel + 1, area * frac, band, ref srcLo, ref srcHi);
                if (partSource != null && partArea > 0.0)
                {
                    // Тот же канал, та же доля, то же ядро — площадь у части
                    // своя. Границы источников и номера ядер уже записаны
                    // полной гистограммой: часть их не расширяет, потому что
                    // непустой быть там, где полная пуста, не может.
                    SplatPart(partSource, pad, channels, channel, partArea * (1.0 - frac));
                    SplatPart(partSource, pad, channels, channel + 1, partArea * frac);
                }
            }

            // Свёртка. Буфер источников общий и переиспользуется между
            // компонентами — каждая ячейка гасится сразу после того, как её
            // размазали, иначе следующий компонент унаследовал бы чужой образ.
            bool any = false;
            for (int idx = srcLo; idx <= srcHi; idx++)
            {
                double weight = source[idx];
                if (weight == 0.0)
                {
                    continue;
                }

                source[idx] = 0.0;
                double partWeight = 0.0;
                if (partSource != null)
                {
                    partWeight = partSource[idx];
                    partSource[idx] = 0.0;
                }

                double[] kernel = bank.Get(bands[idx]);
                if (kernel == null)
                {
                    continue;
                }

                int full0 = idx - pad - bank.LeftSpan(bands[idx]);
                int lo = Math.Max(chLo, full0);
                int hi = Math.Min(chHi, full0 + kernel.Length - 1);
                if (hi < lo)
                {
                    continue;
                }

                for (int i = lo; i <= hi; i++)
                {
                    double k = kernel[i - full0];
                    template[i] += weight * k;
                    if (partWeight > 0.0)
                    {
                        partTemplate[i] += partWeight * k;
                    }
                }

                any = true;
            }

            return any ? template : null;
        }

        /// <summary>
        /// Положить площадь ЧАСТИ в тот же канал источника, что и полная
        /// гистограмма. Ядро и границы там уже записаны — часть их не трогает.
        /// </summary>
        static void SplatPart(double[] source, int pad, int channels, int channel, double weight)
        {
            if (!(weight > 0.0) || channel < -pad || channel > channels - 1 + pad)
            {
                return;
            }

            source[channel + pad] += weight;
        }

        /// <summary>
        /// Положить площадь в целый канал источника, запомнив, каким ядром её
        /// потом размазать. Каналы за пределами буфера отбрасываются: их пик
        /// целиком лежит дальше своего носителя от окна фита.
        /// </summary>
        static void Splat(double[] source, int[] bands, int pad, int channels, int channel, double weight,
                          int band, ref int srcLo, ref int srcHi)
        {
            if (!(weight > 0.0) || channel < -pad || channel > channels - 1 + pad)
            {
                return;
            }

            int idx = channel + pad;
            if (source[idx] == 0.0)
            {
                bands[idx] = band;
            }

            source[idx] += weight;
            if (idx < srcLo)
            {
                srcLo = idx;
            }

            if (idx > srcHi)
            {
                srcHi = idx;
            }
        }

        /// <summary>
        /// Запас буфера источников по обе стороны шкалы. Берётся двойной
        /// носитель профиля у верхнего канала: ПШПВ растёт по шкале как корень,
        /// и на длине запаса прибавить успевает единицы процентов.
        /// </summary>
        static int SourcePad(FwhmCalibration fwhmCalibration, int channels)
        {
            int pad = 64;
            double fwhm = fwhmCalibration.ChannelToFwhm(channels - 1);
            if (fwhm > 0.0 && !Double.IsNaN(fwhm))
            {
                double support = Math.Max(PeakShapeModel.GetLeftSupport(fwhmCalibration, fwhm),
                                          PeakShapeModel.GetRightSupport(fwhmCalibration, fwhm));
                if (support > 0.0 && !Double.IsNaN(support))
                {
                    pad = (int)Math.Ceiling(2.0 * support) + 8;
                }
            }

            if (pad < 64)
            {
                pad = 64;
            }

            if (pad > channels)
            {
                pad = channels;
            }

            return pad;
        }

        /// <summary>
        /// Таблица «номер бина отклика → канал» для текущей калибровки.
        /// </summary>
        double[] DepositChannels(EnergyCalibration calibration, double bin, int count, int channels)
        {
            if (this.depositChannels != null && this.depositChannels.Length >= count
                && this.depositChannelsBin == bin
                && object.ReferenceEquals(this.depositChannelsCalibration, calibration)
                && this.depositChannelsCount == channels)
            {
                return this.depositChannels;
            }

            double[] table = new double[count];
            for (int b = 0; b < count; b++)
            {
                table[b] = EnergyToChannelSafe(calibration, b * bin, channels);
            }

            this.depositChannels = table;
            this.depositChannelsBin = bin;
            this.depositChannelsCalibration = calibration;
            this.depositChannelsCount = channels;
            return table;
        }

        ShapeKernelBank Kernels(FwhmCalibration fwhmCalibration)
        {
            if (this.kernelBank == null || !object.ReferenceEquals(this.kernelBank.Calibration, fwhmCalibration))
            {
                this.kernelBank = new ShapeKernelBank(fwhmCalibration);
            }

            return this.kernelBank;
        }

        double[] sourceBuffer;
        int[] sourceBands;
        double[] depositChannels;
        double depositChannelsBin;
        EnergyCalibration depositChannelsCalibration;
        int depositChannelsCount;
        ShapeKernelBank kernelBank;

        /// <summary>
        /// Банк ядер уширения: профиль единичной площади, посчитанный в ЦЕЛЫХ
        /// смещениях от центра, для лестницы значений ПШПВ с шагом 0.2 %.
        ///
        /// Ядро зависит только от ПШПВ, поэтому при целом центре одно и то же
        /// ядро годится в любом канале — а центры целые, потому что источники
        /// разложены по целым каналам с линейным делением площади. Лестница
        /// нужна, чтобы ядро попадало в кэш: без округления ПШПВ у каждой
        /// группы своя и кэш не срабатывает никогда. Шаг 0.2 % — это ошибка
        /// ширины не более 0.1 %, вдесятеро меньше того, что вообще видно в
        /// разложении.
        ///
        /// Ядро нормировано на единичную СУММУ по всему носителю, как это
        /// делалось при отдельной укладке пика, — иначе площадь образа поехала
        /// бы у краёв шкалы, где носитель обрезан окном фита.
        /// </summary>
        sealed class ShapeKernelBank
        {
            static readonly double LogRatio = Math.Log(1.002);

            readonly FwhmCalibration calibration;
            readonly Dictionary<int, double[]> values = new Dictionary<int, double[]>();
            readonly Dictionary<int, int> lefts = new Dictionary<int, int>();

            public ShapeKernelBank(FwhmCalibration calibration)
            {
                this.calibration = calibration;
            }

            public FwhmCalibration Calibration
            {
                get { return this.calibration; }
            }

            /// <summary>Номер ступени лестницы для заданной ПШПВ.</summary>
            public static int Band(double fwhm)
            {
                return (int)Math.Floor(Math.Log(fwhm) / LogRatio + 0.5);
            }

            /// <summary>Ядро ступени или null, если профиля на ней нет.</summary>
            public double[] Get(int band)
            {
                double[] kernel;
                if (this.values.TryGetValue(band, out kernel))
                {
                    return kernel;
                }

                double fwhm = Math.Exp(band * LogRatio);
                double left = PeakShapeModel.GetLeftSupport(this.calibration, fwhm);
                double right = PeakShapeModel.GetRightSupport(this.calibration, fwhm);
                int leftSpan = 0;
                if (left > 0.0 && right > 0.0 && !Double.IsNaN(left) && !Double.IsNaN(right))
                {
                    leftSpan = (int)Math.Ceiling(left);
                    int rightSpan = (int)Math.Ceiling(right);
                    int span = leftSpan + rightSpan + 1;
                    double[] shape = new double[span];
                    double area = 0.0;
                    for (int i = 0; i < span; i++)
                    {
                        double v = PeakShapeModel.RelativeValue(i - leftSpan, fwhm, this.calibration);
                        shape[i] = v;
                        area += v;
                    }

                    if (area > 0.0)
                    {
                        for (int i = 0; i < span; i++)
                        {
                            shape[i] /= area;
                        }

                        kernel = shape;
                    }
                }

                if (kernel == null)
                {
                    leftSpan = 0;
                }

                this.values[band] = kernel;
                this.lefts[band] = leftSpan;
                return kernel;
            }

            /// <summary>Смещение центра ядра от его начала, в каналах.</summary>
            public int LeftSpan(int band)
            {
                int left;
                return this.lefts.TryGetValue(band, out left) ? left : 0;
            }
        }

        // Диспетчеризация «матричный образ или голые пики» живёт в FitOnce:
        // матричный путь отдаёт ДВЕ колонки (образ и подпороговый хвост), и
        // прятать вторую за скалярной сигнатурой значило бы её потерять.
        double[] BuildTemplate(FsaComponent component, EnergyCalibration calibration,
                                      FwhmCalibration fwhmCalibration, FsaEfficiency efficiency,
                                      double gain, double offset, int chLo, int chHi, int channels)
        {
            double[] template = new double[channels];
            // Значения профиля считаются один раз на носитель и переиспользуются
            // между линиями: образ строится заново на каждом узле сетки дрейфа
            // (9x9 = 81 раз на компонент), и второй проход по носителю ради
            // площади удваивал бы счёт профиля на канал.
            double[] shape = null;
            bool any = false;
            foreach (FsaLine line in component.Lines)
            {
                double position = EnergyToChannelSafe(calibration, line.Energy, channels);
                if (Double.IsNaN(position))
                {
                    continue;
                }

                double p = gain * position + offset;
                double fwhm = fwhmCalibration.ChannelToFwhm(p);
                if (fwhm <= 0.0 || Double.IsNaN(fwhm))
                {
                    continue;
                }

                double weight = line.Intensity / 100.0;
                if (efficiency != null && !component.WeightsAreFinal)
                {
                    double e = efficiency.Eval(line.Energy);
                    if (e <= 0.0)
                    {
                        continue;
                    }

                    weight *= e;
                }

                if (weight <= 0.0)
                {
                    continue;
                }

                double left = PeakShapeModel.GetLeftSupport(fwhmCalibration, fwhm);
                double right = PeakShapeModel.GetRightSupport(fwhmCalibration, fwhm);
                if (!(left > 0.0) || !(right > 0.0))
                {
                    continue;
                }

                int full0 = (int)Math.Floor(p - left);
                int full1 = (int)Math.Ceiling(p + right);
                int span = full1 - full0 + 1;
                if (span <= 0)
                {
                    continue;
                }

                if (shape == null || shape.Length < span)
                {
                    shape = new double[span];
                }

                double area = 0.0;
                for (int i = 0; i < span; i++)
                {
                    double v = PeakShapeModel.RelativeValue(full0 + i - p, fwhm, fwhmCalibration);
                    shape[i] = v;
                    area += v;
                }

                if (!(area > 0.0))
                {
                    continue;
                }

                int lo = Math.Max(chLo, full0);
                int hi = Math.Min(chHi, full1);
                if (hi < lo)
                {
                    continue;
                }

                double norm = weight / area;
                for (int i = lo; i <= hi; i++)
                {
                    template[i] += norm * shape[i - full0];
                }

                any = true;
            }

            return any ? template : null;
        }

        /// <summary>
        /// Кусочно-линейный базис («шапки») для континуума. Шаг узлов привязан к
        /// локальной ПШПВ, чтобы континуум не мог поглотить одиночный пик, но не
        /// реже чем 1/64 диапазона.
        /// </summary>
        static List<double[]> BuildHatBasis(FwhmCalibration fwhmCalibration, int chLo, int chHi, int channels)
        {
            List<int> knots = new List<int>();
            double minStep = (chHi - chLo) / 64.0;
            int ch = chLo;
            while (ch < chHi)
            {
                knots.Add(ch);
                double fwhm = fwhmCalibration.ChannelToFwhm(ch);
                if (Double.IsNaN(fwhm) || fwhm < 1.0)
                {
                    fwhm = 1.0;
                }

                ch += (int)Math.Max(1.0, Math.Max(4.0 * fwhm, minStep));
            }

            // Узел в одном канале от верхней границы дал бы «шапку-спицу» —
            // почти коллинеарную колонку. Сливается только этот вырожденный
            // случай: более широкий порог снимает легитимный узел и дубит
            // континуум у верхнего края.
            if (knots.Count > 1 && chHi - knots[knots.Count - 1] < 2)
            {
                knots.RemoveAt(knots.Count - 1);
            }

            knots.Add(chHi);

            List<double[]> hats = new List<double[]>();
            for (int k = 0; k < knots.Count; k++)
            {
                int left = k > 0 ? knots[k - 1] : knots[k];
                int mid = knots[k];
                int right = k + 1 < knots.Count ? knots[k + 1] : knots[k];
                double[] hat = new double[channels];
                for (int i = left; i <= right; i++)
                {
                    double v;
                    if (i == mid)
                    {
                        v = 1.0;
                    }
                    else if (i < mid)
                    {
                        v = left == mid ? 1.0 : (double)(i - left) / (mid - left);
                    }
                    else
                    {
                        v = right == mid ? 1.0 : (double)(right - i) / (right - mid);
                    }

                    if (v > 0.0)
                    {
                        hat[i] = v;
                    }
                }

                hats.Add(hat);
            }

            return hats;
        }

        static int[] Snip(FwhmCalibration fwhmCalibration, EnergySpectrum spectrum)
        {
            SpectrumAriphmetics ariphmetics = new SpectrumAriphmetics(fwhmCalibration, spectrum, SmoothingMethod.None);
            try
            {
                EnergySpectrum continuum = ariphmetics.Continuum();
                return continuum != null ? continuum.Spectrum : null;
            }
            finally
            {
                ariphmetics.Dispose();
            }
        }

        /// <summary>Быстрый NNLS (Bro &amp; de Jong) на готовых нормальных уравнениях.</summary>
        static double[] NnlsSolve(double[,] gram, double[] c, int m, out bool[] active)
        {
            double[] x = new double[m];
            active = new bool[m];
            // Колонки, добавление которых дало сингулярную активную матрицу
            // (дубликат или коллинеарность): без бана градиент не меняется, и
            // внешний цикл выбирал бы тот же индекс до исчерпания бюджета.
            bool[] banned = new bool[m];
            double tol = 1e-10 * MaxDiagonal(gram, m);
            double[] w = (double[])c.Clone();

            for (int iteration = 0; iteration < 30 * m; iteration++)
            {
                int j = -1;
                double wmax = tol;
                for (int k = 0; k < m; k++)
                {
                    if (!active[k] && !banned[k] && w[k] > wmax)
                    {
                        wmax = w[k];
                        j = k;
                    }
                }

                if (j < 0)
                {
                    break;
                }

                active[j] = true;
                while (true)
                {
                    double[] z = SolveActive(gram, c, active, m);
                    if (z == null)
                    {
                        active[j] = false;
                        banned[j] = true;
                        break;
                    }

                    bool allPositive = true;
                    double alpha = 1.0;
                    for (int k = 0; k < m; k++)
                    {
                        if (active[k] && z[k] <= 0.0)
                        {
                            allPositive = false;
                            double a = x[k] / (x[k] - z[k]);
                            if (a < alpha)
                            {
                                alpha = a;
                            }
                        }
                    }

                    if (allPositive)
                    {
                        for (int k = 0; k < m; k++)
                        {
                            x[k] = active[k] ? z[k] : 0.0;
                        }

                        break;
                    }

                    for (int k = 0; k < m; k++)
                    {
                        if (!active[k])
                        {
                            continue;
                        }

                        x[k] += alpha * (z[k] - x[k]);
                        if (x[k] <= tol)
                        {
                            x[k] = 0.0;
                            active[k] = false;
                        }
                    }
                }

                for (int a = 0; a < m; a++)
                {
                    double s = c[a];
                    for (int b = 0; b < m; b++)
                    {
                        if (x[b] != 0.0)
                        {
                            s -= gram[a, b] * x[b];
                        }
                    }

                    w[a] = s;
                }
            }

            return x;
        }

        static double MaxDiagonal(double[,] gram, int m)
        {
            double max = 0.0;
            for (int k = 0; k < m; k++)
            {
                if (gram[k, k] > max)
                {
                    max = gram[k, k];
                }
            }

            return max > 0.0 ? max : 1.0;
        }

        static double[] SolveActive(double[,] gram, double[] c, bool[] active, int m)
        {
            List<int> index = new List<int>();
            for (int k = 0; k < m; k++)
            {
                if (active[k])
                {
                    index.Add(k);
                }
            }

            int n = index.Count;
            if (n == 0)
            {
                return null;
            }

            double[,] a = new double[n, n + 1];
            for (int r = 0; r < n; r++)
            {
                for (int q = 0; q < n; q++)
                {
                    a[r, q] = gram[index[r], index[q]];
                }

                a[r, n] = c[index[r]];
            }

            if (!GaussSolve(a, n))
            {
                return null;
            }

            double[] z = new double[m];
            for (int r = 0; r < n; r++)
            {
                z[index[r]] = a[r, n];
            }

            return z;
        }

        static bool GaussSolve(double[,] a, int n)
        {
            for (int col = 0; col < n; col++)
            {
                int pivot = col;
                for (int r = col + 1; r < n; r++)
                {
                    if (Math.Abs(a[r, col]) > Math.Abs(a[pivot, col]))
                    {
                        pivot = r;
                    }
                }

                if (Math.Abs(a[pivot, col]) < 1e-30)
                {
                    return false;
                }

                if (pivot != col)
                {
                    for (int q = col; q <= n; q++)
                    {
                        double tmp = a[col, q];
                        a[col, q] = a[pivot, q];
                        a[pivot, q] = tmp;
                    }
                }

                for (int r = 0; r < n; r++)
                {
                    if (r == col)
                    {
                        continue;
                    }

                    double f = a[r, col] / a[col, col];
                    if (f == 0.0)
                    {
                        continue;
                    }

                    for (int q = col; q <= n; q++)
                    {
                        a[r, q] -= f * a[col, q];
                    }
                }
            }

            for (int r = 0; r < n; r++)
            {
                a[r, n] /= a[r, r];
            }

            return true;
        }

        static double[,] InvertSymmetric(double[,] source, int n)
        {
            double[,] a = new double[n, 2 * n];
            for (int r = 0; r < n; r++)
            {
                for (int q = 0; q < n; q++)
                {
                    a[r, q] = source[r, q];
                }

                a[r, n + r] = 1.0;
            }

            for (int col = 0; col < n; col++)
            {
                int pivot = col;
                for (int r = col + 1; r < n; r++)
                {
                    if (Math.Abs(a[r, col]) > Math.Abs(a[pivot, col]))
                    {
                        pivot = r;
                    }
                }

                if (Math.Abs(a[pivot, col]) < 1e-30)
                {
                    return null;
                }

                if (pivot != col)
                {
                    for (int q = 0; q < 2 * n; q++)
                    {
                        double tmp = a[col, q];
                        a[col, q] = a[pivot, q];
                        a[pivot, q] = tmp;
                    }
                }

                double d = a[col, col];
                for (int q = 0; q < 2 * n; q++)
                {
                    a[col, q] /= d;
                }

                for (int r = 0; r < n; r++)
                {
                    if (r == col)
                    {
                        continue;
                    }

                    double f = a[r, col];
                    if (f == 0.0)
                    {
                        continue;
                    }

                    for (int q = 0; q < 2 * n; q++)
                    {
                        a[r, q] -= f * a[col, q];
                    }
                }
            }

            double[,] inverse = new double[n, n];
            for (int r = 0; r < n; r++)
            {
                for (int q = 0; q < n; q++)
                {
                    inverse[r, q] = a[r, n + q];
                }
            }

            return inverse;
        }

        static double EnergyToChannelSafe(EnergyCalibration calibration, double energy, int channels)
        {
            try
            {
                return calibration.EnergyToChannel(energy, maxChannels: channels);
            }
            catch (Exception)
            {
                return Double.NaN;
            }
        }

        static int ClampChannel(double channel, int channels)
        {
            if (Double.IsNaN(channel) || channel < 0.0)
            {
                return 0;
            }

            if (channel > channels - 1)
            {
                return channels - 1;
            }

            return (int)Math.Round(channel);
        }
    }
}
