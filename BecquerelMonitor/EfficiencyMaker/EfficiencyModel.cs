using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BecquerelMonitor.Properties;

namespace BecquerelMonitor.EfficiencyMaker
{
    /// <summary>Линия цепочки: выход задан на распад родителя ряда.</summary>
    public sealed class EfficiencyLine
    {
        public string Nuclide { get; set; }

        public double Energy { get; set; }

        public double Intensity { get; set; }
    }

    /// <summary>Откуда взят абсолютный уровень кривой.</summary>
    public enum EfficiencyLevelSource
    {
        None,
        /// <summary>С исходной кривой: режим «поправить».</summary>
        Reference,
        /// <summary>С опорной точки, введённой руками.</summary>
        Anchor,
        /// <summary>Ниоткуда: восстановлена только форма.</summary>
        ShapeOnly,
        /// <summary>Посчитана из геометрии: уровень абсолютный, не подогнанный.</summary>
        Simulation
    }

    /// <summary>Одна измеренная линия одного спектра.</summary>
    public sealed class EfficiencyObservation
    {
        public string Spectrum { get; set; }

        public string Chain { get; set; }

        public string Nuclide { get; set; }

        public double Energy { get; set; }

        public double Intensity { get; set; }

        public double LiveTime { get; set; }

        public double Channel { get; set; }

        public double Fwhm { get; set; }

        public double NetCounts { get; set; }

        public double NetSigma { get; set; }

        public double Significance { get; set; }

        public double RelativeError { get; set; }

        public double LogRatio { get; set; }

        public double Weight { get; set; }

        public double Residual { get; set; }

        /// <summary>Точка на общей кривой: серийный сдвиг снят, уровень добавлен.</summary>
        public double MeasuredEfficiency { get; set; }

        public int SeriesIndex { get; set; }

        public bool Accepted { get; set; }

        public string Reason { get; set; }

        /// <summary>Серия = (спектр, цепочка): у неё своя неизвестная активность.</summary>
        public string SeriesKey
        {
            get { return this.Spectrum + " / " + this.Chain; }
        }
    }

    public sealed class EfficiencyFitInput
    {
        public List<string> SpectrumFiles = new List<string>();

        /// <summary>
        /// Цепочки на все спектры разом. Годится, когда пачка однородная;
        /// перекрывается поспектральной разметкой ниже.
        /// </summary>
        public List<string> Chains = new List<string>();

        /// <summary>
        /// Свой набор нуклидов у каждого спектра: путь к файлу -> цепочки.
        ///
        /// Пачка почти никогда не однородна — в неё кладут и ториевый образец, и
        /// урановый, и цезиевый источник. Общий список цепочек на всех означал
        /// бы, что в каждом спектре ищутся линии всех наборов: лишние дают
        /// «нет пика» в лучшем случае, а в худшем — площадь шума на месте
        /// несуществующей линии, и она тянет кривую.
        ///
        /// Спектр без разметки в счёт не идёт (о нём говорится в журнале), а не
        /// считается по общему списку: молча взятый чужой набор — ровно та
        /// ошибка, ради которой эта разметка и заводится.
        /// </summary>
        public readonly Dictionary<string, List<string>> ChainsBySpectrum =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        public List<ROIEfficiencyData> Reference;

        /// <summary>
        /// Откуда взята исходная кривая. Нужен только офлайн-харнессу
        /// `tools/effmaker`: он дописывает результат в тот же файл, сохраняя
        /// имя и заметку. В приложении кривая приходит из конфигурации прибора,
        /// и файла у неё нет.
        /// </summary>
        public string ReferencePath;

        public int ResultIndex;

        /// <summary>
        /// Конфигурация устройства, если ссылка спектра никуда не ведёт (Guid).
        /// Только по явному решению: от неё зависят обе калибровки.
        /// </summary>
        public string FallbackDeviceGuid;

        public int PolynomialOrder = 3;

        /// <summary>Минимальный выход линии, % на распад родителя.</summary>
        public double MinIntensity = 1.0;

        public double MinEnergy = 40.0;

        public double MaxEnergy = 2800.0;

        public double MinSignificance = 4.0;

