using System;
using System.CodeDom.Compiler;
using System.Configuration;
using System.Runtime.CompilerServices;

namespace BecquerelMonitor.Properties
{
	// Token: 0x02000134 RID: 308
	[GeneratedCode("Microsoft.VisualStudio.Editors.SettingsDesigner.SettingsSingleFileGenerator", "10.0.0.0")]
	[CompilerGenerated]
	sealed partial class Settings : ApplicationSettingsBase
	{
		// Token: 0x1700043F RID: 1087
		// (get) Token: 0x06000FF5 RID: 4085 RVA: 0x00057B48 File Offset: 0x00055D48
		public static Settings Default
		{
			get
			{
				return Settings.defaultInstance;
			}
		}

		// Token: 0x04000941 RID: 2369
		static Settings defaultInstance = (Settings)SettingsBase.Synchronized(new Settings());
	}
}
