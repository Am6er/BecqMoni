using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using BecquerelMonitor.Properties;

namespace BecquerelMonitor.Utils
{
    public sealed class PeakShapePreviewGraph : Form
    {
        const int LeftMargin = 52;
        const int TopMargin = 16;
        const int RightMargin = 16;
        const int BottomMargin = 34;
        const int SigmaRange = 5;
        const int YGridLinesCount = 5;
        const float LegendPadding = 8.0f;
        const float LegendSectionSpacing = 8.0f;
        const float LegendFormulaSpacing = 3.0f;
        const float LegendWidthRatio = 0.34f;
        const float LegendHeightRatio = 0.48f;
        const float LegendMinScale = 0.68f;

        readonly GlobalConfigManager globalConfigManager = GlobalConfigManager.GetInstance();
        readonly MainForm mainForm;

        FwhmCalibration calibration;
        string selectedCurveText;
        int peakType = FwhmCalibration.GaussianPeakType;
        double normalizedLeftBound = -8.0;
        double normalizedRightBound = 8.0;
        double gaussianReferenceSigma = 1.0 / PseudoVoigtProfile.FwhmToSigma;
        double currentCurveSigma = 1.0 / PseudoVoigtProfile.FwhmToSigma;
        PseudoVoigtParameters voigtParameters;

        public PeakShapePreviewGraph(MainForm mainForm)
        {
            this.mainForm = mainForm;
            this.DoubleBuffered = true;
            this.StartPosition = FormStartPosition.CenterParent;
            this.MinimumSize = new Size(520, 320);
            this.Icon = Resources.becqmoni;

            SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            SetStyle(ControlStyles.UserPaint, true);
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);

            if (this.mainForm != null)
            {
                this.Size = new Size(this.mainForm.Width * 2 / 3, this.mainForm.Height * 2 / 3);
            }
            else
            {
                this.Size = new Size(720, 420);
            }
        }

        public void Init(FwhmCalibration fwhmCalibration, string title)
        {
            this.calibration = fwhmCalibration?.Clone();
            this.Text = title ?? string.Empty;
            PrepareProfile();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Rectangle plotBounds = GetPlotBounds();
            if (plotBounds.Width <= 1 || plotBounds.Height <= 1)
            {
                return;
            }

            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            PaintBackground(graphics, plotBounds);
            PaintGrid(graphics, plotBounds);
            PaintCurves(graphics, plotBounds);
            PaintLegend(graphics, plotBounds);
        }

        void PrepareProfile()
        {
            this.peakType = FwhmCalibration.GaussianPeakType;
            this.voigtParameters = new PseudoVoigtParameters();
            this.selectedCurveText = Resources.ResourceManager.GetString("PeakShapeGaussian");
            this.normalizedLeftBound = -SigmaRange;
            this.normalizedRightBound = SigmaRange;
            this.currentCurveSigma = this.gaussianReferenceSigma;

            if (this.calibration == null)
            {
                return;
            }

            if (FwhmCalibration.IsSupportedPeakType(this.calibration.PeakType))
            {
                this.peakType = this.calibration.PeakType;
            }

            this.selectedCurveText = BuildSelectedCurveText();

            if (this.peakType == FwhmCalibration.VoigtPeakType)
            {
                if (!PseudoVoigtProfile.TryCreate(1.0, this.calibration.VoigtSigma, this.calibration.VoigtGamma, out this.voigtParameters))
                {
                    this.peakType = FwhmCalibration.GaussianPeakType;
                    this.selectedCurveText = Resources.ResourceManager.GetString("PeakShapeGaussian");
                }
                else if (IsFinitePositive(this.voigtParameters.GaussianSigma))
                {
                    this.currentCurveSigma = this.voigtParameters.GaussianSigma;
                }
            }
        }

        string BuildSelectedCurveText()
        {
            string peakName = DCFwhmCalibrationView.GetPeakShapeName(this.peakType);

            if (this.peakType == FwhmCalibration.ExpGaussExpPeakType)
            {
                return string.Format(
                    "{0} ({1:0.0}, {2:0.0})",
                    peakName,
                    this.calibration.ExpGaussExpLeftTail,
                    this.calibration.ExpGaussExpRightTail);
            }

            if (this.peakType == FwhmCalibration.VoigtPeakType)
            {
                return string.Format(
                    "{0} ({1:0.0}, {2:0.0})",
                    peakName,
                    this.calibration.VoigtSigma,
                    this.calibration.VoigtGamma);
            }

            return peakName;
        }

