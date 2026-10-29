// Measurement life cycle of an Atom Spectra analyzer reached over TCP (Wi-Fi bridge board,
// port 8234). Same commands and the same histogram path as AtomSpectraDeviceController
// (COM port); the transport and the protocol loop live in AtomSpectraTcpSession.
//
// Differences from the COM type, on purpose:
// - the connection is opened by Start and closed by Stop/Detach/Dispose: the board
//   serves one client, so an idle BecqMoni must not hold its slot;
// - a dropped link does not stop the measurement: the session reconnects and re-sends -sta
//   (never -rst);
// - Stop with the link down (or with no answer to -sto) tries one immediate reconnect to send -sto and otherwise
//   leaves a deferred -sto and tells the user;
// - the loss of the link is announced once per measurement; no window is raised while the program closes;
// - counters of the board that went down without our -rst (a reset from outside) are never written over the
//   document: the recording stops and the user is told (AtomSpectraTcpSession.BoardReset).
// - IDeviceLiveTimeSource: live time is written here, from the same packet as the counts
//   (SetSpectrumTimes with the configured dead time - the formula of the COM type), so
//   MeasurementController.OnTimer leaves it alone.
// - Transport REST (AtomSpectraRestSession): the board's web API instead of the TCP bridge; the board
//   counts by itself, a Start on a counting board joins it, live time comes from the board.
//   Everything is written to AtomSpectraTcpLog (log window AtomSpectraTcpLogForm).

