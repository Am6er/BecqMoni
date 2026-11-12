using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;
using BecquerelMonitor.EfficiencyMaker;

namespace BecquerelMonitor.FullSpectrumAnalysis
{
    /// <summary>
    /// (`AMBER274`, Э2, 06.11.2026) ИЗ ЧЕГО СОБИРАТЬ СЛОЙ: рычаги замера «по частям»
    /// (`CrystalOwnActivityProbeA274 --mode=parts`). Включён слой или нет, решает не этот класс, а
    /// <see cref="EfficiencyMaker.ResponseMatrixBuilder.AttachCrystalSection"/> и
    /// <see cref="FsaAnalyzer.CrystalOwnLayer"/>.
    /// </summary>
    public sealed class CrystalOwnParts
    {
        /// <summary>Бета бета-ветвей природных нуклидов.</summary>
        public bool Beta = true;

        /// <summary>Каскад бета-ветвей (788.7 кэВ у лантана).</summary>
        public bool BranchGamma = true;

        /// <summary>Гаммы ветви захвата (1435.8 кэВ у лантана).</summary>
        public bool Capture = true;

        /// <summary>
        /// Релаксация дочернего атома после захвата: K-рентген и сумма с гаммой. Включена: решение
        /// Amber 06.11.2026 вопросником, дословно: «Релаксация ВКЛ (Рекомендую)»; без неё слой
        /// перевычитает 1400–1450 кэВ и недовычитает 1450–1490 (журнал `handover-2026-11-06-fixe2.md`).
        /// </summary>
        public bool DaughterRelaxation = true;

        /// <summary>Свободная строка ряда Ac-227.</summary>
        public bool Series = true;

        /// <summary>Природные нуклиды — свободной строкой: так меряется амплитуда по данным.</summary>
        public bool FreeNatural;

        /// <summary>
        /// (`AMBER274`, ревизия по статьям, 12.11.2026) Релаксация вакансии после ВНУТРЕННЕЙ КОНВЕРСИИ
        /// в каскаде бета-ветви: энергия связи оболочки оседает в кристалле (K-рентген фотоном, оже
        /// на месте). Выключить — прежний счёт с дефектом: электрон конверсии кладётся с
        /// E_γ − E_связи, а энергия связи пропадает (у дочки Lu-176 переход 88.35 кэВ конвертирован
        /// на 85 %). Рычаг — мутант пробы `CrystalOwnRevisionProbeA274`.
        /// </summary>
        public bool ConversionRelaxation = true;

        /// <summary>
        /// (`AMBER274`, ревизия по статьям, 12.11.2026) ШКАЛА ГИСТОГРАММЫ СЛОЯ:
        /// <see cref="CrystalOwnActivityBuilder.ScaleEnergy"/> — осевшая энергия (прежний счёт: пики 37.4 и 1473.2 кэВ);
        /// <see cref="CrystalOwnActivityBuilder.ScaleAnchor"/> — свет события на свет пика 661.657 кэВ, как у строк
        /// электрона пробы (`ElectronSourceResponse`);
        /// <see cref="CrystalOwnActivityBuilder.ScalePhoton"/> — свет события по ФОТОННОЙ шкале прибора: энергия E′, у
        /// которой пик полного поглощения фотона даёт тот же свет. Так откалиброван прибор (по
        /// пикам гамма-линий), и так стоят пики фотонных строк матрицы (каждый узел — на своей
        /// энергии). У кристалла без кривой света все три совпадают.
        /// </summary>
        public int Scale = CrystalOwnActivityBuilder.ScalePhoton;

        /// <summary>
        /// Энергия связи L дочернего атома захвата — из `matdb.eadl_binding` (средняя L1–L3), а не
        /// средняя линия L-рентгена `nucdb` (у Ba 4.97 вместо 5.2–6.0 кэВ). Выключить — прежнее.
        /// </summary>
        public bool LBindingFromEadl = true;

        /// <summary>
        /// ЗАМЕР, не настройка: подмена ветви беты перед сборкой события (проба кладёт сюда опытную
        /// форму, не трогая `nucdb.beta_shapes`). null — ветвь как в базе.
        /// </summary>
        public Func<FsaBeta, FsaBeta> BetaOverride;

        /// <summary>ЗАМЕР, не настройка: доля K-захвата вместо вычисленной из `nucdb` (≤ 0 — как в базе).</summary>
        public double KCaptureOverride;
    }