        Rectangle GetPlotBounds()
        {
            return new Rectangle(
                LeftMargin,
                TopMargin,
                Math.Max(1, ClientSize.Width - LeftMargin - RightMargin),
                Math.Max(1, ClientSize.Height - TopMargin - BottomMargin));
        }

        void PaintBackground(Graphics graphics, Rectangle plotBounds)
        {
            Color backgroundColor = this.globalConfigManager.GlobalConfig.ColorConfig.BackgroundColor.Color;
            using (Brush brush = new SolidBrush(backgroundColor))
            {
                graphics.FillRectangle(brush, plotBounds);
            }

            using (Pen pen = new Pen(this.globalConfigManager.GlobalConfig.ColorConfig.GridColor1.Color))
            {
                graphics.DrawRectangle(pen, plotBounds);
            }
        }

        void PaintGrid(Graphics graphics, Rectangle plotBounds)
        {
            using (Pen gridPen = new Pen(this.globalConfigManager.GlobalConfig.ColorConfig.GridColor1.Color))
            using (Brush textBrush = new SolidBrush(this.globalConfigManager.GlobalConfig.ColorConfig.AxisFigureColor.Color))
            using (StringFormat centeredFormat = new StringFormat())
            using (StringFormat rightFormat = new StringFormat())
            {
                centeredFormat.Alignment = StringAlignment.Center;
                rightFormat.Alignment = StringAlignment.Far;

                for (int sigmaIndex = -SigmaRange; sigmaIndex <= SigmaRange; sigmaIndex++)
                {
                    float x = ValueToPlotX(plotBounds, sigmaIndex);
                    graphics.DrawLine(gridPen, x, plotBounds.Top, x, plotBounds.Bottom);

                    RectangleF xLabel = new RectangleF(x - 24.0f, plotBounds.Bottom + 4.0f, 48.0f, 18.0f);
                    string xLabelText = sigmaIndex == 0 ? "0" : string.Format("{0}σ", sigmaIndex);
                    graphics.DrawString(xLabelText, this.Font, textBrush, xLabel, centeredFormat);
                }

                for (int stepIndex = 0; stepIndex <= YGridLinesCount; stepIndex++)
                {
                    float y = plotBounds.Top + plotBounds.Height * stepIndex / (float)YGridLinesCount;
                    double yValue = 1.0 - (double)stepIndex / YGridLinesCount;
                    RectangleF yLabel = new RectangleF(4.0f, y - 8.0f, LeftMargin - 8.0f, 18.0f);

                    graphics.DrawLine(gridPen, plotBounds.Left, y, plotBounds.Right, y);
                    graphics.DrawString(yValue.ToString("0.0"), this.Font, textBrush, yLabel, rightFormat);
                }
            }
        }

        void PaintCurves(Graphics graphics, Rectangle plotBounds)
        {
            Rectangle innerPlotBounds = Rectangle.Inflate(plotBounds, -1, -1);
            int pointsCount = Math.Max(2, innerPlotBounds.Width);
            PointF[] gaussianPoints = BuildCurvePoints(innerPlotBounds, pointsCount, EvaluateGaussianRelative);
            PointF[] selectedPoints = BuildCurvePoints(innerPlotBounds, pointsCount, EvaluateSelectedRelative);

            using (Pen gaussianPen = new Pen(this.globalConfigManager.GlobalConfig.ColorConfig.ActiveSpectrumColor.Color, 2.0f))
            using (Pen selectedPen = new Pen(this.globalConfigManager.GlobalConfig.ColorConfig.SelectionNetColor.Color, 2.0f))
            {
                GraphicsState state = graphics.Save();
                graphics.SetClip(innerPlotBounds);
                graphics.DrawLines(gaussianPen, gaussianPoints);
                graphics.DrawLines(selectedPen, selectedPoints);
                graphics.Restore(state);
            }
        }

