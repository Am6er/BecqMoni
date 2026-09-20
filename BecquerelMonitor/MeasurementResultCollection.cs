using System.Collections.Generic;

namespace BecquerelMonitor
{
    // Token: 0x020000BD RID: 189
    public class MeasurementResultCollection
    {
        // Token: 0x17000288 RID: 648
        // (get) Token: 0x06000925 RID: 2341 RVA: 0x000356B8 File Offset: 0x000338B8
        // (set) Token: 0x06000926 RID: 2342 RVA: 0x000356C0 File Offset: 0x000338C0
        public ResultData ResultData
        {
            get
            {
                return this.resultData;
            }
            set
            {
                this.resultData = value;
            }
        }

        // Token: 0x17000289 RID: 649
        // (get) Token: 0x06000927 RID: 2343 RVA: 0x000356CC File Offset: 0x000338CC
        // (set) Token: 0x06000928 RID: 2344 RVA: 0x000356D4 File Offset: 0x000338D4
        public ROIConfigData ROIConfig
        {
            get
            {
                return this.roiConfig;
            }
            set
            {
                this.roiConfig = value;
            }
        }

        // Token: 0x1700028A RID: 650
        // (get) Token: 0x06000929 RID: 2345 RVA: 0x000356E0 File Offset: 0x000338E0
        // (set) Token: 0x0600092A RID: 2346 RVA: 0x000356E8 File Offset: 0x000338E8
        /// <summary>
        /// ПОЛНОЕ время измерения спектра, с — подпись, а не знаменатель.
        /// (`AMBER35`, 15.09.2026) Знаменатель скорости у зон —
        /// <see cref="CountingTime"/>; ставится оно из
        /// <c>MeasurementResultManager.Calculate</c> вместе с <see cref="LiveTime"/>.
        /// Коллекция нигде не показывается и не сохраняется
        /// (<c>ResultData.MeasurementResultCollection</c> под <c>[XmlIgnore]</c>),
        /// так что подпись здесь — на будущее и для проб.
        /// </summary>
        public double MeasurementTime
        {
            get
            {
                return this.measurementTime;
            }
            set
            {
                this.measurementTime = value;
            }
        }

        /// <summary>
        /// (`AMBER35`, 15.09.2026) Живое время спектра, с; 0 — не задано.
        /// Переносится сюда из <c>EnergySpectrum.LiveTime</c> в
        /// <c>MeasurementResultManager.Calculate</c>.
        /// </summary>
        public double LiveTime
        {
            get
            {
                return this.liveTime;
            }
            set
            {
                this.liveTime = value;
            }
        }

        /// <summary>
        /// (`AMBER35`, 15.09.2026) ЗНАМЕНАТЕЛЬ СКОРОСТИ зон — живое, если задано,
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
                return Utils.LiveTime.Effective(this.liveTime, this.measurementTime);
            }
        }

        // Token: 0x1700028B RID: 651
        // (get) Token: 0x0600092B RID: 2347 RVA: 0x000356F4 File Offset: 0x000338F4
        // (set) Token: 0x0600092C RID: 2348 RVA: 0x000356FC File Offset: 0x000338FC
        public List<MeasurementResult> ResultList
        {
            get
            {
                return this.resultList;
            }
            set
            {
                this.resultList = value;
            }
        }

        // Token: 0x0400050E RID: 1294
        ResultData resultData;

        // Token: 0x0400050F RID: 1295
        ROIConfigData roiConfig;

        // Token: 0x04000510 RID: 1296
        double measurementTime;

        double liveTime;

        // Token: 0x04000511 RID: 1297
        List<MeasurementResult> resultList = new List<MeasurementResult>();
    }
}
