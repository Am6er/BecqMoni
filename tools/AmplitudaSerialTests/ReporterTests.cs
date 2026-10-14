using System;
using System.Collections.Generic;
using System.Globalization;
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

        // ---- Ported from A318 (NoticesTests) when merging PR #36 (P288, 14.10.2026) ----
        // The two cases the tests above do not reach: a box that throws, and the application
        // closing. The loop is the real one in miniature: a posted action runs from the main loop.

        sealed class PostedLoop
        {
            public readonly Queue<Action> Posted = new Queue<Action>();
            public readonly List<string> Shown = new List<string>();

            public void Post(Action action)
            {
                Posted.Enqueue(action);
            }

            public void Run()
            {
                while (Posted.Count > 0) Posted.Dequeue()();
            }
        }

        // A box that throws must neither escape from the posted callback into the message loop -
        // in BecqMoni that is Application.ThreadException and its crash dialog - nor take the
        // messages queued behind it with it, nor shut the reporter for good.
        public static void TestFailedBoxIsContained()
        {
            PostedLoop loop = new PostedLoop();
            int calls = 0;
            AmplitudaSerialReporter reporter = new AmplitudaSerialReporter(delegate (string text)
            {
                calls++;
                if (calls == 1) throw new InvalidOperationException("fake: the box failed");
                loop.Shown.Add(text);
            }, loop.Post);
            reporter.Report("lost");
            reporter.Report("queued behind it");
            Exception escaped = null;
            try { loop.Run(); } catch (Exception ex) { escaped = ex; }
            T.True(escaped == null, "a failed box escaped into the message loop: " + (escaped == null ? "" : escaped.Message));
            reporter.Report("after");
            try { loop.Run(); } catch (Exception ex) { escaped = ex; }
            T.True(string.Join(",", loop.Shown) == "queued behind it,after",
                "shown after a failed box: " + string.Join(",", loop.Shown) + " (" + calls.ToString(CultureInfo.InvariantCulture) + " calls)");
        }

        // Review of 13.10.2026, item 4: while the application is closing no box opens at all -
        // neither for a message raised during the close (the Stop of every document, its
        // "Stop incomplete") nor for one posted just before the close began.
        public static void TestNothingIsShownWhileTheApplicationCloses()
        {
            PostedLoop loop = new PostedLoop();
            bool closing = false;
            AmplitudaSerialReporter reporter = new AmplitudaSerialReporter(text => loop.Shown.Add(text), loop.Post, () => closing);
            reporter.Report("posted before the close");
            closing = true;
            loop.Run();
            reporter.Report("raised during the close");
            loop.Run();
            T.Eq(0, loop.Shown.Count, "boxes shown while closing");
            T.Eq(0, loop.Posted.Count, "nothing left posted");
            // Not jammed by what it dropped (a test of the gate only - an application does not reopen).
            closing = false;
            reporter.Report("after");
            loop.Run();
            T.True(string.Join(",", loop.Shown) == "after", "the reporter still works: " + string.Join(",", loop.Shown));
        }
    }
}
