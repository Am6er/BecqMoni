using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Resources;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using BecquerelMonitor.Properties;

namespace BecquerelMonitor
{
    internal static partial class SpectrumReportData
    {
        static readonly ResourceManager Labels = new ResourceManager(typeof(SpectrumReportData));

        static string Text(string key)
        {
            return Labels.GetString(key, Resources.Culture);
        }

        static string YesNo(bool value)
        {
            return Text(value ? "Yes" : "No");
        }

        static object Number(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value) ? null : (object)value;
        }

        static object Positive(double value)
        {
            return value > 0 ? Number(value) : null;
        }

        static DateTime? Date(DateTime value)
        {
            return value == default(DateTime) || value == ResultData.UnknownStartTime
                ? (DateTime?)null : value;
        }

        static SpectrumReportSheet Sheet(string name, params string[] headers)
        {
            var sheet = new SpectrumReportSheet(Text(name));
            sheet.Add(headers.Select(Text).ToArray());
            return sheet;
        }

        static void Field(SpectrumReportSheet sheet, string label, object value)
        {
            sheet.Add(Text(label), value);
        }

        internal static List<SpectrumReportSheet> Build(DocEnergySpectrum document, FsaResult result,
                                                        DoseRate dose, ResultCorrection correction)
        {
            var rd = document.ActiveResultData;
            var session = document.FsaSession;
            string state = result != null ? Resources.FSAStatusCompleted
                : FsaAnalysisSession.OffText(session.OffReason)
                  ?? (session.IsRunning ? Resources.FSAStatusRunning : session.Status)
                  ?? Text("NotPerformed");

            return new List<SpectrumReportSheet>
            {
                Sample(document, rd), Activities(rd, result, correction, state),
                Device(rd), Background(rd, result), Calibrations(rd, result),
                Peaks(rd), Dose(dose), Analysis(document, rd, result, state)
            };
        }

        static SpectrumReportSheet Sample(DocEnergySpectrum document, ResultData rd)
        {
            var sheet = Sheet("Sample", "Field", "Value");
            Field(sheet, "File", document.IsNamed ? Path.GetFileName(document.Filename) : null);
            Field(sheet, "SpectrumNumber", document.ActiveResultDataIndex + 1);
            var sample = rd.SampleInfo;
            Field(sheet, "SampleName", sample?.Name?.ToString());
            Field(sheet, "Location", sample?.Location);
            Field(sheet, "SamplingTime", sample == null ? null : Date(sample.Time));
            Field(sheet, "Mass", Positive(sample?.Weight ?? 0));
            Field(sheet, "Volume", Positive(sample?.Volume ?? 0));
            Field(sheet, "Notes", sample?.Note?.ToString());
            Field(sheet, "Start", Date(rd.StartTime));
            Field(sheet, "End", Date(rd.EndTime));
            Field(sheet, "Preset", Positive(rd.PresetTime));
            SpectrumTotals(sheet, rd.EnergySpectrum);
            return sheet;
        }

        static void SpectrumTotals(SpectrumReportSheet sheet, EnergySpectrum spectrum)
        {
            if (spectrum == null) return;
            Field(sheet, "RealTime", Number(spectrum.MeasurementTime));
            Field(sheet, "LiveTime", Positive(spectrum.LiveTime));
            Field(sheet, "Channels", spectrum.NumberOfChannels);
            Field(sheet, "PulseCount", spectrum.TotalPulseCount);
            Field(sheet, "ValidPulses", spectrum.ValidPulseCount);
            Field(sheet, "RejectedPulses", spectrum.TotalPulseCount - spectrum.ValidPulseCount);
            Field(sheet, "AverageRate", spectrum.MeasurementTime > 0
                ? Number(spectrum.ValidPulseCount / spectrum.MeasurementTime) : null);
            Field(sheet, "ChannelPitch", Number(spectrum.ChannelPitch));
            Field(sheet, "Samples", spectrum.NumberOfSamples);
        }

