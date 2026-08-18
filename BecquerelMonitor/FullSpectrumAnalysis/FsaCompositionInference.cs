using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BecquerelMonitor.FullSpectrumAnalysis
{
    /// <summary>
    /// Один кандидат в состав: родитель, найденный поиском пиков, и всё, чем
    /// он себя подтвердил или не подтвердил.
    ///
    /// Держится и у отвергнутых тоже: «родителя не было» и «родитель был, но
    /// не набрал» с виду одно и то же, а разбираются по-разному — ровно та же
    /// причина, по которой заведён <see cref="FsaSampleLibrary.Report"/>.
    /// </summary>
    public sealed class FsaParentEvidence
    {
        /// <summary>`nucid` корня: «232TH», «137CS».</summary>
        public string Nucid = "";

        /// <summary>Подпись корня: «Th-232», «Cs-137».</summary>
        public string Name = "";

        /// <summary>У родителя есть дочерние — объявлять его надо рядом.</summary>
        public bool IsChain;

        /// <summary>Пиков, подписанных этим родителем при поиске.</summary>
        public int LabelledPeaks;

        /// <summary>Групп линий, которые модель ждала увидеть.</summary>
        public int Expected;

        /// <summary>Из них подтверждённых пиком.</summary>
        public int Matched;

        /// <summary>Доля подтверждённых: <see cref="Matched"/> / <see cref="Expected"/>.</summary>
        public double Coverage;

        /// <summary>Энергия неспутываемой линии, подтвердившей родителя; NaN — такой нет.</summary>
        public double AnchorKev = double.NaN;

        /// <summary>Родитель взят в состав.</summary>
        public bool Accepted;

        /// <summary>Почему принят или отвергнут — человеку и в журнал.</summary>
        public string Why = "";

        public override string ToString()
        {
            var text = new StringBuilder();
            // Сырой счёт подписанных пиков печатается рядом с долей нарочно:
            // именно он был первым кандидатом в критерий и именно он не годится
            // (`S57`). Видя оба числа вместе, читатель видит и почему.
            text.AppendFormat(CultureInfo.InvariantCulture, "{0} {1:P0} ({2}/{3}, пиков {4})",
                              this.Name, this.Coverage, this.Matched, this.Expected,
                              this.LabelledPeaks);
            if (!double.IsNaN(this.AnchorKev))
            {
                text.AppendFormat(CultureInfo.InvariantCulture, ", якорь {0:F1} кэВ", this.AnchorKev);
            }

            if (this.Why.Length > 0)
            {
                text.Append(" — ").Append(this.Why);
            }

            return text.ToString();
        }
    }

    /// <summary>
    /// Вывод состава пробы ИЗ ПОИСКА ПИКОВ — исполнение правила Amber
    /// 17.08.2026 (`S57`): «если поиск набрал весомое число пиков одного
    /// родителя — идём в базу и берём для FSA весь изотопный состав этого
    /// родителя».
    ///
    /// ПАРНАЯ СТРОКА К `S56`, И ГРАНИЦА МЕЖДУ НИМИ РОВНО ЗДЕСЬ. Постулат
    /// говорит: у каждого спектра своя база линий, привязанная к снятому
    /// нуклиду. В корпусе снятое известно из `manifest.csv`, и состав просто
    /// объявляется (<see cref="FsaSampleSpec"/>). В поле манифеста нет, и
    /// объявить состав некому — его надо ВЫВЕСТИ. Этот класс и есть вывод: он
    /// не собирает библиотеку сам, а заполняет тот же самый
    /// <see cref="FsaSampleSpec"/>, который дальше собирает
    /// <see cref="FsaSampleLibrary"/>. Второй сборки библиотеки в проекте нет и
    /// заводить её нельзя — сузив библиотеку двумя разными способами, мы
    /// получили бы два разных recall при одном имени.
    ///
    /// ОТКУДА БЕРУТСЯ КАНДИДАТЫ. Из подписей найденных пиков, то есть из общей
    /// библиотеки прибора (`NuclideDefinition`), — и это НЕ противоречит
    /// указанию Amber «источник — базы, а не конфиг». Конфиг здесь отвечает на
    /// вопрос «на что вообще посмотреть», и только на него: имя родителя из
    /// <see cref="NuclideDefinition.Chain"/> — это всё, что от него берётся.
    /// Состав ряда, линии, выходы, рентген — из `nucdb` и `matdb`, как и у
    /// `S56`. То есть общая библиотека перестаёт быть тем, что предъявляется
    /// спектру, и становится списком гипотез, каждая из которых обязана себя
    /// оплатить.
    ///
    /// ⛔ КРУГОВАЯ ЗАВИСИМОСТЬ ЗДЕСЬ НАСТОЯЩАЯ, И РАЗРЫВАЕТ ЕЁ ЗНАМЕНАТЕЛЬ, А НЕ ЧИСЛИТЕЛЬ.
    /// Поиск пиков задаёт библиотеку, библиотека задаёт разбор: один неверно
    /// подписанный пик мог бы утащить за собой весь состав. Развязка в том, ЧТО
    /// СРАВНИВАЕТСЯ. Числитель — пики, подписанные этим родителем, то есть
    /// буквально «весомое число пиков одного родителя» из правила Amber.
    /// Знаменатель — сколько таких пиков ДОЛЖНО было бы найтись, будь родитель
    /// в пробе, и считается он не по подписям, а по базе и по разрешению
    /// прибора. Ошибочная подпись поднимает числитель на единицу и не трогает
    /// знаменателя вовсе — фантом получает долю 1/16, а не состав.
    ///
    /// ⚠ Обратное решение — «подтверждать положением пика, не глядя на
    /// подпись» — выглядит строже и было отвергнуто ИЗМЕРЕНИЕМ 18.08.2026.
    /// На сцинтилляторе оно не работает: при ПШПВ 6.7 % у ASN16 окно
    /// соответствия на 344 кэВ выходит в 23 кэВ, и богатый линиями чужак
    /// попадает в чужую структуру всюду — Eu-152 набрал в ториевом спектре
    /// 15 групп из 16, Ag-108m в чароите 3 из 4, Am-241 в урановом стекле
    /// 8 из 15 и притащил за собой ряд нептуния тринадцатью образами.
    ///
    /// ⚠ ЧТО ЭТОТ КЛАСС НЕ ДЕЛАЕТ И ДЕЛАТЬ НЕ ДОЛЖЕН — приписывать линии.
    /// Своя база нуклида НИКОГДА не объяснит всего, что видно в спектре, и это
    /// не её дефект: часть линий рождается ПЕРЕНОСОМ, а не распадом
    /// (флуоресценция пробы, вылет K-рентгена кристалла), и в
    /// `decay_radiations` их нет и быть не может. Их территория —
    /// <see cref="FsaSampleSpec.AtomicXray"/> и матрица отклика (`F27`,
    /// физика 12). Увидев остаток на 20…65 кэВ, надо смотреть, есть ли матрица
    /// и свежа ли она, а не дописывать выдуманные линии.
    /// </summary>
    public static class FsaCompositionInference
    {
        /// <summary>
        /// Наименьшая доля ожидаемо-различимых групп, при которой родитель
        /// берётся в состав без якоря.
        ///
        /// ⛔ ЭТО ЧИСЛО ВЫВЕДЕНО ЗАМЕРОМ ПО КОРПУСУ, а не назначено, — того
        /// требует сама строка `S57`. Прогонялка — `CorpusFsaProbe --lib=infer
        /// --infer-theta=`, мерка — `tools/pie/score.py --members --part=`;
        /// разбор и таблица развёртки в `tools/CORPUS/README.md`.
        ///
        /// Сырой счёт пиков на это место не годится, и в строке названы обе
        /// причины: он смещён в пользу богатых линиями родителей (у Th-232 их
        /// сорок пять выше 1 %, у Cs-137 четыре, и порог «три пика» отдаёт
        /// торий всегда) и несравним поперёк корпуса (ПШПВ от 0.20 % у
        /// германия до 13.3 % у обсидиана). Здесь считается ДОЛЯ, знаменатель
        /// у неё — то, что при этой статистике и этом разрешении ВООБЩЕ можно
        /// было увидеть (см. <see cref="Score"/>), и обе беды снимаются
        /// знаменателем, а не порогом.
        ///
        /// **0.30 — КОЛЕНО развёртки, снятой 18.08.2026 на всём корпусе-129.**
        /// Ниже него фантомы растут быстро и без прибавки recall (0.25 даёт те
        /// же 66 %/70 %, но 8 и 7 фантомов против 5 и 4); выше — recall падает,
        /// а фантомов почти не убывает (0.35: 64 %/68 % при 4 и 3). Таблица
        /// целиком — в `tools/CORPUS/README.md`.
        /// </summary>
        public const double DefaultCoverage = 0.30;

        /// <summary>
        /// Наименьшая полуширина окна соответствия в долях энергии — на случай,
        /// когда калибровки ПШПВ нет или она вырождена.
        ///
        /// ⛔ Допуск подписи (`Tolerance`) сюда НЕ ГОДИТСЯ, и это выяснилось
        /// первым же прогоном. Он задан в ПРОЦЕНТАХ энергии и в поставочных
        /// конфигурациях равен 10…11 — то есть ±260 кэВ на линии 2614. С таким
        /// окном подтверждалось всё подряд: фантомный Eu-152 набирал в ториевом
        /// спектре 15 групп из 16. Допуск отвечает за СНОС ШКАЛЫ при подписи, а
        /// здесь нужна РАЗРЕШИМОСТЬ, и это разные величины.
        /// </summary>
        const double MinWindowFraction = 0.005;

        /// <summary>
        /// Доля веса якоря, начиная с которой НЕПОДТВЕРЖДЁННАЯ группа отменяет
        /// якорь: «ничего сравнимо яркого не пропало».
        ///
        /// Без этой оговорки якорь пропускал фантомы с долей 10 % — Am-243 в
        /// граните 18.08.2026 прошёл единственной линией 74.7 кэВ при девяти
        /// ненайденных. Смысл оговорки прямой: довод «главная линия на месте»
        /// стоит чего-то, только если рядом с ней НЕ отсутствует другая, почти
        /// такая же яркая. Отсутствие такой — довод против родителя, и он
        /// сильнее.
        /// </summary>
        public const double AnchorDominance = 0.5;

        /// <summary>
        /// Полуширина окна соответствия в долях ПШПВ.
        ///
        /// Три четверти, а не половина: сетка дрейфа самого разбора ходит на
        /// ±3 кэВ, и линия, стоящая ровно на краю полуширины, обязана
        /// подтвердиться.
        /// </summary>
        const double WindowFwhmFraction = 0.75;

        /// <summary>
        /// Что вышло — для проб, журнала и полки графика.
        /// </summary>
        public sealed class Report
        {
            /// <summary>Все кандидаты, принятые и отвергнутые, в порядке убывания доли.</summary>
            public readonly List<FsaParentEvidence> Candidates = new List<FsaParentEvidence>();

            /// <summary>Замечания сборки: чего не нашлось в базе и почему.</summary>
            public readonly List<string> Notes = new List<string>();

            /// <summary>Порог доли, с которым шёл этот вывод.</summary>
            public double Coverage;

            /// <summary>Принято родителей.</summary>
            public int Accepted;

            /// <summary>Пиков всего и из них подписанных.</summary>
            public int Peaks, Labelled;

            public override string ToString()
            {
                var text = new StringBuilder();
                text.AppendFormat(CultureInfo.InvariantCulture,
                                  "выведено из пиков: {0} из {1} подписаны, родителей {2} из {3}, порог доли {4:P0}",
                                  this.Labelled, this.Peaks, this.Accepted,
                                  this.Candidates.Count, this.Coverage);
                foreach (FsaParentEvidence candidate in this.Candidates)
                {
                    text.Append("; ").Append(candidate);
                }

                foreach (string note in this.Notes)
                {
                    text.Append("; ").Append(note);
                }

                return text.ToString();
            }
        }

        /// <summary>
        /// Состав по найденным пикам. Никогда не null: состав, в котором не
        /// прошёл ни один родитель, — это результат, а не отказ, и он не пуст —
        /// в нём остаются вездесущие ряды (<see cref="FsaSampleSpec.Room"/>).
        ///
        /// ⛔ Правило Amber 18.08.2026 «везде, где не знаешь, суй NORM — из
        /// базы, а не из `NuclideDefinition`» здесь работает в полную силу:
        /// именно этот случай — прибор в поле, состав неизвестен, — им и
        /// закрывается. Незнание закрывается природными рядами, а не пустотой.
        /// </summary>
        public static FsaSampleSpec Infer(IEnumerable<Peak> peaks,
                                          ResultData resultData,
                                          double coverage,
                                          bool anchors,
                                          bool novelty,
                                          out Report report)
        {
            report = new Report { Coverage = coverage };
            var spec = new FsaSampleSpec();
            if (resultData == null)
            {
                return spec;
            }

            // Окно — рабочий диапазон САМОГО поиска пиков. Иначе знаменатель
            // доли считался бы по линиям, которых прибор не искал: у ASN16 низ
            // стоит на 28.6 кэВ, и весь L-рентген ниже него в «ожидаемое»
            // попадать не вправе.
            var peakConfig = resultData.PeakDetectionMethodConfig as FWHMPeakDetectionMethodConfig;
            if (peakConfig != null && peakConfig.Max_Range > peakConfig.Min_Range)
            {
                spec.MinEnergyKev = peakConfig.Min_Range;
                spec.MaxEnergyKev = peakConfig.Max_Range;
            }

            double minSnr = peakConfig != null && peakConfig.Min_SNR > 0.0 ? peakConfig.Min_SNR : 10.0;
            var found = new List<Peak>();
            if (peaks != null)
            {
                foreach (Peak peak in peaks)
                {
                    if (peak != null && peak.Energy > 0.0)
                    {
                        found.Add(peak);
                    }
                }
            }

            found.Sort((a, b) => a.Energy.CompareTo(b.Energy));
            report.Peaks = found.Count;

            // Вещества вокруг кванта — из геометрии, если она есть. Тот же
            // источник и те же пороги, что у объявленного состава
            // (`CorpusFsaProbe.SpecOf`): второй источник правды двигал бы
            // энергию пика вылета, а она есть разность с Kα кристалла.
            EfficiencyConfigData efficiencyConfig = resultData.Efficiency;
            if (efficiencyConfig != null && efficiencyConfig.HasGeometry)
            {
                GeometryModel geometry = efficiencyConfig.Geometry;
                // ⚠ У КРИСТАЛЛА окна по Kα нет, и это не оплошность: элемент
                // кристалла не только светит сам, но и уносит энергию вылетом,
                // а пик вылета стоит на E − Kα, то есть внутри окна даже когда
                // сама Kα ниже его низа (измерено 18.08.2026 на ASN16).
                // Кристалл идёт целиком — с долями и именем вещества (`S84`):
                // образ вылета у него ОДИН, и соотношение его членов задаёт
                // вещество, а не фит.
                FsaSampleLibrary.DescribeCrystal(spec, geometry.Crystal, 0.01,
                    EfficiencySimulator.ScintillatorNameOf(geometry));
                spec.SampleElements.AddRange(FsaSampleLibrary.HeavyElementsOf(
                    geometry.Source, 0.01, spec.MinEnergyKev, spec.MaxEnergyKev));
            }

            var candidates = Candidates(found, spec, report);
            var models = new List<Model>();
            foreach (string nucid in candidates)
            {
                Model model = Build(nucid, spec, resultData, found, report);
                if (model != null)
                {
                    models.Add(model);
                }
            }

            Score(models, minSnr, anchors);

            // Порядок РЕШАЕТ, а не украшает: приём идёт жадно, по убыванию
            // доли, и «новизна» кандидата проверяется против уже принятых
            // (см. Accept). Первым читается то, на чём состав держится.
            models.Sort((a, b) => b.Evidence.Coverage.CompareTo(a.Evidence.Coverage));
            Accept(models, coverage, novelty);
            Collapse(models);

            foreach (Model model in models)
            {
                report.Candidates.Add(model.Evidence);
                if (!model.Evidence.Accepted)
                {
                    continue;
                }

                report.Accepted++;
                if (model.Evidence.IsChain)
                {
                    spec.Chains.Add(new FsaSampleChain(model.Evidence.Nucid));
                }
                else
                {
                    spec.Nuclides.Add(model.Evidence.Nucid);
                }
            }

            return spec;
        }

        /// <summary>То же с порогом прогона и якорями.</summary>
        public static FsaSampleSpec Infer(IEnumerable<Peak> peaks, ResultData resultData,
                                          double coverage, bool anchors, out Report report)
        {
            return Infer(peaks, resultData, coverage, anchors, true, out report);
        }

        /// <summary>То же с порогом прогона, якорями и проверкой новизны.</summary>
        public static FsaSampleSpec Infer(IEnumerable<Peak> peaks, ResultData resultData,
                                          double coverage, out Report report)
        {
            return Infer(peaks, resultData, coverage, true, true, out report);
        }

        /// <summary>То же с порогом по умолчанию.</summary>
        public static FsaSampleSpec Infer(IEnumerable<Peak> peaks, ResultData resultData,
                                          out Report report)
        {
            return Infer(peaks, resultData, DefaultCoverage, true, true, out report);
        }

        // ------------------------------------------------------------------
        // Кандидаты
        // ------------------------------------------------------------------

        /// <summary>
        /// Родители, которых назвал поиск пиков, в порядке первого появления.
        ///
        /// Родитель берётся из <see cref="NuclideDefinition.Chain"/>: у линии
        /// Bi-214 из радиевого равновесия там стоит «Ra-226», и объявлять надо
        /// именно радий, а не висмут — иначе ряд разорвётся на середине и
        /// половина линий останется без образа. Пусто — линия сама по себе, и
        /// родителем является её собственный нуклид.
        /// </summary>
        static List<string> Candidates(List<Peak> peaks, FsaSampleSpec spec, Report report)
        {
            var order = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var unknown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Peak peak in peaks)
            {
                if (peak.Nuclide == null || string.IsNullOrEmpty(peak.Nuclide.Name))
                {
                    continue;
                }

                report.Labelled++;

                // Характеристический рентген элемента родителем не бывает: за
                // его линиями нет распада вовсе. Но он сообщает нечто о
                // ГЕОМЕТРИИ — свинец домика, вольфрам электрода, — и это
                // единственный способ узнать про защиту, когда её никто не
                // объявлял. Элемент уходит в мешающие образы, где ему и место.
                if (NuclideDefinition.IsElementXrayName(peak.Nuclide.Name))
                {
                    int z = MaterialDatabase.ZOf(NuclideDefinition.NuclideNameOf(peak.Nuclide.Name));
                    if (z > 0 && !spec.ShieldElements.Contains(z))
                    {
                        spec.ShieldElements.Add(z);
                    }

                    continue;
                }

                string parent = peak.Nuclide.Chain;
                if (string.IsNullOrEmpty(parent))
                {
                    parent = peak.Nuclide.NuclideName;
                }

                string nucid = FsaSampleLibrary.NucidOf(parent);
                if (nucid.Length == 0)
                {
                    if (unknown.Add(parent))
                    {
                        report.Notes.Add("подпись «" + parent + "» не разобрана в nucid");
                    }

                    continue;
                }

                if (seen.Add(nucid))
                {
                    order.Add(nucid);
                }
            }

            return order;
        }

        // ------------------------------------------------------------------
        // Модель родителя: что он обещает и что из этого видно
        // ------------------------------------------------------------------

        /// <summary>
        /// Группа линий, неразличимая прибором: несколько линий, стоящих ближе
        /// одной ПШПВ, дают ОДИН наблюдаемый пик, и считать их порознь значит
        /// требовать от прибора того, чего он не умеет.
        ///
        /// Ровно это и снимает вторую беду сырого счёта, названную в `S57`:
        /// при ПШПВ от 0.20 % до 13.3 % одно и то же число линий означает
        /// разное число наблюдаемых структур, а число ГРУПП — уже одно и то же
        /// по смыслу.
        /// </summary>
        sealed class Group
        {
            public double Energy;
            public double Weight;
            public double Window;
            public double Snr = double.NaN;
            public bool Expected;

            public bool Matched
            {
                get { return !double.IsNaN(this.Snr); }
            }
        }

        sealed class Model
        {
            public readonly FsaParentEvidence Evidence = new FsaParentEvidence();
            public readonly List<Group> Groups = new List<Group>();
            public readonly HashSet<string> Members =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        static Model Build(string nucid, FsaSampleSpec spec, ResultData resultData,
                           List<Peak> peaks, Report report)
        {
            var model = new Model();
            model.Evidence.Nucid = nucid;
            model.Evidence.Name = FsaSampleLibrary.PrettyName(nucid);

            // Состав ряда собирается ТЕМ ЖЕ методом, которым потом соберётся
            // библиотека, — иначе критерий мерил бы один список, а фиту
            // предъявлялся другой.
            var branch = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var sample = new FsaSampleLibrary.Report();
            FsaSampleLibrary.CollectChain(new FsaSampleChain(nucid), spec.MinChainBranch,
                                          branch, sample);
            if (branch.Count == 0)
            {
                // Нуклида нет в `decay_chain` вовсе (стабильный, или подпись
                // указывает на то, чего база не знает). Ряда не будет, но сам
                // он линии иметь может — Cs-137 в `decay_chain` есть, а вот
                // одиночки без дочерних встречаются.
                branch[nucid] = 1.0;
            }

            foreach (string member in branch.Keys)
            {
                model.Members.Add(member);
            }

            // Ряд — если у родителя есть кто-то ещё, кроме него самого.
            model.Evidence.IsChain = branch.Count > 1;

            var lines = new List<double[]>();
            foreach (KeyValuePair<string, double> member in branch)
            {
                foreach (double[] line in FsaSampleLibrary.DecayLines(member.Key, sample))
                {
                    if (line[0] < spec.MinEnergyKev || line[0] > spec.MaxEnergyKev)
                    {
                        continue;
                    }

                    lines.Add(new[] { line[0], line[1] * member.Value });
                }
            }

            foreach (string note in sample.Notes)
            {
                report.Notes.Add(note);
            }

            if (lines.Count == 0)
            {
                model.Evidence.Why = "линий в рабочем окне нет";
                return model;
            }

            foreach (Peak peak in peaks)
            {
                if (Belongs(peak, nucid))
                {
                    model.Evidence.LabelledPeaks++;
                }
            }

            lines.Sort((a, b) => a[0].CompareTo(b[0]));
            Collect(model, lines, resultData, peaks);
            return model;
        }

        /// <summary>
        /// Линии — в группы по разрешению, группы — в соответствие с пиками.
        ///
        /// Вес группы есть сумма «выход × эффективность»: линия, которую прибор
        /// почти не регистрирует, ожидаемой не является, сколько бы ни был
        /// велик её выход. Кривой может не быть вовсе (в поле её обычно и нет)
        /// — тогда эффективность единица для всех, и вес вырождается в выход;
        /// это допущение, а не умолчание, и оно названо здесь.
        /// </summary>
        static void Collect(Model model, List<double[]> lines, ResultData resultData,
                            List<Peak> peaks)
        {
            FsaEfficiency efficiency = FsaEfficiency.FromConfig(resultData.Efficiency);
            EnergySpectrum spectrum = resultData.EnergySpectrum;
            EnergyCalibration energyCalibration = spectrum != null ? spectrum.EnergyCalibration : null;
            FwhmCalibration fwhmCalibration = resultData.FwhmCalibration;
            int channels = spectrum != null ? spectrum.NumberOfChannels : 0;

            Group current = null;
            double currentTop = 0.0;
            foreach (double[] line in lines)
            {
                double weight = line[1];
                if (efficiency != null)
                {
                    weight *= efficiency.Eval(line[0]);
                }

                if (!(weight > 0.0))
                {
                    continue;
                }

                double width = Resolution(line[0], energyCalibration, fwhmCalibration, channels);
                if (current != null && line[0] - current.Energy <= width)
                {
                    // Центр группы держится на сильнейшей линии: она и даёт
                    // наблюдаемый пик, а слабые соседи лишь подмешиваются.
                    current.Weight += weight;
                    if (weight > currentTop)
                    {
                        currentTop = weight;
                        current.Energy = line[0];
                    }

                    continue;
                }

                current = new Group { Energy = line[0], Weight = weight };
                currentTop = weight;
                model.Groups.Add(current);
            }

            foreach (Group group in model.Groups)
            {
                double window = Window(group.Energy,
                                       Resolution(group.Energy, energyCalibration,
                                                  fwhmCalibration, channels));
                group.Window = window;
                double best = double.NaN;
                foreach (Peak peak in peaks)
                {
                    if (peak.Energy < group.Energy - window)
                    {
                        continue;
                    }

                    if (peak.Energy > group.Energy + window)
                    {
                        break;
                    }

                    // ⛔ ПОДПИСЬ ПИКА ПРОВЕРЯЕТСЯ, И ЭТО БУКВА ПРАВИЛА AMBER:
                    // «если поиск набрал весомое число пиков ОДНОГО РОДИТЕЛЯ».
                    // Считать подтверждением любой пик в окне пробовали — на
                    // сцинтилляторе это не работает вовсе: при ПШПВ 6.7 % у
                    // ASN16 окно на 344 кэВ выходит в 23 кэВ, и фантомный
                    // Eu-152 набрал в ториевом спектре 15 групп из 16, потому
                    // что богатый линиями чужак попадает в чужую структуру
                    // всюду. Разделяет их не положение, а ПРИНАДЛЕЖНОСТЬ.
                    //
                    // Круговой зависимости это не создаёт: подпись решает
                    // «какой пик чей», а прошёл родитель или нет — решает ДОЛЯ
                    // от ожидаемого, и одной подписи для неё мало. Один
                    // ошибочно подписанный пик даёт долю 1/16, а не состав.
                    if (!Belongs(peak, model.Evidence.Nucid))
                    {
                        continue;
                    }

                    if (double.IsNaN(best) || peak.SNR > best)
                    {
                        best = peak.SNR;
                    }
                }

                group.Snr = best;
            }
        }

        /// <summary>
        /// Пик подписан этим родителем: либо его рядом
        /// (<see cref="NuclideDefinition.Chain"/>), либо им самим.
        /// </summary>
        static bool Belongs(Peak peak, string nucid)
        {
            if (peak.Nuclide == null || string.IsNullOrEmpty(peak.Nuclide.Name))
            {
                return false;
            }

            string parent = peak.Nuclide.Chain;
            if (string.IsNullOrEmpty(parent))
            {
                parent = peak.Nuclide.NuclideName;
            }

            return string.Equals(FsaSampleLibrary.NucidOf(parent), nucid,
                                 StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Полуширина окна соответствия: доля ПШПВ, а при отсутствующей
        /// калибровке — доля энергии. См. <see cref="MinWindowFraction"/> о том,
        /// почему сюда НЕ идёт допуск подписи.
        /// </summary>
        static double Window(double energy, double resolution)
        {
            return Math.Max(WindowFwhmFraction * resolution, MinWindowFraction * energy);
        }

        /// <summary>ПШПВ в кэВ на этой энергии; ноль — калибровки нет.</summary>
        static double Resolution(double energy, EnergyCalibration energyCalibration,
                                 FwhmCalibration fwhmCalibration, int channels)
        {
            if (energyCalibration == null || fwhmCalibration == null || channels <= 0)
            {
                return 0.0;
            }

            try
            {
                double channel = energyCalibration.EnergyToChannel(energy, maxChannels: channels);
                if (double.IsNaN(channel) || double.IsInfinity(channel))
                {
                    return 0.0;
                }

                channel = Math.Max(0.0, Math.Min(channels - 1, channel));
                double fwhm = fwhmCalibration.ChannelToFwhm(channel);
                if (!(fwhm > 0.0) || double.IsNaN(fwhm))
                {
                    return 0.0;
                }

                double left = energyCalibration.ChannelToEnergy(Math.Max(0.0, channel - 0.5 * fwhm));
                double right = energyCalibration.ChannelToEnergy(
                    Math.Min(channels - 1, channel + 0.5 * fwhm));
                double width = right - left;
                return width > 0.0 && !double.IsNaN(width) ? width : 0.0;
            }
            catch (Exception)
            {
                // Калибровка вырождена — соответствие ищется по допуску
                // подписи. Молчать нельзя было бы, если бы отсюда шёл счёт;
                // отсюда идёт ШИРИНА ОКНА, и запасная у неё есть.
                return 0.0;
            }
        }

        // ------------------------------------------------------------------
        // Критерий значимости
        // ------------------------------------------------------------------

        /// <summary>
        /// Кто из кандидатов прошёл — и почему.
        ///
        /// ⛔ ЗНАМЕНАТЕЛЬ ЗДЕСЬ ВАЖНЕЕ ПОРОГА, и в нём весь смысл правила.
        /// Считать долю от ВСЕХ линий родителя нельзя: у Th-232 их сорок пять
        /// выше 1 % на распад, и ни один сцинтиллятор столько не разрешает —
        /// настоящий торий получил бы долю 0.2 и был бы отвергнут. Поэтому
        /// знаменатель — линии, которые при ЭТОЙ статистике и ЭТОМ разрешении
        /// вообще можно было увидеть, и определяется он самими данными:
        ///
        ///   * по подтверждённым группам берётся медиана «SNR на единицу
        ///     веса» — это и есть цена единицы выхода в отсчётах ЭТОГО
        ///     спектра;
        ///   * ожидаемой считается группа, которой при этой цене полагается
        ///     SNR не ниже порога поиска пиков.
        ///
        /// Отсюда сразу два нужных свойства. Слабый спектр не наказывается: у
        /// него мало что «ожидалось», доля считается от малого. И ложный
        /// родитель, зацепившийся одной слабой линией, наказывается жёстко:
        /// медиана цены выходит огромной, ожидаемым становится ВЕСЬ ряд, и
        /// доля падает до одной группы из сорока.
        ///
        /// ⚠ Медиана, а не среднее: одно случайное совпадение сильного пика со
        /// слабой линией сдвинуло бы среднее на порядок.
        /// </summary>
        static void Score(List<Model> models, double minSnr, bool anchors)
        {
            foreach (Model model in models)
            {
                FsaParentEvidence evidence = model.Evidence;
                if (model.Groups.Count == 0)
                {
                    continue;
                }

                var prices = new List<double>();
                foreach (Group group in model.Groups)
                {
                    if (group.Matched && group.Weight > 0.0)
                    {
                        prices.Add(group.Snr / group.Weight);
                    }
                }

                if (prices.Count == 0)
                {
                    evidence.Why = "ни одна линия не подтверждена пиком";
                    continue;
                }

                prices.Sort();
                double price = prices.Count % 2 == 1
                    ? prices[prices.Count / 2]
                    : 0.5 * (prices[prices.Count / 2 - 1] + prices[prices.Count / 2]);

                foreach (Group group in model.Groups)
                {
                    // Подтверждённая группа ожидаемой является по построению:
                    // её ВИДНО. Оценка цены — медиана, и половина
                    // подтверждённых стоит по ней «ниже порога»; вычесть их из
                    // знаменателя, оставив в числителе, значило бы получить
                    // долю больше единицы.
                    group.Expected = group.Matched || price * group.Weight >= minSnr;
                    if (group.Expected)
                    {
                        evidence.Expected++;
                    }

                    if (group.Matched)
                    {
                        evidence.Matched++;
                    }
                }

                evidence.Coverage = evidence.Expected > 0
                    ? (double)evidence.Matched / evidence.Expected : 0.0;
                evidence.AnchorKev = anchors ? Anchor(model, models) : double.NaN;
            }
        }

        /// <summary>
        /// Жадный приём по убыванию доли — и проверка НОВИЗНЫ против уже
        /// принятых.
        ///
        /// ⛔ ТРЕТЬЕ УСЛОВИЕ, И ОНО ПРО ГЛАВНЫЙ ОСТАВШИЙСЯ КЛАСС ФАНТОМОВ.
        /// Доля и якорь судят кандидата ПООДИНОЧКЕ, а фантом живёт не один: он
        /// садится на структуру, которую уже объяснил кто-то другой. Измерено
        /// 18.08.2026 при пороге 30 %: в ториевом спектре `ASN16_Th232` Eu-152
        /// набирает 3 группы из 8 — и все три стоят на линиях самого тория
        /// (121.78 против 129.06 у Ac-228, 344.3 против 338.32, 964.1 против
        /// 968.97), то есть НИ ОДНОЙ своей структуры не приносит. Ровно тот же
        /// механизм, что у фантома Pu-238 из `N18`.
        ///
        /// Поэтому кандидат обязан принести хоть одну СВОЮ группу: такую, рядом
        /// с которой ни у одного уже принятого неродственного родителя нет
        /// ожидаемой линии. Проверка идёт по ПОЛОЖЕНИЮ, а не по подписи —
        /// вопрос здесь другой, чем при подтверждении: не «чей это пик», а
        /// «есть ли уже в составе тот, кто способен светить в этом месте».
        ///
        /// ⚠ Жадность делает порядок значимым, и он выбран не произвольно: по
        /// убыванию доли. Сильнейший кандидат занимает свою структуру первым, и
        /// слабый обязан доказывать себя ОСТАТКОМ. Обратный порядок отдал бы
        /// структуру тому, кто просто оказался раньше в списке пиков.
        /// </summary>
        static void Accept(List<Model> models, double coverage, bool novelty)
        {
            var accepted = new List<Model>();
            foreach (Model model in models)
            {
                FsaParentEvidence evidence = model.Evidence;
                if (evidence.Expected == 0)
                {
                    continue;
                }

                bool anchored = !double.IsNaN(evidence.AnchorKev);
                if (!anchored && evidence.Coverage < coverage)
                {
                    evidence.Why = "доля ниже порога";
                    continue;
                }

                if (novelty && !Novel(model, accepted))
                {
                    evidence.Why = "своей структуры не приносит";
                    continue;
                }

                evidence.Accepted = true;
                evidence.Why = anchored ? "неспутываемая линия на месте" : "доля выше порога";
                accepted.Add(model);
            }
        }

        /// <summary>
        /// У кандидата есть подтверждённая группа, на месте которой ни один уже
        /// принятый неродственный родитель светить не может.
        ///
        /// Пустой список принятых — новизна есть по определению: первому
        /// доказывать нечего, он и задаёт отсчёт.
        /// </summary>
        static bool Novel(Model model, List<Model> accepted)
        {
            foreach (Group group in model.Groups)
            {
                if (!group.Matched)
                {
                    continue;
                }

                bool taken = false;
                foreach (Model other in accepted)
                {
                    if (Kin(model, other))
                    {
                        // Родня спорить не может: это одно утверждение, и
                        // старший поглотит младшего в Collapse.
                        continue;
                    }

                    foreach (Group rival in other.Groups)
                    {
                        if (rival.Expected
                            && Math.Abs(rival.Energy - group.Energy) <= group.Window)
                        {
                            taken = true;
                            break;
                        }
                    }

                    if (taken)
                    {
                        break;
                    }
                }

                if (!taken)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Неспутываемая ГЛАВНАЯ линия родителя, стоящая на месте, — или NaN.
        ///
        /// Второй путь в состав, и нужен он ровно одному классу нуклидов —
        /// бедным линиями. У Cs-137 в рабочем окне четыре группы: 661.66 и три
        /// рентгеновские, которых сцинтиллятор внизу шкалы обычно не берёт.
        /// Доля выходит 1/4, порога не хватает, а нуклид в спектре стоит и
        /// виден за версту. Богатым родителям этот путь не нужен: у настоящего
        /// Th-232 доля и так за две трети.
        ///
        /// ⛔ ЯКОРЕМ СЛУЖИТ ТОЛЬКО САМАЯ СИЛЬНАЯ ОЖИДАЕМАЯ ГРУППА, и это не
        /// строгость ради строгости. Первый прогон брал якорем любую заметную
        /// линию — и якорь нашёлся у ВСЕХ кандидатов подряд, включая Ag-108m в
        /// чароите и Am-241 в урановом стекле; последний тянул за собой ряд
        /// нептуния и тринадцать выдуманных образов. Правило «главная линия»
        /// само по себе содержит нужную проверку: если родитель есть, ярче
        /// всего видно именно её, и её отсутствие есть довод против него, а не
        /// повод искать якорь послабее.
        ///
        /// ⚠ «Не с чем спутать» проверяется по ДРУГИМ кандидатам, и родня из
        /// проверки исключается: если поиск подписал пики и «Tl-208 (Th-232)»,
        /// и голым «Tl-208», кандидатов выйдет два, и они отняли бы якорь друг
        /// у друга — при том что физически это одно утверждение. Родня
        /// опознаётся по вхождению корня в состав соседа.
        /// </summary>
        static double Anchor(Model model, List<Model> models)
        {
            Group main = null;
            foreach (Group group in model.Groups)
            {
                if (group.Expected && (main == null || group.Weight > main.Weight))
                {
                    main = group;
                }
            }

            if (main == null || !main.Matched)
            {
                return double.NaN;
            }

            // Ничего сравнимо яркого не пропало — иначе якорь не довод.
            foreach (Group group in model.Groups)
            {
                if (group.Expected && !group.Matched
                    && group.Weight >= AnchorDominance * main.Weight)
                {
                    return double.NaN;
                }
            }

            foreach (Model other in models)
            {
                if (other == model || Kin(model, other))
                {
                    continue;
                }

                foreach (Group rival in other.Groups)
                {
                    // Соперник учитывается, только если он сам ожидаем: линия в
                    // тысячную процента не спутывает ничего, её просто не видно.
                    if (rival.Expected && Math.Abs(rival.Energy - main.Energy) <= main.Window)
                    {
                        return double.NaN;
                    }
                }
            }

            return main.Energy;
        }

        /// <summary>Родня: корень одного входит в состав другого.</summary>
        static bool Kin(Model a, Model b)
        {
            return a.Members.Contains(b.Evidence.Nucid) || b.Members.Contains(a.Evidence.Nucid);
        }

        /// <summary>
        /// Принятый родитель, целиком лежащий внутри другого принятого, из
        /// состава убирается: объявить и Th-232, и Tl-208 значит предъявить
        /// фиту один и тот же образ дважды.
        ///
        /// Побеждает СТАРШИЙ — тот, в чей ряд входит другой. Так велит само
        /// правило Amber: набрал родитель — берём ВЕСЬ его изотопный состав, а
        /// состав старшего дочернего уже содержит.
        /// </summary>
        static void Collapse(List<Model> models)
        {
            foreach (Model model in models)
            {
                if (!model.Evidence.Accepted)
                {
                    continue;
                }

                foreach (Model elder in models)
                {
                    if (elder == model || !elder.Evidence.Accepted)
                    {
                        continue;
                    }

                    if (elder.Members.Contains(model.Evidence.Nucid)
                        && !model.Members.Contains(elder.Evidence.Nucid))
                    {
                        model.Evidence.Accepted = false;
                        model.Evidence.Why = "входит в ряд " + elder.Evidence.Name;
                        break;
                    }
                }
            }
        }
    }
}
