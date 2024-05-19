using BecquerelMonitor.Properties;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    public partial class SelectROIDialog : Form
    {
        MainForm mainForm;
        string returnGUID = null;
        readonly ROIConfigManager ROIConfigManager;
        readonly List<ROIConfigData> ROIConfigDatas;


        public SelectROIDialog()
        {
            InitializeComponent();
        }

        public SelectROIDialog(Form mainForm)
        {
            InitializeComponent();
            this.mainForm = (MainForm)mainForm;
            this.Icon = Resources.becqmoni;
            this.ROIConfigManager = ROIConfigManager.GetInstance();
            this.ROIConfigDatas = this.ROIConfigManager.ROIConfigList;
            base.SuspendLayout();
            FillROIConfigs();
            base.ResumeLayout();
        }

        private void FillROIConfigs()
        {
            if (ROIConfigDatas == null || ROIConfigDatas.Count == 0) return;

            Parallel.For(0, ROIConfigDatas.Count, i => 
            {
                int counterData = 0;
                for (int j = 0; j < ROIConfigDatas[i].ROIDefinitions.Count; j++)
                    {
                        if (ROIConfigDatas[i].ROIDefinitions[j].PeakEnergy > 0 && ROIConfigDatas[i].ROIDefinitions[j].Intencity > 0)
                        {
                            counterData++;
                            if (counterData > 2)
                            {
                                comboBox1.Items.Add(ROIConfigDatas[i].Name);
                                if (comboBox1.SelectedIndex < 0) comboBox1.SelectedIndex = 0;
                                break;
                            }
                        }
                    }
            });
        }

        public string SendData()
        {
            return this.returnGUID;
        }

        private void button2_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (comboBox1.Items.Count == 0 || comboBox1.SelectedIndex < 0)
            {
                Close();
            }

            for (int i = 0; i < ROIConfigDatas.Count; i++)
            {
                if (ROIConfigDatas[i].Name == comboBox1.SelectedItem.ToString())
                {
                    this.returnGUID = ROIConfigDatas[i].Guid;
                    break;
                }
            }

            Close();
        }

        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {

        }
    }
}