using BecquerelMonitor.Properties;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    public partial class AtomSpectraTcpDeviceController : DeviceController, IDeviceLiveTimeSource, IDisposable
    {
        const int CommandTimeoutMs = 1000;
        const int InfoTimeoutMs = 2000;
        const int KeepAliveMs = 1000;

        private IAtomSpectraSession session;
        // A reset (user's Clear) that the last closed TCP session had not sent yet: handed to the next TCP session
        // and the host:port it was owed for (null: none); a session of another board does not get it.
        // With it, what the board held at that Clear: the next Start resets only a board that still holds it (O3)
        private string carriedResetKey;
        private long carriedResetSeconds, carriedResetCounts;
        private bool linkLossReported;
        private ResultData resultData;
        private string deviceGuid;
        private bool newDocumentCreated = true;
        private bool disposed;
        // The document's preset before a Start raised it by the board's time (Join / Resume); -1: not raised.
        // Clear resets the board, the run starts from zero again, so the preset goes back to this value.
        private int presetBeforeBoardTime = -1;
        // The answer given in advance to the Start question of one spectrum: the window after a board reset has asked already
        private static ResultData answerFor;
        private static AtomSpectraRestStartChoice answer;

        // The Ask of a Start that was answered in advance (taken once): the answer while the board is as the user saw it, else `ask`
        private static Func<AtomSpectraRestStartQuestion, AtomSpectraRestStartChoice> AnswerGiven(ResultData r, Func<AtomSpectraRestStartQuestion, AtomSpectraRestStartChoice> ask)
        {
            if (answerFor == null || !ReferenceEquals(answerFor, r)) return ask;
            AtomSpectraRestStartChoice given = answer;
            long seenSeconds = answerSeconds, seenCounts = answerCounts;
            answerFor = null;
            return q => { bool holds = AnswerStillHolds(given, seenSeconds, seenCounts, q); AtomSpectraTcpLog.Info("Session", "Start question answered in advance: " + given + (holds ? "" : " - the board changed since, asked again")); return holds ? given : ask(q); };
        }

        public bool IsLinkLost
        {
            get { return session != null && session.IsLinkLost; }
        }

        public static bool IsHostInUse(string host, int port)
        {
            return AtomSpectraTcpSession.IsHostInUse(host, port);
        }

        public override bool StartMeasurement(ResultData r)
        {
            return BeginSession(r, true);
        }

        public override void StopMeasurement(ResultData r)
        {
            StopMeasurement(r, false);
        }

        // manual (the user's "Stop") may ask whether a joined acquisition is to be stopped on the board too; the preset, the timer
        // and any other Stop ask nothing: the joined acquisition is detached without -sto, as before (no window, timerBusy not held)
        public override void StopMeasurement(ResultData r, bool manual)
        {
            stopAsked = r;   // a board reset met during this Stop is not offered a replacement (RecordingAtShow)
            if (MainForm.ApplicationClosing)
            {
                // the program closes: detach first, the board keeps counting (no -sto). This is intended for the REST/TCP board;
                // the USB AtomSpectra Pro does otherwise (its StopMeasurement sends -sto)
                AtomSpectraTcpLog.Info("Session", "the program closes: detached, -sto not sent, the board keeps counting");
                DetachFromDevice(r);
                return;
            }

            if (StopInProgress)
            {
                // T4/O1: a Stop entered while the first one still runs (the preset or the timer while its Stop question is open, or while a
                // REST Stop waits behind its window): the first Stop decides and ends the run. This one leaves AskStopJoined as the first
                // set it, sends nothing and starts no second Stop of the session (REST ran one in parallel: "the board counts on", then
                // -sto); the document is frozen since the first Stop (UpdateHistogram)
                AtomSpectraTcpLog.Info("Session", "a second Stop while the first one still runs (its question is open): the first Stop ends the run");
                return;
            }

            DateTime stoppedAt = DateTime.Now;   // O1: the run ends when Stop is pressed (the first Stop), not when its question is answered
            stopping = true;
            try
            {
                IAtomSpectraSession s0 = session;
                if (s0 != null) s0.AskStopJoined = manual ? StopJoinedQuestion(s0) : null;   // a joined acquisition - stop it on the board too?
                // REST Stop behind the wait window, the UI thread does not block on the network
                AtomSpectraTcpStopOutcome outcome = s0 == null ? AtomSpectraTcpStopOutcome.NotRunning
                    : s0 is AtomSpectraRestSession ? AtomSpectraRestUi.RunWithWait(delegate { return s0.Stop(CommandTimeoutMs); })
                    : s0.Stop(CommandTimeoutMs);

                if (deviceGuid != null)
                {
                    r.EndTime = stoppedAt;
                }

                IAtomSpectraSession s = session;
                if (s != null)
                {
                    AtomSpectraTcpLog.Info("Session", "Stop: " + outcome);
                    SetStatus(s, StopStatusText(outcome));
                }
                if (outcome == AtomSpectraTcpStopOutcome.Deferred && s != null)
                {
                    ReportLater(string.Format(CultureInfo.InvariantCulture, StopDeferredText(s), s.Host, s.Port), "Stop: -sto deferred");
                }
                else if (outcome == AtomSpectraTcpStopOutcome.Unconfirmed && s != null)
                {
                    ReportLater(string.Format(CultureInfo.InvariantCulture, Resources.ERRAtomSpectraRestStopUnconfirmed, s.Host, s.Port), "Stop: unconfirmed");
                }
            }
            finally
            {
                stopping = false;
                r.ResultDataStatus.Recording = false;
            }
        }

        // true while StopMeasurement runs (its Stop question may be open, a REST Stop waits behind its window); UI thread
        private bool stopping;

        public override bool StopInProgress
        {
            get
            {
                AtomSpectraTcpSession tcp = session as AtomSpectraTcpSession;
                return stopping || (tcp != null && tcp.StopQuestionOpen);
            }
        }

        // The text of a deferred stop: REST has its own (it retries for 30 min), TCP says whether the link was down or did not answer.
        private static string StopDeferredText(IAtomSpectraSession s)
        {
            if (s is AtomSpectraRestSession) return Resources.ERRAtomSpectraRestStopDeferred;
            AtomSpectraTcpSession tcp = s as AtomSpectraTcpSession;
            return tcp != null && tcp.StopUnanswered ? Resources.ERRAtomSpectraTcpStopUnanswered : Resources.ERRAtomSpectraTcpStopDeferred;
        }

        private static string StopStatusText(AtomSpectraTcpStopOutcome outcome)
        {
            switch (outcome)
            {
                case AtomSpectraTcpStopOutcome.Deferred:
                    return Resources.AtomSpectraTcpStatusStopDeferred;
                case AtomSpectraTcpStopOutcome.Detached:
                    return Resources.AtomSpectraTcpStatusDetached;
                case AtomSpectraTcpStopOutcome.Unconfirmed:
                    return Resources.AtomSpectraTcpStatusStopUnconfirmed;
                default:
                    return Resources.AtomSpectraTcpStatusStopped;
            }
        }

        public override void ClearMeasurementResult(ResultData r)
        {
            if (deviceGuid == null)
            {
                return;
            }

            if (MeasurementController.IsDeviceLeasedByOther(deviceGuid, r.MeasurementController))
            {
                Trace.WriteLine("AtomSpectraTcp: device leased by other, skipping clear");
                r.StartTime = DateTime.Now;
                return;
            }

            AtomSpectraTcpDeviceConfig restConfig = ConfigOf(r);
            if (restConfig != null && restConfig.Transport == AtomSpectraTransport.Rest)
            {
                ClearRest(r, restConfig);   // only now, after a confirmation with the board's numbers; never deferred
                return;
            }

            if (session != null)
            {
                // A joined acquisition is not ours: its reset is asked first, with the board's numbers (as REST ConfirmClear).
                // Without windows AskYesNo throws (S100) and nothing goes to the board.
                bool askedAlready = ReferenceEquals(tcpClearFor, r);   // ConfirmClear has already asked the one question of this Clear
                tcpClearFor = null;
                if (session.JoinedRunning && !askedAlready && !ConfirmJoinedClear(session))
                {
                    AtomSpectraTcpLog.Info("Session", "Clear of a joined acquisition: declined, the board is not reset");
                    AppUi.Report(Resources.MSGAtomSpectraTcpNotReset, "", MessageBoxIcon.Information);
                    return;
                }

                // The explicit Clear is the only user action that resets the analyzer's spectrum;
                // with the link down it is kept and executed later, see AtomSpectraTcpSession.Clear.
                session.Clear(CommandTimeoutMs);
            }
            else
            {
                // Clear before any Start: create the session if possible, then clear.
                AtomSpectraTcpDeviceConfig config = ConfigOf(r);
                if (config != null)
                {
                    OpenSession(config);
                    if (session != null)
                    {
                        session.Clear(CommandTimeoutMs);
                    }
                }
            }

            RestorePreset(r);
            r.StartTime = DateTime.Now;
        }

        // with no open acquisition (recording stopped) Clear sends nothing now, the board is reset at the next Start: the text says so
        // and carries no board numbers (the last known ones are stale after a Stop). With a live session -rst goes at once, the board's numbers are shown.
        internal static string JoinedClearText(IAtomSpectraSession s)
        {
            AtomSpectraTcpSession tcp = s as AtomSpectraTcpSession;
            if (tcp != null && tcp.ClearDeferred)
            {
                return Resources.MSGAtomSpectraTcpClearJoinedDeferredConfirm;
            }
            long t = tcp != null ? tcp.BoardSeconds : 0;
            long n = tcp != null ? tcp.BoardCounts : 0;
            return string.Format(CultureInfo.InvariantCulture, Resources.MSGAtomSpectraTcpClearJoinedConfirm,
                t / 3600, (t % 3600) / 60, n.ToString(CultureInfo.InvariantCulture));
        }

        // The text of the ONE question of "Clear" of a joined TCP acquisition: speaks of the document and the analyzer together; the deferred text (recording stopped) already does
        internal static string JoinedClearBothText(IAtomSpectraSession s)
        {
            AtomSpectraTcpSession tcp = s as AtomSpectraTcpSession;
            if (tcp != null && tcp.ClearDeferred)
                return JoinedClearText(s);
            long t = tcp != null ? tcp.BoardSeconds : 0;
            long n = tcp != null ? tcp.BoardCounts : 0;
            return string.Format(CultureInfo.InvariantCulture, Resources.MSGAtomSpectraTcpClearJoinedBothConfirm, t / 3600, (t % 3600) / 60, n.ToString(CultureInfo.InvariantCulture));
        }

        // The question before "Clear" of a TCP document: a joined acquisition is asked ONCE, with the analyzer's numbers; "No" clears nothing (document and board)
        private bool ConfirmClearTcp(ResultData r)
        {
            IAtomSpectraSession s = session;
            if (s == null || !s.JoinedRunning)
                return DeviceController.AskClearDocument();
            if (!AppUi.AskYesNo(JoinedClearBothText(s), Resources.ConfirmationDialogTitle, MessageBoxIcon.Warning, true))
            {
                AtomSpectraTcpLog.Info("Session", "Clear of a joined acquisition: declined, nothing is cleared (document and board)");
                return false;
            }
            tcpClearFor = r;
            return true;
        }

        private static bool ConfirmJoinedClear(IAtomSpectraSession s)
        {
            return AppUi.AskYesNo(JoinedClearText(s), Resources.ConfirmationDialogTitle, MessageBoxIcon.Warning, true);
        }

        // The board starts from zero after Clear: a preset raised at Start by the board's old time goes back to the document's own.
        private void RestorePreset(ResultData r)
        {
            if (presetBeforeBoardTime < 0)
            {
                return;
            }
            AtomSpectraTcpLog.Info("Session", "preset " + r.ResultDataStatus.PresetTime.ToString(CultureInfo.InvariantCulture) + " s -> "
                + presetBeforeBoardTime.ToString(CultureInfo.InvariantCulture) + " s (Clear: the board starts from zero)");
            r.ResultDataStatus.PresetTime = presetBeforeBoardTime;
            presetBeforeBoardTime = -1;
        }

        public override bool AttachToDevice(ResultData r)
        {
            return BeginSession(r, false);
        }

        public override void DetachFromDevice(ResultData r)
        {
            if (deviceGuid != null)
            {
                r.EndTime = DateTime.Now;
            }
            r.ResultDataStatus.Recording = false;
            if (session != null)
            {
                session.Detach();
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            CloseSession();
        }

        private static AtomSpectraTcpDeviceConfig ConfigOf(ResultData resultData)
        {
            if (resultData == null || resultData.DeviceConfig == null)
            {
                return null;
            }

            string guid = resultData.DeviceConfig.Guid ?? "";
            DeviceConfigInfo configInfo;
            if (DeviceConfigManager.GetInstance().DeviceConfigMap.TryGetValue(guid, out configInfo))
            {
                AtomSpectraTcpDeviceConfig tcpConfig = configInfo.InputDeviceConfig as AtomSpectraTcpDeviceConfig;
                if (tcpConfig != null)
                {
                    return tcpConfig;
                }
            }

            return resultData.DeviceConfig.InputDeviceConfig as AtomSpectraTcpDeviceConfig;
        }

        private void OpenSession(AtomSpectraTcpDeviceConfig config)
        {
            if (config.Transport == AtomSpectraTransport.Rest)
            {
                OpenRestSession(config);
                return;
            }
            int port = config.TcpPort;
            // the same board by the rule of HostKey (spaces trimmed, the case of the name not counted): a session made again at every
            // Start for " Board.local" lost what it knew (a joined acquisition stayed joined only inside one session) - T5
            if (session != null && (session.TransportName != "TCP" || !string.Equals(AtomSpectraTcpSession.HostKey(session.Host, session.Port),
                AtomSpectraTcpSession.HostKey(config.Host, port), StringComparison.OrdinalIgnoreCase)))
            {
                CloseSession();
            }

            if (session == null)
            {
                AtomSpectraTcpSession tcp = new AtomSpectraTcpSession(config.Host, port, AtomSpectraTcpSession.DefaultReconnectDelayMs, KeepAliveMs, null);
                session = tcp;
                if (carriedResetKey != null)
                {
                    // the Clear text promises "the board is reset at the next Start": a closed session must not lose it, but only the same board gets it
                    string newKey = AtomSpectraTcpSession.HostKey(config.Host, port);
                    if (string.Equals(carriedResetKey, newKey, StringComparison.OrdinalIgnoreCase))
                    {
                        tcp.RequestResetOnNextStart(carriedResetSeconds, carriedResetCounts);
                        AtomSpectraTcpLog.Info("Session", "a reset left by the closed session waits for the next Start of " + newKey);
                    }
                    else
                    {
                        AtomSpectraTcpLog.Info("Session", "deferred reset dropped: board changed (" + carriedResetKey + " -> " + newKey + ")");
                    }
                    carriedResetKey = null;
                }
                SetStatus(tcp, Resources.AtomSpectraTcpStatusIdle);
                tcp.DataReady += Acquisition_DataReady;
                tcp.LinkLost += Session_LinkLost;
                tcp.BoardReset += Session_BoardReset;
            }
        }

        private void CloseSession()
        {
            if (session == null)
            {
                return;
            }

            session.DataReady -= Acquisition_DataReady;
            session.LinkLost -= Session_LinkLost;
            AtomSpectraTcpSession tcp = session as AtomSpectraTcpSession;
            if (tcp != null)
            {
                tcp.BoardReset -= Session_BoardReset;
            }
            UnhookRest();
            try
            {
                session.Dispose();
            }
            catch (Exception ex)
            {
                Trace.WriteLine("AtomSpectraTcp: error disposing session: " + ex.Message);
            }
            if (tcp != null && tcp.ResetOnNextStart) carriedResetKey = AtomSpectraTcpSession.HostKey(tcp.Host, tcp.Port);   // read after Dispose: Close may have put a lost -rst back
            if (tcp != null && tcp.ResetOnNextStart)
            {
                carriedResetSeconds = tcp.ClearSeenSeconds;   // what the board held at that Clear goes with the reset (O3)
                carriedResetCounts = tcp.ClearSeenCounts;
            }
            session = null;
        }

        private bool BeginSession(ResultData r, bool start)
        {
            resultData = r;
            deviceGuid = r.DeviceConfig != null ? r.DeviceConfig.Guid : null;

            AtomSpectraTcpDeviceConfig config = ConfigOf(r);
            if (config == null)
            {
                return Refuse(Resources.ERRInvalidDeviceType);
            }

            if (config.Transport == AtomSpectraTransport.Rest)
            {
                return BeginRest(r, config);
            }

            if (string.IsNullOrEmpty(config.Host))
            {
                return Refuse(Resources.ERRAtomSpectraTcpNoHost);
            }

            bool started;
            string refusal;
            try
            {
                started = BeginTcp(r, config, start, out refusal);
            }
            catch (Exception ex)
            {
                // our own text, not the generic "Bluetooth not supported" of MeasurementController.StartRecording
                AtomSpectraTcpLog.Error("Session", "TCP Start failed: " + ex.GetType().Name + ": " + ex.Message);
                started = false;
                refusal = string.Format(CultureInfo.InvariantCulture, Resources.ERRAtomSpectraTcpStart, ex.Message);
            }
            if (started)
            {
                return true;
            }
            r.ResultDataStatus.Recording = false;
            return refusal == null ? false : Refuse(refusal);   // null: "Cancel" in the Start dialog, nothing to say
        }

        // true: started. false: refusal is the text for the user, or null when the user cancelled.
        private bool BeginTcp(ResultData r, AtomSpectraTcpDeviceConfig config, bool start, out string refusal)
        {
            refusal = null;
            OpenSession(config);
            linkLossReported = false;

            // a Start on a board with data asks "Continue / Start over / Cancel" (the REST dialog), knowing what the document holds
            AtomSpectraTcpSession tcp = session as AtomSpectraTcpSession;
            int foldChannels = ProfileChannels(r);   // the channels every snapshot is folded to (FoldAndUpdate)
            if (tcp != null)
            {
                tcp.Ask = AppUi.HasWindows ? ReplaceOnlyIfFolds(r, foldChannels, AnswerGiven(r, new Func<AtomSpectraRestStartQuestion, AtomSpectraRestStartChoice>(q => { q.Note = StartNote(tcp); return AtomSpectraRestUi.AskStart(q); }))) : null;
                tcp.DocumentSeconds = (long)r.ResultDataStatus.ElapsedTime.TotalSeconds;
                tcp.DocumentCounts = r.EnergySpectrum != null ? r.EnergySpectrum.ValidPulseCount : 0;
            }
            IAtomSpectraSession s = session;
            bool newDoc = start && newDocumentCreated;
            string info = null, calibration = null, boardFirmware = null;   // boardFirmware: read only after a Start without an answer
            AtomSpectraTcpBeginOutcome outcome = AtomSpectraRestUi.RunWithWait(delegate
            {
                AtomSpectraTcpBeginOutcome o = s.Begin(newDoc, CommandTimeoutMs);
                if (o == AtomSpectraTcpBeginOutcome.Started)
                {
                    // -inf and -cal behind the same wait window, not on the UI thread after it (InfoTimeoutMs each)
                    info = QueryQuietly(s, "-inf");
                    calibration = QueryQuietly(s, "-cal");
                }
                else if (AtomSpectraFirmware.IsNoAnswerOutcome(o))
                {
                    // one short GET /api/status behind the same wait window; its own failure is ignored (null)
                    string urlError;
                    if (config.RestUrlIsTcpHost()) boardFirmware = AtomSpectraFirmware.ReadVersion(config.CreateRestClient(out urlError), AtomSpectraRestSession.FirmwareReadMs);   // not another board's version (a RestUrl left over from another board)
                }
                return o;
            });
            if (outcome == AtomSpectraTcpBeginOutcome.Cancelled)
            {
                SetStatus(s, Resources.AtomSpectraTcpStatusCancelled);
                return false;
            }

            if (outcome == AtomSpectraTcpBeginOutcome.Started)
            {
                if (newDocumentCreated || s.ReplacedDocument)
                {
                    // Joining or resuming the board's acquisition (or taking its spectrum in place of the document's): the document starts when the board started.
                    r.StartTime = DateTime.Now - TimeSpan.FromSeconds(s.ElapsedAtBeginSeconds);
                    newDocumentCreated = false;
                }
                TakeBoardSpectrum(r, s, foldChannels);
                if (s.ElapsedAtBeginSeconds > 0)
                {
                    // the preset counts the board's time: it must not stop the run at the first tick (as REST, MeasurementController.cs:749)
                    int before = r.ResultDataStatus.PresetTime;
                    r.ResultDataStatus.PresetTime = AtomSpectraRestPolicy.PresetForContinue(before, (long)s.ElapsedAtBeginSeconds, 0);
                    if (presetBeforeBoardTime < 0) presetBeforeBoardTime = before;   // Clear puts it back
                    AtomSpectraTcpLog.Info("Session", "preset " + before.ToString(CultureInfo.InvariantCulture) + " s -> "
                        + r.ResultDataStatus.PresetTime.ToString(CultureInfo.InvariantCulture) + " s (board time "
                        + s.ElapsedAtBeginSeconds.ToString("R", CultureInfo.InvariantCulture) + " s)");
                }
                SetStatus(s, s.JoinedRunning ? Resources.AtomSpectraTcpStatusJoined : Resources.AtomSpectraTcpStatusMeasuring);
                r.ResultDataStatus.Recording = true;
                if (ReferenceEquals(stopAsked, r)) stopAsked = null;
                ApplyDeadTime(r, info);
                RefreshCalibrationFromDevice(r, calibration);   // the board calibration -> document and configuration
                return true;
            }

            AtomSpectraTcpLog.Warn("Session", "Start refused: " + outcome + " (" + s.TransportName + " " + AtomSpectraTcpSession.HostKey(s.Host, s.Port) + ") " + (s.LastError ?? ""));
            SetStatus(s, Resources.AtomSpectraTcpStatusNotStarted);

            switch (outcome)
            {
                case AtomSpectraTcpBeginOutcome.NoHost:
                    refusal = Resources.ERRAtomSpectraTcpNoHost;
                    break;
                case AtomSpectraTcpBeginOutcome.HostBusy:
                    refusal = string.Format(CultureInfo.InvariantCulture, Resources.ERRAtomSpectraTcpHostBusy, config.Host, s.Port);
                    break;
                case AtomSpectraTcpBeginOutcome.ConnectFailed:
                case AtomSpectraTcpBeginOutcome.LinkClosed:
                    refusal = string.Format(CultureInfo.InvariantCulture, Resources.ERRAtomSpectraTcpConnect, config.Host, s.Port, s.LastError ?? "");
                    break;
                case AtomSpectraTcpBeginOutcome.Refused:   // no window to ask in
                    refusal = RefusalText(tcp);
                    break;
                default:
                    refusal = string.Format(CultureInfo.InvariantCulture, Resources.ERRAtomSpectraTcpNoAnswer, config.Host, s.Port);
                    break;
            }
            refusal = AtomSpectraFirmware.AppendWarning(refusal, boardFirmware);   // a known version below 1.2.39 only; otherwise the text stays as it is
            return false;
        }

        // The note under the Start question: why a Clear left for this Start is asked about, what the probe could not see, then what each button sends.
        private static string StartNote(AtomSpectraTcpSession tcp)
        {
            string seconds = ProbeSeconds();
            string note = Resources.AtomSpectraTcpStartNote;
            if (tcp.ResetOnNextStart)
                note = Resources.AtomSpectraTcpStartClearPending + Environment.NewLine + note;   // O3: the board holds an acquisition the Clear did not see
            if (tcp.BoardState == AtomSpectraTcpBoardState.Silent)
                return string.Format(CultureInfo.InvariantCulture, Resources.AtomSpectraTcpStartSilent, seconds) + Environment.NewLine + note;
            if (tcp.BoardState == AtomSpectraTcpBoardState.Uncertain)
                return string.Format(CultureInfo.InvariantCulture, Resources.AtomSpectraTcpStartUncertain, seconds) + Environment.NewLine + note;
            return note;
        }

        private static string ProbeSeconds()
        {
            return (AtomSpectraTcpSession.ProbeMs / 1000.0).ToString("0.0", CultureInfo.InvariantCulture);
        }

        // A Start without a window to ask in: the text of the reason (nothing was sent to the board).
        private static string RefusalText(AtomSpectraTcpSession tcp)
        {
            AtomSpectraTcpRefusal why = tcp != null ? tcp.Refusal : AtomSpectraTcpRefusal.StoppedWithData;
            switch (why)
            {
                case AtomSpectraTcpRefusal.Unknown:
                    return string.Format(CultureInfo.InvariantCulture, Resources.ERRAtomSpectraTcpHeadlessUnknown, ProbeSeconds());
                case AtomSpectraTcpRefusal.BehindDocument:
                    return Resources.ERRAtomSpectraTcpHeadlessBehind;
                case AtomSpectraTcpRefusal.ClearPending:
                    return Resources.ERRAtomSpectraTcpHeadlessClearPending;
                default:
                    return Resources.ERRAtomSpectraTcpHeadlessStopped;
            }
        }

        private static string QueryQuietly(IAtomSpectraSession s, string command)
        {
            try
            {
                return s.Query(command, InfoTimeoutMs);
            }
            catch (Exception ex)
            {
                Trace.WriteLine("AtomSpectraTcp: " + command + " failed: " + ex.Message);
                return null;
            }
        }

        private bool Refuse(string message)
        {
            string name = resultData != null && resultData.DeviceConfig != null ? resultData.DeviceConfig.Name : "unknown";
            if (!AppUi.HasWindows)
            {
                throw new InvalidOperationException("BecqMoni: AtomSpectra TCP did not start the measurement on \"" + name + "\": " + message + " A headless run must not continue with an empty spectrum.");
            }
            AppUi.Report(message, Resources.ErrorString, MessageBoxIcon.Error);
            return false;
        }

        // On the reader thread: only the status line and a post to the UI thread (the fold reads the configuration there).
        private void Acquisition_DataReady(object sender, AtomSpectraTcpDataReadyArgs e)
        {
            if (resultData == null)
            {
                return;
            }

            IAtomSpectraSession s = session;
            if (s != null)
            {
                SetStatus(s, string.Format(CultureInfo.InvariantCulture, Resources.AtomSpectraTcpStatusReceiving, e.ElapsedTime, e.Cps));
            }

            if (MainForm.originalContext != null)
            {
                MainForm.originalContext.Post(d => FoldAndUpdate(e), null);
            }
        }

        // On the UI thread: the profile's channels come from the configuration map here, never from the reader thread.
        private void FoldAndUpdate(AtomSpectraTcpDataReadyArgs e)
        {
            ResultData data = resultData;
            if (data == null)
            {
                return;
            }

            UpdateHistogram(BuildArgs(e, ProfileChannels(data), restChannels));   // BuildArgs: a static method, tested without a window
        }

        // The channels of the profile the snapshots are folded to: the configuration map first, else the document's own configuration (0: none).
        private static int ProfileChannels(ResultData data)
        {
            if (data == null || data.DeviceConfig == null)
            {
                return 0;
            }
            DeviceConfigInfo configInfo;
            return DeviceConfigManager.GetInstance().DeviceConfigMap.TryGetValue(data.DeviceConfig.Guid ?? "", out configInfo)
                ? configInfo.NumberOfChannels : data.DeviceConfig.NumberOfChannels;
        }

        // The snapshot for the document: folded to the profile's channels; a REST snapshot is folded to restChannels (fixed at Start,
        // a config edit during the run does not apply) and keeps the board's own live time, hist_ok, "calib" and the scale
        // of that "calib" (calib_channels).
        internal static AtomSpectraTcpDataReadyArgs BuildArgs(AtomSpectraTcpDataReadyArgs e, int channels, int restChannels)
        {
            AtomSpectraRestDataReadyArgs rest = e as AtomSpectraRestDataReadyArgs;
            if (rest != null && restChannels > 0) channels = restChannels;
            int[] hist = AtomSpectraTcpAcquisition.Compress(e.Histogram, channels);
            if (rest == null) return new AtomSpectraTcpDataReadyArgs(hist, e.CpuLoad, e.ElapsedTime, e.InvalidPulses, e.Cps);
            AtomSpectraRestDataReadyArgs copy = new AtomSpectraRestDataReadyArgs(hist, e.CpuLoad, e.ElapsedTime, e.InvalidPulses, e.Cps,
                rest.LiveSeconds, rest.Total, rest.SpectrometerSilent, rest.HistOk, rest.Calibration);
            copy.CalibChannels = rest.CalibChannels;
            return copy;
        }

        private void UpdateHistogram(AtomSpectraTcpDataReadyArgs e)
        {
            // stopping: the run ended when Stop was pressed (O1) - the snapshots that come while its question is open do not move the document on
            if (resultData == null || !resultData.ResultDataStatus.Recording || stopping)
            {
                return;
            }

            // Ported from AtomSpectraDeviceController.update_hystogram (AtomSpectraDeviceController.cs:367-410).
            EnergySpectrum spectrum = resultData.EnergySpectrum;    // re-fetched: Clear replaces the array
            if (spectrum == null || spectrum.Spectrum == null)
            {
                return;
            }

            if (e.Histogram.Length != spectrum.Spectrum.Length && session is AtomSpectraRestSession)
            {
                // REST: detach without -sto - the board keeps counting, the document is not touched
                session.Detach();
                resultData.ResultDataStatus.Recording = false;
                if (resultData.MeasurementController != null) resultData.MeasurementController.NotifyMeasurementStoppedByDevice();
                AppUi.Report(Resources.ERRIncompatibleChannelParameters, "", MessageBoxIcon.None);
                return;
            }

            if (e.Histogram.Length != spectrum.Spectrum.Length)
            {
                // Stop first, then report: a modal box first would let the next packet open another.
                if (resultData.MeasurementController != null)
                {
                    resultData.MeasurementController.StopRecording();
                }
                resultData.ResultDataStatus.Recording = false;
                AppUi.Report(Resources.ERRIncompatibleChannelParameters, "", MessageBoxIcon.None);
                return;
            }

            // AFTER the length checks above - a snapshot that is refused must not have rewritten the calibration and the configuration file
            CopySnapshot(resultData, spectrum, e, true);
        }

        // One snapshot into the document, its length checked already: the channels, the counts, the time and the live time (calibration:
        // the board calibration of the first REST snapshot after Start as well). The path of every snapshot (UpdateHistogram) and of the
        // board's spectrum that Replace takes at once (TakeBoardSpectrum).
        private void CopySnapshot(ResultData data, EnergySpectrum spectrum, AtomSpectraTcpDataReadyArgs e, bool calibration)
        {
            AtomSpectraRestDataReadyArgs restSnapshot = e as AtomSpectraRestDataReadyArgs;
            if (calibration && boardCalibrationPending && restSnapshot != null)
            {
                // the board calibration of the first snapshot after Start -> the document and the configuration
                boardCalibrationPending = false;
                // "calib" is for calib_channels; the document has e.Histogram.Length channels (board or client fold)
                double[] slots = AtomSpectraRestCalibration.ForChannels(restSnapshot.Calibration, restSnapshot.CalibChannels, e.Histogram.Length);
                AtomSpectraTcpLog.Info("Cal", ApplyBoardCalibration(spectrum, data.DeviceConfig, slots, configSaver));
            }

            Array.Copy(e.Histogram, spectrum.Spectrum, e.Histogram.Length);

            long sum = e.Histogram.Sum(x => (long)x);
            spectrum.TotalPulseCount = sum + e.InvalidPulses;
            spectrum.ValidPulseCount = sum;
            spectrum.ChannelPitch = 1;

            TimeSpan elapsed = TimeSpan.FromSeconds(e.ElapsedTime);
            data.ResultDataStatus.TotalTime = elapsed;
            data.ResultDataStatus.ElapsedTime = elapsed;

            AtomSpectraRestDataReadyArgs rest = e as AtomSpectraRestDataReadyArgs;
            bool boardLive = rest != null && !double.IsNaN(rest.LiveSeconds) && !double.IsInfinity(rest.LiveSeconds)
                && rest.LiveSeconds > 0 && rest.LiveSeconds <= elapsed.TotalSeconds + 1;
            if (data.MeasurementController != null)
            {
                MeasurementController.SetSpectrumTimes(spectrum, elapsed.TotalSeconds, data.DeviceConfig, boardLive);
                if (boardLive)
                {
                    // REST: live time measured by the board itself (GET /api/spectrum.json "live")
                    spectrum.LiveTime = Math.Min(rest.LiveSeconds, elapsed.TotalSeconds);
                }
            }
        }

        // REST Start (AtomSpectraTcpDeviceController.Rest.cs): -inf behind the wait window, the dead time applied here, on the UI thread.
        private void RefreshDeadTimeFromDevice(ResultData r)
        {
            IAtomSpectraSession s = session;
            if (s == null)
            {
                return;
            }

            string answer = AtomSpectraRestUi.RunWithWait(delegate { return QueryQuietly(s, "-inf"); });
            ApplyDeadTime(r, answer);
        }

        // The answer to -inf -> the dead time of the DOCUMENT's config, as AtomSpectraDeviceController.RefreshDeadTimeFromDevice:
        // live time (SetSpectrumTimes) reads the dead time from resultData.DeviceConfig. null (no answer): nothing changes.
        private static void ApplyDeadTime(ResultData r, string answer)
        {
            AtomSpectraTcpDeviceConfig config = r == null || r.DeviceConfig == null ? null : r.DeviceConfig.InputDeviceConfig as AtomSpectraTcpDeviceConfig;
            if (config == null || answer == null)
            {
                return;
            }

            try
            {
                string error;
                double tau = AtomSpectraDeviceConfig.DeadTimeFromInfo(answer, out error);
                if (double.IsNaN(tau))
                {
                    Trace.WriteLine("AtomSpectraTcp: failed to parse dead time: " + error);
                    return;
                }

                if (config.DeadTimeValue != tau)
                {
                    Trace.WriteLine("AtomSpectraTcp: updating dead time from " + config.DeadTimeValue.ToString("R", CultureInfo.InvariantCulture)
                        + " to " + tau.ToString("R", CultureInfo.InvariantCulture));
                    config.DeadTimeValue = tau;
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine("AtomSpectraTcp: error refreshing dead time: " + ex.Message);
            }
        }

        private void Session_LinkLost(object sender, EventArgs e)
        {
            IAtomSpectraSession s = session;
            Trace.WriteLine("AtomSpectraTcp: link lost " + (s != null ? AtomSpectraTcpSession.HostKey(s.Host, s.Port) : "unknown"));
            AtomSpectraRestSession denied = s as AtomSpectraRestSession;   // 401/403 is a refusal of access, not a lost link
            // every loss goes to the status line, a 401/403 after a restored link too (R7); the window only once per measurement
            if (s != null) SetStatus(s, denied != null ? denied.LinkLostStatusText() : Resources.AtomSpectraTcpStatusLinkLost);
            if (linkLossReported)
            {
                return;
            }
            linkLossReported = true;
            if (s == null)
            {
                return;
            }
            // The status bar shows "port lost" only for the COM type (MainForm); until that is extended,
            // the loss is announced once per measurement.
            AtomSpectraRestSession rs = s as AtomSpectraRestSession;   // REST has its own text, not the TCP bridge one
            ReportLater(rs != null ? rs.LinkLostText()
                : string.Format(CultureInfo.InvariantCulture, Resources.MSGAtomSpectraTcpLinkLost, s.Host, s.Port), "link lost");
        }

        // The board's counters went down without our -rst (raised on the reader thread): the document keeps its data, the recording
        // stops, the session detaches without -sto (the acquisition on the board is not the document's any more).
        private void Session_BoardReset(object sender, AtomSpectraTcpBoardResetArgs e)
        {
            IAtomSpectraSession s = sender as IAtomSpectraSession;
            ResultData data = resultData;
            if (s == null)
            {
                return;
            }

            SetStatus(s, Resources.AtomSpectraTcpStatusBoardReset);
            string text = string.Format(CultureInfo.InvariantCulture, Resources.MSGAtomSpectraTcpBoardReset, s.Host, s.Port,
                e.TimeBefore, e.TimeNow, e.TotalBefore, e.TotalNow);
            if (MainForm.originalContext != null)
            {
                MainForm.originalContext.Post(delegate
                {
                    bool recording = data != null && data.ResultDataStatus.Recording;   // a recording the user stopped first is not offered a replacement
                    StopAfterBoardReset(s, data);
                    ScheduleBoardOffer(text, "board reset from outside: " + e.LogText(), data, e.TimeNow, e.TotalNow, recording);   // not over a wait window
                }, null);
            }
            else
            {
                if (data != null) data.ResultDataStatus.Recording = false;
                ThreadPool.QueueUserWorkItem(_ => s.Detach());   // not on the reader thread that raised the event
                Trace.WriteLine(text);
            }
        }

        // On the UI thread, the recording stopped: the board's new acquisition can be taken into this document (its spectrum
        // replaced, after a confirmation) or into a new document with the same configuration, efficiency and background.
        private static void OfferBoardAcquisition(string text, string logText, ResultData data, long boardSeconds, long boardCounts, bool recording)
        {
            if (RecordingSeen != null) RecordingSeen(recording);
            MainForm main = Application.OpenForms.OfType<MainForm>().FirstOrDefault();
            if (!AppUi.HasWindows || MainForm.ApplicationClosing || data == null || main == null || !OfferAfterReset(recording, boardSeconds, boardCounts))
            {
                ShowOrLog(ResetAdvice(text), logText);   // without the window: what to do next
                return;
            }
            long docSeconds = (long)data.ResultDataStatus.ElapsedTime.TotalSeconds;
            long docCounts = data.EnergySpectrum != null ? data.EnergySpectrum.ValidPulseCount : 0;
            AtomSpectraBoardResetChoice choice = AtomSpectraRestUi.AskAfterBoardReset(text, docSeconds, docCounts, boardSeconds, boardCounts);
            AtomSpectraTcpLog.Info("Session", logText + "; the user chose " + choice);
            if (choice != AtomSpectraBoardResetChoice.Close) TakeBoardAcquisition(main, data, choice == AtomSpectraBoardResetChoice.NewDocument, boardSeconds, boardCounts);
        }

        // The Start of the spectrum that takes the board's acquisition, its question answered already: Replace in this
        // document, Continue (join) in a new one. Nothing is started when the document is gone.
        private static void TakeBoardAcquisition(MainForm main, ResultData data, bool newDocument, long boardSeconds, long boardCounts)
        {
            ResultData target = newDocument ? main.CreateDocumentLike(data) : data;
            if (target == null) return;
            answerFor = target;
            answer = newDocument ? AtomSpectraRestStartChoice.Continue : AtomSpectraRestStartChoice.Replace;
            answerSeconds = boardSeconds;
            answerCounts = boardCounts;
            try
            {
                if (!main.StartRecordingOf(target, !newDocument))
                    AtomSpectraTcpLog.Warn("Session", "the board's acquisition was not taken: the document is closed, another of its spectra is selected or the Start was refused");
            }
            finally
            {
                answerFor = null;   // a Start refused before the question must not leave the answer for a later one
            }
        }

        private void StopAfterBoardReset(IAtomSpectraSession s, ResultData data)
        {
            if (session == s)
            {
                s.Detach();
            }
            if (data == null || !data.ResultDataStatus.Recording)
            {
                return;
            }
            data.EndTime = DateTime.Now;
            if (data.MeasurementController != null)
            {
                data.MeasurementController.NotifyMeasurementStoppedByDevice();
            }
            else
            {
                data.ResultDataStatus.Recording = false;
            }
        }

        private static void SetStatus(IAtomSpectraSession s, string state)
        {
            if (s != null)
            {
                AtomSpectraTcpLog.SetStatus(s.TransportName, AtomSpectraTcpSession.HostKey(s.Host, s.Port), state);
            }
        }

        // Posted, not modal inside the caller (a preset-time Stop saves the document right after StopMeasurement).
        private static void ReportLater(string text, string logText)
        {
            if (AppUi.HasWindows && MainForm.originalContext != null && !MainForm.ApplicationClosing)
            {
                MainForm.originalContext.Post(_ => AtomSpectraRestUi.AfterWait(() => ShowOrLog(text, logText)), null);   // not over the wait window
            }
            else
            {
                ShowOrLog(text, logText);
            }
        }

        // While the program closes no window is raised - a modal box would hold up the close (and a Windows shutdown) and nothing
        // can be done about it then: the log only, as AmplitudaUsbDeviceController.ReportLoss.
        private static void ShowOrLog(string text, string logText)
        {
            if (AppUi.HasWindows && !MainForm.ApplicationClosing)
            {
                AppUi.Report(text, "", MessageBoxIcon.Warning);
                return;
            }
            AtomSpectraTcpLog.Warn("Session", logText + " (no window: " + (MainForm.ApplicationClosing ? "the program closes" : "no windows") + ")");
            Trace.WriteLine(text);
        }
    }
}
