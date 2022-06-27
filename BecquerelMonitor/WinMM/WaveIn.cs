using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Xml;
using BecquerelMonitor.WinMM.Properties;

namespace WinMM
{
	// Token: 0x020001B1 RID: 433
	public sealed class WaveIn : IDisposable
	{
		// Token: 0x0600158D RID: 5517 RVA: 0x0006C424 File Offset: 0x0006A624
		public WaveIn(int deviceId)
		{
			if (deviceId >= WaveIn.DeviceCount && deviceId != -1)
			{
				throw new ArgumentOutOfRangeException("deviceId", "The Device ID specified was not within the valid range.");
			}
			this.callback = new NativeMethods.waveInProc(this.InternalCallback);
			this.deviceId = deviceId;
		}

		// Token: 0x0600158E RID: 5518 RVA: 0x0006C4AC File Offset: 0x0006A6AC
		~WaveIn()
		{
			this.Dispose(false);
		}

		// Token: 0x14000067 RID: 103
		// (add) Token: 0x0600158F RID: 5519 RVA: 0x0006C4DC File Offset: 0x0006A6DC
		// (remove) Token: 0x06001590 RID: 5520 RVA: 0x0006C518 File Offset: 0x0006A718
		public event EventHandler<DataReadyEventArgs> DataReady;

		// Token: 0x1700067F RID: 1663
		// (get) Token: 0x06001591 RID: 5521 RVA: 0x0006C554 File Offset: 0x0006A754
		public static ReadOnlyCollection<WaveInDeviceCaps> Devices
		{
			get
			{
				return WaveIn.GetAllDeviceCaps().AsReadOnly();
			}
		}

		// Token: 0x17000680 RID: 1664
		// (get) Token: 0x06001592 RID: 5522 RVA: 0x0006C560 File Offset: 0x0006A760
		// (set) Token: 0x06001593 RID: 5523 RVA: 0x0006C568 File Offset: 0x0006A768
		public int BufferSize
		{
			get
			{
				return this.bufferSize;
			}
			set
			{
				if (value <= 0)
				{
					throw new ArgumentOutOfRangeException("value", "The value you specified is too small");
				}
				this.bufferSize = value;
			}
		}

		// Token: 0x17000681 RID: 1665
		// (get) Token: 0x06001594 RID: 5524 RVA: 0x0006C588 File Offset: 0x0006A788
		// (set) Token: 0x06001595 RID: 5525 RVA: 0x0006C590 File Offset: 0x0006A790
		public int BufferQueueSize
		{
			get
			{
				return this.bufferQueueSize;
			}
			set
			{
				if (this.bufferQueueSize <= 0)
				{
					throw new ArgumentOutOfRangeException("value", "The value you specified is too small");
				}
				this.bufferQueueSize = value;
			}
		}

		// Token: 0x17000682 RID: 1666
		// (get) Token: 0x06001596 RID: 5526 RVA: 0x0006C5B8 File Offset: 0x0006A7B8
		static int DeviceCount
		{
			get
			{
				return (int)NativeMethods.waveInGetNumDevs();
			}
		}

		// Token: 0x17000683 RID: 1667
		// (get) Token: 0x06001597 RID: 5527 RVA: 0x0006C5C0 File Offset: 0x0006A7C0
		static XmlDocument Manufacturers
		{
			get
			{
				if (WaveIn.manufacturers == null)
				{
					XmlDocument xmlDocument = new XmlDocument();
                    xmlDocument.LoadXml(BecquerelMonitor.WinMM.Properties.Resources.Devices);
					WaveIn.manufacturers = xmlDocument;
				}
				return WaveIn.manufacturers;
			}
		}

