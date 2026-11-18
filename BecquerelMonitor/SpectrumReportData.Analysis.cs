using System;
using System.Collections.Generic;
using BecquerelMonitor.FullSpectrumAnalysis;

namespace BecquerelMonitor
{
    internal static partial class SpectrumReportData
    {
        static SpectrumReportSheet Activities(ResultData rd, FsaResult result, ResultCorrection correction, string state)
        {
            var sheet = Sheet("Results", "Nuclide", "Status", "ReferenceTime", "SigmaLevel",
                "ActivityBq", "UncertaintyBq", "MdaBq", "ActivityKg", "UncertaintyKg", "MdaKg",
                "ActivityL", "UncertaintyL", "MdaL", "Notes");
            if (result == null)
            {
                sheet.Add(null, state);
                return sheet;
            }

            var texts = DCResultView.FsaTexts();
            FsaMeasurementResult.Summary summary;
            var raw = FsaMeasurementResult.Build(rd, result, NuclideDefinitionManager.GetInstance().NuclideDefinitions,
                                                 texts, out summary);
            if (raw == null || raw.ResultList.Count == 0)
            {
                sheet.Add(null, Text("NoRows"), null, null, null, null, null, null, null, null, null, null, null,
                    summary.Describe(texts));
                return sheet;
            }
            var manager = new MeasurementResultManager();
            var translated = manager.Translate(raw, ResultTranslation.Becquerels);
            bool toSampling = correction == ResultCorrection.HalfLifeCorrection && rd.SampleInfo != null
                && Date(rd.SampleInfo.Time) != null && Date(rd.StartTime) != null && Date(rd.EndTime) != null;
            if (toSampling) translated = manager.Correct(translated);
            double mass = rd.SampleInfo?.Weight ?? 0;
            double volume = rd.SampleInfo?.Volume ?? 0;
            var config = GlobalConfigManager.GetInstance().GlobalConfig.MeasurementConfig;

            // The panel substitutes zero for an unavailable fit estimate. Its detection-limit
            // input preserves the distinction between that sentinel and a real zero.
            var limits = new Dictionary<string, FsaCharacteristicLimit>(StringComparer.OrdinalIgnoreCase);
            foreach (var limit in FsaPresentationBuilder.UndetectedNamed(result))
                if (!string.IsNullOrEmpty(limit?.Name)) limits[limit.Name] = limit;

            for (int i = 0; i < raw.ResultList.Count; i++)
            {
                var source = raw.ResultList[i];
                var value = translated.ResultList[i];
                bool detected = DCResultView.CheckDetected(source, config);
                FsaCharacteristicLimit limit;
                limits.TryGetValue(source.Line.Name, out limit);
                bool numeric = value.IsValid && summary.Absolute && raw.CountingTime > 0;
                double activity = numeric && (limit == null || limit.CountRate >= 0)
                    ? value.ResultValue : double.NaN;
                double error = numeric && (limit == null || limit.DecisionThresholdRate > 0)
                    ? value.ResultError * (double)config.ErrorLevel : double.NaN;
                double mda = numeric && value.MDA > 0 ? value.MDA : double.NaN;

                string note = summary.Describe(texts);
                if (!string.IsNullOrEmpty(source.Line.Note)) note += ". " + source.Line.Note;
                string moment = toSampling && source.Line.HalfLifeYears > 0 ? Text("AtSampling") : Text("Average");
                sheet.Add(source.Line.Name, Text(detected ? "Detected" : "Undetected"), moment, config.ErrorLevel,
                    Number(activity), Number(error), Number(mda),
                    mass > 0 ? Number(activity / mass) : null, mass > 0 ? Number(error / mass) : null,
                    mass > 0 ? Number(mda / mass) : null,
                    volume > 0 ? Number(activity / volume) : null, volume > 0 ? Number(error / volume) : null,
                    volume > 0 ? Number(mda / volume) : null, note);
            }
            return sheet;
        }

        static SpectrumReportSheet Analysis(DocEnergySpectrum document, ResultData rd, FsaResult result, string state)
        {
            var sheet = Sheet("Fsa", "Field", "Value", "Notes", "Share", "PeakShare", "ZScore", "PeakCounts",
                "DecisionRate", "LimitRate", "ChainRoot");
            Field(sheet, "Status", state);
            Field(sheet, "NuclideSet", document.SelectedNuclideSet?.Name);
            var options = result?.CalculationOptions ?? FsaCalculationOptions.Of(rd);
            Field(sheet, "FromSet", YesNo(options.FromSet));
            Field(sheet, "DbLookups", YesNo(options.DbLookups));
            Field(sheet, "Equilibrium", YesNo(options.ChainEquilibrium));
            Field(sheet, "AtomicXray", YesNo(options.AtomicXray));
            Field(sheet, "Cascade", YesNo(options.CascadeSumming));
            Field(sheet, "Backscatter", YesNo(options.Backscatter));
            Field(sheet, "Escape", YesNo(options.EscapeAndAnnihilation));
            Field(sheet, "Pileup", YesNo(options.PileUp));
            if (result == null) return sheet;

            Field(sheet, "FitStart", Number(result.FitStartKev));
            Field(sheet, "FitFirst", result.FirstChannel);
            Field(sheet, "FitLast", result.LastChannel);
            Field(sheet, "Chi2", Number(result.Chi2NdfPoisson));
            Field(sheet, "Gain", Number(result.Gain));
            Field(sheet, "Offset", Number(result.OffsetChannels));
            Field(sheet, "WidthScale", Number(result.WidthScale));
            Field(sheet, "UncertaintyFactor", Number(result.SigmaInflation));
            Field(sheet, "FitLiveTime", Number(result.LiveTime));
            Field(sheet, "LimitRule", FSAReportView.LimitRuleText(result));
            if (result.Components != null)
                foreach (var component in result.Components)
                    if (component != null)
                        sheet.Add(Text("Component"), FsaPalette.DisplayName(component.Name), null, Number(component.SharePercent),
                            Number(component.PeakSharePercent), Number(component.Z), Number(component.PeakCounts),
                            Positive(component.DecisionThresholdRate), Positive(component.DetectionLimitRate),
                            component.ChainRoot ?? component.DecayChainRoot);

            var presentation = FsaPresentationBuilder.Build(result, document.FsaGrouping,
                                                             document.FsaSession.ResponseMatrixOldFormat);
            foreach (var row in presentation.Rows)
                sheet.Add(FSAReportView.RowCaption(row), row.Value, row.Hint);
            var details = new List<XPTable.Models.Row>();
            FSAReportView.AddQualityDetails(details, presentation, rd, document.FsaSession);
            foreach (var detail in details)
            {
                var name = detail.Cells[0];
                var value = detail.Cells[1];
                var notes = new List<string>();
                foreach (string hint in new[] { name.ToolTipText, value.ToolTipText })
                    if (!string.IsNullOrWhiteSpace(hint) && hint != name.Text && hint != value.Text && !notes.Contains(hint))
                        notes.Add(hint);
                sheet.Add(name.Text, value.Text, string.Join(Environment.NewLine, notes));
            }
            return sheet;
        }
    }
}