        static SpectrumReportSheet Device(ResultData rd)
        {
            var sheet = Sheet("Device", "Field", "Value");
            var device = rd.DeviceConfig;
            Field(sheet, "DeviceName", string.IsNullOrEmpty(device?.Name) ? rd.DeviceConfigReference?.Name : device.Name);
            Field(sheet, "DeviceId", string.IsNullOrEmpty(device?.Guid) ? rd.DeviceConfigReference?.Guid : device.Guid);
            Field(sheet, "Serial", rd.EnergySpectrum.SerialNumber?.ToString());
            Field(sheet, "Detector", rd.DetectorFeature);
            Field(sheet, "DeviceType", device?.DeviceType);
            Field(sheet, "Crystal", device?.CrystalMaterialName);
            Field(sheet, "Notes", device?.Note?.ToString());
            var g = rd.Efficiency?.Geometry;
            if (g != null)
            {
                Field(sheet, "Geometry", g.Name);
                Field(sheet, "Description", g.Describe());
                Field(sheet, "Shielded", YesNo(g.InShield));
                Field(sheet, "Crystal", g.Crystal?.Name);
                Field(sheet, "CrystalDensity", Positive(g.Crystal?.Density ?? 0));
                Field(sheet, "Fwhm662", Positive(g.FwhmAt662Percent));
                Field(sheet, "SourceMaterial", g.Source?.Name);
                Field(sheet, "SourceDensity", Positive(g.Source?.Density ?? 0));
            }
            return sheet;
        }

        static SpectrumReportSheet Background(ResultData rd, FsaResult result)
        {
            var sheet = Sheet("Background", "Field", "Value");
            var bg = rd.BackgroundEnergySpectrum;
            Field(sheet, "Linked", YesNo(bg != null));
            Field(sheet, "File", rd.BackgroundSpectrumFile ?? Path.GetFileName(rd.BackgroundSpectrumPathname));
            Field(sheet, "BackgroundUsed", result == null ? null : YesNo(result.BackgroundUsed));
            if (result != null && result.BackgroundUsed)
            {
                Field(sheet, "BackgroundScale", Number(result.BackgroundScale));
                Field(sheet, "Gain", Number(result.BackgroundGain));
                Field(sheet, "Offset", Number(result.BackgroundOffset));
            }
            if (result != null && !string.IsNullOrEmpty(result.BackgroundRejected))
                Field(sheet, "Reason", result.BackgroundRejected);
            SpectrumTotals(sheet, bg);
            if (bg != null)
            {
                Field(sheet, "Serial", bg.SerialNumber?.ToString());
                var calibration = bg.EnergyCalibration as PolynomialEnergyCalibration;
                if (calibration?.Coefficients != null)
                    for (int i = 0; i < calibration.Coefficients.Length; i++)
                        sheet.Add(Text("EnergyCoeff") + " " + i, Number(calibration.Coefficients[i]));
            }
            return sheet;
        }

        static SpectrumReportSheet Calibrations(ResultData rd, FsaResult result)
        {
            var sheet = Sheet("Calibration", "Parameter", "Value", "Index", "Channel", "Energy", "WidthCh", "Counts",
                "Source", "EnergyAnchor", "Efficiency", "EffError");
            var energy = rd.EnergySpectrum.EnergyCalibration as PolynomialEnergyCalibration;
            if (energy != null) Field(sheet, "PolynomialOrder", energy.PolynomialOrder);
            Coefficients(sheet, "EnergyCoeff", energy?.Coefficients);
            var width = rd.FwhmCalibration;
            Field(sheet, "FwhmModel", width?.GetFormula());
            Coefficients(sheet, "WidthCoeff", width?.Coefficients);
            Field(sheet, "Curvature", Number(rd.TractCurvature));
            if (rd.CalibrationPoints != null)
                foreach (var p in rd.CalibrationPoints)
                    if (p != null) sheet.Add(Text("EnergyPoint"), null, null, p.Channel, p.Energy, null, p.Count);
            if (rd.CalibrationLines != null)
                foreach (var p in rd.CalibrationLines)
                    if (p != null) sheet.Add(Text("CalibrationLine"), null, null, p.Channel, Number(p.Energy),
                        Positive(p.FWHM), p.ShouldSerializeCount() ? (object)p.Count : null, p.Source, YesNo(p.EnergyLine));

            var curve = rd.Efficiency;
            Field(sheet, "Curve", curve?.Name);
            Field(sheet, "FileCurve", rd.FileEfficiency?.Name);
            Field(sheet, "CurveId", curve?.Guid);
            if (curve != null)
            {
                Field(sheet, "Origin", DCResultView.FsaTexts().NameOf(curve.Origin));
                Field(sheet, "Updated", Date(curve.LastUpdated));
                Field(sheet, "Notes", curve.Note?.ToString());
                Field(sheet, "Stamp", curve.ComputeStamp);
                Field(sheet, "GeometryId", curve.GeometryFingerprint);
                Field(sheet, "MatrixEnabled", YesNo(curve.UseResponseMatrix));
                if (curve.Curve != null)
                    foreach (var point in curve.Curve)
                        if (point != null) sheet.Add(Text("EffPoint"), null, null, null, Number(point.Energy),
                            null, null, null, null, Number(point.Efficiency), Number(point.ErrorPercent));
                var matrix = curve.EmbeddedMatrix;
                if (matrix != null)
                {
                    Field(sheet, "EmbeddedStamp", matrix.Stamp);
                    Field(sheet, "MatrixFormat", matrix.Format);
                    Field(sheet, "MatrixHash", matrix.Sha256);
                }
            }
            Field(sheet, "CurveUsed", result == null ? null : YesNo(result.EfficiencyUsed));
            if (result?.EfficiencyUsed == true)
                Field(sheet, "FitCurve", result.EfficiencyCurveName);
            Field(sheet, "MatrixUsed", result == null ? null : YesNo(result.ResponseMatrixUsed));
            if (result?.ResponseMatrixUsed == true)
            {
                Field(sheet, "FitMatrixStamp", result.MatrixStamp);
                Field(sheet, "FitMatrixFormat", Positive(result.MatrixFileVersion));
            }
            return sheet;
        }

