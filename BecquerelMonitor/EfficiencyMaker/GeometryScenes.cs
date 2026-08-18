using System;
using System.Collections.Generic;

namespace BecquerelMonitor.EfficiencyMaker
{
    /// <summary>
    /// Размеры двух геометрий съёмки в поле — «детектор на земле» и «детектор в
    /// лунке» (E27, 16.08.2026). Они стоят в списке источников рядом с точкой,
    /// цилиндром и маринелли (<see cref="GeometrySceneKind"/>), и отличаются от
    /// соседей не формой, а тем, ОТКУДА берутся их размеры.
    ///
    /// Зачем это отдельным правилом. У банки размеры меряют линейкой, а здесь
    /// вопрос другой: докуда считать грунт, чтобы сцена держала сигнал. Ответ —
    /// в свободных пробегах, а не в сантиметрах, и человек его на глаз не
    /// назовёт.
    ///
    /// Что здесь НЕ считается: сама эффективность. Правило расставляет размеры
    /// формулами за одно движение — «просто формулы для быстрого изменения
    /// размеров», постановка Amber, — а считает по ним обычный расчёт прежним
    /// кодом.
    ///
    /// ⛔ Полупространства в формате нет. Прибор на земле — это полубесконечная
    /// среда, и её приходится подменять цилиндром «побольше»; насколько побольше
    /// — и есть предмет формул ниже. Лунку подменять не нужно: это в точности
    /// маринелли (колодец = лунка, проба вокруг и снизу, стенок сосуда нет).
    ///
    /// Откуда коэффициенты. Посчитаны нерассеянным потоком от равномерно
    /// активного грунта — `tools/effmaker/ground_halfspace.py` (полупространство,
    /// радиальный интеграл берётся аналитически через E₁) и
    /// `tools/effmaker/borehole.py` (лунка, счёт по направлениям из точки
    /// кристалла). Оба сверены: полупространство — лобовым двумерным
    /// интегрированием (сходится до 0.01 %) и полем зрения врезной съёмки на
    /// высоте 1 м (90 % с радиуса 8 м — как в литературе по in-situ), лунка —
    /// с прежними числами журнала (0.3 м радиуса и −3 % за широкую лунку
    /// воспроизвелись).
    ///
    /// ⚠ Считался только НЕРАССЕЯННЫЙ поток. Для площади фотопика это и есть
    /// ответ (рассеявшийся квант в пик не попадает), для континуума сцену надо
    /// брать шире.
    /// </summary>
    public static class GeometryScenes
    {
        /// <summary>
        /// Разложить размеры сцены по её виду. Возвращает имя вещества, которое
        /// пришлось подставить пробе, или пустую строку; для обычных сцен не
        /// делает ничего.
        ///
        /// <paramref name="topEnergyKev"/> — ВЕРХНЯЯ энергия расчёта, см.
        /// <see cref="MeanFreePathMm"/>.
        /// </summary>
        public static string Apply(GeometryModel g, double topEnergyKev)
        {
            switch (g.Scene)
            {
                case GeometrySceneKind.Ground:
                    return Ground(g, topEnergyKev);

                case GeometrySceneKind.Borehole:
                    return Borehole(g, topEnergyKev);

                default:
                    return "";
            }
        }

        // ------------------------------------------------------------------
        // Коэффициенты сцен, в свободных пробегах пробы
        // ------------------------------------------------------------------

        /// <summary>Глубина цилиндра на грунте: 3.5 пробега.</summary>
        public const double GroundDepthMfp = 3.5;

        /// <summary>
        /// Радиус цилиндра на грунте: 4 пробега плюс 60 высот кристалла над
        /// грунтом. Второе слагаемое — не украшение, а главный член: у
        /// приподнятого кристалла далёкий грунт виден под скользящими углами,
        /// где путь по грунту короткий, и хвост спадает всего как h/R. Прибор,
        /// лежащий вплотную (h → 0), обходится 3.9 пробега; кристалл на 3 см
        /// требует уже полутора метров.
        /// </summary>
        public const double GroundRadiusMfp = 4.0;

        /// <summary>Сколько радиуса добавляет высота кристалла над грунтом.</summary>
        public const double GroundRadiusPerHeight = 60.0;

