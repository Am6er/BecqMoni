using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace BecquerelMonitor.EfficiencyMaker
{
    // Slider positions use GDML world coordinates, before the common centring transform.
    internal sealed class GdmlSectionScene
    {
        internal sealed class Axis
        {
            internal readonly Vector3D Direction;
            internal readonly double Start, End;

            internal Axis(Vector3D direction, double start, double end)
            {
                Direction = direction;
                Start = start;
                End = end;
            }

            internal Plane At(double fraction)
            {
                return new Plane(Direction, Start + (End - Start) * fraction);
            }
        }

        internal sealed class Plane
        {
            internal readonly Vector3D Normal;
            internal readonly double Offset;

            internal Plane(Vector3D normal, double offset)
            {
                Normal = normal;
                Offset = offset;
            }
        }

        sealed class Mesh
        {
            internal readonly List<Point3D> Points = new List<Point3D>();
            internal readonly List<int> Indices = new List<int>();

            internal void Append(Mesh other)
            {
                int shift = Points.Count;
                Points.AddRange(other.Points);
                foreach (int index in other.Indices) Indices.Add(shift + index);
            }

            // Import only referenced vertices. Some CAD meshes also contain construction points.
            internal static Mesh From(MeshGeometry3D input)
            {
                var mesh = new Mesh();
                var mapped = new int[input.Positions.Count];
                foreach (int i in input.TriangleIndices)
                {
                    if (mapped[i] == 0)
                    {
                        mesh.Points.Add(input.Positions[i]);
                        mapped[i] = mesh.Points.Count;
                    }
                    mesh.Indices.Add(mapped[i] - 1);
                }
                return mesh;
            }

            internal GeometryModel3D Model(GeometryModel3D source)
            {
                var geometry = new MeshGeometry3D
                {
                    Positions = new Point3DCollection(Points),
                    TriangleIndices = new Int32Collection(Indices)
                };
                var model = new GeometryModel3D(geometry, source.Material) { BackMaterial = source.BackMaterial, Transform = source.Transform };
                object volume = source.GetValue(GdmlPartInspector.VolumeProperty);
                if (volume != null) model.SetValue(GdmlPartInspector.VolumeProperty, volume);
                return model;
            }
        }

        sealed class Part
        {
            internal GeometryModel3D Display, SolidModel;
            internal Mesh Mesh, Skin;
            internal bool Closed;
            internal bool Visible;
            internal GdmlVolumeInstance Volume;
            internal Matrix3D Placement, Inverse;

            // Pull the world plane back to local mesh coordinates (WPF row-vector convention).
            internal Vector3D LocalNormal(Vector3D n)
            {
                return new Vector3D(Placement.M11*n.X + Placement.M12*n.Y + Placement.M13*n.Z,
                    Placement.M21*n.X + Placement.M22*n.Y + Placement.M23*n.Z,
                    Placement.M31*n.X + Placement.M32*n.Y + Placement.M33*n.Z);
            }

            internal double Shift(Vector3D n)
            {
                return n.X*Placement.OffsetX + n.Y*Placement.OffsetY + n.Z*Placement.OffsetZ;
            }

            internal Plane LocalPlane(Plane world)
            {
                if (Placement.IsIdentity) return world;
                Vector3D n = LocalNormal(world.Normal);
                double length = n.Length;
                return new Plane(n / length, (world.Offset - Shift(world.Normal)) / length);
            }
        }

        readonly List<Part> parts = new List<Part>();
        readonly Dictionary<MeshGeometry3D, Mesh> shapes = new Dictionary<MeshGeometry3D, Mesh>();

        internal void Add(GeometryModel3D model, bool closed, bool visible = true)
        {
            var shape = (MeshGeometry3D)model.Geometry;
            Mesh mesh;
            if (!shapes.TryGetValue(shape, out mesh))
            {
                mesh = Mesh.From(shape);
                shapes.Add(shape, mesh);
            }
            Matrix3D placement = model.Transform.Value, inverse = placement;
            inverse.Invert();
            parts.Add(new Part { Display = model, SolidModel = model, Mesh = mesh, Placement = placement, Inverse = inverse,
                Volume = (GdmlVolumeInstance)model.GetValue(GdmlPartInspector.VolumeProperty), Closed = closed, Visible = visible });
        }

        internal Axis Range(Vector3D direction)
        {
            double min = double.PositiveInfinity, max = double.NegativeInfinity;
            foreach (Part part in parts)
            {
                if (!part.Visible) continue;
                Vector3D local = part.LocalNormal(direction);
                double shift = part.Shift(direction);
                foreach (Point3D p in part.Mesh.Points)
                {
                    double t = Vector3D.DotProduct(local, (Vector3D)p) + shift;
                    min = Math.Min(min, t);
                    max = Math.Max(max, t);
                }
            }
            return new Axis(direction, min, max);
        }

        internal void BuildVisibleParts(Model3DGroup group, CancellationToken cancel)
        {
            var children = new Dictionary<GdmlVolumeInstance, List<Part>>();
            foreach (Part part in parts)
            {
                var parent = part.Volume.Parent;
                if (parent == null) continue;
                List<Part> siblings;
                if (!children.TryGetValue(parent, out siblings))
                    children.Add(parent, siblings = new List<Part>());
                siblings.Add(part);
            }
            // A nested solid can have its own open port. Its finished skin tells
            // the surrounding material which parts of their contact it supplies.
            var mothers = parts.FindAll(p => p.Visible && p.Closed && children.ContainsKey(p.Volume));
            mothers.Sort((a,b) => b.Volume.Depth.CompareTo(a.Volume.Depth));
            foreach (Part mother in mothers)
            {
                cancel.ThrowIfCancellationRequested();
                var daughters = children[mother.Volume];
                var cavities = daughters.FindAll(p => !p.Visible && p.Closed);
                if (cavities.Count == 0) continue;
                var skin = new GdmlCavitySurface(mother.Inverse, cancel);
                skin.Add(mother.Display, cavities.ConvertAll(p => p.Display), false);
                foreach (Part cavity in cavities)
                {
                    var contacts = new List<GeometryModel3D> { mother.Display };
                    // A sibling owns the entire other side of this interface,
                    // including any open port in its own material.
                    foreach (Part sibling in daughters)
                        if (sibling != cavity) contacts.Add(sibling.SolidModel);
                    // Air nested in air has no contact surface of its own. Only a
                    // visible descendant can replace material on the cavity boundary.
                    var pending = new Stack<Part>();
                    pending.Push(cavity);
                    while (pending.Count > 0)
                    {
                        List<Part> contents;
                        if (!children.TryGetValue(pending.Pop().Volume, out contents)) continue;
                        foreach (Part item in contents)
                        {
                            if (item.Visible) contacts.Add(item.Display);
                            pending.Push(item);
                        }
                    }
                    skin.Add(cavity.Display, contacts, true);
                }
                var mesh = skin.Finish();
                mother.Skin = Mesh.From(mesh);
                var display = mother.Display.Clone();
                display.Geometry = mesh;
                display.Freeze();
                mother.Display = display;
            }
            foreach (Part part in parts)
                if (part.Visible) group.Children.Add(part.Display);
        }

        internal Model3DGroup Cut(Plane first, Plane second, CancellationToken cancel)
        {
            var result = new Model3DGroup();
            if (first == null) { first = second; second = null; }
            if (second != null && Vector3D.CrossProduct(first.Normal, second.Normal).Length < 1e-12)
            {
                if (Vector3D.DotProduct(first.Normal, second.Normal) > 0)
                {
                    if (second.Offset > first.Offset) first = second;
                    second = null;
                }
                else if (first.Offset >= -second.Offset)
                {
                    result.Freeze();
                    return result;
                }
            }
            var firstCap = first == null ? null : new Cap(first, parts);
            var secondCap = second == null ? null : new Cap(second, parts);
            var surfaces = new Mesh[parts.Count];
            for (int i = 0; i < parts.Count; i++)
            {
                Part part = parts[i];
                cancel.ThrowIfCancellationRequested();
                Mesh side = first == null ? part.Mesh :
                    Clip(part.Mesh, part.LocalPlane(first), part.Closed, firstCap, i, cancel);
                if (second != null && side != null)
                {
                    Plane local = part.LocalPlane(second);
                    // Contours for both planes must come from the uncut solid to form closed loops.
                    if (side != part.Mesh && part.Closed)
                        Clip(part.Mesh, local, true, secondCap, i, cancel);
                    side = Clip(side, local, part.Closed, side == part.Mesh ? secondCap : null, i, cancel);
                }
                if (part.Skin != null)
                {
                    side = part.Skin;
                    if (first != null) side = Clip(side, part.LocalPlane(first), true, null, i, cancel);
                    if (second != null && side != null) side = Clip(side, part.LocalPlane(second), true, null, i, cancel);
                }
                if (part.Visible) surfaces[i] = side;
            }
            firstCap?.Fill(surfaces, second, cancel);
            secondCap?.Fill(surfaces, first, cancel);
            for (int i = 0; i < parts.Count; i++)
            {
                Mesh side = surfaces[i];
                if (side != null)
                    result.Children.Add(side == (parts[i].Skin ?? parts[i].Mesh) ? parts[i].Display : side.Model(parts[i].Display));
            }
            result.Freeze();
            return result;
        }

        const double PlaneTolerance = 1e-10; // GDML world lengths in millimetres

        static Mesh Clip(Mesh mesh, Plane plane, bool closed, Cap cap, int owner, CancellationToken cancel)
        {
            double[] distances = new double[mesh.Points.Count];
            double min = double.PositiveInfinity, max = double.NegativeInfinity;
            for (int i = 0; i < distances.Length; i++)
            {
                if ((i & 4095) == 0) cancel.ThrowIfCancellationRequested();
                double d = Vector3D.DotProduct(plane.Normal, (Vector3D)mesh.Points[i]) - plane.Offset;
                if (Math.Abs(d) < PlaneTolerance) d = 0;
                distances[i] = d;
                min = Math.Min(min, d);
                max = Math.Max(max, d);
            }
            if (min > 0 || (!closed && min >= 0)) return mesh;
            if (max <= 0) return null;

            var result = new Mesh();
            var mapped = new int[distances.Length];
            var crossings = new Dictionary<long, int>();
            var polygon = new int[4];
            for (int i = 0; i < mesh.Indices.Count; i += 3)
            {
                if ((i & 1023) == 0) cancel.ThrowIfCancellationRequested();
                int previous = mesh.Indices[i + 2], count = 0, entry = -1, exit = -1;
                for (int j = 0; j < 3; j++)
                {
                    int current = mesh.Indices[i + j];
                    bool inside = closed ? distances[current] > 0 : distances[current] >= 0;
                    bool wasInside = closed ? distances[previous] > 0 : distances[previous] >= 0;
                    if (inside != wasInside)
                    {
                        int split = Crossing(previous, current, mesh, distances, result, mapped, crossings);
                        polygon[count++] = split;
                        if (inside) entry = split;
                        else exit = split;
                    }
                    if (inside)
                        polygon[count++] = CopyVertex(current, mesh, result, mapped);
                    previous = current;
                }
                for (int j = 2; j < count; j++)
                    AddTriangle(result, polygon[0], polygon[j - 1], polygon[j]);
                if (closed && cap != null && entry >= 0 && exit >= 0 && entry != exit)
                    cap.Add(owner, result.Points[entry], result.Points[exit]);
            }
            return result.Indices.Count == 0 ? null : result;
        }

        static int CopyVertex(int index, Mesh source, Mesh target, int[] mapped)
        {
            if (mapped[index] == 0)
            {
                target.Points.Add(source.Points[index]);
                mapped[index] = target.Points.Count;
            }
            return mapped[index] - 1;
        }

        static int Crossing(int a, int b, Mesh mesh, double[] distances, Mesh target,
            int[] mapped, Dictionary<long, int> crossings)
        {
            if (distances[a] == 0) return CopyVertex(a, mesh, target, mapped);
            if (distances[b] == 0) return CopyVertex(b, mesh, target, mapped);
            if (a > b) { int swap = a; a = b; b = swap; }
            long key = ((long)a << 32) | (uint)b;
            int index;
            if (!crossings.TryGetValue(key, out index))
            {
                double t = distances[a] / (distances[a] - distances[b]);
                index = target.Points.Count;
                target.Points.Add(mesh.Points[a] + (mesh.Points[b] - mesh.Points[a]) * t);
                crossings.Add(key, index);
            }
            return index;
        }

        static void AddTriangle(Mesh mesh, int a, int b, int c)
        {
            if (a == b || b == c || c == a) return;
            mesh.Indices.Add(a);
            mesh.Indices.Add(b);
            mesh.Indices.Add(c);
        }

        // Sweep all closed contours in a common plane. Each span belongs to exactly one
        // placement, so the depth buffer never has to choose between two cut materials.
        sealed class Cap
        {
            struct Edge
            {
                internal int Owner, Index;
                internal double Low, High, X0, X1;
                internal double At(double y) { return X0 + (X1 - X0) * ((y - Low) / (High - Low)); }
            }

            struct Strip
            {
                internal Edge Left, Right;
                internal int Owner;
                internal double Bottom, Top;
            }

            readonly Vector3D u, v;
            readonly Point3D origin;
            readonly List<Part> parts;
            readonly Mesh[] faces;
            readonly HashSet<int> inside = new HashSet<int>();
            readonly Dictionary<GdmlVolumeInstance, int> masks = new Dictionary<GdmlVolumeInstance, int>();
            readonly List<Edge> edges = new List<Edge>();
            readonly List<double> levels = new List<double>();
            Strip[] strips;

            internal Cap(Plane plane, List<Part> parts)
            {
                this.parts = parts;
                faces = new Mesh[parts.Count];
                Vector3D seed = Math.Abs(plane.Normal.Z) < 0.8 ? new Vector3D(0,0,1) : new Vector3D(0,1,0);
                u = Vector3D.CrossProduct(seed, plane.Normal);
                u.Normalize();
                v = Vector3D.CrossProduct(u, plane.Normal);
                origin = (Point3D)(plane.Normal * plane.Offset);
            }

            internal void Add(int owner, Point3D a, Point3D b)
            {
                a = parts[owner].Placement.Transform(a);
                b = parts[owner].Placement.Transform(b);
                double ay = Vector3D.DotProduct(a - origin, v), by = Vector3D.DotProduct(b - origin, v);
                if (ay == by) return;
                double ax = Vector3D.DotProduct(a - origin, u), bx = Vector3D.DotProduct(b - origin, u);
                edges.Add(ay < by ? new Edge { Owner = owner, Index = edges.Count, Low = ay, High = by, X0 = ax, X1 = bx }
                                  : new Edge { Owner = owner, Index = edges.Count, Low = by, High = ay, X0 = bx, X1 = ax });
                levels.Add(ay);
                levels.Add(by);
            }

            internal void Fill(Mesh[] surfaces, Plane other, CancellationToken cancel)
            {
                if (edges.Count < 2) return;
                strips = new Strip[edges.Count];
                levels.Sort();
                edges.Sort((a,b) => a.Low.CompareTo(b.Low));
                var active = new List<Edge>();
                var splits = new List<double>();
                int next = 0;
                for (int i = 1; i < levels.Count; i++)
                {
                    double low = levels[i-1], high = levels[i];
                    if (low == high) continue;
                    cancel.ThrowIfCancellationRequested();
                    active.RemoveAll(e => e.High <= low);
                    while (next < edges.Count && edges[next].Low <= low)
                        active.Add(edges[next++]);
                    if (high - low <= PlaneTolerance || active.Count < 2) continue;
                    SplitCrossings(active, low, high, splits, cancel);
                    double bottom = low;
                    foreach (double top in splits)
                    {
                        if (top - bottom <= PlaneTolerance) continue;
                        FillBand(active, bottom, top);
                        bottom = top;
                    }
                }
                foreach (Strip strip in strips)
                    if (strip.Top > strip.Bottom)
                        AddSpan(strip.Owner, strip.Left, strip.Right, strip.Bottom, strip.Top);
                for (int i = 0; i < faces.Length; i++)
                {
                    cancel.ThrowIfCancellationRequested();
                    Mesh face = faces[i];
                    if (face == null) continue;
                    if (other != null)
                        face = Clip(face, parts[i].LocalPlane(other), false, null, i, cancel);
                    if (face == null) continue;
                    Mesh side = surfaces[i];
                    if (side == null)
                        surfaces[i] = face;
                    else
                    {
                        // Source meshes may be shared by repeated placements and future frames.
                        if (side == (parts[i].Skin ?? parts[i].Mesh))
                        {
                            surfaces[i] = face;
                            face.Append(side);
                        }
                        else
                        {
                            side.Append(face);
                        }
                    }
                }
            }

            // Between endpoints, edge order changes only where two straight edges cross.
            // Inspect inversions at the top of the band; disjoint contours take one pass.
            static void SplitCrossings(List<Edge> active, double low, double high, List<double> splits, CancellationToken cancel)
            {
                active.Sort((a,b) => a.At(low).CompareTo(b.At(low)));
                splits.Clear();
                double max = double.NegativeInfinity;
                for (int i = 0; i < active.Count; i++)
                {
                    if ((i & 255) == 0) cancel.ThrowIfCancellationRequested();
                    Edge right = active[i];
                    double top = right.At(high);
                    if (top < max - PlaneTolerance)
                    {
                        for (int j = 0; j < i; j++)
                        {
                            Edge left = active[j];
                            double upper = left.At(high) - top;
                            if (upper <= PlaneTolerance) continue;
                            double lower = right.At(low) - left.At(low);
                            if (lower > PlaneTolerance)
                                splits.Add(low + (high-low) * (lower / (lower+upper)));
                        }
                    }
                    max = Math.Max(max, top);
                }
                splits.Sort();
                splits.Add(high);
            }

            void FillBand(List<Edge> active, double low, double high)
            {
                double mid = low + (high-low)*0.5;
                active.Sort((a,b) => a.At(mid).CompareTo(b.At(mid)));
                inside.Clear();
                masks.Clear();
                int previous = -1;
                Edge start = default(Edge);
                for (int j = 0; j < active.Count;)
                {
                    Edge boundary = active[j];
                    double x = boundary.At(mid);
                    do { Toggle(active[j++].Owner); }
                    while (j < active.Count && active[j].At(mid) - x <= PlaneTolerance);
                    int owner = Owner();
                    if (owner == previous) continue;
                    if (previous >= 0) ExtendStrip(previous, start, boundary, low, high);
                    start = boundary;
                    previous = owner;
                }
            }

            // Endpoints in unrelated contours must not subdivide a continuous material face.
            void ExtendStrip(int owner, Edge left, Edge right, double low, double high)
            {
                Strip strip = strips[left.Index];
                if (strip.Top > strip.Bottom)
                {
                    if (strip.Owner == owner && strip.Right.Index == right.Index && Math.Abs(strip.Top-low) <= PlaneTolerance)
                    {
                        strips[left.Index].Top = high;
                        return;
                    }
                    AddSpan(strip.Owner, strip.Left, strip.Right, strip.Bottom, strip.Top);
                }
                strips[left.Index] = new Strip { Owner = owner, Left = left, Right = right, Bottom = low, Top = high };
            }

            void Toggle(int index)
            {
                bool entering = inside.Add(index);
                if (!entering) inside.Remove(index);
                Part part = parts[index];
                if (part.Visible) return;
                // An air cavity removes material from its ancestors. Independent parts
                // and solid descendants occupying the same span remain eligible.
                for (var p = part.Volume.Parent; p != null; p = p.Parent)
                {
                    int count;
                    masks.TryGetValue(p, out count);
                    count += entering ? 1 : -1;
                    if (count == 0) masks.Remove(p);
                    else masks[p] = count;
                }
            }

            int Owner()
            {
                int best = -1, depth = -1;
                foreach (int i in inside)
                {
                    Part part = parts[i];
                    if (!part.Visible || (part.Volume != null && masks.ContainsKey(part.Volume))) continue;
                    int d = part.Volume == null ? 0 : part.Volume.Depth;
                    // GDML placement order resolves ties between overlapping siblings.
                    if (d > depth || (d == depth && i > best))
                    {
                        best = i;
                        depth = d;
                    }
                }
                return best;
            }

            void AddSpan(int owner, Edge left, Edge right, double low, double high)
            {
                Mesh face = faces[owner] ?? (faces[owner] = new Mesh());
                Matrix3D inverse = parts[owner].Inverse;
                double x0 = left.At(low), x1 = right.At(low), x2 = right.At(high), x3 = left.At(high);
                int start = face.Points.Count;
                face.Points.Add(inverse.Transform(origin + u*x0 + v*low));
                face.Points.Add(inverse.Transform(origin + u*x1 + v*low));
                face.Points.Add(inverse.Transform(origin + u*x2 + v*high));
                face.Points.Add(inverse.Transform(origin + u*x3 + v*high));
                if (x1 > x0) AddTriangle(face, start, start+1, start+2);
                if (x2 > x3) AddTriangle(face, start, start+2, start+3);
            }
        }
    }
}
