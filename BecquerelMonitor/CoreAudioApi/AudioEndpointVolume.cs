using CoreAudioApi.Interfaces;
using System;
using System.Runtime.InteropServices;

namespace CoreAudioApi
{
    // Token: 0x020001CC RID: 460
    public class AudioEndpointVolume : IDisposable
    {
        // Token: 0x14000068 RID: 104
        // (add) Token: 0x06001626 RID: 5670 RVA: 0x0006D944 File Offset: 0x0006BB44
        // (remove) Token: 0x06001627 RID: 5671 RVA: 0x0006D980 File Offset: 0x0006BB80
        public event AudioEndpointVolumeNotificationDelegate OnVolumeNotification;

        // Token: 0x170006AD RID: 1709
        // (get) Token: 0x06001628 RID: 5672 RVA: 0x0006D9BC File Offset: 0x0006BBBC
        public AudioEndPointVolumeVolumeRange VolumeRange
        {
            get
            {
                return this._VolumeRange;
            }
        }

        // Token: 0x170006AE RID: 1710
        // (get) Token: 0x06001629 RID: 5673 RVA: 0x0006D9C4 File Offset: 0x0006BBC4
        public EEndpointHardwareSupport HardwareSupport
        {
            get
            {
                return this._HardwareSupport;
            }
        }

        // Token: 0x170006AF RID: 1711
        // (get) Token: 0x0600162A RID: 5674 RVA: 0x0006D9CC File Offset: 0x0006BBCC
        public AudioEndpointVolumeStepInformation StepInformation
        {
            get
            {
                return this._StepInformation;
            }
        }

        // Token: 0x170006B0 RID: 1712
        // (get) Token: 0x0600162B RID: 5675 RVA: 0x0006D9D4 File Offset: 0x0006BBD4
        public AudioEndpointVolumeChannels Channels
        {
            get
            {
                return this._Channels;
            }
        }

        // Token: 0x170006B1 RID: 1713
        // (get) Token: 0x0600162C RID: 5676 RVA: 0x0006D9DC File Offset: 0x0006BBDC
        // (set) Token: 0x0600162D RID: 5677 RVA: 0x0006DA00 File Offset: 0x0006BC00
        public float MasterVolumeLevel
        {
            get
            {
                float result;
                Marshal.ThrowExceptionForHR(this._AudioEndPointVolume.GetMasterVolumeLevel(out result));
                return result;
            }
            set
            {
                Marshal.ThrowExceptionForHR(this._AudioEndPointVolume.SetMasterVolumeLevel(value, Guid.Empty));
            }
        }

        // Token: 0x170006B2 RID: 1714
        // (get) Token: 0x0600162E RID: 5678 RVA: 0x0006DA18 File Offset: 0x0006BC18
        // (set) Token: 0x0600162F RID: 5679 RVA: 0x0006DA3C File Offset: 0x0006BC3C
        public float MasterVolumeLevelScalar
        {
            get
            {
                float result;
                Marshal.ThrowExceptionForHR(this._AudioEndPointVolume.GetMasterVolumeLevelScalar(out result));
                return result;
            }
            set
            {
                Marshal.ThrowExceptionForHR(this._AudioEndPointVolume.SetMasterVolumeLevelScalar(value, Guid.Empty));
            }
        }

        // Token: 0x170006B3 RID: 1715
        // (get) Token: 0x06001630 RID: 5680 RVA: 0x0006DA54 File Offset: 0x0006BC54
        // (set) Token: 0x06001631 RID: 5681 RVA: 0x0006DA78 File Offset: 0x0006BC78
        public bool Mute
        {
            get
            {
                bool result;
                Marshal.ThrowExceptionForHR(this._AudioEndPointVolume.GetMute(out result));
                return result;
            }
            set
            {
                Marshal.ThrowExceptionForHR(this._AudioEndPointVolume.SetMute(value, Guid.Empty));
            }
        }

        // Token: 0x06001632 RID: 5682 RVA: 0x0006DA90 File Offset: 0x0006BC90
        public void VolumeStepUp()
        {
            Marshal.ThrowExceptionForHR(this._AudioEndPointVolume.VolumeStepUp(Guid.Empty));
        }

        // Token: 0x06001633 RID: 5683 RVA: 0x0006DAA8 File Offset: 0x0006BCA8
        public void VolumeStepDown()
        {
            Marshal.ThrowExceptionForHR(this._AudioEndPointVolume.VolumeStepDown(Guid.Empty));
        }

        // Token: 0x06001634 RID: 5684 RVA: 0x0006DAC0 File Offset: 0x0006BCC0
        internal AudioEndpointVolume(IAudioEndpointVolume realEndpointVolume)
        {
            this._AudioEndPointVolume = realEndpointVolume;
            this._Channels = new AudioEndpointVolumeChannels(this._AudioEndPointVolume);
            this._StepInformation = new AudioEndpointVolumeStepInformation(this._AudioEndPointVolume);
            uint hardwareSupport;
            Marshal.ThrowExceptionForHR(this._AudioEndPointVolume.QueryHardwareSupport(out hardwareSupport));
            this._HardwareSupport = (EEndpointHardwareSupport)hardwareSupport;
            this._VolumeRange = new AudioEndPointVolumeVolumeRange(this._AudioEndPointVolume);
            this._CallBack = new AudioEndpointVolumeCallback(this);
            Marshal.ThrowExceptionForHR(this._AudioEndPointVolume.RegisterControlChangeNotify(this._CallBack));
        }

        // Token: 0x06001635 RID: 5685 RVA: 0x0006DB4C File Offset: 0x0006BD4C
        internal void FireNotification(AudioVolumeNotificationData NotificationData)
        {
            AudioEndpointVolumeNotificationDelegate onVolumeNotification = this.OnVolumeNotification;
            if (onVolumeNotification != null)
            {
                onVolumeNotification(NotificationData);
            }
        }

        // Token: 0x06001636 RID: 5686 RVA: 0x0006DB74 File Offset: 0x0006BD74
        public void Dispose()
        {
            if (this._CallBack != null)
            {
                Marshal.ThrowExceptionForHR(this._AudioEndPointVolume.UnregisterControlChangeNotify(this._CallBack));
                this._CallBack = null;
            }
        }

        // Token: 0x06001637 RID: 5687 RVA: 0x0006DBA0 File Offset: 0x0006BDA0
        ~AudioEndpointVolume()
        {
            this.Dispose();
        }

        // Token: 0x04000CB1 RID: 3249
        IAudioEndpointVolume _AudioEndPointVolume;

        // Token: 0x04000CB2 RID: 3250
        AudioEndpointVolumeChannels _Channels;

        // Token: 0x04000CB3 RID: 3251
        AudioEndpointVolumeStepInformation _StepInformation;

        // Token: 0x04000CB4 RID: 3252
        AudioEndPointVolumeVolumeRange _VolumeRange;

        // Token: 0x04000CB5 RID: 3253
        EEndpointHardwareSupport _HardwareSupport;

        // Token: 0x04000CB6 RID: 3254
        AudioEndpointVolumeCallback _CallBack;
    }
}
