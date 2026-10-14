using System;
using System.Collections.Generic;

namespace BecquerelMonitor
{
    // One message box at a time, never nested. A warning that repeats every poll cycle (a block
    // that stops itself each cycle, the 65535 ceiling on an overloaded line) must not stack a
    // new modal loop on top of the previous one on the UI thread's stack, and a report made
    // from inside a Stop must not block that Stop until the user closes the box.
    //
    // `show` displays one message (AppUi.Report: modal when a window exists). `dispatch` runs
    // an action later on the UI thread (a Post to the message queue; headless: run at once).
    //
    // Every call happens on the UI thread (timer ticks, StopMeasurement, Fail), so there is no
    // locking. A `show` that re-enters Report (a nested timer tick inside the modal loop) can
    // only append to the pending list: the running Drain picks it up after the box is closed.
    //
    // `closing` (P288, kept from A318 when merging PR #36): while it says true nothing is shown
    // at all - checked when a message arrives and again right before its box would open, so a
    // show posted just before the close began does not open in its middle either. The device
    // controller passes MainForm.ApplicationClosing.
    public sealed class AmplitudaSerialReporter
    {
        readonly Action<string> show;
        readonly Action<Action> dispatch;
        readonly Func<bool> closing;
        readonly List<string> pending = new List<string>();
        string current;             // the text being shown right now
        bool showing;               // a Drain is posted or running

        public AmplitudaSerialReporter(Action<string> show, Action<Action> dispatch)
            : this(show, dispatch, () => false)
        {
        }

        public AmplitudaSerialReporter(Action<string> show, Action<Action> dispatch, Func<bool> closing)
        {
            this.show = show;
            this.dispatch = dispatch;
            this.closing = closing;
        }

        public void Report(string text)
        {
            if (text == null || closing())
            {
                return;
            }
            if (text == current || pending.Contains(text))
            {
                return;                 // a repeat of what the user is about to see or is looking at
            }
            pending.Add(text);
            if (showing)
            {
                return;                 // the running (or posted) Drain will get to it
            }
            showing = true;
            try
            {
                dispatch(Drain);
            }
            catch (Exception)
            {
                // The message queue is gone (the program is closing): forget the batch instead of
                // staying "showing" for good, which would silently drop every later report.
                current = null;
                pending.Clear();
                showing = false;
            }
        }

        void Drain()
        {
            try
            {
                while (pending.Count > 0)
                {
                    current = pending[0];
                    pending.RemoveAt(0);
                    if (closing())
                    {
                        continue;
                    }
                    try
                    {
                        show(current);
                    }
                    catch (Exception)
                    {
                        // A box that failed (P288, kept from A318) must not take the rest of the
                        // queue with it, nor escape from a posted callback into the message loop -
                        // in BecqMoni that is Application.ThreadException and its crash dialog.
                    }
                }
            }
            finally
            {
                current = null;
                pending.Clear();
                showing = false;
            }
        }
    }
}
