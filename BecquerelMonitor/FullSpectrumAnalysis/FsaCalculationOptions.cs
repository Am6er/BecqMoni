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
    ///     снимает свободные SE/DE (и K-вылет кристалла `Esc-*`, `S172`)
    ///     независимо от флажка, иначе флажок «вкл» возвращал бы старый
    ///     двойной счёт (`S47`).
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

        /// <summary>
        /// (П236, прототип; решение Amber 06.10.2026 «Из сета нуклидов») Источник
        /// состава — АКТИВНЫЙ СЕТ НУКЛИДОВ: ряды по меткам <see cref="NuclideDefinition.Chain"/>,
        /// одиночки по именам, найденные пики не читаются вовсе. Старше обоих
        /// прочих источников: при <c>true</c> <see cref="DbLookups"/> не смотрят.
        /// Сет и его состав входят в отпечаток сеанса (<c>FsaAnalysisSession.NuclideSetStamp</c>).
        /// </summary>
        public bool FromSet;

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
        /// (`AMBER269`; решение Amber 02.11.2026 вопросником, дословно: «Да, настройкой FSA с
        /// умолчанием 40 (Рекомендую)»; `AMBER274` — умолчание поставки снято до нуля решением Amber
        /// 03.11.2026 вопросником, дословно: «Правило порога + откат с шириной (Рекомендую)», числа —
        /// у <see cref="FsaBand.ShippedFitLowerBoundKev"/>) Нижняя граница разбора, кэВ
        /// (<see cref="FsaAnalyzer.FitLowerBoundKev"/>); нуль — пол полосы фита по правилу порога.
        /// В приложении — с общей настройки (<c>FsaCalculationSettings.FitLowerBoundKev</c>); у
        /// копии конфигурации спектра такого поля нет, и снимок с неё (<see cref="FromConfig"/>,
        /// пробы) несёт умолчание поставки — то же, что у анализатора.
        /// </summary>
        public double FitLowerBoundKev = FsaBand.ShippedFitLowerBoundKev;

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

        /// <summary>
        /// (`AMBER212`, П240 07.10.2026; решения Amber, дословно: «Одно на приложение,
        /// плюс по умолчанию "From Nucbase"», «Все семь одинаково») ИСТОЧНИК НАСТРОЕК
        /// ПРИЛОЖЕНИЯ. Ставит его ТОЛЬКО <c>Program.Main</c> — общая настройка
        /// <see cref="GlobalConfigInfo.FsaCalculation"/>, одна на все спектры и приборы.
        /// Пока он <c>null</c> (пробы, корпус — <c>Program.Main</c> они не проходят),
        /// <see cref="Of"/> читает копию конфигурации спектра, как прежде, и ни одно
        /// корпусное число от этой правки не сдвигается.
        /// </summary>
        public static System.Func<FsaCalculationOptions> ApplicationSource;

        /// <summary>
        /// То же — с активной копии конфигурации спектра; в приложении — с общей
        /// настройки (<see cref="ApplicationSource"/>), спектр тогда не читается.
        /// </summary>
        public static FsaCalculationOptions Of(ResultData resultData)
        {
            System.Func<FsaCalculationOptions> application = ApplicationSource;
            if (application != null)
            {
                return application();
            }

            return FromConfig(resultData != null
                              ? resultData.PeakDetectionMethodConfig as FWHMPeakDetectionMethodConfig
                              : null);
        }

        /// <summary>
        /// Перенести пользовательские смыслы во внутренние ключи анализатора.
        ///
        /// ⛔ Здесь же стоят и ЗАЩИТНЫЕ значения: <c>BackscatterWithMatrix = false</c>
        /// ставится безусловно, чтобы поднятый до вызова (пробой, по ошибке)
        /// ключ не пережил применение пользовательских настроек. ⚠ Поднять его
        /// обратно вправе только тот, кто знает про обстановку, — сессия, уже
        /// взявшая геометрию: <c>GeometryModel.InShield</c> (`AMBER12`). Порядок
        /// поэтому важен: сперва этот фасад, потом матрица и признак защиты.
        /// `EscapeGate`
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
            // (`AMBER269`) нижняя граница разбора — настройкой окна FSA
            analyzer.FitLowerBoundKev = this.FitLowerBoundKev > 0.0 ? this.FitLowerBoundKev : 0.0;
        }

        /// <summary>
        /// Перенести то, что решается при сборке библиотеки из баз: атомный
        /// рентген и равновесие ряда. Сборка одна на все источники состава (`AMBER306`).
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
        /// Часть отпечатка разбора: семь букв, по одной на настройку, и нижняя граница разбора, в
        /// постоянном порядке. Любое переключение меняет строку; две разные
        /// раскладки никогда не дают одну строку.
        /// </summary>
        public string Stamp
        {
            get
            {
                var stamp = new StringBuilder(24);
                stamp.Append(this.FromSet ? "set" : (this.DbLookups ? "db" : "peaks"));
                stamp.Append('|').Append(this.ChainEquilibrium ? "eq" : "free");
                stamp.Append('|').Append(this.AtomicXray ? "xray" : "-xray");
                stamp.Append('|').Append(this.CascadeSumming ? "sum" : "-sum");
                stamp.Append('|').Append(this.Backscatter ? "bs" : "-bs");
                stamp.Append('|').Append(this.EscapeAndAnnihilation ? "esc" : "-esc");
                stamp.Append('|').Append(this.PileUp ? "pu" : "-pu");
                // (`AMBER269`) нижняя граница разбора — граница меняет счёт
                stamp.Append("|lo").Append((this.FitLowerBoundKev > 0.0 ? this.FitLowerBoundKev : 0.0)
                                            .ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                return stamp.ToString();
            }
        }

        public override string ToString()
        {
            return this.Stamp;
        }
    }
}
