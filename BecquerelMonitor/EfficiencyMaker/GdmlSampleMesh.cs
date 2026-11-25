using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media.Media3D;

namespace BecquerelMonitor.EfficiencyMaker
{
    public sealed class GeometrySampleMesh
    {
        string packed;
        Lazy<GdmlSampleMesh> compiled;
        public string Packed
        {
            get { return packed; }
            set
            {
                packed = value;
                compiled = new Lazy<GdmlSampleMesh>(() =>
                {
                    if (string.IsNullOrEmpty(value)) return null;
                    try { return new GdmlSampleMesh(value); }
                    catch (FormatException) { return null; }
                    catch (IOException) { return null; }
                    catch (InvalidDataException) { return null; }
                });
            }
        }
        internal GdmlSampleMesh Solid { get { return compiled?.Value; } }
    }

    // A closed GDML sample in the transport frame (cm). The same triangle boundaries
    // determine the emission volume and every material interval on a photon path.
    internal sealed class GdmlSampleMesh
    {
        sealed class Node
        {
            internal Rect3D Box;
            internal Node Left, Right;
            internal int Start, Count;
        }

        struct Face
        {
            internal Point3D A;
            internal Vector3D AB, AC;
            internal Point3D Centre;
            internal Rect3D Box;
        }

        struct Hit : IComparable<Hit>
        {
            internal double T;
            internal int Sign;
            public int CompareTo(Hit other) { return T.CompareTo(other.T); }
        }

        internal readonly double VolumeCm3;
        readonly bool openSurface;
        readonly Face[] faces;
        readonly Node root;
        [ThreadStatic] static List<Hit> crossings;
        internal Rect3D Bounds { get { return root.Box; } }

