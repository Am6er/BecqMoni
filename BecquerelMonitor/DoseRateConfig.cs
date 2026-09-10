using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;

namespace BecquerelMonitor
{
    // Token: 0x0200001C RID: 28
    public class DoseRateConfig
    {

        // Token: 0x060000EE RID: 238 RVA: 0x00005450 File Offset: 0x00003650
        public DoseRateConfig()
        {
        }

        // Token: 0x060000EF RID: 239 RVA: 0x00005468 File Offset: 0x00003668
        public DoseRateConfig(DoseRateConfig config)
        {
            this.doseRateCalibrationPoints = config.DoseRateCalibrationPoints;
        }

        // Token: 0x060000F0 RID: 240 RVA: 0x000054A4 File Offset: 0x000036A4
        public DoseRateConfig Clone()
        {
            return new DoseRateConfig(this);
        }

        /// <summary>
        /// ⛔ ЛЕГАСИ. Ручные точки калибровки мощности дозы — решение Amber
        /// 10.09.2026 (`AMBER13`), дословно: «Убрать в коде работу с этими
        /// точками. При пересохранении конфига этот рудимент исчезнет.
        /// Поставочеый конфиг не трогать и не обращать внимания, что они там
        /// есть. Это legacy.»
        ///
        /// Отсюда `[XmlIgnore]`, а не удаление свойства. Что это даёт ровно:
        ///
        ///  * конфигурация, лежащая на диске с `&lt;DoseRateCalibrationPoints&gt;`,
        ///    читается БЕЗ них — незнакомый элемент `XmlSerializer` пропускает
        ///    молча, и список остаётся пустым;
        ///  * при первом же пересохранении элемент не пишется вовсе, то есть
        ///    рудимент исчезает сам, как и сказано;
        ///  * поставочные `config/device/*.xml` при этом НЕ ТРОГАЮТСЯ (приказ
        ///    05.09.2026): 36 точек `RC-103.xml` остаются на месте, их просто
        ///    перестают читать.
        ///
        /// ⚠ ЦЕНА, названная Amber заранее и принятая ею: `MainForm.ShowDoseRate`
        /// показывает дозу только при непустом списке, значит показание
        /// пропадает у всех конфигураций, которые его сегодня имеют, — пока
        /// замена расчёта (пункты (а) и (б) строки `AMBER13`) не готова.
        ///
        /// ⛔ Свойство САМО не снято потому, что оно — вход будущего расчёта:
        /// генератор `DoseRateEstimator.Estimate` по-прежнему возвращает эти
        /// точки, а `DoseRateManager.Calculate` по-прежнему умеет по ним
        /// считать; пункт (а) переводит обе стороны с пиковой эффективности на
        /// полную, а не выбрасывает их.
        /// </summary>
        [XmlIgnore]
        public List<DoseRateCalibrationPoint> DoseRateCalibrationPoints
        {
            get
            {
                return this.doseRateCalibrationPoints;
            }
            set
            {
                // ⚠ `value == null` прежде валило сеттер `NullReferenceException`
                // на `value.Sort()`. Живые пути присваивали непустой список, но
                // после `[XmlIgnore]` присваивают сюда только пробы.
                this.doseRateCalibrationPoints = value ?? new List<DoseRateCalibrationPoint>();
                this.doseRateCalibrationPoints.Sort();
            }
        }

        List<DoseRateCalibrationPoint> doseRateCalibrationPoints = new List<DoseRateCalibrationPoint>();
    }
}
