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
        // Ответ диалога — ТОЛЬКО выбор кнопкой «Применить». Предвыбор живёт отдельно:
        // прежде он лежал в том же поле, и закрытие окна крестиком возвращало его
        // как выбор — меню применяло поправку, которую человек отменил (`A319`).
        string returnGUID = null;
        string preselectGUID = null;
        DeviceConfigManager deviceConfigManager;
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
            // if spectrum have live device config, chose it
            this.preselectGUID = PreselectGuid(this.mainForm.ActiveDocument.ActiveResultData.DeviceConfigReference,
                                               this.deviceConfigManager.DeviceConfigMap);

            FillDeviceConfigs();
            base.ResumeLayout();
        }

        /// <summary>
        /// Какую конфигурацию предложить первой: конфигурацию документа, если она
        /// есть среди конфигураций прибора, иначе null (тогда — первая в списке).
        ///
        /// ⛔ `A319` (13.10.2026): прежде конфигурация документа предлагалась только
        /// при мёртвом времени ≠ 0. Документ прибора с нулём (`AmplitudaSerial`,
        /// сам мерит живое время) получал первую конфигурацию списка — ЧУЖОГО
        /// прибора, и «Применить» без раздумий пересчитывало живое время по чужому
        /// τ. Теперь предлагается своя, а меню при нуле живое время не трогает и
        /// говорит, что поправка не нужна (<c>CountsRateManager.ApplyDeadTimeCorrection</c>).
        /// Чужую конфигурацию человек по-прежнему может выбрать сам.
        /// </summary>
        public static string PreselectGuid(DeviceConfigReference reference, IDictionary<string, DeviceConfigInfo> deviceConfigMap)
        {
            if (reference == null || reference.Guid == null || deviceConfigMap == null) return null;
            return deviceConfigMap.ContainsKey(reference.Guid) ? reference.Guid : null;
        }

        private void FillDeviceConfigs()
        {
            if (deviceConfigManager.DeviceConfigList == null || deviceConfigManager.DeviceConfigList.Count == 0) return;

            for (int i = 0; i < deviceConfigManager.DeviceConfigList.Count; i++)
            {
                DeviceListcomboBox.Items.Add(deviceConfigManager.DeviceConfigList[i].Name);
                // GUID по номеру строки: две конфигурации с одним именем различаются только им.
                this.listedGuids.Add(deviceConfigManager.DeviceConfigList[i].Guid);
                if (DeviceListcomboBox.SelectedIndex < 0 && this.preselectGUID != null && deviceConfigManager.DeviceConfigList[i].Guid == this.preselectGUID)
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
            if (DeviceListcomboBox.Items.Count == 0 || DeviceListcomboBox.SelectedIndex < 0 ||
                DeviceListcomboBox.SelectedIndex >= this.listedGuids.Count)
            {
                // Выбора нет — и ответа нет (прежде Close() здесь шёл дальше, к
                // поиску по SelectedItem, которого нет).
                this.returnGUID = null;
                Close();
                return;
            }

            // По номеру строки, а не по имени: прежний поиск по имени при двух
            // конфигурациях с одним именем возвращал первую из них, а не выбранную.
            this.returnGUID = this.listedGuids[DeviceListcomboBox.SelectedIndex];

            Close();
        }

        readonly List<string> listedGuids = new List<string>();

        public string SendData()
        {
            return this.returnGUID;
        }
    }
}
