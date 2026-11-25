using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media.Media3D;

namespace BecquerelMonitor.EfficiencyMaker
{
    // Local coordinates are GDML millimetres. The ray parameter remains the transport
    // distance in cm through every placement, including reflected and boolean solids.
    internal sealed class GdmlTransportBody
    {
        internal struct Ray
        {
            internal Point3D Origin;
            internal Vector3D Direction;
            internal Ray(Point3D origin, Vector3D direction) { Origin = origin; Direction = direction; }
            internal Ray Transform(Matrix3D m) { return new Ray(m.Transform(Origin), m.Transform(Direction)); }
            internal Point3D At(double t) { return Origin + t * Direction; }
        }

        internal readonly Rect3D Bounds;
        readonly GdmlSolid solid;
        internal bool SimpleCrystal { get; private set; }
        readonly GdmlSampleMesh mesh;
        readonly double[][] profile;
        readonly GdmlTransportBody[] operands;
        readonly Matrix3D[] inverse;
        [ThreadStatic] static List<double> boundaries;
        [ThreadStatic] static double[] meshCrossings;

        internal static GdmlTransportBody Get(GdmlSolid solid, Dictionary<GdmlSolid,GdmlTransportBody> cache)
        {
            if (solid == null || !solid.BoundsKnown || solid.ContainsUnknown || solid.Kind == GdmlSolidKind.Unknown ||
                (solid.Unreadable && solid.Kind != GdmlSolidKind.Tessellated))
                throw new InvalidDataException(GdmlText.T("GdmlIncompleteTransport", "Cannot simulate an incomplete GDML volume") + ": " + solid?.Name);
            GdmlTransportBody body;
            if (!cache.TryGetValue(solid, out body)) cache.Add(solid, body = new GdmlTransportBody(solid, cache));
            return body;
        }

        internal static Matrix3D Inverse(Matrix3D matrix) { matrix.Invert(); return matrix; }

        GdmlTransportBody(GdmlSolid solid, Dictionary<GdmlSolid,GdmlTransportBody> cache)
        {
            this.solid = solid;
            SimpleCrystal = solid.Kind == GdmlSolidKind.Box;
            Bounds = new Rect3D(solid.Min[0],solid.Min[1],solid.Min[2],
                solid.Max[0]-solid.Min[0],solid.Max[1]-solid.Min[1],solid.Max[2]-solid.Min[2]);
            switch (solid.Kind)
            {
                case GdmlSolidKind.Tessellated:
                    mesh = new GdmlSampleMesh(GdmlSampleMesh.Encode(solid, p => new Point3D(p.X,p.Y,p.Z)), !solid.VolumeKnown);
                    break;
                case GdmlSolidKind.Polycone:
                    profile = solid.Planes.Select(p => new[] { p[2],p[0] })
                        .Concat(solid.Planes.AsEnumerable().Reverse().Select(p => new[] { p[1],p[0] })).ToArray();
                    break;
                case GdmlSolidKind.GenericPolycone:
                    profile = solid.Profile.ToArray();
                    break;
                case GdmlSolidKind.Subtraction:
                case GdmlSolidKind.Union:
                    operands = new[] { solid.First }.Concat(solid.Second).Select(s => Get(s,cache)).ToArray();
                    inverse = new[] { solid.FirstPlacement }.Concat(solid.SecondPlacements)
                        .Select(t => Inverse(GdmlModelMesh.Placement(t))).ToArray();
                    break;
            }
            if (profile != null && solid.DeltaPhi == 360)
            {
                double radius = solid.Max[0], z0 = solid.Min[2], z1 = solid.Max[2], area = 0;
                bool rectangular = true;
                for (int i=0,j=profile.Length-1; i<profile.Length; j=i++)
                {
                    double r = profile[i][0], z = profile[i][1], s = profile[j][0], t = profile[j][1];
                    rectangular &= (r == s && (r == 0 || r == radius)) || (z == t && (z == z0 || z == z1));
                    area += s*z-r*t;
                }
                SimpleCrystal = rectangular && Math.Abs(Math.Abs(area)/2-radius*(z1-z0)) < 1e-10;
            }
        }

        static bool Slab(double x, double d, double min, double max, ref double lo, ref double hi)
        {
            if (d == 0) return x >= min && x <= max;
            double a = (min-x)/d, b = (max-x)/d;
            lo = Math.Max(lo, Math.Min(a,b));
            hi = Math.Min(hi, Math.Max(a,b));
            return hi > lo;
        }

        internal static bool BoxSpan(Rect3D b, Ray r, out double lo, out double hi)
        {
            lo = double.NegativeInfinity;
            hi = double.PositiveInfinity;
            return Slab(r.Origin.X,r.Direction.X,b.X,b.X+b.SizeX,ref lo,ref hi)
                && Slab(r.Origin.Y,r.Direction.Y,b.Y,b.Y+b.SizeY,ref lo,ref hi)
                && Slab(r.Origin.Z,r.Direction.Z,b.Z,b.Z+b.SizeZ,ref lo,ref hi);
        }

