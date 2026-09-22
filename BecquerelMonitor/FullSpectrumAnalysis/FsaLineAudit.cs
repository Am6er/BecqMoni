using System;
using System.Collections.Generic;
using System.Globalization;

namespace BecquerelMonitor.FullSpectrumAnalysis
{
    /// <summary>
    /// Кросс-проверка разбора по линиям, которые в спектре ОБЯЗАНЫ быть
    /// (`S60`, задание Amber 18.08.2026).
    ///
    /// ЗАЧЕМ. Матрица отклика считается из геометрии и в разборе не проверяется
    /// НИЧЕМ: соврала она или нет, разложение всё равно даёт правдоподобные
    /// числа. Ровно на этом прожили `E31` («матрица молча не подхватывалась»,
    /// пик модели ниже настоящего на 29 %) и `B14` (37 спектров понятной части
    /// считались без матрицы, назвавшись понятными). После `S56` у каждого
    /// спектра есть объявленный состав, а значит и список линий с выходами —
    /// то есть появилось, с чем сверять.
    ///
    /// ⛔ ЧТО ИМЕННО ЗДЕСЬ НЕ ТАВТОЛОГИЯ. Сравнивать «модель против данных» по
    /// всему спектру бессмысленно: фит их и сводил. Но у компонента ОДНА
    /// свободная амплитуда на ВСЕ его линии, а линий у Ac-228 сорок три, от 99
    /// до 1630 кэВ. Относительные высоты этих линий фит не подгонял — их задают
    /// выходы из `nucdb`, кривая эффективности и ДОЛЯ ПИКА из матрицы. Значит у
    /// компонента с N линиями проверяемых степеней свободы N − 1, и врущая
    /// матрица выдаёт себя не величиной расхождения, а его ХОДОМ ПО ЭНЕРГИИ.
    /// Поэтому итог печатается полосами энергии, а не одним числом.
    ///
    /// КАК СЧИТАЕТСЯ ПЛОЩАДЬ. Окно ±1 ПШПВ вокруг линии; подложка берётся У
    /// САМОГО АНАЛИЗАТОРА и вычитается из данных и из модели ОДНОЙ И ТОЙ ЖЕ
    /// величиной. Вычитаемый фон снимается с данных заранее: модель его не
    /// содержит.
    ///
    /// ⚠ (`S173`) Подложка здесь — КОНТИНУУМ ФИТА (<see cref="FsaResult.FitContinuum"/>:
    /// сплайн ПЛЮС отвязанные хвосты матричных образов), а не
    /// <see cref="FsaResult.Continuum"/> и не <see cref="FsaResult.Model"/>
    /// стека. С 14.09.2026 хвосты на экране идут невязкой (решение Amber), но
    /// эта сверка судит МАТРИЦУ по высотам линий, и то, что под окном линии
    /// забрала свободная колонка, — описание подложки фитом, а не промах
    /// матрицы; снимать только сплайн значило бы вменить матрице чужое, и
    /// сверка внизу шкалы врала бы ровно на хвост. Модель для окна берётся тем
    /// же движением (<see cref="FsaResult.FitModel"/>), так что разность
    /// «модель − подложка» под окном — по-прежнему сумма образов, а числа
    /// сверки при переносе хвоста в невязку не сдвинулись ни на знак.
    ///
    /// ⛔ **Оценивать подложку по боковым полосам НЕЛЬЗЯ, и это измерено, а не
    /// выведено.** Первая редакция брала её линейной по двум полосам 1.5…3 ПШПВ,
    /// как принято в пиковой спектрометрии, — и на трёх пробных спектрах **35
    /// площадей из 80 вышли ОТРИЦАТЕЛЬНЫМИ**. Причина не в арифметике: прямая
    /// через две боковые полосы есть ХОРДА, а комптоновский континуум выпуклый,
    /// и на ширине окна сцинтиллятора (у ASN16 ±1 ПШПВ это десятки каналов)
    /// хорда идёт заметно ВЫШЕ кривой. Классический приём молча требует окна,
    /// узкого против кривизны, а у нас его нет. Континуум анализатора этой беды
    /// не имеет: он не оценивается заново, он уже посчитан фитом.
    ///
    /// ⚠ ЛИНИИ, КОТОРЫЕ ПРИБОР НЕ РАЗДЕЛЯЕТ, СЛИВАЮТСЯ В ОДНУ ЗАПИСЬ. Считать
    /// их порознь значило бы поделить одну площадь на два ожидания и получить
    /// вдвое заниженное согласие на ровном месте. Порог слияния — та же ПШПВ,
    /// что у окна: чего не разделяет детектор, того не разделяем и мы.
    ///
    /// ЧТО ЗНАЧИТ «ОБЯЗАНА БЫТЬ» — выводится, а не назначается. Линия обязана
    /// быть видна, если площадь, предсказанная ЕЁ СОБСТВЕННЫМ компонентом,
    /// превышает порог решения Карри для этого окна: ожидание ≥ k·σ₀, где
    /// σ₀² — дисперсия чистой площади ПРИ ОТСУТСТВИИ пика: континуум плюс
    /// фон под окном (пуассоновский шум брутто-счёта) плюс дисперсия
    /// ВЫЧИТАЕМОГО фона s·B (см. ниже), k = 1.645 (α = 5 %). Такой порог сам
    /// учитывает и разрешение группы (ширина окна берётся из ПШПВ), и набранную
    /// статистику — то есть именно то, чего требовала строка `S57`: «порог
    /// вывести замером, а не назначить».
    ///
    /// ⛔ (`S179`, П126 22.09.2026) ФОН ВЫЧИТАЕТСЯ В МАСШТАБЕ ЖИВОГО ВРЕМЕНИ, И
    /// ЕГО ДИСПЕРСИЯ — ТОЖЕ. <see cref="FsaResult.Background"/> — это
    /// s·N_фона, где s = T/T_фона (`FsaAnalyzer`, `backgroundScale`), и
    /// Var(s·N_фона) = s²·N_фона = s·B, а не B: так считает сам анализатор
    /// (`FsaAnalyzer`, «дисперсия — от полного фона», `full·scale`). До
    /// 22.09.2026 здесь стояло `variance += data + bg` — при фоне в 30 раз
    /// длиннее пробы (G1S, 1800 с против 54 000 с) вклад фона в дисперсию
    /// завышался в 1/s = 30 раз; измерено на `G1S24_K40_Petri_2` (s = 0.169,
    /// 511 кэВ): σ 90.0 → 71.1, |Z| 0.70 → 0.89. Порог решения члена фона не
    /// содержал вовсе. Формула порога — Карри (Currie L.A., Anal. Chem. 40
    /// (1968) 586, «critical level» L_C = k·σ₀): σ₀² = Var(брутто) +
    /// Var(вычитаемого) = (C + B) + s·B; при s = 1 и C = 0 это его же
    /// 2.33·√B «парных наблюдений», при s → 0 — 1.645·√B «хорошо известного
    /// фона». Дисперсия континуума фита, как и прежде, не учитывается (он
    /// берётся как есть у обеих сторон сравнения).
    ///
    /// ⚠ (П126) РЯД — ОДНА КОЛОНКА, А ЕГО РЕЗУЛЬТАТ — ПО ЧЛЕНАМ. Компонент
    /// вида <see cref="FsaComponentKind.Chain"/> (и хозяин привязки `S171`)
    /// несёт линии всех членов под именем корня, а анализатор раскладывает
    /// его колонку в <see cref="FsaResult.Components"/> ПОЧЛЕННО
    /// (<see cref="FsaComponentResult.ChainRoot"/>, <see cref="FsaComponentResult.TiedTo"/>).
    /// Поиск результата «по имени компонента» находил либо ничего (Th-232 без
    /// линий — ряд пропускался целиком), либо крошечный образ головы (Rn-222:
    /// 62 отсчёта из 168 000 ряда) — и все линии ряда получали чистоту 0.000 и
    /// «не обязана». Измерено 22.09.2026 до правки: `G1S24_Th232_Petri` —
    /// обязательных 1 из 25, `G1S24_Rn222Coal_Mar_eq01` — 0 из 42. Теперь
    /// «свой» образ ряда — сумма образов всех его членов
    /// (<see cref="OwnCurve"/>): амплитуда у них одна, и именно этим и держится
    /// довод о N − 1 проверяемых степенях свободы из шапки.
    ///
    /// ⚠ Порог считается по СВОЕМУ компоненту, а не по всей модели окна. Иначе
    /// сосед-гигант объявлял бы обязательной линию, которой в спектре нет
    /// вовсе: на первом прогоне так вышло у Pb-210 46.5 кэВ под крылом
    /// тория.
    /// </summary>
    public static class FsaLineAudit
    {
        /// <summary>Итог по одной разрешимой линии (или слитой группе линий).</summary>
        public sealed class LineCheck
        {
            /// <summary>Компонент, которому принадлежит группа.</summary>
            public string Component;

