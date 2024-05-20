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
using Windows.UI.Notifications;

namespace BecquerelMonitor
{
    public partial class SelectROIDialog : Form
    {
        MainForm mainForm;
        string returnGUID = null;
        readonly ROIConfigManager ROIConfigManager;
        readonly List<ROIConfigData> ROIConfigDatas;
        DeviceConfigManager deviceConfigManager;


        public SelectROIDialog()
        {
            InitializeComponent();
        }

        public SelectROIDialog(Form mainForm)
        {
            InitializeComponent();
            this.mainForm = (MainForm)mainForm;
            this.Icon = Resources.becqmoni;
            this.deviceConfigManager = DeviceConfigManager.GetInstance();
            this.ROIConfigManager = ROIConfigManager.GetInstance();
            this.ROIConfigDatas = this.ROIConfigManager.ROIConfigList;
            base.SuspendLayout();
            if (deviceConfigManager.DeviceConfigMap.ContainsKey(this.mainForm.ActiveDocument.ActiveResultData.DeviceConfigReference.Guid))
            {
                DeviceConfigInfo deviceConfigInfo = this.deviceConfigManager.DeviceConfigMap[this.mainForm.ActiveDocument.ActiveResultData.DeviceConfigReference.Guid];
                if(deviceConfigInfo.EfficencyROIGuid != null && this.ROIConfigManager.ROIConfigMap.ContainsKey(deviceConfigInfo.EfficencyROIGuid))
                {
                    this.returnGUID = deviceConfigInfo.EfficencyROIGuid;
                    Close();
                }
                this.checkBox1.Enabled = true;
            } else
            {
                this.checkBox1.Enabled = false;
            }
            FillROIConfigs();
            base.ResumeLayout();
        }

        private void FillROIConfigs()
        {
            if (ROIConfigDatas == null || ROIConfigDatas.Count == 0) return;

            for(int i = 0; i < ROIConfigDatas.Count; i++) 
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
            }
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
                    if (this.checkBox1.Checked)
                    {
                        DeviceConfigInfo deviceConfigInfo = this.deviceConfigManager.DeviceConfigMap[this.mainForm.ActiveDocument.ActiveResultData.DeviceConfigReference.Guid];
                        deviceConfigInfo.EfficencyROIGuid = this.returnGUID;
                        this.deviceConfigManager.SaveConfig(deviceConfigInfo);
                    }
                    
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
