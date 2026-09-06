using System;

namespace BecquerelMonitor.N42
{
    /// <remarks/>
    [System.CodeDom.Compiler.GeneratedCodeAttribute("xsd", "4.8.3928.0")]
    [System.SerializableAttribute()]
    [System.Diagnostics.DebuggerStepThroughAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true, Namespace = "http://physics.nist.gov/N42/2011/N42")]
    public partial class EnergyCalibration
    {

        private string coefficientValuesField;

        private string energyBoundaryValuesField;

        private string idField;

        public EnergyCalibration()
        {
            this.coefficientValuesField = "";
            this.idField = "unknownCalibration"; // same set in Spectrum
        }

        /// <remarks/>
        public string CoefficientValues
        {
            get
            {
                return this.coefficientValuesField;
            }
            set
            {
                this.coefficientValuesField = value;
            }
        }

        /// <summary>
        /// Шкала, заданная ГРАНИЦАМИ ЭНЕРГИЙ КАНАЛОВ, а не полиномом (`A136`).
        ///
        /// В N42-2011/2012 объект EnergyCalibration несёт ЛИБО CoefficientValues,
        /// ЛИБО EnergyBoundaryValues. Поле заведено `A136` затем, чтобы разбор
        /// ВИДЕЛ это положение и называл его своим именем; с `A253` (решение
        /// Amber 06.09.2026) границы ЧИТАЮТСЯ — Util.FitEnergyBoundaryValues
        /// подгоняет по ним полином тем же приёмом, что и дверь SpecUtils.
        ///
        /// ⚠ До 05.09.2026 поля не было, XmlSerializer пропускал элемент МОЛЧА,
        /// и файл со шкалой по границам приходил с пустым CoefficientValues —
        /// в один catch вместе с нечислом в коэффициентах и порядком полинома
        /// больше четырёх. Один текст на три разные беды.
        ///
        /// ⛔ Начального значения НЕТ нарочно: null означает «элемента в файле
        /// не было», и XmlSerializer такое поле при вывозе НЕ ЗАПИСЫВАЕТ. Пустая
        /// строка вместо null дописала бы в КАЖДЫЙ выгруженный файл пустой
        /// &lt;EnergyBoundaryValues /&gt;, которого там быть не должно.
        /// </summary>
        public string EnergyBoundaryValues
        {
            get
            {
                return this.energyBoundaryValuesField;
            }
            set
            {
                this.energyBoundaryValuesField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string id
        {
            get
            {
                return this.idField;
            }
            set
            {
                this.idField = value;
            }
        }

        public double[] CoefficientsToArray()
        {
            string[] n42CalibrationCoeff = this.coefficientValuesField.Replace("\n", string.Empty).Split(new string[] { " " }, StringSplitOptions.None);
            n42CalibrationCoeff = Array.FindAll(n42CalibrationCoeff, isNotN42SpectrumValid);
            double[] coefficients = new double[n42CalibrationCoeff.Length];
            for (int i = 0; i < n42CalibrationCoeff.Length; i++)
            {
                // `A158`: инвариантная культура — то же правило, что у разбора
                //   коэффициентов в Util.cs (`A142`). ⚠ ЧЕСТНО: читателей у этого
                //   метода в дереве НОЛЬ (Util.cs разбирает CoefficientValues сам),
                //   то есть правка ничего сегодня не меняет числом. Сделана она
                //   затем, что метод публичный и живой: пара «пишем инвариантно —
                //   читаем культурой машины» уже дважды расходилась молча, и
                //   оставлять здесь второе соглашение значит ждать третьего раза.
                coefficients[i] = double.Parse(n42CalibrationCoeff[i], System.Globalization.CultureInfo.InvariantCulture);
            }
            return coefficients;
        }

        private bool isNotN42SpectrumValid(string str)
        {
            if (str == "" || str == "\n")
            {
                return false;
            }
            else
            {
                return true;
            }
        }
    }
}
