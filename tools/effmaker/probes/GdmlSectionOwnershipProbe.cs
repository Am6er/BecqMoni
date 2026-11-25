using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media.Media3D;
using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;

// Run next to a diagnostic BecquerelMonitor.exe and its copied config.
// Exercise the actual imported, placed meshes, including the material on cavity boundaries.
internal static class GdmlSectionOwnershipProbe
{
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static readonly Assembly App = typeof(GdmlGeometry).Assembly;
    static readonly DependencyProperty Volume = (DependencyProperty)App.GetType("BecquerelMonitor.GdmlPartInspector")
        .GetField("VolumeProperty", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public).GetValue(null);

    const string Fixture = @"<gdml><materials/><solids>
<box name='world' x='400' y='200' z='200'/><box name='case' x='20' y='20' z='20'/>
<box name='void' x='12' y='12' z='12'/><box name='insert' x='3' y='4' z='4'/>
<box name='water' x='6' y='10' z='4'/><box name='port' x='4' y='4' z='4'/>
<box name='nested' x='4' y='4' z='4'/>
<box name='bar' x='12' y='6' z='4'/>
</solids><structure>
<volume name='Insert'><materialref ref='G4_Al'/><solidref ref='insert'/></volume>
<volume name='Liquid'><materialref ref='G4_WATER'/><solidref ref='water'/></volume>
<volume name='NestedAir'><materialref ref='Dry Air'/><solidref ref='nested'/></volume>
<volume name='Air'><materialref ref='G4_AIR'/><solidref ref='void'/>
<physvol><volumeref ref='Insert'/></physvol>
<physvol><volumeref ref='NestedAir'/><position z='4'/></physvol></volume>
<volume name='Port'><materialref ref='Air'/><solidref ref='port'/></volume>
<volume name='Case'><materialref ref='G4_Cu'/><solidref ref='case'/>
<physvol><volumeref ref='Air'/><position z='2'/></physvol>
<physvol><volumeref ref='Port'/><position x='8'/></physvol>
<physvol><volumeref ref='Liquid'/><position z='-6'/></physvol></volume>
<volume name='BarA'><materialref ref='G4_Al'/><solidref ref='bar'/></volume>
<volume name='BarB'><materialref ref='G4_Cu'/><solidref ref='bar'/></volume>
<volume name='World'><materialref ref='G4_AIR'/><solidref ref='world'/>
<physvol><volumeref ref='Case'/><position x='100' y='30' z='20'/><rotation z='30' unit='deg'/></physvol>
<physvol><volumeref ref='BarA'/><rotation y='35' unit='deg'/></physvol>
<physvol><volumeref ref='BarB'/><rotation y='-35' unit='deg'/></physvol>
</volume></structure><setup name='Default' version='1'><world ref='World'/></setup></gdml>";

    static string Name(GeometryModel3D m) { return ((GdmlVolumeInstance)m.GetValue(Volume)).VolumeName; }

    static object Field(object o, string name) { return o.GetType().GetField(name, Instance).GetValue(o); }

    static object Plane(Vector3D direction, Point3D point)
    {
        var type = App.GetType("BecquerelMonitor.EfficiencyMaker.GdmlSectionScene+Plane");
        return Activator.CreateInstance(type, Instance, null, new object[] {
            direction, Vector3D.DotProduct(direction, (Vector3D)point) }, null);
    }

    static Model3DGroup Cut(object sections, object first, object second)
    {
        return (Model3DGroup)sections.GetType().GetMethod("Cut", Instance)
            .Invoke(sections, new[] { first, second, CancellationToken.None });
    }

    // Intersect the rendered triangles independently of the section implementation.
    // Several triangles of one mesh may meet on their triangulation diagonal.
    static void Expect(Model3DGroup group, Point3D point, Vector3D ray, double distance, params string[] expected)
    {
        var owners = new HashSet<string>();
        foreach (GeometryModel3D m in group.Children)
        {
            var mesh = (MeshGeometry3D)m.Geometry;
            var indices = mesh.TriangleIndices;
            for (int i = 0; i < indices.Count; i += 3)
            {
                Point3D a = m.Transform.Transform(mesh.Positions[indices[i]]);
                Vector3D u = m.Transform.Transform(mesh.Positions[indices[i+1]]) - a;
                Vector3D v = m.Transform.Transform(mesh.Positions[indices[i+2]]) - a;
                Vector3D h = Vector3D.CrossProduct(ray,v), s = point-a;
                double det = Vector3D.DotProduct(u,h);
                if (Math.Abs(det) < 1e-10) continue;
                Vector3D q = Vector3D.CrossProduct(s,u);
                double x = Vector3D.DotProduct(s,h)/det, y = Vector3D.DotProduct(ray,q)/det;
                double t = Vector3D.DotProduct(v,q)/det;
                if (x >= -1e-8 && y >= -1e-8 && x+y <= 1+1e-8 && Math.Abs(t-distance) < 1e-8)
                    owners.Add(Name(m));
            }
        }
        if (!owners.SetEquals(expected))
            throw new Exception("At " + point + ", distance " + distance + ", expected [" +
                string.Join(", ", expected) + "], got [" + string.Join(", ", owners) + "]");
    }

