using System;
using System.Collections.Generic;
using System.Globalization;

namespace BecquerelMonitor.EfficiencyMaker
{
    /// <summary>Отрезок луча внутри одного тома: расстояния от начала луча, мм. Том null — мир (ничего нет).</summary>
    public sealed class GdmlRaySegment
    {
        public GdmlVolumeInstance Volume;
        public double From, To;

        public double Length
        {
            get { return this.To - this.From; }
        }
    }

    /// <summary>
    /// (`AMBER322`) «Что лежит вдоль прямой» в геометрии GDML. Читатель знает габариты и объёмы, чертёж — сечение
    /// плоскостью; толщину слоя по ним не снять: оболочка-вычитание имеет габарит наружного короба. Здесь луч режется
    /// каждым телом на отрезки «внутри»: короб — точно, сетка — пересечением с треугольниками, тела вращения — шагом
    /// с уточнением границы делением пополам, вычитание и объединение — действиями над отрезками. В точке побеждает
    /// самый глубокий том дерева, при равной глубине — поздний (как рисует чертёж).
    /// </summary>
    public sealed class GdmlLocator
    {
        /// <summary>Шаг поиска границ тел вращения, мм: слой тоньше него между двумя пробами может быть пропущен.</summary>
        public const double MarchStep = 0.01;

        const double Edge = 1e-7;

        readonly GdmlGeometry geometry;
        readonly Dictionary<GdmlVolumeInstance, Frame> frames = new Dictionary<GdmlVolumeInstance, Frame>();

        /// <summary>Сетки, у которых луч дал нечётное число пересечений (незамкнутая поверхность): на луче их нет.</summary>
        public readonly HashSet<string> OpenMeshes = new HashSet<string>(StringComparer.Ordinal);

        public GdmlLocator(GdmlGeometry geometry)
        {
            this.geometry = geometry;
        }

        sealed class Frame
        {
            readonly double[] o = new double[3];
            readonly double[][] e = { new double[3], new double[3], new double[3] };

            public Frame(GdmlTransform t)
            {
                GdmlPoint zero = t.Apply(new GdmlPoint(0, 0, 0));
                GdmlPoint[] ends = { t.Apply(new GdmlPoint(1, 0, 0)), t.Apply(new GdmlPoint(0, 1, 0)), t.Apply(new GdmlPoint(0, 0, 1)) };
                this.o[0] = zero.X; this.o[1] = zero.Y; this.o[2] = zero.Z;
                for (int i = 0; i < 3; i++)
                {
                    this.e[i][0] = ends[i].X - zero.X; this.e[i][1] = ends[i].Y - zero.Y; this.e[i][2] = ends[i].Z - zero.Z;
                }
            }

            /// <summary>Точка мира в системе тела (поворот ортогонален: обратный — транспонированный).</summary>
            public double[] Point(double[] w)
            {
                double[] d = { w[0] - this.o[0], w[1] - this.o[1], w[2] - this.o[2] };
                return this.Direction(d);
            }

            public double[] Direction(double[] d)
            {
                return new[] { Dot(this.e[0], d), Dot(this.e[1], d), Dot(this.e[2], d) };
            }
        }

        static double Dot(double[] a, double[] b)
        {
            return a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
        }

        Frame FrameOf(GdmlVolumeInstance volume)
        {
            Frame frame;
            if (!this.frames.TryGetValue(volume, out frame))
            {
                frame = new Frame(volume.Transform ?? GdmlTransform.Identity());
                this.frames[volume] = frame;
            }

            return frame;
        }

