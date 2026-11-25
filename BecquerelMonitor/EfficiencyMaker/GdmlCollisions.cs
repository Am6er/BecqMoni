using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Media.Media3D;

namespace BecquerelMonitor.EfficiencyMaker
{
    internal sealed class GdmlCollisions
    {
        internal sealed class Body
        {
            internal readonly GdmlVolumeInstance Part;
            internal readonly MeshGeometry3D Mesh;
            internal readonly Matrix3D Placement;
            internal readonly Rect3D Bounds;

            internal Body(GdmlVolumeInstance part, MeshGeometry3D mesh, Matrix3D placement)
            {
                Part = part;
                Mesh = mesh;
                Placement = placement;
                Bounds = new Rect3D(part.Min[0], part.Min[1], part.Min[2],
                    part.Max[0] - part.Min[0], part.Max[1] - part.Min[1], part.Max[2] - part.Min[2]);
            }

            internal ulong[] Cells(GdmlVoxelGrid grid, GdmlModelMesh operands, CancellationToken cancel)
            {
                if (GdmlModelMesh.IsBoolean(Part.Solid))
                    return new GdmlBooleanMesh(Part.Solid, operands.Get, cancel).SampleForCollisions(grid, Placement);
                if (Part.Solid.Kind == GdmlSolidKind.Polycone || Part.Solid.Kind == GdmlSolidKind.GenericPolycone)
                    return grid.RasterizeRevolution(Part.Solid, Placement);
                return grid.Rasterize(Mesh, Placement);
            }
        }

        internal readonly List<MeshGeometry3D> Regions = new List<MeshGeometry3D>();
        internal int UncheckedParts;

        internal static GdmlCollisions Find(IList<Body> bodies, CancellationToken cancel)
        {
            var result = new GdmlCollisions();
            var sorted = new List<Body>();
            var operands = new GdmlModelMesh(cancel);
            var interiors = new Dictionary<GdmlSolid, bool>();
            foreach (Body body in bodies)
            {
                cancel.ThrowIfCancellationRequested();
                // A drawable open CAD shell has no defined interior.
                if (!HasDefinedInterior(body.Part.Solid, interiors))
                    result.UncheckedParts++;
                else
                    sorted.Add(body);
            }
            sorted.Sort((a, b) => a.Bounds.X.CompareTo(b.Bounds.X));
            for (int i = 0; i < sorted.Count; i++)
            {
                Body a = sorted[i];
                double endX = a.Bounds.X + a.Bounds.SizeX;
                for (int j = i + 1; j < sorted.Count && sorted[j].Bounds.X < endX; j++)
                {
                    cancel.ThrowIfCancellationRequested();
                    Body b = sorted[j];
                    Rect3D bounds = Rect3D.Intersect(a.Bounds, b.Bounds);
                    if (bounds.IsEmpty || !HasInterior(bounds) || IsAncestor(a.Part, b.Part) || IsAncestor(b.Part, a.Part))
                        continue;

                    MeshGeometry3D mesh = CommonRegion(a, b, bounds, operands, cancel);
                    if (mesh != null)
                        result.Regions.Add(mesh);
                }
            }
            return result;
        }

        internal static bool HasDefinedInterior(GdmlSolid solid, Dictionary<GdmlSolid, bool> memo)
        {
            bool defined;
            if (memo.TryGetValue(solid, out defined))
                return defined;
            defined = solid.Kind != GdmlSolidKind.Tessellated || solid.VolumeKnown;
            if (GdmlModelMesh.IsBoolean(solid))
                defined = HasDefinedInterior(solid.First, memo) && solid.Second.TrueForAll(child => HasDefinedInterior(child, memo));
            memo[solid] = defined;
            return defined;
        }

        // A single sample on coincident faces may classify as interior to both solids.
        // Require common interior at four non-coplanar points close to the cell centre.
        const double Offset = 1e-5; // fraction of a cell
        static readonly Vector3D[] Samples = { new Vector3D(), new Vector3D(Offset, 0, 0),
            new Vector3D(0, Offset, 0), new Vector3D(0, 0, Offset) };

        static MeshGeometry3D CommonRegion(Body a, Body b, Rect3D bounds, GdmlModelMesh operands, CancellationToken cancel)
        {
            // Each pair has its own grid, so small parts retain their resolution in large scenes.
            ulong[] common = null;
            GdmlVoxelGrid grid = null;
            foreach (Vector3D offset in Samples)
            {
                grid = new GdmlVoxelGrid(bounds, cancel, offset);
                ulong[] left = a.Cells(grid, operands, cancel), right = b.Cells(grid, operands, cancel);
                bool hit = false;
                if (common == null)
                    common = left;
                for (int k = 0; k < common.Length; k++)
                {
                    common[k] &= left[k] & right[k];
                    hit |= common[k] != 0;
                }
                if (!hit)
                    return null;
            }
            MeshGeometry3D mesh = grid.Surface(common);
            mesh.Freeze();
            return mesh;
        }

        static bool HasInterior(Rect3D bounds)
        {
            // Ignore round-off at shared faces after GDML placement transforms.
            return HasWidth(bounds.X, bounds.SizeX) && HasWidth(bounds.Y, bounds.SizeY) && HasWidth(bounds.Z, bounds.SizeZ);
        }

        static bool HasWidth(double origin, double width)
        {
            return width > Math.Max(1.0, Math.Abs(origin)) * 1e-13;
        }

        static bool IsAncestor(GdmlVolumeInstance possibleParent, GdmlVolumeInstance part)
        {
            for (var parent = part.Parent; parent != null; parent = parent.Parent)
            {
                if (ReferenceEquals(parent, possibleParent))
                    return true;
            }
            return false;
        }
    }
}
