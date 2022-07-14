using CoreAudioApi.Interfaces;
using System;
using System.Runtime.InteropServices;

namespace CoreAudioApi
{
    // Token: 0x020001C6 RID: 454
    public class AudioSessionControl
    {
        // Token: 0x1700069D RID: 1693
        // (get) Token: 0x0600160A RID: 5642 RVA: 0x0006D618 File Offset: 0x0006B818
        public AudioMeterInformation AudioMeterInformation
        {
            get
            {
                return this._AudioMeterInformation;
            }
        }

        // Token: 0x1700069E RID: 1694
        // (get) Token: 0x0600160B RID: 5643 RVA: 0x0006D620 File Offset: 0x0006B820
        public SimpleAudioVolume SimpleAudioVolume
        {
            get
            {
                return this._SimpleAudioVolume;
            }
        }

        // Token: 0x0600160C RID: 5644 RVA: 0x0006D628 File Offset: 0x0006B828
        internal AudioSessionControl(IAudioSessionControl2 realAudioSessionControl)
        {
            IAudioMeterInformation audioMeterInformation = realAudioSessionControl as IAudioMeterInformation;
            ISimpleAudioVolume simpleAudioVolume = realAudioSessionControl as ISimpleAudioVolume;
            if (audioMeterInformation != null)
            {
                this._AudioMeterInformation = new AudioMeterInformation(audioMeterInformation);
            }
            if (simpleAudioVolume != null)
            {
                this._SimpleAudioVolume = new SimpleAudioVolume(simpleAudioVolume);
            }
            this._AudioSessionControl = realAudioSessionControl;
        }

        // Token: 0x0600160D RID: 5645 RVA: 0x0006D678 File Offset: 0x0006B878
        public void RegisterAudioSessionNotification(IAudioSessionEvents eventConsumer)
        {
            Marshal.ThrowExceptionForHR(this._AudioSessionControl.RegisterAudioSessionNotification(eventConsumer));
        }

        // Token: 0x0600160E RID: 5646 RVA: 0x0006D68C File Offset: 0x0006B88C
        public void UnregisterAudioSessionNotification(IAudioSessionEvents eventConsumer)
        {
            Marshal.ThrowExceptionForHR(this._AudioSessionControl.UnregisterAudioSessionNotification(eventConsumer));
        }

        // Token: 0x1700069F RID: 1695
        // (get) Token: 0x0600160F RID: 5647 RVA: 0x0006D6A0 File Offset: 0x0006B8A0
        public AudioSessionState State
        {
            get
            {
                AudioSessionState result;
                Marshal.ThrowExceptionForHR(this._AudioSessionControl.GetState(out result));
                return result;
            }
        }

        // Token: 0x170006A0 RID: 1696
        // (get) Token: 0x06001610 RID: 5648 RVA: 0x0006D6C4 File Offset: 0x0006B8C4
        public string DisplayName
        {
            get
            {
                IntPtr ptr;
                Marshal.ThrowExceptionForHR(this._AudioSessionControl.GetDisplayName(out ptr));
                string result = Marshal.PtrToStringAuto(ptr);
                Marshal.FreeCoTaskMem(ptr);
                return result;
            }
        }

        // Token: 0x170006A1 RID: 1697
        // (get) Token: 0x06001611 RID: 5649 RVA: 0x0006D6F8 File Offset: 0x0006B8F8
        public string IconPath
        {
            get
            {
                IntPtr ptr;
                Marshal.ThrowExceptionForHR(this._AudioSessionControl.GetIconPath(out ptr));
                string result = Marshal.PtrToStringAuto(ptr);
                Marshal.FreeCoTaskMem(ptr);
                return result;
            }
        }

        // Token: 0x170006A2 RID: 1698
        // (get) Token: 0x06001612 RID: 5650 RVA: 0x0006D72C File Offset: 0x0006B92C
        public string SessionIdentifier
        {
            get
            {
                IntPtr ptr;
                Marshal.ThrowExceptionForHR(this._AudioSessionControl.GetSessionIdentifier(out ptr));
                string result = Marshal.PtrToStringAuto(ptr);
                Marshal.FreeCoTaskMem(ptr);
                return result;
            }
        }

        // Token: 0x170006A3 RID: 1699
        // (get) Token: 0x06001613 RID: 5651 RVA: 0x0006D760 File Offset: 0x0006B960
        public string SessionInstanceIdentifier
        {
            get
            {
                IntPtr ptr;
                Marshal.ThrowExceptionForHR(this._AudioSessionControl.GetSessionInstanceIdentifier(out ptr));
                string result = Marshal.PtrToStringAuto(ptr);
                Marshal.FreeCoTaskMem(ptr);
                return result;
            }
        }

        // Token: 0x170006A4 RID: 1700
        // (get) Token: 0x06001614 RID: 5652 RVA: 0x0006D794 File Offset: 0x0006B994
        public uint ProcessID
        {
            get
            {
                uint result;
                Marshal.ThrowExceptionForHR(this._AudioSessionControl.GetProcessId(out result));
                return result;
            }
        }

        // Token: 0x170006A5 RID: 1701
        // (get) Token: 0x06001615 RID: 5653 RVA: 0x0006D7B8 File Offset: 0x0006B9B8
        public bool IsSystemIsSystemSoundsSession
        {
            get
            {
                return this._AudioSessionControl.IsSystemSoundsSession() == 0;
            }
        }

        // Token: 0x04000CA2 RID: 3234
        internal IAudioSessionControl2 _AudioSessionControl;

        // Token: 0x04000CA3 RID: 3235
        internal AudioMeterInformation _AudioMeterInformation;

        // Token: 0x04000CA4 RID: 3236
        internal SimpleAudioVolume _SimpleAudioVolume;
    }
}