        /// <summary>Отрезки луча по томам, от начала к концу; пустоты между томами — отрезки с томом null.</summary>
        public List<GdmlRaySegment> Ray(double[] origin, double[] direction, double length)
        {
            List<GdmlRaySegment> result = new List<GdmlRaySegment>();
            if (!(length > 0.0))
            {
                return result;
            }

            List<KeyValuePair<GdmlVolumeInstance, List<double>>> inside = new List<KeyValuePair<GdmlVolumeInstance, List<double>>>();
            List<double> cuts = new List<double> { 0.0, length };
            foreach (GdmlVolumeInstance volume in this.geometry.Volumes)
            {
                if (volume == null || volume.Solid == null || !volume.BoundsKnown || !HitsBox(origin, direction, length, volume.Min, volume.Max))
                {
                    continue;
                }

                Frame frame = this.FrameOf(volume);
                List<double> spans = this.Spans(volume.Solid, frame.Point(origin), frame.Direction(direction), length, 0);
                if (spans.Count > 0)
                {
                    inside.Add(new KeyValuePair<GdmlVolumeInstance, List<double>>(volume, spans));
                    cuts.AddRange(spans);
                }
            }

            cuts.Sort();
            for (int i = 0; i + 1 < cuts.Count; i++)
            {
                double a = cuts[i], b = cuts[i + 1];
                if (b - a < Edge)
                {
                    continue;
                }

                double mid = 0.5 * (a + b);
                GdmlVolumeInstance best = null;
                foreach (KeyValuePair<GdmlVolumeInstance, List<double>> pair in inside)
                {
                    if (Covers(pair.Value, mid) && (best == null || pair.Key.Depth >= best.Depth))
                    {
                        best = pair.Key;
                    }
                }

                GdmlRaySegment last = result.Count > 0 ? result[result.Count - 1] : null;
                if (last != null && ReferenceEquals(last.Volume, best))
                {
                    last.To = b;
                }
                else
                {
                    result.Add(new GdmlRaySegment { Volume = best, From = a, To = b });
                }
            }

            return result;
        }

        static bool Covers(List<double> spans, double t)
        {
            for (int i = 0; i + 1 < spans.Count; i += 2)
            {
                if (t >= spans[i] && t <= spans[i + 1])
                {
                    return true;
                }
            }

            return false;
        }

        static bool HitsBox(double[] o, double[] d, double length, double[] min, double[] max)
        {
            double a = 0.0, b = length;
            return Slab(o, d, min, max, ref a, ref b);
        }

