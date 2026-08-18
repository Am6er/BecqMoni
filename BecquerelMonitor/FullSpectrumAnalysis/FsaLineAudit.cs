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
    /// САМОГО АНАЛИЗАТОРА (<see cref="FsaResult.Continuum"/>) и вычитается из
    /// данных и из модели ОДНОЙ И ТОЙ ЖЕ величиной. Вычитаемый фон снимается с
    /// данных заранее: модель его не содержит.
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
    /// превышает порог решения Карри для этого окна: ожидание ≥ k·√B, где
    /// B — континуум плюс фон под окном, k = 1.645 (α = 5 %). Такой порог сам
    /// учитывает и разрешение группы (ширина окна берётся из ПШПВ), и набранную
    /// статистику — то есть именно то, чего требовала строка `S57`: «порог
    /// вывести замером, а не назначить».
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
        public static List<LineCheck> Run(EnergySpectrum spectrum, FsaResult result,
                                          FwhmCalibration fwhmCalibration,
                                          List<FsaComponent> library)
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
            double[] model = result.Model;
            double[] background = result.Background;

            // Амплитуды состава: по имени. Компонент, которого в разложении нет
            // (отсеян по значимости), проверять нечем — его модель пуста, и
            // «ожидание ноль» ничего не сказало бы.
            var byName = new Dictionary<string, FsaComponentResult>(StringComparer.OrdinalIgnoreCase);
            foreach (FsaComponentResult c in result.Components)
            {
                byName[c.Name] = c;
            }

            foreach (FsaComponent component in library)
            {
                // Мешающие образы (рентген, вылет, аннигиляция) пропускаются:
                // у них нет выхода на распад, «обязана быть» для них не
                // определено, и порог Карри считать не от чего.
                if (component.Kind == FsaComponentKind.Nuisance)
                {
                    continue;
                }

                FsaComponentResult fitted;
                if (!byName.TryGetValue(component.Name, out fitted) || fitted.Curve == null)
                {
                    continue;
                }

                List<LineGroup> groups = Group(component, fwhmCalibration,
                                               spectrum.EnergyCalibration, channels,
                                               result.Gain, result.OffsetChannels);
                foreach (LineGroup group in groups)
                {
                    LineCheck check = Measure(group, data, model, result.Continuum, background,
                                              fitted.Curve, result.FirstChannel,
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
        /// </summary>
        static List<LineGroup> Group(FsaComponent component, FwhmCalibration fwhmCalibration,
                                     EnergyCalibration calibration, int channels,
                                     double gain, double offset)
        {
            var raw = new List<LineGroup>();
            foreach (FsaLine line in component.Lines)
            {
                if (!(line.Energy > 0.0) || !(line.Intensity > 0.0))
                {
                    continue;
                }

                double position;
                try
                {
                    position = calibration.EnergyToChannel(line.Energy, maxChannels: channels);
                }
                catch (Exception)
                {
                    continue;
                }

                if (double.IsNaN(position) || double.IsInfinity(position))
                {
                    continue;
                }

                double p = gain * position + offset;
                double fwhm = fwhmCalibration.ChannelToFwhm(p);
                if (!(fwhm > 0.0) || double.IsNaN(fwhm) || p < 0.0 || p > channels - 1)
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
                                 double[] background, double[] own,
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
            double variance = 0.0, baseline = 0.0;
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
                // складывается с дисперсией переднего плана — отсюда
                // `data + bg`, а не `data − bg`.
                variance += data[i] + bg;
                baseline += cont + bg;
            }

            double sigma = variance > 0.0 ? Math.Sqrt(variance) : 0.0;
            double threshold = DecisionK * Math.Sqrt(Math.Max(baseline, 0.0));

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
