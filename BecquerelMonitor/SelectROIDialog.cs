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
        bool formLoading = false;


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
            this.formLoading = true;

            // if ROI selected for that spectrum in Control panel, use it by default
            if (this.mainForm.ActiveDocument.ActiveResultData.ROIConfigReference.Guid != null &&
                this.ROIConfigManager.ROIConfigMap.ContainsKey(this.mainForm.ActiveDocument.ActiveResultData.ROIConfigReference.Guid) &&
                this.ROIConfigManager.ROIConfigMap[this.mainForm.ActiveDocument.ActiveResultData.ROIConfigReference.Guid].HasEfficiency)
            {
                this.returnGUID = this.mainForm.ActiveDocument.ActiveResultData.ROIConfigReference.Guid;
            }

            // if no ROI selected in control panel use ROI from device config by default
            if (this.returnGUID == null && 
                deviceConfigManager.DeviceConfigMap.ContainsKey(this.mainForm.ActiveDocument.ActiveResultData.DeviceConfigReference.Guid))
            {
                DeviceConfigInfo deviceConfigInfo = this.deviceConfigManager.DeviceConfigMap[this.mainForm.ActiveDocument.ActiveResultData.DeviceConfigReference.Guid];
                if(deviceConfigInfo.EfficencyROIGuid != null && this.ROIConfigManager.ROIConfigMap.ContainsKey(deviceConfigInfo.EfficencyROIGuid))
                {
                    this.returnGUID = deviceConfigInfo.EfficencyROIGuid;
                }
                this.checkBox1.Enabled = true;
            } else
            {
                this.checkBox1.Enabled = false;
            }

            FillROIConfigs();
            this.formLoading = false;
            base.ResumeLayout();
        }

        private void FillROIConfigs()
        {
            if (ROIConfigDatas == null || ROIConfigDatas.Count == 0) return;

            for(int i = 0; i < ROIConfigDatas.Count; i++) 
            {
                if (ROIConfigDatas[i].HasEfficiency)
                {
                    comboBox1.Items.Add(ROIConfigDatas[i].Name);
                    if (comboBox1.SelectedIndex < 0 && this.returnGUID != null && ROIConfigDatas[i].Guid == this.returnGUID)
                    {
                        comboBox1.SelectedIndex = i;
                    }
                }
            }
            if (comboBox1.SelectedIndex < 0 && comboBox1.Items.Count > 0)
            {
                comboBox1.SelectedIndex = 0;
            }
        }

        public string SendData()
        {
            return this.returnGUID;
        }

        private void button2_Click(object sender, EventArgs e)
        {
            this.returnGUID = null;
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
