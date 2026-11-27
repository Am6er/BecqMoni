using System;
using System.Globalization;
using System.Reflection;
using BecquerelMonitor.EfficiencyMaker;

// Run with the corpus AS80_point0.in geometry. A primary that starts outside
// the housing and flies away can only deposit energy after exterior scattering.
internal static class AmbientAirReturnProbe
{
    const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    const int Histories = 40000;

    static int ReturningPhotons(GeometryModel geometry, bool bounded)
    {
        var simulator = new EfficiencySimulator(geometry);
        var type = typeof(EfficiencySimulator);
        if (bounded)
            type.GetField("AmbientAirPaddingCm", Members).SetValue(simulator, 0.0);
        simulator.EnsureBuiltForProbe();
        if (!bounded)
        {
            int externalMedia = 0;
            foreach (string line in simulator.DumpScene().Split('\n'))
            {
                var words = line.Trim().Split(' ');
                if (words[0] == "world") externalMedia++;
                if (words.Length >= 7 && words[0] == "region" && words[1] == "box"
                    && double.Parse(words[3], CultureInfo.InvariantCulture) == 0)
                    throw new InvalidOperationException("A zero-size box was exported");
            }
            if (externalMedia != 1) throw new InvalidOperationException("Missing or duplicate exterior medium");
        }
        simulator.ResetStream(341);
        double centerZ = (double)type.GetField("sphereZ", Members).GetValue(simulator);
        var transport = type.GetMethod("AnalogTransport", Members);
        object[] ray = { 5.0, 0.0, centerZ, 1.0, 0.0, 0.0, 100.0, double.MaxValue, 0.0, false };
        int hits = 0;
        for (int i = 0; i < Histories; i++)
        {
            if ((double)transport.Invoke(simulator, ray) > 0) hits++;
        }
        return hits;
    }

    static int Main(string[] args)
    {
        if (args.Length != 1) throw new ArgumentException("Expected the path to AS80_point0.in");
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var geometry = GeometryModel.Load(args[0]);
        geometry.PointDistance = 100;
        int bounded = ReturningPhotons(geometry, true);
        int atmosphere = ReturningPhotons(geometry, false);
        Console.WriteLine("{0} outward 100 keV photons: bounded={1}, atmosphere={2}", Histories, bounded, atmosphere);
        return bounded == 0 && atmosphere > 0 ? 0 : 1;
    }
}
