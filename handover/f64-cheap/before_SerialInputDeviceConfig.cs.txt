using System;

namespace BecquerelMonitor
{
    // Token: 0x020000FB RID: 251
    /// <summary>
    /// Вариант <see cref="InputDeviceConfig"/> без собственных полей: узел
    /// объявлен одним из <c>[XmlElement]</c> в <see cref="DeviceConfigInfo"/>
    /// (строка 221), то есть конфигурация с ним читается штатно, а прибора,
    /// который бы её писал, в <c>DeviceType</c> нет. Ни один конфиг дерева —
    /// поставочный, корпусный, пользовательский — такого узла не содержит
    /// (проверено сплошным поиском 18.08.2026, TODO T48).
    ///
    /// ⚠ Оба метода бросали <see cref="NotImplementedException"/>, и родословная
    /// у них РАЗНАЯ: у <c>Clone()</c> сохранён токен декомпилятора, то есть
    /// бросок пришёл из апстримной сборки, а <c>DeadTime()</c> дописан в этом
    /// форке коммитом <c>dec4d046</c> «Add. LiveTime:» от 06.09.2024 — половина
    /// мины своя. Зовут их БЕЗ защиты:
    /// <c>CountsRateManager.cs:70</c> и <c>:73</c>, <c>DCControlPanel.cs:909</c>,
    /// <c>MainForm.cs:2868</c>, <c>MeasurementController.cs:340</c>,
    /// <c>SelectDeviceDialog.cs:35</c> — для <c>DeadTime()</c>, и
    /// <c>DeviceConfigInfo.cs:465</c> (конструктор копирования) — для
    /// <c>Clone()</c>. ⚠ Хуже как раз второе, и в строке T48 его не было:
    /// <c>Clone()</c> рвётся РАНЬШЕ и в более горячей точке — конструктор копии
    /// зовут <c>DeviceConfigManager.cs:160</c> (дублирование конфигурации) и
    /// <c>:228</c> (сохранение), плюс <c>DeviceConfigInfo.Clone()</c> на
    /// <c>:565</c>. Обратный ход того же наблюдения — довод, что таких файлов и
    /// не существует: апстримный <c>Clone()</c> бросал ВСЕГДА, значит установка,
    /// где такой узел когда-либо записали, рухнула бы при первом же сохранении.
    /// Узел объявлен читаемым, значит и вести себя обязан как
    /// читаемый: <c>[XmlElement]</c> НЕ снят нарочно — снятие сломало бы чтение
    /// файла, который такой узел всё же содержит, а это тише, чем ноль.
    /// </summary>
    public class SerialInputDeviceConfig : InputDeviceConfig
    {
        // Token: 0x06000C2A RID: 3114 RVA: 0x000487AC File Offset: 0x000469AC
        /// <summary>
        /// Полей у класса нет — пустой экземпляр и есть полная копия.
        /// </summary>
        public override InputDeviceConfig Clone()
        {
            return new SerialInputDeviceConfig();
        }

        /// <summary>
        /// Ноль — «мёртвое время не известно», по образцу остальных вариантов
        /// (<see cref="AtomSpectraDeviceConfig"/> отдаёт своё поле,
        /// <see cref="ObsidianDeviceConfig"/> и <see cref="RadiaCodeDeviceConfig"/>
        /// — константу 5 мкс). Читатели этого нуля уже есть и он им понятен:
        /// счётчик загрузки делит на <c>1 − τ·n</c> только при τ &gt; 0
        /// (<c>CountsRateManager.cs:70</c>), а каскадный суммирователь берёт при
        /// нуле своё умолчание окна совпадения (<c>FsaOverlay.DeadTimeOf</c>, S27).
        /// </summary>
        public override double DeadTime()
        {
            return 0.0;
        }
    }
}
