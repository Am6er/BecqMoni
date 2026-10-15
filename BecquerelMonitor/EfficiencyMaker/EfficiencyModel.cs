using System.Collections.Generic;

namespace BecquerelMonitor.EfficiencyMaker
{
    /// <summary>
    /// Откуда взят абсолютный уровень кривой.
    ///
    /// С 13.09.2026 (`AMBER25`, решение Amber) кривую даёт только расчёт из
    /// геометрии: значения `Reference` / `Anchor` / `ShapeOnly` эмпирического
    /// восстановления по спектрам сняты вместе с самим фитом. `None` остаётся
    /// исходным состоянием пустого контейнера (у отказа расчёта).
    /// </summary>
    public enum EfficiencyLevelSource
    {
        None,
        /// <summary>Посчитана из геометрии: уровень абсолютный, не подогнанный.</summary>
        Simulation
    }

    /// <summary>
    /// Контейнер кривой, посчитанной из геометрии (`EfficiencyCalculation.Run`).
    /// Имя историческое — так его читают `DoseRate`, `EfficiencyCurveGraph`,
    /// пробы и сторож `tools/check_matrix_keys.py`; переименовывать не стали,
    /// чтобы не ронять читателей (`AMBER25`).
    /// </summary>
    public sealed class EfficiencyFitResult
    {
        public EfficiencyLevelSource LevelSource = EfficiencyLevelSource.None;

        public double MinEnergy;

        public double MaxEnergy;

        public List<ROIEfficiencyData> Curve = new List<ROIEfficiencyData>();

        /// <summary>
        /// Клеймо «чем посчитана» кривой из геометрии (E12): версия физики
        /// переноса, историй на узел, сетка.
        /// </summary>
        public string ComputeStamp = "";

        /// <summary>
        /// (`AMBER185`) Отпечаток геометрии, ДЛЯ КОТОРОЙ кривая посчитана
        /// (<see cref="ResponseMatrix.GeometryFingerprint"/>), снятый в начале
        /// счёта. Уезжает в конфигурацию вместе с кривой; расхождение с
        /// геометрией конфигурации — «кривая посчитана для другой геометрии».
        /// </summary>
        public string GeometryFingerprint = "";

        /// <summary>
        /// (`AMBER226`, П330) Откуда кривая окна конструктора: расчёт из
        /// геометрии либо импорт файла ЛСРМ («Импорт из LSRM…» редактора
        /// геометрии, решение Amber 15.10.2026 «Кривая окна»). «Сохранить»
        /// кладёт это происхождение в конфигурацию.
        /// </summary>
        public BecquerelMonitor.EfficiencyOrigin Origin = BecquerelMonitor.EfficiencyOrigin.Simulation;

        /// <summary>
        /// (`AMBER226`) Заметка импорта — файл, геометрия, что взято кривой; при
        /// «Сохранить» уходит в <c>EfficiencyConfigData.Note</c>. Пусто у расчёта.
        /// </summary>
        public string ImportNote = "";

        public string Error;

        public bool Ok
        {
            get { return string.IsNullOrEmpty(this.Error) && this.Curve.Count >= 2; }
        }
    }
}