    /// <summary>
    /// (`AMBER274`, этап Э2, 04.11.2026) СБОРКА СЛОЯ «СОБСТВЕННАЯ АКТИВНОСТЬ КРИСТАЛЛА».
    ///
    /// Решения Amber 29.10.2026 (строка `AMBER274`, вопросник того же дня):
    ///
    ///   * природные нуклиды — по распространённости, периоду `nucdb` и массе
    ///     кристалла, БЕЗ свободного параметра («Природные по кристаллу»);
    ///   * Ac-227 — свободной строкой FSA: загрязнение своё у каждого кристалла,
    ///     поэтому его амплитуда не вычисляется, а подбирается;
    ///   * распад в объёме кристалла ведётся СОБЫТИЕМ ЦЕЛИКОМ (итог `AMBER273`);
    ///   * ⛔ АЛЬФЫ НЕ ВХОДЯТ: ряд Ac-227 даёт в слой только бета-члены
    ///     (<see cref="ActiniumSeries"/>), гашения света альф нет (этап Э3). У La-138 и
    ///     Lu-176 альф нет вовсе.
    ///
    /// ⛔ Числа природной активности НЕ хранятся в коде: они считаются здесь из
    /// `nucdb` (распространённость, период), природной атомной массы элемента и
    /// состава кристалла сцены. LaBr3:5%Ce при 5.08 г/см³: 924.5 Бк/г изотопа на атом природного
    /// лантана × 0.08881 % × доля La 0.3485 × 5.08 = 1.4535 Бк/см³ — внутри замера `AMBER273`
    /// (1.43…1.49 Бк/см³ у Quarati и др.).
    /// </summary>
    public static class CrystalOwnActivityBuilder
    {
        /// <summary>Слой беты — единственный, который делает Э2.</summary>
        public const string BetaLayer = "beta";

        /// <summary>Шкала гистограммы — осевшая энергия (см. <see cref="CrystalOwnParts.Scale"/>).</summary>
        public const int ScaleEnergy = 0;

        /// <summary>Шкала гистограммы — свет на свет пика 661.657 кэВ.</summary>
        public const int ScaleAnchor = 1;

        /// <summary>Шкала гистограммы — фотонная шкала прибора (умолчание слоя).</summary>
        public const int ScalePhoton = 2;

        /// <summary>
        /// Узлы фотонной шкалы света, кэВ: гуще у K-краёв тяжёлых элементов кристалла (La 38.9,
        /// Ce 40.4, Lu 63.3), где свет пика на кэВ меняется скачком, реже выше.
        /// </summary>
        static readonly double[] ScaleNodesKev =
        {
            6.0, 8.0, 10.0, 12.0, 14.0, 17.0, 20.0, 24.0, 28.0, 32.0, 36.0, 38.0, 40.0, 42.0, 46.0, 52.0, 60.0, 64.0,
            70.0, 80.0, 100.0, 130.0, 170.0, 220.0, 300.0, 400.0, 511.0, 661.657, 900.0, 1200.0, 1436.0, 1500.0,
            2000.0, 2614.5, 3200.0,
        };

        /// <summary>Историй на узел фотонной шкалы: якорь — среднее по пику, тысяч историй хватает с запасом.</summary>
        const int ScaleNodeHistories = 20000;

        /// <summary>
        /// (`AMBER274`, ревизия по статьям, 12.11.2026) ФОТОННАЯ ШКАЛА СВЕТА кристалла сцены: свет
        /// пика полного поглощения Λ(E) = E·s(E) в узлах, где s — `LastPhotonLightScale` того же
        /// симулятора, что ведёт события слоя. Перевод света события в энергию шкалы прибора —
        /// обратная функция (линейно между узлами; Λ приведена к неубыванию, скачок у K-края
        /// сглаживается до плато). Ниже первого узла и выше последнего — крайним отношением.
        /// </summary>
        public sealed class PhotonLightScale
        {
            public double[] EnergyKev = new double[0];

            /// <summary>Свет пика на кэВ, s(E).</summary>
            public double[] PerKev = new double[0];

            /// <summary>Свет пика Λ(E) = E·s(E), приведённый к неубыванию.</summary>
            public double[] Light = new double[0];

            /// <summary>Свет на кэВ у 661.657 кэВ — якорь строк электрона пробы.</summary>
            public double AnchorPerKev;

            public double ToEnergy(double light)
            {
                int n = this.Light.Length;
                if (n == 0 || !(light > 0.0))
                {
                    return light;
                }

                if (light <= this.Light[0])
                {
                    return this.PerKev[0] > 0.0 ? light / this.PerKev[0] : light;
                }

                if (light >= this.Light[n - 1])
                {
                    return this.PerKev[n - 1] > 0.0 ? light / this.PerKev[n - 1] : light;
                }

                int lo = 0, hi = n - 1;
                while (hi - lo > 1)
                {
                    int mid = (lo + hi) / 2;
                    if (this.Light[mid] <= light)
                    {
                        lo = mid;
                    }
                    else
                    {
                        hi = mid;
                    }
                }

                double span = this.Light[hi] - this.Light[lo];
                return span > 0.0
                    ? this.EnergyKev[lo] + (this.EnergyKev[hi] - this.EnergyKev[lo]) * (light - this.Light[lo]) / span
                    : this.EnergyKev[lo];
            }
        }

