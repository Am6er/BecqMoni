using BecquerelMonitor.Properties;
using System;
using System.Collections.Generic;

namespace BecquerelMonitor
{
    // Token: 0x02000040 RID: 64
    public class DeviceType
    {
        // Token: 0x17000154 RID: 340
        // (get) Token: 0x0600038D RID: 909 RVA: 0x0001144C File Offset: 0x0000F64C
        public static List<DeviceType> DeviceTypeList
        {
            get
            {
                return DeviceType.deviceTypeList;
            }
        }

        // Token: 0x17000155 RID: 341
        // (get) Token: 0x0600038E RID: 910 RVA: 0x00011454 File Offset: 0x0000F654
        public static Dictionary<string, DeviceType> DeviceTypeMap
        {
            get
            {
                return DeviceType.deviceTypeMap;
            }
        }

        // Token: 0x0600038F RID: 911 RVA: 0x0001145C File Offset: 0x0000F65C
        public static void InitializeDeviceTypes()
        {
            DeviceType.deviceTypeList = new List<DeviceType>();
            DeviceType.deviceTypeMap = new Dictionary<string, DeviceType>();
            DeviceType deviceType = new DeviceType();
            deviceType.Id = "AudioInputDevice";
            deviceType.Name = Resources.DeviceTypeAudioInput;
            deviceType.DeviceConfigFormType = typeof(AudioInputDeviceForm);
            deviceType.DeviceControllerType = typeof(AudioInputDeviceController);
            deviceType.DeviceConfigType = typeof(AudioInputDeviceConfig);
            DeviceType.deviceTypeList.Add(deviceType);
            DeviceType.deviceTypeMap.Add(deviceType.Id, deviceType);

            deviceType = new DeviceType();
            deviceType.Id = "AtomSpectraVCP";
            deviceType.Name = Resources.DeviceTypeAtomSpectraVCP;
            deviceType.DeviceConfigFormType = typeof(AtomSpectraVCPDeviceForm);
            deviceType.DeviceControllerType = typeof(AtomSpectraDeviceController);
            deviceType.DeviceConfigType = typeof(AtomSpectraDeviceConfig);
            DeviceType.deviceTypeList.Add(deviceType);

            deviceType = new DeviceType();
            deviceType.Id = "RadiaCode";
            deviceType.Name = Resources.DeviceTypeRadiaCode;
            deviceType.DeviceConfigFormType = typeof(RadiaCodeDeviceForm);
            deviceType.DeviceControllerType = typeof(RadiaCodeDeviceController);
            deviceType.DeviceConfigType = typeof(RadiaCodeDeviceConfig);
            DeviceType.deviceTypeList.Add(deviceType);

            DeviceType.deviceTypeMap.Add(deviceType.Id, deviceType);
        }

        // Token: 0x17000156 RID: 342
        // (get) Token: 0x06000390 RID: 912 RVA: 0x000114EC File Offset: 0x0000F6EC
        // (set) Token: 0x06000391 RID: 913 RVA: 0x000114F4 File Offset: 0x0000F6F4
        public string Id
        {
            get
            {
                return this.id;
            }
            set
            {
                this.id = value;
            }
        }

        // Token: 0x17000157 RID: 343
        // (get) Token: 0x06000392 RID: 914 RVA: 0x00011500 File Offset: 0x0000F700
        // (set) Token: 0x06000393 RID: 915 RVA: 0x00011508 File Offset: 0x0000F708
        public string Name
        {
            get
            {
                return this.name;
            }
            set
            {
                this.name = value;
            }
        }

        // Token: 0x17000158 RID: 344
        // (get) Token: 0x06000394 RID: 916 RVA: 0x00011514 File Offset: 0x0000F714
        // (set) Token: 0x06000395 RID: 917 RVA: 0x0001151C File Offset: 0x0000F71C
        public Type DeviceConfigFormType
        {
            get
            {
                return this.deviceConfigFormType;
            }
            set
            {
                this.deviceConfigFormType = value;
            }
        }

        // Token: 0x17000159 RID: 345
        // (get) Token: 0x06000396 RID: 918 RVA: 0x00011528 File Offset: 0x0000F728
        // (set) Token: 0x06000397 RID: 919 RVA: 0x00011530 File Offset: 0x0000F730
        public Type DeviceControllerType
        {
            get
            {
                return this.deviceContollerType;
            }
            set
            {
                this.deviceContollerType = value;
            }
        }

        // Token: 0x1700015A RID: 346
        // (get) Token: 0x06000398 RID: 920 RVA: 0x0001153C File Offset: 0x0000F73C
        // (set) Token: 0x06000399 RID: 921 RVA: 0x00011544 File Offset: 0x0000F744
        public Type DeviceConfigType
        {
            get
            {
                return this.deviceConfigType;
            }
            set
            {
                this.deviceConfigType = value;
            }
        }

        // Token: 0x0600039B RID: 923 RVA: 0x00011558 File Offset: 0x0000F758
        public override string ToString()
        {
            return this.name;
        }

        // Token: 0x0400016B RID: 363
        static List<DeviceType> deviceTypeList;

        // Token: 0x0400016C RID: 364
        static Dictionary<string, DeviceType> deviceTypeMap;

        // Token: 0x0400016D RID: 365
        string id;

        // Token: 0x0400016E RID: 366
        string name;

        // Token: 0x0400016F RID: 367
        Type deviceConfigFormType;

        // Token: 0x04000170 RID: 368
        Type deviceContollerType;

        // Token: 0x04000171 RID: 369
        Type deviceConfigType;
    }
}
