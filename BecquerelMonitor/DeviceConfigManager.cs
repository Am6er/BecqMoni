using BecquerelMonitor.Properties;
using System;
using System.Collections.Generic;
using System.Deployment.Application;
using System.IO;
using System.Windows.Forms;
using System.Xml.Serialization;
using WinMM;

namespace BecquerelMonitor
{
    // Token: 0x02000075 RID: 117
    public class DeviceConfigManager
    {
        // Token: 0x14000015 RID: 21
        // (add) Token: 0x060005F3 RID: 1523 RVA: 0x000256C0 File Offset: 0x000238C0
        // (remove) Token: 0x060005F4 RID: 1524 RVA: 0x000256FC File Offset: 0x000238FC
        public event DeviceConfigManager.DeviceConfigChangedEventHandler DeviceConfigListChanged;

        // Token: 0x170001D0 RID: 464
        // (get) Token: 0x060005F5 RID: 1525 RVA: 0x00025738 File Offset: 0x00023938
        // (set) Token: 0x060005F6 RID: 1526 RVA: 0x00025740 File Offset: 0x00023940
        public List<DeviceConfigInfo> DeviceConfigList
        {
            get
            {
                return this.deviceConfigList;
            }
            set
            {
                this.deviceConfigList = value;
            }
        }

        // Token: 0x170001D1 RID: 465
        // (get) Token: 0x060005F7 RID: 1527 RVA: 0x0002574C File Offset: 0x0002394C
        // (set) Token: 0x060005F8 RID: 1528 RVA: 0x00025754 File Offset: 0x00023954
        public Dictionary<string, DeviceConfigInfo> DeviceConfigMap
        {
            get
            {
                return this.deviceConfigMap;
            }
            set
            {
                this.deviceConfigMap = value;
            }
        }

        // Token: 0x060005F9 RID: 1529 RVA: 0x00025760 File Offset: 0x00023960
        public static DeviceConfigManager GetInstance()
        {
            DeviceConfigManager.instance.LoadAllConfigFiles();
            return DeviceConfigManager.instance;
        }

