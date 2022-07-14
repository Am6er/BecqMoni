using BecquerelMonitor.WinMM.Properties;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Xml;

namespace WinMM
{
    // Token: 0x0200019E RID: 414
    public sealed class WaveOut : IDisposable
    {
        // Token: 0x060014D9 RID: 5337 RVA: 0x0006AA84 File Offset: 0x00068C84
        public WaveOut(int deviceId)
        {
            if (deviceId >= WaveOut.DeviceCount && deviceId != -1)
            {
                throw new ArgumentOutOfRangeException("deviceId", "The Device ID specified was not within the valid range.");
            }
            this.callback = new NativeMethods.waveOutProc(this.InternalCallback);
            this.deviceId = deviceId;
        }

        // Token: 0x060014DA RID: 5338 RVA: 0x0006AAF8 File Offset: 0x00068CF8
        ~WaveOut()
        {
            this.Dispose(false);
        }

        // Token: 0x14000066 RID: 102
        // (add) Token: 0x060014DB RID: 5339 RVA: 0x0006AB28 File Offset: 0x00068D28
        // (remove) Token: 0x060014DC RID: 5340 RVA: 0x0006AB64 File Offset: 0x00068D64
        public event EventHandler<WaveOutMessageReceivedEventArgs> MessageReceived;

        // Token: 0x1700063A RID: 1594
        // (get) Token: 0x060014DD RID: 5341 RVA: 0x0006ABA0 File Offset: 0x00068DA0
        public static ReadOnlyCollection<WaveOutDeviceCaps> Devices
        {
            get
            {
                return WaveOut.GetAllDeviceCaps().AsReadOnly();
            }
        }

        // Token: 0x1700063B RID: 1595
        // (get) Token: 0x060014DE RID: 5342 RVA: 0x0006ABAC File Offset: 0x00068DAC
        public WaveOutDeviceCaps Capabilities
        {
            get
            {
                if (this.capabilities == null)
                {
                    this.capabilities = WaveOut.GetDeviceCaps(this.deviceId);
                }
                return this.capabilities;
            }
        }

        // Token: 0x1700063C RID: 1596
        // (get) Token: 0x060014DF RID: 5343 RVA: 0x0006ABD0 File Offset: 0x00068DD0
        // (set) Token: 0x060014E0 RID: 5344 RVA: 0x0006AC74 File Offset: 0x00068E74
        public Volume Volume
        {
            get
            {
                uint num = 0u;
                if (this.handle != null && !this.handle.IsInvalid && !this.handle.IsClosed)
                {
                    NativeMethods.Throw(NativeMethods.waveOutGetVolume(this.handle, ref num), NativeMethods.ErrorSource.WaveOut);
                }
                else
                {
                    NativeMethods.Throw(NativeMethods.waveOutGetVolume((UIntPtr)((uint)this.deviceId), ref num), NativeMethods.ErrorSource.WaveOut);
                }
                uint num2 = num & 65535u;
                uint num3 = num >> 16;
                return new Volume
                {
                    Left = num2 / 65535f,
                    Right = num3 / 65535f
                };
            }
            set
            {
                float num = Math.Min(Math.Max(value.Left, 0f), 1f);
                float num2 = Math.Min(Math.Max(value.Right, 0f), 1f);
                uint num3 = (uint)(65535f * num);
                uint num4 = (uint)(65535f * num2);
                uint dwVolume = num3 | num4 << 16;
                if (this.handle != null && !this.handle.IsInvalid && !this.handle.IsClosed)
                {
                    NativeMethods.Throw(NativeMethods.waveOutSetVolume(this.handle, dwVolume), NativeMethods.ErrorSource.WaveOut);
                    return;
                }
                NativeMethods.Throw(NativeMethods.waveOutSetVolume((UIntPtr)((uint)this.deviceId), dwVolume), NativeMethods.ErrorSource.WaveOut);
            }
        }

