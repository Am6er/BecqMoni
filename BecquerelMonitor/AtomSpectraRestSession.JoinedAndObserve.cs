// Replace of the document spectrum in observe-only mode, the Stop question of a joined acquisition and the status text of a refusal
// of access (401/403) of the REST session.
using System;

namespace BecquerelMonitor
{
    public sealed partial class AtomSpectraRestSession
    {
        // asked at Stop of an acquisition only joined, true = send -sto as well; null (no window, the program closes) = as before.
        public Func<bool> AskStopJoined { get; set; }

        // the status line of a lost link: a board that refuses access (401/403) is not a lost link
        public string LinkLostStatusText()
        {
            AtomSpectraRestAcquisition a = acquisition;
            if (a != null && a.AccessDenied)
            {
                return string.Format(System.Globalization.CultureInfo.InvariantCulture, BecquerelMonitor.Properties.Resources.AtomSpectraRestStatusAccessDenied, a.LastAnsweredStatus);
            }
            return BecquerelMonitor.Properties.Resources.AtomSpectraTcpStatusLinkLost;
        }

        // observe only, the board behind the document: Continue is not possible, Replace joins without sending anything.
        private AtomSpectraRestStartPlan ObservePlan(AtomSpectraRestBoardSample second, int below)
        {
            if (Ask == null || !AtomSpectraRestPolicy.BoardHasData(second))
            {
                return AtomSpectraRestStartPlan.RefuseObserve;
            }

            AtomSpectraRestStartQuestion q = new AtomSpectraRestStartQuestion
            {
                Counting = counting,
                Board = second,
                DocumentSeconds = DocumentSeconds,
                DocumentCounts = DocumentCounts,
                ContinueAllowed = false,
                ReplaceAllowed = AtomSpectraRestPolicy.ReplaceAllowed(second, DocumentSeconds, DocumentCounts, false),
                ChannelBelow = below
            };

            if (!q.ReplaceAllowed)
            {
                return AtomSpectraRestStartPlan.RefuseObserve;
            }

            AtomSpectraRestStartChoice choice = Ask(q);
            AtomSpectraTcpLog.Info("Session", "Start dialog (observe only): " + choice);

            if (choice != AtomSpectraRestStartChoice.Replace)
            {
                return AtomSpectraRestStartPlan.Cancel;
            }

            replaced = true;
            AtomSpectraTcpLog.Info("Session", "Start (observe only): the document's spectrum is replaced by the board's; nothing is sent");
            return AtomSpectraRestStartPlan.Join;
        }

        // Stop of a joined acquisition with a window to ask in; true = the user stops it on the board, Stop goes on to -sto as for its own run.
        private bool StopJoinedOnBoard()
        {
            Func<bool> ask = AskStopJoined;
            if (ask == null || observeOnly)
            {
                return false;
            }

            bool stop = ask();
            AtomSpectraTcpLog.Info("Session", stop ? "Stop of a joined acquisition: the user stops it on the board (-sto)" : "Stop of a joined acquisition: the user lets it keep counting (-sto not sent)");

            if (stop)
            {
                joined = false;
            }

            return stop;
        }
    }
}