        internal static string Encode(GdmlSolid solid, Func<GdmlPoint, Point3D> place)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(solid.Vertices.Count);
                writer.Write(solid.Triangles.Count / 3);
                foreach (var point in solid.Vertices)
                {
                    Point3D p = place(point);
                    writer.Write(p.X);
                    writer.Write(p.Y);
                    writer.Write(p.Z);
                }
                foreach (int index in solid.Triangles) writer.Write(index);
                return Convert.ToBase64String(stream.ToArray());
            }
        }

        internal GdmlSampleMesh(string encoded, bool allowOpen = false)
        {
            using (var stream = new MemoryStream(Convert.FromBase64String(encoded)))
            using (var reader = new BinaryReader(stream))
            {
                int vertices = reader.ReadInt32(), triangles = reader.ReadInt32();
                if (vertices < (allowOpen ? 3 : 4) || triangles < (allowOpen ? 1 : 4) || 8L + 24L * vertices + 12L * triangles != stream.Length)
                    throw new InvalidDataException("Invalid GDML sample mesh size.");
                var points = new Point3D[vertices];
                var topology = new GdmlSolid();
                double signedSix = 0;
                for (int i = 0; i < vertices; i++)
                {
                    var p = new Point3D(reader.ReadDouble(), reader.ReadDouble(), reader.ReadDouble());
                    if (double.IsNaN(p.X + p.Y + p.Z) || double.IsInfinity(p.X + p.Y + p.Z))
                        throw new InvalidDataException("Invalid GDML vertex.");
                    points[i] = p;
                    topology.Vertices.Add(new GdmlPoint(10*p.X,10*p.Y,10*p.Z));
                }
                faces = new Face[triangles];
                for (int i = 0; i < triangles; i++)
                {
                    int ia = reader.ReadInt32(), ib = reader.ReadInt32(), ic = reader.ReadInt32();
                    if ((uint)ia >= vertices || (uint)ib >= vertices || (uint)ic >= vertices)
                        throw new InvalidDataException("Invalid GDML face index.");
                    topology.Triangles.Add(ia);
                    topology.Triangles.Add(ib);
                    topology.Triangles.Add(ic);
                    Point3D a = points[ia], b = points[ib], c = points[ic];
                    signedSix += Vector3D.DotProduct(a-points[0],Vector3D.CrossProduct(b-a,c-a));
                    var box = new Rect3D(a, new Size3D());
                    box.Union(b);
                    box.Union(c);
                    faces[i] = new Face { A = a, AB = b-a, AC = c-a, Box = box, Centre = a + ((b-a)+(c-a))/3 };
                }
                VolumeCm3 = Math.Abs(signedSix)/6;
                bool flipped;
                if (!allowOpen && (!(VolumeCm3 > 0) || double.IsInfinity(VolumeCm3) || GdmlGeometry.CountUnpairedEdges(topology,out flipped) != 0))
                    throw new InvalidDataException("The GDML mesh does not enclose a volume.");
            }
            openSurface = allowOpen;
            root = Build(0, faces.Length);
            profile = new Lazy<List<System.Windows.Rect>>(() => SketchRows(false));
            plan = new Lazy<List<System.Windows.Rect>>(() => SketchRows(true));
        }

        Node Build(int start, int count)
        {
            var box = Rect3D.Empty;
            for (int i = start; i < start + count; i++) box.Union(faces[i].Box);
            var node = new Node { Start = start, Count = count, Box = box };
            if (count > 10)
            {
                int axis = box.SizeX >= box.SizeY && box.SizeX >= box.SizeZ ? 0 : box.SizeY >= box.SizeZ ? 1 : 2;
                Array.Sort(faces, start, count, Comparer<Face>.Create((a,b) => Axis(a.Centre,axis).CompareTo(Axis(b.Centre,axis))));
                node.Left = Build(start, count/2);
                node.Right = Build(start + count/2, count-count/2);
            }
            return node;
        }

        static double Axis(Point3D p, int axis) { return axis == 0 ? p.X : axis == 1 ? p.Y : p.Z; }

        static bool Slab(double origin, double direction, double min, double size, ref double lo, ref double hi)
        {
            if (direction == 0) return origin >= min && origin <= min + size;
            double t0 = (min-origin)/direction, t1 = (min+size-origin)/direction;
            lo = Math.Max(lo, Math.Min(t0,t1));
            hi = Math.Min(hi, Math.Max(t0,t1));
            return hi >= lo;
        }

        void Trace(Node node, Point3D p, Vector3D d, List<Hit> hits)
        {
            double lo = double.NegativeInfinity, hi = double.PositiveInfinity;
            Rect3D b = node.Box;
            if (!Slab(p.X,d.X,b.X,b.SizeX,ref lo,ref hi) || !Slab(p.Y,d.Y,b.Y,b.SizeY,ref lo,ref hi)
                || !Slab(p.Z,d.Z,b.Z,b.SizeZ,ref lo,ref hi)) return;
            if (node.Left != null)
            {
                Trace(node.Left,p,d,hits);
                Trace(node.Right,p,d,hits);
                return;
            }
            for (int i = node.Start; i < node.Start + node.Count; i++)
            {
                Face f = faces[i];
                Vector3D h = Vector3D.CrossProduct(d,f.AC);
                double det = Vector3D.DotProduct(f.AB,h);
                if (Math.Abs(det) <= 1e-15) continue;
                Vector3D delta = p-f.A;
                double u = Vector3D.DotProduct(delta,h)/det;
                if (u < -1e-11 || u > 1+1e-11) continue;
                Vector3D q = Vector3D.CrossProduct(delta,f.AB);
                double v = Vector3D.DotProduct(d,q)/det;
                if (v < -1e-11 || u+v > 1+1e-11) continue;
                hits.Add(new Hit { T = Vector3D.DotProduct(f.AC,q)/det, Sign = Math.Sign(det) });
            }
        }

        // Merge duplicate hits at shared edges. Opposite signs at the same point
        // are a tangency. A non-convex mesh may have any number of intervals.
        internal int Spans(double x,double y,double z,double dx,double dy,double dz,ref double[] spans)
        {
            var hits = crossings ?? (crossings = new List<Hit>(16));
            hits.Clear();
            Trace(root,new Point3D(x,y,z),new Vector3D(dx,dy,dz),hits);
            hits.Sort();
            if (spans.Length < hits.Count) Array.Resize(ref spans,hits.Count);
            int n = 0;
            int previousSign = 0;
            double previousT = 0;
            for (int i = 0; i < hits.Count;)
            {
                Hit first = hits[i++];
                bool tangent = false;
                while (i < hits.Count && hits[i].T-first.T < 1e-9)
                    tangent |= hits[i++].Sign != first.Sign;
                if (tangent) continue;
                if (!openSurface) spans[n++] = first.T;
                else
                {
                    // An open surface has no unique interior. Credit only material between
                    // actual facets, from an inward crossing to the next outward crossing.
                    if (previousSign > 0 && first.Sign < 0)
                    {
                        spans[n++] = previousT;
                        spans[n++] = first.T;
                    }
                    previousSign = first.Sign;
                    previousT = first.T;
                }
            }
            return n / 2;
        }

        readonly Lazy<List<System.Windows.Rect>> profile, plan;
        internal IEnumerable<System.Windows.Rect> Sketch(bool fromTop) { return (fromTop ? plan : profile).Value; }

        List<System.Windows.Rect> SketchRows(bool top)
        {
            const int steps = 144;
            var rows = new List<System.Windows.Rect>();
            var b = Bounds;
            double dh = (top ? b.SizeY : b.SizeZ)/steps, dw = b.SizeX/steps;
            double[] spans = new double[8];
            for (int j = 0; j < steps; j++)
            {
                double y = (top ? b.Y : b.Z)+j*dh;
                if (!top)
                {
                    int n = Spans(0,0,y+dh/2,1,0,0,ref spans);
                    for (int k=0; k<2*n; k+=2)
                        rows.Add(new System.Windows.Rect(spans[k],y,spans[k+1]-spans[k],dh));
                    continue;
                }
                int start = -1;
                for (int i = 0; i <= steps; i++)
                {
                    bool inside = i < steps && Spans(b.X+(i+.5)*dw,y+dh/2,0,0,0,1,ref spans) > 0;
                    if (inside && start < 0) start = i;
                    if (!inside && start >= 0)
                    {
                        rows.Add(new System.Windows.Rect(b.X+start*dw,y,(i-start)*dw,dh));
                        start = -1;
                    }
                }
            }
            return rows;
        }

        [ThreadStatic] static double[] pointSpans;
        internal bool Contains(double x,double y,double z)
        {
            if (!Bounds.Contains(x,y,z)) return false;
            if (pointSpans == null) pointSpans = new double[8];
            int count = Spans(x,y,z,1,0.237163,0.129731,ref pointSpans);
            for (int j = 0; j < 2*count; j += 2)
                if (pointSpans[j] <= 0 && pointSpans[j+1] > 0) return true;
            return false;
        }
    }
}
