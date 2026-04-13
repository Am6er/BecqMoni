using System;
using System.Runtime.InteropServices;

namespace BecquerelMonitor
{
    internal static class SpecUtilsNative
    {
        private const string DLL = "SpecUtilsNet.dll";

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern IntPtr Open(string path, string file_ext);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern void Close(IntPtr ptr);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern int GetMeasurementsCount(IntPtr ptr);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern int GetChannelCount(IntPtr ptr, int m_num);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern double GetTotalCounts(IntPtr ptr, int m_num);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern IntPtr GetSpectrum(IntPtr ptr, int m_num, out int size);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern double GetLiveTime(IntPtr ptr, int m_num);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern double GetRealTime(IntPtr ptr, int m_num);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern int Test();

        /// <summary>
        /// Source types:
        /// Polynomial = 0,
        /// FullRangeFraction = 1,
        /// LowerChannelEdge = 2,
        /// UnspecifiedUsingDefaultPolynomial = 3,
        /// InvalidEquationType = 4
        /// </summary>
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern int GetEnergyCalType(IntPtr ptr, int m_num);

        /// <summary>
        /// Source types:
        /// IntrinsicActivity = 0,
        /// Calibration = 1,
        /// Background = 2,
        /// Foreground = 3,
        /// Unknown = 4
        /// </summary>
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern int GetSourceType(IntPtr ptr, int m_num);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern long GetStartTime(IntPtr ptr, int m_num);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern IntPtr GetEnergyCalibrationCoefficients(IntPtr ptr, int m_num, out int size);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern IntPtr GetEnergyCalibrationChannelEnergies(IntPtr ptr, int m, out int size);
    }
}