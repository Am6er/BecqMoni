namespace BecquerelMonitor
{
    // Token: 0x0200001B RID: 27
    public abstract class DeviceController
    {
        // Token: 0x060000E4 RID: 228
        public abstract bool StartMeasurement(ResultData resultData);

        // Token: 0x060000E5 RID: 229
        public abstract void StopMeasurement(ResultData resultData);

        // Token: 0x060000E6 RID: 230
        public abstract void ClearMeasurementResult(ResultData resultData);

        public abstract bool AttachToDevice(ResultData resultData);

        public abstract void DetachFromDevice(ResultData resultData);
    }
}
