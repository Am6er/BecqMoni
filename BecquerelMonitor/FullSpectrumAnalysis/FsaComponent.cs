using System.Collections.Generic;

namespace BecquerelMonitor.FullSpectrumAnalysis
{
    /// <summary>Вид компонента в разложении.</summary>
    public enum FsaComponentKind
    {
        /// <summary>Цепочка из нуклидного сета (жёсткая связка интенсивностей).</summary>
        Chain,
        /// <summary>Одиночный нуклид из встроенной таблицы.</summary>
        Single,
        /// <summary>Мешающий образ: рентген, пики вылета. В «пирог» не входит.</summary>
        Nuisance
    }

    /// <summary>Линия компонента: энергия и выход на распад родителя цепочки, %.</summary>
    public sealed class FsaLine
    {
        public FsaLine(string nuclide, double energy, double intensity)
            : this(nuclide, energy, intensity, 0.0)
        {
        }

        /// <summary>
        /// (`AMBER54`, П127 22.09.2026) Линия с известной аннигиляционной
        /// частью выхода — см. <see cref="AnnihilationIntensity"/>.
        /// </summary>
        public FsaLine(string nuclide, double energy, double intensity, double annihilationIntensity)
        {
            this.Nuclide = nuclide ?? "";
            this.Energy = energy;
            this.Intensity = intensity;
            this.AnnihilationIntensity = annihilationIntensity > 0.0 ? annihilationIntensity : 0.0;
        }

        public string Nuclide { get; private set; }

        public double Energy { get; private set; }

        public double Intensity { get; private set; }

        /// <summary>
        /// (`AMBER54`, П127 22.09.2026) Часть выхода <see cref="Intensity"/>,
        /// пришедшая АННИГИЛЯЦИЕЙ позитронов родителя (`2·ΣI(β⁺)`, % на распад;
        /// библиотека кладёт её на 511 кэВ — <c>FsaSampleLibrary.DecayLines</c>);
        /// 0 — линия целиком гамма или рентген. Гамма и аннигиляция одной
        /// энергии лежат в ОДНОЙ линии (правило `S161`), и её гамма-часть —
        /// разность <see cref="Intensity"/> − <see cref="AnnihilationIntensity"/>.
        /// Читает гейт свободного образа `Ann-511`
        /// (<c>FsaAnalyzer.AnnihilationGate</c>): столкновение с гаммой состава
        /// и своя аннигиляционная линия нуклида — два разных случая, и по одной
        /// энергии их не различить. Имени нуклида признак не несёт: кто β⁺ —
        /// говорит база.
        /// </summary>
        public double AnnihilationIntensity { get; private set; }
    }

    /// <summary>
    /// (`AMBER274`, Э1 29.10.2026) БЕТА-ВЕТВЬ компонента — сплошной спектр, образ
    /// которого строится по разделу строк электрона матрицы
    /// (<c>EfficiencyMaker.ResponseElectronSection</c>, слой «Бета» пробы). Решения
    /// Amber 29.10.2026 вопросником, дословно: «Бета пробы первой (Рекомендую)»,
    /// «Код + BetaShape для кристалла (Рекомендую)» — форма в коде
    /// (<see cref="FsaBetaLibrary"/>: разрешённая с функцией Ферми, уникальные
    /// запреты по ΔJπ).
    /// </summary>
    public sealed class FsaBeta
    {
        public FsaBeta(string nuclide, double endpointKev, double intensity, int daughterZ, int massNumber,
                       int uniqueOrder, bool positron, double meanKev)
        {
            this.Nuclide = nuclide ?? "";
            this.EndpointKev = endpointKev;
            this.Intensity = intensity;
            this.DaughterZ = daughterZ;
            this.MassNumber = massNumber;
            this.UniqueOrder = uniqueOrder;
            this.Positron = positron;
            this.MeanKev = meanKev;
        }

        /// <summary>Нуклид ветви — тем же именем, что у линий его распада.</summary>
        public string Nuclide { get; private set; }

        /// <summary>Граница спектра E₀, кэВ.</summary>
        public double EndpointKev { get; private set; }

        /// <summary>Выход ветви, % на распад РОДИТЕЛЯ компонента (с равновесием ряда, как у линий).</summary>
        public double Intensity { get; private set; }

        /// <summary>Заряд дочернего ядра (функция Ферми).</summary>
        public int DaughterZ { get; private set; }

        /// <summary>Массовое число (радиус ядра в функции Ферми).</summary>
        public int MassNumber { get; private set; }