		// Token: 0x06001598 RID: 5528 RVA: 0x0006C5F8 File Offset: 0x0006A7F8
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
				this.recordingFormat = waveFormat.Clone();
                IntPtr tempHandle = new IntPtr(0);
				NativeMethods.Throw(NativeMethods.waveInOpen(ref tempHandle, (uint)this.deviceId, ref waveformatex, this.callback, (IntPtr)0, NativeMethods.WAVEOPENFLAGS.CALLBACK_WINDOW | NativeMethods.WAVEOPENFLAGS.CALLBACK_THREAD | NativeMethods.WAVEOPENFLAGS.WAVE_FORMAT_DIRECT), NativeMethods.ErrorSource.WaveOut);
				this.handle = new WaveInSafeHandle(tempHandle);
			}
		}

		// Token: 0x06001599 RID: 5529 RVA: 0x0006C6EC File Offset: 0x0006A8EC
		public void Close()
		{
			lock (this.startStopLock)
			{
				if (this.handle != null)
				{
					if (!this.handle.IsClosed && !this.handle.IsInvalid)
					{
						this.Stop();
						this.handle.Close();
					}
					this.handle = null;
				}
			}
		}

		// Token: 0x0600159A RID: 5530 RVA: 0x0006C768 File Offset: 0x0006A968
		public void Start()
		{
			lock (this.startStopLock)
			{
				if (this.bufferMaintainerThread != null)
				{
					throw new InvalidOperationException("The device has already been started.");
				}
				lock (this.bufferingLock)
				{
					this.buffering = true;
					Monitor.Pulse(this.bufferingLock);
				}
				this.bufferMaintainerThread = new Thread(new ThreadStart(this.MaintainBuffers));
				this.bufferMaintainerThread.IsBackground = true;
				this.bufferMaintainerThread.Name = "WaveIn MaintainBuffers thread. (DeviceID = " + this.deviceId + ")";
				this.bufferMaintainerThread.Start();
				NativeMethods.Throw(NativeMethods.waveInStart(this.handle), NativeMethods.ErrorSource.WaveIn);
			}
		}

		// Token: 0x0600159B RID: 5531 RVA: 0x0006C850 File Offset: 0x0006AA50
		public void Stop()
		{
			lock (this.startStopLock)
			{
				if (this.bufferMaintainerThread != null)
				{
					lock (this.bufferingLock)
					{
						this.buffering = false;
						Monitor.Pulse(this.bufferingLock);
					}
					NativeMethods.Throw(NativeMethods.waveInReset(this.handle), NativeMethods.ErrorSource.WaveIn);
					this.bufferMaintainerThread.Join();
					this.bufferMaintainerThread = null;
				}
			}
		}

		// Token: 0x0600159C RID: 5532 RVA: 0x0006C8EC File Offset: 0x0006AAEC
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
			NativeMethods.MMSYSERROR mmsyserror = NativeMethods.waveInOpen(ref intPtr, (uint)this.deviceId, ref waveformatex, null, (IntPtr)0, NativeMethods.WAVEOPENFLAGS.WAVE_FORMAT_QUERY);
			if (mmsyserror == NativeMethods.MMSYSERROR.MMSYSERR_NOERROR)
			{
				return true;
			}
			if (mmsyserror == NativeMethods.MMSYSERROR.WAVERR_BADFORMAT)
			{
				return false;
			}
			NativeMethods.Throw(mmsyserror, NativeMethods.ErrorSource.WaveIn);
			return false;
		}

		// Token: 0x0600159D RID: 5533 RVA: 0x0006C998 File Offset: 0x0006AB98
		public void Dispose()
		{
			this.Dispose(true);
			GC.SuppressFinalize(this);
		}

		// Token: 0x0600159E RID: 5534 RVA: 0x0006C9A8 File Offset: 0x0006ABA8
		static WaveInDeviceCaps GetDeviceCaps(int deviceId)
		{
			NativeMethods.WAVEINCAPS waveincaps = default(NativeMethods.WAVEINCAPS);
			NativeMethods.waveInGetDevCaps(new UIntPtr((uint)deviceId), ref waveincaps, (uint)Marshal.SizeOf(waveincaps.GetType()));
			return new WaveInDeviceCaps
			{
				DeviceId = deviceId,
				Channels = (int)waveincaps.wChannels,
				DriverVersion = (int)waveincaps.vDriverVersion,
				Manufacturer = WaveIn.GetManufacturer(waveincaps.wMid),
				Name = waveincaps.szPname,
				ProductId = (int)waveincaps.wPid
			};
		}

		// Token: 0x0600159F RID: 5535 RVA: 0x0006CA34 File Offset: 0x0006AC34
		static string GetManufacturer(ushort manufacturerId)
		{
			XmlDocument xmlDocument = WaveIn.Manufacturers;
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

		// Token: 0x060015A0 RID: 5536 RVA: 0x0006CAA4 File Offset: 0x0006ACA4
		static List<WaveInDeviceCaps> GetAllDeviceCaps()
		{
			List<WaveInDeviceCaps> list = new List<WaveInDeviceCaps>();
			int deviceCount = WaveIn.DeviceCount;
			for (int i = 0; i < deviceCount; i++)
			{
				list.Add(WaveIn.GetDeviceCaps(i));
			}
			list.Add(WaveIn.GetDeviceCaps(-1));
			return list;
		}

		// Token: 0x060015A1 RID: 5537 RVA: 0x0006CAEC File Offset: 0x0006ACEC
		void MaintainBuffers()
		{
			try
			{
				while (this.buffering)
				{
					lock (this.bufferingLock)
					{
						while (this.bufferQueueCount >= this.bufferQueueSize && this.bufferReleaseQueue.Count == 0 && this.buffering)
						{
							Monitor.Wait(this.bufferingLock);
						}
						goto IL_5E;
					}
					goto IL_58;
					IL_5E:
					if (this.bufferQueueCount < this.bufferQueueSize)
					{
						if (this.buffering)
						{
							goto IL_58;
						}
					}
					while (this.bufferReleaseQueue.Count > 0)
					{
						this.ProcessDone();
					}
					continue;
					IL_58:
					this.AddBuffer();
					goto IL_5E;
				}
				while (this.bufferReleaseQueue.Count > 0 || this.bufferQueueCount > 0)
				{
					lock (this.bufferingLock)
					{
						while (this.bufferReleaseQueue.Count == 0)
						{
							Monitor.Wait(this.bufferingLock, 1000);
						}
						goto IL_E2;
					}
					goto IL_DC;
					IL_E2:
					if (this.bufferReleaseQueue.Count <= 0)
					{
						continue;
					}
					IL_DC:
					this.ProcessDone();
					goto IL_E2;
				}
			}
			catch (ThreadAbortException)
			{
			}
		}

		// Token: 0x060015A2 RID: 5538 RVA: 0x0006CC58 File Offset: 0x0006AE58
		void AddBuffer()
		{
			int num = this.bufferSize * (int)this.recordingFormat.BlockAlign;
			IntPtr lpData = Marshal.AllocHGlobal(num);
			NativeMethods.WAVEHDR wavehdr = new NativeMethods.WAVEHDR
			{
				dwBufferLength = (uint)num,
				dwFlags = (NativeMethods.WAVEHDRFLAGS)0,
				lpData = lpData,
				dwUser = new IntPtr(12345)
			};
			IntPtr intPtr = Marshal.AllocHGlobal(Marshal.SizeOf(wavehdr));
			Marshal.StructureToPtr(wavehdr, intPtr, false);
			NativeMethods.Throw(NativeMethods.waveInPrepareHeader(this.handle, intPtr, (uint)Marshal.SizeOf(typeof(NativeMethods.WAVEHDR))), NativeMethods.ErrorSource.WaveOut);
			NativeMethods.Throw(NativeMethods.waveInAddBuffer(this.handle, intPtr, (uint)Marshal.SizeOf(typeof(NativeMethods.WAVEHDR))), NativeMethods.ErrorSource.WaveOut);
			lock (this.bufferingLock)
			{
				this.bufferQueueCount++;
				Monitor.Pulse(this.bufferingLock);
			}
		}

		// Token: 0x060015A3 RID: 5539 RVA: 0x0006CD54 File Offset: 0x0006AF54
		void ProcessDone()
		{
			IntPtr intPtr;
			lock (this.bufferingLock)
			{
				intPtr = this.bufferReleaseQueue.Dequeue();
				Monitor.Pulse(this.bufferingLock);
			}
			NativeMethods.WAVEHDR wavehdr = (NativeMethods.WAVEHDR)Marshal.PtrToStructure(intPtr, typeof(NativeMethods.WAVEHDR));
			IntPtr lpData = wavehdr.lpData;
			if (wavehdr.dwBytesRecorded > 0u && this.DataReady != null)
			{
				byte[] array = new byte[wavehdr.dwBytesRecorded];
				Marshal.Copy(lpData, array, 0, (int)wavehdr.dwBytesRecorded);
				this.DataReady(this, new DataReadyEventArgs(array));
			}
			NativeMethods.Throw(NativeMethods.waveInUnprepareHeader(this.handle, intPtr, (uint)Marshal.SizeOf(typeof(NativeMethods.WAVEHDR))), NativeMethods.ErrorSource.WaveIn);
			Marshal.FreeHGlobal(lpData);
			Marshal.FreeHGlobal(intPtr);
		}

		// Token: 0x060015A4 RID: 5540 RVA: 0x0006CE38 File Offset: 0x0006B038
		void InternalCallback(IntPtr waveInHandle, NativeMethods.WAVEINMESSAGE message, IntPtr instance, IntPtr param1, IntPtr param2)
		{
			if (message == NativeMethods.WAVEINMESSAGE.MM_WIM_DATA)
			{
				lock (this.bufferingLock)
				{
					this.bufferReleaseQueue.Enqueue(param1);
					this.bufferQueueCount--;
					Monitor.Pulse(this.bufferingLock);
				}
			}
		}

		// Token: 0x060015A5 RID: 5541 RVA: 0x0006CEA0 File Offset: 0x0006B0A0
		void Dispose(bool disposing)
		{
			if (this.handle != null)
			{
				this.Close();
			}
		}

		// Token: 0x04000C74 RID: 3188
		public const int WaveInMapperDeviceId = -1;

		// Token: 0x04000C75 RID: 3189
		static XmlDocument manufacturers;

		// Token: 0x04000C76 RID: 3190
		object startStopLock = new object();

		// Token: 0x04000C77 RID: 3191
		object bufferingLock = new object();

		// Token: 0x04000C78 RID: 3192
		WaveFormat recordingFormat;

		// Token: 0x04000C79 RID: 3193
		bool buffering;

		// Token: 0x04000C7A RID: 3194
		int bufferSize = 200;

		// Token: 0x04000C7B RID: 3195
		int bufferQueueSize = 30;

		// Token: 0x04000C7C RID: 3196
		int bufferQueueCount;

		// Token: 0x04000C7D RID: 3197
		Queue<IntPtr> bufferReleaseQueue = new Queue<IntPtr>();

		// Token: 0x04000C7E RID: 3198
		Thread bufferMaintainerThread;

		// Token: 0x04000C7F RID: 3199
		int deviceId;

		// Token: 0x04000C80 RID: 3200
		WaveInSafeHandle handle;

		// Token: 0x04000C81 RID: 3201
		NativeMethods.waveInProc callback;
	}
}