        /// <summary>
        /// Фотонная шкала света сцены; null — у кристалла нет кривой света (шкала слоя — энергия).
        /// Узел без пика (квант не поглощается целиком ни разу) пропускается.
        /// </summary>
        public static PhotonLightScale MeasurePhotonScale(GeometryModel geometry, double binKev, int seed)
        {
            var probe = new EfficiencySimulator(geometry);
            if (!probe.HasLightCurve)
            {
                return null;
            }

            var energies = new List<double>();
            var perKev = new List<double>();
            double anchor = 0.0;
            for (int i = 0; i < ScaleNodesKev.Length; i++)
            {
                var sim = new EfficiencySimulator(geometry) { Histories = ScaleNodeHistories, Seed = seed + 7919 * (i + 1) };
                double error;
                sim.Response(ScaleNodesKev[i], binKev, out error);
                double s = sim.LastPhotonLightScale;
                if (!(s > 0.0))
                {
                    continue;
                }

                energies.Add(ScaleNodesKev[i]);
                perKev.Add(s);
                if (Math.Abs(ScaleNodesKev[i] - 661.657) < 1e-6)
                {
                    anchor = s;
                }
            }

            if (energies.Count < 2)
            {
                return null;
            }

            var scale = new PhotonLightScale
            {
                EnergyKev = energies.ToArray(),
                PerKev = perKev.ToArray(),
                Light = new double[energies.Count],
                AnchorPerKev = anchor > 0.0 ? anchor : perKev[perKev.Count - 1],
            };
            double top = 0.0;
            for (int i = 0; i < energies.Count; i++)
            {
                top = Math.Max(top, energies[i] * perKev[i]);
                scale.Light[i] = top;
            }

            return scale;
        }

        /// <summary>Шаг гистограммы слоя, кэВ: сетка матрицы отклика.</summary>
        public const double DefaultBinKev = 2.0;

        /// <summary>Верх гистограммы, кэВ: выше самой жёсткой линии слоя (1473 у La-138).</summary>
        public const double HistogramTopKev = 3000.0;

        /// <summary>Меньше этого числа историй на нуклид слой не строят — шум съел бы смысл.</summary>
        public const int MinHistories = 200;

        const double Ln2 = 0.6931471805599453;
        const double Avogadro = 6.02214076e23;

        /// <summary>Доля атомов церия среди мест лантана в LaBr3:Ce, поставляемый состав 5 %.</summary>
        const double CeriumDopantShare = 0.05;

        const int LanthanumZ = 57;
        const int CeriumZ = 58;
        const int BromineZ = 35;

        /// <summary>Нуклид слоя: имя, слой, свободная ли амплитуда, вес в ряду.</summary>
        sealed class Member
        {
            public string Nuclide;
            public string Layer = BetaLayer;
            public bool Free;
            public double Weight = 1.0;
        }

        /// <summary>
        /// Природные нуклиды кристалла: La-138 у лантана, Lu-176 у лютеция. Gd-152 —
        /// альфа-излучатель (этап Э3), поэтому в бета-слой не входит.
        /// </summary>
        public static List<string> NaturalNuclides(GeometryModel geometry)
        {
            var list = new List<string>();
            if (geometry == null || geometry.Crystal == null)
            {
                return list;
            }

            if (geometry.Crystal.Fractions.ContainsKey(57))
            {
                list.Add("138LA");
            }

            if (geometry.Crystal.Fractions.ContainsKey(71))
            {
                list.Add("176LU");
            }

            return list;
        }

        /// <summary>
        /// Свободные строки: ряд Ac-227 у лантановых/цериевых кристаллов. Ряд в
        /// равновесии — одна строка на ряд (решение Amber 29.10.2026), поэтому амплитуда — активность
        /// Ac-227, а веса членов — доли распадов ряда на один распад головы.
        /// </summary>
        public static bool HasActiniumSeries(GeometryModel geometry)
        {
            return geometry != null && geometry.Crystal != null
                   && (geometry.Crystal.Fractions.ContainsKey(57) || geometry.Crystal.Fractions.ContainsKey(58));
        }

        /// <summary>Есть ли у сцены собственный слой вовсе: иначе раздел не строят и файл прежний.</summary>
        public static bool HasOwnActivity(GeometryModel geometry)
        {
            return NaturalNuclides(geometry).Count > 0 || HasActiniumSeries(geometry);
        }

        /// <summary>
        /// Удельная активность природного нуклида в кристалле, Бк/г кристалла:
        /// ln2·N_A/(T½·M) × распространённость × массовая доля элемента.
        /// 0 — данных нет (тогда слой по нуклиду не строится и это видно в отчёте).
        /// </summary>
        public static double SpecificActivityBqPerGram(GeometryModel geometry, string nucid, out string reason)
        {
            reason = null;
            double z, abundance, seconds;
            if (!NaturalData(nucid, out z, out abundance, out seconds))
            {
                reason = "в nucdb нет распространённости или периода";
                return 0.0;
            }

            if (geometry == null || geometry.Crystal == null || !(geometry.Crystal.Density > 0.0))
            {
                reason = "у сцены нет вещества кристалла";
                return 0.0;
            }

            double massShare = MassShareOf(geometry.Crystal, (int)z);
            if (!(massShare > 0.0))
            {
                reason = "элемента Z=" + z.ToString(CultureInfo.InvariantCulture) + " нет в кристалле";
                return 0.0;
            }

            // ⛔ (`AMBER274`, Э2, 06.11.2026) Распространённость — доля АТОМОВ природного элемента,
            // поэтому на грамм элемента идёт его ПРИРОДНАЯ атомная масса, а не масса изотопа:
            // у La 138.905 вместо 137.907 (−0.72 %), у Lu 174.967 вместо 175.943 (+0.56 %).
            double molar;
            if (!MaterialDatabase.AtomicMass.TryGetValue((int)z, out molar) || !(molar > 0.0))
            {
                reason = "нет атомной массы элемента";
                return 0.0;
            }

            double perGram = Ln2 * Avogadro / (seconds * molar) * (abundance / 100.0) * massShare;
            return perGram;
        }

