using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;

namespace AmplitudaSerialTests
{
    // Minimal assertion helpers; a failed check is reported and counted, the run continues.
    public static class T
    {
        public static int Failed;
        public static int Passed;

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

        // Real-time wait for something another thread does.
        public static bool Wait(Func<bool> condition, int timeoutMs)
        {
            Stopwatch clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < timeoutMs)
            {
                if (condition()) return true;
                Thread.Sleep(1);
            }
            return condition();
        }
    }

    static class Program
    {
        static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--live") return RunLive(args);

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
        static int RunLive(string[] args)
        {
            Type live = Assembly.GetExecutingAssembly().GetType("AmplitudaSerialTests.LiveSmoke");
            if (live == null) { Console.WriteLine("LiveSmoke is not built yet."); return 2; }
            return (int)live.GetMethod("Run").Invoke(null, new object[] { args });
        }
    }
}
