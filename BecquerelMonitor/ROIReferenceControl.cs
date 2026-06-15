using System;

namespace BecquerelMonitor
{
    public partial class ROIReferenceControl : ROIPrimitiveControl
    {
        ROIConfigData roiConfig;

        public ROIReferenceControl()
        {
            this.InitializeComponent();
            this.comboBox1.Items.Clear();
            foreach (ROIPrimitiveOperation roiprimitiveOperation in ROIPrimitiveOperation.Operations)
            {
                this.comboBox1.Items.Add(roiprimitiveOperation.Translation);
            }
            this.comboBox1.SelectedIndex = 0;
        }

        public override void PrepareForm(ROIConfigData config)
        {
            this.roiConfig = config;
            this.comboBox2.Items.Clear();
            foreach (ROIDefinitionData roidefinitionData in this.roiConfig.ROIDefinitions)
            {
                this.comboBox2.Items.Add(roidefinitionData.Name);
            }
        }

        public override void LoadFormContents(ROIPrimitiveData prim)
        {
            ROIReferenceData roireferenceData = (ROIReferenceData)prim;
            this.comboBox1.SelectedIndex = ROIPrimitiveOperation.GetOperationIndex(prim.OperationType);
            this.doubleTextBox3.Text = roireferenceData.Coefficient.ToString();
            this.doubleTextBox4.Text = roireferenceData.CoefficientError.ToString();
            this.comboBox2.SelectedItem = roireferenceData.Reference;
            this.textBox1.Text = roireferenceData.Note;
        }

        public override bool SaveFormContents(ROIPrimitiveData prim)
        {
            ROIReferenceData roireferenceData = (ROIReferenceData)prim;
            try
            {
                ROIPrimitiveOperation roiprimitiveOperation = ROIPrimitiveOperation.Operations[this.comboBox1.SelectedIndex];
                roireferenceData.Operation = roiprimitiveOperation;
                roireferenceData.OperationType = roiprimitiveOperation.Name;
                roireferenceData.Coefficient = double.Parse(this.doubleTextBox3.Text);
                roireferenceData.CoefficientError = double.Parse(this.doubleTextBox4.Text);
                roireferenceData.Reference = (string)this.comboBox2.SelectedItem;
                if (roireferenceData.Reference == null)
                {
                    roireferenceData.Reference = "";
                }
                roireferenceData.Note = this.textBox1.Text;
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

        void comboBox2_DropDown(object sender, EventArgs e)
        {
            string selectedItem = (string)this.comboBox2.SelectedItem;
            if (this.roiConfig != null)
            {
                this.comboBox2.Items.Clear();
                foreach (ROIDefinitionData roidefinitionData in this.roiConfig.ROIDefinitions)
                {
                    this.comboBox2.Items.Add(roidefinitionData.Name);
                }
            }
            this.comboBox2.SelectedItem = selectedItem;
        }

        void comboBox2_SelectedIndexChanged(object sender, EventArgs e)
        {
            base.PrimitiveModified();
        }
    }
}