        /// <summary>Активность природного нуклида в объёме кристалла, Бк.</summary>
        public static double NaturalActivityBq(GeometryModel geometry, string nucid, out string reason)
        {
            reason = null;
            double perGram = SpecificActivityBqPerGram(geometry, nucid, out reason);
            if (!(perGram > 0.0))
            {
                return 0.0;
            }

            double volume = GeometryScenes.CrystalVolumeCm3(geometry);
            double mass = volume * geometry.Crystal.Density;
            if (!(mass > 0.0))
            {
                reason = "объём кристалла не определён";
                return 0.0;
            }

            return perGram * mass;
        }

        /// <summary>
        /// Собрать раздел для сцены. <paramref name="histories"/> — историй НА
        /// НУКЛИД (не на ветвь): ветви делят их по интенсивности.
        /// </summary>
        public static ResponseCrystalSection Build(GeometryModel geometry, double binKev, int histories, int seed,
                                                    out string note)
        {
            return Build(geometry, binKev, histories, seed, new CrystalOwnParts(), out note);
        }

        /// <summary>То же с выбором частей: замер «отключение по частям»; раздел матрицы зовёт умолчания.</summary>
        public static ResponseCrystalSection Build(GeometryModel geometry, double binKev, int histories, int seed,
                                                    CrystalOwnParts parts, out string note)
        {
            var section = new ResponseCrystalSection
            {
                Stamp = ResponseCrystalSection.ComputeStamp(geometry, binKev),
                BinKev = binKev,
                Histories = histories,
            };

            var notes = new List<string>();
            note = "";
            if (geometry == null || geometry.Crystal == null || !(geometry.Crystal.Density > 0.0))
            {
                note = "у сцены нет вещества кристалла — слой не собран";
                return section;
            }

            if (histories < MinHistories)
            {
                note = "историй меньше " + MinHistories.ToString(CultureInfo.InvariantCulture) + " — слой не собран";
                return section;
            }

            int bins = ResponseMatrix.ImageBins(HistogramTopKev, binKev);
            int total = 0;

            // (`AMBER274`, ревизия по статьям, 12.11.2026) Шкала гистограммы — свет, если у кристалла
            // есть кривая света; иначе энергия, и примечание это называет.
            PhotonLightScale light = parts.Scale != ScaleEnergy ? MeasurePhotonScale(geometry, binKev, seed) : null;
            notes.Add("шкала слоя: " + (light == null ? "энергия" : parts.Scale == ScaleAnchor ? "свет на якорь 661.657" : "свет, фотонная"));
            if (parts.Scale != ScaleEnergy && light == null)
            {
                notes.Add("кривой света у кристалла нет — слой по осевшей энергии");
            }

            foreach (string nucid in NaturalNuclides(geometry))
            {
                string why;
                double activity = NaturalActivityBq(geometry, nucid, out why);
                if (!(activity > 0.0))
                {
                    notes.Add(Pretty(nucid) + ": " + why);
                    continue;
                }

                var member = new Member { Nuclide = nucid, Free = parts.FreeNatural, Weight = 1.0 };
                CrystalOwnEntry entry = BuildEntry(geometry, member, bins, binKev, histories, seed + total, parts, light, notes);
                if (entry == null)
                {
                    continue;
                }

                entry.ActivityBq = activity;
                section.Entries.Add(entry);
                total++;
            }

            if (parts.Series && HasActiniumSeries(geometry))
            {
                List<Member> series = ActiniumSeries(notes);
                double weight = 0.0;
                foreach (Member member in series)
                {
                    weight += member.Weight;
                }

                if (series.Count > 0 && weight > 0.0)
                {
                    var combined = new CrystalOwnEntry
                    {
                        Nuclide = "227AC",
                        Layer = BetaLayer,
                        Free = true,
                        PerDecay = new double[bins],
                    };

                    foreach (Member member in series)
                    {
                        var seriesParts = new CrystalOwnParts { Scale = parts.Scale, ConversionRelaxation = parts.ConversionRelaxation };
                        CrystalOwnEntry part = BuildEntry(geometry, member, bins, binKev, histories, seed + total, seriesParts, light, notes);
                        total++;
                        if (part == null)
                        {
                            continue;
                        }

                        for (int i = 0; i < bins; i++)
                        {
                            combined.PerDecay[i] += member.Weight * part.PerDecay[i];
                        }
                    }

                    combined.Histories = histories;
                    section.Entries.Add(combined);
                }
            }

            section.Note = string.Join("; ", notes.ToArray());
            note = section.Note;
            return section;
        }