        void PaintLegend(Graphics graphics, Rectangle plotBounds)
        {
            FormulaPiece[] gaussianFormulaLines = BuildGaussianFormulaPieces();
            FormulaPiece[] selectedFormulaLines = BuildSelectedFormulaPieces();
            string formulaFontFamily = GetFormulaFontFamilyName();
            float legendScale = FindLegendScale(graphics, plotBounds, formulaFontFamily, gaussianFormulaLines, selectedFormulaLines);
            float legendPadding = Math.Max(5.0f, LegendPadding * legendScale);
            float sectionSpacing = Math.Max(4.0f, LegendSectionSpacing * legendScale);
            float sampleLineWidth = Math.Max(18.0f, 24.0f * legendScale);
            float sampleGap = Math.Max(6.0f, 8.0f * legendScale);

            using (Font titleFont = new Font(this.Font.FontFamily, Math.Max(7.0f, this.Font.Size * legendScale), FontStyle.Regular))
            using (Font formulaFont = new Font(formulaFontFamily, Math.Max(7.5f, (this.Font.Size + 2.0f) * legendScale), FontStyle.Regular))
            using (Font scriptFont = new Font(formulaFontFamily, Math.Max(6.0f, Math.Max(7.0f, this.Font.Size) * legendScale), FontStyle.Regular))
            {
                float legendContentWidth = Math.Max(
                    MeasureFormulaSectionWidth(graphics, titleFont, formulaFont, scriptFont, Resources.ResourceManager.GetString("PeakShapeGaussian"), gaussianFormulaLines),
                    MeasureFormulaSectionWidth(graphics, titleFont, formulaFont, scriptFont, this.selectedCurveText, selectedFormulaLines));
                float gaussianSectionHeight = MeasureFormulaSection(graphics, titleFont, formulaFont, scriptFont, gaussianFormulaLines);
                float selectedSectionHeight = MeasureFormulaSection(graphics, titleFont, formulaFont, scriptFont, selectedFormulaLines);
                float legendWidth = sampleLineWidth + sampleGap + legendContentWidth + 2.0f * legendPadding;
                float legendHeight = gaussianSectionHeight + selectedSectionHeight + sectionSpacing + 2.0f * legendPadding;
                RectangleF legendBounds = new RectangleF(
                    plotBounds.Right - legendWidth - 8.0f,
                    plotBounds.Top + 8.0f,
                    legendWidth,
                    legendHeight);

                Color backgroundColor = this.globalConfigManager.GlobalConfig.ColorConfig.BackgroundColor.Color;
                Color textColor = this.globalConfigManager.GlobalConfig.ColorConfig.AxisFigureColor.Color;
                using (Brush backgroundBrush = new SolidBrush(backgroundColor))
                using (Pen borderPen = new Pen(this.globalConfigManager.GlobalConfig.ColorConfig.GridColor1.Color))
                using (Brush textBrush = new SolidBrush(textColor))
                using (Pen formulaPen = new Pen(textColor, 1.0f))
                using (Pen gaussianPen = new Pen(this.globalConfigManager.GlobalConfig.ColorConfig.ActiveSpectrumColor.Color, 2.0f))
                using (Pen selectedPen = new Pen(this.globalConfigManager.GlobalConfig.ColorConfig.SelectionNetColor.Color, 2.0f))
                {
                    graphics.FillRectangle(backgroundBrush, legendBounds.X, legendBounds.Y, legendBounds.Width, legendBounds.Height);
                    graphics.DrawRectangle(borderPen, legendBounds.X, legendBounds.Y, legendBounds.Width, legendBounds.Height);

                    float lineStartX = legendBounds.Left + legendPadding;
                    float lineEndX = lineStartX + sampleLineWidth;
                    float textX = lineEndX + sampleGap;
                    float currentY = legendBounds.Top + legendPadding;

                    DrawFormulaSection(
                        graphics,
                        textBrush,
                        gaussianPen,
                        formulaPen,
                        titleFont,
                        formulaFont,
                        scriptFont,
                        Resources.ResourceManager.GetString("PeakShapeGaussian"),
                        gaussianFormulaLines,
                        lineStartX,
                        lineEndX,
                        textX,
                        ref currentY);

                    currentY += sectionSpacing;

                    DrawFormulaSection(
                        graphics,
                        textBrush,
                        selectedPen,
                        formulaPen,
                        titleFont,
                        formulaFont,
                        scriptFont,
                        this.selectedCurveText,
                        selectedFormulaLines,
                        lineStartX,
                        lineEndX,
                        textX,
                        ref currentY);
                }
            }
        }

