namespace BecquerelMonitor
{
    // A device controller that writes EnergySpectrum.LiveTime itself because the device
    // measures live time. MeasurementController.OnTimer then leaves LiveTime alone instead of
    // recomputing it as T - N * DeadTime().
    public interface IDeviceLiveTimeSource
    {
    }
}