        /// <summary>
        /// Глубина лунки: 3 пробега. Это НЕ размер сцены, а совет, сколько
        /// копать: глубже счёт не растёт (3 пробега дают 98 % того, что даёт
        /// бесконечно глубокая лунка). У кого лунка своя — поле правится.
        /// </summary>
        public const double BoreholeDepthMfp = 3.0;

        /// <summary>Грунт вокруг лунки, от её стенки: 4 пробега.</summary>
        public const double BoreholeSideMfp = 4.0;

        /// <summary>Грунт под дном лунки: 3 пробега.</summary>
        public const double BoreholeBottomMfp = 3.0;

        /// <summary>
        /// Зазор лунки: по 10 мм на сторону шире прибора и 20 мм под носом.
        /// Ширина почти бесплатна — лунка Ø200 против Ø40 отнимает 3 %, потому
        /// что вынутый грунт стоял бы столбом над прибором и занимает малый
        /// телесный угол, — а лопата тесной ямы не роет.
        /// </summary>
        public const double BoreholeClearanceMm = 10.0;

        /// <summary>Нос прибора над дном лунки, мм.</summary>
        public const double BoreholeStandoffMm = 20.0;

        /// <summary>
        /// Плотность, ниже которой вещество пробы считается ГАЗОМ и сценой быть
        /// не может: 0.05 г/см³. Воздух в заготовке стоит нарочно (это «пробы
        /// ещё нет»), но пробег в нём стометровый, и сцена по нему выходит в
        /// сорок метров.
        /// </summary>
        public const double GasDensity = 0.05;

        /// <summary>Чем подменяется газ, когда сцену считать не по чему.</summary>
        public const string DefaultSampleMaterial = "Soil";

        // ------------------------------------------------------------------
        // Сцены
        // ------------------------------------------------------------------

        /// <summary>
        /// Прибор лежит на земле, лицом вниз. Грунт — цилиндр под ним, стенок
        /// нет, зазора нет.
        /// </summary>
        public static string Ground(GeometryModel g, double topEnergyKev)
        {
            string substituted = EnsureSample(g);
            double mfp = MeanFreePathMm(g.Source, topEnergyKev);
            double height = CrystalHeightAboveSampleMm(g);

            g.Scene = GeometrySceneKind.Ground;
            g.SourceType = GeometrySourceType.Cylinder;
            g.BeakerToDetectorDistance = 0.0;
            g.BeakerSideWallThickness = 0.0;
            g.BeakerEndWallThickness = 0.0;
            g.SourceHeight = GroundDepthMfp * mfp;
            g.BeakerDiameter = 2.0 * (GroundRadiusMfp * mfp + GroundRadiusPerHeight * height);
            // Расчёт высоту сосуда не читает (у цилиндра сцену задаёт высота
            // ПРОБЫ), но поле есть и уезжает в файл `.in`: пусть не врёт.
            g.BeakerHeight = g.SourceHeight;
            return substituted;
        }

        /// <summary>
        /// Прибор опущен в лунку. Колодец маринелли — сама лунка, проба вокруг
        /// и снизу — грунт, стенок сосуда нет ни у колодца, ни снаружи.
        ///
        /// Глубина лунки прибор НЕ подгоняется под длину прибора нарочно: если
        /// он длиннее лунки, его хвост торчит наружу, и грунт его законно не
        /// окружает — так оно и в поле.
        /// </summary>
        public static string Borehole(GeometryModel g, double topEnergyKev)
        {
            string substituted = EnsureSample(g);
            double mfp = MeanFreePathMm(g.Source, topEnergyKev);
            double hole = DetectorOuterDiameterMm(g) + 2.0 * BoreholeClearanceMm;

            g.Scene = GeometrySceneKind.Borehole;
            g.SourceType = GeometrySourceType.Marinelli;
            g.MarinelliHoleDiameter = hole;
            g.MarinelliToDetectorDistance = BoreholeStandoffMm;
            g.MarinelliHoleHeight = BoreholeDepthMfp * mfp;
            g.MarinelliSourceHeight = g.MarinelliHoleHeight + BoreholeBottomMfp * mfp;
            g.MarinelliBeakerDiameter = hole + 2.0 * BoreholeSideMfp * mfp;
            // Как и у цилиндра: расчёт высоту сосуда не читает, но поле уезжает
            // в файл, и стакан ниже своей пробы выглядел бы ошибкой.
            g.MarinelliBeakerHeight = g.MarinelliSourceHeight;

            // Сосуда нет: лунка — это дырка в грунте, а не стакан.
            g.MarinelliSideThickness = 0.0;
            g.MarinelliEndWallThickness = 0.0;
            g.MarinelliHoleSideThickness = 0.0;
            g.MarinelliHoleEndWallThickness = 0.0;
            return substituted;
        }

