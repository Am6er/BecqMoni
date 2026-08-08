using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using BecquerelMonitor.EfficiencyMaker;

namespace BecquerelMonitor
{
    /// <summary>Откуда взялась кривая. Влияет на то, что с ней можно делать.</summary>
    public enum EfficiencyOrigin
    {
        /// <summary>Посчитана из геометрии монте-карловским переносом.</summary>
        Simulation,

        /// <summary>Импортирована из пары файлов LSRM: `.in` + экспорт кривой.</summary>
        Lsrm,

        /// <summary>Восстановлена по измеренным спектрам (форма — из равновесия).</summary>
        Measurement,

        /// <summary>Введена руками или взята из чужого файла.</summary>
        Manual
    }

    /// <summary>
    /// Кривая эффективности регистрации вместе с геометрией, в которой она
    /// получена. Живёт в конфигурации устройства: кривая привязана к ПРИБОРУ И
    /// ГЕОМЕТРИИ, а не к набору зон, — эффективность полного поглощения зависит
    /// от телесного угла и самопоглощения в пробе, и один и тот же кристалл в
    /// маринелли и с точечным источником даёт разные кривые. Раньше она лежала
    /// секцией в ROI-конфиге, где ей было не место: ROI — это набор окон.
    ///
    /// Структура САМОДОСТАТОЧНА: чтобы открыть её в конструкторе кривой, не
    /// нужны ни файл геометрии `.in`, ни файл кривой. Это нужно не для красоты
    /// — файл спектра несёт копию активной конфигурации и уходит другому
    /// пользователю, у которого этого прибора нет вовсе; он обязан получить всё
    /// то же самое.
    /// </summary>
    public class EfficiencyConfigData
    {
        public string Guid
        {
            get { return this.guid; }
            set { this.guid = value; }
        }

        public string Name
        {
            get { return this.name; }
            set { this.name = value; }
        }

        public DateTime LastUpdated
        {
            get { return this.lastUpdated; }
            set { this.lastUpdated = value; }
        }

        public EfficiencyOrigin Origin
        {
            get { return this.origin; }
            set { this.origin = value; }
        }

        /// <summary>
        /// Точки кривой по возрастанию энергии: энергия, эффективность долей,
        /// погрешность в процентах. Тип тот же, что был в ROI, — числа и их
        /// смысл переезд не меняет.
        /// </summary>
        public List<ROIEfficiencyData> Curve
        {
            get { return this.curve; }
            set { this.curve = value; }
        }

        /// <summary>
        /// Геометрия, в которой кривая получена. Может отсутствовать: кривая,
        /// восстановленная по спектрам, геометрии не имеет и пересчитана быть
        /// не может — но пользоваться ею можно, для активности нужна только
        /// эффективность.
        ///
        /// Размеры — в МИЛЛИМЕТРАХ, как и везде в <see cref="GeometryModel"/>.
        /// </summary>
        public GeometryModel Geometry
        {
            get { return this.geometry; }
            set { this.geometry = value; }
        }

        public CDATA Note
        {
            get { return this.note; }
            set { this.note = value; }
        }

        /// <summary>
        /// Клеймо «чем посчитана» для кривой из геометрии (E12): версия физики
        /// переноса, историй на узел, сетка — инвариантной строкой вида
        /// `phys=6; hist=200000; grid=40-3000 keV/34 std`. Пусто у кривой,
        /// восстановленной по измерениям или введённой руками. Без клейма
        /// кривая в конфигурации неотличима от посчитанной другой физикой.
        /// </summary>
        public string ComputeStamp
        {
            get { return this.computeStamp; }
            set { this.computeStamp = value; }
        }

        [XmlIgnore]
        public bool HasCurve
        {
            get { return this.curve != null && this.curve.Count > 1; }
        }

        [XmlIgnore]
        public bool HasGeometry
        {
            get { return this.geometry != null; }
        }

        public EfficiencyConfigData()
        {
        }

        public EfficiencyConfigData(string name)
        {
            this.guid = System.Guid.NewGuid().ToString();
            this.name = name;
            this.lastUpdated = DateTime.Now;
        }

        /// <summary>
        /// Полная копия с НОВЫМ идентификатором — для кнопки «Дублировать».
        /// Сохранить прежний значило бы завести двух разных с одним именем в
        /// ссылках, а по ссылке их потом ищут.
        /// </summary>
        public EfficiencyConfigData Duplicate(string newName)
        {
            EfficiencyConfigData copy = this.Copy();
            copy.guid = System.Guid.NewGuid().ToString();
            copy.name = newName;
            copy.lastUpdated = DateTime.Now;
            return copy;
        }

        /// <summary>
        /// Полная копия, идентификатор ТОТ ЖЕ — для снимка в файл спектра.
        /// Идентификатор здесь и нужен: по нему видно, из какой конфигурации
        /// прибора снимок сделан, даже когда самого прибора у читателя нет.
        /// </summary>
        public EfficiencyConfigData Copy()
        {
            EfficiencyConfigData copy = new EfficiencyConfigData
            {
                guid = this.guid,
                name = this.name,
                lastUpdated = this.lastUpdated,
                origin = this.origin,
                note = this.note,
                computeStamp = this.computeStamp,
                geometry = this.geometry == null ? null : this.geometry.Clone(),
                curve = new List<ROIEfficiencyData>(),
            };

            if (this.curve != null)
            {
                foreach (ROIEfficiencyData point in this.curve)
                {
                    copy.curve.Add(point.Clone());
                }
            }

            return copy;
        }

        public override string ToString()
        {
            return this.name;
        }

        string guid = "";

        string name = "";

        DateTime lastUpdated = DateTime.Now;

        EfficiencyOrigin origin = EfficiencyOrigin.Manual;

        string computeStamp = "";

        List<ROIEfficiencyData> curve = new List<ROIEfficiencyData>();

        GeometryModel geometry;

        CDATA note = new CDATA("");
    }
}
