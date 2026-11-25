using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Media.Media3D;

namespace BecquerelMonitor.EfficiencyMaker
{
    // In GDML the mother supplies the material immediately outside an air daughter.
    // Build that contact in the mother's coordinates. Faces shared with a sibling
    // (e.g. the liquid surface) already have a visible owner.
    internal sealed class GdmlCavitySurface
    {
        const double Tolerance = 1e-9;
        readonly Matrix3D worldToMother;
        readonly CancellationToken cancel;
        readonly MeshGeometry3D surface = new MeshGeometry3D();
        readonly Dictionary<GeometryModel3D, Facets> cache = new Dictionary<GeometryModel3D, Facets>();

        internal GdmlCavitySurface(Matrix3D worldToMother, CancellationToken cancel)
        {
            this.worldToMother = worldToMother;
            this.cancel = cancel;
        }

        sealed class Triangle
        {
            internal int A, B, C;
            internal Point3D Origin;
            internal Vector3D Normal;
            internal Rect3D Bounds;
        }

        sealed class Node
        {
            internal Rect3D Bounds;
            internal Node Left, Right;
            internal int First, End;
        }

        sealed class Facets
        {
            internal Point3D[] Points;
            internal readonly List<Triangle> Triangles = new List<Triangle>();
            internal Node Root;
        }

        Facets Get(GeometryModel3D model)
        {
            Facets facets;
            if (cache.TryGetValue(model, out facets)) return facets;
            var geometry = (MeshGeometry3D)model.Geometry;
            var transform = model.Transform.Value * worldToMother;
            facets = new Facets { Points = new Point3D[geometry.Positions.Count] };
            geometry.Positions.CopyTo(facets.Points, 0);
            transform.Transform(facets.Points);
            var indices = geometry.TriangleIndices;
            for (int i = 0; i < indices.Count; i += 3)
            {
                if ((i & 1023) == 0) cancel.ThrowIfCancellationRequested();
                int a = indices[i], b = indices[i+1], c = indices[i+2];
                Point3D p = facets.Points[a], q = facets.Points[b], r = facets.Points[c];
                Vector3D normal = Vector3D.CrossProduct(q-p, r-p);
                if (normal.LengthSquared == 0) continue;
                normal.Normalize();
                Rect3D bounds = Rect3D.Empty;
                bounds.Union(p);
                bounds.Union(q);
                bounds.Union(r);
                bounds = new Rect3D(bounds.X-Tolerance, bounds.Y-Tolerance, bounds.Z-Tolerance,
                    bounds.SizeX+2*Tolerance, bounds.SizeY+2*Tolerance, bounds.SizeZ+2*Tolerance);
                facets.Triangles.Add(new Triangle { A = a, B = b, C = c, Origin = p, Normal = normal, Bounds = bounds });
            }
            facets.Root = Index(facets.Triangles, 0, facets.Triangles.Count);
            cache.Add(model, facets);
            return facets;
        }

        Node Index(List<Triangle> triangles, int first, int end)
        {
            cancel.ThrowIfCancellationRequested();
            var node = new Node { First = first, End = end, Bounds = Rect3D.Empty };
            for (int i = first; i < end; i++) node.Bounds.Union(triangles[i].Bounds);
            if (end-first <= 12) return node;
            Rect3D bounds = node.Bounds;
            int axis = bounds.SizeX >= bounds.SizeY && bounds.SizeX >= bounds.SizeZ ? 0 :
                       bounds.SizeY >= bounds.SizeZ ? 1 : 2;
            triangles.Sort(first, end-first, Comparer<Triangle>.Create((a,b) =>
                Centre(a.Bounds, axis).CompareTo(Centre(b.Bounds, axis))));
            int mid = first + (end-first)/2;
            node.Left = Index(triangles, first, mid);
            node.Right = Index(triangles, mid, end);
            return node;
        }

        static double Centre(Rect3D b, int axis)
        {
            if (axis == 0) return b.X + b.SizeX/2;
            if (axis == 1) return b.Y + b.SizeY/2;
            return b.Z + b.SizeZ/2;
        }

