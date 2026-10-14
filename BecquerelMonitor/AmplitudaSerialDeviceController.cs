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
    // Live time comes from the block (IDeviceLiveTimeSource), real time from the PC clock - both,
    // while measuring, at the moment of the last reading's counts (P280, see Publish).
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

        // Warnings and end-of-session reports: one box at a time, posted to the message queue
        // (headless: shown at once, AppUi.Report then prints its line). The refusals in
        // StartCore stay direct AppUi.Report calls: the user just pressed Start and must see
        // the refusal before anything else happens.
        // Shared by ALL AmplitudaSerial documents (static; P288, kept from A318 when merging PR
        // #36): a box of one document runs a modal loop that also dispatches the posted show of
        // another, so a reporter per controller still let N documents with warnings stack N
        // boxes one inside another. And no box at all while the application is closing
        // (MainForm.ApplicationClosing; review of 13.10.2026, item 4): every message here is for
        // information only, and a modal box in the middle of the close holds it up, and a Windows
        // shutdown with it - the same rule as MainForm.SaveLayoutXml and
        // AmplitudaUsbDeviceController.ReportLoss.
        static readonly AmplitudaSerialReporter reporter = new AmplitudaSerialReporter(
            text => AppUi.Report(text, "", MessageBoxIcon.None),
            action =>
            {
                if (AppUi.HasWindows && MainForm.originalContext != null)
                {
                    MainForm.originalContext.Post(_ => action(), null);
                }
                else
                {
                    action();
                }
            },
            () => MainForm.ApplicationClosing);

        readonly System.Windows.Forms.Timer pumpTimer = new System.Windows.Forms.Timer();
        AmplitudaSerialLine line;
        AmplitudaSerialAcquisition acquisition;
        ResultData resultData;
        string portName = "";
        int address;
        int channels;
        string leaseKey;
        bool inTick;
        bool disposed;
        // P309 (15.10.2026): a session that brought no reading leaves the end of a continued
        // document as it was (see KeepsEndWithoutData). UI thread only, like the rest.
        DateTime endBeforeSession;
        bool keepEndWithoutData;
        bool sessionHadData;

        public AmplitudaSerialDeviceController()
        {
            pumpTimer.Interval = PumpIntervalMs;
            pumpTimer.Tick += PumpTimer_Tick;
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

        // A refused Start. With windows: the message, as before (direct, not through `reporter`:
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
            spectrum.ChannelPitch = 1;      // data arrives in channel units, as with the other digital MCAs

            // Continue a stopped or a loaded measurement: the document is the base.
            ResultDataStatus status = resultData.ResultDataStatus;
            double baseReal = Math.Max(status.TotalTime.TotalSeconds, spectrum.MeasurementTime);
            double baseLive = spectrum.LiveTime;
            if (spectrum.TotalPulseCount == 0 && baseReal == 0)
            {
                resultData.StartTime = DateTime.Now;
                keepEndWithoutData = false;
            }
            else if (resultData.StartTime == ResultData.UnknownStartTime)
            {
                ResumeUnknownStart(resultData, baseReal, DateTime.Now);     // P298, see there
                keepEndWithoutData = false;
            }
            else
            {
                keepEndWithoutData = true;      // P309, see KeepsEndWithoutData
            }
            endBeforeSession = resultData.EndTime;
            sessionHadData = false;

            acquisition = new AmplitudaSerialAcquisition(line, (byte)address, channels, config.PollSeconds * 1000,
                spectrum.Spectrum, baseLive, baseReal);
            acquisition.Start();
            pumpTimer.Start();
            status.Recording = true;
            return true;
        }

        // P298 (14.10.2026): Start on a document whose start time is UNKNOWN - the 1970 label of
        // A207 (ResultData.UnknownStartTime), which a file without a start, a sample time and an end
        // opens with (the RadiaCode-101 exports). The start becomes the moment of Start minus the
        // real time the document already holds. Before, it stayed 1970 while Stop wrote the PC clock
        // as the end, and the decay correction (MeasurementResultManager.DecayToSamplingFactor) saw a
        // measurement 56 years long (Cs-137 x1.79). Amber's decision of 14.10.2026, verbatim:
        // «Начало = сейчас − набранное время» - the P279 rule (start = end - real time,
        // DocumentManager.FillAbsentTimes) with the moment of Start as the end. A sample time that is
        // unknown too follows the start (P263: no sample time - the start time). A real time that
        // cannot be subtracted (NaN, negative, infinite, beyond the calendar) leaves the label as it
        // is. The same rule as AmplitudaUsbDeviceController.ResumeUnknownStart; the probe
        // ResumeUnknownStartProbeP298 holds the two to one answer.
        internal static void ResumeUnknownStart(ResultData resultData, double realSeconds, DateTime startPressed)
        {
            if (!(realSeconds >= 0.0) || double.IsInfinity(realSeconds))
            {
                return;
            }
            DateTime start;
            try
            {
                start = startPressed.AddSeconds(-realSeconds);
            }
            catch (ArgumentOutOfRangeException)
            {
                return;
            }
            resultData.StartTime = start;
            if (resultData.SampleInfo != null && resultData.SampleInfo.Time == ResultData.UnknownStartTime)
            {
                resultData.SampleInfo.Time = start;
            }
        }

        // P309 (15.10.2026): THE END OF A CONTINUED DOCUMENT MOVES ONLY WITH DATA. Start on a
        // loaded or stopped spectrum with a known start keeps the start (the document is the base),
        // but the end was written "now" by every finish - a Stop before the first reading, the
        // block that never answered (NoAnswer after 20 s), a failed clear or start - although
        // the counts, the live and the real time stayed exactly those of the file. The decay
        // correction (MeasurementResultManager.DecayToSamplingFactor) then averaged over
        // [start of the file; now]: a 2024 file continued with the block switched off gave
        // Cs-137 x1.03 and Co-60 x1.19 in the result window and in the saved file
        // (P309EndNoDataProbe). The rule of Amber's decision for RadiaCode and Obsidian (P305,
        // 14.10.2026, verbatim: «По первому пакету данных» - "при отказе до первого пакета файл
        // не тронут") applied to this device: a session that brought no reading leaves the end as
        // it was before Start. A session with data ends "now", as before. Not for an empty
        // document and not for the 1970 label - there Start itself set the start (P298), and
        // the end must not stay before it. A Clear while measuring changes the document, so the
        // old end no longer belongs to it. The twin: AmplitudaUsbDeviceController.
        bool KeepsEndWithoutData
        {
            get { return keepEndWithoutData && !sessionHadData; }
        }

        void PumpTimer_Tick(object sender, EventArgs e)
        {
            // Guards against a nested tick (a message loop pumped from inside this one).
            if (inTick || acquisition == null || resultData == null)
            {
                return;
            }
            inTick = true;
            // Declared above the try and handed over in the finally so that every exit path -
            // the early return on a channel mismatch and the catch alike - still reports what
            // this tick took out of the acquisition (N1). Reporting happens last, after every
            // state change below, and AFTER inTick is cleared. The reporter does not run a modal
            // loop inside the tick: it posts the box to the message queue and shows one at a time
            // (a warning that repeats every cycle collapses into the box already on screen), so a
            // restart after a pour or a ceiling (clear -> read -> start) keeps moving through
            // Pump() while a box is open. Clearing inTick stays correct and needed for the
            // nested-tick guard: by then this tick no longer touches `acquisition`, so a nested
            // tick (which runs Pump() and may even run a nested StopMeasurement, e.g. a
            // preset-time stop) is safe (I1).
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
                inTick = false;     // before the reports below, see the comment above
                if (warnings != null)
                {
                    foreach (AmplitudaSerialWarning warning in warnings)
                    {
                        reporter.Report(DescribeWarning(warning));
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
            bool fresh = acquisition.TakeDirty();
            if (fresh)
            {
                sessionHadData = true;      // P309: a reading of this session reached the document
            }
            if (fresh || force)
            {
                acquisition.Compose(spectrum.Spectrum);
                long counts = acquisition.TotalCounts;
                spectrum.TotalPulseCount = counts;
                spectrum.ValidPulseCount = counts;
                spectrum.LiveTime = acquisition.LiveSeconds;
            }
            // Through ticks (100 ns): TimeSpan.FromSeconds rounds to a whole millisecond.
            // P280 (14.10.2026): the real time of the data in hand - the moment of the last
            // reading's counts, the same as TotalPulseCount and LiveTime above - not the PC clock
            // "now", which ran ahead of them by up to a poll period and a read and made the dead
            // time on both panels too high while measuring. It therefore advances once per
            // reading, like the counts and the live time. Except at the preset: once the clock
            // reaches it, the clock is written, so OnTimer stops on the same tick as before
            // (AmplitudaSerialAcquisition.ElapsedTicks).
            TimeSpan elapsed = TimeSpan.FromTicks(acquisition.ElapsedTicks(resultData.ResultDataStatus.PresetTime));
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
            // Warnings are taken before ReleaseAll() (acquisition is about to become null) but
            // reported only after ReleaseAll() and after Recording is cleared, so a box shown
            // meanwhile can never see this session as still in progress (I1).
            List<AmplitudaSerialWarning> warnings = acquisition.TakeWarnings();
            EnergySpectrum spectrum = resultData.EnergySpectrum;
            // MeasurementController.OnTimer no longer ticks for a stopped spectrum: write the final values here.
            spectrum.MeasurementTime = acquisition.RealSeconds;
            spectrum.LiveTime = acquisition.LiveSeconds;
            // P309: no reading in this session - the end of the continued document stays.
            resultData.EndTime = KeepsEndWithoutData ? endBeforeSession : DateTime.Now;
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
                reporter.Report(DescribeWarning(warning));
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
            reporter.Report(message);
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
                // The budget lives in the line next to the constants it is derived from; it grows
                // with the configured gap (every exchange pays the gap, and two foreign steps can
                // run before our urgent read), so a healthy Stop at a large gap is not mistaken
                // for a dead block.
                int stopWaitMs = line.StopBudgetMs(channels);
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
                // Through `reporter`, not a modal box here: the core saves the document right
                // after this returns (a Stop by preset time), and that save must not wait until
                // somebody closes the box (A318, finding 3).
                if (finishedFailure == AmplitudaSerialFailure.BlockReset)
                {
                    reporter.Report(DescribeFailure(finishedFailure, portName, address, channels, 0));
                }
                else if (incomplete || finishedFailure == AmplitudaSerialFailure.ConnectionLost)
                {
                    reporter.Report(string.Format(CultureInfo.InvariantCulture, Resources.ERRAmplitudaSerialStopIncomplete,
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
                    keepEndWithoutData = false;     // P309: the document is not the one the old end belongs to
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
