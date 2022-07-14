using System;

namespace CoreAudioApi
{
    // Token: 0x020001C0 RID: 448
    public class AudioVolumeNotificationData
    {
        // Token: 0x17000691 RID: 1681
        // (get) Token: 0x060015E8 RID: 5608 RVA: 0x0006D350 File Offset: 0x0006B550
        public Guid EventContext
        {
            get
            {
                return this._EventContext;
            }
        }

        // Token: 0x17000692 RID: 1682
        // (get) Token: 0x060015E9 RID: 5609 RVA: 0x0006D358 File Offset: 0x0006B558
        public bool Muted
        {
            get
            {
                return this._Muted;
            }
        }

        // Token: 0x17000693 RID: 1683
        // (get) Token: 0x060015EA RID: 5610 RVA: 0x0006D360 File Offset: 0x0006B560
        public float MasterVolume
        {
            get
            {
                return this._MasterVolume;
            }
        }

        // Token: 0x17000694 RID: 1684
        // (get) Token: 0x060015EB RID: 5611 RVA: 0x0006D368 File Offset: 0x0006B568
        public int Channels
        {
            get
            {
                return this._Channels;
            }
        }

        // Token: 0x17000695 RID: 1685
        // (get) Token: 0x060015EC RID: 5612 RVA: 0x0006D370 File Offset: 0x0006B570
        public float[] ChannelVolume
        {
            get
            {
                return this._ChannelVolume;
            }
        }

        // Token: 0x060015ED RID: 5613 RVA: 0x0006D378 File Offset: 0x0006B578
        public AudioVolumeNotificationData(Guid eventContext, bool muted, float masterVolume, float[] channelVolume)
        {
            this._EventContext = eventContext;
            this._Muted = muted;
            this._MasterVolume = masterVolume;
            this._Channels = channelVolume.Length;
            this._ChannelVolume = channelVolume;
        }

        // Token: 0x04000C95 RID: 3221
        Guid _EventContext;

        // Token: 0x04000C96 RID: 3222
        bool _Muted;

        // Token: 0x04000C97 RID: 3223
        float _MasterVolume;

        // Token: 0x04000C98 RID: 3224
        int _Channels;

        // Token: 0x04000C99 RID: 3225
        float[] _ChannelVolume;
    }
}