        /// <summary>Порядок уникального запрета формы: 0 — разрешённая, n — уникальный n-го.</summary>
        public int UniqueOrder { get; private set; }

        /// <summary>β⁺ (позитрон).</summary>
        public bool Positron { get; private set; }

        /// <summary>Средняя энергия ветви по базе, кэВ (сверка формы).</summary>
        public double MeanKev { get; private set; }

        /// <summary>
        /// (`AMBER274` Ф4, решение Amber 30.10.2026 вопросником, дословно: «Включить в Э1
        /// (Рекомендую)») ЛИНИЯ ЭЛЕКТРОНА КОНВЕРСИИ, а не сплошной спектр: энергия —
        /// <see cref="EndpointKev"/>, выход — <see cref="Intensity"/>, оболочка —
        /// <see cref="Shell"/>. Образ — строка узла при этой энергии («шапочки» сетки).
        /// </summary>
        public bool IsLine { get; private set; }

        /// <summary>(`Ф4`) Оболочка линии конверсии: K, L, M, N+; у ветви — пусто.</summary>
        public string Shell { get; private set; }

        /// <summary>
        /// (`Ф1`/`Ф2`, решение Amber 30.10.2026 вопросником, дословно: «Таблицы
        /// BetaShape/LNHB в nucdb (Рекомендую)») Характер перехода словами по ΔJπ:
        /// `allowed`, `1st non-unique`, `1st unique`, `2nd non-unique`… (у формы из таблицы
        /// — как записан в таблице).
        /// </summary>
        public string Transition { get; private set; }

        /// <summary>
        /// (`Ф1`/`Ф2`) Форма из таблицы `nucdb.beta_shapes`: узлы dN/dT (вид `points`),
        /// граница таблицы <see cref="ShapeEndpointKev"/>; null — формы таблицей нет.
        /// </summary>
        public double[] ShapeT { get; private set; }

        public double[] ShapeDensity { get; private set; }

        public double ShapeEndpointKev { get; private set; }

        /// <summary>(`Ф1`/`Ф2`) Фактор формы C(W) = a0 + a1·W + a2/W + a3·W² к разрешённой (вид `factor`); null — нет.</summary>
        public double[] ShapeFactor { get; private set; }

        /// <summary>
        /// (`AMBER274`, К3) Σ выходов ветвей β⁻ и β⁺ нуклида после отсева пределов `LT` и дублей
        /// второй оценки, % на его распад (<c>FsaBetaLibrary.BranchCheck</c>); NaN — не считалась.
        /// Выше 100 % + <c>FsaBetaLibrary.BranchSumTolerancePercent</c> — поставка противоречит
        /// себе, и отчёт FSA это называет.
        /// </summary>
        public double BranchSumPercent { get; private set; } = double.NaN;

        /// <summary>(`AMBER274`, К3) Та же ветвь с проверенной суммой ветвей нуклида.</summary>
        public FsaBeta WithBranchSum(double sumPercent)
        {
            FsaBeta copy = this.Copy(this.Nuclide, 1.0);
            copy.BranchSumPercent = sumPercent;
            return copy;
        }

        /// <summary>(`Ф1`/`Ф2`) Номер строки `beta_shapes` (0 — форма кодом) и её источник.</summary>
        public int ShapeId { get; private set; }

        public string ShapeSource { get; private set; }

        /// <summary>(`Ф4`) Линия электрона конверсии: энергия, выход (% на распад), оболочка.</summary>
        public static FsaBeta Line(string nuclide, double energyKev, double intensity, string shell)
        {
            return new FsaBeta(nuclide, energyKev, intensity, 0, 0, 0, false, energyKev)
            {
                IsLine = true,
                Shell = shell ?? "",
                Transition = "conversion",
            };
        }

        /// <summary>(`Ф1`/`Ф2`) Та же ветвь с формой из таблицы и словом перехода.</summary>
        public FsaBeta WithShape(string transition, int shapeId, string source, double[] t, double[] density,
                                 double tableEndpointKev, double[] factor)
        {
            FsaBeta copy = this.Copy(this.Nuclide, 1.0);
            copy.Transition = transition;
            copy.ShapeId = shapeId;
            copy.ShapeSource = source;
            copy.ShapeT = t;
            copy.ShapeDensity = density;
            copy.ShapeEndpointKev = tableEndpointKev;
            copy.ShapeFactor = factor;
            return copy;
        }

