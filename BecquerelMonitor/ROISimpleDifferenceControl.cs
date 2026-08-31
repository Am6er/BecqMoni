using System;

namespace BecquerelMonitor
{
    public partial class ROISimpleDifferenceControl : ROIPrimitiveControl
    {
        public ROISimpleDifferenceControl()
        {
            this.InitializeComponent();
            this.comboBox1.Items.Clear();
            foreach (ROIPrimitiveOperation roiprimitiveOperation in ROIPrimitiveOperation.Operations)
            {
                this.comboBox1.Items.Add(roiprimitiveOperation.Translation);
            }
            this.comboBox1.SelectedIndex = 0;
        }

        public override void LoadFormContents(ROIPrimitiveData prim)
        {
            ROISimpleDifferenceData roisimpleDifferenceData = (ROISimpleDifferenceData)prim;
            this.comboBox1.SelectedIndex = ROIPrimitiveOperation.GetOperationIndex(prim.OperationType);
            this.doubleTextBox3.Text = roisimpleDifferenceData.Coefficient.ToString();
            this.doubleTextBox4.Text = roisimpleDifferenceData.CoefficientError.ToString();
            this.doubleTextBox1.Text = roisimpleDifferenceData.LowerLimit.ToString();
            this.doubleTextBox2.Text = roisimpleDifferenceData.UpperLimit.ToString();
            this.textBox1.Text = roisimpleDifferenceData.Note;
        }

        /// <summary>
        /// ⛔ СНАЧАЛА РАЗОБРАТЬ ВСЁ, ПОТОМ ПИСАТЬ. Прежде поля присваивались по
        /// одному прямо в объект, и первое же неразобранное число оставляло его
        /// НАПОЛОВИНУ ИЗМЕНЁННЫМ при возврате <c>false</c> (`A7`): операция и
        /// коэффициент уже новые, границы ещё старые. Читателю возврата от
        /// этого не легче — список зон показывает старое, объект держит смесь,
        /// а при следующем сохранении смесь уезжает на диск.
        ///
        /// Отказ обязан быть БЕЗ ПОСЛЕДСТВИЙ: не разобралось — объект не тронут.
        /// </summary>
        public override bool SaveFormContents(ROIPrimitiveData prim)
        {
            ROISimpleDifferenceData roisimpleDifferenceData = (ROISimpleDifferenceData)prim;
            ROIPrimitiveOperation roiprimitiveOperation;
            double coefficient;
            double coefficientError;
            double lowerLimit;
            double upperLimit;
            try
            {
                roiprimitiveOperation = ROIPrimitiveOperation.Operations[this.comboBox1.SelectedIndex];
                coefficient = double.Parse(this.doubleTextBox3.Text);
                coefficientError = double.Parse(this.doubleTextBox4.Text);
                lowerLimit = double.Parse(this.doubleTextBox1.Text);
                upperLimit = double.Parse(this.doubleTextBox2.Text);
            }
            catch (Exception)
            {
                return false;
            }
            if (upperLimit < lowerLimit)
            {
                upperLimit = lowerLimit;
            }
            roisimpleDifferenceData.Operation = roiprimitiveOperation;
            roisimpleDifferenceData.OperationType = roiprimitiveOperation.Name;
            roisimpleDifferenceData.Coefficient = coefficient;
            roisimpleDifferenceData.CoefficientError = coefficientError;
            roisimpleDifferenceData.LowerLimit = lowerLimit;
            roisimpleDifferenceData.UpperLimit = upperLimit;
            prim.Note = this.textBox1.Text;
            this.doubleTextBox2.Text = upperLimit.ToString();
            return true;
        }

        void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            base.PrimitiveModified();
        }

        void doubleTextBox3_TextChanged(object sender, EventArgs e)
        {
            base.PrimitiveModified();
        }

        void doubleTextBox4_TextChanged(object sender, EventArgs e)
        {
            base.PrimitiveModified();
        }

        void doubleTextBox1_TextChanged(object sender, EventArgs e)
        {
        }

        void doubleTextBox2_TextChanged(object sender, EventArgs e)
        {
        }

        void textBox1_TextChanged(object sender, EventArgs e)
        {
            base.PrimitiveModified();
        }

        void doubleTextBox1_Validated(object sender, EventArgs e)
        {
            base.PrimitiveModified();
        }

        void doubleTextBox2_Validated(object sender, EventArgs e)
        {
            base.PrimitiveModified();
        }
    }
}