        // ------------------------------------------------------------------
        // Из чего считаются размеры
        // ------------------------------------------------------------------

        /// <summary>
        /// Свободный пробег в веществе пробы на заданной энергии, ММ.
        ///
        /// Энергия — ВЕРХНЯЯ энергия расчёта, а не 662 кэВ (решение Amber
        /// 16.08.2026). Пробег растёт с энергией вдвое от 662 к 3000 кэВ, и
        /// сцена, размеченная по 662, на 2614 держит только 93 % — то есть
        /// занижает ВЕРХ кривой относительно её низа. Это перекос формы, а не
        /// общий множитель, и вычесть его потом нечем.
        /// </summary>
        public static double MeanFreePathMm(GeometryMaterial sample, double energyKev)
        {
            double mu = sample != null ? sample.LinearAttenuation(energyKev) : 0.0;
            if (!(mu > 0.0))
            {
                return 0.0;
            }

            return GeometryModel.MmPerCm / mu;
        }

        /// <summary>
        /// Высота СЕРЕДИНЫ кристалла над поверхностью пробы, мм: зазор до пробы
        /// плюс передняя обвязка плюс половина глубины кристалла.
        ///
        /// Почему середина, а не передний торец. Скользящий луч из далёкого
        /// грунта входит в кристалл СБОКУ и взаимодействует по всей его глубине,
        /// а вклад далёкого грунта пропорционален высоте точки взаимодействия.
        /// Это допущение (взвешивание по глубине принято равномерным), и оно не
        /// мелочь: у кристалла 63 мм середина стоит на 3.7 см, а это вдвое
        /// больший радиус сцены, чем у переднего торца.
        ///
        /// Оправа сюда НЕ входит: по умолчанию она стоит за кристаллом
        /// (<c>EfficiencySimulator.MountingInFront</c> выключен), и между пробой
        /// и кристаллом её нет.
        /// </summary>
        public static double CrystalHeightAboveSampleMm(GeometryModel g)
        {
            double front = Math.Max(0.0, g.FrontReflectorThickness)
                           + Math.Max(0.0, g.FrontCladdingThickness);

            double depth;
            if (g.Shape == CrystalShape.Box)
            {
                double halfX, halfY;
                g.CrystalBoxInScene(out halfX, out halfY, out depth);
            }
            else
            {
                depth = g.CrystalHeight;
            }

            return front + 0.5 * Math.Max(0.0, depth);
        }

        /// <summary>
        /// Внешний поперечник прибора, мм — то, что должно пролезть в лунку:
        /// кристалл плюс боковой отражатель и корпус. У бруска берётся
        /// ДИАГОНАЛЬ обвязанного сечения: в круглую лунку он входит именно ею.
        /// </summary>
        public static double DetectorOuterDiameterMm(GeometryModel g)
        {
            double side = Math.Max(0.0, g.SideReflectorThickness)
                          + Math.Max(0.0, g.SideCladdingThickness);

            if (g.Shape == CrystalShape.Box)
            {
                double halfX, halfY, depth;
                g.CrystalBoxInScene(out halfX, out halfY, out depth);
                double x = halfX + side, y = halfY + side;
                return 2.0 * Math.Sqrt(x * x + y * y);
            }

            return g.CrystalDiameter + 2.0 * side;
        }

        // ------------------------------------------------------------------
        // Связки размеров (E33)
        // ------------------------------------------------------------------

        /// <summary>Несогласованный размер: за какое поле держаться и что не так.</summary>
        public sealed class Issue
        {
            /// <summary>Ключ поля редактора — по нему поле подсвечивается.</summary>
            public string Field;

            /// <summary>Имя строки ресурсов с текстом.</summary>
            public string Resource;

            /// <summary>Числа для подстановки в текст: что стоит и чему мешает.</summary>
            public double Value;

            /// <summary>Предел, в который размер обязан уложиться.</summary>
            public double Limit;
        }