        /// <summary>
        /// Члены ряда Ac-227, дающие БЕТУ, с весами «распадов члена на распад головы» (ряд в
        /// равновесии). ⛔ Доля β-ветви в вес НЕ входит: её уже несёт гистограмма члена на его
        /// распад (`BuildEntry`), прежде она считалась дважды: Pb-211 0.990 вместо 1.0, β Bi-211
        /// 7.6e-6 вместо 0.00276. Pb-211 и Bi-211 распадаются на каждый распад головы, Tl-207 — на
        /// α-ветвь Bi-211. Ответвление Fr-223 (α Ac-227, 1.38 %) не ведётся. Доля читается из `nucdb`.
        /// </summary>
        static List<Member> ActiniumSeries(List<string> notes)
        {
            var list = new List<Member>();
            double head = BetaSharePercent("227AC");
            if (!(head > 0.0))
            {
                notes.Add("227AC: в nucdb нет β⁻-ветвей");
                return list;
            }

            list.Add(new Member { Nuclide = "227AC", Weight = 1.0 });
            list.Add(new Member { Nuclide = "211PB", Weight = 1.0 });
            list.Add(new Member { Nuclide = "211BI", Weight = 1.0 });
            list.Add(new Member { Nuclide = "207TL", Weight = 1.0 - BetaSharePercent("211BI") / 100.0 });
            return list;
        }

        /// <summary>Сумма интенсивностей β⁻/β⁺-ветвей нуклида, % на распад.</summary>
        static double BetaSharePercent(string nucid)
        {
            double sum = 0.0;
            foreach (FsaBeta beta in FsaBetaLibrary.Branches(nucid))
            {
                if (beta != null && !beta.IsLine && !beta.Positron && beta.Intensity > 0.0)
                {
                    sum += beta.Intensity;
                }
            }

            return sum;
        }

        /// <summary>
        /// Гистограмма одного нуклида на распад. Бета-ветви — событием целиком
        /// (`FsaBetaLibrary.EventSpec` → `CrystalEventFrom`); захват электрона —
        /// гамма-каскадом (`CrystalGammaEvent`), релаксация дочернего атома — по рычагу
        /// <see cref="CrystalOwnParts.DaughterRelaxation"/>.
        /// </summary>
        static CrystalOwnEntry BuildEntry(GeometryModel geometry, Member member, int bins, double binKev, int histories, int seed,
                                          CrystalOwnParts parts, PhotonLightScale light, List<string> notes)
        {
            var entry = new CrystalOwnEntry
            {
                Nuclide = member.Nuclide,
                Layer = member.Layer,
                Free = member.Free,
                Histories = histories,
                PerDecay = new double[bins],
            };

            var simulator = new EfficiencySimulator(geometry);
            double x, y, z;
            if (!simulator.TryCrystalPoint(out x, out y, out z))
            {
                notes.Add(Pretty(member.Nuclide) + ": в сцене нет кристалла");
                return null;
            }

            simulator.Seed = seed;

            List<FsaBeta> betas = FsaBetaLibrary.Branches(member.Nuclide);
            double betaPercent = 0.0;
            var usable = new List<KeyValuePair<FsaBeta, ElectronBranchSpec>>();
            var atoms = new Dictionary<ElectronBranchSpec, CaptureRelaxation>();
            foreach (FsaBeta source in betas)
            {
                FsaBeta beta = source;
                if (beta == null || beta.IsLine || beta.Positron || !(beta.Intensity > 0.0) || !(beta.EndpointKev > 0.0))
                {
                    continue;
                }

                if (parts.BetaOverride != null)
                {
                    beta = parts.BetaOverride(beta) ?? beta;
                }

                string reason = "нет спектра беты";
                ElectronBranchSpec spec = parts.BranchGamma ? FsaBetaLibrary.EventSpec(beta, out reason)
                                                            : FsaBetaLibrary.BetaOnlySpec(beta);
                if (spec == null)
                {
                    // (`AMBER274`, Э2) Ветвь на основное состояние или на уровень вне схемы
                    // Geant4 — для пробы это «события нет», для распада в кристалле это
                    // законная бета без каскада. Берём её одной бетой.
                    spec = FsaBetaLibrary.BetaOnlySpec(beta);
                    if (spec == null)
                    {
                        notes.Add(Pretty(member.Nuclide) + " " + beta.EndpointKev.ToString("F1", CultureInfo.InvariantCulture)
                                  + " кэВ: " + reason);
                        continue;
                    }
                }

                betaPercent += beta.Intensity;
                usable.Add(new KeyValuePair<FsaBeta, ElectronBranchSpec>(beta, spec));

                // (`AMBER274`, ревизия по статьям, 12.11.2026) Дочерний атом ветви — для вакансий
                // после внутренней конверсии каскада: флуоресценция K и энергия связи L из `matdb`.
                if (parts.ConversionRelaxation && spec.HasCascade)
                {
                    CaptureRelaxation atom = CaptureRelaxation.ForAtom(beta.DaughterZ, FsaBetaLibrary.Binding(beta.DaughterZ)[1]);
                    if (atom != null)
                    {
                        atoms[spec] = atom;
                    }
                    else
                    {
                        notes.Add(Pretty(member.Nuclide) + ": нет данных атома Z=" + beta.DaughterZ.ToString(CultureInfo.InvariantCulture)
                                  + " — энергия связи после конверсии не учтена");
                    }
                }
            }

            // ⛔ (`AMBER274`, Э2, 06.11.2026) Доля захвата — из таблицы ветвей `nucdb`, а не
            // «100 − β»: так альфа-ветвь считалась захватом (Bi-211 99.72 %, Ac-227 1.38 %). Истории
            // считаются на распад нуклида: ветвь, которой нет в слое (альфа), историй не получает,
            // и гистограмма остаётся «на распад».
            double ecPercent = CapturePercent(member.Nuclide);
            if (!(betaPercent + ecPercent > 0.0))
            {
                notes.Add(Pretty(member.Nuclide) + ": ни одной ветви распада");
                return null;
            }

            double scale = 1.0 / histories;

            // Бета-ветви: истории делятся по интенсивности, вес — 1/N.
            //
            // ⛔ (`AMBER274`, Э2, 05.11.2026, `crit8`) ЗДЕСЬ БЫЛА ДВОЙНАЯ
            // НОРМИРОВКА: история уже отобрана с весом I/100 (n =
            // histories·I/100), и сверх того вклад умножался на I/100.
            // Итог Σ(I/100)² = 0.548 у La-138 вместо 1.0 — слой занижался в
            // 1/0.664 = 1.5 раза, а форма ветвей перекашивалась к сильной.
            // Нормировка ровно одна: отбор историй.
            foreach (KeyValuePair<FsaBeta, ElectronBranchSpec> pair in usable)
            {
                int n = (int)Math.Round(histories * pair.Key.Intensity / 100.0);
                if (n <= 0)
                {
                    continue;
                }

                for (int i = 0; i < n; i++)
                {
                    if (!simulator.TryCrystalPoint(out x, out y, out z))
                    {
                        break;
                    }

                    double gamma;
                    CaptureRelaxation atom;
                    atoms.TryGetValue(pair.Value, out atom);
                    double deposit = simulator.CrystalEventFrom(pair.Value, atom, x, y, z, parts.Beta, out gamma);
                    Add(entry.PerDecay, OnScale(deposit, simulator.CrystalEventLight, light, parts.Scale), binKev, scale);
                }
            }

            // Захват электрона: гаммы ветви и K-рентген дочернего атома.
            if (parts.Capture && ecPercent > 0.05)
            {
                double[] gammas = EcGammas(member.Nuclide, ecPercent);
                CaptureRelaxation relaxation = parts.DaughterRelaxation ? RelaxationOf(member.Nuclide, ecPercent, parts.LBindingFromEadl) : null;
                if (relaxation != null && parts.KCaptureOverride > 0.0)
                {
                    // Замер: иная доля K-захвата при тех же ДОЛЯХ линий K-рентгена (линии масштабируются).
                    double factor = parts.KCaptureOverride / relaxation.KCapture;
                    for (int i = 0; i < relaxation.LineCdf.Length; i++)
                    {
                        relaxation.LineCdf[i] *= factor;
                    }

                    relaxation.KCapture = parts.KCaptureOverride;
                }

                int n = (int)Math.Round(histories * ecPercent / 100.0);
                for (int i = 0; i < n; i++)
                {
                    if (!simulator.TryCrystalPoint(out x, out y, out z))
                    {
                        break;
                    }

                    double deposit = simulator.CrystalGammaEvent(gammas, relaxation, x, y, z);
                    Add(entry.PerDecay, OnScale(deposit, simulator.CrystalEventLight, light, parts.Scale), binKev, scale);
                }
            }

            return entry;
        }

