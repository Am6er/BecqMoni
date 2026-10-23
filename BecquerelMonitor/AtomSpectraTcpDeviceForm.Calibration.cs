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
            AtomSpectraRestCalibReason reason = AtomSpectraRestCalibration.ReasonOf(r);

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
                    text = string.Format(CultureInfo.InvariantCulture, Resources.AtomSpectraCalibNotFolded, n);
                    AtomSpectraTcpLog.Info("Cal", "the board calibration is for 8192 channels, it cannot be folded to the " + n.ToString(CultureInfo.InvariantCulture) + " channels of the profile; the page keeps its own");
                }
                else
                {
                    if (target != null) target(folded);
                    text = string.Format(CultureInfo.InvariantCulture, Resources.AtomSpectraCalibToPage, string.Join("; ", folded.Select(c => c.ToString("R", CultureInfo.InvariantCulture))));
                    AtomSpectraTcpLog.Info("Cal", "board calibration given to the Energy Calibration page: " + string.Join("; ", folded.Select(c => c.ToString("R", CultureInfo.InvariantCulture))));
                }
            }
            else if (reason == AtomSpectraRestCalibReason.NotSet)
            {
                text = Resources.AtomSpectraCalibNotSet;
                AtomSpectraTcpLog.Info("Cal", "the board has no calibration (calib_set=false); the page is left as it is");
            }
            else
            {
                text = string.Format(CultureInfo.InvariantCulture, Resources.AtomSpectraCalibNotRead, NotReadReason(r, reason));
                AtomSpectraTcpLog.Info("Cal", "the board calibration was not read (" + (r == null ? "no result" : (r.Error ?? r.Failure.ToString())) + "); the page is left as it is");
            }

            PrependOutput(text + Environment.NewLine);   // the newest line on top, as the console writes
        }

        // the reason of a calibration not taken, for the user (resources; the log keeps the English Error)
        private static string NotReadReason(AtomSpectraTcpCalibrationResult r, AtomSpectraRestCalibReason reason)
        {
            int slot = AtomSpectraRestCalibration.SlotOf(r);
            switch (reason)
            {
                case AtomSpectraRestCalibReason.Network:
                    return AtomSpectraRestCalibration.NetworkTextOf(r) ?? Resources.AtomSpectraRestErrNoAnswer;
                case AtomSpectraRestCalibReason.NotNumber:
                    return string.Format(CultureInfo.InvariantCulture, Resources.AtomSpectraCalibErrSlot, slot);
                case AtomSpectraRestCalibReason.Gap:
                    return string.Format(CultureInfo.InvariantCulture, Resources.AtomSpectraCalibErrGap, slot);
                case AtomSpectraRestCalibReason.NotFinite:
                    return string.Format(CultureInfo.InvariantCulture, Resources.AtomSpectraCalibErrNotFinite, slot);
                case AtomSpectraRestCalibReason.TooFew:
                    return Resources.AtomSpectraCalibErrTooFew;
                default:   // not an object, no array, does not parse
                    return Resources.AtomSpectraCalibErrAnswer;
            }
        }
    }
}