        /// <summary>(`Ф1`/`Ф2`) Та же ветвь со словом перехода (форма кодом).</summary>
        public FsaBeta WithTransition(string transition)
        {
            FsaBeta copy = this.Copy(this.Nuclide, 1.0);
            copy.Transition = transition;
            return copy;
        }

        /// <summary>Та же ветвь с выходом, умноженным на равновесное отношение члена ряда.</summary>
        public FsaBeta Scaled(string nuclide, double factor)
        {
            return this.Copy(nuclide, factor);
        }

        FsaBeta Copy(string nuclide, double factor)
        {
            return new FsaBeta(nuclide, this.EndpointKev, this.Intensity * factor, this.DaughterZ, this.MassNumber,
                               this.UniqueOrder, this.Positron, this.MeanKev)
            {
                IsLine = this.IsLine,
                Shell = this.Shell,
                Transition = this.Transition,
                ShapeT = this.ShapeT,
                ShapeDensity = this.ShapeDensity,
                ShapeEndpointKev = this.ShapeEndpointKev,
                ShapeFactor = this.ShapeFactor,
                ShapeId = this.ShapeId,
                ShapeSource = this.ShapeSource,
                BranchSumPercent = this.BranchSumPercent,
            };
        }
    }

    /// <summary>
    /// (`S171`) ПРИВЯЗКА ВЫРОЖДЕННОГО ЧЛЕНА РЯДА: член, чья колонка в режиме
    /// без связки равновесия неотличима от колонок других членов ТОГО ЖЕ
    /// ряда, своей свободной амплитуды не получает, а идёт с амплитудой
    /// партнёра в равновесном отношении (решение Amber 14.09.2026, дословно:
    /// «Привязать к члену, с которым вырожден, пометить»). Запись лежит у
    /// колонки-хозяина (<see cref="FsaComponent.Ties"/>), в которую линии
    /// члена слиты; на экране член помечен «по партнёру».
    /// </summary>
    public sealed class FsaTie
    {
        /// <summary>Имя привязанного члена (как у его строки состава).</summary>
        public string Member { get; set; }

        /// <summary>Имя члена, с амплитудой которого он идёт.</summary>
        public string Partner { get; set; }

        /// <summary>
        /// Мера, по которой привязан: доля образа члена (за вычетом того, что
        /// берёт на себя континуум), представимая образами прочих СВОБОДНЫХ
        /// членов ряда вместе, 0…1 — дополнение Шура во взвешенной метрике
        /// фита (<c>FsaAnalyzer.TieDegenerateMembers</c>).
        /// </summary>
        public double Share { get; set; }

        /// <summary>
        /// Та же доля, но ОДНИМ партнёром — квадрат косинуса образов члена и
        /// партнёра в той же метрике; партнёр и выбран как наибольший из них.
        /// </summary>
        public double PairShare { get; set; }

        /// <summary>
        /// Суммарный выход члена на собственный распад, % (`S69`) — своя
        /// строка предела у него остаётся, и свёртка судит её по ЕГО выходу,
        /// а не по выходу колонки-хозяина.
        /// </summary>
        public double TotalYieldPercent { get; set; } = double.NaN;

        /// <summary>
        /// Приговор: привязан (доля достигла порога). В списках привязок
        /// (<see cref="FsaComponent.Ties"/>, <c>FsaResult.Ties</c>) всегда
        /// true; false бывает только в списке ПРИГОВОРОВ анализатора
        /// (<c>FsaAnalyzer.ChainTieJudgements</c>), где стоят и свободные —
        /// с мерами, по которым их не привязали.
        /// </summary>
        public bool Tied { get; set; }
    }

    /// <summary>
    /// (`S171`, второе правило) ПРИГОВОР ПРАВИЛА ПРЕДЕЛА НЕИЗМЕРИМОГО ЧЛЕНА
    /// РЯДА (решение Amber 14.09.2026, дословно: «Не измерим при активности
    /// сильнейшего — предел»): член ряда, чей собственный образ при опорной
    /// амплитуде ряда — амплитуде СИЛЬНЕЙШЕГО (точнее всех измеримого)
    /// свободного члена — не дал бы значимости выше порога отсева, свободной
    /// колонки не получает и идёт строкой предела. Записи — по каждому
    /// судимому члену каждого круга, снятым и оставленным
    /// (<c>FsaAnalyzer.ChainLimitJudgements</c>, <c>FsaResult.ChainLimits</c>).
    /// </summary>
    public sealed class FsaChainLimit
    {
        /// <summary>Имя судимого члена.</summary>
        public string Member { get; set; }