        PointF[] BuildCurvePoints(Rectangle plotBounds, int pointsCount, Func<double, double> evaluator)
        {
            PointF[] points = new PointF[pointsCount];
            double xRange = this.normalizedRightBound - this.normalizedLeftBound;
            if (!IsFinitePositive(xRange))
            {
                xRange = 16.0;
            }

            for (int pointIndex = 0; pointIndex < pointsCount; pointIndex++)
            {
                double normalizedX = this.normalizedLeftBound + xRange * pointIndex / (pointsCount - 1);
                double normalizedY = evaluator(normalizedX);
                if (!IsFinite(normalizedY) || normalizedY < 0.0)
                {
                    normalizedY = 0.0;
                }
                else if (normalizedY > 1.0)
                {
                    normalizedY = 1.0;
                }

                float x = plotBounds.Left + (float)((plotBounds.Width - 1) * pointIndex / (double)(pointsCount - 1));
                float y = plotBounds.Bottom - 1 - (float)((plotBounds.Height - 1) * normalizedY);
                points[pointIndex] = new PointF(x, y);
            }

            return points;
        }

        float ValueToPlotX(Rectangle plotBounds, double normalizedX)
        {
            double ratio = (normalizedX - this.normalizedLeftBound) / (this.normalizedRightBound - this.normalizedLeftBound);
            return plotBounds.Left + (float)(plotBounds.Width * ratio);
        }

        double EvaluateGaussianRelative(double normalizedX)
        {
            double physicalX = normalizedX * this.currentCurveSigma;
            double gaussianArgument = physicalX / this.gaussianReferenceSigma;
            return Math.Exp(-0.5 * gaussianArgument * gaussianArgument);
        }

        double EvaluateSelectedRelative(double normalizedX)
        {
            if (this.peakType == FwhmCalibration.ExpGaussExpPeakType)
            {
                if (normalizedX > this.calibration.ExpGaussExpRightTail)
                {
                    return Math.Exp(0.5 * this.calibration.ExpGaussExpRightTail * this.calibration.ExpGaussExpRightTail -
                        this.calibration.ExpGaussExpRightTail * normalizedX);
                }

                if (normalizedX > -this.calibration.ExpGaussExpLeftTail)
                {
                    return Math.Exp(-0.5 * normalizedX * normalizedX);
                }

                return Math.Exp(0.5 * this.calibration.ExpGaussExpLeftTail * this.calibration.ExpGaussExpLeftTail +
                    this.calibration.ExpGaussExpLeftTail * normalizedX);
            }

            if (this.peakType == FwhmCalibration.VoigtPeakType)
            {
                return PseudoVoigtProfile.RelativeValue(normalizedX * this.currentCurveSigma, this.voigtParameters);
            }

            return EvaluateGaussianRelative(normalizedX);
        }

        FormulaPiece[] BuildGaussianFormulaPieces()
        {
            return new[]
            {
                Row(
                    Txt("G(x) = exp("),
                    Fraction(
                        Row(Txt("-"), Sup(Txt("x"), "2")),
                        Row(Txt("2"), Script(Txt("σ"), "g", "2"))),
                    Txt(")")),
                Row(
                    Script(Txt("σ"), "g", null),
                    Txt(" = "),
                    Txt(this.gaussianReferenceSigma.ToString("0.###")))
            };
        }

