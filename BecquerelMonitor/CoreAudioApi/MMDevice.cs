using CoreAudioApi.Interfaces;
using System;
using System.Runtime.InteropServices;

namespace CoreAudioApi
{
    // Token: 0x020001B2 RID: 434
    public class MMDevice
    {
        // Token: 0x060015A6 RID: 5542 RVA: 0x0006CEB8 File Offset: 0x0006B0B8
        void GetPropertyInformation()
        {
            IPropertyStore store;
            Marshal.ThrowExceptionForHR(this._RealDevice.OpenPropertyStore(EStgmAccess.STGM_READ, out store));
            this._PropertyStore = new PropertyStore(store);
        }

        // Token: 0x060015A7 RID: 5543 RVA: 0x0006CEE8 File Offset: 0x0006B0E8
        void GetAudioSessionManager()
        {
            object obj;
            Marshal.ThrowExceptionForHR(this._RealDevice.Activate(ref MMDevice.IID_IAudioSessionManager, CLSCTX.ALL, IntPtr.Zero, out obj));
            this._AudioSessionManager = new AudioSessionManager(obj as IAudioSessionManager2);
        }

        // Token: 0x060015A8 RID: 5544 RVA: 0x0006CF28 File Offset: 0x0006B128
        void GetAudioMeterInformation()
        {
            object obj;
            Marshal.ThrowExceptionForHR(this._RealDevice.Activate(ref MMDevice.IID_IAudioMeterInformation, CLSCTX.ALL, IntPtr.Zero, out obj));
            this._AudioMeterInformation = new AudioMeterInformation(obj as IAudioMeterInformation);
        }

        // Token: 0x060015A9 RID: 5545 RVA: 0x0006CF68 File Offset: 0x0006B168
        void GetAudioEndpointVolume()
        {
            object obj;
            Marshal.ThrowExceptionForHR(this._RealDevice.Activate(ref MMDevice.IID_IAudioEndpointVolume, CLSCTX.ALL, IntPtr.Zero, out obj));
            this._AudioEndpointVolume = new AudioEndpointVolume(obj as IAudioEndpointVolume);
        }

        // Token: 0x17000684 RID: 1668
        // (get) Token: 0x060015AA RID: 5546 RVA: 0x0006CFA8 File Offset: 0x0006B1A8
        public AudioSessionManager AudioSessionManager
        {
            get
            {
                if (this._AudioSessionManager == null)
                {
                    this.GetAudioSessionManager();
                }
                return this._AudioSessionManager;
            }
        }

        // Token: 0x17000685 RID: 1669
        // (get) Token: 0x060015AB RID: 5547 RVA: 0x0006CFC4 File Offset: 0x0006B1C4
        public AudioMeterInformation AudioMeterInformation
        {
            get
            {
                if (this._AudioMeterInformation == null)
                {
                    this.GetAudioMeterInformation();
                }
                return this._AudioMeterInformation;
            }
        }

        // Token: 0x17000686 RID: 1670
        // (get) Token: 0x060015AC RID: 5548 RVA: 0x0006CFE0 File Offset: 0x0006B1E0
        public AudioEndpointVolume AudioEndpointVolume
        {
            get
            {
                if (this._AudioEndpointVolume == null)
                {
                    this.GetAudioEndpointVolume();
                }
                return this._AudioEndpointVolume;
            }
        }

        // Token: 0x17000687 RID: 1671
        // (get) Token: 0x060015AD RID: 5549 RVA: 0x0006CFFC File Offset: 0x0006B1FC
        public PropertyStore Properties
        {
            get
            {
                if (this._PropertyStore == null)
                {
                    this.GetPropertyInformation();
                }
                return this._PropertyStore;
            }
        }

        // Token: 0x17000688 RID: 1672
        // (get) Token: 0x060015AE RID: 5550 RVA: 0x0006D018 File Offset: 0x0006B218
        public string FriendlyName
        {
            get
            {
                if (this._PropertyStore == null)
                {
                    this.GetPropertyInformation();
                }
                if (this._PropertyStore.Contains(PKEY.PKEY_Device_FriendlyName))
                {
                    return (string)this._PropertyStore[PKEY.PKEY_Device_FriendlyName].Value;
                }
                return "Unknown";
            }
        }

        // Token: 0x17000689 RID: 1673
        // (get) Token: 0x060015AF RID: 5551 RVA: 0x0006D070 File Offset: 0x0006B270
        public string InterfaceFriendlyName
        {
            get
            {
                if (this._PropertyStore == null)
                {
                    this.GetPropertyInformation();
                }
                if (this._PropertyStore.Contains(PKEY.PKEY_DeviceInterface_FriendlyName))
                {
                    return (string)this._PropertyStore[PKEY.PKEY_DeviceInterface_FriendlyName].Value;
                }
                return "Unknown";
            }
        }

        // Token: 0x1700068A RID: 1674
        // (get) Token: 0x060015B0 RID: 5552 RVA: 0x0006D0C8 File Offset: 0x0006B2C8
        public string DeviceDesc
        {
            get
            {
                if (this._PropertyStore == null)
                {
                    this.GetPropertyInformation();
                }
                if (this._PropertyStore.Contains(PKEY.PKEY_Device_DeviceDesc))
                {
                    return (string)this._PropertyStore[PKEY.PKEY_Device_DeviceDesc].Value;
                }
                return "Unknown";
            }
        }

        // Token: 0x1700068B RID: 1675
        // (get) Token: 0x060015B1 RID: 5553 RVA: 0x0006D120 File Offset: 0x0006B320
        public string ID
        {
            get
            {
                string result;
                Marshal.ThrowExceptionForHR(this._RealDevice.GetId(out result));
                return result;
            }
        }

        // Token: 0x1700068C RID: 1676
        // (get) Token: 0x060015B2 RID: 5554 RVA: 0x0006D144 File Offset: 0x0006B344
        public EDataFlow DataFlow
        {
            get
            {
                IMMEndpoint immendpoint = this._RealDevice as IMMEndpoint;
                EDataFlow result;
                immendpoint.GetDataFlow(out result);
                return result;
            }
        }

        // Token: 0x1700068D RID: 1677
        // (get) Token: 0x060015B3 RID: 5555 RVA: 0x0006D16C File Offset: 0x0006B36C
        public EDeviceState State
        {
            get
            {
                EDeviceState result;
                Marshal.ThrowExceptionForHR(this._RealDevice.GetState(out result));
                return result;
            }
        }

        // Token: 0x060015B4 RID: 5556 RVA: 0x0006D190 File Offset: 0x0006B390
        internal MMDevice(IMMDevice realDevice)
        {
            this._RealDevice = realDevice;
        }

        // Token: 0x04000C83 RID: 3203
        IMMDevice _RealDevice;

        // Token: 0x04000C84 RID: 3204
        PropertyStore _PropertyStore;

        // Token: 0x04000C85 RID: 3205
        AudioMeterInformation _AudioMeterInformation;

        // Token: 0x04000C86 RID: 3206
        AudioEndpointVolume _AudioEndpointVolume;

        // Token: 0x04000C87 RID: 3207
        AudioSessionManager _AudioSessionManager;

        // Token: 0x04000C88 RID: 3208
        static Guid IID_IAudioMeterInformation = typeof(IAudioMeterInformation).GUID;

        // Token: 0x04000C89 RID: 3209
        static Guid IID_IAudioEndpointVolume = typeof(IAudioEndpointVolume).GUID;

        // Token: 0x04000C8A RID: 3210
        static Guid IID_IAudioSessionManager = typeof(IAudioSessionManager2).GUID;
    }
}
