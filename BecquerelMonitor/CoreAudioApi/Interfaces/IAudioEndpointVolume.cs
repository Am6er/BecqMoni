using System;
using System.Runtime.InteropServices;

namespace CoreAudioApi.Interfaces
{
	// Token: 0x020001BF RID: 447
	[Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface IAudioEndpointVolume
	{
		// Token: 0x060015D6 RID: 5590
		[PreserveSig]
		int RegisterControlChangeNotify(IAudioEndpointVolumeCallback pNotify);

		// Token: 0x060015D7 RID: 5591
		[PreserveSig]
		int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback pNotify);

		// Token: 0x060015D8 RID: 5592
		[PreserveSig]
		int GetChannelCount(out int pnChannelCount);

		// Token: 0x060015D9 RID: 5593
		[PreserveSig]
		int SetMasterVolumeLevel(float fLevelDB, Guid pguidEventContext);

		// Token: 0x060015DA RID: 5594
		[PreserveSig]
		int SetMasterVolumeLevelScalar(float fLevel, Guid pguidEventContext);

		// Token: 0x060015DB RID: 5595
		[PreserveSig]
		int GetMasterVolumeLevel(out float pfLevelDB);

		// Token: 0x060015DC RID: 5596
		[PreserveSig]
		int GetMasterVolumeLevelScalar(out float pfLevel);

		// Token: 0x060015DD RID: 5597
		[PreserveSig]
		int SetChannelVolumeLevel(uint nChannel, float fLevelDB, Guid pguidEventContext);

		// Token: 0x060015DE RID: 5598
		[PreserveSig]
		int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, Guid pguidEventContext);

		// Token: 0x060015DF RID: 5599
		[PreserveSig]
		int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);

		// Token: 0x060015E0 RID: 5600
		[PreserveSig]
		int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);

		// Token: 0x060015E1 RID: 5601
		[PreserveSig]
		int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, Guid pguidEventContext);

		// Token: 0x060015E2 RID: 5602
		[PreserveSig]
		int GetMute(out bool pbMute);

		// Token: 0x060015E3 RID: 5603
		[PreserveSig]
		int GetVolumeStepInfo(out uint pnStep, out uint pnStepCount);

		// Token: 0x060015E4 RID: 5604
		[PreserveSig]
		int VolumeStepUp(Guid pguidEventContext);

		// Token: 0x060015E5 RID: 5605
		[PreserveSig]
		int VolumeStepDown(Guid pguidEventContext);

		// Token: 0x060015E6 RID: 5606
		[PreserveSig]
		int QueryHardwareSupport(out uint pdwHardwareSupportMask);

		// Token: 0x060015E7 RID: 5607
		[PreserveSig]
		int GetVolumeRange(out float pflVolumeMindB, out float pflVolumeMaxdB, out float pflVolumeIncrementdB);
	}
}
