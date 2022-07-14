/*
 * Copyright � 2005, Mathew Hall
 * All rights reserved.
 *
 * Redistribution and use in source and binary forms, with or without modification, 
 * are permitted provided that the following conditions are met:
 *
 *    - Redistributions of source code must retain the above copyright notice, 
 *      this list of conditions and the following disclaimer.
 * 
 *    - Redistributions in binary form must reproduce the above copyright notice, 
 *      this list of conditions and the following disclaimer in the documentation 
 *      and/or other materials provided with the distribution.
 *
 * THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND 
 * ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED 
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. 
 * IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, 
 * INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT 
 * NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, 
 * OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, 
 * WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) 
 * ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY 
 * OF SUCH DAMAGE.
 */


using System;
using System.Runtime.InteropServices;


namespace XPTable.Win32
{
	/// <summary>
	/// Receives dynamic-link library (DLL)-specific version information. 
	/// It is used with the DllGetVersion function
	/// </summary>
	[StructLayout(LayoutKind.Sequential)]
	internal struct DLLVERSIONINFO
	{
		/// <summary>
		/// Size of the structure, in bytes. This member must be filled 
		/// in before calling the function
		/// </summary>
		public int cbSize;

		/// <summary>
		/// Major version of the DLL. If the DLL's version is 4.0.950, 
		/// this value will be 4
		/// </summary>
		public int dwMajorVersion;

		/// <summary>
		/// Minor version of the DLL. If the DLL's version is 4.0.950, 
		/// this value will be 0
		/// </summary>
		public int dwMinorVersion;

		/// <summary>
		/// Build number of the DLL. If the DLL's version is 4.0.950, 
		/// this value will be 950
		/// </summary>
		public int dwBuildNumber;

		/// <summary>
		/// Identifies the platform for which the DLL was built
		/// </summary>
		public int dwPlatformID;
	}
}
