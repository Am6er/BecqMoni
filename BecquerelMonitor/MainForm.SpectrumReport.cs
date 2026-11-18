using BecquerelMonitor.Properties;
using System;
using System.ComponentModel;
using System.IO;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    public partial class MainForm
    {
        private void ExportReport_Click(object sender, EventArgs e)
        {
            if (activeDocument?.ActiveResultData?.EnergySpectrum == null) return;

            var strings = new ComponentResourceManager(typeof(MainForm));
            using (var dialog = new SaveFileDialog
            {
                Title = strings.GetString("ExportReportTitle"),
                Filter = strings.GetString("ExportReportFilter"),
                FileName = Path.GetFileNameWithoutExtension(activeDocument.Text.Trim('*', ' ')),
                RestoreDirectory = true
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    string extension = Path.GetExtension(dialog.FileName);
                    bool xlsx = string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase)
                        || (!string.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase) && dialog.FilterIndex == 1);
                    SaveSpectrumReport(dialog.FileName, xlsx);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, string.Format(Resources.ERRFileSaveFailure, dialog.FileName, ex.Message),
                        Resources.ErrorDialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                }
            }
        }

        internal void SaveSpectrumReport(string path, bool xlsx)
        {
            var document = activeDocument;
            var selected = document.ActiveResultData;
            var result = document.FsaSession.ResultFor(selected);
            var correction = DCResultView.HalfLifeCorrectionShown() ? ResultCorrection.HalfLifeCorrection : ResultCorrection.Nothing;
            var sheets = SpectrumReportData.Build(document, result, doseRateManager.Calculate(selected), correction);
            document.EnergySpectrumView.RenderReportPlots(selected, result, out byte[] spectrum, out byte[] decomposition);
            SpectrumReportWriter.Write(path, xlsx, sheets, spectrum, decomposition);
        }
    }
}
