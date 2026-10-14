using System.Xml.Serialization;

namespace BecquerelMonitor
{
    // Token: 0x0200003E RID: 62
    public abstract class InputDeviceConfig
    {
        // Token: 0x06000378 RID: 888
        public abstract InputDeviceConfig Clone();

        public abstract double DeadTime();

        /// <summary>
        /// (П293, 14.10.2026) У прибора есть поля нижнего и верхнего порога, которые
        /// пункты «порог от курсора» контекстного меню спектра вписывают в окне настроек.
        ///
        /// ⛔ КОНТРАКТ: <c>true</c> ровно тогда, когда страница прибора
        /// (<see cref="InputDeviceForm"/> из <c>DeviceType.DeviceConfigFormType</c>)
        /// отдаёт ОБА поля — <c>LowerThresholdTextBox</c> и <c>UpperThresholdTextBox</c>.
        /// Щелчок по пункту идёт в <c>DeviceConfigForm.SetLowerThreshold</c>, и тот при
        /// пустом поле молча ничего не делает: окно настроек открывается, порог не
        /// ставится. По решению Amber 14.10.2026 («Гасить пункт у таких приборов») пункт у
        /// прибора без полей недоступен — его гасит <c>DocEnergySpectrum.contextMenuStrip1_Opening</c>
        /// по этому признаку. Согласие признака со страницей каждого зарегистрированного
        /// типа проверяет проба <c>ThresholdMenuProbeP293</c>.
        ///
        /// Признак — у конфигурации, а не у страницы: меню знает лишь конфигурацию
        /// документа, а страница живёт только внутри открытого окна настроек, и строить
        /// её ради вопроса значило бы создавать элементы управления на каждом открытии
        /// меню. Умолчание <c>false</c> нарочно: новый прибор без своего ответа получает
        /// ПОГАШЕННЫЙ пункт — это видно, а не открытое окно, где ничего не произошло.
        ///
        /// Только для чтения — в файл конфигурации не пишется.
        /// </summary>
        [XmlIgnore]
        public virtual bool HasThresholdFields
        {
            get { return false; }
        }
    }
}
