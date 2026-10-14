using BecquerelMonitor.Properties;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    // Measurement life cycle for the Amplituda USB list-mode spectrometer.
    //
    // The controller owns its HID reader (no per-GUID static registry; the one static thing is the
    // per-UNIT lease, AmplitudaUsbBlockLeases.Measurements, P257), and it PULLS data:
    // a UI-thread timer takes the batch accumulated by the reader thread. Existing controllers
    // receive transport events and Post them to the UI thread; that is harmless for them because
    // every event carries a complete histogram. Here data arrives as increments, and a Post
    // delivered after Stop or Clear would corrupt the counts - so there is no queue at all.
    //
    // Time base: real time is the sum of per-frame (live + dead time * events) as counted by the
    // device, not the PC clock. A lost frame takes its events and its time with it, so count
    // rates stay unbiased. BecqMoni recomputes live time itself as T - N * DeadTime().
    //
    // No method lets an exception escape: MeasurementController reports any exception from
    // StartMeasurement as "Bluetooth is not supported". The one exception is a refused Start
    // WITHOUT windows (S100 remainder, A316): there StartMeasurement throws on purpose - see
    // RefuseStart.
    public class AmplitudaUsbDeviceController : DeviceController, IDisposable
    {
        const int PollIntervalMs = 250;
        const double LossWarningFraction = 0.01;
        const double LossWarningMinSeconds = 10.0;

        readonly Timer pollTimer = new Timer();
        readonly Stopwatch sessionClock = new Stopwatch();
        AmplitudaUsbReader reader;
        ResultData resultData;
        long elapsedMicros;             // device real time of the whole measurement
        long sessionDeviceMicros;       // device real time since the last Start / Clear
        int lowerThreshold;
        int upperThreshold;
        double deadSeconds;
        bool disposed;

        public AmplitudaUsbDeviceController()
        {
            pollTimer.Interval = PollIntervalMs;
            pollTimer.Tick += PollTimer_Tick;
        }

        public static string DescribeOpenError(int error, string serialNumber)
        {
            if (error == AmplitudaUsbReader.ErrorDeviceNotFound)
            {
                string which = string.IsNullOrEmpty(serialNumber) ? "" : " (s/n " + serialNumber + ")";
                return string.Format(CultureInfo.InvariantCulture, Resources.ERRAmplitudaUsbNotFound, which);
            }
            if (AmplitudaUsbReader.IsBusyError(error))
            {
                return Resources.ERRAmplitudaUsbBusy;
            }
            if (error == AmplitudaUsbReader.ErrorAllInUse)
            {
                // P257. The key is added to Resources.resx / Resources.ru.resx by the coordinator (the
                // shared resource files belong to no lane); until then - the English text.
                string text = Resources.ResourceManager.GetString("ERRAmplitudaUsbAllInUse", Resources.Culture);
                if (string.IsNullOrEmpty(text))
                {
                    text = "Every connected Amplituda USB device is already being measured in another document "
                           + "(a device configuration with \"(first found)\" skips such devices). "
                           + "Connect another device or stop one of those measurements first.";
                }
                return text;
            }
            if (error == AmplitudaUsbReader.ErrorBlockBusy)
            {
                // P285 (Amber 14.10.2026, "Отказывать второму"): the unit named by its serial number is
                // already measured by another document of this application.
                return string.Format(CultureInfo.InvariantCulture, Resources.ERRAmplitudaUsbBlockBusy, serialNumber);
            }
            return string.Format(CultureInfo.InvariantCulture, Resources.ERRAmplitudaUsbOpenFailed, error);
        }

        public static string DescribeRuntimeError(int error)
        {
            if (error == AmplitudaUsbReader.ErrorSilence)
            {
                return string.Format(CultureInfo.InvariantCulture, Resources.ERRAmplitudaUsbSilent, AmplitudaUsbReader.SilenceGiveUpSeconds);
            }
            return string.Format(CultureInfo.InvariantCulture, Resources.ERRAmplitudaUsbDisconnected, error);
        }

        public override bool StartMeasurement(ResultData resultData)
        {
            try
            {
                return StartCore(resultData);
            }
            catch (Exception ex)
            {
                ReleaseReader();
                if (!AppUi.HasWindows)
                {
                    // RefuseStart's own throw, or any other failure: either way the measurement did
                    // not start, and MeasurementController.StartRecording passes it on.
                    throw;
                }
                AppUi.Report(string.Format(CultureInfo.InvariantCulture, Resources.ERRAmplitudaUsbOpenFailed, ex.Message), "", MessageBoxIcon.None);
                return false;
            }
        }

        // A refused Start. With windows: the message, as before. Without windows: a throw (S100
        // remainder, A316; see MeasurementController.StartRecording) - false has no reader there,
        // and a headless run would go on to present an empty spectrum as a measurement.
        static bool RefuseStart(string message)
        {
            if (!AppUi.HasWindows)
            {
                throw new InvalidOperationException(
                    "BecqMoni: the Amplituda USB measurement cannot start: " + message
                    + " A headless run must not continue: the spectrum would stay empty.");
            }
            AppUi.Report(message, "", MessageBoxIcon.None);
            return false;
        }

        bool StartCore(ResultData resultData)
        {
            // Prefer the manager's copy: editing a config saves a clone there, while open
            // documents keep their old instance (RadiaCodeDeviceController does the same).
            AmplitudaUsbDeviceConfig config = null;
            DeviceConfigInfo managed;
            if (resultData.DeviceConfig.Guid != null &&
                DeviceConfigManager.GetInstance().DeviceConfigMap.TryGetValue(resultData.DeviceConfig.Guid, out managed))
            {
                config = managed.InputDeviceConfig as AmplitudaUsbDeviceConfig;
            }
            if (config == null)
            {
                config = resultData.DeviceConfig.InputDeviceConfig as AmplitudaUsbDeviceConfig;
            }
            if (config == null)
            {
                return RefuseStart(Resources.ERRInvalidDeviceType);
            }

            // Dead time from the DOCUMENT's copy: MeasurementController.OnTimer computes live time from
            // resultData.DeviceConfig, and both must use the same value for T - N * tau to equal the device live time.
            AmplitudaUsbDeviceConfig timing = (resultData.DeviceConfig.InputDeviceConfig as AmplitudaUsbDeviceConfig) ?? config;

            // Check the DOCUMENT's geometry, not the config's: after a config switch on a
            // non-empty spectrum BecqMoni keeps the old channel count.
            EnergySpectrum spectrum = resultData.EnergySpectrum;
            int shift;
            if (spectrum.Spectrum == null || spectrum.Spectrum.Length != spectrum.NumberOfChannels ||
                !AmplitudaUsbAccumulator.TryGetShift(spectrum.NumberOfChannels, out shift))
            {
                return RefuseStart(string.Format(CultureInfo.InvariantCulture, Resources.ERRAmplitudaUsbChannels, spectrum.NumberOfChannels));
            }

            ReleaseReader();
            // In the measurement registry (P257): with "(first found)" the reader skips units other
            // documents measure. Its lease goes with the reader - every path here that ends a session
            // ends in ReleaseReader (reader.Dispose), and a failed reader thread frees it itself.
            AmplitudaUsbReader newReader = new AmplitudaUsbReader(config.VendorId, config.ProductId,
                config.SerialNumber, timing.DeadTimeMicroseconds, AmplitudaUsbBlockLeases.Measurements);
            int error;
            if (!newReader.Start(out error))
            {
                newReader.Dispose();
                return RefuseStart(DescribeOpenError(error, config.SerialNumber));
            }

            this.reader = newReader;
            this.resultData = resultData;
            lowerThreshold = config.LowerThreshold;
            upperThreshold = config.UpperThreshold;
            deadSeconds = timing.DeadTime();
            spectrum.ChannelPitch = 1;      // data arrives in channel units, as with the other digital MCAs

            ResultDataStatus status = resultData.ResultDataStatus;
            elapsedMicros = status.TotalTime.Ticks / 10;        // continue a stopped measurement
            sessionDeviceMicros = 0;
            if (spectrum.TotalPulseCount == 0 && elapsedMicros == 0)
            {
                resultData.StartTime = DateTime.Now;
            }
            sessionClock.Restart();
            pollTimer.Start();
            status.Recording = true;
            return true;
        }

        void PollTimer_Tick(object sender, EventArgs e)
        {
            if (resultData == null || reader == null)
            {
                return;
            }
            try
            {
                if (!resultData.ResultDataStatus.Recording)
                {
                    return;
                }
                if (!Drain())
                {
                    Fail(string.Format(CultureInfo.InvariantCulture, Resources.ERRAmplitudaUsbChannels, resultData.EnergySpectrum.NumberOfChannels));
                    return;
                }
                if (reader.State == AmplitudaUsbReaderState.Failed)
                {
                    Fail(DescribeRuntimeError(reader.LastError));
                }
            }
            catch (Exception ex)
            {
                Fail(string.Format(CultureInfo.InvariantCulture, Resources.ERRAmplitudaUsbOpenFailed, ex.Message));
            }
        }

        // Moves the accumulated batch into the document. False = the spectrum geometry is no
        // longer usable (the document was rebuilt with an unsupported channel count).
        bool Drain()
        {
            AmplitudaUsbBatch batch = reader.TakeBatch();
            EnergySpectrum spectrum = resultData.EnergySpectrum;    // re-fetched: Clear replaces the array
            int shift;
            if (spectrum.Spectrum == null || !AmplitudaUsbAccumulator.TryGetShift(spectrum.Spectrum.Length, out shift))
            {
                return false;
            }
            int accepted = AmplitudaUsbAccumulator.Apply(spectrum.Spectrum, batch.Codes, batch.Count, shift, lowerThreshold, upperThreshold);
            // TotalPulseCount takes EVERY device event, including those cut by the thresholds:
            // the stock live time formula T - N * DeadTime() then equals the device's own live time.
            spectrum.TotalPulseCount += batch.Count;
            spectrum.ValidPulseCount += accepted;
            elapsedMicros += batch.RealMicros;
            sessionDeviceMicros += batch.RealMicros;

            // Through ticks (100 ns): TimeSpan.FromSeconds rounds to a whole millisecond.
            TimeSpan elapsed = TimeSpan.FromTicks(elapsedMicros * 10);
            resultData.ResultDataStatus.ElapsedTime = elapsed;
            resultData.ResultDataStatus.TotalTime = elapsed;
            return true;
        }

        // Ends the session: last batch, final times, Recording = false, reader released.
        void FinishSession()
        {
            pollTimer.Stop();
            sessionClock.Stop();
            if (resultData == null)
            {
                ReleaseReader();
                return;
            }
            if (reader != null)
            {
                reader.Stop();      // the thread is gone, so the last batch is complete
                Drain();
            }
            EnergySpectrum spectrum = resultData.EnergySpectrum;
            // MeasurementController.OnTimer no longer ticks for a stopped spectrum: write the final values here.
            spectrum.MeasurementTime = elapsedMicros / 1.0E+06;
            spectrum.LiveTime = Utils.LiveTime.Calculate(spectrum.MeasurementTime, spectrum.TotalPulseCount, deadSeconds);
            resultData.EndTime = DateTime.Now;
            resultData.ResultDataStatus.Recording = false;
            ReleaseReader();
        }

        // The device is gone or the data cannot be used: stop the way RadiaCode/Obsidian do
        // when the device itself ends a measurement.
        void Fail(string message)
        {
            ResultData failed = resultData;
            try
            {
                FinishSession();
            }
            catch (Exception)
            {
                failed.ResultDataStatus.Recording = false;
                ReleaseReader();
            }
            if (failed.MeasurementController != null)
            {
                failed.MeasurementController.NotifyMeasurementStoppedByDevice();
            }
            AppUi.Report(message, "", MessageBoxIcon.None);
        }

        public override void StopMeasurement(ResultData resultData)
        {
            try
            {
                if (this.resultData == null || reader == null)
                {
                    resultData.ResultDataStatus.Recording = false;
                    return;
                }
                FinishSession();
                ReportLoss();
            }
            catch (Exception)
            {
                // Recording must end up false: otherwise OnTimer calls StopRecording on every tick.
                resultData.ResultDataStatus.Recording = false;
                ReleaseReader();
            }
        }

        // The loss note is for a person who pressed Stop. It is NOT raised while the application is
        // closing (A316): a modal window there would hold up the close - and a Windows shutdown -
        // until OK, and nothing can be done about it at that moment; the counts themselves are not
        // biased by lost frames (see the time base above). MainForm.SaveLayoutXml keeps the same rule.
        void ReportLoss()
        {
            if (MainForm.ApplicationClosing)
            {
                return;
            }
            double wall = sessionClock.Elapsed.TotalSeconds;
            if (wall < LossWarningMinSeconds)
            {
                return;
            }
            double lost = wall - sessionDeviceMicros / 1.0E+06;
            if (lost / wall > LossWarningFraction)
            {
                AppUi.Report(string.Format(CultureInfo.InvariantCulture, Resources.MSGAmplitudaUsbFramesLost, lost, 100.0 * lost / wall), "", MessageBoxIcon.None);
            }
        }

        // Called before the panel zeroes the times and re-allocates the spectrum; allowed while recording.
        public override void ClearMeasurementResult(ResultData resultData)
        {
            try
            {
                elapsedMicros = 0;
                sessionDeviceMicros = 0;
                if (reader != null)
                {
                    reader.TakeBatch();         // drop what was collected before the clear
                    sessionClock.Restart();
                    resultData.StartTime = DateTime.Now;
                }
            }
            catch (Exception)
            {
            }
        }

        // The device cannot be commanded and keeps no histogram: there is nothing to attach to.
        public override bool AttachToDevice(ResultData resultData)
        {
            return false;
        }

        public override void DetachFromDevice(ResultData resultData)
        {
        }

        void ReleaseReader()
        {
            if (reader != null)
            {
                reader.Dispose();
                reader = null;
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            pollTimer.Stop();
            pollTimer.Dispose();
            ReleaseReader();
        }
    }
}
