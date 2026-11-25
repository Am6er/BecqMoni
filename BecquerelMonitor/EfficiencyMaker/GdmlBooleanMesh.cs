using System;
using System.Threading;
using System.Windows.Media.Media3D;

namespace BecquerelMonitor.EfficiencyMaker
{
    // Preview only. Rasterize the operands on the same grid, then combine their occupied cells.
    // Each XY column fits in one word; a triangle crossing toggles all cells above its surface.
    internal sealed class GdmlBooleanMesh
    {
        readonly GdmlSolid root;
        readonly Func<GdmlSolid, MeshGeometry3D> primitive;
        readonly CancellationToken cancel;
        GdmlVoxelGrid grid;
        int nodes;
        bool useProfiles;

        internal GdmlBooleanMesh(GdmlSolid root, Func<GdmlSolid, MeshGeometry3D> primitive, CancellationToken cancel)
        {
            this.root = root;
            this.primitive = primitive;
            this.cancel = cancel;
        }

        internal MeshGeometry3D Build()
        {
            for (int i = 0; i < 3; i++)
            {
                double extent = root.Max[i] - root.Min[i];
                if (!(extent > 0) || double.IsInfinity(extent))
                    return null;
            }
            var bounds = new Rect3D(root.Min[0], root.Min[1], root.Min[2],
                root.Max[0] - root.Min[0], root.Max[1] - root.Min[1], root.Max[2] - root.Min[2]);
            grid = new GdmlVoxelGrid(bounds, cancel);
            ulong[] cells = Occupancy(root, Matrix3D.Identity, 0);
            return cells == null ? null : grid.Surface(cells);
        }

        internal ulong[] SampleForCollisions(GdmlVoxelGrid target, Matrix3D placement)
        {
            grid = target;
            nodes = 0;
            useProfiles = true;
            return Occupancy(root, placement, 0);
        }

        ulong[] Occupancy(GdmlSolid solid, Matrix3D transform, int depth)
        {
            cancel.ThrowIfCancellationRequested();
            // A shared operand can occur on both sides at every level of a GDML tree.
            if (solid == null || solid.Unreadable || solid.ContainsUnknown ||
                depth > GdmlGeometry.MaxSolidDepth || ++nodes > 1024)
                return null;
            if (GdmlModelMesh.IsBoolean(solid))
            {
                ulong[] combined = Occupancy(solid.First, GdmlModelMesh.Placement(solid.FirstPlacement) * transform, depth+1);
                if (combined == null)
                    return null;
                for (int i = 0; i < solid.Second.Count; i++)
                {
                    GdmlTransform at = i < solid.SecondPlacements.Count ? solid.SecondPlacements[i] : null;
                    ulong[] other = Occupancy(solid.Second[i], GdmlModelMesh.Placement(at) * transform, depth+1);
                    if (other == null)
                        return null;
                    for (int j = 0; j < combined.Length; j++)
                        combined[j] = solid.Kind == GdmlSolidKind.Union ? combined[j] | other[j] : combined[j] & ~other[j];
                }
                return combined;
            }
            if (useProfiles && (solid.Kind == GdmlSolidKind.Polycone || solid.Kind == GdmlSolidKind.GenericPolycone))
                return grid.RasterizeRevolution(solid, transform);
            MeshGeometry3D mesh = primitive(solid);
            if (mesh == null || mesh.TriangleIndices.Count == 0)
                return null;
            return grid.Rasterize(mesh, transform);
        }
    }
}
