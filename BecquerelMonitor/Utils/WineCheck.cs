using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace BecquerelMonitor.Utils
{
    public static class WineCheck
    {

        public static bool isWine()
        {
            try
            {
                var version = GetWineVersion();
                return true;
            }
            catch
            {

            }

            return false;
        }

        [DllImport("ntdll.dll", EntryPoint = "wine_get_version", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern string GetWineVersion();
    }


}