        /// <summary>Имя сильнейшего члена ряда, при амплитуде которого судили.</summary>
        public string Reference { get; set; }

        /// <summary>
        /// Опорная амплитуда — амплитуда сильнейшего в том же фите (распады
        /// корня ряда за живое время, как у всех членов ряда без связки).
        /// </summary>
        public double ReferenceAmplitude { get; set; }

        /// <summary>
        /// Погрешность амплитуды члена в том же фите — знаменатель значимости
        /// (та же σ, что у отсева, с тем же множителем χ²/ndf).
        /// </summary>
        public double Sigma { get; set; }

        /// <summary>Ожидаемая значимость члена при опорной амплитуде: <see cref="ReferenceAmplitude"/> / <see cref="Sigma"/>.</summary>
        public double ExpectedZ { get; set; }

        /// <summary>Фактическая значимость члена в том же фите (то, что видел отсев).</summary>
        public double Z { get; set; }

        /// <summary>Порог, с которым сравнивали (порог отсева того же разбора либо подменённый пробой).</summary>
        public double Threshold { get; set; }

        /// <summary>Круг суда (после каждого снятия — перефит и новый круг).</summary>
        public int Round { get; set; }

        /// <summary>Ожидаемая значимость ниже порога в этом круге.</summary>
        public bool Below { get; set; }

        /// <summary>
        /// Приговор: снят в предел В ЭТОМ КРУГЕ. За круг снимается ОДИН член —
        /// с наименьшей ожидаемой значимостью среди тех, у кого она ниже
        /// порога; прочие (<see cref="Below"/>) судятся снова после перефита,
        /// когда опорная амплитуда уже не занижена снятым стоком.
        /// </summary>
        public bool Limited { get; set; }
    }

    /// <summary>
    /// Образ компонента: набор линий, из которых строится «спектр пиков»
    /// (сумма гауссиан единичной площади с весами по выходу и эффективности).
    /// </summary>
    public sealed class FsaComponent
    {
        public FsaComponent(string name, FsaComponentKind kind)
        {
            this.Name = name;
            this.Kind = kind;
            this.Lines = new List<FsaLine>();
            this.Betas = new List<FsaBeta>();
        }

        /// <summary>
        /// (`AMBER274`, Э1) Бета-ветви компонента (по членам, тем же весом, что их
        /// линии). Читает их только образ по матрице С РАЗДЕЛОМ строк электрона;
        /// без раздела список не влияет ни на одно число разбора.
        /// </summary>
        public List<FsaBeta> Betas { get; private set; }

        /// <summary>
        /// (`AMBER274`, Ф14) СТРОКА ПО СПЛОШНОМУ СПЕКТРУ — колонка чистого бета-излучателя:
        /// линий нет (<see cref="Lines"/> пуст), образ — одна бета её членов
        /// (<see cref="Betas"/>), амплитуда свободная против сплайна. Решение Amber
        /// 30.10.2026 вопросником, дословно: «Строка без линий по набору (Рекомендую)» —
        /// в состав входит только набором или пробой (объявленным составом), пределы —
        /// как у прочих, пометка «по сплошному спектру». Собирает её
        /// <c>FsaSampleLibrary.Build</c> (правило — <c>FsaSampleLibrary.PureBetaPhotonFloorPercent</c>);
        /// без годного раздела электрона матрицы анализатор снимает её до фита с причиной
        /// (<c>FsaAnalyzer.BetaOnlyRemoved</c>). Имён нуклидов признак не несёт: чистый ли
        /// излучатель — говорит база.
        /// </summary>
        public bool BetaOnly { get; set; }

        /// <summary>(`AMBER274`) Перенести бета-ветви нуклида <paramref name="nuclide"/> (null — все) из <paramref name="source"/>.</summary>
        public void CopyBetas(FsaComponent source, string nuclide)
        {
            if (source == null || source.Betas == null)
            {
                return;
            }

            foreach (FsaBeta beta in source.Betas)
            {
                if (nuclide == null || string.Equals(beta.Nuclide, nuclide, System.StringComparison.OrdinalIgnoreCase))
                {
                    this.Betas.Add(beta);
                }
            }
        }

        public string Name { get; private set; }

        public FsaComponentKind Kind { get; private set; }

        public List<FsaLine> Lines { get; private set; }

