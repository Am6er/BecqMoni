using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace BecquerelMonitor.EfficiencyMaker
{
    /// <summary>
    /// Чертёж импортированной геометрии GDML: слева разрез плоскостью через ось
    /// прибора, справа поперечное сечение (вид с торца). Решение Amber
    /// 29.10.2026 вопросником, дословно: «Разрез + вид с торца».
    ///
    /// Тела вращения (polycone, genericPolycone) рисуются заливкой профиля,
    /// параллелепипеды — сечением коробки, сетки tessellated — линиями
    /// пересечения треугольников с плоскостью. Цвет — по веществу; вычитание
    /// (subtraction) закрашивает вычтенное цветом материнского тома.
    /// </summary>
    public sealed class GdmlSketch : Control
    {
        static readonly Color Canvas = Color.FromArgb(0xD6, 0xD2, 0xC4);
        static readonly Color Ink = Color.FromArgb(0x20, 0x20, 0x20);
        static readonly Color Divider = Color.FromArgb(0x90, 0x90, 0x90);

        static readonly Color[] Palette =
        {
            Color.FromArgb(0x35, 0xA5, 0xAD),
            Color.FromArgb(0x8C, 0xEC, 0xEC),
            Color.FromArgb(0x82, 0x90, 0xB0),
            Color.FromArgb(0xE8, 0xEE, 0xF6),
            Color.FromArgb(0xA6, 0xD5, 0xE8),
            Color.FromArgb(0x78, 0x80, 0x8E),
            Color.FromArgb(0xD9, 0xB3, 0x6C),
            Color.FromArgb(0xC9, 0x7B, 0x7B),
            Color.FromArgb(0x9B, 0xBF, 0x7A),
            Color.FromArgb(0xB0, 0x8C, 0xC9),
            Color.FromArgb(0x7F, 0xA8, 0xD9),
            Color.FromArgb(0xD9, 0xA6, 0xC4),
        };

        GdmlGeometry geometry;
        Font labelFont;
        Font smallFont;

        public GdmlSketch()
        {
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            this.BackColor = Canvas;
            this.labelFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            this.smallFont = new Font("Segoe UI", 7.5f);
        }

        public GdmlGeometry Geometry
        {
            get { return this.geometry; }
        }

        public void SetGeometry(GdmlGeometry value)
        {
            this.geometry = value;
            this.Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (this.labelFont != null)
                {
                    this.labelFont.Dispose();
                }

                if (this.smallFont != null)
                {
                    this.smallFont.Dispose();
                }
            }

            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Canvas);

            if (this.geometry == null || !string.IsNullOrEmpty(this.geometry.FatalError)
                || this.geometry.Volumes.Count == 0)
            {
                string text = GdmlText.T("GdmlSketchNoGeometry", "No GDML geometry");
                SizeF size = graphics.MeasureString(text, this.labelFont);
                graphics.DrawString(text, this.labelFont, Brushes.DimGray,
                    (this.ClientSize.Width - size.Width) / 2.0f,
                    (this.ClientSize.Height - size.Height) / 2.0f);
                return;
            }

            Rectangle client = this.ClientRectangle;
            int gap = 16;
            int leftWidth = (client.Width - gap) / 2;
            Rectangle left = new Rectangle(client.Left, client.Top, leftWidth, client.Height);
            Rectangle right = new Rectangle(client.Left + leftWidth + gap, client.Top,
                                            client.Width - leftWidth - gap, client.Height);

            this.DrawView(graphics, left, true);
            this.DrawView(graphics, right, false);

            using (Pen pen = new Pen(Divider, 1.0f))
            {
                graphics.DrawLine(pen, left.Right + gap / 2, 6, left.Right + gap / 2, client.Height - 6);
            }
        }

        void DrawView(Graphics graphics, Rectangle rect, bool axial)
        {
            int normal, u, v;
            ViewAxes(axial, this.geometry.Axis, out normal, out u, out v);
            double plane = axial ? 0.0 : this.geometry.EndPlane;

            ViewFrame frame = this.MakeFrame(rect, u, v);
            if (frame == null)
            {
                return;
            }

            using (Pen border = new Pen(Divider, 1.0f))
            {
                graphics.DrawRectangle(border, rect.Left, rect.Top, rect.Width - 1, rect.Height - 1);
            }

            string title = axial
                ? GdmlText.T("GdmlSketchAxial", "Axial section (y = 0)")
                : string.Format(CultureInfo.InvariantCulture,
                    GdmlText.T("GdmlSketchEnd", "End view ({0} = {1} mm)"),
                    this.geometry.Axis, GdmlText.N(plane));
            graphics.DrawString(title, this.labelFont, Brushes.Black, rect.Left + 8, rect.Top + 6);

            if (axial)
            {
                // Ось прибора: штрихпунктир по нулю радиальной координаты.
                if (frame.VMin <= 0.0 && frame.VMax >= 0.0)
                {
                    PointF a = frame.ToScreen(frame.UMin, 0.0);
                    PointF b = frame.ToScreen(frame.UMax, 0.0);
                    using (Pen axisPen = new Pen(Color.FromArgb(0x60, 0x60, 0x60), 1.0f))
                    {
                        axisPen.DashStyle = DashStyle.DashDot;
                        graphics.DrawLine(axisPen, a, b);
                    }
                }
            }
            else
            {
                PointF center = frame.ToScreen(0.0, 0.0);
                using (Pen centerPen = new Pen(Color.FromArgb(0x60, 0x60, 0x60), 1.0f))
                {
                    centerPen.DashStyle = DashStyle.Dot;
                    graphics.DrawLine(centerPen, center.X - 8, center.Y, center.X + 8, center.Y);
                    graphics.DrawLine(centerPen, center.X, center.Y - 8, center.X, center.Y + 8);
                }
            }

            foreach (GdmlVolumeInstance volume in this.geometry.Volumes)
            {
                this.DrawVolume(graphics, frame, volume, normal, u, v, plane, false);
            }

            this.DrawScale(graphics, frame, rect);
        }

        ViewFrame MakeFrame(Rectangle rect, int u, int v)
        {
            double umin = double.MaxValue, umax = double.MinValue;
            double vmin = double.MaxValue, vmax = double.MinValue;
            foreach (GdmlVolumeInstance volume in this.geometry.Volumes)
            {
                if (!volume.BoundsKnown)
                {
                    continue;
                }

                umin = Math.Min(umin, volume.Min[u]);
                umax = Math.Max(umax, volume.Max[u]);
                vmin = Math.Min(vmin, volume.Min[v]);
                vmax = Math.Max(vmax, volume.Max[v]);
            }

            if (umin > umax || vmin > vmax)
            {
                return null;
            }

            if (umax - umin < 1e-6)
            {
                umax = umin + 1.0;
            }

            if (vmax - vmin < 1e-6)
            {
                vmax = vmin + 1.0;
            }

            return new ViewFrame(rect, umin, umax, vmin, vmax, 16.0f);
        }

        void DrawVolume(Graphics graphics, ViewFrame frame, GdmlVolumeInstance volume,
                        int normal, int u, int v, double plane, bool hole)
        {
            if (volume.Solid == null || volume.Solid.Kind == GdmlSolidKind.Unknown)
            {
                return;
            }

            if (!IntersectsPlane(volume, normal, plane))
            {
                return;
            }

            if (volume.Solid.Kind == GdmlSolidKind.Subtraction)
            {
                if (volume.Solid.First != null)
                {
                    this.DrawSolid(graphics, frame, volume, volume.Solid.First, normal, u, v, plane, hole);
                }

                foreach (GdmlSolid second in volume.Solid.Second)
                {
                    this.DrawSolid(graphics, frame, volume, second, normal, u, v, plane, true);
                }

                return;
            }

            this.DrawSolid(graphics, frame, volume, volume.Solid, normal, u, v, plane, hole);
        }

        void DrawSolid(Graphics graphics, ViewFrame frame, GdmlVolumeInstance volume, GdmlSolid solid,
                       int normal, int u, int v, double plane, bool hole)
        {
            Color color = hole ? this.ParentColor(volume) : MaterialColor(volume.MaterialName);
            using (SolidBrush brush = new SolidBrush(color))
            using (Pen pen = new Pen(Darken(color), hole ? 1.6f : 0.9f))
            {
                switch (solid.Kind)
                {
                    case GdmlSolidKind.Box:
                        this.DrawBox(graphics, frame, volume, solid, normal, u, v, plane, brush, pen);
                        break;
                    case GdmlSolidKind.Polycone:
                    case GdmlSolidKind.GenericPolycone:
                        this.DrawRevolution(graphics, frame, volume, solid, normal, u, v, plane, brush, pen);
                        break;
                    case GdmlSolidKind.Tessellated:
                        this.DrawMesh(graphics, frame, volume, solid, normal, u, v, plane, pen, hole);
                        break;
                }
            }
        }

        static bool IntersectsPlane(GdmlVolumeInstance volume, int normal, double plane)
        {
            if (!volume.BoundsKnown)
            {
                return true;
            }

            double low = volume.Min[normal];
            double high = volume.Max[normal];
            double tolerance = 1e-6 * Math.Max(1.0, Math.Max(Math.Abs(low), Math.Abs(high)));
            return low - tolerance <= plane && plane <= high + tolerance;
        }

        // ------------------------------------------------------------------
        // Коробка
        // ------------------------------------------------------------------

        void DrawBox(Graphics graphics, ViewFrame frame, GdmlVolumeInstance volume, GdmlSolid solid,
                     int normal, int u, int v, double plane, Brush brush, Pen pen)
        {
            double hx = solid.SizeX / 2.0;
            double hy = solid.SizeY / 2.0;
            double hz = solid.SizeZ / 2.0;
            GdmlPoint[] corners = new GdmlPoint[8];
            for (int i = 0; i < 8; i++)
            {
                corners[i] = volume.Transform.Apply(new GdmlPoint(
                    (i & 1) == 0 ? -hx : hx,
                    (i & 2) == 0 ? -hy : hy,
                    (i & 4) == 0 ? -hz : hz));
            }

            double[] distance = new double[8];
            for (int i = 0; i < 8; i++)
            {
                distance[i] = Coordinate(corners[i], normal) - plane;
            }

            List<PointF> points = new List<PointF>();
            for (int i = 0; i < 8; i++)
            {
                if (Math.Abs(distance[i]) < 1e-7)
                {
                    AddPoint(points, frame, corners[i], u, v);
                }
            }

            int[,] edges =
            {
                { 0, 1 }, { 0, 2 }, { 0, 4 }, { 1, 3 }, { 1, 5 }, { 2, 3 },
                { 2, 6 }, { 3, 7 }, { 4, 5 }, { 4, 6 }, { 5, 7 }, { 6, 7 },
            };
            for (int i = 0; i < edges.GetLength(0); i++)
            {
                int a = edges[i, 0];
                int b = edges[i, 1];
                if (distance[a] * distance[b] < 0.0)
                {
                    double t = distance[a] / (distance[a] - distance[b]);
                    GdmlPoint point = new GdmlPoint(
                        corners[a].X + t * (corners[b].X - corners[a].X),
                        corners[a].Y + t * (corners[b].Y - corners[a].Y),
                        corners[a].Z + t * (corners[b].Z - corners[a].Z));
                    AddPoint(points, frame, point, u, v);
                }
            }

            FillPolygon(graphics, points, brush, pen);
        }

        // ------------------------------------------------------------------
        // Тела вращения
        // ------------------------------------------------------------------

        void DrawRevolution(Graphics graphics, ViewFrame frame, GdmlVolumeInstance volume, GdmlSolid solid,
                            int normal, int u, int v, double plane, Brush brush, Pen pen)
        {
            // Ось тел вращения — z. Осевой разрез совпадает с плоскостью y = 0,
            // поэтому профиль (r, z) и его зеркало и есть сечение.
            if (normal == 1)
            {
                if (solid.Kind == GdmlSolidKind.Polycone)
                {
                    for (int i = 0; i + 1 < solid.Planes.Count; i++)
                    {
                        double[] a = solid.Planes[i];
                        double[] b = solid.Planes[i + 1];
                        if (Math.Abs(a[0] - b[0]) < 1e-9)
                        {
                            continue;
                        }

                        DrawProfile(graphics, frame, volume, brush, pen, u, v, new[]
                        {
                            new[] { a[1], a[0] }, new[] { a[2], a[0] },
                            new[] { b[2], b[0] }, new[] { b[1], b[0] },
                        });
                        DrawProfile(graphics, frame, volume, brush, pen, u, v, new[]
                        {
                            new[] { -a[1], a[0] }, new[] { -a[2], a[0] },
                            new[] { -b[2], b[0] }, new[] { -b[1], b[0] },
                        });
                    }
                }
                else
                {
                    DrawProfile(graphics, frame, volume, brush, pen, u, v, ToArray(solid.Profile, 1.0));
                    DrawProfile(graphics, frame, volume, brush, pen, u, v, ToArray(solid.Profile, -1.0));
                }

                return;
            }

            // Поперечное сечение: кольца/диски на плоскости z = plane.
            GdmlPoint origin = volume.Transform.Apply(new GdmlPoint(0, 0, 0));
            double localZ = plane - origin.Z;
            if (solid.Kind == GdmlSolidKind.Polycone)
            {
                for (int i = 0; i + 1 < solid.Planes.Count; i++)
                {
                    double[] a = solid.Planes[i];
                    double[] b = solid.Planes[i + 1];
                    if (!Between(localZ, a[0], b[0]))
                    {
                        continue;
                    }

                    double rmin, rmax;
                    Interpolate(a[0], a[1], a[2], b[0], b[1], b[2], localZ, out rmin, out rmax);
                    DrawAnnulus(graphics, frame, origin, rmin, rmax, brush, pen);
                }
            }
            else
            {
                List<double> radii = new List<double>();
                for (int i = 0; i < solid.Profile.Count; i++)
                {
                    double[] a = solid.Profile[i];
                    double[] b = solid.Profile[(i + 1) % solid.Profile.Count];
                    if (Between(localZ, a[1], b[1]) && Math.Abs(b[1] - a[1]) > 1e-12)
                    {
                        double t = (localZ - a[1]) / (b[1] - a[1]);
                        if (t >= 0.0 && t <= 1.0)
                        {
                            radii.Add(a[0] + t * (b[0] - a[0]));
                        }
                    }
                }

                radii.Sort();
                for (int i = 0; i + 1 < radii.Count; i += 2)
                {
                    DrawAnnulus(graphics, frame, origin, radii[i], radii[i + 1], brush, pen);
                }
            }
        }

        static double[][] ToArray(List<double[]> profile, double sign)
        {
            double[][] result = new double[profile.Count][];
            for (int i = 0; i < profile.Count; i++)
            {
                result[i] = new[] { profile[i][0] * sign, profile[i][1] };
            }

            return result;
        }

        void DrawProfile(Graphics graphics, ViewFrame frame, GdmlVolumeInstance volume,
                         Brush brush, Pen pen, int u, int v, double[][] profile)
        {
            if (profile == null || profile.Length < 3)
            {
                return;
            }

            List<PointF> points = new List<PointF>();
            foreach (double[] point in profile)
            {
                // Профиль (r, z) — точка (r, 0, z) в системе тома.
                GdmlPoint world = volume.Transform.Apply(new GdmlPoint(point[0], 0.0, point[1]));
                points.Add(frame.ToScreen(Coordinate(world, u), Coordinate(world, v)));
            }

            FillPolygon(graphics, points, brush, pen);
        }

        void DrawAnnulus(Graphics graphics, ViewFrame frame, GdmlPoint origin,
                         double rmin, double rmax, Brush brush, Pen pen)
        {
            if (rmax <= 0.0)
            {
                return;
            }

            PointF center = frame.ToScreen(origin.X, origin.Y);
            float outer = (float)(rmax * frame.Scale);
            float inner = (float)(Math.Max(0.0, rmin) * frame.Scale);
            if (outer < 0.5f)
            {
                return;
            }

            RectangleF outerRect = new RectangleF(center.X - outer, center.Y - outer, outer * 2.0f, outer * 2.0f);
            if (inner <= 0.5f)
            {
                graphics.FillEllipse(brush, outerRect);
                graphics.DrawEllipse(pen, outerRect);
                return;
            }

            RectangleF innerRect = new RectangleF(center.X - inner, center.Y - inner, inner * 2.0f, inner * 2.0f);
            using (GraphicsPath path = new GraphicsPath())
            {
                path.FillMode = FillMode.Alternate;
                path.AddEllipse(outerRect);
                path.AddEllipse(innerRect);
                graphics.FillPath(brush, path);
                graphics.DrawEllipse(pen, outerRect);
                graphics.DrawEllipse(pen, innerRect);
            }
        }

        static bool Between(double value, double a, double b)
        {
            return value >= Math.Min(a, b) && value <= Math.Max(a, b);
        }

        static void Interpolate(double z1, double rmin1, double rmax1,
                                double z2, double rmin2, double rmax2, double z,
                                out double rmin, out double rmax)
        {
            if (Math.Abs(z2 - z1) < 1e-12)
            {
                rmin = rmin1;
                rmax = rmax1;
                return;
            }

            double t = (z - z1) / (z2 - z1);
            rmin = rmin1 + t * (rmin2 - rmin1);
            rmax = rmax1 + t * (rmax2 - rmax1);
        }

        // ------------------------------------------------------------------
        // Сетка tessellated
        // ------------------------------------------------------------------

        void DrawMesh(Graphics graphics, ViewFrame frame, GdmlVolumeInstance volume, GdmlSolid solid,
                      int normal, int u, int v, double plane, Pen pen, bool hole)
        {
            if (solid.Triangles.Count < 3 || solid.Vertices.Count == 0)
            {
                return;
            }

            GraphicsPath path = new GraphicsPath();
            double[] distance = new double[3];
            GdmlPoint[] world = new GdmlPoint[3];

            for (int i = 0; i + 2 < solid.Triangles.Count; i += 3)
            {
                for (int k = 0; k < 3; k++)
                {
                    world[k] = volume.Transform.Apply(solid.Vertices[solid.Triangles[i + k]]);
                    distance[k] = Coordinate(world[k], normal) - plane;
                }

                if ((distance[0] > 0.0 && distance[1] > 0.0 && distance[2] > 0.0)
                    || (distance[0] < 0.0 && distance[1] < 0.0 && distance[2] < 0.0))
                {
                    continue;
                }

                List<PointF> points = new List<PointF>();
                for (int k = 0; k < 3; k++)
                {
                    int next = (k + 1) % 3;
                    if (Math.Abs(distance[k]) < 1e-9)
                    {
                        AddProjected(points, frame, world[k], u, v);
                    }

                    if (distance[k] * distance[next] < 0.0)
                    {
                        double t = distance[k] / (distance[k] - distance[next]);
                        GdmlPoint point = new GdmlPoint(
                            world[k].X + t * (world[next].X - world[k].X),
                            world[k].Y + t * (world[next].Y - world[k].Y),
                            world[k].Z + t * (world[next].Z - world[k].Z));
                        AddProjected(points, frame, point, u, v);
                    }
                }

                if (points.Count >= 2)
                {
                    path.AddLine(points[0], points[1]);
                }
            }

            if (path.PointCount == 0)
            {
                path.Dispose();
                return;
            }

            if (hole)
            {
                using (Pen holePen = new Pen(this.ParentColor(volume), 2.4f))
                {
                    graphics.DrawPath(holePen, path);
                }
            }
            else
            {
                graphics.DrawPath(pen, path);
            }

            path.Dispose();
        }

        // ------------------------------------------------------------------
        // Мелкие помощники рисования
        // ------------------------------------------------------------------

        static int AxisIndex(char axis)
        {
            return axis == 'x' ? 0 : axis == 'y' ? 1 : 2;
        }

        static void ViewAxes(bool axial, char axis, out int normal, out int u, out int v)
        {
            if (axial)
            {
                normal = 1; // плоскость y = 0 содержит ось z и ось x
                if (axis == 'z')
                {
                    u = 2;
                    v = 0;
                }
                else if (axis == 'x')
                {
                    u = 0;
                    v = 2;
                }
                else
                {
                    u = 1;
                    v = 0;
                }
            }
            else
            {
                normal = AxisIndex(axis);
                if (axis == 'z')
                {
                    u = 0;
                    v = 1;
                }
                else if (axis == 'x')
                {
                    u = 1;
                    v = 2;
                }
                else
                {
                    u = 0;
                    v = 2;
                }
            }
        }

        static double Coordinate(GdmlPoint point, int index)
        {
            return index == 0 ? point.X : index == 1 ? point.Y : point.Z;
        }

        static void AddPoint(List<PointF> points, ViewFrame frame, GdmlPoint point, int u, int v)
        {
            AddProjected(points, frame, point, u, v);
        }

        static void AddProjected(List<PointF> points, ViewFrame frame, GdmlPoint point, int u, int v)
        {
            PointF screen = frame.ToScreen(Coordinate(point, u), Coordinate(point, v));
            foreach (PointF existing in points)
            {
                if (Math.Abs(existing.X - screen.X) < 0.01f && Math.Abs(existing.Y - screen.Y) < 0.01f)
                {
                    return;
                }
            }

            points.Add(screen);
        }

        static void FillPolygon(Graphics graphics, List<PointF> points, Brush brush, Pen pen)
        {
            if (points.Count < 3)
            {
                return;
            }

            double cx = 0.0, cy = 0.0;
            foreach (PointF point in points)
            {
                cx += point.X;
                cy += point.Y;
            }

            cx /= points.Count;
            cy /= points.Count;

            List<SectionPoint> ordered = new List<SectionPoint>();
            foreach (PointF point in points)
            {
                ordered.Add(new SectionPoint
                {
                    Point = point,
                    Angle = Math.Atan2(point.Y - cy, point.X - cx),
                });
            }

            ordered.Sort((a, b) => a.Angle.CompareTo(b.Angle));
            PointF[] polygon = new PointF[ordered.Count];
            for (int i = 0; i < ordered.Count; i++)
            {
                polygon[i] = ordered[i].Point;
            }

            graphics.FillPolygon(brush, polygon);
            graphics.DrawPolygon(pen, polygon);
        }

        struct SectionPoint
        {
            public PointF Point;
            public double Angle;
        }

        static Color MaterialColor(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return Color.FromArgb(0x90, 0x90, 0x90);
            }

            uint hash = 2166136261;
            foreach (char c in name)
            {
                hash ^= c;
                hash *= 16777619;
            }

            return Palette[(int)(hash % (uint)Palette.Length)];
        }

        Color ParentColor(GdmlVolumeInstance volume)
        {
            return volume != null && volume.Parent != null
                ? MaterialColor(volume.Parent.MaterialName)
                : Canvas;
        }

        static Color Darken(Color color)
        {
            return Color.FromArgb(color.A, color.R * 2 / 3, color.G * 2 / 3, color.B * 2 / 3);
        }

        void DrawScale(Graphics graphics, ViewFrame frame, Rectangle rect)
        {
            double width = frame.UMax - frame.UMin;
            double[] steps = { 1.0, 2.0, 5.0, 10.0, 20.0, 50.0, 100.0, 200.0, 500.0, 1000.0 };
            double length = steps[0];
            foreach (double step in steps)
            {
                if (step <= width / 4.0)
                {
                    length = step;
                }
            }

            float pixels = (float)(length * frame.Scale);
            float x = rect.Left + 12.0f;
            float y = rect.Bottom - 18.0f;
            using (Pen pen = new Pen(Ink, 1.5f))
            {
                graphics.DrawLine(pen, x, y, x + pixels, y);
                graphics.DrawLine(pen, x, y - 4, x, y + 4);
                graphics.DrawLine(pen, x + pixels, y - 4, x + pixels, y + 4);
            }

            graphics.DrawString(GdmlText.N(length) + " mm", this.smallFont, Brushes.Black, x, y - 16.0f);
        }

        /// <summary>Рамка вида: миллиметры → точки экрана, с сохранением пропорций.</summary>
        sealed class ViewFrame
        {
            public readonly double UMin, UMax, VMin, VMax;
            public readonly float Scale;

            readonly float left;
            readonly float top;
            readonly float width;
            readonly float height;

            public ViewFrame(Rectangle rect, double umin, double umax, double vmin, double vmax, float margin)
            {
                this.UMin = umin;
                this.UMax = umax;
                this.VMin = vmin;
                this.VMax = vmax;

                double du = umax - umin;
                double dv = vmax - vmin;
                float sx = (rect.Width - 2.0f * margin) / (float)du;
                float sy = (rect.Height - 2.0f * margin) / (float)dv;
                this.Scale = Math.Min(sx, sy);
                this.width = (float)du * this.Scale;
                this.height = (float)dv * this.Scale;
                this.left = rect.Left + (rect.Width - this.width) / 2.0f;
                this.top = rect.Top + (rect.Height - this.height) / 2.0f;
            }

            public PointF ToScreen(double u, double v)
            {
                return new PointF(
                    this.left + (float)((u - this.UMin) * this.Scale),
                    this.top + (float)((this.VMax - v) * this.Scale));
            }
        }
    }
}
