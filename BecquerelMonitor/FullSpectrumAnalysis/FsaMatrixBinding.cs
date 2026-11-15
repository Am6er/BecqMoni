using BecquerelMonitor.EfficiencyMaker;

namespace BecquerelMonitor.FullSpectrumAnalysis
{
    /// <summary>
    /// Привязка матрицы отклика к разбору — ОДНИМ движением и ОДНИМ местом
    /// (`AMBER12`, 09.09.2026).
    ///
    /// ⛔ Зачем отдельный класс. Мест, где матрица кладётся в анализатор, было
    /// два физически: экран (<see cref="FsaAnalysisSession"/>) и каждая проба,
    /// собирающая <see cref="FsaAnalyzer"/> руками. Пока рядом с матрицей стояло
    /// одно вещество кристалла, расхождение было терпимым; с признаком защиты
    /// оно стало ложью: правка доехала до экрана и НЕ доехала до стенда — снимок
    /// «до» и «после» вышел побитово одинаковым, и проверить правку было нечем.
    /// Это ровно грабли «ключ, не доехавший до построителя, мёртв»: приёмка
    /// показывает согласие там, где на деле измеряли одно и то же.
    ///
    /// Поэтому решение живёт здесь, а обе стороны его СПРАШИВАЮТ.
    /// </summary>
    public static class FsaMatrixBinding
    {
        /// <summary>
        /// Положить матрицу в анализатор вместе со всем, что идёт с нею:
        ///
        ///   * вещество кристалла — им каскадное суммирование ставит сумм-пики
        ///     по сумме СВЕТА (`S20`), и без матрицы суммирования нет вовсе;
        ///   * обратное рассеяние в ОБСТАНОВКЕ (`AMBER12`) — образ при матрице
        ///     погашен (`A83`), потому что внутриприборное рассеяние она считает
        ///     сама; но свинцовый домик, стол и стены в её сцену не входят ни
        ///     при какой <c>GeometrySceneKind</c>, и там, где человек отметил
        ///     защиту, образ возвращается.
        ///
        /// Матрица <c>null</c> — не делается НИЧЕГО: разбор идёт старым путём,
        /// где образ рассеяния строится и так.
        /// </summary>
        public static void Bind(FsaAnalyzer analyzer, GeometryModel geometry, ResponseMatrix matrix)
        {
            Bind(analyzer, geometry, matrix, null);
        }

        /// <summary>
        /// (`AMBER274` Э3, полоса `a274f` 14.11.2026) То же для ПРИБОРА <paramref name="efficiency"/> — его геометрия и
        /// его поля слоя кристалла: источник QF альф и форма групп (раздел слоя сверяется с рецептом прибора,
        /// <see cref="EfficiencyMaker.ResponseMatrix.CrystalSectionFor(GeometryModel, EfficiencyConfigData, out EfficiencyMaker.CrystalSectionRefusal, out string)"/>)
        /// и активность ряда с датой — запасной путь счёта по альфам (<see cref="FsaAnalyzer.CrystalOwnSeriesFallback"/>).
        /// У прибора без этих полей — ровно <see cref="Bind(FsaAnalyzer, GeometryModel, ResponseMatrix)"/>. Имя своё, а не
        /// перегрузка: пробы зовут <c>Bind(analyzer, null, matrix)</c>, и перегрузка сделала бы такой вызов неоднозначным.
        /// </summary>
        public static void BindDevice(FsaAnalyzer analyzer, EfficiencyConfigData efficiency, ResponseMatrix matrix)
        {
            Bind(analyzer, efficiency != null ? efficiency.Geometry : null, matrix, efficiency);
        }

