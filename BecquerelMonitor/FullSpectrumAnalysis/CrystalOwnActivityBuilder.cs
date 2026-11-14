using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
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

        /// <summary>
        /// (`AMBER274` Э3, полоса `a274a` 14.11.2026) АЛЬФЫ РЯДА В СЛОЕ: фактор гашения света альфы QF на
        /// энергии <see cref="CrystalOwnActivityBuilder.AlphaQuenchingReferenceKev"/> — свет альфы с энергией
        /// E_α ложится на фотонную шкалу прибора в E_α·QF(E_α) кэВ-экв (QF меряется по пикам альф на шкале,
        /// откалиброванной гамма-линиями, — так и задан). ≤ 0 — АЛЬФ НЕТ (умолчание): раздел, клеймо и счёт
        /// побитно прежние (этап Э2). Числа QF в коде нет: таблица QF(E) по веществу — `matdb` словом Amber
        /// (решение 29.10.2026 «Таблица + уточнение по фону»), уточнение — по альфам фона прибора
        /// (проба `CrystalOwnAlphaProbeA274a`).
        /// </summary>
        public double AlphaQuenching;

        /// <summary>
        /// (`a274a`) Наклон QF по энергии альфы, на 1 МэВ: QF(E) = <see cref="AlphaQuenching"/> + наклон·(E − E_ref)/1000.
        /// Гашение у альфы тем слабее, чем она быстрее (тормозная способность ниже), — QF растёт с энергией.
        /// </summary>
        public double AlphaQuenchingSlopePerMev;

        /// <summary>(`a274a`) Альфы в слое включены.</summary>
        public bool HasAlpha
        {
            get { return this.AlphaQuenching > 0.0; }
        }
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
    ///   * ⛔ АЛЬФЫ — ТОЛЬКО ПО РЫЧАГУ (`AMBER274` Э3, полоса `a274a` 14.11.2026): умолчанием ряд Ac-227
    ///     даёт в слой только бета-члены (<see cref="ActiniumSeries"/>), и раздел побитно прежний. С
    ///     <see cref="CrystalOwnParts.AlphaQuenching"/> &gt; 0 в ряд входят альфы (свет с гашением QF плюс
    ///     каскад дочки), а активность ряда разбор берёт СЧЁТОМ АЛЬФ спектра
    ///     (<see cref="FixSeriesByAlphas"/>; решение Amber 13.11.2026 «По счёту альф в Э3»). У La-138 и
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

            /// <summary>
            /// (`AMBER274` Э3, `a274a`) Обратное к <see cref="ToEnergy"/>: свет пика полного поглощения на энергии
            /// <paramref name="energyKev"/> фотонной шкалы — линейно между узлами, за краями — крайним отношением.
            /// Нужен свету альфы: её E_α·QF задан на фотонной шкале, а складывается свет.
            /// </summary>
            public double ToLight(double energyKev)
            {
                int n = this.EnergyKev.Length;
                if (n == 0 || !(energyKev > 0.0))
                {
                    return energyKev;
                }

                if (energyKev <= this.EnergyKev[0])
                {
                    return energyKev * this.PerKev[0];
                }

                if (energyKev >= this.EnergyKev[n - 1])
                {
                    return energyKev * this.PerKev[n - 1];
                }

                int lo = 0, hi = n - 1;
                while (hi - lo > 1)
                {
                    int mid = (lo + hi) / 2;
                    if (this.EnergyKev[mid] <= energyKev)
                    {
                        lo = mid;
                    }
                    else
                    {
                        hi = mid;
                    }
                }

                double span = this.EnergyKev[hi] - this.EnergyKev[lo];
                return span > 0.0
                    ? this.Light[lo] + (this.Light[hi] - this.Light[lo]) * (energyKev - this.EnergyKev[lo]) / span
                    : this.Light[lo];
            }

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

        /// <summary>
        /// ⛔ (`AMBER274`, полоса `a274t` 13.11.2026) ВЕРСИЯ РЕЦЕПТА СЛОЯ — поднимать при ЛЮБОЙ правке
        /// счёта события слоя в коде (не рычагом), которая меняет гистограмму: раздел прежнего
        /// рецепта разбор после этого не возьмёт (`ResponseCrystalSection.UsableFor`). 2 — рецепт
        /// ревизии по статьям 12.11.2026 (`a274r`/`a274s`): энергия связи после конверсии оседает,
        /// свет по фотонной шкале, L захвата из EADL. Разделов рецепта 1 не писалось (слой выключен).
        /// </summary>
        public const int RecipeVersion = 2;

        /// <summary>
        /// (`a274t`) Рецепт слоя в клейме раздела: версия и рычаги, меняющие гистограмму
        /// (<see cref="CrystalOwnParts"/>); null — рецепт умолчания. Подмены замера (форма беты,
        /// доля K-захвата) пишутся признаком: раздел с ними разбору не годен никогда.
        /// </summary>
        public static string RecipeStamp(CrystalOwnParts parts)
        {
            CrystalOwnParts p = parts ?? new CrystalOwnParts();
            var sb = new StringBuilder();
            sb.Append(RecipeVersion.ToString(CultureInfo.InvariantCulture)).Append(':');
            sb.Append(p.Beta ? 'b' : '-').Append(p.BranchGamma ? 'g' : '-').Append(p.Capture ? 'c' : '-')
              .Append(p.DaughterRelaxation ? 'd' : '-').Append(p.Series ? 's' : '-').Append(p.FreeNatural ? 'f' : '-')
              .Append(p.ConversionRelaxation ? 'v' : '-').Append(p.LBindingFromEadl ? 'l' : '-');
            sb.Append(":sc").Append(p.Scale.ToString(CultureInfo.InvariantCulture));
            if (p.BetaOverride != null)
            {
                sb.Append(":bo");
            }

            if (p.KCaptureOverride > 0.0)
            {
                sb.Append(":kc").Append(p.KCaptureOverride.ToString("R", CultureInfo.InvariantCulture));
            }

            // (`AMBER274` Э3, `a274a`) альфы и их гашение — только при ВКЛ: клеймо умолчания побитно прежнее
            if (p.HasAlpha)
            {
                sb.Append(":aq").Append(p.AlphaQuenching.ToString("R", CultureInfo.InvariantCulture))
                  .Append(":as").Append(p.AlphaQuenchingSlopePerMev.ToString("R", CultureInfo.InvariantCulture))
                  .Append(":av").Append(AlphaRecipeVersion.ToString(CultureInfo.InvariantCulture));
            }

            return sb.ToString();
        }

        /// <summary>Верх гистограммы, кэВ: выше самой жёсткой линии слоя (1473 у La-138).</summary>
        public const double HistogramTopKev = 3000.0;

        /// <summary>
        /// (`AMBER274` Э3, `a274a`) Верх гистограммы раздела С АЛЬФАМИ, кэВ-экв: альфа Po-215 7386 кэВ при QF ≈ 0.4 —
        /// ≈ 3000, альфа Rn-219 с каскадом 271 + 402 кэВ — до ≈ 3400. Выше верха событие легло бы в последний бин
        /// (<see cref="Add"/>), поэтому верх раздела с альфами свой; без альф — прежний.
        /// </summary>
        public const double AlphaHistogramTopKev = 4500.0;

        /// <summary>(`a274a`) Энергия альфы, на которой задан QF (<see cref="CrystalOwnParts.AlphaQuenching"/>), кэВ.</summary>
        public const double AlphaQuenchingReferenceKev = 6000.0;

        /// <summary>
        /// (`a274a`) Версия счёта событий альф в клейме (`:av`) — поднимать при правке счёта альф в коде.
        /// 1 — альфа светит E_α·QF(E_α) на фотонной шкале, каскад дочки по схеме Geant4 из той же точки, свет
        /// складывается; отдача ядра — внутри QF; вылет альфы с поверхности кристалла не ведётся.
        /// </summary>
        public const int AlphaRecipeVersion = 1;

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
                // (`a274t`) клеймо несёт рецепт, которым раздел собран, — не умолчание
                Stamp = ResponseCrystalSection.ComputeStamp(geometry, binKev, parts),
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

            int bins = ResponseMatrix.ImageBins(parts.HasAlpha ? AlphaHistogramTopKev : HistogramTopKev, binKev);
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
                List<Member> series = ActiniumSeries(notes, parts.HasAlpha);
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
                        AlphaPerDecay = parts.HasAlpha ? new double[bins] : null,
                    };

                    foreach (Member member in series)
                    {
                        var seriesParts = new CrystalOwnParts
                        {
                            Scale = parts.Scale,
                            ConversionRelaxation = parts.ConversionRelaxation,
                            AlphaQuenching = parts.AlphaQuenching,                          // (`a274a`) 0 — как прежде
                            AlphaQuenchingSlopePerMev = parts.AlphaQuenchingSlopePerMev,
                        };
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

                        if (combined.AlphaPerDecay != null && part.AlphaPerDecay != null)
                        {
                            for (int i = 0; i < bins; i++)
                            {
                                combined.AlphaPerDecay[i] += member.Weight * part.AlphaPerDecay[i];
                            }
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
        static List<Member> ActiniumSeries(List<string> notes, bool alpha)
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

            // (`AMBER274` Э3, `a274a`) ЧИСТЫЕ АЛЬФА-ИЗЛУЧАТЕЛИ ряда — только при альфах, В КОНЕЦ списка: зерно
            // члена — номер в списке, и бета-члены остаются со своими зёрнами. Веса — распадов члена на распад
            // головы: Th-227 питается β⁻ Ac-227, Ra-223 — Th-227 и ответвлением Fr-223 (вместе — каждый распад),
            // Rn-219 и Po-215 — каждый распад. Альфы Ac-227 (ответвление на Fr-223) и Bi-211 несут их записи
            // выше. Бета Fr-223 (1.38 %) и альфа Po-211 (β⁻ Bi-211, 0.28 %) не ведутся — названо в примечании.
            if (alpha)
            {
                list.Add(new Member { Nuclide = "227TH", Weight = head / 100.0 });
                list.Add(new Member { Nuclide = "223RA", Weight = 1.0 });
                list.Add(new Member { Nuclide = "219RN", Weight = 1.0 });
                list.Add(new Member { Nuclide = "215PO", Weight = 1.0 });
                notes.Add("ряд Ac-227: бета Fr-223 и альфа Po-211 не ведутся");
            }

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

            // (`AMBER274` Э3, `a274a`) альфа-ветви члена — только при альфах; без них список пуст и счёт прежний
            List<AlphaLine> alphas = parts.HasAlpha ? AlphaLines(member.Nuclide) : new List<AlphaLine>();
            double alphaPercent = 0.0;
            foreach (AlphaLine line in alphas)
            {
                alphaPercent += line.Intensity;
            }

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
            if (!(betaPercent + ecPercent + alphaPercent > 0.0))
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

            // (`AMBER274` Э3, `a274a`) АЛЬФА-ВЕТВИ: событие — альфа (свет с гашением) плюс каскад дочки из той же
            // точки (<see cref="AlphaEvent"/>). Истории — на распад нуклида по выходу линии, как у беты.
            if (alphas.Count > 0)
            {
                entry.AlphaPerDecay = new double[bins];
                foreach (AlphaLine line in alphas)
                {
                    int n = (int)Math.Round(histories * line.Intensity / 100.0);
                    for (int i = 0; i < n; i++)
                    {
                        if (!simulator.TryCrystalPoint(out x, out y, out z))
                        {
                            break;
                        }

                        AlphaSample sample = AlphaEvent(simulator, line, x, y, z);
                        double position = AlphaPosition(sample, parts, light);
                        Add(entry.PerDecay, position, binKev, scale);
                        Add(entry.AlphaPerDecay, position, binKev, scale);
                    }
                }
            }

            return entry;
        }

        /// <summary>
        /// (`AMBER274` Э3, `a274a`) АЛЬФА-ЛИНИЯ нуклида: энергия и выход (`nucdb.decay_radiations`, `type_a = 'A'`,
        /// ветвь `dec_type = 0`), уровень дочки, который она питает (баланс Q_α − E_α·A/(A − 4), Q_α — `nuclides.qa`),
        /// и строка каскада с этого уровня (<see cref="FsaBetaLibrary.AlphaEventSpec"/>; null — каскада нет).
        /// </summary>
        public sealed class AlphaLine
        {
            public string Nuclide;
            public double EnergyKev;

            /// <summary>Выход, % на распад нуклида.</summary>
            public double Intensity;

            /// <summary>Уровень дочки по балансу, кэВ.</summary>
            public double LevelKev;

            public int DaughterZ;
            public int DaughterA;

            /// <summary>Каскад с уровня (null — альфа на основное состояние, изомер или уровня в схеме нет).</summary>
            public ElectronBranchSpec Cascade;

            /// <summary>Атом дочки — энергия связи после конверсии каскада оседает (как у беты); null — данных нет.</summary>
            public CaptureRelaxation Atom;

            /// <summary>Почему каскада нет (null — есть).</summary>
            public string NoCascade;
        }

        /// <summary>(`a274a`) Одно разыгранное событие альфы: энергия альфы, осевшее и свет каскада.</summary>
        public struct AlphaSample
        {
            public double AlphaKev;
            public double CascadeKev;
            public double CascadeLight;
        }

        /// <summary>
        /// (`a274a`) Альфа-линии нуклида из `nucdb` со строками каскада. Пусто — альф нет или базы нет. Кэш по нуклиду.
        /// </summary>
        public static List<AlphaLine> AlphaLines(string nucid)
        {
            lock (AlphaGate)
            {
                List<AlphaLine> cached;
                if (AlphaCache.TryGetValue(nucid ?? "", out cached))
                {
                    return cached;
                }
            }

            var list = new List<AlphaLine>();
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nucdb.sqlite");
            if (string.IsNullOrEmpty(nucid) || !File.Exists(path))
            {
                return list;
            }

            double z = 0.0, q = double.NaN;
            var rows = new List<double[]>();
            using (var connection = new SqliteConnection("Data Source=" + path + ";Mode=ReadOnly"))
            {
                connection.Open();
                using (SqliteCommand command = connection.CreateCommand())
                {
                    command.CommandText = "select z, qa from nuclides where nucid = $n order by l_seqno limit 1";
                    command.Parameters.AddWithValue("$n", nucid);
                    using (SqliteDataReader reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            z = reader.IsDBNull(0) ? 0.0 : Convert.ToDouble(reader.GetValue(0), CultureInfo.InvariantCulture);
                            double qa;
                            if (!reader.IsDBNull(1) && double.TryParse(Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture),
                                                                       NumberStyles.Float, CultureInfo.InvariantCulture, out qa))
                            {
                                q = qa;
                            }
                        }
                    }
                }

                using (SqliteCommand command = connection.CreateCommand())
                {
                    command.CommandText = "select energy_num, intensity_num from decay_radiations"
                                          + " where parent_nucid = $n and type_a = 'A' and dec_type = '0' order by energy_num desc";
                    command.Parameters.AddWithValue("$n", nucid);
                    using (SqliteDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
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

            int a = MassNumberOf(nucid);
            if (z > 2.0 && a > 4 && !double.IsNaN(q))
            {
                int dz = (int)z - 2;
                int da = a - 4;
                CaptureRelaxation atom = CaptureRelaxation.ForAtom(dz, FsaBetaLibrary.Binding(dz)[1]);
                foreach (double[] row in rows)
                {
                    double level = Math.Max(0.0, q - row[0] * a / (a - 4.0));
                    string why;
                    ElectronBranchSpec cascade = FsaBetaLibrary.AlphaEventSpec(dz, da, level, out why);
                    list.Add(new AlphaLine
                    {
                        Nuclide = nucid,
                        EnergyKev = row[0],
                        Intensity = row[1],
                        LevelKev = level,
                        DaughterZ = dz,
                        DaughterA = da,
                        Cascade = cascade,
                        Atom = atom,
                        NoCascade = cascade == null ? why : null,
                    });
                }
            }

            lock (AlphaGate)
            {
                AlphaCache[nucid] = list;
            }

            return list;
        }

        static readonly object AlphaGate = new object();
        static readonly Dictionary<string, List<AlphaLine>> AlphaCache = new Dictionary<string, List<AlphaLine>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Массовое число из имени `nucdb` (`227TH` → 227); 0 — не разобралось.</summary>
        static int MassNumberOf(string nucid)
        {
            int i = 0;
            while (i < nucid.Length && char.IsDigit(nucid[i]))
            {
                i++;
            }

            int a;
            return i > 0 && int.TryParse(nucid.Substring(0, i), NumberStyles.Integer, CultureInfo.InvariantCulture, out a) ? a : 0;
        }

        /// <summary>
        /// (`a274a`) Событие альфы в точке (x, y, z): каскад дочки — <c>CrystalEventFrom</c> без беты (свет каскада —
        /// <c>CrystalEventLight</c>); сама альфа — пробег в микроны, оседает на месте целиком, её свет ставит
        /// <see cref="AlphaPosition"/> гашением.
        /// </summary>
        public static AlphaSample AlphaEvent(EfficiencySimulator simulator, AlphaLine line, double x, double y, double z)
        {
            var sample = new AlphaSample { AlphaKev = line.EnergyKev };
            if (line.Cascade != null)
            {
                double gamma;
                sample.CascadeKev = simulator.CrystalEventFrom(line.Cascade, line.Atom, x, y, z, false, out gamma);
                sample.CascadeLight = simulator.CrystalEventLight;
            }

            return sample;
        }

        /// <summary>(`a274a`) Свет альфы на фотонной шкале прибора, кэВ-экв: E_α·QF(E_α).</summary>
        public static double AlphaLightKev(double alphaKev, CrystalOwnParts parts)
        {
            double qf = parts.AlphaQuenching + parts.AlphaQuenchingSlopePerMev * (alphaKev - AlphaQuenchingReferenceKev) / 1000.0;
            return alphaKev * Math.Max(0.0, qf);
        }

        /// <summary>
        /// (`a274a`) Место события альфы на шкале гистограммы. Свет складывается: каскад (электроны и фотоны —
        /// своей кривой света) плюс альфа (E_α·QF на фотонной шкале → свет обратной функцией
        /// <see cref="PhotonLightScale.ToLight"/>). Шкала — энергия (кривой света нет): осевшее каскада + E_α·QF.
        /// </summary>
        public static double AlphaPosition(AlphaSample sample, CrystalOwnParts parts, PhotonLightScale light)
        {
            double alphaEq = AlphaLightKev(sample.AlphaKev, parts);
            if (light == null || parts.Scale == ScaleEnergy || !(sample.CascadeKev > 0.0) || !(sample.CascadeLight > 0.0))
            {
                if (light == null || parts.Scale == ScaleEnergy)
                {
                    return sample.CascadeKev + alphaEq;
                }

                // каскад не осел — одна альфа: на фотонной шкале она и есть E_α·QF
                return parts.Scale == ScaleAnchor && light.AnchorPerKev > 0.0
                    ? light.ToLight(alphaEq) / light.AnchorPerKev
                    : alphaEq;
            }

            double total = sample.CascadeLight + light.ToLight(alphaEq);
            return parts.Scale == ScaleAnchor
                ? (light.AnchorPerKev > 0.0 ? total / light.AnchorPerKev : sample.CascadeKev + alphaEq)
                : light.ToEnergy(total);
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

        // ------------------------------------------------------------------
        // (`AMBER274` Э3, полоса `a274a` 14.11.2026) СЧЁТ РЯДА ПО АЛЬФАМ
        // ------------------------------------------------------------------

        /// <summary>(`a274a`) Итог счёта ряда по альфам спектра.</summary>
        public sealed class AlphaCount
        {
            /// <summary>Активность головы ряда, Бк (0 — альф не найдено).</summary>
            public double ActivityBq;

            /// <summary>Погрешность (Пуассон окна и подложки), Бк.</summary>
            public double SigmaBq;

            /// <summary>Значимость: активность / погрешность.</summary>
            public double Z;

            /// <summary>Окно счёта, кэВ-экв шкалы спектра.</summary>
            public double WindowLoKev, WindowHiKev;

            /// <summary>Полоса подложки ниже окна, кэВ-экв.</summary>
            public double SideLoKev, SideHiKev;

            /// <summary>Отсчётов в окне; подложка в окне (оценка); отсчётов образа ряда в окне на 1 Бк·с.</summary>
            public double Counts, Baseline, TemplatePerBqS;

            /// <summary>Доля альф образа выше верха шкалы спектра (срезана АЦП): счёт на неё поправлен образом, но это допущение.</summary>
            public double CutAboveTop;

            /// <summary>χ²/ndf подгонки «образ ряда + постоянная» по окну и полосе подложки (Пирсон по данным).</summary>
            public double ChiPerNdf;

            /// <summary>Найден ли ряд (значимость не меньше <see cref="AlphaCountMinZ"/>).</summary>
            public bool Found;

            /// <summary>
            /// Счёт годен: подложка в окне не больше <see cref="AlphaCountMaxBaselineShare"/> отсчётов окна. Больше —
            /// в окне альф лежат чужие линии или их комптон (проба с U/Th/Ra), и постоянная подложка их не вычитает.
            /// </summary>
            public bool Reliable;

            public string Note;
        }

        /// <summary>(`a274a`) Значимость счёта альф, с которой ряд считается найденным.</summary>
        public const double AlphaCountMinZ = 3.0;

        /// <summary>
        /// (`a274a`) Наибольшая доля подложки в окне альф, при которой счёт годен. Замер на втором приборе LaBr3
        /// (журнал `handover-2026-11-14-a274a.md`): 11 фонов — 4.7…7.0 %, пробы без линий выше 1.7 МэВ — 4.6…8.7 %;
        /// пробы с U/Th/Ra — 12…75 %, и их счёт уходит от счёта своего фона на −7…+1900 %.
        /// </summary>
        public const double AlphaCountMaxBaselineShare = 0.10;

        /// <summary>(`a274a`) Ширина полосы подложки ниже окна альф, кэВ-экв.</summary>
        public const double AlphaSideBandKev = 300.0;

        /// <summary>
        /// (`AMBER274` Э3, `a274a`) АКТИВНОСТЬ РЯДА ПО СЧЁТУ АЛЬФ СПЕКТРА — решение Amber 13.11.2026 вопросником,
        /// дословно: «По счёту альф в Э3 (Рекомендую)». Окно — область альф образа
        /// (<see cref="CrystalOwnEntry.AlphaPerDecay"/>, от 0.1 до 99.9 % их доли) с запасом в две FWHM с каждой
        /// стороны, сверху — не выше шкалы спектра; ниже окна — полоса подложки <see cref="AlphaSideBandKev"/>.
        /// По окну и полосе подгоняются две величины — активность при ПОЛНОМ образе ряда (альфы, бета, каскады)
        /// и постоянная подложка на канал (взвешенные наименьшие квадраты, вес 1/max(y, 1)). Активность —
        /// (отсчёты окна − подложка) / (образ окна на 1 Бк·с): СЧЁТ, а не форма, — сдвиг групп альф внутри окна
        /// (QF, шкала) его не меняет. ⚠ Подложка постоянная: гамма-линия пробы внутри окна (2614 кэВ рядом с
        /// группой Rn-219 + Bi-211) счёт завышает — её видно по <see cref="AlphaCount.ChiPerNdf"/>.
        /// </summary>
        /// <param name="entry">запись ряда с частью альф</param>
        /// <param name="binKev">шаг сетки раздела</param>
        /// <param name="counts">отсчёты спектра по каналам</param>
        /// <param name="liveTime">живое время, с</param>
        /// <param name="energyAt">энергия (кэВ-экв) дробного канала — шкала спектра</param>
        /// <param name="fwhmKevAt">FWHM на энергии, кэВ</param>
        /// <param name="topKev">верх счёта, кэВ-экв (≤ 0 — верх шкалы спектра)</param>
        public static AlphaCount CountByAlphas(CrystalOwnEntry entry, double binKev, double[] counts, double liveTime,
                                               Func<double, double> energyAt, Func<double, double> fwhmKevAt, double topKev)
        {
            return CountByAlphas(entry, binKev, counts, liveTime, energyAt, fwhmKevAt, topKev, null);
        }

        /// <summary>
        /// (`a274a`) То же с ИЗВЕСТНОЙ частью слоя <paramref name="fixedPerSecond"/> (природные записи раздела ×
        /// их активность, отсчётов в секунду по бинам сетки): она уширяется тем же гауссом и вычитается из данных
        /// до счёта, а полоса подложки начинается не ниже её верха (99.99 % её доли плюс три FWHM) — иначе в
        /// полосу подложки ложится пик суммы La-138 1436 + K (у LaBr3 он на 300 кэВ ниже группы альф) и
        /// постоянная подложка его съедает. Полоса подложки уже 50 кэВ — подложки нет (b = 0), это в примечании.
        /// </summary>
        public static AlphaCount CountByAlphas(CrystalOwnEntry entry, double binKev, double[] counts, double liveTime,
                                               Func<double, double> energyAt, Func<double, double> fwhmKevAt, double topKev,
                                               double[] fixedPerSecond)
        {
            var result = new AlphaCount();
            if (entry == null || entry.AlphaPerDecay == null || entry.PerDecay == null || counts == null || counts.Length < 16
                || !(binKev > 0.0) || !(liveTime > 0.0) || energyAt == null || fwhmKevAt == null)
            {
                result.Note = "нет части альф, спектра или шкалы";
                return result;
            }

            int channels = counts.Length;
            var edge = new double[channels + 1];
            for (int i = 0; i <= channels; i++)
            {
                edge[i] = energyAt(i - 0.5);
            }

            for (int i = 1; i <= channels; i++)
            {
                if (!(edge[i] > edge[i - 1]))
                {
                    result.Note = "шкала спектра не возрастает";
                    return result;
                }
            }

            double spectrumTop = edge[channels];
            double top = topKev > 0.0 ? Math.Min(topKev, spectrumTop) : spectrumTop;

            // Область альф образа: 0.1 … 99.9 % их доли.
            double alphaSum = 0.0;
            foreach (double v in entry.AlphaPerDecay)
            {
                alphaSum += v;
            }

            if (!(alphaSum > 0.0))
            {
                result.Note = "часть альф пуста";
                return result;
            }

            double run = 0.0, eLo = double.NaN, eHi = double.NaN;
            for (int k = 0; k < entry.AlphaPerDecay.Length; k++)
            {
                run += entry.AlphaPerDecay[k];
                if (double.IsNaN(eLo) && run >= 0.001 * alphaSum)
                {
                    eLo = k * binKev;
                }

                if (double.IsNaN(eHi) && run >= 0.999 * alphaSum)
                {
                    eHi = k * binKev;
                }
            }

            result.WindowLoKev = eLo - 2.0 * Math.Max(0.0, fwhmKevAt(eLo));
            result.WindowHiKev = Math.Min(top, eHi + 2.0 * Math.Max(0.0, fwhmKevAt(eHi)));
            result.SideHiKev = result.WindowLoKev;
            result.SideLoKev = result.WindowLoKev - AlphaSideBandKev;
            double fixedTop = 0.0;
            if (fixedPerSecond != null)
            {
                double fixedSum = 0.0, fixedRun = 0.0;
                foreach (double v in fixedPerSecond)
                {
                    fixedSum += Math.Max(0.0, v);
                }

                for (int k = 0; k < fixedPerSecond.Length && fixedSum > 0.0; k++)
                {
                    fixedRun += Math.Max(0.0, fixedPerSecond[k]);
                    if (fixedRun >= 0.9999 * fixedSum)
                    {
                        fixedTop = k * binKev + 3.0 * Math.Max(0.0, fwhmKevAt(k * binKev));
                        break;
                    }
                }

                result.SideLoKev = Math.Max(result.SideLoKev, fixedTop);
            }

            bool noSide = result.SideHiKev - result.SideLoKev < 50.0;
            if (!(result.WindowHiKev > result.WindowLoKev) || result.SideLoKev < edge[0])
            {
                result.Note = "окно альф вне шкалы спектра";
                return result;
            }

            // Образ ряда и его альф на 1 Бк·с по каналам: гаусс с FWHM шкалы, по бинам сетки.
            double[] template = Broaden(entry.PerDecay, binKev, edge, fwhmKevAt);
            double[] known = fixedPerSecond != null ? Broaden(fixedPerSecond, binKev, edge, fwhmKevAt) : null;
            double alphaAll = 0.0, alphaAbove = 0.0;
            for (int k = 0; k < entry.AlphaPerDecay.Length; k++)
            {
                alphaAll += entry.AlphaPerDecay[k];
                if (k * binKev > top)
                {
                    alphaAbove += entry.AlphaPerDecay[k];
                }
            }

            result.CutAboveTop = alphaAll > 0.0 ? alphaAbove / alphaAll : 0.0;

            // Каналы окна и полосы подложки (по центру канала).
            double sww = 0.0, swt = 0.0, swtt = 0.0, swy = 0.0, swty = 0.0;
            double nWin = 0.0, yWin = 0.0, tWin = 0.0;
            int used = 0;
            for (int i = 0; i < channels; i++)
            {
                double centre = 0.5 * (edge[i] + edge[i + 1]);
                bool win = centre >= result.WindowLoKev && centre < result.WindowHiKev;
                bool side = !noSide && centre >= result.SideLoKev && centre < result.SideHiKev;
                if (!win && !side)
                {
                    continue;
                }

                double y = counts[i] - (known != null ? known[i] * liveTime : 0.0);
                double tt = template[i] * liveTime;
                double w = 1.0 / Math.Max(y, 1.0);
                sww += w;
                swt += w * tt;
                swtt += w * tt * tt;
                swy += w * y;
                swty += w * tt * y;
                used++;
                if (win)
                {
                    nWin += 1.0;
                    yWin += y;
                    tWin += tt;
                }
            }

            double det = sww * swtt - swt * swt;
            if (used < 4 || !(det > 0.0) || !(tWin > 0.0))
            {
                result.Note = "окно альф без отсчётов или без образа";
                return result;
            }

            // y = A·t + b: решение 2×2 и ковариация подложки; без полосы подложки — b = 0.
            double aFit = (sww * swty - swt * swy) / det;
            double bFit = (swtt * swy - swt * swty) / det;
            double varB = swtt / det;
            if (noSide)
            {
                aFit = swty / swtt;
                bFit = 0.0;
                varB = 0.0;
            }

            if (aFit < 0.0)
            {
                aFit = 0.0;
                bFit = noSide ? 0.0 : swy / sww;
                varB = noSide ? 0.0 : 1.0 / sww;
            }

            double chi = 0.0;
            for (int i = 0; i < channels; i++)
            {
                double centre = 0.5 * (edge[i] + edge[i + 1]);
                if ((centre >= result.WindowLoKev && centre < result.WindowHiKev)
                    || (!noSide && centre >= result.SideLoKev && centre < result.SideHiKev))
                {
                    double r = counts[i] - (known != null ? known[i] * liveTime : 0.0) - aFit * template[i] * liveTime - bFit;
                    chi += r * r / Math.Max(counts[i], 1.0);
                }
            }

            result.ChiPerNdf = used > 2 ? chi / (used - (noSide ? 1 : 2)) : 0.0;
            result.Counts = yWin;
            result.Baseline = Math.Max(0.0, bFit) * nWin;
            result.TemplatePerBqS = tWin / liveTime;
            double net = yWin - result.Baseline;
            result.ActivityBq = Math.Max(0.0, net / tWin);
            result.SigmaBq = Math.Sqrt(Math.Max(yWin, 1.0) + varB * nWin * nWin) / tWin;
            result.Z = result.SigmaBq > 0.0 ? result.ActivityBq / result.SigmaBq : 0.0;
            result.Found = result.Z >= AlphaCountMinZ;
            result.Reliable = result.Found && yWin > 0.0 && result.Baseline <= AlphaCountMaxBaselineShare * yWin;
            var note = new StringBuilder();
            note.Append("окно ").Append(result.WindowLoKev.ToString("F0", CultureInfo.InvariantCulture)).Append('–')
                .Append(result.WindowHiKev.ToString("F0", CultureInfo.InvariantCulture)).Append(" кэВ-экв, подложка ")
                .Append((result.Counts > 0.0 ? 100.0 * result.Baseline / result.Counts : 0.0).ToString("F1", CultureInfo.InvariantCulture))
                .Append(" %, χ²/ndf ").Append(result.ChiPerNdf.ToString("F2", CultureInfo.InvariantCulture));
            if (noSide)
            {
                note.Append("; полосы подложки нет (известная часть слоя до ")
                    .Append(fixedTop.ToString("F0", CultureInfo.InvariantCulture)).Append(" кэВ-экв) — подложка 0");
            }

            if (result.CutAboveTop > 0.001)
            {
                note.Append("; выше верха шкалы ").Append((100.0 * result.CutAboveTop).ToString("F1", CultureInfo.InvariantCulture))
                    .Append(" % альф образа");
            }

            result.Note = note.ToString();
            return result;
        }

        /// <summary>
        /// (`a274a`) Счёт по альфам на шкале спектра приложения: энергия — <paramref name="calibration"/>, FWHM —
        /// <paramref name="fwhmCalibration"/> (в каналах) через шаг шкалы. Нет FWHM — счёт не идёт.
        /// </summary>
        public static AlphaCount CountByAlphas(CrystalOwnEntry entry, double binKev, int[] raw, double liveTime,
                                               EnergyCalibration calibration, FwhmCalibration fwhmCalibration)
        {
            return CountByAlphas(entry, binKev, raw, liveTime, calibration, fwhmCalibration, null);
        }

        /// <summary>(`a274a`) То же с известной частью слоя (см. перегрузку с <c>fixedPerSecond</c>).</summary>
        public static AlphaCount CountByAlphas(CrystalOwnEntry entry, double binKev, int[] raw, double liveTime,
                                               EnergyCalibration calibration, FwhmCalibration fwhmCalibration,
                                               double[] fixedPerSecond)
        {
            if (raw == null || calibration == null || fwhmCalibration == null)
            {
                return new AlphaCount { Note = "нет спектра, шкалы или FWHM" };
            }

            var counts = new double[raw.Length];
            for (int i = 0; i < raw.Length; i++)
            {
                counts[i] = raw[i];
            }

            int channels = raw.Length;
            Func<double, double> energyAt = calibration.ChannelToEnergy;
            Func<double, double> fwhmKevAt = e =>
            {
                double ch = calibration.EnergyToChannel(e, Math.Max(channels * 4, 10000));
                double step = Math.Abs(calibration.ChannelToEnergy(ch + 0.5) - calibration.ChannelToEnergy(ch - 0.5));
                return fwhmCalibration.ChannelToFwhm(ch) * step;
            };
            return CountByAlphas(entry, binKev, counts, liveTime, energyAt, fwhmKevAt, 0.0, fixedPerSecond);
        }

        /// <summary>
        /// (`AMBER274` Э3, `a274a`) РАЗДЕЛ ДЛЯ РАЗБОРА СПЕКТРА: свободная запись ряда с частью альф получает
        /// активность СЧЁТОМ АЛЬФ ЭТОГО спектра (<see cref="CountByAlphas(CrystalOwnEntry, double, int[], double, EnergyCalibration, FwhmCalibration)"/>)
        /// и становится записью с известной амплитудой (вычитается до фита, как природные); ряд не найден
        /// (значимость ниже <see cref="AlphaCountMinZ"/>) — запись снимается; счёт не годен
        /// (<see cref="AlphaCount.Reliable"/>: в окне альф чужие линии) — запись остаётся свободной.
        /// ⚠ Решение Amber 13.11.2026 «у `LABR_BRIL` строку не включать» данными НЕ исполняется: на фоне корпусного
        /// `LABR_BRIL` счёт даёт 0.311 Бк при Z = 50 и подложке 21.6 % (критик `crit274a`) — ряд «найден», счёт
        /// негоден, строка остаётся свободной. Исполнять его — выбором источника QF (без QF у прибора альф в разделе
        /// нет вовсе), а не этим входом.
        /// Записи без части альф и природные — как есть. Возвращает копию; исходный раздел не меняется.
        /// </summary>
        public static ResponseCrystalSection FixSeriesByAlphas(ResponseCrystalSection section, int[] raw, double liveTime,
                                                               EnergyCalibration calibration, FwhmCalibration fwhmCalibration,
                                                               List<AlphaCount> counted, out string note)
        {
            note = null;
            if (section == null || !section.HasAlpha)
            {
                return section;
            }

            var copy = new ResponseCrystalSection
            {
                Stamp = section.Stamp,
                BinKev = section.BinKev,
                Histories = section.Histories,
                Note = section.Note,
            };
            var notes = new List<string>();
            double[] known = KnownPerSecond(section);
            foreach (CrystalOwnEntry entry in section.Entries)
            {
                if (entry == null || !entry.Free || entry.AlphaPerDecay == null)
                {
                    copy.Entries.Add(entry);
                    continue;
                }

                AlphaCount count = CountByAlphas(entry, section.BinKev, raw, liveTime, calibration, fwhmCalibration, known);
                if (counted != null)
                {
                    counted.Add(count);
                }

                string head = Pretty(entry.Nuclide) + " по альфам: ";
                if (!count.Found)
                {
                    notes.Add(head + "ряд не найден (" + count.ActivityBq.ToString("F3", CultureInfo.InvariantCulture) + " ± "
                              + count.SigmaBq.ToString("F3", CultureInfo.InvariantCulture) + " Бк; " + count.Note + ") — строка снята");
                    continue;
                }

                if (!count.Reliable)
                {
                    // Ряд есть, но в окне альф чужие линии: счёт не годен, строка остаётся СВОБОДНОЙ — амплитуду
                    // делит с образами пробы подгонка FSA (решение о запасном пути — за Amber, журнал `a274a`).
                    notes.Add(head + "счёт не годен (подложка " + (count.Counts > 0.0 ? 100.0 * count.Baseline / count.Counts : 0.0)
                                  .ToString("F1", CultureInfo.InvariantCulture) + " % > "
                              + (100.0 * AlphaCountMaxBaselineShare).ToString("F0", CultureInfo.InvariantCulture)
                              + " %: в окне альф чужие линии) — строка свободная");
                    copy.Entries.Add(entry);
                    continue;
                }

                notes.Add(head + count.ActivityBq.ToString("F3", CultureInfo.InvariantCulture) + " ± "
                          + count.SigmaBq.ToString("F3", CultureInfo.InvariantCulture) + " Бк (" + count.Note + ")");
                copy.Entries.Add(new CrystalOwnEntry
                {
                    Nuclide = entry.Nuclide,
                    Layer = entry.Layer,
                    Free = false,
                    ActivityBq = count.ActivityBq,
                    Histories = entry.Histories,
                    PerDecay = entry.PerDecay,
                    AlphaPerDecay = entry.AlphaPerDecay,
                });
            }

            note = notes.Count > 0 ? string.Join("; ", notes.ToArray()) : null;
            return copy;
        }

        /// <summary>
        /// (`a274a`) Известная часть слоя в секунду по бинам сетки: природные записи раздела (не свободные) ×
        /// их активность. null — таких записей нет.
        /// </summary>
        public static double[] KnownPerSecond(ResponseCrystalSection section)
        {
            if (section == null || section.Entries == null)
            {
                return null;
            }

            double[] known = null;
            foreach (CrystalOwnEntry entry in section.Entries)
            {
                if (entry == null || entry.Free || entry.PerDecay == null || !(entry.ActivityBq > 0.0))
                {
                    continue;
                }

                if (known == null)
                {
                    known = new double[entry.PerDecay.Length];
                }

                for (int k = 0; k < entry.PerDecay.Length && k < known.Length; k++)
                {
                    known[k] += entry.PerDecay[k] * entry.ActivityBq;
                }
            }

            return known;
        }

        /// <summary>
        /// (`a274a`) Гистограмма на сетке <paramref name="binKev"/> (бин k — центр k·шаг) → каналы с границами
        /// <paramref name="edge"/>: гаусс с FWHM <paramref name="fwhmKevAt"/> на энергии бина, доля канала — разность
        /// функции ошибок. Для счёта альф, а не для фита: уширение разбора — своё (`BroadenResponseDeposit`).
        /// </summary>
        static double[] Broaden(double[] histogram, double binKev, double[] edge, Func<double, double> fwhmKevAt)
        {
            int channels = edge.Length - 1;
            var result = new double[channels];
            for (int k = 0; k < histogram.Length; k++)
            {
                double v = histogram[k];
                if (!(v > 0.0))
                {
                    continue;
                }

                double e = k * binKev;
                double sigma = Math.Max(fwhmKevAt(e), binKev) / 2.3548200450309493;
                double lo = e - 5.0 * sigma, hi = e + 5.0 * sigma;
                int i0 = Lower(edge, lo);
                for (int i = Math.Max(0, i0 - 1); i < channels && edge[i] < hi; i++)
                {
                    double a = Phi((edge[i] - e) / sigma), b = Phi((edge[i + 1] - e) / sigma);
                    result[i] += v * (b - a);
                }
            }

            return result;
        }

        static int Lower(double[] edge, double value)
        {
            int lo = 0, hi = edge.Length - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (edge[mid] <= value)
                {
                    lo = mid;
                }
                else
                {
                    hi = mid;
                }
            }

            return lo;
        }

        /// <summary>Функция нормального распределения (Абрамовиц — Стиган 7.1.26, погрешность 1.5·10⁻⁷).</summary>
        static double Phi(double x)
        {
            double z = Math.Abs(x) / Math.Sqrt(2.0);
            double t = 1.0 / (1.0 + 0.3275911 * z);
            double erf = 1.0 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t * Math.Exp(-z * z);
            return x >= 0.0 ? 0.5 * (1.0 + erf) : 0.5 * (1.0 - erf);
        }

        /// <summary>
        /// (`a274a`) ЗАМЕР, не разбор: разыгранные события альф ряда Ac-227 в кристалле сцены — для уточнения QF
        /// по альфам фона (пробе не надо пересобирать раздел на каждое QF: место события пересчитывает
        /// <see cref="AlphaPosition"/>). Вес — доля распада головы на событие (вес члена / историй).
        /// </summary>
        public static List<KeyValuePair<double, AlphaSample>> SampleSeriesAlphas(GeometryModel geometry, int histories, int seed,
                                                                                 List<string> notes)
        {
            var list = new List<KeyValuePair<double, AlphaSample>>();
            var members = ActiniumSeries(notes ?? new List<string>(), true);
            int index = 0;
            foreach (Member member in members)
            {
                List<AlphaLine> lines = AlphaLines(member.Nuclide);
                if (lines.Count == 0)
                {
                    index++;
                    continue;
                }

                var simulator = new EfficiencySimulator(geometry) { Seed = seed + 104729 * (index + 1) };
                index++;
                foreach (AlphaLine line in lines)
                {
                    if (notes != null && line.Cascade == null && line.Intensity >= 1.0)
                    {
                        notes.Add(Pretty(member.Nuclide) + " α " + line.EnergyKev.ToString("F1", CultureInfo.InvariantCulture)
                                  + ": " + line.NoCascade);
                    }

                    int n = (int)Math.Round(histories * line.Intensity / 100.0);
                    for (int i = 0; i < n; i++)
                    {
                        double x, y, z;
                        if (!simulator.TryCrystalPoint(out x, out y, out z))
                        {
                            break;
                        }

                        list.Add(new KeyValuePair<double, AlphaSample>(member.Weight / histories, AlphaEvent(simulator, line, x, y, z)));
                    }
                }
            }

            return list;
        }
    }
}