            /// <summary>Энергия, кэВ, взвешенная по выходам внутри группы.</summary>
            public double EnergyKev;

            /// <summary>Сколько линий слилось в эту запись.</summary>
            public int Lines;

            /// <summary>Суммарный выход группы, % на распад родителя.</summary>
            public double IntensityPct;

            /// <summary>Площадь пика по МОДЕЛИ, отсчёты (подложка снята).</summary>
            public double Expected;

            /// <summary>Площадь пика ИЗМЕРЕННАЯ, отсчёты (фон и подложка сняты).</summary>
            public double Measured;

            /// <summary>Разброс измеренной площади, отсчёты.</summary>
            public double Sigma;

            /// <summary>Порог решения Карри для этого окна, отсчёты.</summary>
            public double DecisionThreshold;

            /// <summary>
            /// Доля ожидаемой площади, принадлежащая СВОЕМУ компоненту. Ниже
            /// единицы — в окне сидит сосед, и расхождение может быть его.
            /// Читать таблицу без этой колонки нельзя.
            /// </summary>
            public double Purity;

            /// <summary>Линия обязана быть видна: ожидание выше порога решения.</summary>
            public bool Obligatory;

            /// <summary>(измерено − ожидание) / σ. Плюс — данных больше модели.</summary>
            public double Z
            {
                get
                {
                    return this.Sigma > 0.0 ? (this.Measured - this.Expected) / this.Sigma : double.NaN;
                }
            }

