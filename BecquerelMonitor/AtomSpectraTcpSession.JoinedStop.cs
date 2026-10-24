// The Stop question of a joined acquisition (as in the REST session): the user decides whether the board is stopped too.
using System;

namespace BecquerelMonitor
{
    public sealed partial class AtomSpectraTcpSession
    {
        // asked at Stop of an acquisition only joined, true = send -sto as well; null (no window, the program closes) = as before
        public Func<bool> AskStopJoined { get; set; }

        // true while the Stop question of a joined acquisition is on the screen
        private bool stopQuestionOpen;

        // Stop of a joined acquisition with a window to ask in; true = the user stops it on the board, Stop goes on to -sto as for its own run
        private bool StopJoinedOnBoard()
        {
            Func<bool> ask = AskStopJoined;
            if (ask == null) return false;
            bool stop;
            stopQuestionOpen = true;   // the window is modal but the timers go on: a Stop entered meanwhile must not close the session under it
            try { stop = ask(); }
            finally { stopQuestionOpen = false; }
            AtomSpectraTcpLog.Info("Session", stop ? "Stop of a joined acquisition: the user stops it on the board (-sto)" : "Stop of a joined acquisition: the user lets it keep counting (-sto not sent)");
            if (stop) joinedRunning = false;
            return stop;
        }
    }
}
