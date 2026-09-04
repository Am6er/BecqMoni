using System.Text;

namespace BecquerelMonitor.FullSpectrumAnalysis
{
    /// <summary>
    /// (`A145`, этап 1: `A168`, `A170`) СНИМОК ПОЛЬЗОВАТЕЛЬСКИХ НАСТРОЕК РАСЧЁТА
    /// полноспектрального разбора — и ЕДИНСТВЕННАЯ точка, где они становятся
    /// внутренними ключами анализатора и спецификации библиотеки.
    ///
    /// ЗАЧЕМ ОТДЕЛЬНЫЙ ТИП, а не пять присваиваний на месте. Внутренних ключей
    /// у анализатора больше, чем пользовательских смыслов, и связь между ними
    /// не «один к одному» (`handover/a145-fsa-display-groups.md`, разделы
    /// «Важная поправка к A145: EscapeGate» и «Остальные внутренние ключи»):
    ///
    ///   * «каскадное суммирование» — это ОБЕ половины разом:
    ///     <see cref="FsaAnalyzer.CascadeSumming"/> (множитель на площадь) и
    ///     <see cref="FsaAnalyzer.CascadeSumPeaks"/> (сумм-пики); третий ключ
    ///     <c>SumLayerIncludesContinuum</c> — про штриховку, а не про счёт, и
    ///     фасадом не трогается;
    ///   * «обратное рассеяние» пишет <see cref="FsaAnalyzer.Backscatter"/> и
    ///     ТОЛЬКО его: <see cref="FsaAnalyzer.BackscatterWithMatrix"/> нарочно
    ///     возвращает двойной счёт для A/B (`A83`), и поднять его отсюда
    ///     нельзя — члена с таким именем у этого типа нет, компилятор не даст;
    ///   * «вылеты и аннигиляция» пишет
    ///     <see cref="FsaAnalyzer.EscapeAndAnnihilation"/>, а защитный
    ///     <see cref="FsaAnalyzer.EscapeGate"/> не трогает: при матрице гейт
    ///     снимает свободные SE/DE независимо от флажка, иначе флажок «вкл»
    ///     возвращал бы старый двойной счёт (`S47`).
    ///
    /// Читатели: приложение (сеанс разбора, этап 2 `A145`) снимает копию
    /// <see cref="FromConfig"/> на UI-потоке вместе с прочими снимками и зовёт
    /// обе <c>ApplyTo</c>; <see cref="Stamp"/> входит в отпечаток разбора, чтобы
    /// переключение ЛЮБОГО расчётного флага меняло отпечаток (критерий 7).
    /// Пробы (`CorpusFsaProbe`, `FsaDoubleCountProbe`) идут той же дорогой,
    /// а абляционные ключи (`BackscatterWithMatrix`, `EscapeGate`) ставят
    /// ПОСЛЕ фасада и только у себя.
    ///
    /// ⚠ Группировка «родители/дочерние» сюда НЕ входит и входить не должна:
    /// это настройка представления, она не меняет расчёт и не входит в
    /// отпечаток (критерий 7).
    /// </summary>
    public sealed class FsaCalculationOptions
    {
        /// <summary>
        /// Источник состава: <c>true</c> — из NucBase по цепочке родителя
        /// (`S57`, <see cref="FWHMPeakDetectionMethodConfig.DbLookupsForFsa"/>),
        /// <c>false</c> — по подписям найденных пиков.
        /// </summary>
        public bool DbLookups;

        /// <summary>Ряд связан равновесием (`S70`). Действует только при <see cref="DbLookups"/>.</summary>
        public bool ChainEquilibrium = true;

        /// <summary>Атомный рентген в библиотеке (<see cref="FsaSampleSpec.AtomicXray"/>).</summary>
        public bool AtomicXray = true;

        /// <summary>Каскадное суммирование — обе половины разом.</summary>
        public bool CascadeSumming = true;

        /// <summary>Отдельный образ обратного рассеяния там, где матрицы нет.</summary>
        public bool Backscatter = true;

        /// <summary>Отдельные образы SE/DE (без матрицы) и `Ann-511`.</summary>
        public bool EscapeAndAnnihilation = true;

        /// <summary>Образ случайных наложений.</summary>
        public bool PileUp = true;