        FormulaPiece[] BuildSelectedFormulaPieces()
        {
            if (this.peakType == FwhmCalibration.ExpGaussExpPeakType)
            {
                return new[]
                {
                    Row(
                        Txt("E(t) = exp("),
                        Fraction(Sup(Txt("R"), "2"), Txt("2")),
                        Txt(" - Rt),  t > R")),
                    Row(
                        Txt("E(t) = exp("),
                        Fraction(Row(Txt("-"), Sup(Txt("t"), "2")), Txt("2")),
                        Txt("),  -L ≤ t ≤ R")),
                    Row(
                        Txt("E(t) = exp("),
                        Fraction(Sup(Txt("L"), "2"), Txt("2")),
                        Txt(" + Lt),  t < -L")),
                    Row(
                        Txt("t = "),
                        Fraction(Txt("x"), Txt("σ")),
                        Txt(",  σ = "),
                        Txt(this.currentCurveSigma.ToString("0.###")),
                        Txt(",  L = "),
                        Txt(this.calibration.ExpGaussExpLeftTail.ToString("0.###")),
                        Txt(",  R = "),
                        Txt(this.calibration.ExpGaussExpRightTail.ToString("0.###")))
                };
            }

            if (this.peakType == FwhmCalibration.VoigtPeakType)
            {
                return new[]
                {
                    Row(
                        Txt("V(x) = "),
                        Fraction(
                            Row(
                                Txt("ηL(x,γ) + (1 - η)"),
                                Script(Txt("G"), "V", null),
                                Txt("(x,σ)")),
                            Row(Txt("V(0)")))),
                    Row(
                        Script(Txt("G"), "V", null),
                        Txt("(x,σ) = "),
                        Fraction(
                            Row(
                                Txt("exp("),
                                Fraction(
                                    Row(Txt("-"), Sup(Txt("x"), "2")),
                                    Row(Txt("2"), Sup(Txt("σ"), "2"))),
                                Txt(")")),
                            Row(
                                Txt("σ"),
                                Txt("√(2π)")))),
                    Row(
                        Txt("L(x,γ) = "),
                        Fraction(
                            Row(Txt("γ")),
                            Row(
                                Txt("π("),
                                Sup(Txt("x"), "2"),
                                Txt(" + "),
                                Sup(Txt("γ"), "2"),
                                Txt(")")))),
                    Row(
                        Txt("σ = "),
                        Txt(this.voigtParameters.GaussianSigma.ToString("0.###")),
                        Txt(",  γ = "),
                        Txt(this.voigtParameters.LorentzGamma.ToString("0.###")),
                        Txt(",  η = "),
                        Txt(this.voigtParameters.Eta.ToString("0.###")))
                };
            }

            return new[]
            {
                Row(
                    Txt("G(x) = exp("),
                    Fraction(
                        Row(Txt("-"), Sup(Txt("x"), "2")),
                        Row(Txt("2"), Script(Txt("σ"), "g", "2"))),
                    Txt(")")),
                Row(
                    Script(Txt("σ"), "g", null),
                    Txt(" = "),
                    Txt(this.currentCurveSigma.ToString("0.###")))
            };
        }

        float MeasureFormulaSection(Graphics graphics, Font titleFont, Font formulaFont, Font scriptFont, FormulaPiece[] lines)
        {
            float height = titleFont.GetHeight(graphics);
            foreach (FormulaPiece line in lines)
            {
                FormulaMetrics metrics = line.Measure(graphics, formulaFont, scriptFont);
                height += LegendFormulaSpacing + metrics.Height;
            }
            return height;
        }

        float MeasureFormulaSectionWidth(Graphics graphics, Font titleFont, Font formulaFont, Font scriptFont, string title, FormulaPiece[] lines)
        {
            float width = graphics.MeasureString(title ?? string.Empty, titleFont, PointF.Empty, StringFormat.GenericTypographic).Width;
            foreach (FormulaPiece line in lines)
            {
                width = Math.Max(width, line.Measure(graphics, formulaFont, scriptFont).Width);
            }

            return width;
        }

