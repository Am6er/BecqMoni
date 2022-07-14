using CoreAudioApi;
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace BecquerelMonitor
{
    // Token: 0x02000132 RID: 306
    public class AudioVolumeController
    {
        // Token: 0x06000FDF RID: 4063
        [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern uint waveInMessage(UIntPtr deviceID, uint uMsg, ref uint dwParam1, ref uint dwParam2);

        // Token: 0x06000FE0 RID: 4064
        [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern uint waveInMessage(UIntPtr deviceID, uint uMsg, StringBuilder dwParam1, ref uint dwParam2);

        // Token: 0x06000FE2 RID: 4066 RVA: 0x00057900 File Offset: 0x00055B00
        public void SetVolume(int deviceid, float value)
        {
            MMDevice mmdevice = this.FindDevice(deviceid);
            mmdevice.AudioEndpointVolume.MasterVolumeLevelScalar = value;
        }

        // Token: 0x06000FE3 RID: 4067 RVA: 0x00057928 File Offset: 0x00055B28
        public float GetVolume(int deviceid)
        {
            MMDevice mmdevice = this.FindDevice(deviceid);
            return mmdevice.AudioEndpointVolume.MasterVolumeLevelScalar;
        }

        // Token: 0x06000FE4 RID: 4068 RVA: 0x0005794C File Offset: 0x00055B4C
        public MMDevice FindDevice(int deviceId)
        {
            uint capacity = 0u;
            uint num = 0u;
            AudioVolumeController.waveInMessage(new UIntPtr((uint)deviceId), 2066u, ref capacity, ref num);
            StringBuilder stringBuilder = new StringBuilder((int)capacity);
            AudioVolumeController.waveInMessage(new UIntPtr((uint)deviceId), 2065u, stringBuilder, ref capacity);
            string a = stringBuilder.ToString();
            MMDeviceEnumerator mmdeviceEnumerator = new MMDeviceEnumerator();
            MMDeviceCollection mmdeviceCollection = mmdeviceEnumerator.EnumerateAudioEndPoints(EDataFlow.eCapture, EDeviceState.DEVICE_STATEMASK_ALL);
            MMDevice result = null;
            for (int i = 0; i < mmdeviceCollection.Count; i++)
            {
                MMDevice mmdevice = mmdeviceCollection[i];
                string friendlyName = mmdevice.FriendlyName;
                PropertyStore properties = mmdevice.Properties;
                if (a == mmdevice.ID)
                {
                    result = mmdevice;
                    break;
                }
            }
            return result;
        }

        // Token: 0x06000FE5 RID: 4069 RVA: 0x00057A00 File Offset: 0x00055C00
        public string GetCompleteDeviceName(MMDevice device)
        {
            Guid guid = new Guid("b3f8fa53-0004-438e-9003-51a46e139bfc");
            PropertyStore properties = device.Properties;
            return device.FriendlyName + "(" + properties[guid].Value.ToString() + ")";
        }

        // Token: 0x04000938 RID: 2360
        const uint DRV_RESERVED = 2048u;

        // Token: 0x04000939 RID: 2361
        const uint DRV_QUERYFUNCTIONINSTANCEID = 2065u;

        // Token: 0x0400093A RID: 2362
        const uint DRV_QUERYFUNCTIONINSTANCEIDSIZE = 2066u;
    }
}
