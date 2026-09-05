using BecquerelMonitor.Properties;
using BecquerelMonitor.Utils;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace BecquerelMonitor
{
    public class PeakDetector
    {
        public List<Peak> DetectPeak(ResultData resultData, BackgroundMode bgMode, SmoothingMethod smoothMethod, NuclideSet nuclideSet, List<NuclideDefinition> nuclideDefinitions = null)
        {
            // Снимок списка нуклидов. DetectPeak крутится в Task.Run, а
            // NuclideSetForm правит и СОРТИРУЕТ тот же список из UI-потока:
            // перечисление живого списка ловит "Collection was modified", а
            // catch-all в DCPeakDetectionView гасит этим всю детекцию в одну
            // строку Trace. Копию снимает вызывающий — на UI-потоке; null для
            // однопоточных вызовов (харнесс).
            this.nuclideDefinitions = nuclideDefinitions ?? this.nuclideManager.NuclideDefinitions;

            FWHMPeakDetectionMethodConfig fwhmPeakDetectionMethodConfig = (FWHMPeakDetectionMethodConfig)resultData.PeakDetectionMethodConfig;
            EnergySpectrum inferenceSpectrum;
            SpectrumAriphmetics sa = new SpectrumAriphmetics();
            if (bgMode == BackgroundMode.Substract && resultData.BackgroundEnergySpectrum != null)
            {
                sa = new SpectrumAriphmetics(resultData.EnergySpectrum);
                inferenceSpectrum = sa.Substract(resultData.BackgroundEnergySpectrum);
            }
            else
            {
                inferenceSpectrum = resultData.EnergySpectrum.Clone();
            }

            EnergySpectrum searchSpectrum = inferenceSpectrum.Clone();
            int countlimit = GlobalConfigManager.GetInstance().GlobalConfig.ChartViewConfig.CountLimit;
            bool progressiveSmooth = GlobalConfigManager.GetInstance().GlobalConfig.ChartViewConfig.ProgresiveSmooth;
            switch (smoothMethod)
            {
                case SmoothingMethod.SimpleMovingAverage:
                    int points = GlobalConfigManager.GetInstance().GlobalConfig.ChartViewConfig.NumberOfSMADataPoints;
                    searchSpectrum.Spectrum = sa.SMA(searchSpectrum.Spectrum, points, countlimit: countlimit, progressive: progressiveSmooth);
                    break;
                case SmoothingMethod.WeightedMovingAverage:
                    points = GlobalConfigManager.GetInstance().GlobalConfig.ChartViewConfig.NumberOfWMADataPoints;
                    searchSpectrum.Spectrum = sa.WMA(searchSpectrum.Spectrum, points, countlimit: countlimit, progressive: progressiveSmooth);
                    break;
            }

            List<Peak> peaks = new List<Peak>();
            if (searchSpectrum.TotalPulseCount == 0)
            {
                return peaks;
            }

            FWHMPeakDetector.PeakFinder finder = PeakFinder(searchSpectrum, fwhmPeakDetectionMethodConfig, resultData.FwhmCalibration);

            peaks = CollectPeaks(finder, searchSpectrum, fwhmPeakDetectionMethodConfig.Tolerance, sa, nuclideSet, fwhmPeakDetectionMethodConfig);
            return peaks;
        }

        /// <summary>
        /// ⛔ ИЗ ДВУХ НЕРАЗРЕШИМЫХ БЛИЗНЕЦОВ ДЕРЖИМ ПОДПИСАННОГО (05.09.2026).
        ///
        /// Неразрешимые пики — это ОДИН пик, увиденный финдером дважды, и в
        /// список едет один из них. До этой правки ехал ТОТ, ЧТО ПРИШЁЛ РАНЬШЕ
        /// (то есть левее по шкале), если ни один спор о нуклиде их не связал.
        ///
        /// Дефект вылез замером `S134`: у `LaBrBril_Eu152` пики 1089.1 и 1109.1
        /// неразрешимы (ПШПВ 9.6), и на линию `Eu-152` 1111.0 ложится ВТОРОЙ —
        /// промах 1.9 кэВ против 21.9, и он же вдвое заметнее (SNR 23.0 против
        /// 14.6). Пока подпись получали ОБА, спор о нуклиде оставлял верного;
        /// как только у левого подписи не стало, порядок прихода выкинул
        /// правого — и европий в спектре европия пропал с экрана. Причина не в
        /// окне подписи: окно лишь сняло то, что склеивало этот случай.
        /// </summary>
        bool isNewPeak(Peak newpeak, bool hidepeaks, List<Peak> peaks)
        {
            bool isUnresol = false;
            List<Peak> unresolvedTwins = new List<Peak>();
            foreach (Peak peak in peaks)
            {
                // Sparrow limit
                // Критерий неразрешимости двух пиков delta < 2 * sigma
                // fwhm = 2 * sqrt(2 * ln(2)) * sigma
                // delta < 0.85 * fwhm
                if (Math.Abs(newpeak.Channel - peak.Channel) <= 0.85 * peak.FWHM)
                {
                    isUnresol = true;
                    unresolvedTwins.Add(peak);
                }
                if (newpeak.Nuclide != null && peak.Nuclide != null)
                {
                    if (newpeak.Nuclide.Energy == peak.Nuclide.Energy)
                    {
                        double newpeak_delta = Math.Abs(newpeak.Energy - newpeak.Nuclide.Energy);
                        double oldpeak_delta = Math.Abs(peak.Energy - peak.Nuclide.Energy);
                        if (newpeak_delta < oldpeak_delta)
                        {
                            if (hidepeaks || isUnresol)
                            {
                                peaks.Remove(peak);
                            }
                            else
                            {
                                peak.Nuclide = null;
                            }
                            return true;
                        }
                        else
                        {
                            if (hidepeaks || isUnresol)
                            {
                                return false;
                            }
                            // Mirror of the branch above: when the peaks are resolvable,
                            // the farther peak only loses the nuclide label. It used to be
                            // dropped entirely, losing a real peak.
                            newpeak.Nuclide = null;
                        }
                    }
                }
            }

            // Близнец без подписи уступает близнецу с подписью. Условие узкое
            // нарочно: подписан ТОЛЬКО пришедший и НИ ОДИН из уже стоящих —
            // тогда выбор однозначен и спорить не с кем. Если подпись есть у
            // обоих, их развёл спор о нуклиде выше по тексту; если ни у кого —
            // порядок прихода остаётся прежним.
            if (isUnresol && newpeak.Nuclide != null && unresolvedTwins.Count > 0)
            {
                bool allBlank = true;
                foreach (Peak twin in unresolvedTwins)
                {
                    if (twin.Nuclide != null) { allBlank = false; break; }
                }
                if (allBlank)
                {
                    foreach (Peak twin in unresolvedTwins) peaks.Remove(twin);
                    return true;
                }
            }

            return !isUnresol;
        }

        List<Peak> CollectPeaks(FWHMPeakDetector.PeakFinder finder, EnergySpectrum energySpectrum, double tol, SpectrumAriphmetics sa, NuclideSet nuclideSet, FWHMPeakDetectionMethodConfig peakConfig)
        {
            List<Peak> peaks = new List<Peak>();
            if (finder.centroids == null)
            {
                return peaks;
            }

            for (int i = 0; i < finder.centroids.Length; i++)
            {
                // Площадь берётся у того же финдера и по тому же номеру: все
                // его массивы параллельны и фильтруются вместе (`PeakFinder`
                // обрезает их одним проходом). Отсутствие массива — не повод
                // молча подставить ноль, поэтому длина проверяется.
                double netCounts = finder.integrals != null && i < finder.integrals.Length
                    ? finder.integrals[i]
                    : 0.0;

                Peak peak = CreatePeak(
                    energySpectrum,
                    finder.centroids[i],
                    finder.snrs[i],
                    finder.fwhms[i],
                    finder.fwhm_delta[i],
                    netCounts,
                    sa,
                    peakConfig,
                    refineCentroid: true);
                peak.PeakSearchOrigin = PeakSearchOrigin.FWHMPeakFinder;
                // ⛔ `Peak.FWHM` — В КАНАЛАХ, а не в кэВ, и это не описка:
                //    финдер строит рёбра как `bin_edges[i] = i`
                //    (`FWHMPeakDetector.Spectrum`), калибровка ПШПВ тоже
                //    канальная (`FwhmCalibration.ChannelToFwhm`), и
                //    `DCPeakDetectionView` подставляет её прямо в
                //    `ChannelToEnergy(Channel ± FWHM/2)`. Отбору подписи нужна
                //    ширина В КЭВ — промах линии меряется там, — и считается
                //    она ТЕМ ЖЕ выражением. Умножать на «кэВ-на-канал» нельзя:
                //    калибровка нелинейна, половинки растягиваются по-разному.
                peak.Nuclide = MatchNuclide(peak, tol, nuclideSet,
                                            FwhmKev(peak, energySpectrum.EnergyCalibration));
                if (peak.Nuclide == null && nuclideSet?.HideUnknownPeaks == true)
                {
                    continue;
                }

                bool hidepeaks = nuclideSet != null && nuclideSet.HideUnknownPeaks;
                if (isNewPeak(peak, hidepeaks, peaks))
                {
                    peaks.Add(peak);
                }
            }

            // Оба правила, которым нужен ВЕСЬ спектр целиком, стоят ПОСЛЕ
            // сборки списка нарочно (`A196`, `A197`). Подпись участвует в
            // разводе неразрешимых близнецов (`isNewPeak`), и если снимать её
            // ПО ХОДУ, то менялся бы не только текст над пиком, но и то, какие
            // пики вообще доедут до списка. Здесь же список пиков остаётся тем
            // же, что и до правки, — меняются только надписи, и цену правки
            // видно замером без примеси.
            ConfirmLabels(peaks, energySpectrum, peakConfig, nuclideSet);

            return peaks;
        }

        /// <summary>
        /// (`A196`) Энергия аннигиляционного кванта, кэВ — <c>m_e·c²</c>.
        /// Число физическое, а не библиотечное: по нему ИЩЕТСЯ запись образа
        /// аннигиляции в библиотеке, а энергия суммы берётся уже удвоением
        /// НАЙДЕННОЙ записи — чтобы округление осталось библиотечным (511 → 1022),
        /// а не спорило с ним на десятых долях.
        /// </summary>
        public const double AnnihilationKev = 510.99895;

        /// <summary>
        /// (`A197`) Выход, ниже которого линия ОДНА ЗА СЕБЯ НЕ ОТВЕЧАЕТ: чтобы
        /// подписать ею пик, в спектре обязана быть видна ЕЩЁ ОДНА линия того же
        /// имени — в процентах на распад родителя.
        ///
        /// ⛔ Зачем порог вообще, если правило звучит «у победителя должны быть
        /// прочие линии». Потому что без порога оно снимает ЗАКОННЫЕ подписи:
        /// развёртка по корпусу (129 спектров, поставочная библиотека, 1367
        /// подписей) при окне подтверждения 0.5 ПШПВ даёт ИСТИНУ 562 против 572
        /// — десять истинных подписей уходят, и среди них `Ac-228` 911.0 (выход
        /// 25.8 %) в `AS80_Onyx` и `Tl-208` 2614.0 в `ASN3_Tile`. Яркая линия —
        /// сама себе улика; подтверждения требует СЛАБАЯ.
        ///
        /// ⚠ ЧИСЛО ВЫБРАНО ПО ПОЛКЕ, А НЕ ПО ЛУЧШЕЙ ТОЧКЕ. Развёртка по порогу
        /// (окно 0.5 ПШПВ; ИСТИНА / ФОН / ЛОЖЬ / приборные из 1522 пиков, база
        /// 572 / 433 / 198 / 164):
        ///
        /// <code>
        ///   порог, %   ИСТИНА   ФОН   ЛОЖЬ   приборн
        ///      1        572     425   198      164
        ///      2        572     421   189      164
        ///      3        572     421   186      164
        ///      5        572     413   186      164
        ///      8        572     408   185      164
        ///     10        572     405   185      144
        ///     15        572     392   175      144
        ///     20        570     384   175      144
        /// </code>
        ///
        /// ЛОЖЬ стоит на 186 для любого порога от 3 до 5 и на 185 до 8 — ответ
        /// в этой полосе не зависит от числа. Взято 5 %: середина полки, ИСТИНА
        /// ещё цела с запасом (она держится до 15 %), приборные образы не
        /// тронуты вовсе (их выход не проставлен, а те 20, у кого он есть,
        /// начинают сниматься с 10 %).
        /// </summary>
        public const double LabelSelfConfirmYieldPercent = 5.0;

        /// <summary>
        /// (`A196`, `A197`) На сколько ПШПВ ПИКА линия может отстоять от него,
        /// чтобы считаться УВИДЕННОЙ в спектре.
        ///
        /// ⛔ Это НЕ <see cref="MaximumLabelMissInFwhm"/> и одним числом их
        /// делать нельзя, хотя оба меряют промах в ПШПВ. Там решается «этот ли
        /// пик», и окно нарочно широкое: подпись всё равно достаётся ближайшей
        /// линии, и лишняя ширина никого не пускает вперёд. Здесь решается
        /// «видна ли улика», и широкое окно превращает улику в что угодно: у
        /// сцинтиллятора с ПШПВ 49 кэВ на 700 кэВ окно 1.5 ПШПВ накрывает
        /// ±73 кэВ, и пик 697.3 в `G1S24_Na22_P5` подтвердил бы линию 766.0 —
        /// то есть уран в спектре натрия. При 0.5 ПШПВ (±24.5 кэВ) не
        /// подтверждает.
        /// </summary>
        public const double ConfirmingLineMissInFwhm = 0.5;

        /// <summary>
        /// Полоса поиска пиков В КЭВ. Настройки задают её в кэВ, но финдер
        /// переводит их в каналы и ОБРЕЗАЕТ по числу каналов спектра, поэтому у
        /// спектра на 1024 канала с калибровкой до 1500 кэВ верх полосы 1500, а
        /// не записанные в приборе 3000. Правилу подтверждения это знать
        /// обязательно: требовать линию 2614 кэВ от спектра, который выше 1500
        /// не смотрел, значило бы снять законную подпись за недостижимую улику.
        /// </summary>
        public static void SearchRangeKev(EnergySpectrum energySpectrum,
                                          FWHMPeakDetectionMethodConfig peakConfig,
                                          out double minKev, out double maxKev)
        {
            int lo, hi;
            SearchRangeChannels(energySpectrum, peakConfig, out lo, out hi);
            minKev = energySpectrum.EnergyCalibration.ChannelToEnergy(lo);
            maxKev = energySpectrum.EnergyCalibration.ChannelToEnergy(hi);
        }

        /// <summary>
        /// Та же полоса поиска, но В КАНАЛАХ — как её и строит финдер. Расчёт
        /// здесь ОДИН на обоих читателей нарочно: разойдись они, и правило
        /// подтверждения требовало бы улику из области, которую поиск не
        /// смотрел (или наоборот прощало бы её отсутствие).
        /// </summary>
        static void SearchRangeChannels(EnergySpectrum energySpectrum,
                                        FWHMPeakDetectionMethodConfig peakConfig,
                                        out int minChannel, out int maxChannel)
        {
            EnergyCalibration cal = energySpectrum.EnergyCalibration;
            int channels = energySpectrum.NumberOfChannels;
            int lo = Convert.ToInt32(cal.EnergyToChannel(peakConfig.Min_Range, maxChannels: channels));
            int hi = Convert.ToInt32(cal.EnergyToChannel(peakConfig.Max_Range, maxChannels: channels));
            lo = Math.Max(0, Math.Min(channels - 1, lo));
            hi = Math.Max(0, Math.Min(channels - 1, hi));
            if (hi < lo)
            {
                int swap = lo;
                lo = hi;
                hi = swap;
            }
            minChannel = lo;
            maxChannel = hi;
        }

        /// <summary>
        /// ЧТО ПОДПИСЬ ЗНАЧИТ ДЛЯ ВСЕГО СПЕКТРА, а не для одного пика
        /// (`A196`, `A197`, решение Amber 05.09.2026 — «Оба»).
        ///
        /// <see cref="MatchNuclide"/> судит пик в одиночку: линия достаточно
        /// вероятна и лежит внутри пика — подпись её. Два дефекта этим не
        /// берутся по построению, и оба измерены:
        ///
        ///  1. пик-СУММА двух аннигиляционных квантов (1022 кэВ) в спектрах
        ///     Na-22 подписывался `Pa-234m` 1001.0 — промах 0.03…0.36 ПШПВ,
        ///     выход 0.842 %, то есть РОВНО такой же, как у законных подписей
        ///     той же линии в урановых пробах;
        ///  2. снятая ложная подпись не исчезала, а ПЕРЕИМЕНОВЫВАЛАСЬ: из 24
        ///     снятых плутониевых 19 пиков получили следующего по близости
        ///     (`U-235` 145.0 — 14, `I-131` 364.0 — 5), и «уран» оказывался
        ///     написан над спектром бария.
        ///
        /// Различает такие случаи только СОСТАВ ОСТАЛЬНОГО спектра, и здесь он
        /// есть. Два правила, оба поверх уже собранного списка пиков:
        /// приборная подпись суммы 511+511 (впереди нуклидных) и подтверждение
        /// слабой линии ДРУГОЙ линией того же имени.
        /// </summary>
        void ConfirmLabels(List<Peak> peaks, EnergySpectrum energySpectrum,
                           FWHMPeakDetectionMethodConfig peakConfig, NuclideSet nuclideSet)
        {
            if (peaks == null || peaks.Count == 0)
            {
                return;
            }

            EnergyCalibration cal = energySpectrum.EnergyCalibration;
            double[] fwhmKev = new double[peaks.Count];
            for (int i = 0; i < peaks.Count; i++)
            {
                fwhmKev[i] = FwhmKev(peaks[i], cal);
            }

            LabelAnnihilationSum(peaks, fwhmKev);

            double rangeMin, rangeMax;
            SearchRangeKev(energySpectrum, peakConfig, out rangeMin, out rangeMax);

            for (int i = 0; i < peaks.Count; i++)
            {
                NuclideDefinition nd = peaks[i].Nuclide;
                if (nd == null)
                {
                    continue;
                }
                // Выход НЕ ПРОСТАВЛЕН (0) — приборный образ или запись без
                // паспорта; порогом он не судится ровно там же, где и в
                // MatchNuclide (см. MinimumLabelYieldPercent).
                if (!(nd.Intencity > 0.0))
                {
                    continue;
                }
                if (nd.Intencity >= LabelSelfConfirmYieldPercent)
                {
                    continue;
                }
                if (OwnLineSeen(nd, peaks, fwhmKev, i, rangeMin, rangeMax))
                {
                    continue;
                }
                peaks[i].Nuclide = null;
            }

            // Пик, оставшийся без подписи ЗДЕСЬ, обязан уйти из списка на тех же
            // основаниях, на каких он не попал бы в него в CollectPeaks.
            if (nuclideSet != null && nuclideSet.HideUnknownPeaks)
            {
                peaks.RemoveAll(p => p.Nuclide == null);
            }
        }

        /// <summary>
        /// Видна ли в спектре ЕЩЁ ОДНА линия того же имени.
        ///
        /// ⚠ Улика — ПИК ВОЗЛЕ ЛИНИИ, а не «пик, подписанный этим же нуклидом».
        /// Разница измерена: требование чужой ПОДПИСИ рубит спор двух имён за
        /// один пик задним числом (подтверждающая линия могла проиграть соседу
        /// по промаху) и снимает 19 истинных подписей из 572 против нуля здесь.
        ///
        /// ⛔ «Своя линия» — запись библиотеки с ТЕМ ЖЕ ИМЕНЕМ. Не «тот же
        /// элемент» и не «тот же ряд»: ряд подтверждал бы `Pa-234m` калием
        /// через полкорпуса.
        ///
        /// Нуклид, у которого ДРУГИХ пригодных линий в полосе прибора нет
        /// вовсе, подтверждать нечем — и он НЕ СУДИТСЯ (`Cs-137` 661.7,
        /// `K-40` 1460.8, `Am-241` 59.5 в поставочной библиотеке именно таковы).
        /// </summary>
        bool OwnLineSeen(NuclideDefinition nuclide, List<Peak> peaks, double[] fwhmKev,
                         int self, double rangeMin, double rangeMax)
        {
            bool anyOther = false;
            foreach (NuclideDefinition other in this.nuclideDefinitions)
            {
                if (other == null || !other.Visible || other.Energy == 0.0) continue;
                if (!string.Equals(other.Name, nuclide.Name, StringComparison.Ordinal)) continue;
                if (Math.Abs(other.Energy - nuclide.Energy) < 1e-9) continue;
                if (other.Intencity > 0.0 && other.Intencity < MinimumLabelYieldPercent) continue;
                if (other.Energy < rangeMin || other.Energy > rangeMax) continue;

                anyOther = true;
                for (int j = 0; j < peaks.Count; j++)
                {
                    if (j == self || !(fwhmKev[j] > 0.0)) continue;
                    if (Math.Abs(peaks[j].Energy - other.Energy) <= ConfirmingLineMissInFwhm * fwhmKev[j])
                    {
                        return true;
                    }
                }
            }
            return !anyOther;
        }

        /// <summary>
        /// (`A196`) Пик на 1022 кэВ — это СУММА ДВУХ аннигиляционных квантов, а
        /// не линия нуклида, и подписывается он образом аннигиляции.
        ///
        /// Три условия, и все три обязательны:
        ///
        ///  * в библиотеке есть запись образа аннигиляции (видимая, БЕЗ выхода,
        ///    у энергии кванта). Имя берётся у неё и достраивается ХВОСТОМ из
        ///    ресурсов приложения (`A229`, <see cref="AnnihilationSumName"/>):
        ///    без хвоста подписи 511 и 1022 читались бы с графика ОДИНАКОВО;
        ///  * в спектре есть сам пик 511 — без слагаемого суммы не бывает;
        ///  * пик 511 ЗАМЕТНЕЕ пика 1022 (сравниваются SNR). Сумма — эффект
        ///    второго порядка по загрузке, и обратное соотношение значило бы,
        ///    что 1022 — что-то другое.
        ///
        /// Окно у обоих пиков узкое (<see cref="ConfirmingLineMissInFwhm"/>):
        /// подпись перебивает нуклидную, и вставать она должна ровно на месте,
        /// а не «где-то рядом». Мерено по корпусу: правило срабатывает на
        /// четырёх спектрах Na-22 и НИ НА ОДНОМ другом — у трёх урановых проб с
        /// законным `Pa-234m` 1001.0 пика 511 нет вовсе.
        /// </summary>
        void LabelAnnihilationSum(List<Peak> peaks, double[] fwhmKev)
        {
            NuclideDefinition annihilation = AnnihilationLine();
            if (annihilation == null)
            {
                return;
            }
            double sumKev = 2.0 * annihilation.Energy;

            int single = -1;
            for (int i = 0; i < peaks.Count; i++)
            {
                if (!(fwhmKev[i] > 0.0)) continue;
                if (Math.Abs(peaks[i].Energy - annihilation.Energy) > ConfirmingLineMissInFwhm * fwhmKev[i]) continue;
                if (single < 0 || peaks[i].SNR > peaks[single].SNR) single = i;
            }
            if (single < 0)
            {
                return;
            }

            NuclideDefinition sum = null;
            for (int i = 0; i < peaks.Count; i++)
            {
                if (i == single || !(fwhmKev[i] > 0.0)) continue;
                if (Math.Abs(peaks[i].Energy - sumKev) > ConfirmingLineMissInFwhm * fwhmKev[i]) continue;
                if (!(peaks[single].SNR > peaks[i].SNR)) continue;
                if (sum == null)
                {
                    sum = new NuclideDefinition
                    {
                        Name = AnnihilationSumName(annihilation.Name),
                        Energy = sumKev,
                        HalfLife = annihilation.HalfLife,
                        NuclideColor = annihilation.NuclideColor,
                        Visible = true,
                        Intencity = 0.0,
                        Chain = annihilation.Chain,
                        Sets = annihilation.Sets == null ? null : new HashSet<Guid>(annihilation.Sets)
                    };
                }
                peaks[i].Nuclide = sum;
            }
        }

        /// <summary>
        /// (`A229`, решение Amber 05.09.2026) ИМЯ приборной подписи суммы
        /// 511+511 — имя библиотечной записи образа аннигиляции ПЛЮС хвост из
        /// ресурсов приложения.
        ///
        /// ⛔ Хвост живёт в `Properties/Resources.resx` (+ `.ru.resx`), а не
        /// здесь и не в поставочной библиотеке: своих имён линий у кода нет
        /// (правка `config/NuclideDefinition.xml` запрещена приказом Amber
        /// 01.09.2026), а надпись читает человек — значит, она переводится.
        ///
        /// ⚠ Строка ресурса — ОБРАЗЕЦ с `{0}`, а не «хвост, приклеенный кодом»:
        /// куда именно встаёт имя записи, какие вокруг него скобки и пробел —
        /// свойство ЯЗЫКА. Склей мы «имя + пробел + скобка + хвост» здесь, и
        /// половина видимой строки осталась бы вне ресурсов, то есть ровно тем
        /// дефектом, о котором `A229`.
        ///
        /// Образец без `{0}` (недоперевод) хвоста не даёт вовсе — тогда лучше
        /// прежняя одинаковая надпись, чем подпись, потерявшая имя.
        /// </summary>
        internal static string AnnihilationSumName(string imageName)
        {
            string format = Resources.ResourceManager.GetString("PeakLabelAnnihilationSum", LabelCulture());
            if (string.IsNullOrEmpty(format) || format.IndexOf("{0}", StringComparison.Ordinal) < 0)
            {
                return imageName;
            }
            return string.Format(CultureInfo.InvariantCulture, format, imageName);
        }

        /// <summary>
        /// (`A229`) Язык НАДПИСИ, и он берётся из настройки, а не у потока.
        ///
        /// ⛔ `DetectPeak` крутится в `Task.Run`. С 05.09.2026 язык выставляется
        /// всем потокам (`Program.ApplyLanguage`, `A238`), и подпись взяла бы
        /// верный язык и от потока, — но настройка остаётся источником
        /// НАДЁЖНЕЕ: культуру потока приносит контекст исполнения, а он
        /// переносится не всюду (замер `CultureProbeO14`: поток пула, вошедший
        /// без контекста, языка настройки не видит). Обход `A229` поэтому
        /// оставлен, а не снят.
        ///
        /// Значения настройки те же три, что кладёт меню языка: «OS» (метка, не
        /// имя культуры — отдаём культуру потока, которой распоряжается уже
        /// система), «» (инвариант, то есть первичные английские ресурсы) и
        /// «ru-RU».
        ///
        /// ⛔ Разбор значения — ОДИН на дерево, <see cref="Program.LanguageCulture"/>
        /// (`A238`). Своя копия здесь была, и она РАСХОДИЛАСЬ: на «OS»
        /// <c>GetCultureInfo</c> отдаёт не отказ, а ОСЕТИНСКУЮ культуру (`os` —
        /// код языка по ISO 639), ресурсов на ней нет, и подпись уезжала в
        /// нейтральный английский, пока остальное окно шло по языку системы.
        /// </summary>
        static CultureInfo LabelCulture()
        {
            try
            {
                GlobalConfigInfo config = GlobalConfigManager.GetInstance().GlobalConfig;
                if (config != null)
                {
                    return Program.LanguageCulture(config.Language);
                }
            }
            catch (Exception)
            {
                // Настройка недоступна (харнесс, ранний вызов) — подпись важнее
                // языка: отдаём культуру потока, а не роняем поиск пиков.
            }
            return null;
        }

        /// <summary>
        /// Запись образа аннигиляции в библиотеке: видимая, БЕЗ проставленного
        /// выхода (образ прибора, а не линия распада) и у энергии кванта.
        /// Нет такой записи — правило суммы не работает вовсе: назвать пик
        /// нечем, а назвать его нуклидом было бы тем самым дефектом.
        /// </summary>
        NuclideDefinition AnnihilationLine()
        {
            NuclideDefinition best = null;
            double bestMiss = 2.0;
            foreach (NuclideDefinition nd in this.nuclideDefinitions)
            {
                if (nd == null || !nd.Visible || nd.Intencity > 0.0) continue;
                double miss = Math.Abs(nd.Energy - AnnihilationKev);
                if (miss <= bestMiss)
                {
                    bestMiss = miss;
                    best = nd;
                }
            }
            return best;
        }

        /// <param name="netCounts">
        /// Чистая площадь пика — отклик согласованного фильтра за вычетом
        /// подложки (`PeakFinder.integrals`, то есть `signal[xbin]`).
        ///
        /// До 13.08.2026 сюда не приходило НИЧЕГО, и `Peak.Count` у каждого
        /// найденного пика оставался нулём. Поле при этом читалось — в
        /// `PeakOriginProbe` на нём стоят два отбора «пик заметный»
        /// (родитель обратного рассеяния и слагаемые случайной суммы), и оба
        /// сравнивали ноль с нулём: `q.Count &lt; 0.05·maxCounts` при нулевом
        /// максимуме ложно ВСЕГДА. Отсюда и «случайных сумм ноль» в журнале
        /// InterSpec (§6), списанное тогда на лабораторные условия, и то, что
        /// обратное рассеяние объясняло 59 % всех пиков: родителем годился
        /// любой пик выше по шкале (TODO P4).
        /// </param>
        Peak CreatePeak(
            EnergySpectrum energySpectrum,
            double centroid,
            double snr,
            double fwhm,
            double fwhmDelta,
            double netCounts,
            SpectrumAriphmetics sa,
            FWHMPeakDetectionMethodConfig config,
            bool refineCentroid)
        {
            if (refineCentroid && sa != null && config != null)
            {
                int concat = Math.Max(1, config.Ch_Concat);
                // Keep the window at least [c-2, c+2]: for spectra shorter than Ch_Concat
                // the integer division gave mul = 0, the window collapsed to [c-1, c+1]
                // and FindCentroid returned only a BOUNDARY - every peak systematically
                // shifted by +-1 channel on 256/512/1000-channel spectra.
                int mul = Math.Max(1, energySpectrum.Spectrum.Length / concat);
                centroid = sa.FindCentroid(
                    energySpectrum,
                    Convert.ToInt32(centroid),
                    Convert.ToInt32(centroid - mul - 1),
                    Convert.ToInt32(centroid + mul + 1),
                    config.UseCenterOfMassCentroid);
            }

            Peak peak = new Peak();
            peak.Channel = Math.Max(0, Math.Min(energySpectrum.NumberOfChannels - 1, Convert.ToInt32(Math.Round(centroid))));
            peak.Energy = energySpectrum.EnergyCalibration.ChannelToEnergy(peak.Channel);
            peak.SNR = snr;
            peak.FWHM = fwhm;
            peak.FWHM_DELTA = fwhmDelta;
            peak.Count = netCounts > 0.0 && !Double.IsNaN(netCounts)
                ? (int)Math.Round(Math.Min(netCounts, Int32.MaxValue))
                : 0;
            return peak;
        }

        /// <summary>
        /// (`S134`) Ниже этого выхода на распад родителя линия ПОДПИСЫВАТЬ ПИК
        /// НЕ МОЖЕТ — в процентах.
        ///
        /// ⛔ Это НЕ то же число, что порог активности
        /// (<c>EnergySpectrumView.MinimumActivityYieldPercent</c> = 1 %), и
        /// одним их делать нельзя. Порог активности отвечает на вопрос «можно
        /// ли по этой подписи считать беккерели», а здесь — «может ли эта линия
        /// вообще быть этим пиком». Цена измерена по корпусу (129 спектров,
        /// поставочная <c>BecquerelMonitor\config\NuclideDefinition.xml</c>,
        /// 1432 подписи): порог 1 % снимает 63 подписи, из них 19 у нуклидов,
        /// которые в пробе ЕСТЬ (`Ac-228` 726.863 в ториевых, `Pa-234m` 1001.0
        /// и 766.0 в урановых, `Th-234` 112.81), — то есть режет вместе с ложью
        /// законные метки.
        ///
        /// ⚠ ЧИСЛО НЕ ПОДОГНАНО, и это проверено разверткой: ответ по корпусу
        /// ОДИН И ТОТ ЖЕ для любого порога от 0.002 % до 0.21 % — снимаются
        /// ровно 24 подписи, все до одной ложные (`Pu-238` 152.0 при выходе
        /// 0.0009 % — 19 штук, `Pu-239` 375.0 при 0.0016 % — 5), и ни одной
        /// истинной или фоновой. В библиотеке между 0.0016 % и 0.21 % пусто на
        /// два порядка, поэтому 0.1 % стоит в середине пустой полосы, а не на
        /// её краю.
        ///
        /// ⛔ Выход, РАВНЫЙ нулю, значит «не проставлен», а не «нулевой», и
        /// порогом не судится: так стоят приборные образы (`Annihilation`,
        /// `Tl-208 SE`, характеристический рентген) и часть линий поставочной
        /// библиотеки. Судить их этим порогом значило бы снять 67 подписей
        /// корпуса за отсутствие поля, которого у них не бывает.
        /// </summary>
        public const double MinimumLabelYieldPercent = 0.1;

        /// <summary>
        /// (`S134`, `S64`) На сколько ПШПВ НАЙДЕННОГО ПИКА линия может отстоять
        /// от него и всё ещё считаться им.
        ///
        /// ⛔ Допуск поиска <c>tol</c> задан В ПРОЦЕНТАХ ОТ ЭНЕРГИИ, и у
        /// корпусных приборов он 10 % — то есть ±146 кэВ на калии и ±15 кэВ на
        /// 152 кэВ. Разрешение прибора с этим не связано никак: линия, отстоящая
        /// от пика на 10.7 кэВ при ПШПВ 6.1 кэВ, лежит ЗА пиком целиком, и
        /// назвать её этим пиком нельзя ни при каком допуске. Отсюда второе
        /// окно — по РАЗРЕШЕНИЮ; действуют оба, проходит линия только сквозь
        /// оба.
        ///
        /// ⚠ Число выбрано по цене истинных подписей, а не по красоте. Развёртка
        /// по корпусу — сколько подписей вышло бы за окно (всего 575 истинных,
        /// 448 фоновых, 237 ложных, 172 приборных):
        ///
        /// <code>
        ///   окно      истинных   фоновых    ложных   приборных
        ///   0.50 ПШПВ    17        101        44        11
        ///   0.75 ПШПВ    10         70        30        10
        ///   1.00 ПШПВ     6         52        28         8
        ///   1.25 ПШПВ     3         35        22         8
        ///   1.50 ПШПВ     3         30        20         8
        ///   2.00 ПШПВ     3         22        17         8
        ///   3.00 ПШПВ     3         18        14         6
        /// </code>
        ///
        /// Колено ровно на 1.25: ниже цена истинных подписей растёт вдвое,
        /// выше — не падает вовсе, а ложных снимается меньше. Взято 1.5 —
        /// то же колено круглым числом.
        ///
        /// ⚠ ТРИ ИСТИННЫЕ ПОДПИСИ ЭТО ВСЁ-ТАКИ СНИМАЕТ, и все три германиевые
        /// (германий вне работы по решению Amber). Две из них стоят НЕ НА СВОЕЙ
        /// ЛИНИИ и снимаются по делу: `HPGE_Uranium` пик 1737.9 при ПШПВ
        /// 2.2 кэВ подписан `Bi-214` 1764.0 (11.9 ПШПВ), пик 49.46 при ПШПВ
        /// 0.65 — `Pb-210` 46.539 (4.5 ПШПВ). Третья — цена ОКРУГЛЁННОЙ энергии
        /// в поставочной библиотеке: `HPGeGMX_Eu152` пик 121.78 это линия
        /// европия 121.78, записанная в библиотеке как «125» (3.5 ПШПВ).
        /// </summary>
        public const double MaximumLabelMissInFwhm = 1.5;

        /// <summary>
        /// Кто подписывает пик.
        ///
        /// ⛔ БЛИЖАЙШАЯ ЛИНИЯ — НЕ ОТВЕТ (`S134`, 05.09.2026). Подпись это
        /// утверждение о составе пробы: её читают с графика и из списка пиков,
        /// и по ней же покупается число беккерелей (`S96`). До этой правки
        /// отбор был один — относительный промах меньше допуска, — и линия
        /// `Pu-238` 152.0 кэВ с выходом 0.0009 % на распад собирала 19 пиков в
        /// спектрах бария, церия, европия, радия, лютеция и смеси, где
        /// плутония нет вовсе; отказ активности по выходу (`S99`) убирал у них
        /// ЧИСЛО, а метку «плутоний» оставлял.
        ///
        /// Добавлены два независимых окна, и оба проверяются ДО сравнения
        /// промахов: <see cref="MinimumLabelYieldPercent"/> (линия обязана быть
        /// достаточно вероятной, чтобы дать пик) и
        /// <see cref="MaximumLabelMissInFwhm"/> (линия обязана лежать ВНУТРИ
        /// пика по разрешению прибора, а не внутри процентного допуска).
        ///
        /// ⚠ ЧЕГО ЭТО НЕ ЛЕЧИТ И НЕ ПРИТВОРЯЕТСЯ, ЧТО ЛЕЧИТ: подпись линией
        /// ЧУЖОГО родителя, неотличимой по положению (`S64`) — `Xray-W` 59.318
        /// вместо `Am-241` 59.541, `Ac-228` 1459.14 вместо `K-40` 1460.82, —
        /// и подпись законной линии в чужой сцене (`Pa-234m` 1001.0 на
        /// сумме двух аннигиляционных 1022 кэВ в спектрах Na-22: промах
        /// 0.03–0.36 ПШПВ, выход 0.842 %). Ни окно по промаху, ни окно по
        /// выходу их не берут по построению — различает такие пары только
        /// СОСТАВ ОСТАЛЬНОГО спектра, а он сюда не приходит.
        /// </summary>
        /// <summary>
        /// ПШПВ пика В КЭВ из ПШПВ в каналах — тем же выражением, каким её
        /// читает панель поиска пиков (<c>DCPeakDetectionView</c>).
        /// Ноль значит «не измерена»; отбор подписи тогда обходится без окна
        /// по разрешению, а не подставляет выдуманную ширину.
        /// </summary>
        static double FwhmKev(Peak peak, EnergyCalibration calibration)
        {
            if (calibration == null || !(peak.FWHM > 0.0) || Double.IsNaN(peak.FWHM))
            {
                return 0.0;
            }
            return Math.Abs(calibration.ChannelToEnergy(peak.Channel + peak.FWHM / 2.0)
                            - calibration.ChannelToEnergy(peak.Channel - peak.FWHM / 2.0));
        }

        NuclideDefinition MatchNuclide(Peak peak, double tol, NuclideSet nuclideSet, double fwhmKev)
        {
            NuclideDefinition bestNuclide = null;
            double minDelta = Double.MaxValue;

            // Окно по разрешению. Ширина приходит уже В КЭВ (см. вызов); ноль
            // значит «ПШПВ не измерена» — тогда второго окна просто нет, и
            // отбор остаётся прежним.
            double window = fwhmKev > 0.0 && !Double.IsNaN(fwhmKev)
                ? MaximumLabelMissInFwhm * fwhmKev
                : Double.PositiveInfinity;

            foreach (NuclideDefinition nuclideDefinition in this.nuclideDefinitions)
            {
                if (!nuclideDefinition.Visible || nuclideDefinition.Energy == 0.0) continue;
                if (nuclideSet != null && !nuclideDefinition.Sets.Contains(nuclideSet.Id)) continue;

                // Выход = 0 значит «не проставлен» (см. MinimumLabelYieldPercent).
                if (nuclideDefinition.Intencity > 0.0
                    && nuclideDefinition.Intencity < MinimumLabelYieldPercent) continue;

                double miss = Math.Abs(peak.Energy - nuclideDefinition.Energy);
                if (miss > window) continue;

                double delta = miss / nuclideDefinition.Energy;
                if (delta < tol / 100.0 && delta < minDelta)
                {
                    bestNuclide = nuclideDefinition;
                    minDelta = delta;
                }
            }

            return bestNuclide;
        }

        FWHMPeakDetector.PeakFinder PeakFinder(EnergySpectrum energySpectrum, FWHMPeakDetectionMethodConfig peakConfig, FwhmCalibration fwhmCalibration)
        {
            int min_range_ch, max_range_ch;
            SearchRangeChannels(energySpectrum, peakConfig, out min_range_ch, out max_range_ch);

            double fwhm_tol_min = ((double)peakConfig.Min_FWHM_Tol) / 100;
            double fwhm_tol_max = ((double)peakConfig.Max_FWHM_Tol) / 100;

            FWHMPeakDetector.Spectrum spec = new FWHMPeakDetector.Spectrum(energySpectrum);
            int concat = Math.Max(1, peakConfig.Ch_Concat);
            int mul = energySpectrum.NumberOfChannels / concat;
            if (mul > 1)
            {
                spec.combine_bins(mul);
            }
            FWHMPeakDetector.PeakFilter kernel = new FWHMPeakDetector.PeakFilter(fwhmCalibration);
            FWHMPeakDetector.PeakFinder finder = new FWHMPeakDetector.PeakFinder(
                spec,
                kernel,
                fwhm_tol_min: fwhm_tol_min,
                fwhm_tol_max: fwhm_tol_max);
            finder.find_peaks(
                min_range_ch,
                max_range_ch,
                peakConfig.Min_SNR,
                peakConfig.Max_Items);
            return finder;
        }

        NuclideDefinitionManager nuclideManager = NuclideDefinitionManager.GetInstance();

        // Снимок NuclideDefinitions на время одного прогона DetectPeak.
        List<NuclideDefinition> nuclideDefinitions;
    }
}