        float FindLegendScale(
            Graphics graphics,
            Rectangle plotBounds,
            string formulaFontFamily,
            FormulaPiece[] gaussianFormulaLines,
            FormulaPiece[] selectedFormulaLines)
        {
            float maxLegendWidth = Math.Max(220.0f, plotBounds.Width * LegendWidthRatio);
            float maxLegendHeight = Math.Max(120.0f, plotBounds.Height * LegendHeightRatio);

            for (float scale = 1.0f; scale >= LegendMinScale; scale -= 0.06f)
            {
                float legendPadding = Math.Max(5.0f, LegendPadding * scale);
                float sectionSpacing = Math.Max(4.0f, LegendSectionSpacing * scale);
                float sampleLineWidth = Math.Max(18.0f, 24.0f * scale);
                float sampleGap = Math.Max(6.0f, 8.0f * scale);

                using (Font titleFont = new Font(this.Font.FontFamily, Math.Max(7.0f, this.Font.Size * scale), FontStyle.Regular))
                using (Font formulaFont = new Font(formulaFontFamily, Math.Max(7.5f, (this.Font.Size + 2.0f) * scale), FontStyle.Regular))
                using (Font scriptFont = new Font(formulaFontFamily, Math.Max(6.0f, Math.Max(7.0f, this.Font.Size) * scale), FontStyle.Regular))
                {
                    float legendContentWidth = Math.Max(
                        MeasureFormulaSectionWidth(graphics, titleFont, formulaFont, scriptFont, Resources.ResourceManager.GetString("PeakShapeGaussian"), gaussianFormulaLines),
                        MeasureFormulaSectionWidth(graphics, titleFont, formulaFont, scriptFont, this.selectedCurveText, selectedFormulaLines));
                    float gaussianSectionHeight = MeasureFormulaSection(graphics, titleFont, formulaFont, scriptFont, gaussianFormulaLines);
                    float selectedSectionHeight = MeasureFormulaSection(graphics, titleFont, formulaFont, scriptFont, selectedFormulaLines);
                    float legendWidth = sampleLineWidth + sampleGap + legendContentWidth + 2.0f * legendPadding;
                    float legendHeight = gaussianSectionHeight + selectedSectionHeight + sectionSpacing + 2.0f * legendPadding;

                    if (legendWidth <= maxLegendWidth && legendHeight <= maxLegendHeight)
                    {
                        return scale;
                    }
                }
            }

            return LegendMinScale;
        }

        void DrawFormulaSection(
            Graphics graphics,
            Brush textBrush,
            Pen samplePen,
            Pen formulaPen,
            Font titleFont,
            Font formulaFont,
            Font scriptFont,
            string title,
            FormulaPiece[] lines,
            float lineStartX,
            float lineEndX,
            float textX,
            ref float currentY)
        {
            float titleHeight = titleFont.GetHeight(graphics);
            graphics.DrawLine(samplePen, lineStartX, currentY + titleHeight * 0.55f, lineEndX, currentY + titleHeight * 0.55f);
            graphics.DrawString(title, titleFont, textBrush, textX, currentY);
            currentY += titleHeight;

            foreach (FormulaPiece line in lines)
            {
                currentY += LegendFormulaSpacing;
                FormulaMetrics metrics = line.Measure(graphics, formulaFont, scriptFont);
                float baseline = currentY + metrics.Ascent;
                line.Draw(graphics, textBrush, formulaPen, textX, baseline, formulaFont, scriptFont);
                currentY += metrics.Height;
            }
        }

        static string GetFormulaFontFamilyName()
        {
            foreach (FontFamily family in FontFamily.Families)
            {
                if (string.Equals(family.Name, "Cambria Math", StringComparison.OrdinalIgnoreCase))
                {
                    return family.Name;
                }
            }

            foreach (FontFamily family in FontFamily.Families)
            {
                if (string.Equals(family.Name, "Cambria", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(family.Name, "Times New Roman", StringComparison.OrdinalIgnoreCase))
                {
                    return family.Name;
                }
            }

            return SystemFonts.DefaultFont.FontFamily.Name;
        }

        static FormulaPiece Txt(string text)
        {
            return new TextFormulaPiece(text);
        }

        static FormulaPiece Row(params FormulaPiece[] pieces)
        {
            return new RowFormulaPiece(pieces);
        }

        static FormulaPiece Fraction(FormulaPiece numerator, FormulaPiece denominator)
        {
            return new FractionFormulaPiece(numerator, denominator);
        }

        static FormulaPiece Script(FormulaPiece basePiece, string subscript, string superscript)
        {
            return new ScriptFormulaPiece(
                basePiece,
                string.IsNullOrEmpty(subscript) ? null : new TextFormulaPiece(subscript),
                string.IsNullOrEmpty(superscript) ? null : new TextFormulaPiece(superscript));
        }

        static FormulaPiece Sup(FormulaPiece basePiece, string superscript)
        {
            return Script(basePiece, null, superscript);
        }

        static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        static bool IsFinitePositive(double value)
        {
            return value > 0.0 && IsFinite(value);
        }

        sealed class FormulaMetrics
        {
            public FormulaMetrics(float width, float ascent, float descent)
            {
                Width = width;
                Ascent = ascent;
                Descent = descent;
            }

            public float Width { get; }
            public float Ascent { get; }
            public float Descent { get; }
            public float Height => Ascent + Descent;
        }

        abstract class FormulaPiece
        {
            public abstract FormulaMetrics Measure(Graphics graphics, Font formulaFont, Font scriptFont);

            public abstract void Draw(Graphics graphics, Brush brush, Pen pen, float x, float baseline, Font formulaFont, Font scriptFont);

            protected static SizeF MeasureText(Graphics graphics, string text, Font font)
            {
                return graphics.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic);
            }

            protected static float FontAscent(Font font, Graphics graphics)
            {
                float height = font.GetHeight(graphics);
                return height * 0.78f;
            }

            protected static float FontDescent(Font font, Graphics graphics)
            {
                float height = font.GetHeight(graphics);
                return height - FontAscent(font, graphics);
            }
        }