        // Token: 0x1700063D RID: 1597
        // (get) Token: 0x060014E1 RID: 5345 RVA: 0x0006AD30 File Offset: 0x00068F30
        // (set) Token: 0x060014E2 RID: 5346 RVA: 0x0006AD5C File Offset: 0x00068F5C
        public float Pitch
        {
            get
            {
                uint value = 0u;
                NativeMethods.Throw(NativeMethods.waveOutGetPitch(this.handle, ref value), NativeMethods.ErrorSource.WaveOut);
                return WaveOut.FixedToFloat(value);
            }
            set
            {
                NativeMethods.Throw(NativeMethods.waveOutSetPitch(this.handle, WaveOut.FloatToFixed(value)), NativeMethods.ErrorSource.WaveOut);
            }
        }

        // Token: 0x1700063E RID: 1598
        // (get) Token: 0x060014E3 RID: 5347 RVA: 0x0006AD78 File Offset: 0x00068F78
        // (set) Token: 0x060014E4 RID: 5348 RVA: 0x0006ADA4 File Offset: 0x00068FA4
        public float PlaybackRate
        {
            get
            {
                uint value = 0u;
                NativeMethods.Throw(NativeMethods.waveOutGetPlaybackRate(this.handle, ref value), NativeMethods.ErrorSource.WaveOut);
                return WaveOut.FixedToFloat(value);
            }
            set
            {
                NativeMethods.Throw(NativeMethods.waveOutSetPlaybackRate(this.handle, WaveOut.FloatToFixed(value)), NativeMethods.ErrorSource.WaveOut);
            }
        }

        // Token: 0x1700063F RID: 1599
        // (get) Token: 0x060014E5 RID: 5349 RVA: 0x0006ADC0 File Offset: 0x00068FC0
        static int DeviceCount
        {
            get
            {
                return (int)NativeMethods.waveOutGetNumDevs();
            }
        }

        // Token: 0x17000640 RID: 1600
        // (get) Token: 0x060014E6 RID: 5350 RVA: 0x0006ADC8 File Offset: 0x00068FC8
        static XmlDocument Manufacturers
        {
            get
            {
                if (WaveOut.manufacturers == null)
                {
                    XmlDocument xmlDocument = new XmlDocument();
                    xmlDocument.LoadXml(Resources.Devices);
                    WaveOut.manufacturers = xmlDocument;
                }
                return WaveOut.manufacturers;
            }
        }

        // Token: 0x060014E7 RID: 5351 RVA: 0x0006AE00 File Offset: 0x00069000
        public void Open(WaveFormat waveFormat)
        {
            lock (this.startStopLock)
            {
                if (this.handle != null)
                {
                    throw new InvalidOperationException("The device is already open.");
                }
                NativeMethods.WAVEFORMATEX waveformatex = default(NativeMethods.WAVEFORMATEX);
                waveformatex.nAvgBytesPerSec = waveFormat.AverageBytesPerSecond;
                waveformatex.wBitsPerSample = waveFormat.BitsPerSample;
                waveformatex.nBlockAlign = waveFormat.BlockAlign;
                waveformatex.nChannels = waveFormat.Channels;
                waveformatex.wFormatTag = (short)waveFormat.FormatTag;
                waveformatex.nSamplesPerSec = waveFormat.SamplesPerSecond;
                waveformatex.cbSize = 0;
                IntPtr tempHandle = new IntPtr(0);
                NativeMethods.Throw(NativeMethods.waveOutOpen(ref tempHandle, (uint)this.deviceId, ref waveformatex, this.callback, (IntPtr)0, NativeMethods.WAVEOPENFLAGS.CALLBACK_WINDOW | NativeMethods.WAVEOPENFLAGS.CALLBACK_THREAD | NativeMethods.WAVEOPENFLAGS.WAVE_FORMAT_DIRECT), NativeMethods.ErrorSource.WaveOut);
                this.handle = new WaveOutSafeHandle(tempHandle);
                lock (this.bufferingLock)
                {
                    this.buffering = true;
                    Monitor.Pulse(this.bufferingLock);
                }
                this.bufferMaintainerThread = new Thread(new ThreadStart(this.MaintainBuffers));
                this.bufferMaintainerThread.IsBackground = true;
                this.bufferMaintainerThread.Name = "WaveOut MaintainBuffers thread. (DeviceID = " + this.deviceId + ")";
                this.bufferMaintainerThread.Start();
            }
        }

