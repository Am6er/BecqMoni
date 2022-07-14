using CoreAudioApi.Interfaces;
using System;
using System.Runtime.InteropServices;

namespace CoreAudioApi
{
    // Token: 0x020001B8 RID: 440
    class AudioEndpointVolumeCallback : IAudioEndpointVolumeCallback
    {
        // Token: 0x060015BE RID: 5566 RVA: 0x0006D284 File Offset: 0x0006B484
        internal AudioEndpointVolumeCallback(AudioEndpointVolume parent)
        {
            this._Parent = parent;
        }

        // Token: 0x060015BF RID: 5567 RVA: 0x0006D294 File Offset: 0x0006B494
        [PreserveSig]
        public int OnNotify(IntPtr NotifyData)
        {
            AUDIO_VOLUME_NOTIFICATION_DATA audio_VOLUME_NOTIFICATION_DATA = (AUDIO_VOLUME_NOTIFICATION_DATA)Marshal.PtrToStructure(NotifyData, typeof(AUDIO_VOLUME_NOTIFICATION_DATA));
            IntPtr value = Marshal.OffsetOf(typeof(AUDIO_VOLUME_NOTIFICATION_DATA), "ChannelVolume");
            IntPtr ptr = (IntPtr)((long)NotifyData + (long)value);
            float[] array = new float[audio_VOLUME_NOTIFICATION_DATA.nChannels];
            int num = 0;
            while ((long)num < (long)((ulong)audio_VOLUME_NOTIFICATION_DATA.nChannels))
            {
                array[num] = (float)Marshal.PtrToStructure(ptr, typeof(float));
                num++;
            }
            AudioVolumeNotificationData notificationData = new AudioVolumeNotificationData(audio_VOLUME_NOTIFICATION_DATA.guidEventContext, audio_VOLUME_NOTIFICATION_DATA.bMuted, audio_VOLUME_NOTIFICATION_DATA.fMasterVolume, array);
            this._Parent.FireNotification(notificationData);
            return 0;
        }

        // Token: 0x04000C92 RID: 3218
        AudioEndpointVolume _Parent;
    }
}