        /// <summary>
        /// Вес линии посчитан целиком, эффективность в него уже входит и второй
        /// раз применяться не должна. Так устроен образ обратного рассеяния:
        /// вес берётся на энергии исходного фотона, а стоит линия на энергии
        /// рассеянного.
        /// </summary>
        public bool WeightsAreFinal { get; set; }

        /// <summary>
        /// Образ выведен из состава предыдущего прохода, а не задан библиотекой:
        /// перед пересборкой такие колонки выбрасываются, иначе они накапливались
        /// бы от прохода к проходу.
        /// </summary>
        public bool Derived { get; set; }

        /// <summary>
        /// (`AMBER4`) Образ характеристического рентгена САМОГО КРИСТАЛЛА — в
        /// отличие от рентгена пробы и защиты, которые приходят извне.
        ///
        /// ⛔ Различать их обязательно, и по флагу, а не по имени: имя у
        /// сведённого образа собирается из вещества (`Xray-NaI`), а у
        /// поэлементного запасного пути — из символа (`Xray-I`), и правило,
        /// узнающее образ по имени, однажды промолчит. Читатель флага один —
        /// гейт <see cref="FsaAnalyzer.CrystalXrayGate"/>, снимающий эту
        /// колонку при живой матрице отклика.
        /// </summary>
        public bool FromCrystal { get; set; }

        /// <summary>
        /// (`S172`) Образ K-ВЫЛЕТА САМОГО КРИСТАЛЛА (`Esc-<вещество>`): гребёнка
        /// на `E − Kα` от каждой линии состава, построенная по кривой в
        /// <c>FsaSampleLibrary.AddEscape</c>. Пара к <see cref="FromCrystal"/>:
        /// тот — собственный рентген кристалла, этот — его вылет; оба —
        /// образы ПРИБОРА, а не пробы.
        ///
        /// ⛔ Признак — флаг, а не имя, по той же причине (имя собирается из
        /// вещества: `Esc-CsI`, `Esc-NaI`). Читатель флага один — гейт
        /// <see cref="FsaAnalyzer.EscapeGate"/>: при живой матрице отклика
        /// вылет уже внутри образов нуклидов (канал `EscapeXray`), и свободная
        /// колонка рядом — второй счёт того же и сток невязки (решение Amber
        /// 14.09.2026 «При матрице вылет не класть»). Без матрицы образ —
        /// единственное, чем вылет выражен, и гейт его не трогает.
        /// </summary>
        public bool CrystalEscape { get; set; }

        /// <summary>
        /// (`AMBER8`) Имя КОЛОНКИ-РОДИТЕЛЯ у образа одиночного или двойного
        /// вылета (`SE-*`, `DE-*`); у прочих образов пусто.
        ///
        /// ⛔ Зачем. Пик вылета — не самостоятельная линия, а ДОЛЯ фотопика
        /// родителя: без родительского кванта рождаться нечему. Свободная
        /// амплитуда у такого образа держится только на том, что фиту дали
        /// гауссиану в удобном месте, и, оставшись без родителя, она садится
        /// на первую подходящую структуру. Измерено 08.09.2026 на
        /// `Cs 137 в домике 24.11.2022` (задача Amber `AMBER8`, её слова: «Это
        /// явная ошибка — DE-1461. Это просто катастрофа»): `K-40` отсеян по
        /// значимости (z = 0.00), `SE-1461` тоже (z = 0.00), а `DE-1461` выжил
        /// с z = 64.34 и забрал 5.156 % спектра, сев на 439 кэВ — в область
        /// комптоновского края цезия.
        /// </summary>
        public string EscapeParent { get; set; }

        /// <summary>
        /// (`AMBER311`, полоса `a311f`) Энергия РОДИТЕЛЬСКОЙ ЛИНИИ образа вылета, кэВ: по ней фит находит в
        /// колонке родителя тот пик полного поглощения, долей которого образ является. NaN — не образ вылета.
        /// </summary>
        public double EscapeParentEnergy { get; set; } = double.NaN;

        /// <summary>
        /// (`AMBER311`, полосы `a311f`/`a311g`) ВЕРХНЯЯ ГРАНИЦА СУММЫ площадей образов вылета родительской линии
        /// (SE + DE) в долях площади пика полного поглощения этой линии: σ_пар/σ_фото вещества кристалла на энергии родителя
        /// (<c>FsaLibrary.PairToPhotoRatio</c>, довод там же). NaN — вещество кристалла неизвестно, границы нет.
        /// Читает её фит при рычаге <c>FsaAnalyzer.EscapeBoundToParent</c>; без рычага поле не читается.
        /// </summary>
        public double EscapeShareCap { get; set; } = double.NaN;