        /// <summary>
        /// Размеры, которые поодиночке годны, а вместе невозможны (E33, задача
        /// Amber 17.08.2026).
        ///
        /// Зачем отдельной функцией, а не в <c>GeometryEditorPanel.Validate</c>.
        /// Проверка перед сохранением ловила только вырожденное — нулевой
        /// кристалл, нулевую плотность, колодец шире стакана, — и невозможная
        /// сцена принималась молча: расчёт доводился до конца и выдавал
        /// правдоподобную ЧУЖУЮ кривую. Правило же тут не про форму ввода, а про
        /// геометрию, поэтому живёт рядом с ней и проверяется пробой
        /// (<c>GeometryLimitsProbe</c>), а не глазами по экрану.
        ///
        /// ⚠ Смысл размеров взят у того, кто по ним СЧИТАЕТ (<see cref="SampleVolumeCm3"/>
        /// и <c>EfficiencySimulator</c>), а не у их названий: у цилиндра и короба
        /// размер ВНЕШНИЙ, и проба занимает просвет за вычетом стенок, а у
        /// маринелли проба — кольцо между колодцем и стенкой стакана.
        ///
        /// Чего здесь нет нарочно: «прибор длиннее лунки». Хвост прибора,
        /// торчащий наружу, — не ошибка, а обычная полевая съёмка, и
        /// <see cref="Borehole"/> глубину под прибор нарочно не подгоняет.
        /// </summary>
        public static List<Issue> Inconsistencies(GeometryModel g)
        {
            List<Issue> issues = new List<Issue>();
            if (g == null)
            {
                return issues;
            }

            if (g.SourceType == GeometrySourceType.Marinelli)
            {
                // (а) В колодец прибор обязан пролезть. У бруска — диагональю:
                // именно ею он входит в круглую лунку.
                double outer = DetectorOuterDiameterMm(g);
                if (outer > 0.0 && g.MarinelliHoleDiameter < outer)
                {
                    issues.Add(new Issue
                    {
                        Field = "MarinelliHoleDiameter",
                        Resource = "GeometryEditorErrorHoleNarrow",
                        Value = g.MarinelliHoleDiameter,
                        Limit = outer,
                    });
                }

                // (б, г) Кольцо пробы обязано существовать: внешний радиус за
                // вычетом стенки больше внутреннего вместе со стенкой колодца.
                double rIn = 0.5 * g.MarinelliHoleDiameter + g.MarinelliHoleSideThickness;
                double rOut = 0.5 * g.MarinelliBeakerDiameter - g.MarinelliSideThickness;
                if (!(rOut > rIn))
                {
                    issues.Add(new Issue
                    {
                        Field = "MarinelliBeakerDiameter",
                        Resource = "GeometryEditorErrorRingGone",
                        Value = 2.0 * rOut,
                        Limit = 2.0 * rIn,
                    });
                }

                // (в) Колодец не может быть глубже пробы: ниже его дна лежит
                // столб пробы, и отрицательным он не бывает.
                if (g.MarinelliHoleHeight > g.MarinelliSourceHeight)
                {
                    issues.Add(new Issue
                    {
                        Field = "MarinelliHoleHeight",
                        Resource = "GeometryEditorErrorHoleDeeper",
                        Value = g.MarinelliHoleHeight,
                        Limit = g.MarinelliSourceHeight,
                    });
                }

                // ...а стакан — ниже своей пробы.
                if (g.MarinelliBeakerHeight > 0.0
                    && g.MarinelliSourceHeight > g.MarinelliBeakerHeight)
                {
                    issues.Add(new Issue
                    {
                        Field = "MarinelliSourceHeight",
                        Resource = "GeometryEditorErrorSampleTaller",
                        Value = g.MarinelliSourceHeight,
                        Limit = g.MarinelliBeakerHeight,
                    });
                }
            }
            else if (g.SourceType == GeometrySourceType.Cylinder)
            {
                // (г) Стенка не может съесть весь просвет: радиус пробы — это
                // ПОЛОВИНА диаметра за вычетом стенки (см. SampleVolumeCm3).
                if (!(0.5 * g.BeakerDiameter > g.BeakerSideWallThickness))
                {
                    issues.Add(new Issue
                    {
                        Field = "BeakerSideWallThickness",
                        Resource = "GeometryEditorErrorWallEatsSample",
                        Value = g.BeakerSideWallThickness,
                        Limit = 0.5 * g.BeakerDiameter,
                    });
                }

                if (g.BeakerHeight > 0.0 && g.SourceHeight > g.BeakerHeight)
                {
                    issues.Add(new Issue
                    {
                        Field = "SourceHeight",
                        Resource = "GeometryEditorErrorSampleTaller",
                        Value = g.SourceHeight,
                        Limit = g.BeakerHeight,
                    });
                }
            }
            else if (g.SourceType == GeometrySourceType.Box)
            {
                // (б) У короба стенка снимается с ОБЕИХ сторон, поэтому предел
                // вдвое строже, чем у цилиндра с его радиусом.
                if (!(g.BoxSourceX > 2.0 * g.BoxSideWallThickness)
                    || !(g.BoxSourceY > 2.0 * g.BoxSideWallThickness))
                {
                    issues.Add(new Issue
                    {
                        Field = "BoxSideWallThickness",
                        Resource = "GeometryEditorErrorWallEatsSample",
                        Value = g.BoxSideWallThickness,
                        Limit = 0.5 * Math.Min(g.BoxSourceX, g.BoxSourceY),
                    });
                }
            }

            return issues;
        }

