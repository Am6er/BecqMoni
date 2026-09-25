using System;
using System.Reflection;

namespace AmplitudaUsbTests
{
    // Minimal assertion helpers; a failed check is reported and counted, the run continues.
    public static class T
    {
        public static int Failed;
        public static int Passed;
        public static string DumpPath = "";

        public static void True(bool condition, string what)
        {
            if (condition) { Passed++; return; }
            Failed++;
            Console.WriteLine("  FAIL: " + what);
        }

        public static void Eq(long expected, long actual, string what)
        {
            True(expected == actual, what + " (expected " + expected + ", got " + actual + ")");
        }

        public static void Near(double expected, double actual, double tolerance, string what)
        {
            True(Math.Abs(expected - actual) <= tolerance, what + " (expected " + expected + " +/- " + tolerance + ", got " + actual + ")");
        }
    }

    static class Program
    {
        static int Main(string[] args)
        {
            int liveSeconds = 0;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--dump" && i + 1 < args.Length) T.DumpPath = args[++i];
                else if (args[i] == "--live" && i + 1 < args.Length) liveSeconds = int.Parse(args[++i]);
            }
            if (liveSeconds > 0) return RunLive(liveSeconds);

            // Every public static parameterless Test* method of every *Tests class is a test.
            foreach (Type type in Assembly.GetExecutingAssembly().GetTypes())
            {
                if (!type.Name.EndsWith("Tests")) continue;
                foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (!method.Name.StartsWith("Test") || method.GetParameters().Length != 0) continue;
                    Console.WriteLine(type.Name + "." + method.Name);
                    try { method.Invoke(null, null); }
                    catch (TargetInvocationException ex)
                    {
                        T.Failed++;
                        Console.WriteLine("  EXCEPTION: " + ex.InnerException);
                    }
                }
            }
            Console.WriteLine();
            Console.WriteLine("passed " + T.Passed + ", failed " + T.Failed);
            return T.Failed == 0 ? 0 : 1;
        }

        // Looked up by reflection so that this file compiles before LiveSmoke.cs exists.
        static int RunLive(int seconds)
        {
            Type live = Assembly.GetExecutingAssembly().GetType("AmplitudaUsbTests.LiveSmoke");
            if (live == null) { Console.WriteLine("LiveSmoke is not built yet."); return 2; }
            return (int)live.GetMethod("Run").Invoke(null, new object[] { seconds });
        }
    }
}
