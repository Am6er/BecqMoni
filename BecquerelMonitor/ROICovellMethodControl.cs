using System;

namespace BecquerelMonitor
{
    public partial class ROICovellMethodControl : ROIPrimitiveControl
    {
        public ROICovellMethodControl()
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
            ROICovellMethodData roicovellMethodData = (ROICovellMethodData)prim;
            this.comboBox1.SelectedIndex = ROIPrimitiveOperation.GetOperationIndex(roicovellMethodData.OperationType);
            this.doubleTextBox3.Text = roicovellMethodData.Coefficient.ToString();
            this.doubleTextBox4.Text = roicovellMethodData.CoefficientError.ToString();
            this.doubleTextBox1.Text = roicovellMethodData.LowerLimit.ToString();
            this.doubleTextBox2.Text = roicovellMethodData.UpperLimit.ToString();
            this.doubleTextBox5.Text = roicovellMethodData.LeftRegionCenter.ToString();
            this.doubleTextBox6.Text = roicovellMethodData.RightRegionCenter.ToString();
            this.doubleTextBox7.Text = roicovellMethodData.LeftRegionWidth.ToString();
            this.doubleTextBox8.Text = roicovellMethodData.RightRegionWidth.ToString();
            this.textBox1.Text = roicovellMethodData.Note;
        }

        public override bool SaveFormContents(ROIPrimitiveData prim)
        {
            ROICovellMethodData roicovellMethodData = (ROICovellMethodData)prim;
            try
            {
                ROIPrimitiveOperation roiprimitiveOperation = ROIPrimitiveOperation.Operations[this.comboBox1.SelectedIndex];
                roicovellMethodData.Operation = roiprimitiveOperation;
                roicovellMethodData.OperationType = roiprimitiveOperation.Name;
                roicovellMethodData.Coefficient = double.Parse(this.doubleTextBox3.Text);
                roicovellMethodData.CoefficientError = double.Parse(this.doubleTextBox4.Text);
                roicovellMethodData.LowerLimit = double.Parse(this.doubleTextBox1.Text);
                roicovellMethodData.UpperLimit = double.Parse(this.doubleTextBox2.Text);
                if (roicovellMethodData.UpperLimit < roicovellMethodData.LowerLimit)
                {
                    roicovellMethodData.UpperLimit = roicovellMethodData.LowerLimit;
                    this.doubleTextBox2.Text = roicovellMethodData.LowerLimit.ToString();
                }
                roicovellMethodData.LeftRegionCenter = double.Parse(this.doubleTextBox5.Text);
                roicovellMethodData.RightRegionCenter = double.Parse(this.doubleTextBox6.Text);
                roicovellMethodData.LeftRegionWidth = double.Parse(this.doubleTextBox7.Text);
                roicovellMethodData.RightRegionWidth = double.Parse(this.doubleTextBox8.Text);
                roicovellMethodData.Note = this.textBox1.Text;
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

        void integerTextBox1_TextChanged(object sender, EventArgs e)
        {
            base.PrimitiveModified();
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

        void doubleTextBox5_TextChanged(object sender, EventArgs e)
        {
        }

        void doubleTextBox6_TextChanged(object sender, EventArgs e)
        {
        }

        void doubleTextBox7_TextChanged(object sender, EventArgs e)
        {
            base.PrimitiveModified();
        }

        void doubleTextBox8_TextChanged(object sender, EventArgs e)
        {
            base.PrimitiveModified();
        }

        void doubleTextBox5_Validated(object sender, EventArgs e)
        {
            base.PrimitiveModified();
        }

        void doubleTextBox6_Validated(object sender, EventArgs e)
        {
            base.PrimitiveModified();
        }
    }
}
