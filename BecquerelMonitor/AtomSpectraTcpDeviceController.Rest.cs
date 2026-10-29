using BecquerelMonitor.Properties;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    // REST branch of the controller: the board is the only source of truth;
    // Start reads the board behind a wait window and asks when it holds data; joining never sends a command;
    // a board reset/restart from outside stops the recording without touching the document;
    // Clear resets the board only now, after a confirmation with the board's numbers.
    public partial class AtomSpectraTcpDeviceController
    {
        private string restKey;
        private int restChannels;
        private string restOpenError;

        private void OpenRestSession(AtomSpectraTcpDeviceConfig config)
        {
            string key = config.RestSessionKey();
            if (session is AtomSpectraRestSession && session.IsOpen && key != restKey)
            {
                // a configuration saved during a run never drops the running session (its own -sto would be lost)
                AtomSpectraTcpLog.Warn("Session", "REST settings changed during a run: they apply after Stop");
                return;
            }
            if (session != null && (!(session is AtomSpectraRestSession) || key != restKey)) CloseSession();
            if (carriedResetKey != null)
            {
                // REST has no deferred reset: the one the TCP session left is dropped, not kept for a later switch back to TCP
                AtomSpectraTcpLog.Info("Session", "deferred reset dropped: transport changed to REST (" + carriedResetKey + ")");
                carriedResetKey = null;
            }
            if (session != null) return;
            string error;
            AtomSpectraRestClient client = config.CreateRestClient(out error);
            restOpenError = error;
            if (client == null)
            {
                AtomSpectraTcpLog.Warn("Session", "REST client not created: " + error);
                return;
            }
            AtomSpectraRestSession rest = new AtomSpectraRestSession(client, config.RestPollSeconds, config.RestObserveOnly, config.RestLimitSeconds);
            rest.DataReady += Acquisition_DataReady;
            rest.LinkLost += Session_LinkLost;
            rest.BoardReset += RestBoardReset;
            rest.BoardStalled += RestBoardStalled;
            rest.BoardKeepsCounting += RestBoardKeepsCounting;
            session = rest;
            restKey = key;
            AtomSpectraRestClient.Current = client;
            AtomSpectraTcpLog.DataAgeSeconds = delegate
            {
                AtomSpectraRestAcquisition a = rest.Acquisition;
                return a == null ? -1 : a.DataAgeSeconds;
            };
            AtomSpectraTcpLog.StaleAfterSeconds = 2 * config.RestPollSeconds + 5;
            AtomSpectraTcpLog.SetStatus("REST", AtomSpectraTcpLog.Mask(client.BaseUrl), rest.ObserveOnly ? Resources.AtomSpectraRestStatusIdleObserve : Resources.AtomSpectraRestStatusIdle);
        }

        private void UnhookRest()
        {
            AtomSpectraRestSession rest = session as AtomSpectraRestSession;
            if (rest != null)
            {
                rest.BoardReset -= RestBoardReset;
                rest.BoardStalled -= RestBoardStalled;
                rest.BoardKeepsCounting -= RestBoardKeepsCounting;
            }
        }

        // The refusal of a profile whose channels the board cannot fill (null: the profile is fine). The board gives 8192 channels and
        // folds them by a whole factor only: 16384 would send -sta and then detach at the first snapshot (the board counting on), and
        // 6000 or 3000 would lose the top channels in the fold without a word.
        internal static string RestChannelsRefusal(int channels)
        {
            if (channels <= 0 || AtomSpectraRestSpectrum.ChannelsSupported(channels)) return null;
            return string.Format(CultureInfo.InvariantCulture, Resources.ERRAtomSpectraRestChannels, channels);
        }

        // The user's text of a board URL that gives no client (the masked URL; user:password named when that is the reason)
        private static string BadUrlText(AtomSpectraTcpDeviceConfig config)
        {
            string url = config.EffectiveRestUrl();
            string text = string.Format(CultureInfo.InvariantCulture, Resources.ERRAtomSpectraRestBadUrl, AtomSpectraTcpLog.Mask(url));
            Uri u;
            if (Uri.TryCreate(url, UriKind.Absolute, out u) && !string.IsNullOrEmpty(u.UserInfo)) text += " " + Resources.ERRAtomSpectraRestUrlUserInfo;
            return text;
        }

        private bool BeginRest(ResultData r, AtomSpectraTcpDeviceConfig config)
        {
            if (string.IsNullOrWhiteSpace(config.RestUrl) && string.IsNullOrWhiteSpace(config.Host))
            {
                return Refuse(Resources.ERRAtomSpectraTcpNoHost);   // an empty address is saved; it is refused here, at Start
            }
            // before any request to the board: a profile the board cannot fill is refused here
            int profileChannels = r.DeviceConfig != null ? r.DeviceConfig.NumberOfChannels : 0;
            string channelsRefusal = RestChannelsRefusal(profileChannels);
            if (channelsRefusal != null)
            {
                AtomSpectraTcpLog.Warn("Session", AtomSpectraTcpLog.Format("Start refused: the profile has {0} channels, the board folds 8192 by a whole factor only; nothing sent", profileChannels));
                r.ResultDataStatus.Recording = false;
                return Refuse(channelsRefusal);
            }
            string refusal = null;
            try
            {
                OpenRestSession(config);
                AtomSpectraRestSession rest = session as AtomSpectraRestSession;
                if (rest == null)
                {
                    refusal = BadUrlText(config);
                }
                else
                {
                    linkLossReported = false;
                    rest.DocumentSeconds = (long)r.ResultDataStatus.ElapsedTime.TotalSeconds;
                    rest.DocumentCounts = r.EnergySpectrum != null ? r.EnergySpectrum.ValidPulseCount : 0;
                    // the document's channels: "Continue" is offered only when the board holds at least as much in every one of them
                    rest.DocumentBins = r.EnergySpectrum != null && r.EnergySpectrum.Spectrum != null ? (int[])r.EnergySpectrum.Spectrum.Clone() : null;
                    // asked on the UI thread; a headless run never asks (joins or refuses)
                    rest.Ask = AppUi.HasWindows ? ReplaceOnlyIfFolds(r, profileChannels, AnswerGiven(r, new Func<AtomSpectraRestStartQuestion, AtomSpectraRestStartChoice>(AtomSpectraRestUi.AskStart))) : null;
                    // ?channels=N of the profile at Start (the same number restChannels keeps below)
                    rest.RequestChannels = profileChannels;
                    bool newDoc = newDocumentCreated;
                    AtomSpectraTcpBeginOutcome outcome = AtomSpectraRestUi.RunWithWait(delegate
                    {
                        AtomSpectraTcpBeginOutcome o = rest.Begin(newDoc, CommandTimeoutMs);
                        // GET /api/device (dead time) on this worker as well: the UI thread never waits for the board
                        if (o == AtomSpectraTcpBeginOutcome.Started) RefreshDeadTimeFromDevice(r);
                        return o;
                    });
                    if (outcome == AtomSpectraTcpBeginOutcome.Started)
                    {
                        restChannels = profileChannels;   // fixed at Start
                        long boardSeconds = (long)rest.ElapsedAtBeginSeconds;
                        r.StartTime = DateTime.Now - TimeSpan.FromSeconds(boardSeconds);   // the document starts when the board started
                        newDocumentCreated = false;
                        TakeBoardSpectrum(r, rest, profileChannels);
                        int before = r.ResultDataStatus.PresetTime;
                        // the preset must not stop the board before its own limit (MeasurementController.cs:749)
                        r.ResultDataStatus.PresetTime = rest.PresetAfterStart(before);
                        AtomSpectraTcpLog.Info("Session", AtomSpectraTcpLog.Format("preset {0} s -> {1} s (board time {2} s)", before, r.ResultDataStatus.PresetTime, boardSeconds));
                        AtomSpectraTcpLog.SetStatus("REST", rest.Endpoint, rest.ObserveOnly ? Resources.AtomSpectraRestStatusObserving
                            : (rest.JoinedRunning ? Resources.AtomSpectraRestStatusJoined : Resources.AtomSpectraRestStatusMeasuring));
                        r.ResultDataStatus.Recording = true;
                        if (ReferenceEquals(stopAsked, r)) stopAsked = null;
                        boardCalibrationPending = true;   // the first snapshot's "calib" -> document and configuration
                        return true;
                    }
                    r.ResultDataStatus.Recording = false;
                    AtomSpectraTcpLog.Warn("Session", "Start not done: " + outcome + " " + rest.Failure + " " + (rest.LastError ?? ""));
                    AtomSpectraTcpLog.SetStatus("REST", rest.Endpoint, Resources.AtomSpectraRestStatusNotStarted);
                    if (outcome == AtomSpectraTcpBeginOutcome.Cancelled) return false;
                    refusal = RestFailureText(rest, outcome);
                }
            }
            catch (Exception ex)
            {
                // our own text, not the generic "Bluetooth not supported" of MeasurementController
                AtomSpectraTcpLog.Error("Session", "Start failed: " + ex.GetType().Name + ": " + ex.Message);
                refusal = string.Format(CultureInfo.InvariantCulture, Resources.ERRAtomSpectraRestStart, ex.Message);
            }
            r.ResultDataStatus.Recording = false;
            return Refuse(refusal);
        }

        private static string RestFailureText(AtomSpectraRestSession rest, AtomSpectraTcpBeginOutcome outcome)
        {
            switch (rest.Failure)
            {
                case AtomSpectraRestFailure.Unreachable:
                    return rest.UnreachableText();
                case AtomSpectraRestFailure.StopRefused:
                case AtomSpectraRestFailure.ResetRefused:
                    return string.Format(CultureInfo.InvariantCulture, Resources.ERRAtomSpectraRestRestartPartial, rest.LastErrorText ?? Resources.AtomSpectraRestErrNoAnswer);
                case AtomSpectraRestFailure.HeadlessBehind:
                    return Resources.ERRAtomSpectraRestHeadlessBehind;
                case AtomSpectraRestFailure.HeadlessAsk:   // a stopped board with data and no window to ask in
                    return AtomSpectraRestSession.HeadlessAskText;
                case AtomSpectraRestFailure.DocumentNotEmpty:
                    return Resources.ERRAtomSpectraRestNeedNewDocument;
                case AtomSpectraRestFailure.ObserveBehind:
                    return Resources.ERRAtomSpectraRestObserveBehind;
                default:
                    return string.Format(CultureInfo.InvariantCulture, Resources.ERRAtomSpectraRestStart, rest.LastErrorText ?? Resources.AtomSpectraRestErrNoAnswer);
            }
        }

        private enum RestClearPlan { Ask, BoardConfirmed, DocumentOnly }

        private RestClearPlan restClearPlan = RestClearPlan.Ask;
        private ResultData restClearFor;
        private ResultData tcpClearFor;   // the document whose Clear was confirmed by the one question of ConfirmClearTcp

        public override bool ConfirmClear(ResultData r, Func<bool> ask = null)   // ask: the window question of other devices, not used here
        {
            restClearPlan = RestClearPlan.Ask;
            restClearFor = null;
            tcpClearFor = null;

            // PR #39 review: every window of this confirmation (the TCP question of a joined acquisition, the report of a board that could not
            // be read and the question after it, the REST question) holds the windows waiting for a wait window to close: "The board was
            // reset" of another document must not come up inside it and pass for its answer (PR #38 review, R8)
            return AtomSpectraRestUi.HoldingAfterWait(() => ConfirmClearHeld(r));
        }

        private bool ConfirmClearHeld(ResultData r)
        {
            AtomSpectraTcpDeviceConfig config = ConfigOf(r);

            if (deviceGuid == null
                || config == null
                || MeasurementController.IsDeviceLeasedByOther(deviceGuid, r.MeasurementController))
            {
                return DeviceController.AskClearDocument();
            }

            if (config.Transport != AtomSpectraTransport.Rest)
            {
                return ConfirmClearTcp(r);   // ONE question for a joined TCP acquisition too (as #BM-22 for REST)
            }

            return ConfirmClearRest(r, config);
        }

        private bool ConfirmClearRest(ResultData r, AtomSpectraTcpDeviceConfig config)
        {
            // a TCP session (the transport was switched to REST; it stays in the controller from Stop to Start too) and its deferred reset are
            // replaced only in ClearRest after a "Yes", never before the answer: the board is read here through a session of its own (nothing is
            // started, nothing hooked), the controller's stays as it is. Before the PR #39 review this case asked the plain question and then,
            // after a "Yes", the board's own one - two questions, and "Yes, No" cleared the document only
            bool keepSession = (session != null && !(session is AtomSpectraRestSession)) || carriedResetKey != null;
            AtomSpectraRestSession rest;
            if (keepSession)
            {
                string clientError;
                AtomSpectraRestClient client = config.CreateRestClient(out clientError);
                rest = client == null ? null : new AtomSpectraRestSession(client, config.RestPollSeconds, config.RestObserveOnly, config.RestLimitSeconds);
            }
            else
            {
                OpenRestSession(config);
                rest = session as AtomSpectraRestSession;
            }

            if (rest == null || rest.ObserveOnly)
            {
                return DeviceController.AskClearDocument();
            }

            string error = null;
            AtomSpectraRestBoardSample board = AtomSpectraRestUi.RunWithWait(delegate
            {
                string e, user;
                AtomSpectraRestBoardSample s = rest.ReadBoard(AtomSpectraRestSession.ProbeTimeoutMs, out e, out user);
                if (s == null) AtomSpectraTcpLog.Warn("Session", "Clear: the board was not read: " + e);
                error = user;
                return s;
            });

            if (board == null)
            {
                AppUi.Report(string.Format(CultureInfo.InvariantCulture, Resources.MSGAtomSpectraRestNotReset, error ?? Resources.AtomSpectraRestErrNoAnswer), "", MessageBoxIcon.Warning);

                if (!DeviceController.AskClearDocument())
                {
                    return false;
                }

                restClearPlan = RestClearPlan.DocumentOnly;
                restClearFor = r;
                return true;
            }

            if (!AtomSpectraRestUi.ConfirmClear(board, true))
            {
                AtomSpectraTcpLog.Info("Session", "Clear: declined, nothing is cleared (document and board)");
                return false;
            }

            restClearPlan = RestClearPlan.BoardConfirmed;
            restClearFor = r;
            return true;
        }

        // #BM-22: ClearRest below carries out what ConfirmClear has already asked (restClearPlan)
        private void ClearRest(ResultData r, AtomSpectraTcpDeviceConfig config)
        {
            if (!AppUi.HasWindows) throw new InvalidOperationException("BecqMoni: AtomSpectra REST Clear needs a confirmation window; the board was not reset.");
            // #BM-22: ConfirmClear (the one question of "Clear") has already been answered for this document
            RestClearPlan plan = ReferenceEquals(restClearFor, r) ? restClearPlan : RestClearPlan.Ask;
            restClearPlan = RestClearPlan.Ask;
            restClearFor = null;
            OpenRestSession(config);   // after the "Yes" only: a TCP session left by a switch of the transport (and its deferred reset) goes now
            if (plan == RestClearPlan.DocumentOnly) return;   // the board could not be read, the user chose to clear the document only
            AtomSpectraRestSession rest = session as AtomSpectraRestSession;
            if (rest == null)
            {
                AppUi.Report(BadUrlText(config), "", MessageBoxIcon.Warning);
                return;
            }
            if (rest.ObserveOnly)
            {
                rest.ClearNow(CommandTimeoutMs);
                AppUi.Report(Resources.MSGAtomSpectraRestObserveOnly, "", MessageBoxIcon.Information);
                return;
            }
            string error = null;
            bool confirmed = plan == RestClearPlan.BoardConfirmed;   // the board's numbers were shown in the one question
            AtomSpectraRestBoardSample board = confirmed ? null : AtomSpectraRestUi.RunWithWait(delegate
            {
                string e, user;
                AtomSpectraRestBoardSample s = rest.ReadBoard(AtomSpectraRestSession.ProbeTimeoutMs, out e, out user);
                if (s == null) AtomSpectraTcpLog.Warn("Session", "Clear: the board was not read: " + e);
                error = user;
                return s;
            });
            if (board == null && !confirmed)
            {
                AppUi.Report(string.Format(CultureInfo.InvariantCulture, Resources.MSGAtomSpectraRestNotReset, error ?? Resources.AtomSpectraRestErrNoAnswer), "", MessageBoxIcon.Warning);
                return;
            }
            if (!confirmed && !AtomSpectraRestUi.ConfirmClear(board))
            {
                AtomSpectraTcpLog.Info("Session", "Clear: declined, board not reset");
                AppUi.Report(string.Format(CultureInfo.InvariantCulture, Resources.MSGAtomSpectraRestNotReset, Resources.AtomSpectraRestDeclined), "", MessageBoxIcon.Information);
                return;
            }
            bool ok = AtomSpectraRestUi.RunWithWait(delegate { return rest.ClearNow(CommandTimeoutMs); });
            if (!ok)
            {
                AppUi.Report(string.Format(CultureInfo.InvariantCulture, Resources.MSGAtomSpectraRestNotReset, rest.LastErrorText ?? Resources.AtomSpectraRestErrNoAnswer), "", MessageBoxIcon.Warning);
                return;
            }
            r.StartTime = DateTime.Now;
        }

        private void RestBoardReset(object sender, string why)
        {
            AtomSpectraRestSession rest = sender as AtomSpectraRestSession;
            string text = rest != null ? rest.Guard.TripText : null;   // `why` is the log's English reason
            StopByBoard(string.Format(CultureInfo.InvariantCulture, Resources.MSGAtomSpectraRestBoardReset, text ?? "?"), rest != null ? rest.TripSample : null);
        }

        private void RestBoardStalled(object sender, EventArgs e)
        {
            StopByBoard(Resources.MSGAtomSpectraRestBoardStalled);
        }

        // Stop of an acquisition joined, not started here: no -sto went out, and the user is told that the board counts on
        private void RestBoardKeepsCounting(object sender, EventArgs e)
        {
            AtomSpectraRestSession rest = sender as AtomSpectraRestSession;
            if (MainForm.ApplicationClosing)
            {
                AtomSpectraTcpLog.Info("Session", "the program closes: the joined acquisition goes on counting on the board, no message shown");
                return;
            }
            ReportLater(string.Format(CultureInfo.InvariantCulture, Resources.MSGAtomSpectraRestStopJoined, rest != null ? rest.Endpoint : "?"),
                "Stop: the joined acquisition goes on counting on the board");
        }

        // board: the board's new acquisition after a reset from outside (null: a stall); one with a spectrum is offered to the user,
        // one without (404) gets the advice of a new document
        private void StopByBoard(string message, AtomSpectraRestBoardSample board = null)
        {
            ResultData data = resultData;
            AtomSpectraRestSession rest = session as AtomSpectraRestSession;
            if (rest != null) AtomSpectraTcpLog.SetStatus("REST", rest.Endpoint, Resources.AtomSpectraRestStatusStoppedByBoard);
            if (MainForm.ApplicationClosing)
            {
                // the program is closing: no window (the session has written the reason to the log)
                if (data != null) data.ResultDataStatus.Recording = false;
                AtomSpectraTcpLog.Warn("Session", "the recording is stopped by the board state while the program closes: no message shown");
                return;
            }
            if (MainForm.originalContext != null)
            {
                MainForm.originalContext.Post(delegate
                {
                    bool recording = data != null && data.ResultDataStatus.Recording;   // a recording the user stopped first is not offered a replacement
                    if (recording && data.MeasurementController != null) data.MeasurementController.NotifyMeasurementStoppedByDevice();
                    if (MainForm.ApplicationClosing) return;   // the program began to close after the post
                    // an empty board (404 or 0 s / 0 counts) gets the advice of a new document, not the window
                    if (board != null) ScheduleBoardOffer(message, "board reset from outside", data, board.HasData ? board.Time : 0, board.HasData ? board.Total : 0, recording);   // not over a wait window
                    else AtomSpectraRestUi.AfterWait(() => AppUi.Report(message, "", MessageBoxIcon.Warning));
                }, null);
            }
            else
            {
                if (data != null) data.ResultDataStatus.Recording = false;
                Trace.WriteLine(message);
            }
        }
    }
}