        // Token: 0x060014E8 RID: 5352 RVA: 0x0006AF88 File Offset: 0x00069188
        public void Close()
        {
            lock (this.startStopLock)
            {
                if (this.handle != null && !this.handle.IsClosed && !this.handle.IsInvalid)
                {
                    this.Stop();
                    lock (this.bufferingLock)
                    {
                        this.buffering = false;
                        Monitor.Pulse(this.bufferingLock);
                    }
                    this.bufferMaintainerThread.Join();
                    this.handle.Close();
                    this.handle = null;
                }
            }
        }

        // Token: 0x060014E9 RID: 5353 RVA: 0x0006B044 File Offset: 0x00069244
        public void Write(byte[] bufferData)
        {
            lock (this.startStopLock)
            {
                IntPtr intPtr = Marshal.AllocHGlobal(bufferData.Length);
                Marshal.Copy(bufferData, 0, intPtr, bufferData.Length);
                NativeMethods.WAVEHDR wavehdr = new NativeMethods.WAVEHDR
                {
                    dwBufferLength = (uint)bufferData.Length,
                    dwFlags = (NativeMethods.WAVEHDRFLAGS)0,
                    lpData = intPtr,
                    dwUser = new IntPtr(12345)
                };
                IntPtr intPtr2 = Marshal.AllocHGlobal(Marshal.SizeOf(wavehdr));
                Marshal.StructureToPtr(wavehdr, intPtr2, false);
                NativeMethods.Throw(NativeMethods.waveOutPrepareHeader(this.handle, intPtr2, (uint)Marshal.SizeOf(typeof(NativeMethods.WAVEHDR))), NativeMethods.ErrorSource.WaveOut);
                NativeMethods.Throw(NativeMethods.waveOutWrite(this.handle, intPtr2, (uint)Marshal.SizeOf(typeof(NativeMethods.WAVEHDR))), NativeMethods.ErrorSource.WaveOut);
                lock (this.bufferingLock)
                {
                    this.bufferQueueCount++;
                    Monitor.Pulse(this.bufferingLock);
                }
            }
        }

        // Token: 0x060014EA RID: 5354 RVA: 0x0006B164 File Offset: 0x00069364
        public void Pause()
        {
            lock (this.startStopLock)
            {
                NativeMethods.Throw(NativeMethods.waveOutPause(this.handle), NativeMethods.ErrorSource.WaveOut);
            }
        }

        // Token: 0x060014EB RID: 5355 RVA: 0x0006B1AC File Offset: 0x000693AC
        public void Resume()
        {
            lock (this.startStopLock)
            {
                NativeMethods.Throw(NativeMethods.waveOutRestart(this.handle), NativeMethods.ErrorSource.WaveOut);
            }
        }

        // Token: 0x060014EC RID: 5356 RVA: 0x0006B1F4 File Offset: 0x000693F4
        public void Stop()
        {
            lock (this.startStopLock)
            {
                NativeMethods.Throw(NativeMethods.waveOutReset(this.handle), NativeMethods.ErrorSource.WaveOut);
            }
        }