        sealed class TextFormulaPiece : FormulaPiece
        {
            readonly string text;

            public TextFormulaPiece(string text)
            {
                this.text = text ?? string.Empty;
            }

            public override FormulaMetrics Measure(Graphics graphics, Font formulaFont, Font scriptFont)
            {
                SizeF size = MeasureText(graphics, this.text, formulaFont);
                return new FormulaMetrics(size.Width, FontAscent(formulaFont, graphics), FontDescent(formulaFont, graphics));
            }

            public override void Draw(Graphics graphics, Brush brush, Pen pen, float x, float baseline, Font formulaFont, Font scriptFont)
            {
                FormulaMetrics metrics = Measure(graphics, formulaFont, scriptFont);
                graphics.DrawString(this.text, formulaFont, brush, x, baseline - metrics.Ascent, StringFormat.GenericTypographic);
            }
        }

        sealed class RowFormulaPiece : FormulaPiece
        {
            readonly FormulaPiece[] pieces;

            public RowFormulaPiece(params FormulaPiece[] pieces)
            {
                this.pieces = pieces ?? Array.Empty<FormulaPiece>();
            }

            public override FormulaMetrics Measure(Graphics graphics, Font formulaFont, Font scriptFont)
            {
                float width = 0.0f;
                float ascent = 0.0f;
                float descent = 0.0f;

                foreach (FormulaPiece piece in this.pieces)
                {
                    FormulaMetrics metrics = piece.Measure(graphics, formulaFont, scriptFont);
                    width += metrics.Width;
                    ascent = Math.Max(ascent, metrics.Ascent);
                    descent = Math.Max(descent, metrics.Descent);
                }

                return new FormulaMetrics(width, ascent, descent);
            }

            public override void Draw(Graphics graphics, Brush brush, Pen pen, float x, float baseline, Font formulaFont, Font scriptFont)
            {
                float currentX = x;
                foreach (FormulaPiece piece in this.pieces)
                {
                    piece.Draw(graphics, brush, pen, currentX, baseline, formulaFont, scriptFont);
                    currentX += piece.Measure(graphics, formulaFont, scriptFont).Width;
                }
            }
        }

        sealed class ScriptFormulaPiece : FormulaPiece
        {
            readonly FormulaPiece basePiece;
            readonly FormulaPiece subscriptPiece;
            readonly FormulaPiece superscriptPiece;

            public ScriptFormulaPiece(FormulaPiece basePiece, FormulaPiece subscriptPiece, FormulaPiece superscriptPiece)
            {
                this.basePiece = basePiece;
                this.subscriptPiece = subscriptPiece;
                this.superscriptPiece = superscriptPiece;
            }

