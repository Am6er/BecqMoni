// Measurement life cycle of an Atom Spectra analyzer reached over TCP (Wi-Fi bridge board,
// port 8234). Same commands and the same histogram path as AtomSpectraDeviceController
// (COM port); the transport and the protocol loop live in AtomSpectraTcpSession.
//
// Differences from the COM type, on purpose:
// - the connection is opened by Start and closed by Stop/Detach/Dispose: the board
//   serves one client, so an idle BecqMoni must not hold its slot;
// - a dropped link does not stop the measurement: the session reconnects and re-sends -sta
//   (never -rst);
// - Stop with the link down tries one immediate reconnect to send -sto and otherwise leaves
//   a deferred -sto and tells the user;
// - the loss of the link is announced once per measurement.
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
using System.Windows.Forms;

namespace BecquerelMonitor
{
    public partial class AtomSpectraTcpDeviceController : DeviceController, IDeviceLiveTimeSource, IDisposable
    {
        const int CommandTimeoutMs = 1000;
        const int InfoTimeoutMs = 2000;
        const int KeepAliveMs = 1000;

        private IAtomSpectraSession session;
        private bool linkLossReported;
        private ResultData resultData;
        private string deviceGuid;
        private bool newDocumentCreated = true;
        private bool disposed;

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
            try
            {
                IAtomSpectraSession s0 = session;
                // REST Stop behind the wait window, the UI thread does not block on the network
                AtomSpectraTcpStopOutcome outcome = s0 == null ? AtomSpectraTcpStopOutcome.NotRunning
                    : s0 is AtomSpectraRestSession ? AtomSpectraRestUi.RunWithWait(delegate { return s0.Stop(CommandTimeoutMs); })
                    : s0.Stop(CommandTimeoutMs);

                if (deviceGuid != null)
                {
                    r.EndTime = DateTime.Now;
                }

                if (session != null)
                {
                    AtomSpectraTcpLog.Info("Session", "Stop: " + outcome);
                    AtomSpectraTcpLog.SetStatus(session.TransportName, session.Host + ":" + session.Port, "stopped (" + outcome + ")");
                }
                if (outcome == AtomSpectraTcpStopOutcome.Deferred)
                {
                    ReportLater(string.Format(CultureInfo.InvariantCulture, Resources.ERRAtomSpectraTcpStopDeferred, session.Host, session.Port));
                }
                else if (outcome == AtomSpectraTcpStopOutcome.Unconfirmed && session != null)
                {
                    ReportLater(string.Format(CultureInfo.InvariantCulture, Resources.ERRAtomSpectraRestStopUnconfirmed, session.Host, session.Port));
                }
            }
            finally
            {
                r.ResultDataStatus.Recording = false;
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

            r.StartTime = DateTime.Now;
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
            if (session != null && (session.Host != config.Host || session.Port != port || session.TransportName != "TCP"))
            {
                CloseSession();
            }

            if (session == null)
            {
                session = new AtomSpectraTcpSession(config.Host, port, AtomSpectraTcpSession.DefaultReconnectDelayMs, KeepAliveMs, null);
                AtomSpectraTcpLog.SetStatus(session.TransportName, config.Host + ":" + port, "idle");
                session.DataReady += Acquisition_DataReady;
                session.LinkLost += Session_LinkLost;
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
            UnhookRest();
            try
            {
                session.Dispose();
            }
            catch (Exception ex)
            {
                Trace.WriteLine("AtomSpectraTcp: error disposing session: " + ex.Message);
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

            OpenSession(config);
            linkLossReported = false;

            // a Start on a board with data asks "Continue / Start over / Cancel" (the REST dialog)
            AtomSpectraTcpSession tcp = session as AtomSpectraTcpSession;
            if (tcp != null) tcp.Ask = AppUi.HasWindows ? new Func<AtomSpectraRestStartQuestion, AtomSpectraRestStartChoice>(q => { q.Note = Resources.AtomSpectraTcpStartNote; return AtomSpectraRestUi.AskStart(q); }) : null;
            bool newDoc = start && newDocumentCreated;
            AtomSpectraTcpBeginOutcome outcome = AtomSpectraRestUi.RunWithWait(delegate { return session.Begin(newDoc, CommandTimeoutMs); });
            if (outcome == AtomSpectraTcpBeginOutcome.Cancelled)
            {
                r.ResultDataStatus.Recording = false;
                AtomSpectraTcpLog.SetStatus(session.TransportName, session.Host + ":" + session.Port, "not started: cancelled");
                return false;
            }

            if (outcome == AtomSpectraTcpBeginOutcome.Started)
            {
                if (newDocumentCreated)
                {
                    // Joining or resuming the board's acquisition: the document starts when the board started.
                    r.StartTime = DateTime.Now - TimeSpan.FromSeconds(session.ElapsedAtBeginSeconds);
                    newDocumentCreated = false;
                }
                if (session.ElapsedAtBeginSeconds > 0)
                {
                    // the preset counts the board's time: it must not stop the run at the first tick (as REST, MeasurementController.cs:749)
                    int before = r.ResultDataStatus.PresetTime;
                    r.ResultDataStatus.PresetTime = AtomSpectraRestPolicy.PresetForContinue(before, (long)session.ElapsedAtBeginSeconds, 0);
                    AtomSpectraTcpLog.Info("Session", "preset " + before + " s -> " + r.ResultDataStatus.PresetTime + " s (board time " + session.ElapsedAtBeginSeconds + " s)");
                }
                AtomSpectraTcpLog.SetStatus(session.TransportName, session.Host + ":" + session.Port, session.JoinedRunning ? "joined a running acquisition" : "measuring");
                r.ResultDataStatus.Recording = true;
                RefreshDeadTimeFromDevice(r);
                RefreshCalibrationFromDevice(r);   // the board calibration -> document and configuration
                return true;
            }

            r.ResultDataStatus.Recording = false;
            AtomSpectraTcpLog.Warn("Session", "Start refused: " + outcome + " (" + session.TransportName + " " + session.Host + ":" + session.Port + ") " + (session.LastError ?? ""));
            AtomSpectraTcpLog.SetStatus(session.TransportName, session.Host + ":" + session.Port, "not started: " + outcome);

            switch (outcome)
            {
                case AtomSpectraTcpBeginOutcome.NoHost:
                    return Refuse(Resources.ERRAtomSpectraTcpNoHost);
                case AtomSpectraTcpBeginOutcome.HostBusy:
                    return Refuse(string.Format(CultureInfo.InvariantCulture, Resources.ERRAtomSpectraTcpHostBusy, config.Host, session.Port));
                case AtomSpectraTcpBeginOutcome.ConnectFailed:
                case AtomSpectraTcpBeginOutcome.LinkClosed:
                    return Refuse(string.Format(CultureInfo.InvariantCulture, Resources.ERRAtomSpectraTcpConnect, config.Host, session.Port, session.LastError ?? ""));
                case AtomSpectraTcpBeginOutcome.NoAnswer:
                    return Refuse(string.Format(CultureInfo.InvariantCulture, Resources.ERRAtomSpectraTcpNoAnswer, config.Host, session.Port));
                case AtomSpectraTcpBeginOutcome.Refused:   // no window to ask in, a stopped board with data
                    return Refuse(session.LastError ?? AtomSpectraRestSession.HeadlessAskText);
                default:
                    return Refuse(string.Format(CultureInfo.InvariantCulture, Resources.ERRAtomSpectraTcpNoAnswer, config.Host, session.Port));
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

        private void Acquisition_DataReady(object sender, AtomSpectraTcpDataReadyArgs e)
        {
            ResultData data = resultData;
            if (data == null)
            {
                return;
            }

            int channels;
            string guid = data.DeviceConfig != null ? data.DeviceConfig.Guid ?? "" : "";
            DeviceConfigInfo configInfo;
            if (DeviceConfigManager.GetInstance().DeviceConfigMap.TryGetValue(guid, out configInfo))
            {
                channels = configInfo.NumberOfChannels;
            }
            else
            {
                channels = data.DeviceConfig != null ? data.DeviceConfig.NumberOfChannels : 0;
            }

            AtomSpectraTcpDataReadyArgs args = BuildArgs(e, channels, restChannels);   // a static method, tested without a window
            IAtomSpectraSession s = session;
            if (s != null)
            {
                AtomSpectraTcpLog.SetStatus(s.TransportName, s.Host + ":" + s.Port, "receiving, time " + e.ElapsedTime + " s, cps " + e.Cps);
            }

            if (MainForm.originalContext != null)
            {
                MainForm.originalContext.Post(d => UpdateHistogram(args), null);
            }
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
            if (resultData == null || !resultData.ResultDataStatus.Recording)
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
            AtomSpectraRestDataReadyArgs restSnapshot = e as AtomSpectraRestDataReadyArgs;
            if (boardCalibrationPending && restSnapshot != null)
            {
                // the board calibration of the first snapshot after Start -> the document and the configuration
                boardCalibrationPending = false;
                // "calib" is for calib_channels; the document has e.Histogram.Length channels (board or client fold)
                double[] slots = AtomSpectraRestCalibration.ForChannels(restSnapshot.Calibration, restSnapshot.CalibChannels, e.Histogram.Length);
                AtomSpectraTcpLog.Info("Cal", ApplyBoardCalibration(spectrum, resultData.DeviceConfig, slots, configSaver));
            }

            Array.Copy(e.Histogram, spectrum.Spectrum, e.Histogram.Length);

            long sum = e.Histogram.Sum(x => (long)x);
            spectrum.TotalPulseCount = sum + e.InvalidPulses;
            spectrum.ValidPulseCount = sum;
            spectrum.ChannelPitch = 1;

            TimeSpan elapsed = TimeSpan.FromSeconds(e.ElapsedTime);
            resultData.ResultDataStatus.TotalTime = elapsed;
            resultData.ResultDataStatus.ElapsedTime = elapsed;

            AtomSpectraRestDataReadyArgs rest = e as AtomSpectraRestDataReadyArgs;
            bool boardLive = rest != null && !double.IsNaN(rest.LiveSeconds) && !double.IsInfinity(rest.LiveSeconds)
                && rest.LiveSeconds > 0 && rest.LiveSeconds <= elapsed.TotalSeconds + 1;
            if (resultData.MeasurementController != null)
            {
                MeasurementController.SetSpectrumTimes(spectrum, elapsed.TotalSeconds, resultData.DeviceConfig, boardLive);
                if (boardLive)
                {
                    // REST: live time measured by the board itself (GET /api/spectrum.json "live")
                    spectrum.LiveTime = Math.Min(rest.LiveSeconds, elapsed.TotalSeconds);
                }
            }
        }

        private void RefreshDeadTimeFromDevice(ResultData r)
        {
            if (session == null)
            {
                return;
            }

            // The DOCUMENT's config, as AtomSpectraDeviceController.RefreshDeadTimeFromDevice: live time
            // (SetSpectrumTimes) reads the dead time from resultData.DeviceConfig.
            AtomSpectraTcpDeviceConfig config = r.DeviceConfig == null ? null : r.DeviceConfig.InputDeviceConfig as AtomSpectraTcpDeviceConfig;
            if (config == null)
            {
                return;
            }

            try
            {
                string answer = session == null ? null : session.Query("-inf", InfoTimeoutMs);
                if (answer == null)
                {
                    return;
                }

                string error;
                double tau = AtomSpectraDeviceConfig.DeadTimeFromInfo(answer, out error);
                if (double.IsNaN(tau))
                {
                    Trace.WriteLine("AtomSpectraTcp: failed to parse dead time: " + error);
                    return;
                }

                if (config.DeadTimeValue != tau)
                {
                    Trace.WriteLine("AtomSpectraTcp: updating dead time from " + config.DeadTimeValue + " to " + tau);
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
            Trace.WriteLine("AtomSpectraTcp: link lost " + (session != null ? session.Host + ":" + session.Port : "unknown"));
            if (linkLossReported)
            {
                return;
            }
            linkLossReported = true;
            IAtomSpectraSession s = session;
            if (s == null)
            {
                return;
            }
            AtomSpectraTcpLog.SetStatus(s.TransportName, s.Host + ":" + s.Port, "link lost, retrying");
            // The status bar shows "port lost" only for the COM type (MainForm); until that is extended,
            // the loss is announced once per measurement.
            AtomSpectraRestSession rs = s as AtomSpectraRestSession;   // REST has its own text, not the TCP bridge one
            ReportLater(rs != null ? rs.LinkLostText()
                : string.Format(CultureInfo.InvariantCulture, Resources.MSGAtomSpectraTcpLinkLost, s.Host, s.Port));
        }

        private static void ReportLater(string text)
        {
            // Posted, not modal inside the caller (a preset-time Stop saves the document right after StopMeasurement).
            if (AppUi.HasWindows && MainForm.originalContext != null)
            {
                MainForm.originalContext.Post(_ => AppUi.Report(text, "", MessageBoxIcon.Warning), null);
            }
            else
            {
                Trace.WriteLine(text);
            }
        }
    }
}
