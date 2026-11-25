using System;
using System.Threading;
using System.Windows.Media.Media3D;

namespace BecquerelMonitor.EfficiencyMaker
{
    // Each XY column fits in one word. A crossing toggles all cell centres above the surface.
    internal sealed class GdmlVoxelGrid
    {
        const int Size = 64;
        readonly Point3D origin, sampleOrigin;
        readonly Vector3D step;
        readonly CancellationToken cancel;

        internal GdmlVoxelGrid(Rect3D bounds, CancellationToken cancel, Vector3D sampleOffset = default(Vector3D))
        {
            origin = bounds.Location;
            step = new Vector3D(bounds.SizeX, bounds.SizeY, bounds.SizeZ) / Size;
            sampleOrigin = origin + new Vector3D(step.X * sampleOffset.X, step.Y * sampleOffset.Y, step.Z * sampleOffset.Z);
            this.cancel = cancel;
        }

        internal ulong[] Rasterize(MeshGeometry3D mesh, Matrix3D transform)
        {
            Point3D[] points = new Point3D[mesh.Positions.Count];
            for (int i = 0; i < points.Length; i++)
            {
                Point3D p = transform.Transform(mesh.Positions[i]);
                points[i] = new Point3D((p.X-sampleOrigin.X)/step.X, (p.Y-sampleOrigin.Y)/step.Y, (p.Z-sampleOrigin.Z)/step.Z);
            }
            ulong[] cells = new ulong[Size*Size];
            for (int i = 0; i < mesh.TriangleIndices.Count; i += 3)
            {
                if ((i & 1023) == 0)
                    cancel.ThrowIfCancellationRequested();
                Crossings(cells, points[mesh.TriangleIndices[i]], points[mesh.TriangleIndices[i+1]], points[mesh.TriangleIndices[i+2]]);
            }
            return cells;
        }

        // Sample the original R/Z profile for collisions. Coincident round faces can have
        // different triangle edges (for example, when one cylinder is rotated about its axis).
        internal ulong[] RasterizeRevolution(GdmlSolid solid, Matrix3D placement)
        {
            placement.Invert();
            Vector3D dz = placement.Transform(new Vector3D(0, 0, step.Z));
            ulong[] cells = new ulong[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                cancel.ThrowIfCancellationRequested();
                for (int x = 0; x < Size; x++)
                {
                    Point3D p = placement.Transform(new Point3D(sampleOrigin.X + (x + 0.5) * step.X,
                        sampleOrigin.Y + (y + 0.5) * step.Y, sampleOrigin.Z + 0.5 * step.Z));
                    ulong column = 0;
                    for (int z = 0; z < Size; z++)
                    {
                        if (GdmlLocator.InRevolution(solid, p.X, p.Y, p.Z))
                            column |= 1UL << z;
                        p += dz;
                    }
                    cells[y * Size + x] = column;
                }
            }
            return cells;
        }

        internal MeshGeometry3D Surface(ulong[] cells)
        {
            MeshGeometry3D mesh = new MeshGeometry3D();
            for (int axis = 0; axis < 3; axis++)
            {
                AddFaces(mesh, cells, axis, -1);
                AddFaces(mesh, cells, axis, 1);
            }
            return mesh;
        }

        static double Edge(Point3D a, Point3D b, double x, double y)
        {
            return (b.X-a.X)*(y-a.Y) - (b.Y-a.Y)*(x-a.X);
        }

        // Half-open edges assign a ray through a shared edge to exactly one triangle.
        static bool Inside(double edge, Point3D a, Point3D b)
        {
            return edge > 0 || (edge == 0 && (b.Y > a.Y || (b.Y == a.Y && b.X < a.X)));
        }

        static int FirstCell(double value)
        {
            return (int)Math.Max(0, Math.Min(Size, Math.Ceiling(value - 0.5)));
        }

        static void Crossings(ulong[] cells, Point3D a, Point3D b, Point3D c)
        {
            double area = Edge(a,b,c.X,c.Y);
            if (area == 0)
                return;
            if (area < 0)
            {
                Point3D swap = b; b = c; c = swap;
                area = -area;
            }
            int xmin = FirstCell(Math.Min(a.X,Math.Min(b.X,c.X))), ymin = FirstCell(Math.Min(a.Y,Math.Min(b.Y,c.Y)));
            int xmax = Math.Min(Size-1, FirstCell(Math.Max(a.X,Math.Max(b.X,c.X))));
            int ymax = Math.Min(Size-1, FirstCell(Math.Max(a.Y,Math.Max(b.Y,c.Y))));
            for (int y = ymin; y <= ymax; y++)
            {
                for (int x = xmin; x <= xmax; x++)
                {
                    double u = Edge(b,c,x+0.5,y+0.5), v = Edge(c,a,x+0.5,y+0.5), w = Edge(a,b,x+0.5,y+0.5);
                    if (!Inside(u,b,c) || !Inside(v,c,a) || !Inside(w,a,b))
                        continue;
                    int z = FirstCell((u*a.Z + v*b.Z + w*c.Z) / area);
                    if (z < Size)
                        cells[y*Size+x] ^= ulong.MaxValue << z;
                }
            }
        }

        static bool Filled(ulong[] cells, int axis, int layer, int u, int v)
        {
            if (layer < 0 || layer >= Size)
                return false;
            int x = axis == 0 ? layer : axis == 1 ? v : u;
            int y = axis == 0 ? u : axis == 1 ? layer : v;
            int z = axis == 0 ? v : axis == 1 ? u : layer;
            return (cells[y*Size+x] & (1UL << z)) != 0;
        }

        Point3D Corner(int axis, int layer, int u, int v)
        {
            int x = axis == 0 ? layer : axis == 1 ? v : u;
            int y = axis == 0 ? u : axis == 1 ? layer : v;
            int z = axis == 0 ? v : axis == 1 ? u : layer;
            return new Point3D(origin.X+x*step.X, origin.Y+y*step.Y, origin.Z+z*step.Z);
        }

        void AddFaces(MeshGeometry3D mesh, ulong[] cells, int axis, int direction)
        {
            for (int layer = 0; layer < Size; layer++)
            {
                cancel.ThrowIfCancellationRequested();
                int plane = direction > 0 ? layer+1 : layer;
                for (int v = 0; v < Size; v++)
                {
                    int start = -1;
                    for (int u = 0; u <= Size; u++)
                    {
                        bool face = u < Size && Filled(cells, axis, layer, u, v) && !Filled(cells, axis, layer+direction, u, v);
                        if (face && start < 0)
                            start = u;
                        if (!face && start >= 0)
                        {
                            int a = direction > 0 ? start : u, b = direction > 0 ? u : start;
                            GdmlModelMesh.AddQuad(mesh, Corner(axis,plane,a,v), Corner(axis,plane,b,v),
                                Corner(axis,plane,b,v+1), Corner(axis,plane,a,v+1));
                            start = -1;
                        }
                    }
                }
            }
        }
    }
}
