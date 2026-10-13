using System;
using System.Collections.Generic;
using BecquerelMonitor;

namespace AmplitudaSerialTests
{
    public static class ReporterTests
    {
        // A fake message box: records what was shown, counts the nesting depth, and can call
        // Report from inside the show (what a modal loop's nested timer tick does).
        sealed class Rig
        {
            public readonly List<string> Shown = new List<string>();
            public readonly List<Action> Posted = new List<Action>();
            public readonly bool Deferred;
            public int Depth;
            public int MaxDepth;
            public AmplitudaSerialReporter Reporter;
            // Raised from inside the show of the key text.
            public readonly Dictionary<string, string[]> Reentrant = new Dictionary<string, string[]>();

            public Rig(bool deferred)
            {
                Deferred = deferred;
                Reporter = new AmplitudaSerialReporter(Show, Dispatch);
            }

            void Show(string text)
            {
                Depth++;
                if (Depth > MaxDepth) MaxDepth = Depth;
                Shown.Add(text);
                string[] again;
                if (Reentrant.TryGetValue(text, out again))
                {
                    foreach (string next in again) Reporter.Report(next);
                }
                Depth--;
            }

            void Dispatch(Action action)
            {
                if (Deferred) Posted.Add(action);
                else action();
            }

            public void RunPosted()
            {
                while (Posted.Count > 0)
                {
                    Action next = Posted[0];
                    Posted.RemoveAt(0);
                    next();
                }
            }
        }

        public static void TestSingleReportIsShownOnce()
        {
            Rig rig = new Rig(false);
            rig.Reporter.Report("a");
            T.Eq(1, rig.Shown.Count, "shown once");
            T.True(rig.Shown[0] == "a", "text");
        }

        public static void TestRepeatOfTheShowingTextIsDroppedAndNeverNested()
        {
            Rig rig = new Rig(false);
            rig.Reentrant["a"] = new[] { "a", "a" };
            rig.Reporter.Report("a");
            T.Eq(1, rig.Shown.Count, "the repeat was collapsed");
            T.Eq(1, rig.MaxDepth, "show never re-entered");
        }

        public static void TestDifferentTextRaisedDuringTheShowFollowsIt()
        {
            Rig rig = new Rig(false);
            rig.Reentrant["a"] = new[] { "b" };
            rig.Reporter.Report("a");
            T.True(string.Join(",", rig.Shown) == "a,b", "order: " + string.Join(",", rig.Shown));
            T.Eq(1, rig.MaxDepth, "show never re-entered");
        }

        public static void TestPendingRepeatsAreCollapsedAndOrderIsKept()
        {
            Rig rig = new Rig(false);
            rig.Reentrant["a"] = new[] { "b", "c", "b", "a", "c" };
            rig.Reporter.Report("a");
            T.True(string.Join(",", rig.Shown) == "a,b,c", "order: " + string.Join(",", rig.Shown));
            T.Eq(1, rig.MaxDepth, "show never re-entered");
        }

        public static void TestAfterTheDrainARepeatIsShownAgain()
        {
            Rig rig = new Rig(false);
            rig.Reporter.Report("a");
            rig.Reporter.Report("a");
            T.True(string.Join(",", rig.Shown) == "a,a", "one box per cycle: " + string.Join(",", rig.Shown));
            T.Eq(1, rig.MaxDepth, "never nested");
        }

        public static void TestNothingIsShownUntilThePostedActionRuns()
        {
            Rig rig = new Rig(true);
            rig.Reporter.Report("a");
            T.Eq(0, rig.Shown.Count, "nothing shown before the post runs");
            T.Eq(1, rig.Posted.Count, "one action posted");
            rig.Reporter.Report("b");
            rig.Reporter.Report("a");
            rig.Reporter.Report("b");
            T.Eq(1, rig.Posted.Count, "no second post while one is waiting");
            rig.RunPosted();
            T.True(string.Join(",", rig.Shown) == "a,b", "shown in order, repeats collapsed: " + string.Join(",", rig.Shown));
            rig.Reporter.Report("a");
            T.Eq(1, rig.Posted.Count, "a new report after the drain posts again");
            rig.RunPosted();
            T.True(string.Join(",", rig.Shown) == "a,b,a", "shown again: " + string.Join(",", rig.Shown));
            T.Eq(1, rig.MaxDepth, "never nested");
        }
    }
}
