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
    public partial class SelectDeviceDialog : Form
    {
        MainForm mainForm;
        string returnGUID = null;
        DeviceConfigManager deviceConfigManager;
        bool formLoading = false;

        public SelectDeviceDialog()
        {
            InitializeComponent();
        }

        public SelectDeviceDialog(Form mainForm)
        {
            InitializeComponent();
            this.mainForm = (MainForm)mainForm;
            this.Icon = Resources.becqmoni;
            this.deviceConfigManager = DeviceConfigManager.GetInstance();
            base.SuspendLayout();
            this.formLoading = true;

            // if spectrum have live device config, chose it
            if (this.mainForm.ActiveDocument.ActiveResultData.DeviceConfigReference != null &&
                this.mainForm.ActiveDocument.ActiveResultData.DeviceConfigReference.Guid != null &&
                this.deviceConfigManager.DeviceConfigMap.ContainsKey(this.mainForm.ActiveDocument.ActiveResultData.DeviceConfigReference.Guid) &&
                this.deviceConfigManager.DeviceConfigMap[this.mainForm.ActiveDocument.ActiveResultData.DeviceConfigReference.Guid].InputDeviceConfig.DeadTime() != 0)
            {
                this.returnGUID = this.mainForm.ActiveDocument.ActiveResultData.DeviceConfigReference.Guid;
            }

            FillDeviceConfigs();
            this.formLoading = false;
            base.ResumeLayout();
        }

        private void FillDeviceConfigs()
        {
            if (deviceConfigManager.DeviceConfigList == null || deviceConfigManager.DeviceConfigList.Count == 0) return;

            for (int i = 0; i < deviceConfigManager.DeviceConfigList.Count; i++)
            {
                DeviceListcomboBox.Items.Add(deviceConfigManager.DeviceConfigList[i].Name);
                if (DeviceListcomboBox.SelectedIndex < 0 && this.returnGUID != null && deviceConfigManager.DeviceConfigList[i].Guid == this.returnGUID)
                {
                    DeviceListcomboBox.SelectedIndex = DeviceListcomboBox.Items.Count - 1;
                }
            }
            if (DeviceListcomboBox.SelectedIndex < 0 && DeviceListcomboBox.Items.Count > 0)
            {
                DeviceListcomboBox.SelectedIndex = 0;
            }
        }

        private void CancelBtn_Click(object sender, EventArgs e)
        {
            this.returnGUID = null;
            Close();
        }

        private void ApplyBtn_Click(object sender, EventArgs e)
        {
            if (DeviceListcomboBox.Items.Count == 0 || DeviceListcomboBox.SelectedIndex < 0)
            {
                Close();
            }

            for (int i = 0; i < deviceConfigManager.DeviceConfigList.Count; i++)
            {
                if (deviceConfigManager.DeviceConfigList[i].Name == DeviceListcomboBox.SelectedItem.ToString())
                {
                    this.returnGUID = deviceConfigManager.DeviceConfigList[i].Guid;
                    break;
                }
            }

            Close();
        }

        public string SendData()
        {
            return this.returnGUID;
        }
    }
}