            /// <summary>
            /// Отношение измеренного к ожидаемому. Именно оно, а не Z, говорит о
            /// МАТРИЦЕ: Z растёт со статистикой и на спектре в сто миллионов
            /// отсчётов кричит там, где расхождение ничтожно, а отношение
            /// сравнимо поперёк корпуса, где счета разнятся в тысячи раз.
            /// </summary>
            public double Ratio
            {
                get
                {
                    return this.Expected > 0.0 ? this.Measured / this.Expected : double.NaN;
                }
            }
        }

        /// <summary>Множитель квантиля порога решения: α = 5 %.</summary>
        public const double DecisionK = 1.645;

        /// <summary>Полуширина окна в долях ПШПВ.</summary>
        const double WindowFwhm = 1.0;

        /// <summary>
        /// Сверка по всем линиям состава. Пустой список — сверять нечего (нет
        /// калибровок либо ни одна линия не попала в окно фита); это результат,
        /// а не отказ.
        /// </summary>
        /// <param name="backgroundSpectrum">
        /// (`S179`) Спектр фона, который анализатор ВЫЧЕЛ (тот же, что подан в
        /// <c>FsaAnalyzer.Analyze</c>); null — фона не было. Нужен ради масштаба
        /// s = T/T_фона: в <see cref="FsaResult"/> его нет, а без него дисперсия
        /// вычтенного фона считается в 1/s раз неверно. Разбор, ВЗЯВШИЙ фон
        /// (<see cref="FsaResult.BackgroundUsed"/>), без этого аргумента —
        /// отказ (<see cref="ArgumentException"/>), а не молчаливая единица:
        /// ровно молчаливая единица здесь и стояла до 22.09.2026.
        /// </param>
        public static List<LineCheck> Run(EnergySpectrum spectrum, FsaResult result,
                                          FwhmCalibration fwhmCalibration,
                                          List<FsaComponent> library,
                                          EnergySpectrum backgroundSpectrum)
        {
            var checks = new List<LineCheck>();
            if (spectrum == null || result == null || fwhmCalibration == null || library == null
                || result.Model == null || spectrum.Spectrum == null
                || spectrum.EnergyCalibration == null)
            {
                return checks;
            }

            int channels = spectrum.NumberOfChannels;
            int[] data = spectrum.Spectrum;
            // (`S173`) Модель и подложка — ФИТА, с отвязанными хвостами (см. шапку).
            double[] model = result.FitModel();
            double[] continuum = result.FitContinuum();
            double[] background = result.Background;
            double backgroundScale = BackgroundScale(spectrum, result, backgroundSpectrum);

            foreach (FsaComponent component in library)
            {
                // Мешающие образы (рентген, вылет, аннигиляция) пропускаются:
                // у них нет выхода на распад, «обязана быть» для них не
                // определено, и порог Карри считать не от чего.
                if (component.Kind == FsaComponentKind.Nuisance)
                {
                    continue;
                }

                // Амплитуды состава: свой образ компонента — по имени, у ряда
                // и хозяина привязки — сумма образов членов (см. шапку).
                // Компонент, которого в разложении нет (отсеян по значимости),
                // проверять нечем — его модель пуста, и «ожидание ноль» ничего
                // не сказало бы.
                double[] own = OwnCurve(component, result.Components, channels);
                if (own == null)
                {
                    continue;
                }

                List<LineGroup> groups = Group(component, fwhmCalibration,
                                               spectrum.EnergyCalibration, channels,
                                               result.Gain, result.OffsetChannels,
                                               result.AnchorLightCurve, result.AnchorLightBeta,
                                               result.AnchorLightReferenceKev,
                                               result.AdcScale, result.AdcE0Kev, result.AdcZeroKev);
                foreach (LineGroup group in groups)
                {
                    LineCheck check = Measure(group, data, model, continuum, background,
                                              backgroundScale, own, result.FirstChannel,
                                              result.LastChannel, channels);
                    if (check != null)
                    {
                        check.Component = component.Name;
                        checks.Add(check);
                    }
                }
            }

            return checks;
        }

