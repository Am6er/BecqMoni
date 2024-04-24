using System;
using System.Linq;
using System.Xml.Serialization;

namespace BecquerelMonitor
{
    public class ConfidenceLevel
    {
        public readonly static string[] levels = new string[] {"90%", "95%", "99%" };
        public readonly static decimal[] z_score = new decimal[] { 1.44m, 1.645m, 2.326m};

        public static string GetLevel(decimal z_score_v)
        {
            for (int i = 0; i < levels.Length; i++)
            {
                if (z_score[i] == z_score_v) return levels[i];
            }
            return "??%";
        }

        public static int GetLevelIndex(decimal z_score_v)
        {
            for (int i = 0; i < levels.Length; i++)
            {
                if (z_score[i] == z_score_v) return i;
            }
            return 0;
        }
    }
}
