using BecquerelMonitor.Properties;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    // StartMeasurement as "Bluetooth is not supported".
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

        readonly System.Windows.Forms.Timer pumpTimer = new System.Windows.Forms.Timer();
        AmplitudaSerialLine line;
        AmplitudaSerialAcquisition acquisition;
        ResultData resultData;
        string portName = "";
        int address;
        int channels;
        bool inTick;
        bool disposed;

        public AmplitudaSerialDeviceController()
        {
            pumpTimer.Interval = PumpIntervalMs;
            pumpTimer.Tick += PumpTimer_Tick;
        }

        static string SettleKey(string portName, int address)
        {
            return portName.ToUpperInvariant() + ":" + address;
        }

        public static string DescribeAcquireError(string portName, string error)
        {
            if (error == AmplitudaSerialLine.ErrorSettingsDiffer)
            {
                return string.Format(Resources.ERRAmplitudaSerialSettingsDiffer, portName);
            }
            return string.Format(Resources.ERRAmplitudaSerialPort, portName, error);
        }

        public static string DescribeFailure(AmplitudaSerialFailure failure, string portName, int address,
            int configuredChannels, int suggestedChannels)
        {
            switch (failure)
            {
                case AmplitudaSerialFailure.NoAnswer:
                    return string.Format(Resources.ERRAmplitudaSerialNoAnswer, portName, address);
                case AmplitudaSerialFailure.ClearFailed:
                    return string.Format(Resources.ERRAmplitudaSerialClearFailed, portName, address);
                case AmplitudaSerialFailure.StartFailed:
                    return string.Format(Resources.ERRAmplitudaSerialStartFailed, portName, address);
                case AmplitudaSerialFailure.ChannelsExceedBlock:
                    return string.Format(Resources.ERRAmplitudaSerialChannelsExceedBlock, portName, address,
                        configuredChannels, suggestedChannels);
                case AmplitudaSerialFailure.BlockReset:
                    return Resources.MSGAmplitudaSerialBlockReset;
                default:
                    return string.Format(Resources.ERRAmplitudaSerialLost, portName, address);
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
                AppUi.Report(string.Format(Resources.ERRAmplitudaSerialError, ex.Message), "", MessageBoxIcon.None);
                return false;
            }
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
                AppUi.Report(Resources.ERRInvalidDeviceType, "", MessageBoxIcon.None);
                return false;
            }
            if (string.IsNullOrEmpty(config.PortName))
            {
                AppUi.Report(Resources.ERRAmplitudaSerialNoPort, "", MessageBoxIcon.None);
                return false;
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
                        AppUi.Report(string.Format(Resources.ERRAmplitudaSerialSettling, (int)elapsed, config.HvSettleSeconds,
                            (int)Math.Ceiling(config.HvSettleSeconds - elapsed)), "", MessageBoxIcon.None);
                        return false;
                    }
                }
            }

            // Check the DOCUMENT's geometry, not the config's: after a config switch on a
            // non-empty spectrum BecqMoni keeps the old channel count.
            EnergySpectrum spectrum = resultData.EnergySpectrum;
            if (spectrum.Spectrum == null || spectrum.Spectrum.Length != spectrum.NumberOfChannels ||
                !AmplitudaSerialProtocol.IsValidChannelCount(spectrum.NumberOfChannels))
            {
                AppUi.Report(string.Format(Resources.ERRAmplitudaSerialChannels, spectrum.NumberOfChannels), "", MessageBoxIcon.None);
                return false;
            }

            ReleaseAll();
            string error;
            AmplitudaSerialLine acquired = AmplitudaSerialLine.Acquire(config.ToSettings(), out error);
            if (acquired == null)
            {
                AppUi.Report(DescribeAcquireError(config.PortName, error), "", MessageBoxIcon.None);
                return false;
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
            // AppUi.Report may run a modal loop in which this timer keeps ticking.
            if (inTick || acquisition == null || resultData == null)
            {
                return;
            }
            inTick = true;
            // Declared above the try and reported in the finally so that every exit path -
            // the early return on a channel mismatch and the catch alike - still reports what
            // this tick took out of the acquisition (N1). Reporting happens last, after every
            // state change below: a modal AppUi.Report keeps MainForm's own timer ticking, and a
            // nested StopMeasurement (e.g. a preset-time stop) must not run while this tick still
            // intends to touch `acquisition` (I1).
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
                    Fail(string.Format(Resources.ERRAmplitudaSerialChannels, resultData.EnergySpectrum.NumberOfChannels));
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
                Fail(string.Format(Resources.ERRAmplitudaSerialError, ex.Message));
            }
            finally
            {
                if (warnings != null)
                {
                    foreach (AmplitudaSerialWarning warning in warnings)
                    {
                        AppUi.Report(DescribeWarning(warning), "", MessageBoxIcon.None);
                    }
                }
                inTick = false;
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
            // Warnings are taken before ReleaseAll() (acquisition is about to become null) but
            // reported only after ReleaseAll() and after Recording is cleared, so a modal
            // AppUi.Report here can never see this session as still in progress (I1).
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
                AppUi.Report(DescribeWarning(warning), "", MessageBoxIcon.None);
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
            AppUi.Report(message, "", MessageBoxIcon.None);
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
                // One 1024-byte exchange (header or a page) costs ~0.7 s at 19200 baud; a Halt
                // read is status + header + N pages, so the wait has to scale with the channel
                // count instead of a flat number that silently drops the final reading above a
                // few thousand channels (I3). The floor keeps the small-block case unchanged.
                int stopWaitMs = Math.Max(8000, 2000 + AmplitudaSerialProtocol.PageCount(channels) * 1500);
                Stopwatch wait = Stopwatch.StartNew();
                while (!acquisition.Finished && wait.ElapsedMilliseconds < stopWaitMs)
                {
                    acquisition.Pump();
                    Thread.Sleep(20);
                }
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
                if (finishedFailure == AmplitudaSerialFailure.BlockReset)
                {
                    AppUi.Report(DescribeFailure(finishedFailure, portName, address, channels, 0), "", MessageBoxIcon.None);
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

        void ReleaseAll()
        {
            acquisition = null;
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
