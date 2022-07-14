using System;

namespace WinMM
{
    // Token: 0x020001AF RID: 431
    [Flags]
    public enum WaveFormats
    {
        // Token: 0x04000C5A RID: 3162
        Mono8Bit11Khz = 1,
        // Token: 0x04000C5B RID: 3163
        Stereo8Bit11Khz = 2,
        // Token: 0x04000C5C RID: 3164
        Mono16Bit11Khz = 4,
        // Token: 0x04000C5D RID: 3165
        Stereo16Bit11Khz = 8,
        // Token: 0x04000C5E RID: 3166
        Mono8Bit22Khz = 16,
        // Token: 0x04000C5F RID: 3167
        Stereo8Bit22Khz = 32,
        // Token: 0x04000C60 RID: 3168
        Mono16Bit22Khz = 64,
        // Token: 0x04000C61 RID: 3169
        Stereo16Bit22Khz = 128,
        // Token: 0x04000C62 RID: 3170
        Mono8Bit44Khz = 256,
        // Token: 0x04000C63 RID: 3171
        Stereo8Bit44Khz = 512,
        // Token: 0x04000C64 RID: 3172
        Mono16Bit44Khz = 1024,
        // Token: 0x04000C65 RID: 3173
        Stereo16Bit44Khz = 2048,
        // Token: 0x04000C66 RID: 3174
        Mono8Bit48Khz = 4096,
        // Token: 0x04000C67 RID: 3175
        Stereo8Bit48Khz = 8192,
        // Token: 0x04000C68 RID: 3176
        Mono16Bit48Khz = 16384,
        // Token: 0x04000C69 RID: 3177
        Stereo16Bit48Khz = 32768,
        // Token: 0x04000C6A RID: 3178
        Mono8Bit96Khz = 65536,
        // Token: 0x04000C6B RID: 3179
        Stereo8Bit96Khz = 131072,
        // Token: 0x04000C6C RID: 3180
        Mono16Bit96Khz = 262144,
        // Token: 0x04000C6D RID: 3181
        Stereo16Bit96Khz = 524288
    }
}