        /// <summary>
        /// Готовый образ по каналам, если он не строится из линий вовсе.
        /// Заведено для случайных наложений (pile-up): их форма — автосвёртка
        /// САМОГО спектра, линий у неё нет.
        ///
        /// Такой образ НЕ двигается сеткой дрейфа, и это не упущение: он выведен
        /// из измеренного спектра и уже стоит в его шкале. Двигать его вслед за
        /// библиотечными образами значило бы сдвинуть спектр относительно
        /// самого себя.
        /// </summary>
        public double[] FixedTemplate { get; set; }

        /// <summary>
        /// (`AMBER123`, П166 28.09.2026) ВЕРХНЯЯ ГРАНИЦА АМПЛИТУДЫ колонки в её
        /// собственных единицах; NaN — границы нет (умолчание: всякая колонка
        /// прежняя, побитово). Ставится колонке наложений — число пар не может
        /// превышать N·R·τ_d (окно наложения не длиннее мёртвого времени на
        /// импульс, посылка `AMBER76`). Фит, получив амплитуду выше границы,
        /// закрепляет её на границе и решает остальное заново
        /// (<c>FsaAnalyzer.FitOnce</c>).
        /// </summary>
        public double AmplitudeCap { get; set; } = double.NaN;

        /// <summary>
        /// Суммарный выход ВСЕХ излучений нуклида (γ и X) на его СОБСТВЕННЫЙ
        /// распад, % (`S69`). NaN — неизвестен, и тогда правило отсева не
        /// применяется вовсе.
        ///
        /// ⛔ Это АПРИОРНОЕ свойство нуклида, а не образа. Считать его по
        /// <see cref="Lines"/> нельзя по двум причинам сразу: у члена ряда веса
        /// линий домножены на долю ветвления от корня, а из образа выброшено
        /// всё, что не попало в рабочее окно прибора, — тогда как случай
        /// «линии есть и выход приличный, но они ВНЕ шкалы прибора» (Ra-228,
        /// 13.52 и 16.2 кэВ) обязан отличаться от «излучения нет вовсе»
        /// (Po-212) и от «выход пренебрежим» (Po-216, 0.0019 %).
        ///
        /// ⚠ Заполняется только сборкой из баз
        /// (<see cref="FsaSampleLibrary"/>): у <see cref="FsaLibrary"/> линии
        /// приходят из пользовательского конфига и встроенной таблицы, их сумма
        /// — итог КОНФИГА, а не нуклида, и выдавать её за априорное свойство
        /// значило бы соврать тихо.
        /// </summary>
        public double TotalYieldPercent { get; set; } = double.NaN;

        /// <summary>
        /// (`A169`) ПРОИСХОЖДЕНИЕ В РЯДУ: имя корня ряда распада, членом
        /// которого этот образ пришёл в состав; null — одиночный нуклид или
        /// приборный образ. Заполняется сборкой из баз
        /// (<see cref="FsaSampleLibrary"/>) у КАЖДОГО излучающего члена ряда с
        /// двумя и более такими членами — и при связке равновесием, и без неё.
        ///
        /// ⛔ Не путать с признаком связанной амплитуды
        /// (<see cref="FsaComponentResult.ChainRoot"/>): тот стоит только у
        /// строк, амплитуда которых закреплена связкой ряда, а этот — у всех
        /// членов независимо от того, как их амплитуды подбирались. По этому
        /// полю группируют строки в родителей без пересчёта (`A145`); по тому —
        /// говорят, своя амплитуда у строки или общая.
        /// </summary>
        public string DecayChainRoot { get; set; }

        /// <summary>
        /// (`S171`) ЧЛЕНЫ РЯДА, ПРИВЯЗАННЫЕ К ЭТОЙ КОЛОНКЕ: их линии слиты в
        /// <see cref="Lines"/> под своими именами (<see cref="FsaLine.Nuclide"/>),
        /// и колонка у фита ОДНА — как у ряда при связке, но только для этой
        /// связки членов. null — привязанных нет. Заполняет гейт вырожденности
        /// анализатора; читатели — почленный разбор колонки в отчёт и пределы.
        /// Имя колонки остаётся именем хозяина.
        /// </summary>
        public List<FsaTie> Ties { get; set; }
    }
}
