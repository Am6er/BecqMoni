using System.Collections.Generic;

namespace BecquerelMonitor
{
    public class MeasurementResultCollection
    {
        public ResultData ResultData { get; set; }

        /// <summary>
        /// Чья коллекция — ключ источника строк (у разбора FSA —
        /// <c>FsaMeasurementResult.ConfigGuid</c>); окно результата сравнивает
        /// по нему, та же ли коллекция пришла, и перестраивает таблицу при смене.
        /// До 07.10.2026 здесь лежал Guid конфигурации ROI.
        /// </summary>
        public string SourceKey { get; set; }

        /// <summary>
        /// ПОЛНОЕ время измерения спектра, с — подпись, а не знаменатель.
        /// (`AMBER35`, 15.09.2026) Знаменатель скорости — <see cref="CountingTime"/>.
        /// </summary>
        public double MeasurementTime { get; set; }

        /// <summary>(`AMBER35`, 15.09.2026) Живое время спектра, с; 0 — не задано.</summary>
        public double LiveTime { get; set; }

        /// <summary>
        /// (`AMBER35`, 15.09.2026) ЗНАМЕНАТЕЛЬ СКОРОСТИ — живое, если задано,
        /// иначе полное: то же правило <see cref="Utils.LiveTime.Effective"/>, что
        /// у <c>EnergySpectrum.EffectiveLiveTime</c>. На него делят
        /// <c>Translate</c> (имп/с, Бк, Бк/кг, Бк/л) и <c>Correct</c>.
        /// ⚠ Коллекция, у которой задано ТОЛЬКО <see cref="MeasurementTime"/>
        /// (так её собирают пробы), считает по нему — побитово как прежде.
        /// </summary>
        public double CountingTime
        {
            get
            {
                return Utils.LiveTime.Effective(this.LiveTime, this.MeasurementTime);
            }
        }

        public List<MeasurementResult> ResultList { get; set; } = new List<MeasurementResult>();
    }
}
