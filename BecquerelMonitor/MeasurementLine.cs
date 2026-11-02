namespace BecquerelMonitor
{
    /// <summary>
    /// Строка результата измерения — нуклид или ряд разбора FSA (`AMBER208`,
    /// 07.10.2026). Пришла на место <c>ROIDefinitionData</c>, когда зоны ROI
    /// сняты из приложения по решению Amber «максимально почистить остатки»:
    /// <see cref="MeasurementResultManager.Translate"/> читает отсюда K,
    /// <see cref="MeasurementResultManager.Correct"/> — период полураспада.
    /// </summary>
    public sealed class MeasurementLine
    {
        /// <summary>Имя родителя (корень ряда или одиночный нуклид) — так, как в сете.</summary>
        public string Name { get; set; }

        /// <summary>
        /// Период полураспада, лет (год = 365 сут, как в
        /// <see cref="NuclideDefinition"/>); ≤ 0 — поправки на распад нет.
        /// </summary>
        public double HalfLifeYears { get; set; }

        /// <summary>
        /// K — беккерели на имп/с. У разбора амплитуда уже в распадах в секунду
        /// в шкале кривой или матрицы: 1 при абсолютной шкале, 0 — беккерели
        /// скрыты («нет коэффициента»).
        /// </summary>
        public double Coefficient { get; set; }

        public double CoefficientError { get; set; }

        /// <summary>
        /// (`AMBER274`, Ф14) Пометка строки при имени — «по сплошному спектру» у чистого
        /// бета-излучателя; пусто — пометки нет. Имя (<see cref="Name"/>) остаётся именем
        /// нуклида: по нему ищется период, подсвечивается слой графика и переносится выбор.
        /// </summary>
        public string Note { get; set; }
    }
}