    [STAThread]
    static void Main()
    {
        GlobalConfigManager.GetInstance();
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, Fixture);
            GdmlGeometry geometry = GdmlGeometry.Read(path);
            if (!string.IsNullOrEmpty(geometry.FatalError)) throw new Exception(geometry.FatalError);
            var type = App.GetType("BecquerelMonitor.GdmlModelDialog");
            using (var form = (Form)Activator.CreateInstance(type, Instance, null,
                new object[] { geometry, GdmlOverview.Of(geometry) }, null))
            {
                var scene = type.GetMethod("BuildScene", Instance).Invoke(form, new object[] { CancellationToken.None });
                var parts = (Model3DGroup)Field(scene, "Parts");
                var names = parts.Children.Cast<GeometryModel3D>().Select(Name).ToArray();
                if (names.Length != 5 || names.Contains("Air") || names.Contains("Port")) throw new Exception("Unexpected visible parts");
                var toWorld = parts.Children.Cast<GeometryModel3D>().Single(m => Name(m) == "Case").Transform.Value;
                Func<double,double,double,Point3D> at = (x,y,z) => toWorld.Transform(new Point3D(x,y,z));
                var dx = toWorld.Transform(new Vector3D(1,0,0));
                var dy = toWorld.Transform(new Vector3D(0,1,0));
                var dz = toWorld.Transform(new Vector3D(0,0,1));
                // Ceiling, partial contact with water, and port opening through the wall.
                Expect(parts, at(4,.31,0), dz, 8, "Case");
                Expect(parts, at(.7,.31,6), dz, 2, "Case");
                Expect(parts, at(2,.31,0), -dz, 4, "Liquid");
                Expect(parts, at(4,.31,0), -dz, 4, "Case");
                Expect(parts, at(3,.31,.41), dx, 3);
                Expect(parts, at(3,.31,.41), dx, 7);
                var sections = Field(scene, "Sections");
                var p = Plane(dy, at(0,.31,0));
                var r = Plane(dx, at(.23,0,0));
                foreach (Model3DGroup cut in new[] { Cut(sections,p,null), Cut(sections,p,r), Cut(sections,r,p) })
                {
                    Expect(cut, at(4,.31,3), dy, 0);
                    Expect(cut, at(.7,.31,3), dy, 0, "Insert");
                    Expect(cut, at(2,.31,-6), dy, 0, "Liquid");
                    Expect(cut, at(8,.31,4), dy, 0, "Case");
                    Expect(cut, at(8,.31,.23), dy, 0);
                    Expect(cut, at(4,1.31,0), dz, 8, "Case");
                }
                var both = Cut(sections,p,r);
                Expect(both, at(-.7,.31,3), dy, 0);
                Expect(both, at(.23,1.31,3), dx, 0, "Insert");
                Expect(both, at(.23,1.31,6), dx, 0);
                Expect(both, at(.23,1.31,9), dx, 0, "Case");
                // A slider can land exactly on the lid between the cavity and solid material.
                Expect(Cut(sections,Plane(-dz,at(0,0,8)),null), at(4,.31,8), dz, 0);
                Expect(Cut(sections,Plane(dz,at(0,0,8)),null), at(4,.31,8), dz, 0, "Case");
                // The two rotated siblings cross between contour vertices. Determine
                // their expected occupancy analytically in each box's local coordinates.
                var normal = new Vector3D(0,1,0);
                var crossed = Cut(sections, Plane(normal,new Point3D(0,.17,0)), null);
                var bars = parts.Children.Cast<GeometryModel3D>().Where(m => Name(m).StartsWith("Bar")).ToArray();
                for (double x = -5.87; x < 6; x += 1)
                    for (double z = -4.71; z < 5; z += .5)
                    {
                        Point3D point = new Point3D(x,.17,z);
                        string expected = null;
                        foreach (var bar in bars)
                        {
                            Point3D local = bar.Transform.Inverse.Transform(point);
                            if (Math.Abs(local.X) < 6 && Math.Abs(local.Z) < 2) expected = Name(bar);
                        }
                        Expect(crossed, point, normal, 0, expected == null ? new string[0] : new[] { expected });
                    }
                Console.WriteLine("PASS: air, aperture, partial contact, nested solid, two cuts, lid and crossing siblings.");
            }
        }
        finally { File.Delete(path); }
    }
}
