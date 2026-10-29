namespace BecquerelMonitor
{
    // Token: 0x0200001B RID: 27
    public abstract class DeviceController
    {
        // Token: 0x060000E4 RID: 228
        public abstract bool StartMeasurement(ResultData resultData);

        // Token: 0x060000E5 RID: 229
        public abstract void StopMeasurement(ResultData resultData);

        // manual = the user pressed "Stop" (or deleted the spectrum); false = the preset, the timer or the program itself stops it.
        // Only a manual Stop may ask the user anything (AtomSpectra: stop a joined acquisition on the board too?).
        public virtual void StopMeasurement(ResultData resultData, bool manual)
        {
            StopMeasurement(resultData);
        }

        // true while a StopMeasurement of this device still runs and another may enter it (AtomSpectra TCP/REST: its Stop question is open,
        // a REST Stop waits behind its window): MeasurementController does not end the run a second time. No other device stops so.
        public virtual bool StopInProgress
        {
            get { return false; }
        }

        // Token: 0x060000E6 RID: 230
        public abstract void ClearMeasurementResult(ResultData resultData);

        // The question before "Clear" (#BM-22): the user answers once for the document and the device together. A device
        // that has something of its own to ask (AtomSpectra REST: the board's numbers) overrides this and asks ONE question
        // instead of this one; "No" means nothing is cleared, neither the document nor the device.
        // ask = the question of the window that called "Clear" (every device except AtomSpectra TCP/REST asks exactly as the release did,
        // MessageBox without owner, see DCControlPanel); AtomSpectra TCP/REST ignore it and ask through AppUi,
        // also when a document keeps such a controller after the device was changed: the question is then asked by the controller, not by the caller.
        public virtual bool ConfirmClear(ResultData resultData, System.Func<bool> ask = null)
        {
            return ask != null ? ask() : AskClearDocument();
        }

        public static bool AskClearDocument()
        {
            return AppUi.AskYesNo(Properties.Resources.MSGClearSpectrum, Properties.Resources.ConfirmationDialogTitle, System.Windows.Forms.MessageBoxIcon.Exclamation, false);
        }

        public abstract bool AttachToDevice(ResultData resultData);

        public abstract void DetachFromDevice(ResultData resultData);
    }
}
