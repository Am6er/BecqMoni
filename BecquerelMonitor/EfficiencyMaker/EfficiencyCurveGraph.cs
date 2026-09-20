using System;
using BecquerelMonitor.Properties;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace BecquerelMonitor.EfficiencyMaker
{
    /// <summary>
    /// График кривой эффективности в логарифмических осях: кривая, лежащая в
    /// конфигурации прибора (пунктиром), и только что посчитанная из геометрии.
    ///
    /// Режим «разность» и точки измеренных линий сняты 13.09.2026 вместе с
    /// эмпирическим восстановлением кривой по спектрам (`AMBER25`, решение
    /// Amber): расчёт из геометрии не поправляет прежнюю кривую, а даёт свою с
    /// абсолютным уровнем — показывать его расхождение с прежней как
    /// «отличие» значило бы выдавать за поправку то, что поправкой не является.
    /// </summary>
    public class EfficiencyCurveGraph : Control
    {
        List<ROIEfficiencyData> reference;
        EfficiencyFitResult result;

        public EfficiencyCurveGraph()
        {
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            this.BackColor = Color.White;
        }

        public void SetData(List<ROIEfficiencyData> referenceCurve, EfficiencyFitResult fit)
        {
            this.reference = referenceCurve;
            this.result = fit;
            this.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(this.BackColor);

            Rectangle plot = new Rectangle(58, 12, Math.Max(this.Width - 78, 10),
                                           Math.Max(this.Height - 46, 10));
            if (plot.Width < 40 || plot.Height < 40)
            {
                return;
            }

            // Диапазон выбирается по исходным double: PointF округляет 0.001
            // вверх через границу декады, а слабый хвост может обратить в ноль.
            List<ROIEfficiencyData> points = new List<ROIEfficiencyData>();
            if (this.reference != null)
            {
                points.AddRange(this.reference.Where(IsDrawable));
            }

            if (this.result != null)
            {
                points.AddRange(this.result.Curve.Where(IsDrawable));
            }

            if (points.Count < 2)
            {
                TextRenderer.DrawText(g, Resources.EfficiencyMakerGraphEmpty, this.Font,
                    plot, Color.Gray, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            double eLo = points.Min(p => p.Energy), eHi = points.Max(p => p.Energy);
            double vLo = points.Min(p => p.Efficiency), vHi = points.Max(p => p.Efficiency);
            if (eHi <= eLo) eHi = eLo * 2.0;
            if (vHi <= vLo) vHi = vLo * 2.0;
            double lx0 = Math.Log10(eLo), lx1 = Math.Log10(eHi);
            double ly0 = Math.Log10(vLo), ly1 = Math.Log10(vHi);
            lx0 = Math.Floor(lx0 * 4) / 4.0; lx1 = Math.Ceiling(lx1 * 4) / 4.0;
            ly0 = Math.Floor(ly0); ly1 = Math.Ceiling(ly1);
            // AMBER37: не более шести декад под верхней подписанной декадой.
            ly0 = Math.Max(ly0, ly1 - 6);

            Func<double, float> mapX = v =>
                (float)(plot.Left + (Math.Log10(Math.Max(v, 1e-12)) - lx0) / (lx1 - lx0) * plot.Width);
            Func<double, float> mapY = v =>
                (float)(plot.Bottom - (Math.Max(Math.Log10(v), ly0) - ly0) / (ly1 - ly0) * plot.Height);

            using (Pen grid = new Pen(Color.FromArgb(0xE0, 0xE0, 0xE0)))
            using (Pen axis = new Pen(Color.FromArgb(0x80, 0x80, 0x80)))
            using (Brush text = new SolidBrush(Color.FromArgb(0x50, 0x50, 0x50)))
            {
                for (int d = (int)Math.Floor(ly0); d <= (int)Math.Ceiling(ly1); d++)
                {
                    float y = (float)(plot.Bottom - (d - ly0) / (ly1 - ly0) * plot.Height);
                    if (y < plot.Top - 1 || y > plot.Bottom + 1) continue;
                    g.DrawLine(grid, plot.Left, y, plot.Right, y);
                    g.DrawString("1e" + d.ToString(CultureInfo.InvariantCulture), this.Font, text, 2, y - 7);
                }

                foreach (double decade in new[] { 10.0, 100.0, 1000.0 })
                {
                    for (int k = 1; k <= 9; k++)
                    {
                        double v = decade * k;
                        if (v < eLo * 0.5 || v > eHi * 2.0) continue;
                        float x = mapX(v);
                        if (x < plot.Left - 1 || x > plot.Right + 1) continue;
                        bool major = k == 1 || (decade >= 100 && (k == 5 || k == 2));
                        g.DrawLine(grid, x, plot.Top, x, plot.Bottom);
                        if (major)
                        {
                            // Ось подписана в кэВ (EfficiencyMakerGraphXAxis), и
                            // метка тоже в кэВ. Прежнее «1M» на отметке 1000
                            // читалось как мегаэлектронвольт на килоэлектронной
                            // шкале — то есть как промах в тысячу раз.
                            string label = v.ToString("0", CultureInfo.InvariantCulture);
                            g.DrawString(label, this.Font, text, x - 10, plot.Bottom + 3);
                        }
                    }
                }

                g.DrawRectangle(axis, plot);
                g.DrawString(Resources.EfficiencyMakerGraphXAxis, this.Font, text,
                    plot.Right - 60, plot.Bottom + 16);
            }

            if (this.reference != null && this.reference.Count >= 2)
            {
                DrawCurve(g, this.reference, mapX, mapY, plot,
                    Color.FromArgb(0x90, 0x90, 0x90), 1.6f, DashStyle.Dash);
            }

            if (this.result != null && this.result.Curve.Count >= 2)
            {
                DrawCurve(g, this.result.Curve, mapX, mapY, plot,
                    Color.FromArgb(0x1F, 0x6F, 0xB2), 2.2f, DashStyle.Solid);
            }
        }

        static bool IsDrawable(ROIEfficiencyData point)
        {
            return point.Energy > 0 && !double.IsInfinity(point.Energy)
                && point.Efficiency > 0 && !double.IsInfinity(point.Efficiency);
        }

        static void DrawCurve(Graphics g, List<ROIEfficiencyData> curve,
                              Func<double, float> mapX, Func<double, float> mapY,
                              Rectangle plot, Color color, float width, DashStyle dash)
        {
            List<PointF> path = new List<PointF>();
            foreach (ROIEfficiencyData point in curve.Where(IsDrawable)
                                                     .OrderBy(p => p.Energy))
            {
                float x = mapX(point.Energy), y = mapY(point.Efficiency);
                // Точка вне поля не выбрасывается, а прижимается: разрыв линии
                // читался бы как отсутствие кривой, а не как выход за рамку.
                path.Add(new PointF(
                    Math.Min(Math.Max(x, plot.Left), plot.Right),
                    Math.Min(Math.Max(y, plot.Top), plot.Bottom)));
            }

            if (path.Count < 2)
            {
                return;
            }

            using (Pen pen = new Pen(color, width) { DashStyle = dash })
            {
                g.DrawLines(pen, path.ToArray());
            }
        }
    }
}