        // Token: 0x060005FB RID: 1531 RVA: 0x00025794 File Offset: 0x00023994
        public void LoadAllConfigFiles()
        {
            if (this.listLoaded)
            {
                return;
            }
            this.deviceConfigList.Clear();
            this.deviceConfigMap.Clear();
            XmlSerializer xmlSerializer = new XmlSerializer(typeof(DeviceConfigInfo_097b));
            XmlSerializer xmlSerializer2 = new XmlSerializer(typeof(DeviceConfigInfo));
            try
            {
                string[] files = Directory.GetFiles(userDirectoryConfigDevice, "*.xml");
                foreach (string path in files)
                {
                    DeviceConfigInfo deviceConfigInfo;
                    using (FileStream fileStream = new FileStream(path, FileMode.Open))
                    {
                        deviceConfigInfo = (DeviceConfigInfo)xmlSerializer2.Deserialize(fileStream);
                    }
                    if (!(deviceConfigInfo.FormatVersion == "120920"))
                    {
                        using (FileStream fileStream2 = new FileStream(path, FileMode.Open))
                        {
                            DeviceConfigInfo_097b old = (DeviceConfigInfo_097b)xmlSerializer.Deserialize(fileStream2);
                            deviceConfigInfo = new DeviceConfigInfo(old);
                        }
                    }
                    deviceConfigInfo.OriginalFilename = Path.GetFileName(path);
                    deviceConfigInfo.Filename = Path.GetFileName(path);
                    if (this.deviceConfigMap.ContainsKey(deviceConfigInfo.Guid))
                    {
                        MessageBox.Show(string.Format(Resources.ERRDuplicateDeviceConfigGUID, deviceConfigInfo.Filename), Resources.ErrorDialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                    }
                    else
                    {
                        this.deviceConfigList.Add(deviceConfigInfo);
                        this.deviceConfigMap.Add(deviceConfigInfo.Guid, deviceConfigInfo);
                    }
                }
            }
            catch (Exception)
            {
                Directory.CreateDirectory(userDirectoryConfigDeviceDir);
                MessageBox.Show(Resources.ERRLoadingDeviceConfigFailed, Resources.ErrorDialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Hand);
            }
            this.deviceConfigList.Sort();
            this.listLoaded = true;
        }

        // Token: 0x060005FC RID: 1532 RVA: 0x00025990 File Offset: 0x00023B90
        public DeviceConfigInfo CreateConfig(string filename)
        {
            DeviceConfigInfo deviceConfigInfo = new DeviceConfigInfo();
            deviceConfigInfo.InitFormatVersion();
            deviceConfigInfo.Guid = Guid.NewGuid().ToString();
            deviceConfigInfo.OriginalFilename = filename;
            deviceConfigInfo.Filename = filename;
            deviceConfigInfo.Name = Path.GetFileNameWithoutExtension(filename);
            string path = userDirectoryConfigDevice + deviceConfigInfo.Filename;
            AudioInputDeviceConfig audioInputDeviceConfig = (AudioInputDeviceConfig)deviceConfigInfo.InputDeviceConfig;
            WaveInDeviceCaps audioInputDevice = null;
            if (WaveIn.Devices.Count > 0)
            {
                audioInputDevice = WaveIn.Devices[0];
            }
            audioInputDeviceConfig.AudioInputDevice = audioInputDevice;
            try
            {
                using (FileStream fileStream = new FileStream(path, FileMode.Create))
                {
                    XmlSerializer xmlSerializer = new XmlSerializer(typeof(DeviceConfigInfo));
                    xmlSerializer.Serialize(fileStream, deviceConfigInfo);
                }
            }
            catch (Exception)
            {
                MessageBox.Show(Resources.ERRSavingDeviceConfigFailed, Resources.ErrorDialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Hand);
                return null;
            }
            this.deviceConfigList.Add(deviceConfigInfo);
            this.deviceConfigMap.Add(deviceConfigInfo.Guid, deviceConfigInfo);
            this.deviceConfigList.Sort();
            if (this.DeviceConfigListChanged != null)
            {
                this.DeviceConfigListChanged(this, new DeviceConfigChangedEventArgs(deviceConfigInfo.Guid));
            }
            return deviceConfigInfo;
        }

        // Token: 0x060005FD RID: 1533 RVA: 0x00025AE8 File Offset: 0x00023CE8
        public DeviceConfigInfo DuplicateConfig(DeviceConfigInfo config, string filename)
        {
            DeviceConfigInfo deviceConfigInfo = config.Clone();
            deviceConfigInfo.InitFormatVersion();
            deviceConfigInfo.Guid = Guid.NewGuid().ToString();
            deviceConfigInfo.OriginalFilename = filename;
            deviceConfigInfo.Filename = filename;
            deviceConfigInfo.Name = config.Name + Resources.CopyPostfix;
            try
            {
                string path = userDirectoryConfigDevice + deviceConfigInfo.Filename;
                using (FileStream fileStream = new FileStream(path, FileMode.Create))
                {
                    XmlSerializer xmlSerializer = new XmlSerializer(typeof(DeviceConfigInfo));
                    xmlSerializer.Serialize(fileStream, deviceConfigInfo);
                }
            }
            catch (Exception)
            {
                MessageBox.Show(Resources.ERRSavingDeviceConfigFailed, Resources.ErrorDialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Hand);
                return null;
            }
            this.deviceConfigList.Add(deviceConfigInfo);
            this.deviceConfigMap.Add(deviceConfigInfo.Guid, deviceConfigInfo);
            this.deviceConfigList.Sort();
            if (this.DeviceConfigListChanged != null)
            {
                this.DeviceConfigListChanged(this, new DeviceConfigChangedEventArgs(deviceConfigInfo.Guid));
            }
            return deviceConfigInfo;
        }

        // Token: 0x060005FE RID: 1534 RVA: 0x00025C10 File Offset: 0x00023E10
        public bool SaveConfig(DeviceConfigInfo devConfig)
        {
            DeviceConfigInfo deviceConfigInfo = this.deviceConfigMap[devConfig.Guid];
            this.deviceConfigMap.Remove(deviceConfigInfo.Guid);
            this.deviceConfigList.Remove(deviceConfigInfo);
            if (devConfig.OriginalFilename != devConfig.Filename)
            {
                try
                {
                    File.Delete(userDirectoryConfigDevice + devConfig.OriginalFilename);
                }
                catch (Exception)
                {
                    return false;
                }
            }
            devConfig.OriginalFilename = devConfig.Filename;
            devConfig.LastUpdated = DateTime.Now;
            devConfig.Dirty = false;
            deviceConfigInfo = devConfig.Clone();
            try
            {
                string path = userDirectoryConfigDevice + deviceConfigInfo.Filename;
                using (FileStream fileStream = new FileStream(path, FileMode.Create))
                {
                    XmlSerializer xmlSerializer = new XmlSerializer(typeof(DeviceConfigInfo));
                    xmlSerializer.Serialize(fileStream, deviceConfigInfo);
                }
            }
            catch (Exception)
            {
                MessageBox.Show(Resources.ERRSavingDeviceConfigFailed, Resources.ErrorDialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Hand);
                return false;
            }
            this.deviceConfigList.Add(deviceConfigInfo);
            this.deviceConfigMap.Add(deviceConfigInfo.Guid, deviceConfigInfo);
            this.deviceConfigList.Sort();
            if (this.DeviceConfigListChanged != null)
            {
                this.DeviceConfigListChanged(this, new DeviceConfigChangedEventArgs(deviceConfigInfo.Guid));
            }
            return true;
        }

        // Token: 0x060005FF RID: 1535 RVA: 0x00025D88 File Offset: 0x00023F88
        public void DeleteConfig(DeviceConfigInfo devConfig)
        {
            DeviceConfigInfo deviceConfigInfo = this.deviceConfigMap[devConfig.Guid];
            try
            {
                File.Delete(userDirectoryConfigDevice + deviceConfigInfo.OriginalFilename);
            }
            catch (Exception)
            {
            }
            this.deviceConfigList.Remove(deviceConfigInfo);
            this.deviceConfigMap.Remove(deviceConfigInfo.Guid);
            if (this.DeviceConfigListChanged != null)
            {
                this.DeviceConfigListChanged(this, new DeviceConfigChangedEventArgs(deviceConfigInfo.Guid));
            }
        }

        string userDirectory = BecquerelMonitor.Package.GetInstance().UserDirectory;

        string userDirectoryConfigDevice = BecquerelMonitor.Package.GetInstance().Device;

        string userDirectoryConfigDeviceDir = BecquerelMonitor.Package.GetInstance().DeviceDir;

        // Token: 0x04000328 RID: 808
        List<DeviceConfigInfo> deviceConfigList = new List<DeviceConfigInfo>();

        // Token: 0x04000329 RID: 809
        Dictionary<string, DeviceConfigInfo> deviceConfigMap = new Dictionary<string, DeviceConfigInfo>();

        // Token: 0x0400032A RID: 810
        bool listLoaded;

        // Token: 0x0400032C RID: 812
        static DeviceConfigManager instance = new DeviceConfigManager();

        // Token: 0x02000222 RID: 546
        // (Invoke) Token: 0x06001933 RID: 6451
        public delegate void DeviceConfigChangedEventHandler(object sender, DeviceConfigChangedEventArgs e);
    }
}
