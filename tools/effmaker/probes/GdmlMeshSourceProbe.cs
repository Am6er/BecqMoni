using System;
using System.IO;
using System.Reflection;
using System.Xml.Serialization;
using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;

// Three disjoint cubes in one closed solid require six crossings of a single ray.
// This exercises both the old four-value span buffer and persistence of the mesh.
internal static class GdmlMeshSourceProbe
{
    static void Check(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException(message);
    }

    static string Cubes()
    {
        int[] faces = { 0,2,3, 0,3,1, 4,5,7, 4,7,6, 0,1,5, 0,5,4,
                        2,6,7, 2,7,3, 0,4,6, 0,6,2, 1,3,7, 1,7,5 };
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(24);
            writer.Write(36);
            for (int cube = 0; cube < 3; cube++)
                for (int i = 0; i < 8; i++)
                {
                    writer.Write((double)(2*cube+(i & 1)));
                    writer.Write((double)((i >> 1) & 1));
                    writer.Write((double)(-4+(i >> 2)));
                }
            for (int cube = 0; cube < 3; cube++)
                foreach (int index in faces) writer.Write(8*cube+index);
            return Convert.ToBase64String(stream.ToArray());
        }
    }

    [STAThread]
    static void Main()
    {
        var g = GeometryEditorPanel.Blank();
        g.SourceType = GeometrySourceType.Mesh;
        g.SampleMesh = new GeometrySampleMesh { Packed = Cubes() };
        Check(Math.Abs(GeometryScenes.SampleVolumeCm3(g)-3) < 1e-12, "mesh volume");
        var mesh = typeof(GeometrySampleMesh).GetProperty("Solid",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(g.SampleMesh,null);
        var span = mesh.GetType().GetMethod("Spans",BindingFlags.Instance|BindingFlags.NonPublic);
        object[] ray = { -1.0, .4, -3.6, 1.0, 0.0, 0.0, new double[4] };
        Check((int)span.Invoke(mesh,ray) == 3, "three intervals");
        var edges = (double[])ray[6];
        for (int i=0; i<6; i++) Check(Math.Abs(edges[i]-i-1) < 1e-10, "ray crossing");

        string path = Path.GetTempFileName();
        try
        {
            GeometryWriter.Save(g,path);
            Check(GeometryModel.Load(path).SampleMesh.Packed == g.SampleMesh.Packed, ".in mesh");
            var template = new SampleTemplate();
            template.CaptureFrom(g);
            var xml = new XmlSerializer(typeof(SampleTemplate));
            using (var file = File.Create(path)) xml.Serialize(file,template);
            var restored = GeometryEditorPanel.Blank();
            using (var file = File.OpenRead(path)) ((SampleTemplate)xml.Deserialize(file)).Apply(restored);
            Check(Math.Abs(GeometryScenes.SampleVolumeCm3(restored)-3) < 1e-12,"XML template mesh");
            restored.SampleMesh = new GeometrySampleMesh { Packed = "invalid mesh" };
            Check(restored.MissingMaterialProblem() != null,"invalid mesh accepted");
        }
        finally { File.Delete(path); }
        Console.WriteLine("PASS: non-convex ray intervals, volume, .in and XML round trips, invalid mesh.");
    }
}
