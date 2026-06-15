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

        public override bool SaveFormContents(ROIPrimitiveData prim)
        {
            ROISimpleDifferenceData roisimpleDifferenceData = (ROISimpleDifferenceData)prim;
            try
            {
                ROIPrimitiveOperation roiprimitiveOperation = ROIPrimitiveOperation.Operations[this.comboBox1.SelectedIndex];
                roisimpleDifferenceData.Operation = roiprimitiveOperation;
                roisimpleDifferenceData.OperationType = roiprimitiveOperation.Name;
                roisimpleDifferenceData.Coefficient = double.Parse(this.doubleTextBox3.Text);
                roisimpleDifferenceData.CoefficientError = double.Parse(this.doubleTextBox4.Text);
                roisimpleDifferenceData.LowerLimit = double.Parse(this.doubleTextBox1.Text);
                roisimpleDifferenceData.UpperLimit = double.Parse(this.doubleTextBox2.Text);
                if (roisimpleDifferenceData.UpperLimit < roisimpleDifferenceData.LowerLimit)
                {
                    roisimpleDifferenceData.UpperLimit = roisimpleDifferenceData.LowerLimit;
                    this.doubleTextBox2.Text = roisimpleDifferenceData.LowerLimit.ToString();
                }
                this.doubleTextBox2.Text = roisimpleDifferenceData.UpperLimit.ToString();
                prim.Note = this.textBox1.Text;
            }
            catch (Exception)
            {
                return false;
            }
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