        // ------------------------------------------------------------------

        /// <summary>
        /// (`S179`) Масштаб вычтенного фона s = T/T_фона — тем же правилом, что
        /// у анализатора (<c>EnergySpectrum.EffectiveLiveTime</c> обеих сторон,
        /// `AMBER35`). Ноль — фон не вычитался; тогда слагаемых фона в дисперсии
        /// нет и масштаб не нужен.
        /// </summary>
        static double BackgroundScale(EnergySpectrum spectrum, FsaResult result,
                                      EnergySpectrum backgroundSpectrum)
        {
            if (!result.BackgroundUsed || result.Background == null)
            {
                return 0.0;
            }

            if (backgroundSpectrum == null)
            {
                throw new ArgumentException(
                    "FsaLineAudit.Run: разбор вычел фон (BackgroundUsed), а спектр фона не подан — "
                    + "без него масштаб T/T_фона дисперсии фона неизвестен (S179)",
                    "backgroundSpectrum");
            }

            double live = spectrum.EffectiveLiveTime;
            double backgroundLive = backgroundSpectrum.EffectiveLiveTime;
            if (!(live > 0.0) || !(backgroundLive > 0.0))
            {
                throw new ArgumentException(string.Format(CultureInfo.InvariantCulture,
                    "FsaLineAudit.Run: живое время пробы {0} или фона {1} не положительно — "
                    + "анализатор такой разбор не делает, сверять нечего (S179)",
                    live, backgroundLive), "backgroundSpectrum");
            }

            return live / backgroundLive;
        }

