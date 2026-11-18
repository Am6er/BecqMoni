using BecquerelMonitor.FullSpectrumAnalysis;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace BecquerelMonitor
{
    public partial class EnergySpectrumView
    {
        // Render with the normal chart code. A separate view owns the drawing buffers
        // so exporting cannot switch the user's mode, change zoom or schedule a fit.
        internal void RenderReportPlots(ResultData source, FsaResult result, out byte[] spectrum, out byte[] decomposition)
        {
            using (var plot = new EnergySpectrumView())
            {
                plot.Font = Font;
                plot.ClientSize = FitFieldWidth() > 0 && ClientSize.Height > bottom + hScrollBar1.Height
                    ? ClientSize : new Size(1000, 600);
                plot.globalConfigManager = globalConfigManager;
                plot.nuclideManager = nuclideManager;
                plot.left = left;
                plot.bottom = bottom;
                plot.horizontalUnit = horizontalUnit;
                plot.verticalUnit = verticalUnit;
                plot.horizontalScale = horizontalScale;
                plot.verticalScale = verticalScale;
                plot.verticalScaleType = verticalScaleType;
                plot.pownum = pownum;
                plot.fittingMode = fittingMode;
                plot.pixelPerEnergy = pixelPerEnergy;
                plot.energyViewOffset = energyViewOffset;
                plot.chartType = chartType;
                plot.drawingMode = drawingMode;
                plot.smoothingMethod = smoothingMethod;
                plot.peakMode = peakMode;
                plot.highlightedPeaks = highlightedPeaks;
                plot.selectionStart = selectionStart;
                plot.selectionEnd = selectionEnd;
                plot.selectionStartEnergy = selectionStartEnergy;
                plot.selectionEndEnergy = selectionEndEnergy;
                plot.cursorX = cursorX;
                plot.cursorChannel = cursorChannel;
                plot.cursorEnergy = cursorEnergy;
                plot.fsaGrouping = fsaGrouping;
                plot.fsaMatrixLayer = fsaMatrixLayer;
                plot.fsaHighlight = fsaHighlight;
                plot.fsaHighlightWholeChain = fsaHighlightWholeChain;
                plot.fsaHighlightPart = fsaHighlightPart;
                plot.fsaHighlightFirst = fsaHighlightFirst;
                plot.fsaHighlightLast = fsaHighlightLast;
                plot.fsaShowResidual = fsaShowResidual;
                plot.fsaShowSpectralData = fsaShowSpectralData;
                plot.hScrollBar1.Maximum = hScrollBar1.Maximum;
                plot.hScrollBar1.Value = hScrollBar1.Value;
                plot.vScrollBar1.Maximum = vScrollBar1.Maximum;
                plot.vScrollBar1.Value = vScrollBar1.Value;
                // The screen fits its axes to all spectra in the document, even hidden
                // comparisons. Preserve those bounds, but only draw the requested trace.
                plot.resultDataList = new List<ResultData>(resultDataList.Count);
                foreach (ResultData item in resultDataList)
                {
                    var copy = item.CopyForReportPlot();
                    copy.Visible = ReferenceEquals(item, source);
                    if (copy.Visible) plot.activeResultData = copy;
                    plot.resultDataList.Add(copy);
                }
                plot.fsaSession = FsaAnalysisSession.ForReportImage(result, FsaSession.ResponseMatrixOldFormat);

                var normal = backgroundMode == BackgroundMode.Visible ? BackgroundMode.Visible : BackgroundMode.Invisible;
                spectrum = plot.RenderReportPng(normal, DeviceDpi);
                decomposition = result == null ? null : plot.RenderReportPng(BackgroundMode.ShowFSA, DeviceDpi);
            }
        }

        private byte[] RenderReportPng(BackgroundMode mode, int dpi)
        {
            backgroundMode = mode;
            InvalidatePreparedViewData();
            PrepareDrawingData(false);
            RecalcScrollBar();
            using (var bitmap = new Bitmap(ClientSize.Width - vScrollBar1.Width, ClientSize.Height - hScrollBar1.Height))
            using (var output = new MemoryStream())
            {
                bitmap.SetResolution(dpi, dpi);
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(globalConfigManager.GlobalConfig.ColorConfig.BackgroundColor.Color);
                    DrawChart(graphics);
                }
                bitmap.Save(output, ImageFormat.Png);
                return output.ToArray();
            }
        }
    }
}
