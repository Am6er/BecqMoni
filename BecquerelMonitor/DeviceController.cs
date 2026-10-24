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

        public abstract bool AttachToDevice(ResultData resultData);

        public abstract void DetachFromDevice(ResultData resultData);
    }
}
