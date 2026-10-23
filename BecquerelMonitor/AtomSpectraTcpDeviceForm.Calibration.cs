// the board calibration goes to the Energy Calibration page after "Test connection (REST)" and "Refresh".
using BecquerelMonitor.Properties;
using System;
using System.Globalization;
using System.Linq;

namespace BecquerelMonitor
{
    public partial class AtomSpectraTcpDeviceForm
    {
        // The Energy Calibration page of DeviceConfigForm; the test harness puts its own here.
        internal Action<double[]> calibrationTarget = null;   // set by reflection in the test harness only
        internal Func<int> profileChannels = null;            // the channels of the profile edited (the test harness has no DeviceConfigForm)

        // calib_set=false or a bad answer leaves the page as it is and says why in the output window.
        private void ApplyBoardCalibration(AtomSpectraTcpCalibrationResult r)
        {
            string text;

            if (r != null && r.Failure == AtomSpectraTcpCalibrationFailure.None && r.Coefficients != null)
            {
                Action<double[]> target = calibrationTarget ?? (deviceConfigForm == null ? null : new Action<double[]>(deviceConfigForm.ApplyAtomSpectraCalibration));
                // the board polynomial is for 8192 channels; the page of a profile with fewer channels gets the same
                // rescale as the document at Start (no profile known: as it is). N is the field being edited, not the saved profile.
                // no whole fold (6000, 16384): NOT the 8192-channel polynomial on the page - nothing is given, and the output says why
                double[] page = (double[])r.Coefficients.Clone();
                int n = profileChannels != null ? profileChannels() : (deviceConfigForm != null && deviceConfigForm.ActiveDeviceConfig != null ? deviceConfigForm.ChannelsEdited() : 0);
                double[] folded = n > 0 ? AtomSpectraRestCalibration.ForChannels(page, 0, n) : page;
                if (folded == null)
                {
                    text = string.Format(CultureInfo.InvariantCulture, Resources.AtomSpectraCalibNotRead, "the board calibration is for 8192 channels, it cannot be folded to the " + n + " channels of the profile; the page keeps its own");
                }
                else
                {
                    if (target != null) target(folded);
                    text = string.Format(CultureInfo.InvariantCulture, Resources.AtomSpectraCalibToPage, string.Join("; ", folded.Select(c => c.ToString("R", CultureInfo.InvariantCulture))));
                }
            }
            else if (r != null && r.Error == "calib_set is false")
            {
                text = Resources.AtomSpectraCalibNotSet;
            }
            else
            {
                text = string.Format(CultureInfo.InvariantCulture, Resources.AtomSpectraCalibNotRead, r == null ? "?" : (r.Error ?? r.Failure.ToString()));
            }

            AtomSpectraTcpLog.Info("Cal", text);
            textOutput.AppendText(text + Environment.NewLine);
        }
    }
}