        /// <summary>
        /// Снимок с конфигурации поиска пиков спектра. <c>null</c> — умолчания
        /// (состав по пикам, равновесие и все пять компонентов включены), ровно
        /// то, что читалось до `A145`, когда конфигурации у спектра не было.
        /// </summary>
        public static FsaCalculationOptions FromConfig(FWHMPeakDetectionMethodConfig config)
        {
            var options = new FsaCalculationOptions();
            if (config == null)
            {
                return options;
            }

            options.DbLookups = config.DbLookupsForFsa;
            options.ChainEquilibrium = config.ChainEquilibrium;
            options.AtomicXray = config.AtomicXrayForFsa;
            options.CascadeSumming = config.CascadeSummingForFsa;
            options.Backscatter = config.BackscatterForFsa;
            options.EscapeAndAnnihilation = config.EscapeAndAnnihilationForFsa;
            options.PileUp = config.PileUpForFsa;
            return options;
        }

        /// <summary>То же — с активной копии конфигурации спектра.</summary>
        public static FsaCalculationOptions Of(ResultData resultData)
        {
            return FromConfig(resultData != null
                              ? resultData.PeakDetectionMethodConfig as FWHMPeakDetectionMethodConfig
                              : null);
        }

        /// <summary>
        /// Перенести пользовательские смыслы во внутренние ключи анализатора.
        ///
        /// ⛔ Здесь же стоят и ЗАЩИТНЫЕ значения: <c>BackscatterWithMatrix = false</c>
        /// ставится безусловно, чтобы поднятый до вызова (пробой, по ошибке)
        /// ключ не пережил применение пользовательских настроек. `EscapeGate`
        /// не трогается вовсе — это внутренняя защита от двойного счёта при
        /// матрице, и её значение к флажку отношения не имеет (`A168`).
        /// </summary>
        public void ApplyTo(FsaAnalyzer analyzer)
        {
            if (analyzer == null)
            {
                return;
            }

            analyzer.CascadeSumming = this.CascadeSumming;
            analyzer.CascadeSumPeaks = this.CascadeSumming;
            analyzer.Backscatter = this.Backscatter;
            analyzer.BackscatterWithMatrix = false;
            analyzer.EscapeAndAnnihilation = this.EscapeAndAnnihilation;
            analyzer.PileUp = this.PileUp;
        }

        /// <summary>
        /// Перенести то, что решается при сборке библиотеки из баз: атомный
        /// рентген и равновесие ряда. Путь по подписям пиков спецификации не
        /// имеет; там рентген передаётся доводом
        /// <see cref="FsaLibrary.BuildFromPeaks(System.Collections.Generic.IEnumerable{Peak}, System.Collections.Generic.IEnumerable{NuclideDefinition}, System.Collections.Generic.IDictionary{int, double}, bool)"/>.
        /// </summary>
        public void ApplyTo(FsaSampleSpec spec)
        {
            if (spec == null)
            {
                return;
            }

            spec.AtomicXray = this.AtomicXray;
            spec.Equilibrium = this.ChainEquilibrium;
        }

        /// <summary>
        /// Родительская группировка результата возможна только при составе из
        /// NucBase со связанным рядом (правило `A145`/`A169`): у свободных
        /// дочерних предел обнаружения родителя не существует. Это условие
        /// НАСТРОЕК; есть ли в составе хоть один связанный ряд, говорит уже
        /// результат — <see cref="FsaResult.ParentGroupingAllowed"/>.
        /// </summary>
        public bool ParentGroupingPossible
        {
            get { return this.DbLookups && this.ChainEquilibrium; }
        }

        /// <summary>
        /// Часть отпечатка разбора: семь букв, по одной на настройку, в
        /// постоянном порядке. Любое переключение меняет строку; две разные
        /// раскладки никогда не дают одну строку.
        /// </summary>
        public string Stamp
        {
            get
            {
                var stamp = new StringBuilder(24);
                stamp.Append(this.DbLookups ? "db" : "peaks");
                stamp.Append('|').Append(this.ChainEquilibrium ? "eq" : "free");
                stamp.Append('|').Append(this.AtomicXray ? "xray" : "-xray");
                stamp.Append('|').Append(this.CascadeSumming ? "sum" : "-sum");
                stamp.Append('|').Append(this.Backscatter ? "bs" : "-bs");
                stamp.Append('|').Append(this.EscapeAndAnnihilation ? "esc" : "-esc");
                stamp.Append('|').Append(this.PileUp ? "pu" : "-pu");
                return stamp.ToString();
            }
        }

        public override string ToString()
        {
            return this.Stamp;
        }
    }
}
