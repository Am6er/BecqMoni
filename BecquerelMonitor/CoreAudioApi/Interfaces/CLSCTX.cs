using System;

namespace CoreAudioApi.Interfaces
{
    // Token: 0x020001DB RID: 475
    [Flags]
    enum CLSCTX : uint
    {
        // Token: 0x04000CEC RID: 3308
        INPROC_SERVER = 1u,
        // Token: 0x04000CED RID: 3309
        INPROC_HANDLER = 2u,
        // Token: 0x04000CEE RID: 3310
        LOCAL_SERVER = 4u,
        // Token: 0x04000CEF RID: 3311
        INPROC_SERVER16 = 8u,
        // Token: 0x04000CF0 RID: 3312
        REMOTE_SERVER = 16u,
        // Token: 0x04000CF1 RID: 3313
        INPROC_HANDLER16 = 32u,
        // Token: 0x04000CF2 RID: 3314
        RESERVED1 = 64u,
        // Token: 0x04000CF3 RID: 3315
        RESERVED2 = 128u,
        // Token: 0x04000CF4 RID: 3316
        RESERVED3 = 256u,
        // Token: 0x04000CF5 RID: 3317
        RESERVED4 = 512u,
        // Token: 0x04000CF6 RID: 3318
        NO_CODE_DOWNLOAD = 1024u,
        // Token: 0x04000CF7 RID: 3319
        RESERVED5 = 2048u,
        // Token: 0x04000CF8 RID: 3320
        NO_CUSTOM_MARSHAL = 4096u,
        // Token: 0x04000CF9 RID: 3321
        ENABLE_CODE_DOWNLOAD = 8192u,
        // Token: 0x04000CFA RID: 3322
        NO_FAILURE_LOG = 16384u,
        // Token: 0x04000CFB RID: 3323
        DISABLE_AAA = 32768u,
        // Token: 0x04000CFC RID: 3324
        ENABLE_AAA = 65536u,
        // Token: 0x04000CFD RID: 3325
        FROM_DEFAULT_CONTEXT = 131072u,
        // Token: 0x04000CFE RID: 3326
        INPROC = 3u,
        // Token: 0x04000CFF RID: 3327
        SERVER = 21u,
        // Token: 0x04000D00 RID: 3328
        ALL = 23u
    }
}