            public override FormulaMetrics Measure(Graphics graphics, Font formulaFont, Font scriptFont)
            {
                FormulaMetrics baseMetrics = this.basePiece.Measure(graphics, formulaFont, scriptFont);
                FormulaMetrics subMetrics = this.subscriptPiece?.Measure(graphics, scriptFont, scriptFont);
                FormulaMetrics supMetrics = this.superscriptPiece?.Measure(graphics, scriptFont, scriptFont);

                float scriptWidth = Math.Max(subMetrics?.Width ?? 0.0f, supMetrics?.Width ?? 0.0f);
                float width = baseMetrics.Width + scriptWidth;
                float ascent = baseMetrics.Ascent;
                float descent = baseMetrics.Descent;

                if (supMetrics != null)
                {
                    ascent = Math.Max(ascent, baseMetrics.Ascent + supMetrics.Height * 0.55f);
                }

                if (subMetrics != null)
                {
                    descent = Math.Max(descent, baseMetrics.Descent + subMetrics.Height * 0.55f);
                }

                return new FormulaMetrics(width, ascent, descent);
            }

            public override void Draw(Graphics graphics, Brush brush, Pen pen, float x, float baseline, Font formulaFont, Font scriptFont)
            {
                FormulaMetrics baseMetrics = this.basePiece.Measure(graphics, formulaFont, scriptFont);
                this.basePiece.Draw(graphics, brush, pen, x, baseline, formulaFont, scriptFont);
                float scriptX = x + baseMetrics.Width;

                if (this.superscriptPiece != null)
                {
                    FormulaMetrics supMetrics = this.superscriptPiece.Measure(graphics, scriptFont, scriptFont);
                    this.superscriptPiece.Draw(graphics, brush, pen, scriptX, baseline - baseMetrics.Ascent * 0.55f, scriptFont, scriptFont);
                }

                if (this.subscriptPiece != null)
                {
                    this.subscriptPiece.Draw(graphics, brush, pen, scriptX, baseline + baseMetrics.Descent * 0.65f, scriptFont, scriptFont);
                }
            }
        }

        sealed class FractionFormulaPiece : FormulaPiece
        {
            readonly FormulaPiece numerator;
            readonly FormulaPiece denominator;

            public FractionFormulaPiece(FormulaPiece numerator, FormulaPiece denominator)
            {
                this.numerator = numerator;
                this.denominator = denominator;
            }

            public override FormulaMetrics Measure(Graphics graphics, Font formulaFont, Font scriptFont)
            {
                FormulaMetrics numeratorMetrics = this.numerator.Measure(graphics, formulaFont, scriptFont);
                FormulaMetrics denominatorMetrics = this.denominator.Measure(graphics, formulaFont, scriptFont);
                float sidePadding = Math.Max(4.0f, formulaFont.Size * 0.25f);
                float numeratorGap = Math.Max(4.0f, formulaFont.Size * 0.35f);
                float denominatorGap = Math.Max(4.0f, formulaFont.Size * 0.4f);
                float width = Math.Max(numeratorMetrics.Width, denominatorMetrics.Width) + sidePadding * 2.0f;
                float ascent = numeratorMetrics.Height + numeratorGap + 1.0f;
                float descent = denominatorMetrics.Height + denominatorGap + 1.0f;
                return new FormulaMetrics(width, ascent, descent);
            }

            public override void Draw(Graphics graphics, Brush brush, Pen pen, float x, float baseline, Font formulaFont, Font scriptFont)
            {
                FormulaMetrics numeratorMetrics = this.numerator.Measure(graphics, formulaFont, scriptFont);
                FormulaMetrics denominatorMetrics = this.denominator.Measure(graphics, formulaFont, scriptFont);
                FormulaMetrics metrics = Measure(graphics, formulaFont, scriptFont);
                float numeratorGap = Math.Max(4.0f, formulaFont.Size * 0.35f);
                float denominatorGap = Math.Max(4.0f, formulaFont.Size * 0.4f);

                float lineY = baseline - 1.0f;
                float numeratorX = x + (metrics.Width - numeratorMetrics.Width) * 0.5f;
                float denominatorX = x + (metrics.Width - denominatorMetrics.Width) * 0.5f;
                float numeratorBaseline = lineY - numeratorGap - numeratorMetrics.Descent;
                float denominatorBaseline = lineY + denominatorGap + denominatorMetrics.Ascent;

                this.numerator.Draw(graphics, brush, pen, numeratorX, numeratorBaseline, formulaFont, scriptFont);
                graphics.DrawLine(pen, x + 1.0f, lineY, x + metrics.Width - 1.0f, lineY);
                this.denominator.Draw(graphics, brush, pen, denominatorX, denominatorBaseline, formulaFont, scriptFont);
            }
        }
    }
}
