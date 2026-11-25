using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace BecquerelMonitor.EfficiencyMaker
{
    internal sealed class GdmlModelMesh
    {
        readonly Dictionary<GdmlSolid, MeshGeometry3D> meshes = new Dictionary<GdmlSolid, MeshGeometry3D>();
        readonly CancellationToken cancel;

        internal GdmlModelMesh(CancellationToken cancel)
        {
            this.cancel = cancel;
        }

        internal static bool IsBoolean(GdmlSolid solid)
        {
            return solid.Kind == GdmlSolidKind.Union || solid.Kind == GdmlSolidKind.Subtraction;
        }

        internal MeshGeometry3D Get(GdmlSolid solid)
        {
            this.cancel.ThrowIfCancellationRequested();
            MeshGeometry3D mesh;
            if (this.meshes.TryGetValue(solid, out mesh))
                return mesh;

            mesh = null;
            if (solid.Kind == GdmlSolidKind.Tessellated)
            {
                // Even an open shell has drawable faces. A closed volume is only needed for CSG.
                Point3DCollection points = new Point3DCollection(solid.Vertices.Count);
                foreach (GdmlPoint p in solid.Vertices)
                    points.Add(new Point3D(p.X, p.Y, p.Z));
                mesh = new MeshGeometry3D { Positions = points, TriangleIndices = new Int32Collection(solid.Triangles) };
            }
            else if (!solid.Unreadable && !solid.ContainsUnknown)
            {
                switch (solid.Kind)
                {
                    case GdmlSolidKind.Box:
                        mesh = Box(solid);
                        break;
                    case GdmlSolidKind.Polycone:
                    case GdmlSolidKind.GenericPolycone:
                        mesh = Revolution(solid);
                        break;
                    case GdmlSolidKind.Union:
                    case GdmlSolidKind.Subtraction:
                        mesh = new GdmlBooleanMesh(solid, this.Get, this.cancel).Build();
                        break;
                }
            }
            if (mesh != null)
                mesh.Freeze();
            this.meshes.Add(solid, mesh);
            return mesh;
        }

        internal static Matrix3D Placement(GdmlTransform t)
        {
            if (t == null)
                return Matrix3D.Identity;
            GdmlPoint o = t.Apply(new GdmlPoint());
            GdmlPoint x = t.Apply(new GdmlPoint(1, 0, 0));
            GdmlPoint y = t.Apply(new GdmlPoint(0, 1, 0));
            GdmlPoint z = t.Apply(new GdmlPoint(0, 0, 1));
            return new Matrix3D(x.X-o.X, x.Y-o.Y, x.Z-o.Z, 0,
                                y.X-o.X, y.Y-o.Y, y.Z-o.Z, 0,
                                z.X-o.X, z.Y-o.Y, z.Z-o.Z, 0, o.X, o.Y, o.Z, 1);
        }

        static MeshGeometry3D Box(GdmlSolid solid)
        {
            double x = solid.SizeX / 2, y = solid.SizeY / 2, z = solid.SizeZ / 2;
            MeshGeometry3D mesh = new MeshGeometry3D();
            AddQuad(mesh, new Point3D(-x,-y,-z), new Point3D(-x,y,-z), new Point3D(x,y,-z), new Point3D(x,-y,-z));
            AddQuad(mesh, new Point3D(-x,-y,z), new Point3D(x,-y,z), new Point3D(x,y,z), new Point3D(-x,y,z));
            AddQuad(mesh, new Point3D(-x,-y,-z), new Point3D(x,-y,-z), new Point3D(x,-y,z), new Point3D(-x,-y,z));
            AddQuad(mesh, new Point3D(x,y,-z), new Point3D(-x,y,-z), new Point3D(-x,y,z), new Point3D(x,y,z));
            AddQuad(mesh, new Point3D(-x,y,-z), new Point3D(-x,-y,-z), new Point3D(-x,-y,z), new Point3D(-x,y,z));
            AddQuad(mesh, new Point3D(x,-y,-z), new Point3D(x,y,-z), new Point3D(x,y,z), new Point3D(x,-y,z));
            return mesh;
        }

        internal static void AddQuad(MeshGeometry3D mesh, Point3D a, Point3D b, Point3D c, Point3D d)
        {
            int start = mesh.Positions.Count;
            mesh.Positions.Add(a);
            mesh.Positions.Add(b);
            mesh.Positions.Add(c);
            mesh.Positions.Add(d);
            Triangle(mesh, start, start+1, start+2);
            Triangle(mesh, start, start+2, start+3);
        }

        static void Triangle(MeshGeometry3D mesh, int a, int b, int c)
        {
            mesh.TriangleIndices.Add(a);
            mesh.TriangleIndices.Add(b);
            mesh.TriangleIndices.Add(c);
        }

        static Point3D Revolve(Point rz, double phi)
        {
            return new Point3D(rz.X * Math.Cos(phi), rz.X * Math.Sin(phi), rz.Y);
        }

        static MeshGeometry3D Revolution(GdmlSolid solid)
        {
            List<Point> profile = new List<Point>();
            if (solid.Kind == GdmlSolidKind.GenericPolycone)
            {
                foreach (double[] p in solid.Profile)
                    profile.Add(new Point(p[0], p[1]));
            }
            else
            {
                foreach (double[] p in solid.Planes)
                    profile.Add(new Point(p[2], p[0]));
                for (int i = solid.Planes.Count - 1; i >= 0; i--)
                    profile.Add(new Point(solid.Planes[i][1], solid.Planes[i][0]));
            }
            double area = 0;
            for (int i = 0; i < profile.Count; i++)
            {
                Point a = profile[i], b = profile[(i+1) % profile.Count];
                area += a.X * b.Y - b.X * a.Y;
            }
            if (area < 0)
                profile.Reverse();

            MeshGeometry3D mesh = new MeshGeometry3D();
            double start = solid.StartPhi * Math.PI / 180, sweep = solid.DeltaPhi * Math.PI / 180;
            int steps = Math.Max(1, (int)Math.Ceiling(solid.DeltaPhi / 5));
            bool full = solid.DeltaPhi >= 360;
            int rings = full ? steps : steps + 1;
            for (int i = 0; i < profile.Count; i++)
            {
                Point a = profile[i], b = profile[(i+1) % profile.Count];
                if (a == b || (a.X == 0 && b.X == 0))
                    continue;
                int first = mesh.Positions.Count;
                for (int j = 0; j < rings; j++)
                {
                    double phi = start + sweep * j / steps;
                    mesh.Positions.Add(Revolve(a, phi));
                    mesh.Positions.Add(Revolve(b, phi));
                }
                for (int j = 0; j < steps; j++)
                {
                    int here = first + 2*j, next = first + 2*((j+1) % rings);
                    if (a.X > 0)
                        Triangle(mesh, here, next, here+1);
                    if (b.X > 0)
                        Triangle(mesh, next, next+1, here+1);
                }
            }
            if (!full)
                CloseSector(mesh, profile, start, start + sweep);
            return mesh;
        }

        // A horizontal band of a concave R/Z profile may contain several disjoint spans.
        static void CloseSector(MeshGeometry3D mesh, List<Point> profile, double start, double end)
        {
            double[] levels = profile.Select(p => p.Y).Distinct().OrderBy(z => z).ToArray();
            for (int i = 1; i < levels.Length; i++)
            {
                double low = levels[i-1], high = levels[i];
                List<Point> radii = new List<Point>();
                for (int j = 0; j < profile.Count; j++)
                {
                    Point a = profile[j], b = profile[(j+1) % profile.Count];
                    if (a.Y == b.Y || Math.Min(a.Y,b.Y) > low || Math.Max(a.Y,b.Y) < high)
                        continue;
                    double slope = (b.X-a.X) / (b.Y-a.Y);
                    radii.Add(new Point(a.X + (low-a.Y)*slope, a.X + (high-a.Y)*slope));
                }
                radii.Sort((a,b) => (a.X+a.Y).CompareTo(b.X+b.Y));
                for (int j = 1; j < radii.Count; j += 2)
                {
                    Point a = new Point(radii[j-1].X, low), b = new Point(radii[j].X, low);
                    Point c = new Point(radii[j].Y, high), d = new Point(radii[j-1].Y, high);
                    AddQuad(mesh, Revolve(a,start), Revolve(b,start), Revolve(c,start), Revolve(d,start));
                    AddQuad(mesh, Revolve(d,end), Revolve(c,end), Revolve(b,end), Revolve(a,end));
                }
            }
        }
    }
}