        static void Coefficients(SpectrumReportSheet sheet, string label, double[] values)
        {
            if (values == null) return;
            for (int i = 0; i < values.Length; i++)
                sheet.Add(Text(label), Number(values[i]), i);
        }

        static SpectrumReportSheet Peaks(ResultData rd)
        {
            var sheet = Sheet("Peaks", "Status", "Channel", "Energy", "WidthCh", "WidthKev", "Counts",
                "Snr", "Left", "Right", "Nuclide", "OtherCandidates", "LibraryEnergy", "EnergyDifference",
                "EnergyDifferencePercent", "Resolution", "FwhmDelta");
            if (rd.DetectedPeaks == null || rd.DetectedPeaks.Count == 0)
            {
                string state = rd.PeakSearchFailure;
                if (string.IsNullOrEmpty(state))
                    state = Text(rd.PeakSearchCompleted ? "NoPeaks" :
                        FsaAnalysisSession.IsPeakSearchPending(rd) ? "InProgress" : "NotPerformed");
                sheet.Add(state);
                return sheet;
            }
            foreach (var p in rd.DetectedPeaks)
            {
                if (p == null) continue;
                var candidates = p.NuclideCandidates.Skip(1).Where(n => n != null).Select(n => n.Name);
                var calibration = rd.EnergySpectrum.EnergyCalibration as PolynomialEnergyCalibration;
                double width = p.FwhmKev(calibration);
                double center = calibration?.ChannelToEnergy(p.Channel) ?? double.NaN;
                double libraryEnergy = p.Nuclide?.Energy ?? double.NaN;
                double delta = libraryEnergy > 0 ? p.Energy - libraryEnergy : double.NaN;
                sheet.Add(Text("Detected"), p.Channel, Number(p.Energy), Positive(p.FWHM), Positive(width),
                    p.Count, Number(p.SNR), p.LeftChannel, p.RightChannel, p.Nuclide?.Name, string.Join(", ", candidates),
                    Positive(libraryEnergy), Number(delta), Number(100 * delta / libraryEnergy),
                    width > 0 && center > 0 ? Number(100 * width / center) : null,
                    p.FWHM > 0 ? Number(p.FWHM_DELTA) : null);
            }
            return sheet;
        }

        static SpectrumReportSheet Dose(DoseRate dose)
        {
            var sheet = Sheet("Dose", "Field", "Value", "LoKev", "HiKev", "Counts", "Cps", "Efficiency",
                "RateDose", "CenterEnergy", "RepresentativeEnergy", "FluenceRate");
            if (dose == null || !string.IsNullOrEmpty(dose.Refusal))
            {
                Field(sheet, "Status", dose?.Refusal ?? Text("NoDose"));
                return sheet;
            }
            Field(sheet, "RateDose", Number(dose.Rate));
            Field(sheet, "DoseError", Number(dose.Error));
            Field(sheet, "Approximate", YesNo(dose.Approximate));
            Field(sheet, "Coverage", dose.Coverage >= 0 ? Number(100 * dose.Coverage) : null);
            Field(sheet, "SourceScene", YesNo(dose.SourceScene));
            Field(sheet, "SampleTransmission", Number(dose.SampleTransmission));
            if (dose.Ranges != null)
                foreach (var r in dose.Ranges)
                    sheet.Add(Text(r.Skipped ? "SkippedBand" : "Band"), null, Number(r.LowKev), Number(r.HighKev),
                        Number(r.Counts), Number(r.Cps), Number(r.Efficiency), r.Skipped ? null : Number(r.DoseRate),
                        Number(r.CenterKev), Number(r.RepresentativeKev), r.Skipped ? null : Number(r.FluenceRate));
            return sheet;
        }
    }
}
