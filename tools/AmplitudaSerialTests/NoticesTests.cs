using System;
using System.Collections.Generic;
using System.Globalization;
using BecquerelMonitor;

namespace AmplitudaSerialTests
{
    // A318 (3): the messages of the AmplitudaSerial device to the user. The device controller
    // used to call the modal AppUi.Report right where a message arose - in the pump timer's tick
    // and in the synchronous Stop. Since the tick clears its re-entry flag before reporting (so
    // that Pump() runs on while a box is open), a warning that repeats every cycle opened a box
    // INSIDE the open box, every 5-8 s, all night; and a Stop by preset time waited for its
    // "Stop incomplete" box before the document could be saved.
    //
    // The fake message loop below is what WinForms does: a posted callback runs from the main
    // loop, and a modal box runs a loop of its own that dispatches posted callbacks too (the pump
    // timer keeps ticking in it and raises the warning again).
    public static class NoticesTests
    {
        sealed class MessageLoop
        {
            public readonly Queue<Action> Posted = new Queue<Action>();
            public int Depth;
            public int MaxDepth;
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

        public static void TestOneBoxAtATimeAndNeverInsideTheCaller()
        {
            MessageLoop loop = new MessageLoop();
            AmplitudaSerialNotices notices = null;
            notices = new AmplitudaSerialNotices(loop.Post, delegate (string text)
            {
                loop.Depth++;
                loop.MaxDepth = Math.Max(loop.MaxDepth, loop.Depth);
                loop.Shown.Add(text);
                // While the box is open, the tick fires again and again (a block that stops
                // itself every cycle) and the modal loop dispatches whatever was posted.
                if (loop.Shown.Count < 40)
                {
                    notices.Add("stopped");
                    notices.Add("stopped");
                    notices.Add("ceiling");
                    loop.Run();
                }
                loop.Depth--;
            }, () => false);

            notices.Add("stopped");
            T.Eq(0, loop.Shown.Count, "boxes shown inside the caller (the tick, the Stop)");
            loop.Run();
            T.Eq(1, loop.MaxDepth, "boxes open at once");
            T.Eq(40, loop.Shown.Count, "boxes shown in all");
            T.True(loop.Shown.Count > 1 && loop.Shown[1] == "stopped\r\n\r\nceiling",
                "what came while a box was open is shown once, together: " + (loop.Shown.Count > 1 ? loop.Shown[1] : "(nothing)"));
            T.Eq(0, loop.Posted.Count, "nothing left posted");
        }

        // Once the queue is empty a new message opens a new box again (the gate is not stuck).
        public static void TestGateReopensAfterTheBoxIsClosed()
        {
            MessageLoop loop = new MessageLoop();
            AmplitudaSerialNotices notices = new AmplitudaSerialNotices(loop.Post, delegate (string text)
            {
                loop.Shown.Add(text);
            }, () => false);
            notices.Add("first");
            loop.Run();
            notices.Add("second");
            notices.Add("third");
            loop.Run();
            T.Eq(2, loop.Shown.Count, "boxes");
            T.True(loop.Shown.Count == 2 && loop.Shown[0] == "first" && loop.Shown[1] == "second\r\n\r\nthird",
                "texts: " + string.Join(" | ", loop.Shown.ToArray()));
        }

        // A box that throws must not shut the gate for good: the next message is still shown.
        public static void TestFailedBoxDoesNotJamTheGate()
        {
            MessageLoop loop = new MessageLoop();
            int calls = 0;
            AmplitudaSerialNotices notices = new AmplitudaSerialNotices(loop.Post, delegate (string text)
            {
                calls++;
                if (calls == 1) throw new InvalidOperationException("fake: the box failed");
                loop.Shown.Add(text);
            }, () => false);
            notices.Add("lost");
            loop.Run();
            notices.Add("shown");
            loop.Run();
            T.True(loop.Shown.Count == 1 && loop.Shown[0] == "shown", "after a failed box: " + string.Join(" | ", loop.Shown.ToArray())
                + " (" + calls.ToString(CultureInfo.InvariantCulture) + " calls)");
        }

        // Review of 13.10.2026, item 4: while the application is closing no box opens at all -
        // neither for a message raised during the close (the Stop of every document, its
        // "Stop incomplete") nor for one posted just before the close began.
        public static void TestNothingIsShownWhileTheApplicationCloses()
        {
            MessageLoop loop = new MessageLoop();
            bool closing = false;
            AmplitudaSerialNotices notices = new AmplitudaSerialNotices(loop.Post, delegate (string text)
            {
                loop.Shown.Add(text);
            }, () => closing);
            notices.Add("posted before the close");
            closing = true;
            loop.Run();
            notices.Add("raised during the close");
            loop.Run();
            T.Eq(0, loop.Shown.Count, "boxes shown while closing");
        }
    }
}
