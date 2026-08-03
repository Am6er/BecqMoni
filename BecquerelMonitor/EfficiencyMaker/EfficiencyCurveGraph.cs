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
    /// График кривой эффективности в логарифмических осях: исходная кривая,
    /// восстановленная и сами измеренные точки. Точки нужны на картинке не
    /// меньше кривой — по их разбросу видно, держится ли вековое равновесие и
    /// не утащила ли кривую одна линия.
    /// </summary>
    public class EfficiencyCurveGraph : Control
    {
        static readonly Color[] SeriesColors =
        {
            Color.FromArgb(0xD9, 0x53, 0x4F), Color.FromArgb(0x42, 0x8B, 0xCA),
            Color.FromArgb(0x5C, 0xB8, 0x5C), Color.FromArgb(0xF0, 0xAD, 0x4E),
            Color.FromArgb(0x9B, 0x59, 0xB6), Color.FromArgb(0x16, 0xA0, 0x85),
            Color.FromArgb(0xC0, 0x39, 0x2B), Color.FromArgb(0x2C, 0x3E, 0x50)
        };

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

            List<PointF> points = new List<PointF>();
            if (this.reference != null)
            {
                points.AddRange(this.reference.Where(p => p.Energy > 0 && p.Efficiency > 0)
                    .Select(p => new PointF((float)p.Energy, (float)p.Efficiency)));
            }

            if (this.result != null)
            {
                points.AddRange(this.result.Curve.Where(p => p.Energy > 0 && p.Efficiency > 0)
                    .Select(p => new PointF((float)p.Energy, (float)p.Efficiency)));
                points.AddRange(this.result.Observations
                    .Where(o => o.Accepted && o.MeasuredEfficiency > 0)
                    .Select(o => new PointF((float)o.Energy, (float)o.MeasuredEfficiency)));
            }

            if (points.Count < 2)
            {
                TextRenderer.DrawText(g, Resources.EfficiencyMakerGraphEmpty, this.Font,
                    plot, Color.Gray, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            double eLo = points.Min(p => p.X), eHi = points.Max(p => p.X);
            double vLo = points.Min(p => p.Y), vHi = points.Max(p => p.Y);
            if (eHi <= eLo) eHi = eLo * 2.0;
            if (vHi <= vLo) vHi = vLo * 2.0;
            double lx0 = Math.Log10(eLo), lx1 = Math.Log10(eHi);
            double ly0 = Math.Log10(vLo), ly1 = Math.Log10(vHi);
            lx0 = Math.Floor(lx0 * 4) / 4.0; lx1 = Math.Ceiling(lx1 * 4) / 4.0;
            ly0 = Math.Floor(ly0); ly1 = Math.Ceiling(ly1);

            Func<double, float> mapX = v =>
                (float)(plot.Left + (Math.Log10(Math.Max(v, 1e-12)) - lx0) / (lx1 - lx0) * plot.Width);
            Func<double, float> mapY = v =>
                (float)(plot.Bottom - (Math.Log10(Math.Max(v, 1e-12)) - ly0) / (ly1 - ly0) * plot.Height);

            using (Pen grid = new Pen(Color.FromArgb(0xE0, 0xE0, 0xE0)))
            using (Pen axis = new Pen(Color.FromArgb(0x80, 0x80, 0x80)))
            using (Brush text = new SolidBrush(Color.FromArgb(0x50, 0x50, 0x50)))
            {
                for (int d = (int)Math.Floor(ly0); d <= (int)Math.Ceiling(ly1); d++)
                {
                    float y = mapY(Math.Pow(10, d));
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
                            string label = v >= 1000
                                ? (v / 1000).ToString("0.#", CultureInfo.InvariantCulture) + "M"
                                : v.ToString("0", CultureInfo.InvariantCulture);
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

            if (this.result != null)
            {
                List<string> series = this.result.Observations
                    .Where(o => o.Accepted).Select(o => o.Chain).Distinct().ToList();
                foreach (EfficiencyObservation o in this.result.Observations)
                {
                    if (!o.Accepted || o.MeasuredEfficiency <= 0)
                    {
                        continue;
                    }

                    Color c = SeriesColors[Math.Max(series.IndexOf(o.Chain), 0) % SeriesColors.Length];
                    float x = mapX(o.Energy), y = mapY(o.MeasuredEfficiency);
                    if (x < plot.Left || x > plot.Right || y < plot.Top || y > plot.Bottom)
                    {
                        continue;
                    }

                    using (Brush b = new SolidBrush(Color.FromArgb(0xC0, c)))
                    {
                        g.FillEllipse(b, x - 3f, y - 3f, 6f, 6f);
                    }

                    if (o.RelativeError > 0)
                    {
                        float yLo = mapY(o.MeasuredEfficiency * (1.0 - Math.Min(o.RelativeError, 0.9)));
                        float yHi = mapY(o.MeasuredEfficiency * (1.0 + o.RelativeError));
                        using (Pen p = new Pen(Color.FromArgb(0x90, c)))
                        {
                            g.DrawLine(p, x, Math.Max(yHi, plot.Top), x, Math.Min(yLo, plot.Bottom));
                        }
                    }
                }

                int row = 0;
                foreach (string chain in series)
                {
                    Color c = SeriesColors[series.IndexOf(chain) % SeriesColors.Length];
                    using (Brush b = new SolidBrush(c))
                    {
                        g.FillEllipse(b, plot.Left + 8, plot.Top + 8 + row * 15, 7, 7);
                        g.DrawString(chain, this.Font, b, plot.Left + 20, plot.Top + 3 + row * 15);
                    }

                    row++;
                }
            }
        }

        static void DrawCurve(Graphics g, List<ROIEfficiencyData> curve,
                              Func<double, float> mapX, Func<double, float> mapY,
                              Rectangle plot, Color color, float width, DashStyle dash)
        {
            List<PointF> path = new List<PointF>();
            foreach (ROIEfficiencyData point in curve.Where(p => p.Energy > 0 && p.Efficiency > 0)
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