        /// <summary>
        /// Место события на шкале гистограммы: осевшая энергия, свет на якорь 661.657 кэВ или свет
        /// по фотонной шкале прибора (<see cref="CrystalOwnParts.Scale"/>). Шкалы нет — энергия.
        /// </summary>
        static double OnScale(double depositKev, double lightKev, PhotonLightScale light, int mode)
        {
            if (light == null || mode == ScaleEnergy || !(depositKev > 0.0) || !(lightKev > 0.0))
            {
                return depositKev;
            }

            return mode == ScaleAnchor
                ? (light.AnchorPerKev > 0.0 ? lightKev / light.AnchorPerKev : depositKev)
                : light.ToEnergy(lightKev);
        }

        /// <summary>
        /// Гаммы ВЕТВИ ЗАХВАТА (dec_type = 1 в `nucdb`), от жёстких к мягким,
        /// пока не набрана доля захвата. У La-138 это ровно 1435.8 кэВ (65.5 %
        /// — вся ветвь захвата).
        ///
        /// ⛔ (`AMBER274`, Э2, 05.11.2026, `crit8`) ПРЕЖНЯЯ ВЕРСИЯ БРАЛА ВСЕ
        /// гаммы распада: у La-138 в событие захвата попадала и 788.7 кэВ —
        /// линия БЕТА-ветви (Ce-138), которой в EC-событии нет. Событие
        /// складывало 1435.8 + 788.7 и давало несуществующий каскад. Ветвь
        /// различается полем `dec_type` (1 — захват/β⁺, 2 — β⁻, 0 — α), и
        /// берутся только строки своей ветви.
        ///
        /// ⚠ Схемы уровней в `nucdb` нет, поэтому каскад плоский: гаммы
        /// кладутся одним событием. Для La-138 это точно (одна линия), для
        /// прочих — приближение, и оно названо в примечании раздела.
        /// </summary>
        static double[] EcGammas(string nucid, double ecPercent)
        {
            var picked = new List<double>();
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nucdb.sqlite");
            if (!File.Exists(path))
            {
                return picked.ToArray();
            }

            var rows = new List<double[]>();
            try
            {
                using (var connection = new SqliteConnection("Data Source=" + path + ";Mode=ReadOnly"))
                {
                    connection.Open();
                    using (SqliteCommand command = connection.CreateCommand())
                    {
                        command.CommandText = "select energy_num, intensity_num from decay_radiations"
                                              + " where parent_nucid = $n and type_a = 'G' and dec_type = '1'"
                                              + " order by energy_num desc";
                        command.Parameters.AddWithValue("$n", nucid);
                        using (SqliteDataReader reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                if (reader.IsDBNull(0))
                                {
                                    continue;
                                }

                                double energy = Number(reader, 0);
                                double intensity = Number(reader, 1);
                                if (energy > 0.0 && intensity > 0.0)
                                {
                                    rows.Add(new[] { energy, intensity });
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                return picked.ToArray();
            }

            double covered = 0.0;
            foreach (double[] line in rows)
            {
                if (covered >= ecPercent * 0.999)
                {
                    break;
                }

                picked.Add(line[0]);
                covered += line[1];
            }

            return picked.ToArray();
        }

        /// <summary>
        /// Релаксация дочернего атома по `decay_radiations`: линии K-рентгена ветви захвата
        /// (сводная строка `KB` — сумма подлиний и не берётся) на один захват, вероятность
        /// K-вакансии P(K-рентген) / ω_K дочернего атома, энергия связи L — средняя линия
        /// L-рентгена. null — данных нет, и событие остаётся без релаксации.
        /// </summary>
        static CaptureRelaxation RelaxationOf(string nucid, double ecPercent, bool lBindingFromEadl)
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nucdb.sqlite");
            double z = ProtonNumberOf(nucid);
            if (!File.Exists(path) || !(ecPercent > 0.0) || !(z > 1.0))
            {
                return null;
            }

            var energies = new List<double>();
            var shares = new List<double>();
            double lKev = 0.0;
            using (var connection = new SqliteConnection("Data Source=" + path + ";Mode=ReadOnly"))
            {
                connection.Open();
                using (SqliteCommand command = connection.CreateCommand())
                {
                    command.CommandText = "select type_c, energy_num, intensity_num from decay_radiations"
                                          + " where parent_nucid = $n and type_a = 'X' and dec_type = '1'";
                    command.Parameters.AddWithValue("$n", nucid);
                    using (SqliteDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string kind = reader.IsDBNull(0) ? "" : reader.GetString(0);
                            double energy = Number(reader, 1);
                            double intensity = Number(reader, 2);
                            if (kind == "L")
                            {
                                lKev = energy;
                            }
                            else if (kind.StartsWith("K", StringComparison.Ordinal) && kind != "KB" && energy > 0.0 && intensity > 0.0)
                            {
                                energies.Add(energy);
                                shares.Add(intensity / ecPercent);
                            }
                        }
                    }
                }
            }

            MaterialDatabase.Fluorescence f = MaterialDatabase.FluorescenceOf((int)z - 1);
            if (energies.Count == 0 || f == null || !(f.KEdgeKev > 0.0))
            {
                return null;
            }

            // (`AMBER274`, ревизия по статьям, 12.11.2026) Осадок захвата не с K — ЭНЕРГИЯ СВЯЗИ L
            // дочернего атома (`matdb.eadl_binding`), а не средняя линия L-рентгена.
            if (lBindingFromEadl)
            {
                double bound = FsaBetaLibrary.Binding((int)z - 1)[1];
                if (bound > 0.0)
                {
                    lKev = bound;
                }
            }

            var relaxation = new CaptureRelaxation
            {
                LineKev = energies.ToArray(),
                LineCdf = new double[energies.Count],
                KEdgeKev = f.KEdgeKev,
                LEdgeKev = lKev,
            };
            double run = 0.0;
            for (int i = 0; i < shares.Count; i++)
            {
                run += shares[i];
                relaxation.LineCdf[i] = run;
            }

            double omega = f.Omega(true);
            relaxation.KCapture = omega > 0.0 ? Math.Min(1.0, run / omega) : run;
            return relaxation;
        }

        /// <summary>Доля ветви захвата нуклида по таблице ветвей `nucdb` (`l_decays`, род 1), % на распад.</summary>
        static double CapturePercent(string nucid)
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nucdb.sqlite");
            if (!File.Exists(path))
            {
                return 0.0;
            }

            using (var connection = new SqliteConnection("Data Source=" + path + ";Mode=ReadOnly"))
            {
                connection.Open();
                using (SqliteCommand command = connection.CreateCommand())
                {
                    command.CommandText = "select sum(perc_num) from l_decays where nucid = $n and l_seqno = 0 and dec_type = 1";
                    command.Parameters.AddWithValue("$n", nucid);
                    object value = command.ExecuteScalar();
                    return value == null || value is DBNull ? 0.0 : Convert.ToDouble(value, CultureInfo.InvariantCulture);
                }
            }
        }

        /// <summary>Заряд ядра нуклида по `nucdb`; 0 — записи нет.</summary>
        static double ProtonNumberOf(string nucid)
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nucdb.sqlite");
            if (!File.Exists(path))
            {
                return 0.0;
            }

            using (var connection = new SqliteConnection("Data Source=" + path + ";Mode=ReadOnly"))
            {
                connection.Open();
                using (SqliteCommand command = connection.CreateCommand())
                {
                    command.CommandText = "select z from nuclides where nucid = $n order by l_seqno limit 1";
                    command.Parameters.AddWithValue("$n", nucid);
                    object value = command.ExecuteScalar();
                    return value == null || value is DBNull ? 0.0 : Convert.ToDouble(value, CultureInfo.InvariantCulture);
                }
            }
        }

