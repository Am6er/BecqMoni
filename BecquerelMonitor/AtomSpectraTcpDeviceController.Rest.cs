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
            if (session != null) return;
            string error;
            AtomSpectraRestClient client = config.CreateRestClient(out error);
            restOpenError = error;
            if (client == null) return;
            AtomSpectraRestSession rest = new AtomSpectraRestSession(client, config.RestPollSeconds, config.RestObserveOnly, config.RestLimitSeconds);
            rest.DataReady += Acquisition_DataReady;
            rest.LinkLost += Session_LinkLost;
            rest.BoardReset += RestBoardReset;
            rest.BoardStalled += RestBoardStalled;
            session = rest;
            restKey = key;
            AtomSpectraRestClient.Current = client;
            AtomSpectraTcpLog.DataAgeSeconds = delegate
            {
                AtomSpectraRestAcquisition a = rest.Acquisition;
                return a == null ? -1 : a.DataAgeSeconds;
            };
            AtomSpectraTcpLog.StaleAfterSeconds = 2 * config.RestPollSeconds + 5;
            AtomSpectraTcpLog.SetStatus("REST", AtomSpectraTcpLog.Mask(client.BaseUrl), rest.ObserveOnly ? "idle (observe only)" : "idle");
        }

        private void UnhookRest()
        {
            AtomSpectraRestSession rest = session as AtomSpectraRestSession;
            if (rest != null)
            {
                rest.BoardReset -= RestBoardReset;
                rest.BoardStalled -= RestBoardStalled;
            }
        }

        private bool BeginRest(ResultData r, AtomSpectraTcpDeviceConfig config)
        {
            if (string.IsNullOrWhiteSpace(config.RestUrl) && string.IsNullOrWhiteSpace(config.Host))
            {
                return Refuse(Resources.ERRAtomSpectraTcpNoHost);   // an empty address is saved; it is refused here, at Start
            }
            string refusal = null;
            try
            {
                OpenRestSession(config);
                AtomSpectraRestSession rest = session as AtomSpectraRestSession;
                if (rest == null)
                {
                    refusal = string.Format(CultureInfo.InvariantCulture, Resources.ERRAtomSpectraRestBadUrl, restOpenError ?? "?");
                }
                else
                {
                    linkLossReported = false;
                    rest.DocumentSeconds = (long)r.ResultDataStatus.ElapsedTime.TotalSeconds;
                    rest.DocumentCounts = r.EnergySpectrum != null ? r.EnergySpectrum.ValidPulseCount : 0;
                    // asked on the UI thread; a headless run never asks (joins or refuses)
                    rest.Ask = AppUi.HasWindows ? new Func<AtomSpectraRestStartQuestion, AtomSpectraRestStartChoice>(AtomSpectraRestUi.AskStart) : null;
                    // ?channels=N of the profile at Start (the same number restChannels keeps below)
                    rest.RequestChannels = r.DeviceConfig != null ? r.DeviceConfig.NumberOfChannels : 0;
                    bool newDoc = newDocumentCreated;
                    AtomSpectraTcpBeginOutcome outcome = AtomSpectraRestUi.RunWithWait(delegate { return rest.Begin(newDoc, CommandTimeoutMs); });
                    if (outcome == AtomSpectraTcpBeginOutcome.Started)
                    {
                        restChannels = r.DeviceConfig != null ? r.DeviceConfig.NumberOfChannels : 0;   // fixed at Start
                        long boardSeconds = (long)rest.ElapsedAtBeginSeconds;
                        r.StartTime = DateTime.Now - TimeSpan.FromSeconds(boardSeconds);   // the document starts when the board started
                        newDocumentCreated = false;
                        int before = r.ResultDataStatus.PresetTime;
                        // the preset must not stop the board before its own limit (MeasurementController.cs:749)
                        r.ResultDataStatus.PresetTime = rest.PresetAfterStart(before);
                        AtomSpectraTcpLog.Info("Session", "preset " + before + " s -> " + r.ResultDataStatus.PresetTime + " s (board time " + boardSeconds + " s)");
                        AtomSpectraTcpLog.SetStatus("REST", rest.Host + ":" + rest.Port, rest.ObserveOnly ? "observing" : (rest.JoinedRunning ? "joined a running acquisition" : "measuring"));
                        r.ResultDataStatus.Recording = true;
                        boardCalibrationPending = true;   // the first snapshot's "calib" -> document and configuration
                        RefreshDeadTimeFromDevice(r);
                        return true;
                    }
                    r.ResultDataStatus.Recording = false;
                    AtomSpectraTcpLog.Warn("Session", "Start not done: " + outcome + " " + rest.Failure + " " + (rest.LastError ?? ""));
                    AtomSpectraTcpLog.SetStatus("REST", rest.Host + ":" + rest.Port, "not started: " + outcome);
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
                    return string.Format(CultureInfo.InvariantCulture, Resources.ERRAtomSpectraRestRestartPartial, rest.LastError ?? "");
                case AtomSpectraRestFailure.HeadlessBehind:
                    return Resources.ERRAtomSpectraRestHeadlessBehind;
                case AtomSpectraRestFailure.HeadlessAsk:   // a stopped board with data and no window to ask in
                    return AtomSpectraRestSession.HeadlessAskText;
                case AtomSpectraRestFailure.DocumentNotEmpty:
                    return Resources.ERRAtomSpectraRestNeedNewDocument;
                default:
                    return string.Format(CultureInfo.InvariantCulture, Resources.ERRAtomSpectraRestStart, rest.LastError ?? outcome.ToString());
            }
        }

        private void ClearRest(ResultData r, AtomSpectraTcpDeviceConfig config)
        {
            if (!AppUi.HasWindows) throw new InvalidOperationException("BecqMoni: AtomSpectra REST Clear needs a confirmation window; the board was not reset.");
            OpenRestSession(config);
            AtomSpectraRestSession rest = session as AtomSpectraRestSession;
            if (rest == null)
            {
                AppUi.Report(string.Format(CultureInfo.InvariantCulture, Resources.ERRAtomSpectraRestBadUrl, restOpenError ?? "?"), "", MessageBoxIcon.Warning);
                return;
            }
            if (rest.ObserveOnly)
            {
                rest.ClearNow(CommandTimeoutMs);
                AppUi.Report(Resources.MSGAtomSpectraRestObserveOnly, "", MessageBoxIcon.Information);
                return;
            }
            string error = null;
            AtomSpectraRestBoardSample board = AtomSpectraRestUi.RunWithWait(delegate
            {
                string e;
                AtomSpectraRestBoardSample s = rest.ReadBoard(AtomSpectraRestSession.ProbeTimeoutMs, out e);
                error = e;
                return s;
            });
            if (board == null)
            {
                AppUi.Report(string.Format(CultureInfo.InvariantCulture, Resources.MSGAtomSpectraRestNotReset, error ?? "?"), "", MessageBoxIcon.Warning);
                return;
            }
            if (!AtomSpectraRestUi.ConfirmClear(board))
            {
                AtomSpectraTcpLog.Info("Session", "Clear: declined, board not reset");
                AppUi.Report(string.Format(CultureInfo.InvariantCulture, Resources.MSGAtomSpectraRestNotReset, Resources.AtomSpectraRestDeclined), "", MessageBoxIcon.Information);
                return;
            }
            bool ok = AtomSpectraRestUi.RunWithWait(delegate { return rest.ClearNow(CommandTimeoutMs); });
            if (!ok)
            {
                AppUi.Report(string.Format(CultureInfo.InvariantCulture, Resources.MSGAtomSpectraRestNotReset, rest.LastError ?? "?"), "", MessageBoxIcon.Warning);
                return;
            }
            r.StartTime = DateTime.Now;
        }

        private void RestBoardReset(object sender, string why)
        {
            StopByBoard(string.Format(CultureInfo.InvariantCulture, Resources.MSGAtomSpectraRestBoardReset, why));
        }

        private void RestBoardStalled(object sender, EventArgs e)
        {
            StopByBoard(Resources.MSGAtomSpectraRestBoardStalled);
        }

        private void StopByBoard(string message)
        {
            ResultData data = resultData;
            AtomSpectraRestSession rest = session as AtomSpectraRestSession;
            if (rest != null) AtomSpectraTcpLog.SetStatus("REST", rest.Host + ":" + rest.Port, "stopped by the board state");
            if (MainForm.originalContext != null)
            {
                MainForm.originalContext.Post(delegate
                {
                    if (data != null && data.ResultDataStatus.Recording && data.MeasurementController != null) data.MeasurementController.NotifyMeasurementStoppedByDevice();
                    AppUi.Report(message, "", MessageBoxIcon.Warning);
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
