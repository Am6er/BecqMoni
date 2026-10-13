using System;
using System.Globalization;
using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;
// П191: почему пара линий нуклида изотропна — ветви, сопоставленные переходы, схема g4.
public static class AngDebugP191
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        GlobalConfigManager.GetInstance();
        foreach (string key in args)
        {
            CascadeAtomicData atomic = CascadeAtomicData.Of(key);
            Console.WriteLine("== {0}: {1}", key, atomic == null ? "atomic = null" : "ветвей " + atomic.Branches.Count + "; " + atomic.Note);
            if (atomic == null) continue;
            for (int b = 0; b < atomic.Branches.Count; b++)
            {
                CascadeAtomicData.Branch br = atomic.Branches[b];
                Console.WriteLine("   ветвь {0}: {1} Z={2} A={3} perc={4} dec={5}", b, br.Nucid, br.Z, br.A, br.Perc, br.DecType);
                AngularCorrelation.Scheme scheme = AngularCorrelation.SchemeOf(br.Z, br.A);
                Console.WriteLine("      схема g4: {0}", scheme == null ? "НЕТ" : "переходов " + scheme.Transitions.Count + ", спинов " + scheme.Jpi.Count);
            }

            foreach (CascadeAtomicData.GammaLine row in atomic.GammaIntensity)
            {
                Console.WriteLine("   γ {0,9:F3} кэВ I {1,8:F3} % канал {2}  переход {3}", row.EnergyKev, row.IntensityPct, row.Channel,
                                  row.Transition == null ? "НЕТ" : string.Format("{0}→{1} ({2:F3} кэВ) ветвь {3} α_K {4:F4}", row.Transition.FromSeq, row.Transition.ToSeq, row.Transition.EnergyKev, row.Transition.BranchIndex, row.Transition.AlphaK));
            }
        }

        return 0;
    }
}
