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

        /// <summary>
        /// (`AMBER274` Э3, полоса `a274f` 14.11.2026) ФОРМА ГРУПП АЛЬФ — решение Amber 14.11.2026 вопросником,
        /// дословно: «Мерить форму по фону» (исключение из правила «Сбор света НЕ подгонять» её словом). Группы альф
        /// в данных шире гамма-пика той же шкалы и несут низкую полку; с гамма-шириной слой с альфами съедал
        /// линию 2614 кэВ проб тория (журнал `handover-2026-11-14-a274a.md` §7). Форма события альфы на шкале
        /// гистограммы (место p): доля <see cref="AlphaShelfShare"/> событий размазана равномерно по отрезку
        /// [p·(1 − <see cref="AlphaShelfLength"/>), p] («полка»), и всё вместе уширено гауссом с FWHM
        /// <see cref="AlphaExtraFwhmShare"/>·p — СВЕРХ приборного (приборное кладёт разбор, как у всех образов).
        /// Числа — замер по защищённым фонам прибора, хранит их прибор
        /// (<see cref="EfficiencyConfigData.AlphaGroupShape"/>), в коде чисел нет. Все нули — формы нет: событие
        /// альфы ложится в свой бин, как у полосы `a274a`.
        /// </summary>
        public double AlphaExtraFwhmShare;

        /// <summary>(`a274f`) Доля событий альфы в низкой полке (0…1).</summary>
        public double AlphaShelfShare;

        /// <summary>(`a274f`) Длина полки, доля места события.</summary>
        public double AlphaShelfLength;

        /// <summary>(`a274f`) У альф задана форма групп (иначе событие — в свой бин).</summary>
        public bool HasAlphaShape
        {
            get
            {
                return this.HasAlpha && (this.AlphaExtraFwhmShare > 0.0 || (this.AlphaShelfShare > 0.0 && this.AlphaShelfLength > 0.0));
            }
        }
    }

    /// <summary>
    /// (`AMBER274` Э3, полоса `a274f` 14.11.2026) АКТИВНОСТЬ РЯДА КРИСТАЛЛА ИЗ ПОЛЯ ПРИБОРА — запасной путь счёта ряда
    /// по альфам. Решение Amber 14.11.2026 вопросником, дословно: «По фону с распадом (Рекомендую)»: у прибора
    /// записаны активность головы ряда A₀ и её дата (<see cref="EfficiencyConfigData.CrystalSeriesActivity"/>, по
    /// счёту альф фонов); когда счёт по альфам ЭТОГО спектра не годен (в окне альф линии пробы), ряд берётся из
    /// поля с распадом головы: A = A₀·2^(−Δt/T½), T½ — из `nucdb`. Кладёт привязка
    /// (<see cref="FsaMatrixBinding.BindDevice"/>), дату
    /// спектра — <see cref="FsaAnalyzer.AdoptSpectrum"/>.
    /// </summary>
    public sealed class CrystalOwnSeriesFallback
    {
        /// <summary>Активность головы ряда на дату <see cref="Date"/>, Бк.</summary>
        public double ActivityBq;

        /// <summary>Дата, на которую задана активность.</summary>
        public DateTime Date;

        /// <summary>Начало набора спектра; <see cref="ResultData.UnknownStartTime"/> — неизвестно (поправки на распад нет — путь закрыт).</summary>
        public DateTime SpectrumStart = ResultData.UnknownStartTime;

        /// <summary>
        /// Активность головы ряда <paramref name="nucid"/> на начало набора спектра, Бк; 0 и причина — поля нет,
        /// дата спектра неизвестна или периода нет в базе.
        /// </summary>
        public double ActivityAtSpectrum(string nucid, out string why)
        {
            why = null;
            if (!(this.ActivityBq > 0.0))
            {
                why = "у прибора нет активности ряда";
                return 0.0;
            }

            if (this.SpectrumStart == ResultData.UnknownStartTime || this.SpectrumStart.Year < 1990)
            {
                why = "дата набора спектра неизвестна — поправку на распад не взять";
                return 0.0;
            }

            double halfLife = CrystalOwnActivityBuilder.HalfLifeSeconds(nucid);
            if (!(halfLife > 0.0))
            {
                why = "в nucdb нет периода " + FsaSampleLibrary.PrettyName(nucid);
                return 0.0;
            }

            double seconds = (this.SpectrumStart - this.Date).TotalSeconds;
            return this.ActivityBq * Math.Pow(2.0, -seconds / halfLife);
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

                // (`a274f`) форма групп альф — только когда задана: клеймо альф без формы прежнее
                if (p.HasAlphaShape)
                {
                    sb.Append(":aw").Append(p.AlphaExtraFwhmShare.ToString("R", CultureInfo.InvariantCulture))
                      .Append(":af").Append(p.AlphaShelfShare.ToString("R", CultureInfo.InvariantCulture))
                      .Append(":al").Append(p.AlphaShelfLength.ToString("R", CultureInfo.InvariantCulture));
                }
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
                            AlphaExtraFwhmShare = parts.AlphaExtraFwhmShare,                // (`a274f`) форма групп альф
                            AlphaShelfShare = parts.AlphaShelfShare,
                            AlphaShelfLength = parts.AlphaShelfLength,
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

        /// <summary>
        /// (`AMBER274`, предложение `a274n` (в), полоса `a274m` 14.11.2026) ОТПЕЧАТОК ДАННЫХ РАСПАДА СЛОЯ — в клеймо
        /// раздела (поле `ndb=`): SHA-256 (16 знаков) строк `nuclides`, `decay_radiations` и `l_decays` базы `nucdb` у
        /// нуклидов, которые слой этой сцены читает (природные по составу кристалла; у лантановых и цериевых — члены
        /// ряда Ac-227, бета и альфа). Без него правка `nucdb` (выход ветви, период, распространённость) оставляла бы
        /// посчитанный раздел «годным». Базы нет — `none`. Кэш по файлу базы (путь, длина, время) и набору нуклидов.
        /// ⚠ Не покрыты: формы бет (`beta_shapes`), схемы уровней дочек (`schemedb`), атомные данные (`matdb`).
        /// </summary>
        public static string DecayDataFingerprint(GeometryModel geometry)
        {
            return DecayDataFingerprint(geometry, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nucdb.sqlite"));
        }

        /// <summary>То же для явного файла базы (проба положительного контроля).</summary>
        public static string DecayDataFingerprint(GeometryModel geometry, string path)
        {
            var nuclides = new List<string>(NaturalNuclides(geometry));
            if (HasActiniumSeries(geometry))
            {
                nuclides.AddRange(ActiniumSeriesNuclides);
            }

            var info = new FileInfo(path);
            if (!info.Exists || nuclides.Count == 0)
            {
                return "none";
            }

            string key = info.FullName + "|" + info.Length.ToString(CultureInfo.InvariantCulture) + "|"
                         + info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture) + "|" + string.Join(",", nuclides.ToArray());
            lock (DecayFingerprintGate)
            {
                string cached;
                if (DecayFingerprintCache.TryGetValue(key, out cached))
                {
                    return cached;
                }
            }

            string result;
            try
            {
                var text = new StringBuilder();
                using (var connection = new SqliteConnection("Data Source=" + info.FullName + ";Mode=ReadOnly"))
                {
                    connection.Open();
                    foreach (string nucid in nuclides)
                    {
                        foreach (string query in new[]
                        {
                            "select * from nuclides where nucid = $n order by rowid",
                            "select * from decay_radiations where parent_nucid = $n order by rowid",
                            "select * from l_decays where nucid = $n order by rowid",
                        })
                        {
                            text.Append('#').Append(nucid).Append('\n');
                            using (SqliteCommand command = connection.CreateCommand())
                            {
                                command.CommandText = query;
                                command.Parameters.AddWithValue("$n", nucid);
                                using (SqliteDataReader reader = command.ExecuteReader())
                                {
                                    while (reader.Read())
                                    {
                                        for (int i = 0; i < reader.FieldCount; i++)
                                        {
                                            text.Append(reader.IsDBNull(i) ? "" : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture))
                                                .Append('\t');
                                        }

                                        text.Append('\n');
                                    }
                                }
                            }
                        }
                    }
                }

                using (var sha = System.Security.Cryptography.SHA256.Create())
                {
                    byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()));
                    var hex = new StringBuilder();
                    for (int i = 0; i < 8; i++)
                    {
                        hex.Append(digest[i].ToString("x2", CultureInfo.InvariantCulture));
                    }

                    result = hex.ToString();
                }
            }
            catch (Exception)
            {
                // база не читается — раздел по ней не собрать и не сверить; «unread» с годным клеймом не совпадёт
                return "unread";
            }

            lock (DecayFingerprintGate)
            {
                DecayFingerprintCache[key] = result;
            }

            return result;
        }

        /// <summary>Члены ряда Ac-227, которых читает слой (бета-члены и чистые альфа-излучатели) — для отпечатка данных.</summary>
        static readonly string[] ActiniumSeriesNuclides = { "227AC", "211PB", "211BI", "207TL", "227TH", "223RA", "219RN", "215PO" };

        static readonly object DecayFingerprintGate = new object();
        static readonly Dictionary<string, string> DecayFingerprintCache = new Dictionary<string, string>(StringComparer.Ordinal);

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

                // (`a274f`) с формой групп события альф сперва копятся отдельно (место без формы), форма кладётся
                // на всю их гистограмму разом; без формы — прежний счёт, событие в свой бин
                bool shaped = parts.HasAlphaShape;
                double[] bare = shaped ? new double[bins] : null;
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
                        if (shaped)
                        {
                            Add(bare, position, binKev, scale);
                        }
                        else
                        {
                            Add(entry.PerDecay, position, binKev, scale);
                            Add(entry.AlphaPerDecay, position, binKev, scale);
                        }
                    }
                }

                if (shaped)
                {
                    entry.AlphaPerDecay = ShapeAlphaGroups(bare, binKev, parts);
                    for (int i = 0; i < bins; i++)
                    {
                        entry.PerDecay[i] += entry.AlphaPerDecay[i];
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

                // (`a274n`, внесено полосой `a274m` 14.11.2026) Σ выходов α строк базы — не доля α-моды. В базе дерева
                // `5db1d5e0`: Ra-223 97.8 %, Rn-219 99.8 %, Th-227 99.156 % при моде 100 %, Ac-227 1.413 % при моде
                // 1.38 %; база полосы `a274n` (правка по LNHB, ещё не перенесена) даст 100.82 / 100.03 / 1.422. Слою нужна
                // доля моды на распад нуклида (`l_decays`, род 0, уровень 0) — строки нормируются на неё; моды нет —
                // как в базе. Зовётся только при альфах (QF > 0): при выключенных альфах ни бита.
                double mode = 0.0;
                using (SqliteCommand command = connection.CreateCommand())
                {
                    command.CommandText = "select perc_num from l_decays where nucid = $n and l_seqno = 0 and dec_type = 0";
                    command.Parameters.AddWithValue("$n", nucid);
                    object v = command.ExecuteScalar();
                    if (v != null && !(v is DBNull))
                    {
                        mode = Convert.ToDouble(v, CultureInfo.InvariantCulture);
                    }
                }

                double rowSum = 0.0;
                foreach (double[] r in rows)
                {
                    rowSum += r[1];
                }

                if (mode > 0.0 && rowSum > 0.0)
                {
                    foreach (double[] r in rows)
                    {
                        r[1] *= mode / rowSum;
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
        /// (`AMBER274` Э3, полоса `a274f` 14.11.2026) ФОРМА ГРУПП АЛЬФ на гистограмме событий альф
        /// (<paramref name="bare"/>: бин k — место k·шаг, событие без формы). Сперва полка: доля
        /// <see cref="CrystalOwnParts.AlphaShelfShare"/> содержимого бина ложится равномерно на отрезок
        /// [p·(1 − <see cref="CrystalOwnParts.AlphaShelfLength"/>), p]; затем гаусс с FWHM
        /// <see cref="CrystalOwnParts.AlphaExtraFwhmShare"/>·p (доли бинов — разность функции ошибок). Сумма
        /// гистограммы сохраняется: то, что гаусс вынес бы за края сетки, остаётся внутри (перенормировка ядра).
        /// Возвращает новую гистограмму; формы нет — копию.
        /// </summary>
        public static double[] ShapeAlphaGroups(double[] bare, double binKev, CrystalOwnParts parts)
        {
            int bins = bare.Length;
            var shelf = new double[bins];
            double share = parts != null ? Math.Min(1.0, Math.Max(0.0, parts.AlphaShelfShare)) : 0.0;
            double length = parts != null ? Math.Min(0.999, Math.Max(0.0, parts.AlphaShelfLength)) : 0.0;
            double extra = parts != null ? Math.Max(0.0, parts.AlphaExtraFwhmShare) : 0.0;
            for (int k = 0; k < bins; k++)
            {
                double v = bare[k];
                if (v == 0.0)
                {
                    continue;
                }

                double p = k * binKev;
                double lo = p * (1.0 - length);
                if (!(share > 0.0) || !(p - lo > 1e-9 * binKev))
                {
                    shelf[k] += v;
                    continue;
                }

                shelf[k] += (1.0 - share) * v;
                double perKev = share * v / (p - lo);
                int first = Math.Max(0, (int)Math.Floor(lo / binKev + 0.5));
                for (int j = first; j <= k; j++)
                {
                    double a = Math.Max(lo, (j - 0.5) * binKev), b = Math.Min(p, (j + 0.5) * binKev);
                    if (b > a)
                    {
                        shelf[j] += perKev * (b - a);
                    }
                }
            }

            if (!(extra > 0.0))
            {
                return shelf;
            }

            var result = new double[bins];
            var weights = new List<double>();
            for (int k = 0; k < bins; k++)
            {
                double v = shelf[k];
                if (v == 0.0)
                {
                    continue;
                }

                double p = k * binKev;
                double sigma = extra * p / 2.3548200450309493;
                if (!(sigma > 0.05 * binKev))
                {
                    result[k] += v;
                    continue;
                }

                int j0 = Math.Max(0, (int)Math.Floor((p - 5.0 * sigma) / binKev + 0.5));
                int j1 = Math.Min(bins - 1, (int)Math.Floor((p + 5.0 * sigma) / binKev + 0.5));
                weights.Clear();
                double sum = 0.0;
                for (int j = j0; j <= j1; j++)
                {
                    double w = Phi(((j + 0.5) * binKev - p) / sigma) - Phi(((j - 0.5) * binKev - p) / sigma);
                    weights.Add(w);
                    sum += w;
                }

                if (!(sum > 0.0))
                {
                    result[k] += v;
                    continue;
                }

                for (int j = j0; j <= j1; j++)
                {
                    result[j] += v * weights[j - j0] / sum;
                }
            }

            return result;
        }

        /// <summary>(`a274f`) Период полураспада нуклида по `nucdb` (`nuclides.half_life_sec`, основное состояние), с; 0 — нет.</summary>
        public static double HalfLifeSeconds(string nucid)
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nucdb.sqlite");
            if (string.IsNullOrEmpty(nucid) || !File.Exists(path))
            {
                return 0.0;
            }

            try
            {
                using (var connection = new SqliteConnection("Data Source=" + path + ";Mode=ReadOnly"))
                {
                    connection.Open();
                    using (SqliteCommand command = connection.CreateCommand())
                    {
                        command.CommandText = "select half_life_sec from nuclides where nucid = $n order by l_seqno limit 1";
                        command.Parameters.AddWithValue("$n", nucid);
                        using (SqliteDataReader reader = command.ExecuteReader())
                        {
                            return reader.Read() ? Number(reader, 0) : 0.0;
                        }
                    }
                }
            }
            catch (Exception)
            {
                return 0.0;
            }
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
            /// Счёт годен: подложка в окне не больше <see cref="AlphaCountMaxBaselineShare"/> отсчётов окна (больше —
            /// под окном альф лежит гамма-континуум пробы, а он не постоянен: падает ступенями к верху окна, и
            /// постоянная подложка, взятая с его уровня ниже окна, вычитает лишнее), в окне нет узкой линии
            /// (<see cref="LineFound"/>), группы альф не уходят за верх шкалы (<see cref="Overflow"/>) И половины окна
            /// дают одну активность (<see cref="SplitFound"/>, `a274v`).
            /// </summary>
            public bool Reliable;

            /// <summary>
            /// (`a274z`) ГРУППЫ АЛЬФ УХОДЯТ ЗА ВЕРХ ШКАЛЫ: окно счёта упирается в верх шкалы, а последний канал спектра
            /// (у АЦП он копит всё, что выше шкалы; в сам счёт он не входит) держит больше
            /// <see cref="AlphaOverflowMaxShare"/> счёта окна. Счёт
            /// тогда зависит от того, где шкала режет верхнюю группу, то есть от шкалы у самого верха, а её там
            /// привязать нечем.
            /// </summary>
            public bool Overflow;

            /// <summary>
            /// (`a274z`) Ушедшее за шкалу в долях счёта окна: отсчёты последнего канала спектра (если окно упирается в
            /// верх шкалы) и избыток каналов-выбросов (<see cref="HotChannels"/>). В сам счёт ни то, ни другое не входит.
            /// </summary>
            public double OverflowShare;

            /// <summary>(`a274z`) Каналов-выбросов в области счёта: одиночные каналы, где осело переполнение АЦП; в счёт вместо них идёт медиана соседей.</summary>
            public int HotChannels;

            /// <summary>
            /// (`a274f`) В окне альф или полосе подложки стоит УЗКАЯ ЛИНИЯ — гамма-линия пробы: остаток «данные −
            /// образ ряда − подложка» в полосе шириной с приборный пик выше своих боков значимее
            /// <see cref="AlphaLineMinZ"/> и больше <see cref="AlphaLineMinShare"/> ЧИСТОГО СЧЁТА ОКНА — то есть линия
            /// сама сдвигает активность ряда больше этой доли (а её комптон в окне — ещё на столько же и больше).
            /// Группы альф шире приборного пика (<see cref="CrystalOwnParts.AlphaExtraFwhmShare"/>), поэтому гладкая
            /// невязка формы групп даёт признак слабее. Имён нуклидов признак не знает.
            /// </summary>
            public bool LineFound;

            /// <summary>(`a274f`) Самая сильная узкая особенность остатка: место (кэВ-экв), значимость и доля от чистого счёта окна.</summary>
            public double LineKev, LineZ, LineShare;

            /// <summary>
            /// (`a274v`, замечание критика `crit274z`) ПОЛОВИНЫ ОКНА НЕ СОГЛАСНЫ: активность по верхней и нижней половине
            /// окна (граница — <see cref="SplitKev"/>, там, где образ ряда реже всего) с общей подложкой расходится
            /// больше <see cref="AlphaSplitMaxShare"/> активности окна при значимости не меньше <see cref="AlphaLineMinZ"/>.
            /// В окне лежит добавка, которой нет в образе ряда, — счёт её считает альфами.
            /// </summary>
            public bool SplitFound;

            /// <summary>(`a274v`) Граница половин окна (кэВ-экв), их расхождение (верхняя − нижняя) в долях активности окна и его значимость.</summary>
            public double SplitKev, SplitShare, SplitZ;

            public string Note;
        }

        /// <summary>(`a274a`) Значимость счёта альф, с которой ряд считается найденным.</summary>
        public const double AlphaCountMinZ = 3.0;

        /// <summary>
        /// (`a274a`; порог пересчитан полосой `a274z` 15.11.2026) Наибольшая доля подложки в окне альф, при которой
        /// счёт годен. Прежние 10 % мерены на образе альф БЕЗ формы групп (журнал `handover-2026-11-14-a274a.md`:
        /// фоны 4.7…7.0 %, пробы без линий выше 1.7 МэВ 4.6…8.7 %): провалы между группами тот счёт относил к
        /// подложке. С формой групп (`a274f`) подложка фонов — 0.8…1.6 % окна, годных проб — до 2.3 %, а порог
        /// остался прежним и пропускал пробы с Th/U: у них подложка 3.7…6.6 %, а счёт ниже счёта фона той же поры на
        /// 0.9…3.3 %. Механизм измерен (журнал `handover-2026-11-15-a274z.md`): избыток пробы над фоном на кэВ под
        /// окном альф не постоянен — у низа окна он тот же, что в полосе подложки (0.9…1.5 её уровня), выше падает
        /// до 0.1…0.5 и к верху окна сходит в нуль: континуум линий пробы кончается у их комптоновских краёв. В
        /// целом по окну избыток — 0.3…0.7 того, что вычитает постоянная подложка с уровня полосы, и счёт занижен на
        /// 0.4…0.75 прироста подложки над фоновой; опыт с подложенным континуумом это воспроизводит
        /// (`CrystalOwnBaselineProbeA274z`). Исправить это в счёте нельзя: форму континуума задаёт состав пробы,
        /// которого счёт не знает (его знает подгонка разбора — у негодного счёта ряд берётся из поля прибора с
        /// распадом, а без поля строка остаётся свободной и делит амплитуду с образами пробы).
        /// ПОРОГ 2 % — решение Amber 15.11.2026 вопросником, дословно: «Снизить до 2 % (Рекомендую)» (был 2.5 %:
        /// между 2.30 % годной пробы и 3.70 % пробы с торием). ⚠ Что порог 2 % пропускает — годный счёт со смещением,
        /// ЗАМЕР опытом с подложенной ступенью (`CrystalOwnBaselineProbeA274z`, шаг ступени 0.1 % счёта окна): на
        /// десяти годных фонах (подложка 1.32…1.66 %) — до −0.34 % при обрыве у края линии 2614 кэВ и до −0.51 % у
        /// края линии 2204 кэВ (ряд урана; критик `crit274z`); на спектре с наименьшей подложкой (0.64 %) — до
        /// −0.70 % и до −1.01 %: сдвиг идёт за приростом подложки (0.54…0.56 и 0.80…0.81 прироста), а запас до
        /// порога у такого спектра вдвое больше. Разброс защищённых фонов — 0.19 %.
        /// </summary>
        public const double AlphaCountMaxBaselineShare = 0.02;

        /// <summary>
        /// (`a274z`, 15.11.2026) Наибольшая доля последнего канала спектра в счёте окна альф, при которой счёт годен.
        /// Замер на втором приборе LaBr3 (журнал `handover-2026-11-15-a274z.md`): у 29 спектров из 34 последний канал
        /// с каналами-выбросами держит не больше 1.5 % счёта окна, у пяти спектров одной поры — 3.7…7.7 %, и без
        /// ушедшего за шкалу их счёт ниже счёта фонов на 1.3…3.7 % (образ ряда отводит выше их шкалы 0.5…2.8 % альф:
        /// шкала у верха не та). Сам последний канал и выбросы в счёт не входят — они только мерило.
        /// </summary>
        public const double AlphaOverflowMaxShare = 0.03;

        /// <summary>
        /// (`a274z`) СКОЛЬКО ВЕРХНИХ КАНАЛОВ ШКАЛЫ СНЯТЬ ИЗ СЧЁТА АЛЬФ — выбор одной строкой. 1 — последний канал:
        /// решения Amber 15.11.2026 вопросником, дословно: «Исключить (Рекомендую)» и, на переспрос критика
        /// `crit274z` (один канал или девять), — «Последний канал (Рекомендую)». 0 — как было до решения (последний
        /// канал в окне); 9 — отвергнутый вариант (у одного фона второго прибора переполнение осело в канале за
        /// восемь до последнего). То же переполнение, осевшее ниже последнего канала, снимают каналы-выбросы
        /// (<see cref="AlphaHotChannelRatio"/>; 0 — не снимать) — её слово того же дня: «Оставить фильтр (Рекомендую)».
        /// </summary>
        public const int AlphaTopChannelsOut = 1;

        /// <summary>(`a274z`) Канал-выброс области счёта альф: отсчётов больше стольких медиан соседей (0 — выбросы не снимаются).</summary>
        public const double AlphaHotChannelRatio = 5.0;

        /// <summary>(`a274z`) Соседей с каждой стороны, по которым берётся медиана для канала-выброса.</summary>
        public const int AlphaHotChannelReach = 15;

        /// <summary>(`a274a`) Ширина полосы подложки ниже окна альф, кэВ-экв.</summary>
        public const double AlphaSideBandKev = 300.0;

        /// <summary>
        /// (`a274f`) Значимость узкой особенности остатка, с которой она считается линией пробы в окне альф.
        /// Замер на втором приборе LaBr3 (журнал `handover-2026-11-14-a274f.md`): фоны и пробы без линий выше
        /// 1.7 МэВ против проб с Th/U/Ra — там же числа.
        /// </summary>
        public const double AlphaLineMinZ = 5.0;

        /// <summary>
        /// (`a274f`; порог пересчитан полосой `a274v` 15.11.2026) Наименьшая доля узкой особенности от чистого счёта окна
        /// альф (см. <see cref="AlphaLineMinZ"/>). Было 4 %, и линия приборной ширины площадью 3 % счёта окна (критик
        /// `crit274z`: счёт сдвинут на +3.1 %) находилась долей 2.2…2.9 % и проходила годной. Замер на 38 спектрах
        /// второго прибора LaBr3 (журнал `handover-2026-11-15-a274v.md`): у годного счёта особенность не больше 1.68 %
        /// (KCl, наложения; счёт при этом в −0.08 % от поля прибора), у фонов — 0.31…0.74 %. Порог 2 % — между ними.
        /// Ложное срабатывание дёшево: негодный счёт берёт ряд из поля прибора с распадом.
        /// </summary>
        public const double AlphaLineMinShare = 0.02;

        /// <summary>
        /// (`a274v`, замечание критика `crit274z`) Наибольшее расхождение активности половин окна альф в долях активности
        /// окна, при котором счёт годен (<see cref="AlphaCount.SplitFound"/>). Замер на 38 спектрах второго прибора LaBr3
        /// (журнал `handover-2026-11-15-a274v.md`): у годного счёта половины расходятся на −5.8…+1.8 % (у всех, кроме
        /// KCl, верхняя ниже нижней на 1.5…5.8 % — форма образа у верха окна не та; на счёт это не влияет), ступень
        /// +4 медианы от середины окна до верха шкалы — +112 %. Порог 8 % — с запасом над годными.
        /// </summary>
        public const double AlphaSplitMaxShare = 0.08;

        /// <summary>(`a274v`) Граница половин окна ищется там, где набралось от <see cref="AlphaSplitLo"/> до <see cref="AlphaSplitHi"/> образа окна.</summary>
        public const double AlphaSplitLo = 0.3, AlphaSplitHi = 0.7;

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

            // (`a274z`, второй круг) ПОСЛЕДНИЙ КАНАЛ СПЕКТРА В СЧЁТ НЕ ВХОДИТ — решение Amber 15.11.2026 вопросником,
            // дословно: «Исключить (Рекомендую)». У АЦП он копит всё, что выше шкалы (наложения, космика, срезанные
            // альфы): в образе ряда этого нет, а в окне счёта, упиравшемся в верх шкалы, он давал ≈ 1.1 % счёта.
            // Верх шкалы для счёта — нижняя граница снятых каналов: они выпадают из окна, из подгонки подложки,
            // из χ² и из поиска узкой линии. Признак <see cref="AlphaCount.Overflow"/> на них по-прежнему смотрит.
            // Сколько каналов снять — одна константа <see cref="AlphaTopChannelsOut"/>.
            int topOut = Math.Max(0, Math.Min(AlphaTopChannelsOut, channels - 16));
            double spectrumTop = edge[channels - topOut];
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

            // (`a274z`, второй круг) КАНАЛЫ-ВЫБРОСЫ. У спектра, сложенного из наборов с разной шкалой, переполнение
            // АЦП оседает не только в последнем канале, но и в одиночных каналах ниже него (замер: у трёх спектров
            // второго прибора 1, 2 и 4 таких канала с 0.8…3.6 % счёта окна при 15…7000 отсчётах в соседних). Одиночный
            // канал — не черта спектра: приборный пик здесь шире сотни каналов. Выброс — канал области счёта, где
            // отсчётов больше пяти медиан 30 соседей (и больше медианы на десять её корней). В счёт идёт медиана
            // соседей, избыток над ней — в то же мерило, что и последний канал (AlphaCount.OverflowShare).
            double[] data = (double[])counts.Clone();
            double hotExcess = 0.0;
            {
                double regionLo = noSide ? result.WindowLoKev : result.SideLoKev;
                var near = new List<double>(2 * AlphaHotChannelReach);
                for (int i = 0; i < channels - Math.Max(topOut, 1) && AlphaHotChannelRatio > 0.0; i++)
                {
                    double centre = 0.5 * (edge[i] + edge[i + 1]);
                    if (centre < regionLo || centre >= result.WindowHiKev)
                    {
                        continue;
                    }

                    near.Clear();
                    for (int j = Math.Max(0, i - AlphaHotChannelReach); j <= Math.Min(channels - 1 - Math.Max(topOut, 1), i + AlphaHotChannelReach); j++)
                    {
                        if (j != i)
                        {
                            near.Add(counts[j]);
                        }
                    }

                    if (near.Count < 4)
                    {
                        continue;
                    }

                    near.Sort();
                    double median = 0.5 * (near[(near.Count - 1) / 2] + near[near.Count / 2]);
                    if (counts[i] > AlphaHotChannelRatio * Math.Max(median, 5.0) && counts[i] > median + 10.0 * Math.Sqrt(Math.Max(median, 1.0)))
                    {
                        hotExcess += counts[i] - median;
                        data[i] = median;
                        result.HotChannels++;
                    }
                }
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

                double y = data[i] - (known != null ? known[i] * liveTime : 0.0);
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
                    double r = data[i] - (known != null ? known[i] * liveTime : 0.0) - aFit * template[i] * liveTime - bFit;
                    chi += r * r / Math.Max(data[i], 1.0);
                }
            }

            result.ChiPerNdf = used > 2 ? chi / (used - (noSide ? 1 : 2)) : 0.0;

            // (`a274f`) УЗКАЯ ЛИНИЯ в окне и полосе подложки: остаток в полосе ±0.6 приборной FWHM против своих
            // боков (0.6…1.8 FWHM с каждой стороны) — сумма с нулевой площадью ядра, гладкую невязку она гасит.
            {
                double scanLo = noSide ? result.WindowLoKev : result.SideLoKev;
                var centre = new double[channels];
                var cumR = new double[channels + 1];
                var cumY = new double[channels + 1];
                for (int i = 0; i < channels; i++)
                {
                    centre[i] = 0.5 * (edge[i] + edge[i + 1]);
                    double model = (known != null ? known[i] * liveTime : 0.0) + aFit * template[i] * liveTime + bFit;
                    cumR[i + 1] = cumR[i] + data[i] - model;
                    cumY[i + 1] = cumY[i] + Math.Max(data[i], 0.0);
                }

                double bestScore = 0.0;
                double netWindow = yWin - Math.Max(0.0, bFit) * nWin;
                for (int i = 0; i < channels && netWindow > 0.0; i++)
                {
                    double e = centre[i];
                    if (e < scanLo || e >= result.WindowHiKev)
                    {
                        continue;
                    }

                    double width = Math.Max(fwhmKevAt(e), 0.0);
                    if (!(width > 0.0))
                    {
                        continue;
                    }

                    // ядро целиком внутри области подгонки: за её краями образа нет, и остаток там — не невязка;
                    // у края шкалы ядро вышло бы усечённым с одной стороны и приняло бы склон за линию
                    if (e - 1.8 * width < scanLo || e + 1.8 * width > result.WindowHiKev)
                    {
                        continue;
                    }

                    int c0 = Lower(edge, e - 0.6 * width), c1 = Lower(edge, e + 0.6 * width) + 1;
                    int f0 = Lower(edge, e - 1.8 * width), f1 = Lower(edge, e + 1.8 * width) + 1;
                    if (f0 < 0 || f1 > channels || c1 <= c0 || f1 - f0 <= c1 - c0)
                    {
                        continue;
                    }

                    double nC = c1 - c0, nS = (f1 - f0) - nC;
                    double rC = cumR[c1] - cumR[c0], rS = (cumR[f1] - cumR[f0]) - rC;
                    double yC = cumY[c1] - cumY[c0], yS = (cumY[f1] - cumY[f0]) - yC;
                    double ratio = nC / nS;
                    double excess = rC - ratio * rS;
                    double sigma = Math.Sqrt(Math.Max(yC + ratio * ratio * yS, 1.0));
                    double z = excess / sigma;

                    // доля — от чистого счёта окна: на столько линия сама сдвигает активность ряда. Доля от местного
                    // образа не годится: на склонах групп и у низа окна образ мал, и любая невязка даёт десятки процентов
                    double lineShare = excess / netWindow;
                    if (z >= AlphaLineMinZ && lineShare > bestScore)
                    {
                        bestScore = lineShare;
                        result.LineKev = e;
                        result.LineZ = z;
                        result.LineShare = lineShare;
                    }
                }

                result.LineFound = result.LineZ >= AlphaLineMinZ && result.LineShare >= AlphaLineMinShare;
            }

            // (`a274v`, замечание критика `crit274z`) СОГЛАСИЕ ПОЛОВИН ОКНА. Счёт — не форма: сдвиг групп внутри окна
            // его не меняет. Но и признаки годности формы не судили — гладкая добавка, похожая на образ не больше, чем
            // ступень, проходила годной (ступень +4 медианы от середины окна до верха шкалы давала счёт в 3.4 раза
            // больше при подложке 0.14 %). Окно делится надвое там, где образ ряда реже всего (наименьшая плотность
            // образа на кэВ в средней части окна, от 30 до 70 % его счёта: сдвиг групп через границу там меньше всего
            // переносит), и активность считается по каждой половине с той же подложкой. Верный счёт даёт одну
            // активность в обеих; добавка, которой нет в образе, — разную.
            {
                var winIdx = new List<int>();
                double tAll = 0.0;
                for (int i = 0; i < channels; i++)
                {
                    double centre = 0.5 * (edge[i] + edge[i + 1]);
                    if (centre >= result.WindowLoKev && centre < result.WindowHiKev)
                    {
                        winIdx.Add(i);
                        tAll += template[i];
                    }
                }

                int split = -1;
                double run2 = 0.0, best = double.MaxValue;
                foreach (int i in winIdx)
                {
                    double before = run2;
                    run2 += template[i];
                    if (!(tAll > 0.0) || before < AlphaSplitLo * tAll || run2 > AlphaSplitHi * tAll)
                    {
                        continue;
                    }

                    double density = template[i] / (edge[i + 1] - edge[i]);
                    if (density < best)
                    {
                        best = density;
                        split = i;
                    }
                }

                if (split >= 0)
                {
                    double yLo = 0.0, tLo = 0.0, nLo = 0.0, yHi = 0.0, tHi = 0.0, nHi = 0.0;
                    foreach (int i in winIdx)
                    {
                        double y = data[i] - (known != null ? known[i] * liveTime : 0.0);
                        double tt = template[i] * liveTime;
                        if (i < split)
                        {
                            yLo += y;
                            tLo += tt;
                            nLo += 1.0;
                        }
                        else
                        {
                            yHi += y;
                            tHi += tt;
                            nHi += 1.0;
                        }
                    }

                    double aAll = (yWin - Math.Max(0.0, bFit) * nWin) / tWin;
                    if (tLo > 0.0 && tHi > 0.0 && aAll > 0.0)
                    {
                        double b = Math.Max(0.0, bFit);
                        double aLo = (yLo - b * nLo) / tLo, aHi = (yHi - b * nHi) / tHi;
                        double lever = nLo / tLo - nHi / tHi;
                        double sigma = Math.Sqrt(Math.Max(yLo, 1.0) / (tLo * tLo) + Math.Max(yHi, 1.0) / (tHi * tHi) + varB * lever * lever);
                        result.SplitKev = edge[split];
                        result.SplitShare = (aHi - aLo) / aAll;
                        result.SplitZ = sigma > 0.0 ? (aHi - aLo) / sigma : 0.0;
                        result.SplitFound = Math.Abs(result.SplitZ) >= AlphaLineMinZ && Math.Abs(result.SplitShare) >= AlphaSplitMaxShare;
                    }
                }
            }

            result.Counts = yWin;
            result.Baseline = Math.Max(0.0, bFit) * nWin;
            result.TemplatePerBqS = tWin / liveTime;
            double net = yWin - result.Baseline;
            result.ActivityBq = Math.Max(0.0, net / tWin);
            result.SigmaBq = Math.Sqrt(Math.Max(yWin, 1.0) + varB * nWin * nWin) / tWin;
            result.Z = result.SigmaBq > 0.0 ? result.ActivityBq / result.SigmaBq : 0.0;
            result.Found = result.Z >= AlphaCountMinZ;

            // (`a274z`) Окно упирается в верх шкалы — сразу за ним стоит последний канал (переполнение АЦП). В счёт он
            // не входит (см. выше), но мерит, сколько событий ушло за шкалу: его отсчёты и избыток каналов-выбросов
            // в долях счёта окна.
            if (yWin > 0.0)
            {
                // мерило — верхние каналы шкалы (снятые из счёта; не снято ни одного — последний) и избыток выбросов
                double beyond = hotExcess;
                for (int i = channels - Math.Max(topOut, 1); i < channels && result.WindowHiKev >= spectrumTop; i++)
                {
                    beyond += Math.Max(0.0, counts[i]);
                }

                result.OverflowShare = beyond / yWin;
                result.Overflow = result.OverflowShare > AlphaOverflowMaxShare;
            }

            result.Reliable = result.Found && yWin > 0.0 && result.Baseline <= AlphaCountMaxBaselineShare * yWin && !result.LineFound
                              && !result.Overflow && !result.SplitFound;
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

            if (result.LineZ >= AlphaLineMinZ)
            {
                note.Append(result.LineFound ? "; ЛИНИЯ в окне альф у " : "; узкая особенность у ")
                    .Append(result.LineKev.ToString("F0", CultureInfo.InvariantCulture)).Append(" кэВ-экв (")
                    .Append((100.0 * result.LineShare).ToString("F2", CultureInfo.InvariantCulture)).Append(" % счёта окна, z ")
                    .Append(result.LineZ.ToString("F1", CultureInfo.InvariantCulture)).Append(')');
            }

            if (result.Overflow)
            {
                note.Append("; ГРУППЫ АЛЬФ ЗА ВЕРХОМ ШКАЛЫ: последний канал и каналы-выбросы — ")
                    .Append((100.0 * result.OverflowShare).ToString("F2", CultureInfo.InvariantCulture)).Append(" % счёта окна");
            }

            if (result.SplitFound)
            {
                note.Append("; ПОЛОВИНЫ ОКНА НЕ СОГЛАСНЫ (граница ").Append(result.SplitKev.ToString("F0", CultureInfo.InvariantCulture))
                    .Append(" кэВ-экв): верхняя − нижняя ").Append((100.0 * result.SplitShare).ToString("F1", CultureInfo.InvariantCulture))
                    .Append(" % активности окна, z ").Append(result.SplitZ.ToString("F1", CultureInfo.InvariantCulture));
            }

            if (result.HotChannels > 0)
            {
                note.Append("; каналов-выбросов снято ").Append(result.HotChannels.ToString(CultureInfo.InvariantCulture));
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
            return FixSeriesByAlphas(section, raw, liveTime, calibration, fwhmCalibration, counted, null, out note);
        }

        /// <summary>
        /// (`AMBER274` Э3, полоса `a274f` 14.11.2026) То же с ЗАПАСНЫМ ПУТЁМ <paramref name="fallback"/> — решение
        /// Amber 14.11.2026 вопросником, дословно: «По фону с распадом (Рекомендую)»: счёт по альфам этого спектра не
        /// годен (подложка окна, узкая линия в окне) или невозможен (окно альф вне шкалы, ряд по счёту не найден) —
        /// ряд берётся из поля прибора с распадом головы (<see cref="CrystalOwnSeriesFallback.ActivityAtSpectrum"/>)
        /// и вычитается известной частью. Негодность счёта — `AlphaCount.Reliable`: подложка окна больше порога
        /// (под окном гамма-континуум пробы, `a274z`), узкая линия в окне, группы альф за верхом шкалы (`a274z`),
        /// половины окна не согласны (`a274v`).
        /// Поля нет (null, активность 0, дата спектра неизвестна) — как прежде:
        /// негодный счёт оставляет строку свободной, не найденный ряд её снимает.
        /// </summary>
        public static ResponseCrystalSection FixSeriesByAlphas(ResponseCrystalSection section, int[] raw, double liveTime,
                                                               EnergyCalibration calibration, FwhmCalibration fwhmCalibration,
                                                               List<AlphaCount> counted, CrystalOwnSeriesFallback fallback,
                                                               out string note)
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
                if (!count.Found || !count.Reliable)
                {
                    string why;
                    if (!count.Found)
                    {
                        why = "ряд не найден (" + count.ActivityBq.ToString("F3", CultureInfo.InvariantCulture) + " ± "
                              + count.SigmaBq.ToString("F3", CultureInfo.InvariantCulture) + " Бк; " + count.Note + ")";
                    }
                    else if (count.LineFound)
                    {
                        why = "счёт не годен (в окне альф линия у " + count.LineKev.ToString("F0", CultureInfo.InvariantCulture)
                              + " кэВ-экв: " + (100.0 * count.LineShare).ToString("F2", CultureInfo.InvariantCulture) + " % счёта окна, z "
                              + count.LineZ.ToString("F1", CultureInfo.InvariantCulture) + "; счёт дал "
                              + count.ActivityBq.ToString("F3", CultureInfo.InvariantCulture) + " Бк)";
                    }
                    else if (count.SplitFound)
                    {
                        why = "счёт не годен (половины окна альф не согласны: верхняя − нижняя "
                              + (100.0 * count.SplitShare).ToString("F1", CultureInfo.InvariantCulture) + " % активности окна > "
                              + (100.0 * AlphaSplitMaxShare).ToString("0.#", CultureInfo.InvariantCulture) + " %, z "
                              + count.SplitZ.ToString("F1", CultureInfo.InvariantCulture)
                              + ": в окне добавка, которой нет в образе ряда; счёт дал "
                              + count.ActivityBq.ToString("F3", CultureInfo.InvariantCulture) + " Бк)";
                    }
                    else if (count.Overflow)
                    {
                        why = "счёт не годен (группы альф уходят за верх шкалы: последний канал и каналы-выбросы — "
                              + (100.0 * count.OverflowShare).ToString("F2", CultureInfo.InvariantCulture) + " % счёта окна > "
                              + (100.0 * AlphaOverflowMaxShare).ToString("0.#", CultureInfo.InvariantCulture) + " %; счёт дал "
                              + count.ActivityBq.ToString("F3", CultureInfo.InvariantCulture) + " Бк)";
                    }
                    else
                    {
                        why = "счёт не годен (подложка " + (count.Counts > 0.0 ? 100.0 * count.Baseline / count.Counts : 0.0)
                                  .ToString("F1", CultureInfo.InvariantCulture) + " % > "
                              + (100.0 * AlphaCountMaxBaselineShare).ToString("0.#", CultureInfo.InvariantCulture)
                              + " %: под окном альф гамма-континуум пробы, постоянная подложка вычитает лишнее; счёт дал "
                              + count.ActivityBq.ToString("F3", CultureInfo.InvariantCulture) + " Бк)";
                    }

                    // (`a274f`) Запасной путь — активность ряда из поля прибора с распадом головы (решение Amber
                    // 14.11.2026 «По фону с распадом»): запись становится известной и вычитается до фита.
                    string noField = null;
                    double known0 = fallback != null ? fallback.ActivityAtSpectrum(entry.Nuclide, out noField) : 0.0;
                    if (known0 > 0.0)
                    {
                        notes.Add(head + why + " — ряд из поля прибора с распадом: "
                                  + known0.ToString("F3", CultureInfo.InvariantCulture) + " Бк ("
                                  + fallback.ActivityBq.ToString("F3", CultureInfo.InvariantCulture) + " Бк на "
                                  + fallback.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ")");
                        copy.Entries.Add(new CrystalOwnEntry
                        {
                            Nuclide = entry.Nuclide,
                            Layer = entry.Layer,
                            Free = false,
                            ActivityBq = known0,
                            Histories = entry.Histories,
                            PerDecay = entry.PerDecay,
                            AlphaPerDecay = entry.AlphaPerDecay,
                        });
                        continue;
                    }

                    string tail = fallback != null && noField != null ? " (поле прибора: " + noField + ")" : "";
                    if (!count.Found)
                    {
                        notes.Add(head + why + " — строка снята" + tail);
                        continue;
                    }

                    // Ряд есть, но счёт не годен, а поля прибора нет: строка остаётся СВОБОДНОЙ — амплитуду делит с
                    // образами пробы подгонка FSA.
                    notes.Add(head + why + " — строка свободная" + tail);
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
