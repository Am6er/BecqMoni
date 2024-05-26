using System;
using System.Linq;
using System.Xml.Serialization;

namespace BecquerelMonitor
{
    public class ConfidenceLevel
    {
        public readonly static string[] single_side_levels = new string[] {"84%", "90%", "95%", "99%" };
        public readonly static decimal[] z_score_single_side = new decimal[] {1.0m, 1.282m, 1.645m, 2.326m};

        public static string GetSingleSideLevel(decimal z_score_v)
        {
            for (int i = 0; i < single_side_levels.Length; i++)
            {
                if (z_score_single_side[i] == z_score_v) return single_side_levels[i];
            }
            return "??%";
        }

        public static int GetSingleSideLevelIndex(decimal z_score_v)
        {
            for (int i = 0; i < single_side_levels.Length; i++)
            {
                if (z_score_single_side[i] == z_score_v) return i;
            }
            return 0;
        }
    }
}
