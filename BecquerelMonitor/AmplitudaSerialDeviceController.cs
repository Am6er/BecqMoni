using BecquerelMonitor.Properties;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    // Measurement life cycle for a detector block on the "Progress" RS-232 daisy chain.
    //
    // The block accumulates the histogram itself, but the DOCUMENT is the master: on Start the
    // block is cleared and the document becomes "what it had + what the block has counted
    // since" (AmplitudaSerialSession). That survives a block losing its memory, continues any
    // saved spectrum and lifts the block's 16-bit limits. All of that logic lives in
    // AmplitudaSerialAcquisition; this class only connects it to BecqMoni: a UI-thread timer
    // pumps the acquisition and copies its state into the document.
    //
    // The port belongs to a shared AmplitudaSerialLine, not to the controller: several blocks
    // on one port measure at the same time in different documents.
    //
    // Live time comes from the block (IDeviceLiveTimeSource), real time from the PC clock.
    //
    // No method lets an exception escape: MeasurementController reports any exception from
    // StartMeasurement as "Bluetooth is not supported". The one exception is a refused Start
    // WITHOUT windows (S100 remainder, as AmplitudaUsbDeviceController since A316): there
    // StartMeasurement throws on purpose - see RefuseStart.
    public class AmplitudaSerialDeviceController : DeviceController, IDeviceLiveTimeSource, IDisposable
    {
        const int PumpIntervalMs = 200;

        // Settle guard after spectrum protection (a detected block reset): keyed by port+address,
        // not by device config, because the guard protects the PHYSICAL block - several device
        // configs could in principle name the same port+address, and a settle time recorded by
        // one must still be honoured for the other. Shared across every controller instance
        // (static), like the AmplitudaSerialLine registry it parallels.
        static readonly object lastResetLock = new object();
        static readonly Dictionary<string, DateTime> lastReset = new Dictionary<string, DateTime>(StringComparer.Ordinal);

        // Measurement lease per PHYSICAL block (port + address, same key as the settle guard).
        // MeasurementController leases a device per config GUID, so two configs naming the same
        // block could otherwise measure it at once - and every start / pour of one would clear
        // the block under the other. Taken in StartCore, released in ReleaseAll.
        static readonly object activeBlocksLock = new object();
        static readonly Dictionary<string, AmplitudaSerialDeviceController> activeBlocks =
            new Dictionary<string, AmplitudaSerialDeviceController>(StringComparer.Ordinal);

        // Every message of a running measurement (warnings, failures, an incomplete Stop) goes
        // through this one gate, shared by all AmplitudaSerial documents: one box at a time,
        // shown later from the UI message loop, never inside the tick or the Stop that raised it
        // (A318, finding 3), and none at all while the application is closing (review of
        // 13.10.2026, item 4) - see AmplitudaSerialNotices. The refusals of StartMeasurement stay
        // direct: they answer the user's own click, and no box of ours can be open then.
        static readonly AmplitudaSerialNotices notices = new AmplitudaSerialNotices(PostToUi, ShowBox,
            () => MainForm.ApplicationClosing);

        readonly System.Windows.Forms.Timer pumpTimer = new System.Windows.Forms.Timer();
        AmplitudaSerialLine line;
        AmplitudaSerialAcquisition acquisition;
        ResultData resultData;
        string portName = "";
        int address;
        int channels;
        int gapMs;
        string leaseKey;
        bool inTick;
        bool disposed;

        public AmplitudaSerialDeviceController()
        {
            pumpTimer.Interval = PumpIntervalMs;
            pumpTimer.Tick += PumpTimer_Tick;
        }

        // The UI message loop, as the other devices post to it (MainForm.originalContext). With
        // no loop (a headless run, or the main form already gone) the message is shown at once:
        // AppUi.Report then prints a line or raises the box on this thread, as it always did.
        static void PostToUi(Action action)
        {
            SynchronizationContext ui = MainForm.originalContext;
            if (ui != null)
            {
                try
                {
                    ui.Post(delegate { action(); }, null);
                    return;
                }
                catch (Exception)
                {
                    // The loop is gone (the window closed between the check and the post).
                }
            }
            action();
        }

        static void ShowBox(string text)
        {
            AppUi.Report(text, "", MessageBoxIcon.None);
        }

        static string SettleKey(string portName, int address)
        {
            return portName.ToUpperInvariant() + ":" + address.ToString(CultureInfo.InvariantCulture);
        }

        // {1} of ERRAmplitudaSerialSettingsDiffer is the pause of the line already open: the
        // config form's Test / Find blocks with an unsaved pause change meet the line of a
        // measurement running with the SAME configuration, so the text must not say whose line it
        // is, only what it is set to (review of 13.10.2026, item 12).
        public static string DescribeAcquireError(string portName, string error)
        {
            if (error == AmplitudaSerialLine.ErrorSettingsDiffer)
            {
                int openGap = AmplitudaSerialLine.OpenGapMs(portName);     // 0: the line closed meanwhile
                return string.Format(CultureInfo.InvariantCulture, Resources.ERRAmplitudaSerialSettingsDiffer, portName,
                    openGap > 0 ? (object)openGap : "?");
            }
            return string.Format(CultureInfo.InvariantCulture, Resources.ERRAmplitudaSerialPort, portName, error);
        }

        public static string DescribeFailure(AmplitudaSerialFailure failure, string portName, int address,
            int configuredChannels, int suggestedChannels)
        {
            switch (failure)
            {
                case AmplitudaSerialFailure.NoAnswer:
                    return string.Format(CultureInfo.InvariantCulture, Resources.ERRAmplitudaSerialNoAnswer, portName, address);
                case AmplitudaSerialFailure.ClearFailed:
                    return string.Format(CultureInfo.InvariantCulture, Resources.ERRAmplitudaSerialClearFailed, portName, address);
                case AmplitudaSerialFailure.StartFailed:
                    return string.Format(CultureInfo.InvariantCulture, Resources.ERRAmplitudaSerialStartFailed, portName, address);
                case AmplitudaSerialFailure.ChannelsExceedBlock:
                    return string.Format(CultureInfo.InvariantCulture, Resources.ERRAmplitudaSerialChannelsExceedBlock, portName, address,
                        configuredChannels, suggestedChannels);
                case AmplitudaSerialFailure.BlockReset:
                    return Resources.MSGAmplitudaSerialBlockReset;
                default:
                    return string.Format(CultureInfo.InvariantCulture, Resources.ERRAmplitudaSerialLost, portName, address);
            }
        }

        static string DescribeWarning(AmplitudaSerialWarning warning)
        {
            switch (warning)
            {
                // The only warning path left for a reset (RequestStop's Halt continuation): the
                // measurement already finished normally there, so the same spectrum-protection
                // text that a failed measurement would show is reused verbatim.
                case AmplitudaSerialWarning.BlockReset:
                    return Resources.MSGAmplitudaSerialBlockReset;
                case AmplitudaSerialWarning.BlockCeiling:
                    return Resources.MSGAmplitudaSerialBlockCeiling;
                case AmplitudaSerialWarning.BlockWontStop:
                    return Resources.MSGAmplitudaSerialBlockWontStop;
                default:
                    return Resources.MSGAmplitudaSerialBlockStopped;
            }
        }

        public override bool StartMeasurement(ResultData resultData)
        {
            try
            {
                return StartCore(resultData);
            }
            catch (Exception ex)
            {
                ReleaseAll();
                if (!AppUi.HasWindows)
                {
                    // RefuseStart's own throw, or any other failure: either way the measurement did
                    // not start, and MeasurementController.StartRecording passes it on.
                    throw;
                }
                AppUi.Report(string.Format(CultureInfo.InvariantCulture, Resources.ERRAmplitudaSerialError, ex.Message), "", MessageBoxIcon.None);
                return false;
            }
        }

        // A refused Start. With windows: the message, as before (direct, not through `notices`:
        // it answers the user's own click). Without windows: a throw (S100 remainder, as
        // AmplitudaUsbDeviceController.RefuseStart; see MeasurementController.StartRecording) -
        // false has no reader there, and a headless run would go on to present an empty spectrum
        // as a measurement.
        static bool RefuseStart(string message)
        {
            if (!AppUi.HasWindows)
            {
                throw new InvalidOperationException(
                    "BecqMoni: the Amplituda Serial measurement cannot start: " + message
                    + " A headless run must not continue: the spectrum would stay empty.");
            }
            AppUi.Report(message, "", MessageBoxIcon.None);
            return false;
        }

        bool StartCore(ResultData resultData)
        {
            // Prefer the manager's copy: editing a config saves a clone there, while open
            // documents keep their old instance (RadiaCodeDeviceController does the same).
            AmplitudaSerialDeviceConfig config = null;
            DeviceConfigInfo managed;
            if (resultData.DeviceConfig.Guid != null &&
                DeviceConfigManager.GetInstance().DeviceConfigMap.TryGetValue(resultData.DeviceConfig.Guid, out managed))
            {
                config = managed.InputDeviceConfig as AmplitudaSerialDeviceConfig;
            }
            if (config == null)
            {
                config = resultData.DeviceConfig.InputDeviceConfig as AmplitudaSerialDeviceConfig;
            }
            if (config == null)
            {
                return RefuseStart(Resources.ERRInvalidDeviceType);
            }
            if (string.IsNullOrEmpty(config.PortName))
            {
                return RefuseStart(Resources.ERRAmplitudaSerialNoPort);
            }

            // Spectrum protection settle guard: refuse Start for a while after a detected block
            // reset, so a measurement cannot start straight into the block's own ~1-minute
            // high-voltage re-tuning (see AmplitudaSerialAcquisition's class comment). Before
            // acquiring the line, on purpose: no point opening the port just to refuse anyway.
            if (config.HvSettleSeconds > 0)
            {
                DateTime resetAtUtc;
                bool known;
                lock (lastResetLock)
                {
                    known = lastReset.TryGetValue(SettleKey(config.PortName, config.Address), out resetAtUtc);
                }
                if (known)
                {
                    double elapsed = (DateTime.UtcNow - resetAtUtc).TotalSeconds;
                    if (elapsed < 0)
                    {
                        // The system clock moved backwards (DST, a manual change, an NTP
                        // correction): treat the guard as expired rather than refuse Start on a
                        // bogus multi-year "elapsed".
                        elapsed = config.HvSettleSeconds;
                    }
                    if (elapsed < config.HvSettleSeconds)
                    {
                        return RefuseStart(string.Format(CultureInfo.InvariantCulture, Resources.ERRAmplitudaSerialSettling, (int)elapsed, config.HvSettleSeconds,
                            (int)Math.Ceiling(config.HvSettleSeconds - elapsed)));
                    }
                }
            }

            // Check the DOCUMENT's geometry, not the config's: after a config switch on a
            // non-empty spectrum BecqMoni keeps the old channel count.
            EnergySpectrum spectrum = resultData.EnergySpectrum;
            if (spectrum.Spectrum == null || spectrum.Spectrum.Length != spectrum.NumberOfChannels ||
                !AmplitudaSerialProtocol.IsValidChannelCount(spectrum.NumberOfChannels))
            {
                return RefuseStart(string.Format(CultureInfo.InvariantCulture, Resources.ERRAmplitudaSerialChannels, spectrum.NumberOfChannels));
            }

            ReleaseAll();       // also gives up this controller's own previous lease
            string key = SettleKey(config.PortName, config.Address);
            bool busy;
            lock (activeBlocksLock)
            {
                AmplitudaSerialDeviceController holder;
                busy = activeBlocks.TryGetValue(key, out holder) && holder != this;
                if (!busy)
                {
                    activeBlocks[key] = this;
                    leaseKey = key;
                }
            }
            if (busy)
            {
                // Reported outside the lock: the box is modal and other ticks keep running.
                return RefuseStart(string.Format(CultureInfo.InvariantCulture, Resources.ERRAmplitudaSerialBlockBusy,
                    config.PortName, config.Address));
            }
            string error;
            AmplitudaSerialLine acquired = AmplitudaSerialLine.Acquire(config.ToSettings(), out error);
            if (acquired == null)
            {
                ReleaseLease();
                return RefuseStart(DescribeAcquireError(config.PortName, error));
            }

            line = acquired;
            this.resultData = resultData;
            portName = config.PortName;
            address = config.Address;
            channels = spectrum.NumberOfChannels;
            gapMs = config.GapMilliseconds;     // the line's own: Acquire refuses a port open with another gap
            spectrum.ChannelPitch = 1;      // data arrives in channel units, as with the other digital MCAs

            // Continue a stopped or a loaded measurement: the document is the base.
            ResultDataStatus status = resultData.ResultDataStatus;
            double baseReal = Math.Max(status.TotalTime.TotalSeconds, spectrum.MeasurementTime);
            double baseLive = spectrum.LiveTime;
            if (spectrum.TotalPulseCount == 0 && baseReal == 0)
            {
                resultData.StartTime = DateTime.Now;
            }

            acquisition = new AmplitudaSerialAcquisition(line, (byte)address, channels, config.PollSeconds * 1000,
                spectrum.Spectrum, baseLive, baseReal);
            acquisition.Start();
            pumpTimer.Start();
            status.Recording = true;
            return true;
        }

        void PumpTimer_Tick(object sender, EventArgs e)
        {
            // A modal box anywhere on the UI thread (ours or any other) runs a loop in which this
            // timer keeps ticking; inTick guards against re-entry from code below that may pump.
            if (inTick || acquisition == null || resultData == null)
            {
                return;
            }
            inTick = true;
            // Declared above the try and handed over in the finally so that every exit path -
            // the early return on a channel mismatch and the catch alike - still reports what
            // this tick took out of the acquisition (N1). No box is shown from here: the
            // warnings go to `notices`, which shows them later from the UI message loop, one box
            // at a time (A318, finding 3). While that box is open this timer keeps ticking with
            // inTick clear, so Pump() runs on - a restart after a pour or a ceiling
            // (clear -> read -> start) moves only through Pump() - and a warning repeated
            // meanwhile only joins the queue instead of opening a box inside the box (I1).
            List<AmplitudaSerialWarning> warnings = null;
            try
            {
                if (!resultData.ResultDataStatus.Recording)
                {
                    return;
                }
                acquisition.Pump();
                warnings = acquisition.TakeWarnings();
                if (!Publish(false))
                {
                    Fail(string.Format(CultureInfo.InvariantCulture, Resources.ERRAmplitudaSerialChannels, resultData.EnergySpectrum.NumberOfChannels));
                    return;
                }
                if (acquisition == null)     // a nested Stop ran underneath (defensive)
                {
                    return;
                }
                if (acquisition.Finished && acquisition.Failure != AmplitudaSerialFailure.None)
                {
                    Fail(DescribeFailure(acquisition.Failure, portName, address, channels, acquisition.SuggestedChannels));
                }
            }
            catch (Exception ex)
            {
                Fail(string.Format(CultureInfo.InvariantCulture, Resources.ERRAmplitudaSerialError, ex.Message));
            }
            finally
            {
                inTick = false;
                if (warnings != null)
                {
                    foreach (AmplitudaSerialWarning warning in warnings)
                    {
                        notices.Add(DescribeWarning(warning));
                    }
                }
            }
        }

        // Copies the acquisition state into the document. False = the spectrum geometry is no
        // longer usable (the document was rebuilt with another channel count).
        bool Publish(bool force)
        {
            EnergySpectrum spectrum = resultData.EnergySpectrum;    // re-fetched: Clear replaces the array
            if (spectrum.Spectrum == null || spectrum.Spectrum.Length != channels)
            {
                return false;
            }
            if (acquisition.TakeDirty() || force)
            {
                acquisition.Compose(spectrum.Spectrum);
                long counts = acquisition.TotalCounts;
                spectrum.TotalPulseCount = counts;
                spectrum.ValidPulseCount = counts;
                spectrum.LiveTime = acquisition.LiveSeconds;
            }
            // Through ticks (100 ns): TimeSpan.FromSeconds rounds to a whole millisecond.
            TimeSpan elapsed = TimeSpan.FromTicks((long)(acquisition.RealSeconds * 1.0E+07));
            resultData.ResultDataStatus.ElapsedTime = elapsed;
            resultData.ResultDataStatus.TotalTime = elapsed;
            return true;
        }

        // Ends the session: final values, Recording = false, line released.
        void FinishSession()
        {
            pumpTimer.Stop();
            if (resultData == null || acquisition == null)
            {
                ReleaseAll();
                return;
            }
            Publish(true);
            // Warnings are taken before ReleaseAll() (acquisition is about to become null) and
            // handed to `notices` only after ReleaseAll() and after Recording is cleared; the box
            // itself comes later still, from the UI message loop (I1, A318).
            List<AmplitudaSerialWarning> warnings = acquisition.TakeWarnings();
            EnergySpectrum spectrum = resultData.EnergySpectrum;
            // MeasurementController.OnTimer no longer ticks for a stopped spectrum: write the final values here.
            spectrum.MeasurementTime = acquisition.RealSeconds;
            spectrum.LiveTime = acquisition.LiveSeconds;
            resultData.EndTime = DateTime.Now;
            resultData.ResultDataStatus.Recording = false;
            if (acquisition.BlockResetDetected)
            {
                // Arms the settle guard checked in StartCore, whether this session ended via the
                // Fail path (a reset noticed while measuring) or a normal Stop that happened to
                // see one (RequestStop's Halt continuation) - either way the block just lost its
                // memory and needs its high voltage re-tuning time before a new Start is safe.
                lock (lastResetLock)
                {
                    lastReset[SettleKey(portName, address)] = DateTime.UtcNow;
                }
            }
            ReleaseAll();
            foreach (AmplitudaSerialWarning warning in warnings)
            {
                notices.Add(DescribeWarning(warning));
            }
        }

        void Fail(string message)
        {
            ResultData failed = resultData;
            try
            {
                if (acquisition != null && !acquisition.Finished)
                {
                    acquisition.Abort();
                }
                FinishSession();
            }
            catch (Exception)
            {
                failed.ResultDataStatus.Recording = false;
                ReleaseAll();
            }
            if (failed.MeasurementController != null)
            {
                failed.MeasurementController.NotifyMeasurementStoppedByDevice();
            }
            notices.Add(message);
        }

        // Synchronous on purpose: the core may save the document right after StopRecording,
        // so the last reading of the stopped block has to be in the document when this returns.
        public override void StopMeasurement(ResultData resultData)
        {
            try
            {
                if (this.resultData == null || acquisition == null)
                {
                    resultData.ResultDataStatus.Recording = false;
                    return;
                }
                pumpTimer.Stop();
                acquisition.RequestStop();
                // The wait scales with the channel count (a Halt read is status + header + N
                // pages, I3) AND with the gap of the line (every exchange is four gaps plus the
                // block), and it counts TWO steps of another document's read job, not one: one in
                // flight when the stop command is queued, one between that command and the urgent
                // read. Only the Stop's own halt read is Urgent (a pour's halt rides with the
                // normal polls), so a foreign job is parked after its current STEP, never finished
                // first - the budget is thus independent of the other blocks' geometry and of how
                // many share the line; Stops are serialised on the UI thread, so one urgent read at
                // a time. The old budget (2000 + 1500 per page, 8 s floor) counted one foreign step
                // at the default gap and cut a healthy Stop at a long one (A318, finding 2). The
                // terms and the numbers are at AmplitudaSerialTiming.StopBudgetMs; the console
                // tests measure a Stop against it.
                int stopWaitMs = AmplitudaSerialTiming.StopBudgetMs(channels, gapMs);
                Stopwatch wait = Stopwatch.StartNew();
                while (!acquisition.Finished && wait.ElapsedMilliseconds < stopWaitMs)
                {
                    acquisition.Pump();
                    Thread.Sleep(20);
                }
                // The final reading was NOT taken if the budget expired (Abort) or the stop
                // sequence gave up on a silent block (ConnectionLost): tell the user (review I-3).
                bool incomplete = !acquisition.Finished;
                if (!acquisition.Finished)
                {
                    acquisition.Abort();
                }
                // Captured before FinishSession() nulls `acquisition` (M2, add-ons review):
                // RequestStop's own Halt continuation only ever raises a WARNING for a reset
                // (Failure stays None there - see its comment), which FinishSession's own
                // warnings loop already reports below; this covers a failure value reaching here
                // by any other route instead of being silently swallowed just because the normal
                // per-tick "Finished && Failure != None" reporting in PumpTimer_Tick no longer
                // runs once pumpTimer.Stop() has been called above. Without this, the settle
                // guard could still get armed (FinishSession arms it from BlockResetDetected
                // regardless of Failure) while the user is told nothing - then the next Start
                // refuses "out of nowhere".
                AmplitudaSerialFailure finishedFailure = acquisition.Failure;
                FinishSession();
                // Through `notices`, not a modal box here: the core saves the document right
                // after this returns (a Stop by preset time), and that save must not wait until
                // somebody closes the box (A318, finding 3).
                if (finishedFailure == AmplitudaSerialFailure.BlockReset)
                {
                    notices.Add(DescribeFailure(finishedFailure, portName, address, channels, 0));
                }
                else if (incomplete || finishedFailure == AmplitudaSerialFailure.ConnectionLost)
                {
                    notices.Add(string.Format(CultureInfo.InvariantCulture, Resources.ERRAmplitudaSerialStopIncomplete,
                        portName, address));
                }
            }
            catch (Exception)
            {
                // Recording must end up false: otherwise OnTimer calls StopRecording on every tick.
                resultData.ResultDataStatus.Recording = false;
                ReleaseAll();
            }
        }

        // Called before the panel zeroes the times and re-allocates the spectrum; allowed while recording.
        public override void ClearMeasurementResult(ResultData resultData)
        {
            try
            {
                if (acquisition != null && !acquisition.Finished)
                {
                    acquisition.ClearAll();
                    resultData.StartTime = DateTime.Now;
                }
            }
            catch (Exception)
            {
            }
        }

        // The document is the master: a histogram found in the block belongs to nobody.
        public override bool AttachToDevice(ResultData resultData)
        {
            return false;
        }

        public override void DetachFromDevice(ResultData resultData)
        {
        }

        void ReleaseLease()
        {
            if (leaseKey == null)
            {
                return;
            }
            lock (activeBlocksLock)
            {
                AmplitudaSerialDeviceController holder;
                if (activeBlocks.TryGetValue(leaseKey, out holder) && holder == this)
                {
                    activeBlocks.Remove(leaseKey);
                }
            }
            leaseKey = null;
        }

        void ReleaseAll()
        {
            acquisition = null;
            ReleaseLease();
            if (line != null)
            {
                line.Release();     // sends a pending stop command before the port closes
                line = null;
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            pumpTimer.Stop();
            pumpTimer.Dispose();
            try
            {
                if (acquisition != null && !acquisition.Finished)
                {
                    acquisition.Abort();
                }
            }
            catch (Exception)
            {
            }
            ReleaseAll();
        }
    }
}