        // Token: 0x060014ED RID: 5357 RVA: 0x0006B23C File Offset: 0x0006943C
        public bool SupportsFormat(WaveFormat waveFormat)
        {
            NativeMethods.WAVEFORMATEX waveformatex = default(NativeMethods.WAVEFORMATEX);
            waveformatex.nAvgBytesPerSec = waveFormat.AverageBytesPerSecond;
            waveformatex.wBitsPerSample = waveFormat.BitsPerSample;
            waveformatex.nBlockAlign = waveFormat.BlockAlign;
            waveformatex.nChannels = waveFormat.Channels;
            waveformatex.wFormatTag = (short)waveFormat.FormatTag;
            waveformatex.nSamplesPerSec = waveFormat.SamplesPerSecond;
            waveformatex.cbSize = 0;
            IntPtr intPtr = new IntPtr(0);
            NativeMethods.MMSYSERROR mmsyserror = NativeMethods.waveOutOpen(ref intPtr, (uint)this.deviceId, ref waveformatex, null, (IntPtr)0, NativeMethods.WAVEOPENFLAGS.WAVE_FORMAT_QUERY);
            if (mmsyserror == NativeMethods.MMSYSERROR.MMSYSERR_NOERROR)
            {
                return true;
            }
            if (mmsyserror == NativeMethods.MMSYSERROR.WAVERR_BADFORMAT)
            {
                return false;
            }
            NativeMethods.Throw(mmsyserror, NativeMethods.ErrorSource.WaveOut);
            return false;
        }

        // Token: 0x060014EE RID: 5358 RVA: 0x0006B2E8 File Offset: 0x000694E8
        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        // Token: 0x060014EF RID: 5359 RVA: 0x0006B2F8 File Offset: 0x000694F8
        static WaveOutDeviceCaps GetDeviceCaps(int deviceId)
        {
            NativeMethods.WAVEOUTCAPS waveoutcaps = default(NativeMethods.WAVEOUTCAPS);
            NativeMethods.waveOutGetDevCaps(new UIntPtr((uint)deviceId), ref waveoutcaps, (uint)Marshal.SizeOf(waveoutcaps.GetType()));
            return new WaveOutDeviceCaps
            {
                DeviceId = deviceId,
                Channels = (int)waveoutcaps.wChannels,
                DriverVersion = (int)waveoutcaps.vDriverVersion,
                Manufacturer = WaveOut.GetManufacturer(waveoutcaps.wMid),
                Name = waveoutcaps.szPname,
                ProductId = (int)waveoutcaps.wPid,
                Capabilities = waveoutcaps.dwSupport
            };
        }

        // Token: 0x060014F0 RID: 5360 RVA: 0x0006B390 File Offset: 0x00069590
        static string GetManufacturer(ushort manufacturerId)
        {
            XmlDocument xmlDocument = WaveOut.Manufacturers;
            XmlElement xmlElement = null;
            if (xmlDocument != null)
            {
                xmlElement = (XmlElement)xmlDocument.SelectSingleNode("/devices/manufacturer[@id='" + manufacturerId.ToString(CultureInfo.InvariantCulture) + "']");
            }
            if (xmlElement == null)
            {
                return "Unknown [" + manufacturerId + "]";
            }
            return xmlElement.GetAttribute("name");
        }

        // Token: 0x060014F1 RID: 5361 RVA: 0x0006B400 File Offset: 0x00069600
        static uint FloatToFixed(float value)
        {
            short num = (short)value;
            ushort num2 = (ushort)((value - (float)num) * 65535f);
            return (uint)((int)num << 8 | (int)((uint)num2 >> 8));
        }

        // Token: 0x060014F2 RID: 5362 RVA: 0x0006B428 File Offset: 0x00069628
        static float FixedToFloat(uint value)
        {
            short num = (short)(value >> 8);
            ushort num2 = (ushort)value;
            return (float)num + (float)num2 / 65535f;
        }

        // Token: 0x060014F3 RID: 5363 RVA: 0x0006B44C File Offset: 0x0006964C
        static List<WaveOutDeviceCaps> GetAllDeviceCaps()
        {
            List<WaveOutDeviceCaps> list = new List<WaveOutDeviceCaps>();
            int deviceCount = WaveOut.DeviceCount;
            for (int i = 0; i < deviceCount; i++)
            {
                list.Add(WaveOut.GetDeviceCaps(i));
            }
            list.Add(WaveOut.GetDeviceCaps(-1));
            return list;
        }

