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

            analyzer.SampleBetaSection = section;
            analyzer.SampleBetaRefusal = refusal;

            // (`AMBER274`, Э2) РАЗДЕЛ СОБСТВЕННОЙ АКТИВНОСТИ КРИСТАЛЛА — той же сверкой
            // клейма со сценой: гистограмма чужого кристалла в разбор не ложится. Раздел
            // необязательный: нет его — разбор побитово прежний.
            EfficiencyMaker.ResponseCrystalSection own = matrix.CrystalSection;
            analyzer.CrystalOwnSection = own != null && own.Count > 0 && geometry != null
                                         && own.UsableFor(geometry, matrix.BinKev)
                ? own
                : null;
        }
    }
}