        internal bool Contains(Point3D p)
        {
            if (!Bounds.Contains(p)) return false;
            if (mesh != null) return mesh.Contains(p.X,p.Y,p.Z);
            if (profile != null) return GdmlLocator.InRevolution(solid,p.X,p.Y,p.Z);
            if (operands == null) return true;
            bool inside = operands[0].Contains(inverse[0].Transform(p));
            bool union = solid.Kind == GdmlSolidKind.Union;
            for (int i = 1; i < operands.Length && inside != union; i++)
                if (operands[i].Contains(inverse[i].Transform(p))) return union;
            return inside;
        }

        // Candidate intersections with the revolved polygon edges. A midpoint test
        // removes the opposite cone sheet, edge extensions and discarded phi sectors.
        void RevolutionCuts(Ray ray, List<double> cuts)
        {
            var p = ray.Origin;
            var d = ray.Direction;
            for (int i = 0, j = profile.Length-1; i < profile.Length; j = i++)
            {
                double r0 = profile[j][0], z0 = profile[j][1];
                double dr = profile[i][0]-r0, dz = profile[i][1]-z0;
                if (dz == 0)
                {
                    if (dr != 0 && d.Z != 0) cuts.Add((z0-p.Z)/d.Z);
                    continue;
                }
                double slope = dr/dz;
                double radius = r0+slope*(p.Z-z0), rate = slope*d.Z;
                double a = d.X*d.X+d.Y*d.Y-rate*rate;
                double b = p.X*d.X+p.Y*d.Y-radius*rate;
                double c = p.X*p.X+p.Y*p.Y-radius*radius;
                if (a == 0)
                {
                    if (b != 0) cuts.Add(-c/(2*b));
                    continue;
                }
                double disc = b*b-a*c;
                if (disc < 0) continue;
                double root = Math.Sqrt(disc);
                double q = -b-(b < 0 ? -root : root);
                cuts.Add(q/a);
                if (q != 0) cuts.Add(c/q);
            }
            if (solid.DeltaPhi < 360)
            {
                PhiCut(solid.StartPhi,ray,cuts);
                PhiCut(solid.StartPhi+solid.DeltaPhi,ray,cuts);
            }
        }

        static void PhiCut(double degrees, Ray ray, List<double> cuts)
        {
            double angle = degrees*Math.PI/180, x = -Math.Sin(angle), y = Math.Cos(angle);
            double d = x*ray.Direction.X+y*ray.Direction.Y;
            if (d != 0) cuts.Add(-(x*ray.Origin.X+y*ray.Origin.Y)/d);
        }

        void Gather(Ray ray, List<double> cuts)
        {
            double lo, hi;
            if (!BoxSpan(Bounds,ray,out lo,out hi)) return;
            cuts.Add(lo);
            cuts.Add(hi);
            if (mesh != null)
            {
                if (meshCrossings == null) meshCrossings = new double[8];
                int n = MeshSpans(ray, ref meshCrossings);
                for (int i = 0; i < 2*n; i++) cuts.Add(meshCrossings[i]);
            }
            else if (profile != null) RevolutionCuts(ray,cuts);
            else if (operands != null)
                for (int i = 0; i < operands.Length; i++) operands[i].Gather(ray.Transform(inverse[i]),cuts);
        }

        int MeshSpans(Ray r, ref double[] spans)
        {
            return mesh.Spans(r.Origin.X,r.Origin.Y,r.Origin.Z,r.Direction.X,r.Direction.Y,r.Direction.Z,ref spans);
        }

        internal int Spans(Ray ray, ref double[] spans)
        {
            double lo, hi;
            if (!BoxSpan(Bounds,ray,out lo,out hi) || hi < 0) return 0;
            if (mesh != null) return MeshSpans(ray,ref spans);
            if (operands == null && profile == null)
            {
                spans[0] = lo;
                spans[1] = hi;
                return 1;
            }
            var cuts = boundaries ?? (boundaries = new List<double>(32));
            cuts.Clear();
            Gather(ray,cuts);
            cuts.Sort();
            if (spans.Length < 2*cuts.Count) Array.Resize(ref spans,2*cuts.Count);
            int count = 0;
            double start = lo;
            foreach (double cut in cuts)
            {
                if (cut <= start+1e-10) continue;
                double end = Math.Min(cut,hi);
                if (Contains(ray.At((start+end)/2)))
                {
                    if (count == 0 || spans[count-1] != start) spans[count++] = start;
                    else count--;
                    spans[count++] = end;
                }
                start = end;
                if (end == hi) break;
            }
            return count/2;
        }
    }
}