        // Token: 0x060014F4 RID: 5364 RVA: 0x0006B494 File Offset: 0x00069694
        void InternalCallback(IntPtr waveOutHandle, NativeMethods.WAVEOUTMESSAGE message, IntPtr instance, IntPtr param1, IntPtr param2)
        {
            if (message == NativeMethods.WAVEOUTMESSAGE.WOM_DONE)
            {
                lock (this.bufferingLock)
                {
                    this.bufferReleaseQueue.Enqueue(param1);
                    this.bufferQueueCount--;
                    Monitor.Pulse(this.bufferingLock);
                }
            }
            if (this.MessageReceived != null)
            {
                this.MessageReceived(this, new WaveOutMessageReceivedEventArgs((WaveOutMessage)message));
            }
        }

        // Token: 0x060014F5 RID: 5365 RVA: 0x0006B518 File Offset: 0x00069718
        void MaintainBuffers()
        {
            try
            {
                while (this.buffering || this.bufferQueueCount > 0 || this.bufferReleaseQueue.Count > 0)
                {
                    lock (this.bufferingLock)
                    {
                        while (this.bufferReleaseQueue.Count == 0 && (this.bufferQueueCount > 0 || this.buffering))
                        {
                            Monitor.Wait(this.bufferingLock, 1000);
                        }
                        goto IL_5B;
                    }
                    goto IL_55;
                IL_5B:
                    if (this.bufferReleaseQueue.Count <= 0)
                    {
                        continue;
                    }
                IL_55:
                    this.ProcessDone();
                    goto IL_5B;
                }
            }
            catch (ThreadAbortException)
            {
            }
        }

        // Token: 0x060014F6 RID: 5366 RVA: 0x0006B5D8 File Offset: 0x000697D8
        void ProcessDone()
        {
            IntPtr intPtr;
            lock (this.bufferingLock)
            {
                intPtr = this.bufferReleaseQueue.Dequeue();
                Monitor.Pulse(this.bufferingLock);
            }
            IntPtr lpData = ((NativeMethods.WAVEHDR)Marshal.PtrToStructure(intPtr, typeof(NativeMethods.WAVEHDR))).lpData;
            NativeMethods.Throw(NativeMethods.waveOutUnprepareHeader(this.handle, intPtr, (uint)Marshal.SizeOf(typeof(NativeMethods.WAVEHDR))), NativeMethods.ErrorSource.WaveOut);
            Marshal.FreeHGlobal(lpData);
            Marshal.FreeHGlobal(intPtr);
        }

        // Token: 0x060014F7 RID: 5367 RVA: 0x0006B670 File Offset: 0x00069870
        void Dispose(bool disposing)
        {
            if (this.handle != null)
            {
                this.Close();
            }
        }

        // Token: 0x04000C2A RID: 3114
        public const int WaveOutMapperDeviceId = -1;

        // Token: 0x04000C2B RID: 3115
        static XmlDocument manufacturers;

        // Token: 0x04000C2C RID: 3116
        int deviceId;

        // Token: 0x04000C2D RID: 3117
        WaveOutDeviceCaps capabilities;

        // Token: 0x04000C2E RID: 3118
        object startStopLock = new object();

        // Token: 0x04000C2F RID: 3119
        object bufferingLock = new object();

        // Token: 0x04000C30 RID: 3120
        bool buffering;

        // Token: 0x04000C31 RID: 3121
        int bufferQueueCount;

        // Token: 0x04000C32 RID: 3122
        Queue<IntPtr> bufferReleaseQueue = new Queue<IntPtr>();

        // Token: 0x04000C33 RID: 3123
        Thread bufferMaintainerThread;

        // Token: 0x04000C34 RID: 3124
        WaveOutSafeHandle handle;

        // Token: 0x04000C35 RID: 3125
        NativeMethods.waveOutProc callback;
    }
}