        /// <summary>Полуширина окна фита в ПШПВ.</summary>
        public double WindowFwhm = 4.0;

        /// <summary>
        /// Соседняя линия ближе этого числа ПШПВ считается наложением. Полторы
        /// ПШПВ, а не половина: на сцинтилляторе 609 кэВ радия и 662 кэВ цезия
        /// разведены на 1.3 ПШПВ и всё равно сидят друг на друге — с окном 0.6
        /// фит цезиевого спектра «нашёл» радий в 5·10⁷ отсчётов.
        /// </summary>
        public double BlendFwhm = 1.2;

        /// <summary>...если её выход не ниже этой доли от нашего.</summary>
        public double BlendRatio = 0.2;

        /// <summary>
        /// Линии СВОЕЙ цепочки ближе этого числа ПШПВ сливаются в одну
        /// наблюдаемую с суммарным выходом: прибор их не разделяет.
        /// </summary>
        public double MergeFwhm = 1.0;

        /// <summary>
        /// Чужая линия ближе этого числа ПШПВ неразрешима никаким фитом —
        /// колонки становятся коллинеарными. Только такие и отбрасываются.
        /// </summary>
        public double UnresolvableFwhm = 0.35;

        /// <summary>
        /// Насколько центр линии разрешено уточнять, в ПШПВ. Больше трети —
        /// и подгонка уезжает на соседний сильный пик, а не на свой.
        /// </summary>
        public double CenterSearchFwhm = 0.3;

        /// <summary>
        /// Разброс невязок серии (в разах), выше которого серия выбрасывается
        /// целиком: цепочки в спектре либо нет, либо она не в равновесии.
        /// </summary>
        public double MaxSeriesScatter = 2.5;

        /// <summary>Порог отбраковки выброса в проходах фита, в робастных сигмах.</summary>
        public double OutlierSigma = 3.0;

        /// <summary>Пол относительной погрешности площади, %.</summary>
        public double SystematicPercent = 3.0;

        public bool SubtractBackground = true;

        public double AnchorEnergy;

        public double AnchorEfficiency;
    }

    public sealed class EfficiencyFitResult
    {
        public List<EfficiencyObservation> Observations = new List<EfficiencyObservation>();

        public double[] Coefficients = new double[0];

        public List<string> SeriesKeys = new List<string>();

        public double[] SeriesOffsets = new double[0];

        public double Level;

        public EfficiencyLevelSource LevelSource = EfficiencyLevelSource.None;

        public double Chi2Ndf;

        public double MinEnergy;

        public double MaxEnergy;

        public List<ROIEfficiencyData> Curve = new List<ROIEfficiencyData>();

        /// <summary>Исходная кривая: по ней продолжается форма за краями измерений.</summary>
        public List<ROIEfficiencyData> ReferenceCurve;

        public string Error;

        public bool Ok
        {
            get { return string.IsNullOrEmpty(this.Error) && this.Curve.Count >= 2; }
        }

        public int AcceptedCount
        {
            get { return this.Observations.Count(o => o.Accepted); }
        }
    }

    /// <summary>
    /// Таблицы линий для восстановления кривой. Цепочки берутся из наборов
    /// нуклидов пользовательского конфига — там выходы записаны на распад
    /// родителя ряда, а это ровно то, что нужно для векового равновесия.
    /// </summary>
    public static class EfficiencyLibrary
    {
        /// <summary>
        /// Набор, который в кривую не годится, и ПОЧЕМУ. Раньше такие наборы
        /// просто не появлялись в списке, и созданный пользователем набор
        /// пропадал молча — ни в списке, ни в журнале. Причина всегда одна из
        /// трёх, и все три чинятся руками, если о них сказать.
        /// </summary>
        public sealed class SetReject
        {
            public string Name;
            public string Reason;
        }

        public static Dictionary<string, List<EfficiencyLine>> BuildChains()
        {
            List<SetReject> ignored;
            return BuildChains(out ignored);
        }

