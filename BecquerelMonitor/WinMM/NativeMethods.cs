using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace WinMM
{
    // Token: 0x020001A4 RID: 420
    sealed class NativeMethods
    {
        // Token: 0x06001506 RID: 5382 RVA: 0x0006B774 File Offset: 0x00069974
        NativeMethods()
        {
        }

        // Token: 0x06001507 RID: 5383 RVA: 0x0006B77C File Offset: 0x0006997C
        public static void Throw(NativeMethods.MMSYSERROR error, NativeMethods.ErrorSource source)
        {
            if (error == NativeMethods.MMSYSERROR.MMSYSERR_NOERROR)
            {
                return;
            }
            StringBuilder stringBuilder = new StringBuilder(255);
            string text = string.Empty;
            NativeMethods.MMSYSERROR mmsyserror = NativeMethods.MMSYSERROR.MMSYSERR_ERROR;
            switch (source)
            {
                case NativeMethods.ErrorSource.WaveIn:
                    mmsyserror = NativeMethods.waveInGetErrorText(error, stringBuilder, (uint)(stringBuilder.Capacity + 1));
                    break;
                case NativeMethods.ErrorSource.WaveOut:
                    mmsyserror = NativeMethods.waveOutGetErrorText(error, stringBuilder, (uint)(stringBuilder.Capacity + 1));
                    break;
            }
            if (mmsyserror != NativeMethods.MMSYSERROR.MMSYSERR_NOERROR)
            {
                string str = error.ToString();
                string str2 = "(";
                int num = (int)error;
                text = str + str2 + num.ToString(CultureInfo.CurrentCulture) + ")";
            }
            else
            {
                text = stringBuilder.ToString() + " (" + error.ToString() + ")";
            }
            if (error == NativeMethods.MMSYSERROR.MMSYSERR_ERROR)
            {
                throw new MMSystemException(text);
            }
            if (error == NativeMethods.MMSYSERROR.MMSYSERR_INVALPARAM)
            {
                throw new InvalidParameterException(text);
            }
            if (error == NativeMethods.MMSYSERROR.MMSYSERR_BADDEVICEID)
            {
                throw new BadDeviceIdException(text);
            }
            if (error == NativeMethods.MMSYSERROR.MMSYSERR_INVALHANDLE)
            {
                throw new InvalidHandleException();
            }
            throw new MMSystemException(text.ToString());
        }

        // Token: 0x06001508 RID: 5384
        [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern uint PlaySound(string lpszSound, IntPtr hmod, NativeMethods.PLAYSOUNDFLAGS fuSound);

        // Token: 0x06001509 RID: 5385
        [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern NativeMethods.MMSYSERROR waveInAddBuffer(WaveInSafeHandle hwi, IntPtr pwh, uint cbwh);

        // Token: 0x0600150A RID: 5386
        [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern NativeMethods.MMSYSERROR waveInClose(WaveInSafeHandle hwi);

        // Token: 0x0600150B RID: 5387
        [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern NativeMethods.MMSYSERROR waveInGetDevCaps(UIntPtr uDeviceID, ref NativeMethods.WAVEINCAPS pwic, uint cbwic);

        // Token: 0x0600150C RID: 5388
        [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern NativeMethods.MMSYSERROR waveInGetErrorText(NativeMethods.MMSYSERROR mmrError, StringBuilder pszText, uint cchText);

        // Token: 0x0600150D RID: 5389
        [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern uint waveInGetNumDevs();

        // Token: 0x0600150E RID: 5390
        [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern NativeMethods.MMSYSERROR waveInOpen(ref IntPtr phwi, uint uDeviceID, ref NativeMethods.WAVEFORMATEX pwfx, NativeMethods.waveInProc dwCallback, IntPtr dwCallbackInstance, NativeMethods.WAVEOPENFLAGS fdwOpen);

        // Token: 0x0600150F RID: 5391
        [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern NativeMethods.MMSYSERROR waveInPrepareHeader(WaveInSafeHandle hwi, IntPtr pwh, uint cbwh);

        // Token: 0x06001510 RID: 5392
        [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern NativeMethods.MMSYSERROR waveInReset(WaveInSafeHandle hwi);

        // Token: 0x06001511 RID: 5393
        [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern NativeMethods.MMSYSERROR waveInStart(WaveInSafeHandle hwi);

        // Token: 0x06001512 RID: 5394
        [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern NativeMethods.MMSYSERROR waveInUnprepareHeader(WaveInSafeHandle hwi, IntPtr pwh, uint cbwh);

        // Token: 0x06001513 RID: 5395
        [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern NativeMethods.MMSYSERROR waveOutClose(WaveOutSafeHandle hwo);

        // Token: 0x06001514 RID: 5396
        [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern NativeMethods.MMSYSERROR waveOutGetDevCaps(UIntPtr uDeviceID, ref NativeMethods.WAVEOUTCAPS pwoc, uint cbwoc);

        // Token: 0x06001515 RID: 5397
        [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern NativeMethods.MMSYSERROR waveOutGetErrorText(NativeMethods.MMSYSERROR mmrError, StringBuilder pszText, uint cchText);

        // Token: 0x06001516 RID: 5398
        [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern uint waveOutGetNumDevs();

        // Token: 0x06001517 RID: 5399
        [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern NativeMethods.MMSYSERROR waveOutGetPitch(WaveOutSafeHandle hwo, ref uint pdwPitch);

        // Token: 0x06001518 RID: 5400
        [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern NativeMethods.MMSYSERROR waveOutGetPlaybackRate(WaveOutSafeHandle hwo, ref uint pdwRate);

        // Token: 0x06001519 RID: 5401
        [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern NativeMethods.MMSYSERROR waveOutGetVolume(WaveOutSafeHandle hwo, ref uint pdwVolume);

        // Token: 0x0600151A RID: 5402
        [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern NativeMethods.MMSYSERROR waveOutGetVolume(UIntPtr uDeviceID, ref uint pdwVolume);

        // Token: 0x0600151B RID: 5403
        [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern NativeMethods.MMSYSERROR waveOutOpen(ref IntPtr phwo, uint uDeviceID, ref NativeMethods.WAVEFORMATEX pwfx, NativeMethods.waveOutProc dwCallback, IntPtr dwCallbackInstance, NativeMethods.WAVEOPENFLAGS dwFlags);

        // Token: 0x0600151C RID: 5404
        [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern NativeMethods.MMSYSERROR waveOutPause(WaveOutSafeHandle hwo);

        // Token: 0x0600151D RID: 5405
        [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern NativeMethods.MMSYSERROR waveOutPrepareHeader(WaveOutSafeHandle hwo, IntPtr pwh, uint cbwh);

        // Token: 0x0600151E RID: 5406
        [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern NativeMethods.MMSYSERROR waveOutReset(WaveOutSafeHandle hwo);

        // Token: 0x0600151F RID: 5407
        [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern NativeMethods.MMSYSERROR waveOutRestart(WaveOutSafeHandle hwo);

        // Token: 0x06001520 RID: 5408
        [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern NativeMethods.MMSYSERROR waveOutSetPitch(WaveOutSafeHandle hwo, uint pdwPitch);

        // Token: 0x06001521 RID: 5409
        [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern NativeMethods.MMSYSERROR waveOutSetPlaybackRate(WaveOutSafeHandle hwo, uint dwRate);

        // Token: 0x06001522 RID: 5410
        [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern NativeMethods.MMSYSERROR waveOutSetVolume(WaveOutSafeHandle hwo, uint dwVolume);

        // Token: 0x06001523 RID: 5411
        [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern NativeMethods.MMSYSERROR waveOutSetVolume(UIntPtr uDeviceID, uint dwVolume);

        // Token: 0x06001524 RID: 5412
        [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern NativeMethods.MMSYSERROR waveOutUnprepareHeader(WaveOutSafeHandle hwo, IntPtr pwh, uint cbwh);

        // Token: 0x06001525 RID: 5413
        [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern NativeMethods.MMSYSERROR waveOutWrite(WaveOutSafeHandle hwo, IntPtr pwh, uint cbwh);

        // Token: 0x0200024F RID: 591
        // (Invoke) Token: 0x06001A7C RID: 6780
        public delegate void waveOutProc(IntPtr hwo, NativeMethods.WAVEOUTMESSAGE uMsg, IntPtr dwInstance, IntPtr dwParam1, IntPtr dwParam2);

        // Token: 0x02000250 RID: 592
        // (Invoke) Token: 0x06001A80 RID: 6784
        public delegate void waveInProc(IntPtr hwi, NativeMethods.WAVEINMESSAGE uMsg, IntPtr dwInstance, IntPtr dwParam1, IntPtr dwParam2);

        // Token: 0x02000251 RID: 593
        [Flags]
        public enum WAVEOPENFLAGS
        {
            // Token: 0x04000E05 RID: 3589
            CALLBACK_NULL = 0,
            // Token: 0x04000E06 RID: 3590
            CALLBACK_WINDOW = 65536,
            // Token: 0x04000E07 RID: 3591
            CALLBACK_THREAD = 131072,
            // Token: 0x04000E08 RID: 3592
            [Obsolete]
            CALLBACK_TASK = 131072,
            // Token: 0x04000E09 RID: 3593
            CALLBACK_FUNCTION = 196608,
            // Token: 0x04000E0A RID: 3594
            WAVE_FORMAT_QUERY = 1,
            // Token: 0x04000E0B RID: 3595
            WAVE_ALLOWSYNC = 2,
            // Token: 0x04000E0C RID: 3596
            WAVE_MAPPED = 4,
            // Token: 0x04000E0D RID: 3597
            WAVE_FORMAT_DIRECT = 8
        }

        // Token: 0x02000252 RID: 594
        public enum WAVEHDRFLAGS
        {
            // Token: 0x04000E0F RID: 3599
            WHDR_BEGINLOOP = 4,
            // Token: 0x04000E10 RID: 3600
            WHDR_DONE = 1,
            // Token: 0x04000E11 RID: 3601
            WHDR_ENDLOOP = 8,
            // Token: 0x04000E12 RID: 3602
            WHDR_INQUEUE = 16,
            // Token: 0x04000E13 RID: 3603
            WHDR_PREPARED = 2
        }

        // Token: 0x02000253 RID: 595
        public enum WAVEOUTMESSAGE
        {
            // Token: 0x04000E15 RID: 3605
            WOM_OPEN = 955,
            // Token: 0x04000E16 RID: 3606
            WOM_CLOSE,
            // Token: 0x04000E17 RID: 3607
            WOM_DONE
        }

        // Token: 0x02000254 RID: 596
        public enum WAVEINMESSAGE
        {
            // Token: 0x04000E19 RID: 3609
            MM_WIM_OPEN = 958,
            // Token: 0x04000E1A RID: 3610
            MM_WIM_CLOSE,
            // Token: 0x04000E1B RID: 3611
            MM_WIM_DATA
        }

        // Token: 0x02000255 RID: 597
        public enum MMSYSERROR
        {
            // Token: 0x04000E1D RID: 3613
            MMSYSERR_NOERROR,
            // Token: 0x04000E1E RID: 3614
            MMSYSERR_ERROR,
            // Token: 0x04000E1F RID: 3615
            MMSYSERR_BADDEVICEID,
            // Token: 0x04000E20 RID: 3616
            MMSYSERR_NOTENABLED,
            // Token: 0x04000E21 RID: 3617
            MMSYSERR_ALLOCATED,
            // Token: 0x04000E22 RID: 3618
            MMSYSERR_INVALHANDLE,
            // Token: 0x04000E23 RID: 3619
            MMSYSERR_NODRIVER,
            // Token: 0x04000E24 RID: 3620
            MMSYSERR_NOMEM,
            // Token: 0x04000E25 RID: 3621
            MMSYSERR_NOTSUPPORTED,
            // Token: 0x04000E26 RID: 3622
            MMSYSERR_BADERRNUM,
            // Token: 0x04000E27 RID: 3623
            MMSYSERR_INVALFLAG,
            // Token: 0x04000E28 RID: 3624
            MMSYSERR_INVALPARAM,
            // Token: 0x04000E29 RID: 3625
            MMSYSERR_HANDLEBUSY,
            // Token: 0x04000E2A RID: 3626
            MMSYSERR_INVALIDALIAS,
            // Token: 0x04000E2B RID: 3627
            MMSYSERR_BADDB,
            // Token: 0x04000E2C RID: 3628
            MMSYSERR_KEYNOTFOUND,
            // Token: 0x04000E2D RID: 3629
            MMSYSERR_READERROR,
            // Token: 0x04000E2E RID: 3630
            MMSYSERR_WRITEERROR,
            // Token: 0x04000E2F RID: 3631
            MMSYSERR_DELETEERROR,
            // Token: 0x04000E30 RID: 3632
            MMSYSERR_VALNOTFOUND,
            // Token: 0x04000E31 RID: 3633
            MMSYSERR_NODRIVERCB,
            // Token: 0x04000E32 RID: 3634
            MMSYSERR_MOREDATA,
            // Token: 0x04000E33 RID: 3635
            WAVERR_BADFORMAT = 32,
            // Token: 0x04000E34 RID: 3636
            WAVERR_STILLPLAYING,
            // Token: 0x04000E35 RID: 3637
            WAVERR_UNPREPARED,
            // Token: 0x04000E36 RID: 3638
            WAVERR_SYNC,
            // Token: 0x04000E37 RID: 3639
            MIDIERR_UNPREPARED = 64,
            // Token: 0x04000E38 RID: 3640
            MIDIERR_STILLPLAYING,
            // Token: 0x04000E39 RID: 3641
            MIDIERR_NOMAP,
            // Token: 0x04000E3A RID: 3642
            MIDIERR_NOTREADY,
            // Token: 0x04000E3B RID: 3643
            MIDIERR_NODEVICE,
            // Token: 0x04000E3C RID: 3644
            MIDIERR_INVALIDSETUP,
            // Token: 0x04000E3D RID: 3645
            MIDIERR_BADOPENMODE,
            // Token: 0x04000E3E RID: 3646
            MIDIERR_DONT_CONTINUE,
            // Token: 0x04000E3F RID: 3647
            TIMERR_NOCANDO = 97,
            // Token: 0x04000E40 RID: 3648
            TIMERR_STRUCT = 129,
            // Token: 0x04000E41 RID: 3649
            JOYERR_PARMS = 165,
            // Token: 0x04000E42 RID: 3650
            JOYERR_NOCANDO,
            // Token: 0x04000E43 RID: 3651
            JOYERR_UNPLUGGED,
            // Token: 0x04000E44 RID: 3652
            MCIERR_INVALID_DEVICE_ID = 257,
            // Token: 0x04000E45 RID: 3653
            MCIERR_UNRECOGNIZED_KEYWORD = 259,
            // Token: 0x04000E46 RID: 3654
            MCIERR_UNRECOGNIZED_COMMAND = 261,
            // Token: 0x04000E47 RID: 3655
            MCIERR_HARDWARE,
            // Token: 0x04000E48 RID: 3656
            MCIERR_INVALID_DEVICE_NAME,
            // Token: 0x04000E49 RID: 3657
            MCIERR_OUT_OF_MEMORY,
            // Token: 0x04000E4A RID: 3658
            MCIERR_DEVICE_OPEN,
            // Token: 0x04000E4B RID: 3659
            MCIERR_CANNOT_LOAD_DRIVER,
            // Token: 0x04000E4C RID: 3660
            MCIERR_MISSING_COMMAND_STRING,
            // Token: 0x04000E4D RID: 3661
            MCIERR_PARAM_OVERFLOW,
            // Token: 0x04000E4E RID: 3662
            MCIERR_MISSING_STRING_ARGUMENT,
            // Token: 0x04000E4F RID: 3663
            MCIERR_BAD_INTEGER,
            // Token: 0x04000E50 RID: 3664
            MCIERR_PARSER_INTERNAL,
            // Token: 0x04000E51 RID: 3665
            MCIERR_DRIVER_INTERNAL,
            // Token: 0x04000E52 RID: 3666
            MCIERR_MISSING_PARAMETER,
            // Token: 0x04000E53 RID: 3667
            MCIERR_UNSUPPORTED_FUNCTION,
            // Token: 0x04000E54 RID: 3668
            MCIERR_FILE_NOT_FOUND,
            // Token: 0x04000E55 RID: 3669
            MCIERR_DEVICE_NOT_READY,
            // Token: 0x04000E56 RID: 3670
            MCIERR_INTERNAL,
            // Token: 0x04000E57 RID: 3671
            MCIERR_DRIVER,
            // Token: 0x04000E58 RID: 3672
            MCIERR_CANNOT_USE_ALL,
            // Token: 0x04000E59 RID: 3673
            MCIERR_MULTIPLE,
            // Token: 0x04000E5A RID: 3674
            MCIERR_EXTENSION_NOT_FOUND,
            // Token: 0x04000E5B RID: 3675
            MCIERR_OUTOFRANGE,
            // Token: 0x04000E5C RID: 3676
            MCIERR_FLAGS_NOT_COMPATIBLE = 284,
            // Token: 0x04000E5D RID: 3677
            MCIERR_FILE_NOT_SAVED = 286,
            // Token: 0x04000E5E RID: 3678
            MCIERR_DEVICE_TYPE_REQUIRED,
            // Token: 0x04000E5F RID: 3679
            MCIERR_DEVICE_LOCKED,
            // Token: 0x04000E60 RID: 3680
            MCIERR_DUPLICATE_ALIAS,
            // Token: 0x04000E61 RID: 3681
            MCIERR_BAD_CONSTANT,
            // Token: 0x04000E62 RID: 3682
            MCIERR_MUST_USE_SHAREABLE,
            // Token: 0x04000E63 RID: 3683
            MCIERR_MISSING_DEVICE_NAME,
            // Token: 0x04000E64 RID: 3684
            MCIERR_BAD_TIME_FORMAT,
            // Token: 0x04000E65 RID: 3685
            MCIERR_NO_CLOSING_QUOTE,
            // Token: 0x04000E66 RID: 3686
            MCIERR_DUPLICATE_FLAGS,
            // Token: 0x04000E67 RID: 3687
            MCIERR_INVALID_FILE,
            // Token: 0x04000E68 RID: 3688
            MCIERR_NULL_PARAMETER_BLOCK,
            // Token: 0x04000E69 RID: 3689
            MCIERR_UNNAMED_RESOURCE,
            // Token: 0x04000E6A RID: 3690
            MCIERR_NEW_REQUIRES_ALIAS,
            // Token: 0x04000E6B RID: 3691
            MCIERR_NOTIFY_ON_AUTO_OPEN,
            // Token: 0x04000E6C RID: 3692
            MCIERR_NO_ELEMENT_ALLOWED,
            // Token: 0x04000E6D RID: 3693
            MCIERR_NONAPPLICABLE_FUNCTION,
            // Token: 0x04000E6E RID: 3694
            MCIERR_ILLEGAL_FOR_AUTO_OPEN,
            // Token: 0x04000E6F RID: 3695
            MCIERR_FILENAME_REQUIRED,
            // Token: 0x04000E70 RID: 3696
            MCIERR_EXTRA_CHARACTERS,
            // Token: 0x04000E71 RID: 3697
            MCIERR_DEVICE_NOT_INSTALLED,
            // Token: 0x04000E72 RID: 3698
            MCIERR_GET_CD,
            // Token: 0x04000E73 RID: 3699
            MCIERR_SET_CD,
            // Token: 0x04000E74 RID: 3700
            MCIERR_SET_DRIVE,
            // Token: 0x04000E75 RID: 3701
            MCIERR_DEVICE_LENGTH,
            // Token: 0x04000E76 RID: 3702
            MCIERR_DEVICE_ORD_LENGTH,
            // Token: 0x04000E77 RID: 3703
            MCIERR_NO_INTEGER,
            // Token: 0x04000E78 RID: 3704
            MCIERR_WAVE_OUTPUTSINUSE = 320,
            // Token: 0x04000E79 RID: 3705
            MCIERR_WAVE_SETOUTPUTINUSE,
            // Token: 0x04000E7A RID: 3706
            MCIERR_WAVE_INPUTSINUSE,
            // Token: 0x04000E7B RID: 3707
            MCIERR_WAVE_SETINPUTINUSE,
            // Token: 0x04000E7C RID: 3708
            MCIERR_WAVE_OUTPUTUNSPECIFIED,
            // Token: 0x04000E7D RID: 3709
            MCIERR_WAVE_INPUTUNSPECIFIED,
            // Token: 0x04000E7E RID: 3710
            MCIERR_WAVE_OUTPUTSUNSUITABLE,
            // Token: 0x04000E7F RID: 3711
            MCIERR_WAVE_SETOUTPUTUNSUITABLE,
            // Token: 0x04000E80 RID: 3712
            MCIERR_WAVE_INPUTSUNSUITABLE,
            // Token: 0x04000E81 RID: 3713
            MCIERR_WAVE_SETINPUTUNSUITABLE,
            // Token: 0x04000E82 RID: 3714
            MCIERR_SEQ_DIV_INCOMPATIBLE = 336,
            // Token: 0x04000E83 RID: 3715
            MCIERR_SEQ_PORT_INUSE,
            // Token: 0x04000E84 RID: 3716
            MCIERR_SEQ_PORT_NONEXISTENT,
            // Token: 0x04000E85 RID: 3717
            MCIERR_SEQ_PORT_MAPNODEVICE,
            // Token: 0x04000E86 RID: 3718
            MCIERR_SEQ_PORT_MISCERROR,
            // Token: 0x04000E87 RID: 3719
            MCIERR_SEQ_TIMER,
            // Token: 0x04000E88 RID: 3720
            MCIERR_SEQ_PORTUNSPECIFIED,
            // Token: 0x04000E89 RID: 3721
            MCIERR_SEQ_NOMIDIPRESENT,
            // Token: 0x04000E8A RID: 3722
            MCIERR_NO_WINDOW = 346,
            // Token: 0x04000E8B RID: 3723
            MCIERR_CREATEWINDOW,
            // Token: 0x04000E8C RID: 3724
            MCIERR_FILE_READ,
            // Token: 0x04000E8D RID: 3725
            MCIERR_FILE_WRITE,
            // Token: 0x04000E8E RID: 3726
            MCIERR_NO_IDENTITY,
            // Token: 0x04000E8F RID: 3727
            MIXERR_INVALLINE = 1024,
            // Token: 0x04000E90 RID: 3728
            MIXERR_INVALCONTROL,
            // Token: 0x04000E91 RID: 3729
            MIXERR_INVALVALUE,
            // Token: 0x04000E92 RID: 3730
            MIXERR_LASTERROR = 1026
        }

        // Token: 0x02000256 RID: 598
        [Flags]
        public enum WAVECAPS
        {
            // Token: 0x04000E94 RID: 3732
            WAVECAPS_PITCH = 1,
            // Token: 0x04000E95 RID: 3733
            WAVECAPS_PLAYBACKRATE = 2,
            // Token: 0x04000E96 RID: 3734
            WAVECAPS_VOLUME = 4,
            // Token: 0x04000E97 RID: 3735
            WAVECAPS_LRVOLUME = 8,
            // Token: 0x04000E98 RID: 3736
            WAVECAPS_SYNC = 16,
            // Token: 0x04000E99 RID: 3737
            WAVECAPS_SAMPLEACCURATE = 32,
            // Token: 0x04000E9A RID: 3738
            WAVECAPS_DIRECTSOUND = 64
        }

        // Token: 0x02000257 RID: 599
        [Flags]
        public enum WAVEFORMATS
        {
            // Token: 0x04000E9C RID: 3740
            WAVE_FORMAT_1M08 = 1,
            // Token: 0x04000E9D RID: 3741
            WAVE_FORMAT_1S08 = 2,
            // Token: 0x04000E9E RID: 3742
            WAVE_FORMAT_1M16 = 4,
            // Token: 0x04000E9F RID: 3743
            WAVE_FORMAT_1S16 = 8,
            // Token: 0x04000EA0 RID: 3744
            WAVE_FORMAT_2M08 = 16,
            // Token: 0x04000EA1 RID: 3745
            WAVE_FORMAT_2S08 = 32,
            // Token: 0x04000EA2 RID: 3746
            WAVE_FORMAT_2M16 = 64,
            // Token: 0x04000EA3 RID: 3747
            WAVE_FORMAT_2S16 = 128,
            // Token: 0x04000EA4 RID: 3748
            WAVE_FORMAT_4M08 = 256,
            // Token: 0x04000EA5 RID: 3749
            WAVE_FORMAT_4S08 = 512,
            // Token: 0x04000EA6 RID: 3750
            WAVE_FORMAT_4M16 = 1024,
            // Token: 0x04000EA7 RID: 3751
            WAVE_FORMAT_4S16 = 2048,
            // Token: 0x04000EA8 RID: 3752
            WAVE_FORMAT_48M08 = 4096,
            // Token: 0x04000EA9 RID: 3753
            WAVE_FORMAT_48S08 = 8192,
            // Token: 0x04000EAA RID: 3754
            WAVE_FORMAT_48M16 = 16384,
            // Token: 0x04000EAB RID: 3755
            WAVE_FORMAT_48S16 = 32768,
            // Token: 0x04000EAC RID: 3756
            WAVE_FORMAT_96M08 = 65536,
            // Token: 0x04000EAD RID: 3757
            WAVE_FORMAT_96S08 = 131072,
            // Token: 0x04000EAE RID: 3758
            WAVE_FORMAT_96M16 = 262144,
            // Token: 0x04000EAF RID: 3759
            WAVE_FORMAT_96S16 = 524288
        }

        // Token: 0x02000258 RID: 600
        public enum WAVEFORMATTAG
        {
            // Token: 0x04000EB1 RID: 3761
            WAVE_FORMAT_PCM = 1,
            // Token: 0x04000EB2 RID: 3762
            WAVE_FORMAT_ADPCM
        }

        // Token: 0x02000259 RID: 601
        [Flags]
        public enum PLAYSOUNDFLAGS
        {
            // Token: 0x04000EB4 RID: 3764
            SND_SYNC = 0,
            // Token: 0x04000EB5 RID: 3765
            SND_ASYNC = 1,
            // Token: 0x04000EB6 RID: 3766
            SND_NODEFAULT = 2,
            // Token: 0x04000EB7 RID: 3767
            SND_MEMORY = 4,
            // Token: 0x04000EB8 RID: 3768
            SND_LOOP = 8,
            // Token: 0x04000EB9 RID: 3769
            SND_NOSTOP = 16,
            // Token: 0x04000EBA RID: 3770
            SND_PURGE = 64,
            // Token: 0x04000EBB RID: 3771
            SND_APPLICATION = 128,
            // Token: 0x04000EBC RID: 3772
            SND_NOWAIT = 8192,
            // Token: 0x04000EBD RID: 3773
            SND_ALIAS = 65536,
            // Token: 0x04000EBE RID: 3774
            SND_FILENAME = 131072,
            // Token: 0x04000EBF RID: 3775
            SND_RESOURCE = 262148,
            // Token: 0x04000EC0 RID: 3776
            SND_ALIAS_ID = 1114112
        }

        // Token: 0x0200025A RID: 602
        public enum ErrorSource
        {
            // Token: 0x04000EC2 RID: 3778
            WaveIn,
            // Token: 0x04000EC3 RID: 3779
            WaveOut
        }

        // Token: 0x0200025B RID: 603
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        internal struct WAVEOUTCAPS
        {
            // Token: 0x04000EC4 RID: 3780
            public ushort wMid;

            // Token: 0x04000EC5 RID: 3781
            public ushort wPid;

            // Token: 0x04000EC6 RID: 3782
            public uint vDriverVersion;

            // Token: 0x04000EC7 RID: 3783
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szPname;

            // Token: 0x04000EC8 RID: 3784
            public NativeMethods.WAVEFORMATS dwFormats;

            // Token: 0x04000EC9 RID: 3785
            public ushort wChannels;

            // Token: 0x04000ECA RID: 3786
            public ushort wReserved1;

            // Token: 0x04000ECB RID: 3787
            public NativeMethods.WAVECAPS dwSupport;
        }

        // Token: 0x0200025C RID: 604
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        internal struct WAVEINCAPS
        {
            // Token: 0x04000ECC RID: 3788
            public ushort wMid;

            // Token: 0x04000ECD RID: 3789
            public ushort wPid;

            // Token: 0x04000ECE RID: 3790
            public uint vDriverVersion;

            // Token: 0x04000ECF RID: 3791
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szPname;

            // Token: 0x04000ED0 RID: 3792
            public uint dwFormats;

            // Token: 0x04000ED1 RID: 3793
            public ushort wChannels;

            // Token: 0x04000ED2 RID: 3794
            public ushort wReserved1;
        }

        // Token: 0x0200025D RID: 605
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        internal struct WAVEHDR
        {
            // Token: 0x04000ED3 RID: 3795
            public IntPtr lpData;

            // Token: 0x04000ED4 RID: 3796
            public uint dwBufferLength;

            // Token: 0x04000ED5 RID: 3797
            public uint dwBytesRecorded;

            // Token: 0x04000ED6 RID: 3798
            public IntPtr dwUser;

            // Token: 0x04000ED7 RID: 3799
            public NativeMethods.WAVEHDRFLAGS dwFlags;

            // Token: 0x04000ED8 RID: 3800
            public uint dwLoops;

            // Token: 0x04000ED9 RID: 3801
            public IntPtr lpNext;

            // Token: 0x04000EDA RID: 3802
            public uint reserved;
        }

        // Token: 0x0200025E RID: 606
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        internal struct WAVEFORMATEX
        {
            // Token: 0x04000EDB RID: 3803
            public short wFormatTag;

            // Token: 0x04000EDC RID: 3804
            public short nChannels;

            // Token: 0x04000EDD RID: 3805
            public int nSamplesPerSec;

            // Token: 0x04000EDE RID: 3806
            public int nAvgBytesPerSec;

            // Token: 0x04000EDF RID: 3807
            public short nBlockAlign;

            // Token: 0x04000EE0 RID: 3808
            public short wBitsPerSample;

            // Token: 0x04000EE1 RID: 3809
            public short cbSize;
        }

        // Token: 0x0200025F RID: 607
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        internal struct WAVEFORMATEXTENSIBLE
        {
            // Token: 0x04000EE2 RID: 3810
            public short wFormatTag;

            // Token: 0x04000EE3 RID: 3811
            public short nChannels;

            // Token: 0x04000EE4 RID: 3812
            public int nSamplesPerSec;

            // Token: 0x04000EE5 RID: 3813
            public int nAvgBytesPerSec;

            // Token: 0x04000EE6 RID: 3814
            public short nBlockAlign;

            // Token: 0x04000EE7 RID: 3815
            public short wBitsPerSample;

            // Token: 0x04000EE8 RID: 3816
            public short cbSize;

            // Token: 0x04000EE9 RID: 3817
            public ushort wValidBitsPerSample;

            // Token: 0x04000EEA RID: 3818
            public uint dwChannelMask;

            // Token: 0x04000EEB RID: 3819
            public Guid SubFormat;
        }

        // Token: 0x02000260 RID: 608
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        internal struct MMTIME
        {
            // Token: 0x04000EEC RID: 3820
            public uint wType;

            // Token: 0x04000EED RID: 3821
            public uint wData1;

            // Token: 0x04000EEE RID: 3822
            public uint wData2;
        }
    }
}
