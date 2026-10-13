namespace BecquerelMonitor
{
    /// <summary>
    /// Строка окна результата: нуклид разбора FSA (<see cref="Line"/>) и его
    /// число в выбранных единицах. Приписки суммирования и помехи спутника
    /// принадлежали пути зон ROI и сняты вместе с ним (`AMBER208`, 07.10.2026):
    /// у разбора суммирование и помехи учтены в самой модели.
    /// </summary>
    public class MeasurementResult
    {
        public MeasurementLine Line { get; set; }

        public double ResultValue { get; set; }

        public double ResultError { get; set; }

        public double MDA { get; set; }

        public bool IsValid { get; set; } = true;

        /// <summary>
        /// Почему результата нет, когда <see cref="IsValid"/> = false.
        /// Пусто — показывается общее «Ошибка». Нужен, чтобы «нет K» в
        /// таблице отличалось и от нуля беккерелей, и от ошибки счёта:
        /// раньше зона без коэффициента молча печатала 0 Бк (TODO G7).
        /// </summary>
        public string StatusText { get; set; }

        public MeasurementResult(MeasurementLine line, double resultValue, double resultError)
        {
            this.Line = line;
            this.ResultValue = resultValue;
            this.ResultError = resultError;
        }

        public MeasurementResult(MeasurementLine line, double resultValue, double resultError, double mda)
        {
            this.Line = line;
            this.ResultValue = resultValue;
            this.ResultError = resultError;
            this.MDA = mda;
        }
    }
}