        /// <summary>
        /// Объём пробы сцены, см³. Считается по тем же областям, что строит
        /// расчёт (<c>EfficiencySimulator.Build</c>): у маринелли это кольцо
        /// вокруг колодца ПЛЮС столб под его дном.
        /// </summary>
        public static double SampleVolumeCm3(GeometryModel g)
        {
            const double Mm3PerCm3 = 1000.0;
            switch (g.SourceType)
            {
                case GeometrySourceType.Cylinder:
                {
                    double r = Math.Max(0.0, 0.5 * g.BeakerDiameter - g.BeakerSideWallThickness);
                    return Math.PI * r * r * Math.Max(0.0, g.SourceHeight) / Mm3PerCm3;
                }

                case GeometrySourceType.Box:
                    return Math.Max(0.0, g.BoxSourceX - 2.0 * g.BoxSideWallThickness)
                           * Math.Max(0.0, g.BoxSourceY - 2.0 * g.BoxSideWallThickness)
                           * Math.Max(0.0, g.BoxSourceHeight) / Mm3PerCm3;

                case GeometrySourceType.Marinelli:
                {
                    // Кольцо идёт на всю высоту пробы, а столб — только ниже
                    // дна колодца: ровно так их кладёт EfficiencySimulator.
                    double rIn = 0.5 * g.MarinelliHoleDiameter + g.MarinelliHoleSideThickness;
                    double rOut = Math.Max(rIn, 0.5 * g.MarinelliBeakerDiameter - g.MarinelliSideThickness);
                    double hole = Math.Max(0.0, g.MarinelliHoleHeight);
                    double total = Math.Max(0.0, g.MarinelliSourceHeight);
                    double ring = Math.PI * (rOut * rOut - rIn * rIn) * total;
                    double cap = Math.PI * rIn * rIn * Math.Max(0.0, total - hole);
                    return (ring + cap) / Mm3PerCm3;
                }

                default:
                    return 0.0;
            }
        }

        /// <summary>
        /// Убедиться, что вещество пробы годится сцене, и подменить его грунтом,
        /// если нет. Возвращает имя подставленного вещества; пусто — не трогали.
        ///
        /// Подмена НЕ молчаливая: имя возвращается наружу, редактор его говорит,
        /// а в списке веществ видно новое. Молча подставлять нельзя — на этом
        /// уже обжигались (проба, оставшаяся воздухом, завысила кривую AS80x80
        /// в 2.6 раза, `E19`), но и считать сцену по воздуху нельзя тем более:
        /// пробег в нём стометровый.
        /// </summary>
        public static string EnsureSample(GeometryModel g)
        {
            if (g.Source != null && g.Source.Density > GasDensity
                && g.Source.Fractions.Count > 0)
            {
                return "";
            }

            GeometryMaterialLibrary.Entry entry =
                GeometryMaterialLibrary.ByName(DefaultSampleMaterial);
            if (entry == null)
            {
                return "";
            }

            g.Source = GeometryMaterialLibrary.Make(entry, entry.Density);
            return entry.Name;
        }
    }
}