        /// <summary>
        /// Свой образ компонента в результате: сумма кривых всех строк
        /// результата, принадлежащих ЭТОЙ колонке фита — одноимённой, членов
        /// ряда с этим корнем (<see cref="FsaComponentResult.ChainRoot"/>) и
        /// привязанных членов (`S171`, <see cref="FsaComponent.Ties"/>). null —
        /// в разложении колонки нет.
        /// </summary>
        static double[] OwnCurve(FsaComponent component, List<FsaComponentResult> results, int channels)
        {
            if (results == null)
            {
                return null;
            }

            double[] own = null;
            foreach (FsaComponentResult r in results)
            {
                if (r.Curve == null || !BelongsTo(component, r))
                {
                    continue;
                }

                if (own == null)
                {
                    own = new double[channels];
                }

                int n = Math.Min(channels, r.Curve.Length);
                for (int i = 0; i < n; i++)
                {
                    own[i] += r.Curve[i];
                }
            }

            return own;
        }

        static bool BelongsTo(FsaComponent component, FsaComponentResult r)
        {
            if (string.Equals(r.Name, component.Name, StringComparison.OrdinalIgnoreCase)
                || string.Equals(r.ChainRoot, component.Name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (component.Ties != null)
            {
                foreach (FsaTie tie in component.Ties)
                {
                    if (string.Equals(tie.Member, r.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        sealed class LineGroup
        {
            public double EnergyKev;
            public double IntensityPct;
            public int Lines;

            /// <summary>Положение в каналах С УЧЁТОМ дрейфа, найденного фитом.</summary>
            public double Channel;

            /// <summary>ПШПВ в каналах на этом положении.</summary>
            public double FwhmChannels;
        }

        /// <summary>
        /// Линии компонента, слитые по разрешимости. Положение считается ТЕМ ЖЕ
        /// ходом, каким его считает анализатор: сперва энергия в канал по
        /// калибровке спектра, затем найденный фитом дрейф
        /// <c>p = gain·position + offset</c>. Иначе окно встанет мимо пика на
        /// величину дрейфа, а он у корпуса доходит до трёх каналов.
        ///
        /// ⛔ (`S181`, П136 22.09.2026) «ТЕМ ЖЕ ХОДОМ» ВКЛЮЧАЕТ КАРТУ НУЛЯ
        /// «adc». Анализатор кладёт каждый бин образа не прямой калибровкой, а
        /// через E꜀(x) = E(0) + (x − z₀)·s (<c>FsaAnalyzer.LightEnergyKev</c>,
        /// `S169`); сверка этого не делала и расходилась с образом на
        /// Δ(x) = (s − 1)(x − x₁) — при E(0) = −13 кэВ и верхней линии 2614 это
        /// −11.8 кэВ на 238 кэВ, больше половины ПШПВ NaI, а при E(0) = −44 —
        /// две ПШПВ, то есть окно ±1 ПШПВ мимо пика целиком. `gain`/`offset`
        /// не спасали: они найдены МНК опор ПОВЕРХ карты. Расхождение нулевое
        /// только у верхней линии библиотеки, поэтому глазом его не видно.
        ///
        /// ⚠ Карта включается лишь при живой матрице и включённой привязке
        /// (<c>FsaAnalyzer.AnchorScale</c>): на спектрах без матрицы `adcScale`
        /// равен нулю, и здесь всё идёт прежней прямой — до бита.
        /// </summary>
        static List<LineGroup> Group(FsaComponent component, FwhmCalibration fwhmCalibration,
                                     EnergyCalibration calibration, int channels,
                                     double gain, double offset,
                                     string lightCurve, double lightBeta, double lightReferenceKev,
                                     double adcScale, double adcE0Kev, double adcZeroKev)
        {
            var raw = new List<LineGroup>();
            foreach (FsaLine line in component.Lines)
            {
                if (!(line.Energy > 0.0) || !(line.Intensity > 0.0))
                {
                    continue;
                }

                // (`S181`) Энергия калибровки, отвечающая свету линии, — то же
                // число, какое анализатор отдаёт калибровке для бинов образа.
                double lineKev = adcScale > 0.0
                    ? adcE0Kev + (line.Energy - adcZeroKev) * adcScale
                    : line.Energy;

                double position;
                try
                {
                    position = calibration.EnergyToChannel(lineKev, maxChannels: channels);
                }
                catch (Exception)
                {
                    continue;
                }

                if (double.IsNaN(position) || double.IsInfinity(position))
                {
                    continue;
                }

                // (`F11` (в), П18) световая координата привязки — тот же счёт,
                // что у таблицы анализатора; без неё (β = 0) — прежняя прямая.
                double p = gain * position + offset
                           + (lightBeta != 0.0
                              ? lightBeta * FsaLightScale.ShiftChannels(lightCurve, calibration, position, channels, lightReferenceKev)
                              : 0.0);
                double fwhm = fwhmCalibration.ChannelToFwhm(p);
                if (!(fwhm > 0.0) || double.IsNaN(fwhm) || double.IsInfinity(fwhm)
                    || p < 0.0 || p > channels - 1)
                {
                    continue;
                }

                raw.Add(new LineGroup
                {
                    EnergyKev = line.Energy,
                    IntensityPct = line.Intensity,
                    Lines = 1,
                    Channel = p,
                    FwhmChannels = fwhm,
                });
            }

            raw.Sort((a, b) => a.Channel.CompareTo(b.Channel));

            var merged = new List<LineGroup>();
            foreach (LineGroup line in raw)
            {
                LineGroup last = merged.Count > 0 ? merged[merged.Count - 1] : null;
                if (last != null && Math.Abs(line.Channel - last.Channel) < last.FwhmChannels)
                {
                    // Слияние с весом по выходу: центр группы — там, где на
                    // самом деле стоит её тяжесть.
                    double weight = last.IntensityPct + line.IntensityPct;
                    last.EnergyKev = (last.EnergyKev * last.IntensityPct
                                      + line.EnergyKev * line.IntensityPct) / weight;
                    last.Channel = (last.Channel * last.IntensityPct
                                    + line.Channel * line.IntensityPct) / weight;
                    last.IntensityPct = weight;
                    last.Lines++;
                    continue;
                }

                merged.Add(line);
            }

            return merged;
        }

        /// <summary>
        /// Площадь пика в окне у данных и у модели, одной и той же процедурой.
        /// null — окно с боковыми полосами не помещается в диапазон фита.
        /// </summary>
        static LineCheck Measure(LineGroup group, int[] data, double[] model, double[] continuum,
                                 double[] background, double backgroundScale, double[] own,
                                 int firstChannel, int lastChannel, int channels)
        {
            int lo = (int)Math.Floor(group.Channel - WindowFwhm * group.FwhmChannels);
            int hi = (int)Math.Ceiling(group.Channel + WindowFwhm * group.FwhmChannels);

            // Окно обязано лежать внутри диапазона фита: за его краем модель
            // молчит, и сравнение вышло бы «модель ноль против данных сколько
            // есть».
            if (lo < Math.Max(0, firstChannel) || hi > Math.Min(channels - 1, lastChannel))
            {
                return null;
            }

            double measured = 0.0, expected = 0.0, expectedOwn = 0.0;
            double variance = 0.0, nullVariance = 0.0;
            for (int i = lo; i <= hi; i++)
            {
                double bg = background != null ? background[i] : 0.0;
                double cont = continuum != null ? continuum[i] : 0.0;

                // Подложка — континуум анализатора; она снимается с ОБЕИХ
                // сторон сравнения одинаково, поэтому в разность не входит и
                // испортить её не может.
                measured += data[i] - bg - cont;
                expected += model[i] - cont;
                expectedOwn += own[i];

                // Фон ВЫЧИТАЕТСЯ, а не делится, поэтому его дисперсия
                // складывается с дисперсией переднего плана — отсюда плюс, а
                // не минус. (`S179`) `bg` — уже s·N_фона, и его дисперсия —
                // s²·N_фона = s·bg, как у анализатора; `data + bg` считало бы
                // фон набранным за время пробы.
                double backgroundVariance = backgroundScale * bg;
                variance += data[i] + backgroundVariance;

                // Порог решения Карри — от дисперсии чистой площади ПРИ
                // ОТСУТСТВИИ пика: брутто-счёт тогда равен подложке плюс фон
                // (пуассон: cont + bg), плюс дисперсия вычитаемого фона.
                nullVariance += cont + bg + backgroundVariance;
            }

            double sigma = variance > 0.0 ? Math.Sqrt(variance) : 0.0;
            double threshold = DecisionK * Math.Sqrt(Math.Max(nullVariance, 0.0));

            return new LineCheck
            {
                EnergyKev = group.EnergyKev,
                Lines = group.Lines,
                IntensityPct = group.IntensityPct,
                Expected = expected,
                Measured = measured,
                Sigma = sigma,
                DecisionThreshold = threshold,

                // ⚠ Чистота считается от СВОЕГО вклада к полному ожиданию и
                // зажимается единицей. Без зажима она вылезала выше неё на
                // первом прогоне (у Tl-208 860.6 вышло 1.59): полное ожидание
                // окна может оказаться меньше вклада одного компонента, когда
                // сосед в этом окне ушёл в минус. Число выше единицы читателю
                // ничего не говорит, а колонку он читает как долю.
                Purity = expected > 0.0 ? Math.Min(1.0, expectedOwn / expected) : 0.0,

                // Обязательность — по СВОЕМУ компоненту. Сосед-гигант не должен
                // объявлять обязательной линию, которой в спектре нет.
                Obligatory = expectedOwn >= threshold && expectedOwn > 0.0,
            };
        }

        /// <summary>
        /// Полосы энергии, по которым печатается итог. Врущая матрица выдаёт
        /// себя ХОДОМ отношения по энергии, а не одним числом: доля пика падает
        /// с энергией, и ошибка в ней перекошена туда же.
        /// </summary>
        public static readonly double[] Bands = { 0.0, 100.0, 200.0, 400.0, 800.0, 1600.0, 1.0e9 };

        /// <summary>Имя полосы для печати.</summary>
        public static string BandName(int index)
        {
            if (index < 0 || index >= Bands.Length - 1)
            {
                return "?";
            }

            return Bands[index + 1] > 1.0e8
                ? string.Format(CultureInfo.InvariantCulture, "{0,5:F0}+     ", Bands[index])
                : string.Format(CultureInfo.InvariantCulture, "{0,5:F0}…{1,-5:F0}",
                                Bands[index], Bands[index + 1]);
        }

        /// <summary>Номер полосы по энергии; −1 — вне полос.</summary>
        public static int BandOf(double energyKev)
        {
            for (int i = 0; i + 1 < Bands.Length; i++)
            {
                if (energyKev >= Bands[i] && energyKev < Bands[i + 1])
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>Медиана списка; NaN на пустом.</summary>
        public static double Median(List<double> values)
        {
            if (values == null || values.Count == 0)
            {
                return double.NaN;
            }

            var sorted = new List<double>(values);
            sorted.Sort();
            int n = sorted.Count;
            return n % 2 == 1 ? sorted[n / 2] : 0.5 * (sorted[n / 2 - 1] + sorted[n / 2]);
        }
    }
}