        public static Dictionary<string, List<EfficiencyLine>> BuildChains(out List<SetReject> rejected)
        {
            Dictionary<string, List<EfficiencyLine>> chains =
                new Dictionary<string, List<EfficiencyLine>>(StringComparer.OrdinalIgnoreCase);
            rejected = new List<SetReject>();
            NuclideDefinitionManager manager = NuclideDefinitionManager.GetInstance();
            if (manager == null || manager.NuclideSets == null)
            {
                return chains;
            }

            foreach (NuclideSet set in manager.NuclideSets)
            {
                if (set == null || string.IsNullOrEmpty(set.Name))
                {
                    continue;
                }

                // Наборы-обманки из журнала исследований в кривую не годятся.
                if (set.Name.IndexOf("~decoy", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue;
                }

                string name = set.Name.Split('|')[0].Trim();
                if (chains.ContainsKey(name))
                {
                    rejected.Add(new SetReject
                    {
                        Name = set.Name,
                        Reason = Resources.EfficiencyMakerSetDuplicateName
                    });
                    continue;
                }

                int inSet = 0;
                // Не счётчик, а поимённый список: «одна линия из двух» не
                // говорит, КАКАЯ, а искать её глазами в наборе из тридцати
                // строк — отдельная работа.
                List<string> withoutIntensity = new List<string>();
                List<EfficiencyLine> lines = new List<EfficiencyLine>();
                foreach (NuclideDefinition definition in manager.NuclideDefinitions)
                {
                    if (definition == null || definition.Sets == null
                        || !definition.Sets.Contains(set.Id))
                    {
                        continue;
                    }

                    inSet++;
                    if (definition.Intencity <= 0.0 || definition.Energy <= 0.0)
                    {
                        withoutIntensity.Add(string.Format(CultureInfo.InvariantCulture,
                            "{0} {1:0.###} keV", (definition.Name ?? "").Trim(), definition.Energy));
                        continue;
                    }

                    lines.Add(new EfficiencyLine
                    {
                        Nuclide = definition.NuclideName,
                        Energy = definition.Energy,
                        Intensity = definition.Intencity
                    });
                }

                if (lines.Count >= 2)
                {
                    lines.Sort((a, b) => a.Energy.CompareTo(b.Energy));
                    chains[name] = lines;
                    continue;
                }

                // Метод стоит на отношении площадей линий к их выходам: без
                // выхода линия в кривую не входит, а одной линии мало — сравнить
                // не с чем. Оба случая называются вслух, с числами.
                rejected.Add(new SetReject
                {
                    Name = name,
                    Reason = withoutIntensity.Count > 0
                        ? string.Format(Resources.EfficiencyMakerSetNoIntensity,
                                        withoutIntensity.Count, inSet,
                                        string.Join("; ", withoutIntensity.ToArray()),
                                        lines.Count)
                        : string.Format(Resources.EfficiencyMakerSetTooFewLines, lines.Count)
                });
            }

            return chains;
        }

        /// <summary>
        /// Все известные линии — для проверки наложений. Кроме цепочек сюда
        /// входят пики вылета от 2614 кэВ и характеристический рентген: они
        /// не нуклиды, но площадь соседней линии портят точно так же.
        /// </summary>
        public static List<EfficiencyLine> AllKnownLines(Dictionary<string, List<EfficiencyLine>> chains)
        {
            List<EfficiencyLine> all = new List<EfficiencyLine>();
            foreach (KeyValuePair<string, List<EfficiencyLine>> chain in chains)
            {
                all.AddRange(chain.Value);
            }

            Action<string, double, double> add = (nuclide, energy, intensity) =>
                all.Add(new EfficiencyLine { Nuclide = nuclide, Energy = energy, Intensity = intensity });

            add("K-40", 1460.822, 10.66);
            add("Cs-137", 661.657, 85.10);
            add("Am-241", 59.541, 35.92);
            add("Co-60", 1173.228, 99.85);
            add("Co-60", 1332.492, 99.98);
            add("SE-2614", 2103.5, 100.0);
            add("DE-2614", 1592.5, 100.0);
            add("Xray-Pb", 74.969, 100.0);
            add("Xray-Pb", 72.804, 59.5);
            add("Xray-Pb", 84.936, 23.0);
            add("Xray-W", 59.318, 100.0);
            add("Xray-W", 57.981, 57.6);
            add("Annih", 511.0, 100.0);
            return all;
        }
    }
}