        static bool Slab(double[] o, double[] d, double[] min, double[] max, ref double a, ref double b)
        {
            for (int i = 0; i < 3; i++)
            {
                if (Math.Abs(d[i]) < 1e-12)
                {
                    if (o[i] < min[i] - Edge || o[i] > max[i] + Edge)
                    {
                        return false;
                    }

                    continue;
                }

                double t0 = (min[i] - o[i]) / d[i], t1 = (max[i] - o[i]) / d[i];
                if (t0 > t1)
                {
                    double swap = t0; t0 = t1; t1 = swap;
                }

                a = Math.Max(a, t0);
                b = Math.Min(b, t1);
                if (a > b)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Отрезки луча внутри тела, в его системе: пары «вход, выход» по возрастанию.</summary>
        List<double> Spans(GdmlSolid solid, double[] o, double[] d, double length, int depth)
        {
            List<double> none = new List<double>();
            if (solid == null || depth > GdmlGeometry.MaxSolidDepth)
            {
                return none;
            }

            switch (solid.Kind)
            {
                case GdmlSolidKind.Box:
                    {
                        double a = 0.0, b = length;
                        double[] max = { 0.5 * solid.SizeX, 0.5 * solid.SizeY, 0.5 * solid.SizeZ };
                        double[] min = { -max[0], -max[1], -max[2] };
                        return Slab(o, d, min, max, ref a, ref b) && b - a > Edge ? new List<double> { a, b } : none;
                    }

                case GdmlSolidKind.Polycone:
                case GdmlSolidKind.GenericPolycone:
                    return SpansOfRevolution(solid, o, d, length);

                case GdmlSolidKind.Tessellated:
                    return this.SpansOfMesh(solid, o, d, length);

                case GdmlSolidKind.Subtraction:
                case GdmlSolidKind.Union:
                    {
                        List<double> spans = this.Placed(solid.First, solid.FirstPlacement, o, d, length, depth);
                        for (int i = 0; i < solid.Second.Count; i++)
                        {
                            GdmlTransform at = i < solid.SecondPlacements.Count ? solid.SecondPlacements[i] : null;
                            List<double> other = this.Placed(solid.Second[i], at, o, d, length, depth);
                            spans = solid.Kind == GdmlSolidKind.Union ? Unite(spans, other) : Subtract(spans, other);
                        }

                        return spans;
                    }

                default:
                    return none;
            }
        }

        List<double> Placed(GdmlSolid solid, GdmlTransform placement, double[] o, double[] d, double length, int depth)
        {
            if (placement == null)
            {
                return this.Spans(solid, o, d, length, depth + 1);
            }

            Frame frame = new Frame(placement);
            return this.Spans(solid, frame.Point(o), frame.Direction(d), length, depth + 1);
        }

        static List<double> Subtract(List<double> a, List<double> b)
        {
            List<double> result = new List<double>();
            for (int i = 0; i + 1 < a.Count; i += 2)
            {
                double from = a[i], to = a[i + 1];
                for (int j = 0; j + 1 < b.Count && from < to; j += 2)
                {
                    if (b[j + 1] <= from || b[j] >= to)
                    {
                        continue;
                    }

                    if (b[j] - from > Edge)
                    {
                        result.Add(from);
                        result.Add(b[j]);
                    }

                    from = Math.Max(from, b[j + 1]);
                }

                if (to - from > Edge)
                {
                    result.Add(from);
                    result.Add(to);
                }
            }

            return result;
        }

        static List<double> Unite(List<double> a, List<double> b)
        {
            List<double[]> all = new List<double[]>();
            for (int i = 0; i + 1 < a.Count; i += 2) all.Add(new[] { a[i], a[i + 1] });
            for (int i = 0; i + 1 < b.Count; i += 2) all.Add(new[] { b[i], b[i + 1] });
            all.Sort((x, y) => x[0].CompareTo(y[0]));
            List<double> result = new List<double>();
            foreach (double[] span in all)
            {
                if (result.Count > 0 && span[0] <= result[result.Count - 1] + Edge)
                {
                    result[result.Count - 1] = Math.Max(result[result.Count - 1], span[1]);
                }
                else
                {
                    result.Add(span[0]);
                    result.Add(span[1]);
                }
            }

            return result;
        }

        static bool InRevolution(GdmlSolid solid, double x, double y, double z)
        {
            double r = Math.Sqrt(x * x + y * y);
            if (solid.DeltaPhi < 360.0 - 1e-6)
            {
                double phi = Math.Atan2(y, x) * 180.0 / Math.PI - solid.StartPhi;
                phi -= 360.0 * Math.Floor(phi / 360.0);
                if (phi > solid.DeltaPhi)
                {
                    return false;
                }
            }

            if (solid.Kind == GdmlSolidKind.Polycone)
            {
                for (int i = 0; i + 1 < solid.Planes.Count; i++)
                {
                    double[] a = solid.Planes[i], b = solid.Planes[i + 1];
                    double z0 = Math.Min(a[0], b[0]), z1 = Math.Max(a[0], b[0]);
                    if (z < z0 || z > z1 || !(z1 - z0 > 0.0))
                    {
                        continue;
                    }

                    double f = (z - a[0]) / (b[0] - a[0]);
                    if (r >= a[1] + f * (b[1] - a[1]) && r <= a[2] + f * (b[2] - a[2]))
                    {
                        return true;
                    }
                }

                return false;
            }

            // genericPolycone: замкнутый профиль (r, z), правило чёт-нечет.
            bool inside = false;
            int n = solid.Profile.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double ri = solid.Profile[i][0], zi = solid.Profile[i][1], rj = solid.Profile[j][0], zj = solid.Profile[j][1];
                if ((zi > z) != (zj > z) && r < (rj - ri) * (z - zi) / (zj - zi) + ri)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        static List<double> SpansOfRevolution(GdmlSolid solid, double[] o, double[] d, double length)
        {
            List<double> result = new List<double>();
            double rmax = 0.0, zmin = double.MaxValue, zmax = double.MinValue;
            if (solid.Kind == GdmlSolidKind.Polycone)
            {
                foreach (double[] plane in solid.Planes)
                {
                    rmax = Math.Max(rmax, plane[2]);
                    zmin = Math.Min(zmin, plane[0]);
                    zmax = Math.Max(zmax, plane[0]);
                }
            }
            else
            {
                foreach (double[] point in solid.Profile)
                {
                    rmax = Math.Max(rmax, Math.Abs(point[0]));
                    zmin = Math.Min(zmin, point[1]);
                    zmax = Math.Max(zmax, point[1]);
                }
            }

            double a = 0.0, b = length;
            if (!(rmax > 0.0) || !(zmax > zmin)
                || !Slab(o, d, new[] { -rmax, -rmax, zmin }, new[] { rmax, rmax, zmax }, ref a, ref b) || !(b - a > Edge))
            {
                return result;
            }

            Func<double, bool> at = t => InRevolution(solid, o[0] + t * d[0], o[1] + t * d[1], o[2] + t * d[2]);
            int steps = (int)Math.Min(2000000.0, Math.Max(1.0, Math.Ceiling((b - a) / MarchStep)));
            double step = (b - a) / steps;
            double previousT = a + 1e-9;
            bool previous = at(previousT);
            double entered = previous ? a : double.NaN;
            for (int i = 1; i <= steps; i++)
            {
                double t = i == steps ? b - 1e-9 : a + i * step;
                bool now = at(t);
                if (now != previous)
                {
                    double lo = previousT, hi = t;
                    for (int k = 0; k < 40 && hi - lo > 1e-9; k++)
                    {
                        double mid = 0.5 * (lo + hi);
                        if (at(mid) == previous) lo = mid; else hi = mid;
                    }

                    double cross = 0.5 * (lo + hi);
                    if (now)
                    {
                        entered = cross;
                    }
                    else
                    {
                        result.Add(entered);
                        result.Add(cross);
                    }
                }

                previous = now;
                previousT = t;
            }

            if (previous)
            {
                result.Add(entered);
                result.Add(b);
            }

            return result;
        }

        List<double> SpansOfMesh(GdmlSolid solid, double[] o, double[] d, double length)
        {
            // Луч через ребро или вершину сетки считается дважды; оси CAD-моделей проходят ровно через них.
            // Сдвиг начала на доли микрона поперёк луча снимает совпадение; три попытки разными сдвигами.
            double[] u = Math.Abs(d[0]) < 0.9 ? new[] { 1.0, 0.0, 0.0 } : new[] { 0.0, 1.0, 0.0 };
            double[] p = Cross(d, u), q = Cross(d, p);
            double[][] shifts = { new[] { 1.234e-4, 2.718e-4 }, new[] { -3.141e-4, 1.618e-4 }, new[] { 2.236e-4, -1.732e-4 } };
            foreach (double[] shift in shifts)
            {
                double[] from =
                {
                    o[0] + shift[0] * p[0] + shift[1] * q[0], o[1] + shift[0] * p[1] + shift[1] * q[1], o[2] + shift[0] * p[2] + shift[1] * q[2],
                };
                List<double> hits = new List<double>();
                for (int i = 0; i + 2 < solid.Triangles.Count; i += 3)
                {
                    double t;
                    if (HitTriangle(from, d, solid.Vertices[solid.Triangles[i]], solid.Vertices[solid.Triangles[i + 1]],
                                    solid.Vertices[solid.Triangles[i + 2]], out t))
                    {
                        hits.Add(t);
                    }
                }

                // Вся прямая, а не луч: начало может лежать внутри тела, и чётность верна только на целой прямой.
                if (hits.Count % 2 == 0)
                {
                    hits.Sort();
                    List<double> spans = new List<double>();
                    for (int i = 0; i + 1 < hits.Count; i += 2)
                    {
                        double a = Math.Max(0.0, hits[i]), b = Math.Min(length, hits[i + 1]);
                        if (b - a > Edge)
                        {
                            spans.Add(a);
                            spans.Add(b);
                        }
                    }

                    return spans;
                }
            }

            this.OpenMeshes.Add(solid.Name);
            return new List<double>();
        }

        static double[] Cross(double[] a, double[] b)
        {
            return new[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };
        }

        /// <summary>Пересечение прямой с треугольником (Мёллер — Трумбор); t любого знака.</summary>
        static bool HitTriangle(double[] o, double[] d, GdmlPoint a, GdmlPoint b, GdmlPoint c, out double t)
        {
            t = 0.0;
            double[] e1 = { b.X - a.X, b.Y - a.Y, b.Z - a.Z }, e2 = { c.X - a.X, c.Y - a.Y, c.Z - a.Z };
            double[] h = Cross(d, e2);
            double det = Dot(e1, h);
            if (Math.Abs(det) < 1e-14)
            {
                return false;
            }

            double[] s = { o[0] - a.X, o[1] - a.Y, o[2] - a.Z };
            double u = Dot(s, h) / det;
            if (u < 0.0 || u > 1.0)
            {
                return false;
            }

            double[] q = Cross(s, e1);
            double v = Dot(d, q) / det;
            if (v < 0.0 || u + v > 1.0)
            {
                return false;
            }

            t = Dot(e2, q) / det;
            return true;
        }
    }
}
