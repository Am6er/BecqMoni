// The offers made to the user around a board: the answer given in advance is re-checked, the window after a board reset only for a running
// recording and a board with a spectrum, the advice of a new document without that window, the Stop question of a joined acquisition,
// the board's spectrum taken at once by Replace (and Replace offered only where that spectrum fits the document).
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

        // checks if the previously given answer is still valid against the current board state; only for a COUNTING board (R3): the window
        // after a board reset offers to take the board's acquisition as it goes on, and for a stopped board those answers would send -sta,
        // which that window does not say - the Start window is shown then, with what each button sends
        internal static bool AnswerStillHolds(AtomSpectraRestStartChoice given, long seenSeconds, long seenCounts, AtomSpectraRestStartQuestion q)
        {
            if (q == null || !q.Counting)
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

            // the program may begin to close while the question is open (logging off, taskkill without /F): its answer is then not acted on (O2)
            return () => !MainForm.ApplicationClosing && AtomSpectraRestUi.AskStopJoined(endpoint) && !MainForm.ApplicationClosing;
        }

        // R1: can the board's spectrum the Start question shows be taken into the document as it is - folded to the profile's channels the
        // way of every snapshot (AtomSpectraTcpAcquisition.Compress), equal to the document's channels, the fold by a whole factor
        internal static bool SnapshotFits(int[] boardBins, int foldChannels, int documentChannels)
        {
            if (boardBins == null || boardBins.Length == 0 || documentChannels <= 0)
            {
                return false;
            }
            int folded = foldChannels > 0 && boardBins.Length > foldChannels ? foldChannels : boardBins.Length;
            return folded == documentChannels && boardBins.Length % folded == 0;
        }

        // R1: "Replace document spectrum" only where the board's spectrum folds into the document's channels (SnapshotFits); otherwise
        // its first snapshot is refused (ERRIncompatibleChannelParameters) and the document could not become what the user confirmed.
        // The policy (AtomSpectraRestPolicy.ReplaceAllowed) does not know the channels, so the question is narrowed here, before the answer
        // given in advance is checked (AnswerGiven) and before the window
        private static Func<AtomSpectraRestStartQuestion, AtomSpectraRestStartChoice> ReplaceOnlyIfFolds(ResultData r, int foldChannels, Func<AtomSpectraRestStartQuestion, AtomSpectraRestStartChoice> ask)
        {
            int documentChannels = r != null && r.EnergySpectrum != null && r.EnergySpectrum.Spectrum != null ? r.EnergySpectrum.Spectrum.Length : 0;
            return q =>
            {
                int[] bins = q != null && q.Board != null ? q.Board.Bins : null;
                if (q != null && q.ReplaceAllowed && !SnapshotFits(bins, foldChannels, documentChannels))
                {
                    q.ReplaceAllowed = false;
                    AtomSpectraTcpLog.Info("Session", AtomSpectraTcpLog.Format("Replace not offered: the board's {0} channels do not fold into the document's {1} (profile {2})",
                        bins != null ? bins.Length : 0, documentChannels, foldChannels));
                }
                return ask(q);
            };
        }

        // R1: "Replace" puts into the document at once what the user confirmed: the board's spectrum of the Start question, folded to the
        // document's channels, with its time and counts - the copy of every snapshot (CopySnapshot). The board's time alone (as before) left
        // the document's own spectrum under it until the first snapshot: a first snapshot refused, a lost link, 401/403/404 or an early Stop
        // kept that mixture (a false count rate and activity), and AutoSave wrote it to the file
        private void TakeBoardSpectrum(ResultData r, IAtomSpectraSession s, int foldChannels)
        {
            if (r == null || s == null || !s.ReplacedDocument)
            {
                return;
            }

            AtomSpectraTcpDataReadyArgs seen = s.ReplacedSnapshot;
            EnergySpectrum spectrum = r.EnergySpectrum;
            AtomSpectraTcpDataReadyArgs e = seen != null ? BuildArgs(seen, foldChannels, restChannels) : null;
            if (e == null || spectrum == null || spectrum.Spectrum == null || e.Histogram.Length != spectrum.Spectrum.Length)
            {
                // not expected: Replace is offered only where the spectrum folds (ReplaceOnlyIfFolds); the document is left as it is
                AtomSpectraTcpLog.Warn("Session", "Replace: the board's spectrum of the question does not fit the document; the document is left until the first snapshot");
                return;
            }

            CopySnapshot(r, spectrum, e, false);   // the board calibration comes with the first snapshot (boardCalibrationPending)
            AtomSpectraTcpLog.Info("Session", AtomSpectraTcpLog.Format("Replace: the document takes the board's spectrum of the question at once ({0} s, {1} counts, {2} channels)",
                e.ElapsedTime, spectrum.ValidPulseCount, e.Histogram.Length));
        }
    }
}
