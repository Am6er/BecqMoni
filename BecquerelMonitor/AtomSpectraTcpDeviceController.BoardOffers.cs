// The offers made to the user around a board: the answer given in advance is re-checked, the window after a board reset only for a running
// recording and a board with a spectrum, the advice of a new document without that window, the Stop question of a joined acquisition,
// the board time taken at once by Replace.
using BecquerelMonitor.Properties;
using System;
using System.Globalization;

namespace BecquerelMonitor
{
    public partial class AtomSpectraTcpDeviceController
    {
        // the board's time and counts the user saw when the answer was given in advance
        private static long answerSeconds;
        private static long answerCounts;

        // checks if the previously given answer is still valid against the current board state
        internal static bool AnswerStillHolds(AtomSpectraRestStartChoice given, long seenSeconds, long seenCounts, AtomSpectraRestStartQuestion q)
        {
            if (q == null)
            {
                return false;
            }

            if (given == AtomSpectraRestStartChoice.Continue)
            {
                return q.ContinueAllowed;
            }

            if (given == AtomSpectraRestStartChoice.Replace)
            {
                return q.ReplaceAllowed
                    && q.Board != null
                    && q.Board.HasData
                    && q.Board.Time >= seenSeconds
                    && q.Board.Total >= seenCounts;
            }

            return false;
        }

        // determines if the reset window should be offered based on recording state and board data
        internal static bool OfferAfterReset(bool recording, long boardSeconds, long boardCounts)
        {
            return recording && (boardSeconds > 0 || boardCounts > 0);
        }

        // appends the new document advice to the provided text
        internal static string ResetAdvice(string text)
        {
            return (text ?? "") + " " + Resources.MSGAtomSpectraRestBoardResetNewDocument;
        }

        // the document whose Stop was asked (the user's, the timer's or the preset's) and has not been started again; UI thread
        private ResultData stopAsked;

        // test seam: the "recording" the offer is made with
        internal static Action<bool> RecordingSeen = null;   // set by the test stand through reflection

        // read at the moment the offer is shown, not when it was posted: a Stop begun meanwhile (it sets Recording only in its finally)
        // means the user stopped it first, whatever Recording said at the post
        internal bool RecordingAtShow(ResultData data, bool recordingAtPost)
        {
            return recordingAtPost && !ReferenceEquals(stopAsked, data);
        }

        // the offer after a board reset, held while a wait window is open; the recording is judged when it is shown
        internal void ScheduleBoardOffer(string text, string logText, ResultData data, long boardSeconds, long boardCounts, bool recordingAtPost)
        {
            AtomSpectraRestUi.AfterWait(() => OfferBoardAcquisition(text, logText, data, boardSeconds, boardCounts, RecordingAtShow(data, recordingAtPost)));
        }

        // can a window be raised for the Stop question (a seam of the test stand: the headless stand has no windows)
        internal static Func<bool> WindowsForStopQuestion = () => AppUi.HasWindows;

        // creates a delegate to ask about stopping a joined acquisition if conditions are met
        private static Func<bool> StopJoinedQuestion(IAtomSpectraSession s)
        {
            if (!WindowsForStopQuestion() || s == null)
            {
                return null;
            }

            string endpoint;
            AtomSpectraRestSession restSession = s as AtomSpectraRestSession;
            if (restSession != null)
            {
                endpoint = restSession.Endpoint;
            }
            else
            {
                endpoint = AtomSpectraTcpSession.HostKey(s.Host, s.Port);
            }

            return () => !MainForm.ApplicationClosing && AtomSpectraRestUi.AskStopJoined(endpoint);
        }

        // applies the board's elapsed time to the result data for a replaced document
        private static void TakeBoardTime(ResultData r, IAtomSpectraSession s)
        {
            if (r == null || s == null || !s.ReplacedDocument)
            {
                return;
            }

            TimeSpan t = TimeSpan.FromSeconds(s.ElapsedAtBeginSeconds);
            r.ResultDataStatus.ElapsedTime = t;
            r.ResultDataStatus.TotalTime = t;

            AtomSpectraTcpLog.Info("Session", "Replace: the document takes the board's time " + s.ElapsedAtBeginSeconds.ToString("0", CultureInfo.InvariantCulture) + " s at once (its own time must not meet the preset before the first snapshot)");
        }
    }
}