        static void Bind(FsaAnalyzer analyzer, GeometryModel geometry, ResponseMatrix matrix, EfficiencyConfigData efficiency)
        {
            if (analyzer == null || matrix == null)
            {
                return;
            }

            analyzer.ResponseMatrix = matrix;
            analyzer.ScintillatorMaterial = EfficiencySimulator.ScintillatorNameOf(geometry);
            // (`AMBER282`) Защита СЛОЯМИ входит в сцену переноса — обратное рассеяние от неё уже в
            // матрице, и формула поверх неё посчитала бы его дважды. Признак без слоёв — как прежде.
            analyzer.BackscatterWithMatrix = geometry != null && geometry.InShield && !geometry.ShieldInScene;
            // (`N14`, П49; `AMBER46`, П87 16.09.2026) Таблица Q_k(E) сцены —
            // ИЗ САМОЙ МАТРИЦЫ (блок `ANGK` формата 9, построена из тех же
            // историй, что её строки). Сайдкар `*.qk` по отпечатку геометрии
            // снят по постановке Amber 16.09.2026 («Никаких сайдкаров. Стоп.»):
            // у матрицы, которую строит приложение, таблица есть всегда, и
            // «ключ ВКЛ, а таблицы нет» (`A311`) больше невозможно. null —
            // только у матрицы, собранной руками (пробы); состояние видно
            // пробам (`AngularQk`).
            analyzer.AngularQk = matrix.AngularQk;

            // ⛔ (`AMBER274`, `Д2` критика 30.10.2026) РАЗДЕЛ ЭЛЕКТРОНА — СВЕРКОЙ С ЭТОЙ
            // СЦЕНОЙ: собственное клеймо раздела (физика, матрица, шаг, капсула) против
            // геометрии разбора. Фотонное клеймо капсулу не видит, и без этой сверки раздел
            // другой капсулы брался молча. Отказ — причиной, которую печатает отчёт FSA.
            EfficiencyMaker.ElectronSectionRefusal refusal = EfficiencyMaker.ElectronSectionRefusal.Missing;
            EfficiencyMaker.ResponseElectronSection section = geometry != null
                ? matrix.ElectronSectionFor(geometry, out refusal)
                : null;
            if (geometry == null)
            {
                refusal = matrix.ElectronSection != null
                    ? EfficiencyMaker.ElectronSectionRefusal.NoScene
                    : EfficiencyMaker.ElectronSectionRefusal.Missing;
            }

            // ⛔ (`a305m` 15.11.2026) Сцене изотропного поля раздел не положен: у матрицы БЕЗ раздела
            // `ElectronSectionFor` отвечает `Missing` раньше сцены, и отчёт принял бы это за «пересчитайте
            // матрицу». Причина сцены — `IsoScene`, как у матрицы с разделом.
            if (geometry != null && geometry.Scene == GeometrySceneKind.Iso)
            {
                refusal = EfficiencyMaker.ElectronSectionRefusal.IsoScene;
            }

            analyzer.SampleBetaSection = section;
            analyzer.SampleBetaRefusal = refusal;

            // (`AMBER274`, Э2) РАЗДЕЛ СОБСТВЕННОЙ АКТИВНОСТИ КРИСТАЛЛА — той же сверкой
            // клейма со сценой: гистограмма чужого кристалла в разбор не ложится. Раздел
            // необязательный: нет его — разбор побитово прежний.
            // (`a274m` 14.11.2026) Решение то же (`UsableFor`), но с ПРИЧИНОЙ: негодный раздел у кристалла,
            // которому слой положен, — отказ, и разбор с включённым слоем его называет
            // (`FsaAnalyzer.CrystalOwnRefusal`), а не идёт молча без слоя.
            EfficiencyMaker.CrystalSectionRefusal ownRefusal;
            string ownDetail;
            // (`a274f` 14.11.2026) рецепт слоя — ПРИБОРА (источник QF альф и форма групп — его поля); прибора нет —
            // рецепт умолчания, как прежде
            analyzer.CrystalOwnSection = matrix.CrystalSectionFor(geometry, efficiency, out ownRefusal, out ownDetail);
            analyzer.CrystalOwnRefusal = ownRefusal;
            analyzer.CrystalOwnRefusalDetail = ownDetail;

            // (`a274f`) запасной путь счёта ряда по альфам — активность ряда кристалла из поля прибора
            CrystalSeriesActivity series = efficiency != null ? efficiency.CrystalSeriesActivity : null;
            analyzer.CrystalOwnSeriesFallback = series != null && series.ActivityBq > 0.0
                ? new CrystalOwnSeriesFallback { ActivityBq = series.ActivityBq, Date = series.Date }
                : null;
        }
    }
}