        internal void Add(GeometryModel3D model, List<GeometryModel3D> contacts, bool inward)
        {
            Facets source = Get(model);
            var nearby = new List<Facets>();
            Rect3D bounds = model.Bounds;
            var search = new Rect3D(bounds.X-Tolerance, bounds.Y-Tolerance, bounds.Z-Tolerance,
                bounds.SizeX+2*Tolerance, bounds.SizeY+2*Tolerance, bounds.SizeZ+2*Tolerance);
            foreach (var contact in contacts)
            {
                if (!search.IntersectsWith(contact.Bounds)) continue;
                Facets facets = Get(contact);
                if (source.Root.Bounds.IntersectsWith(facets.Root.Bounds)) nearby.Add(facets);
            }
            int shift = surface.Positions.Count;
            foreach (Point3D p in source.Points) surface.Positions.Add(p);
            var candidates = new List<Triangle>();
            foreach (Triangle triangle in source.Triangles)
            {
                cancel.ThrowIfCancellationRequested();
                Point3D a = source.Points[triangle.A], b = source.Points[triangle.B], c = source.Points[triangle.C];
                List<List<Point3D>> pieces = null;
                foreach (Facets facets in nearby)
                {
                    candidates.Clear();
                    Query(facets, facets.Root, triangle, candidates);
                    foreach (Triangle mask in candidates)
                    {
                        if (Math.Abs(Vector3D.DotProduct(a-mask.Origin, mask.Normal)) > Tolerance ||
                            Math.Abs(Vector3D.DotProduct(b-mask.Origin, mask.Normal)) > Tolerance ||
                            Math.Abs(Vector3D.DotProduct(c-mask.Origin, mask.Normal)) > Tolerance) continue;
                        if (pieces == null) pieces = new List<List<Point3D>> { new List<Point3D> { a, b, c } };
                        var result = new List<List<Point3D>>();
                        foreach (var piece in pieces) Subtract(piece, facets, mask, result);
                        pieces = result;
                        if (pieces.Count == 0) break;
                    }
                    if (pieces != null && pieces.Count == 0) break;
                }
                if (pieces == null)
                    Emit(shift+triangle.A, shift+triangle.B, shift+triangle.C, inward);
                else
                    foreach (var piece in pieces)
                    {
                        int start = surface.Positions.Count;
                        foreach (Point3D p in piece) surface.Positions.Add(p);
                        for (int i = 2; i < piece.Count; i++) Emit(start, start+i-1, start+i, inward);
                    }
            }
        }

        static void Query(Facets facets, Node node, Triangle triangle, List<Triangle> matches)
        {
            if (!node.Bounds.IntersectsWith(triangle.Bounds)) return;
            if (node.Left != null)
            {
                Query(facets, node.Left, triangle, matches);
                Query(facets, node.Right, triangle, matches);
                return;
            }
            for (int i = node.First; i < node.End; i++)
                if (facets.Triangles[i].Bounds.IntersectsWith(triangle.Bounds)) matches.Add(facets.Triangles[i]);
        }

        // Subtract a coplanar contact triangle from a convex polygon. Each edge
        // splits off one convex part of the difference, so fan triangulation is sufficient.
        static void Subtract(List<Point3D> polygon, Facets facets, Triangle mask, List<List<Point3D>> result)
        {
            Point3D[] corners = { facets.Points[mask.A], facets.Points[mask.B], facets.Points[mask.C] };
            var outside = new List<List<Point3D>>();
            var inside = polygon;
            for (int i = 0; i < 3; i++)
            {
                Point3D origin = corners[i];
                Vector3D n = Vector3D.CrossProduct(mask.Normal, corners[(i+1)%3]-origin);
                n.Normalize();
                var keep = new List<Point3D>();
                var cut = new List<Point3D>();
                Point3D p = inside[inside.Count-1];
                double d = Distance(p, origin, n);
                foreach (Point3D q in inside)
                {
                    double e = Distance(q, origin, n);
                    if ((d > 0 && e < 0) || (d < 0 && e > 0))
                    {
                        Point3D crossing = p + (q-p)*(d/(d-e));
                        keep.Add(crossing);
                        cut.Add(crossing);
                    }
                    if (e >= 0) keep.Add(q);
                    if (e <= 0) cut.Add(q);
                    p = q;
                    d = e;
                }
                if (keep.Count < 3) { result.Add(polygon); return; }
                if (cut.Count >= 3) outside.Add(cut);
                inside = keep;
            }
            result.AddRange(outside);
        }

        static double Distance(Point3D p, Point3D origin, Vector3D normal)
        {
            double d = Vector3D.DotProduct(p-origin, normal);
            return Math.Abs(d) < Tolerance ? 0 : d;
        }

        void Emit(int a, int b, int c, bool inward)
        {
            surface.TriangleIndices.Add(a);
            surface.TriangleIndices.Add(inward ? c : b);
            surface.TriangleIndices.Add(inward ? b : c);
        }

        internal MeshGeometry3D Finish()
        {
            surface.Freeze();
            return surface;
        }
    }
}