        static void Add(double[] histogram, double deposit, double binKev, double weight)
        {
            if (!(deposit > 0.0) || !(binKev > 0.0))
            {
                return;
            }

            int bin = (int)(deposit / binKev + 0.5);
            if (bin < 0 || bin >= histogram.Length)
            {
                bin = histogram.Length - 1;
            }

            histogram[bin] += weight;
        }

        /// <summary>
        /// Данные природного нуклида из `nucdb`: Z, распространённость (%), период (с).
        /// false — записи нет или поля пусты.
        /// </summary>
        static bool NaturalData(string nucid, out double z, out double abundance, out double seconds)
        {
            z = 0.0;
            abundance = 0.0;
            seconds = 0.0;
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nucdb.sqlite");
            if (!File.Exists(path))
            {
                return false;
            }

            try
            {
                using (var connection = new SqliteConnection("Data Source=" + path + ";Mode=ReadOnly"))
                {
                    connection.Open();
                    using (SqliteCommand command = connection.CreateCommand())
                    {
                        command.CommandText = "select z, abundance, half_life_sec from nuclides"
                                              + " where nucid = $n order by l_seqno limit 1";
                        command.Parameters.AddWithValue("$n", nucid);
                        using (SqliteDataReader reader = command.ExecuteReader())
                        {
                            if (!reader.Read() || reader.IsDBNull(0))
                            {
                                return false;
                            }

                            z = Convert.ToDouble(reader.GetValue(0), CultureInfo.InvariantCulture);
                            abundance = Number(reader, 1);
                            seconds = Number(reader, 2);
                            return abundance > 0.0 && seconds > 0.0;
                        }
                    }
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        static double Number(SqliteDataReader reader, int index)
        {
            if (reader.IsDBNull(index))
            {
                return 0.0;
            }

            double value;
            return double.TryParse(Convert.ToString(reader.GetValue(index), CultureInfo.InvariantCulture),
                                   NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                ? value
                : 0.0;
        }

        /// <summary>
        /// Массовая доля элемента Z в веществе кристалла.
        ///
        /// ⛔ (`AMBER274`, Э2, 05.11.2026, `crit8`) `GeometryMaterial.Fractions`
        /// — ДОЛИ МАССЫ (так их собирает `GeometryMaterialLibrary.Compose` из
        /// формулы: атомные доли × атомные массы, нормированные на сумму).
        /// Прежняя версия умножала их ещё раз на атомную массу, то есть у
        /// LaBr3 считала долю La 0.367 → 0.502 и завышала активность в 1.37
        /// раза.
        ///
        /// ⛔ (`AMBER274`, Э2, 06.11.2026) ЛЕГИРОВАНИЕ. Вещество `Lanthanum bromide` в библиотеке —
        /// формула La1 Br3 (`GeometryMaterialLibrary.cs`, у человека и в его сохранённом
        /// `GeometryMaterials.xml`), а настоящий кристалл — LaBr3:5%Ce: 5 % атомов лантана заменены
        /// церием, плотность та же, La-138 на 5 % меньше. Состав вещества в библиотеке НЕ менялся:
        /// набор Z {35, 57} опознаёт LaBr3 в `ElectronData.Match`, и цериевый состав увёл бы перенос
        /// и кривую света у всех сцен LaBr3, в том числе при выключенном слое. Поэтому легирование
        /// учтено только в этой доле. Вещество со своим церием считается как задано.
        /// </summary>
        static double MassShareOf(GeometryMaterial material, int z)
        {
            if (material == null || material.Fractions == null)
            {
                return 0.0;
            }

            double own = 0.0;
            double total = 0.0;
            foreach (KeyValuePair<int, double> pair in material.Fractions)
            {
                if (!(pair.Value > 0.0))
                {
                    continue;
                }

                total += pair.Value;
                if (pair.Key == z)
                {
                    own = pair.Value;
                }
            }

            if (z == LanthanumZ && material.Fractions.ContainsKey(BromineZ) && !material.Fractions.ContainsKey(CeriumZ))
            {
                double la, ce;
                if (MaterialDatabase.AtomicMass.TryGetValue(LanthanumZ, out la) && MaterialDatabase.AtomicMass.TryGetValue(CeriumZ, out ce)
                    && la > 0.0 && ce > 0.0 && own > 0.0)
                {
                    double kept = own * (1.0 - CeriumDopantShare);
                    double added = own * CeriumDopantShare * ce / la;
                    return kept / (total - own + kept + added);
                }
            }

            return total > 0.0 ? own / total : 0.0;
        }

        static string Pretty(string nucid)
        {
            return FsaSampleLibrary.PrettyName(nucid);
        }
    }
}
