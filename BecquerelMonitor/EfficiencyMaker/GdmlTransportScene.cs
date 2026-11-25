using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Windows.Media.Media3D;

namespace BecquerelMonitor.EfficiencyMaker
{
    // The original document and selected placement indices travel with the geometry.
    // Each worker owns its material caches but shares this immutable navigation geometry.
    public sealed class GdmlTransportScene
    {
        string packed, fingerprint;
        internal string Fingerprint => fingerprint ?? (fingerprint = GdmlCopyStore.Sha256Of(System.Text.Encoding.ASCII.GetBytes(packed ?? "")));
        Lazy<GdmlTransportGeometry> geometry;
        public string Packed
        {
            get { return packed; }
            set { packed = value; fingerprint = null; geometry = new Lazy<GdmlTransportGeometry>(() => Decode(value)); }
        }

        internal GdmlTransportGeometry Geometry
        {
            get
            {
                if (geometry == null) throw new InvalidDataException(GdmlText.T("GdmlTransportMissing","The original GDML scene is missing. Import the file again."));
                return geometry.Value;
            }
        }

        internal static GdmlTransportScene Capture(GdmlGeometry file, int crystal, int sample, bool shield, bool sourceOverride, Matrix3D worldToScene)
        {
            var result = new GdmlTransportScene();
            using (var buffer = new MemoryStream())
            {
                using (var zipped = new GZipStream(buffer,CompressionMode.Compress,true))
                using (var writer = new BinaryWriter(zipped))
                {
                    writer.Write(2);
                    writer.Write(crystal);
                    writer.Write(sample);
                    writer.Write(shield);
                    writer.Write(sourceOverride);
                    foreach (double n in new[] { worldToScene.M11,worldToScene.M12,worldToScene.M13,
                        worldToScene.M21,worldToScene.M22,worldToScene.M23,worldToScene.M31,worldToScene.M32,
                        worldToScene.M33,worldToScene.OffsetX,worldToScene.OffsetY,worldToScene.OffsetZ }) writer.Write(n);
                    writer.Write(file.TransportXml);
                }
                result.packed = Convert.ToBase64String(buffer.ToArray());
            }
            result.geometry = new Lazy<GdmlTransportGeometry>(() => new GdmlTransportGeometry(file,crystal,sample,shield,sourceOverride,worldToScene));
            return result;
        }

        static GdmlTransportGeometry Decode(string data)
        {
            if (string.IsNullOrEmpty(data)) throw new InvalidDataException(GdmlText.T("GdmlTransportMissing","The original GDML scene is missing. Import the file again."));
            using (var bytes = new MemoryStream(Convert.FromBase64String(data)))
            using (var zip = new GZipStream(bytes,CompressionMode.Decompress))
            using (var reader = new BinaryReader(zip))
            using (var doc = new MemoryStream())
            {
                int version = reader.ReadInt32();
                if (version < 1 || version > 2) throw new InvalidDataException("GDML scene version");
                int crystal = reader.ReadInt32(), sample = reader.ReadInt32();
                bool shield = reader.ReadBoolean();
                bool sourceOverride = version == 1 || reader.ReadBoolean();
                var frame = new Matrix3D(reader.ReadDouble(),reader.ReadDouble(),reader.ReadDouble(),0,
                    reader.ReadDouble(),reader.ReadDouble(),reader.ReadDouble(),0,
                    reader.ReadDouble(),reader.ReadDouble(),reader.ReadDouble(),0,
                    reader.ReadDouble(),reader.ReadDouble(),reader.ReadDouble(),1);
                zip.CopyTo(doc);
                return new GdmlTransportGeometry(GdmlGeometry.Read(doc.ToArray(),null),crystal,sample,shield,sourceOverride,frame);
            }
        }

        internal static GdmlTransportScene Restore(GdmlImportInfo info, GeometryModel model)
        {
            string copy = GdmlCopyStore.Find(info.Sha256);
            if (copy != null)
            {
                try
                {
                    var file = GdmlGeometry.Read(copy);
                    var overview = GdmlOverview.Of(file);
                    var initial = GdmlFieldMap.Build(file,overview);
                    var missing = new List<string>();
                    var choice = GdmlChoice.FromInfo(info,file,initial,missing,model.Source,missing);
                    var map = choice.Build(file,overview);
                    if (missing.Count == 0 && (map.Found & choice.Parts) == choice.Parts)
                    {
                        map.Apply(model, choice.Parts);
                        return model.TransportScene;
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return new GdmlTransportScene();
        }

        internal GdmlTransportGeometry.Part SampleOrNull
        {
            get { return Problem() == null ? Geometry.Sample : null; }
        }

        internal string Problem()
        {
            try { var value = Geometry; return null; }
            catch (Exception e) when (e is InvalidDataException || e is IOException || e is FormatException ||
                e is InvalidOperationException || e is ArgumentException)
            { return e.Message; }
        }
    }

    internal sealed class GdmlTransportGeometry
    {
        internal sealed class Part
        {
            internal string Name;
            internal int Depth;
            internal Rect3D Bounds;
            internal Matrix3D Inverse;
            internal GdmlTransportBody Body;
            internal GeometryMaterial Material;
            internal Part Parent, MediumOwner;
            internal readonly List<Part> Children = new List<Part>();
            internal double OwnVolumeCm3;
            internal bool Contains(Point3D p) { return Bounds.Contains(p) && Body.Contains(Inverse.Transform(p)); }
            internal int Spans(GdmlTransportBody.Ray ray, ref double[] spans)
            {
                double lo, hi;
                if (!GdmlTransportBody.BoxSpan(Bounds,ray,out lo,out hi) || hi < 0) return 0;
                return Body.Spans(ray.Transform(Inverse),ref spans);
            }

        }

        internal readonly Part[] Parts;
        internal readonly Part Crystal, Sample, World;
        internal readonly bool ImportedShield, SourceOverride;
        internal readonly List<string> Warnings = new List<string>();

        internal GeometryMaterial MaterialOf(Part part, GeometryMaterial sampleOverride)
        {
            if (SourceOverride && Sample != null)
            {
                for (var owner = part; owner != null; owner = owner.Parent)
                {
                    if (owner == Sample) return sampleOverride;
                    if (owner == owner.MediumOwner) break;
                }
            }
            return part.Material;
        }

        internal GdmlTransportGeometry(GdmlGeometry geo, int crystal, int sample, bool shield, bool sourceOverride, Matrix3D frame)
        {
            if (geo.FatalError != null || geo.World == null || crystal < 0 || crystal >= geo.Volumes.Count ||
                sample < -1 || sample >= geo.Volumes.Count || !frame.HasInverse)
                throw new InvalidDataException(geo.FatalError ?? "Invalid GDML scene or placement");
            ImportedShield = shield;
            SourceOverride = sourceOverride;
            var bodies = new Dictionary<GdmlSolid,GdmlTransportBody>();
            var placed = new Dictionary<GdmlVolumeInstance,Part>();
            var materials = new Dictionary<GdmlMaterial,GeometryMaterial>();
            foreach (var v in new[] { geo.World }.Concat(geo.Volumes))
            {
                if (v.Solid == null || !v.PlacementKnown || !string.IsNullOrEmpty(v.UnknownContent) || !v.BoundsKnown)
                    throw new InvalidDataException(GdmlText.T("GdmlIncompleteTransport","Cannot simulate an incomplete GDML volume") + ": " + v.VolumeName + " " + v.UnknownContent);
                var body = GdmlTransportBody.Get(v.Solid,bodies);
                var transform = GdmlModelMesh.Placement(v.Transform) * frame;
                var part = new Part { Name = v.InstanceName, Depth = v.Depth, Body = body,
                    Bounds = new MatrixTransform3D(transform).TransformBounds(body.Bounds),
                    Inverse = GdmlTransportBody.Inverse(transform), OwnVolumeCm3 = v.OwnVolumeCm3 };
                if (v.Parent != null)
                {
                    part.Parent = placed[v.Parent];
                    part.Parent.Children.Add(part);
                }
                if (string.IsNullOrWhiteSpace(v.MaterialName))
                {
                    part.MediumOwner = part.Parent?.MediumOwner;
                    part.Material = part.Parent?.Material ?? GdmlFieldMap.ToField(GdmlG4Materials.Table["G4_AIR"]);
                    Warnings.Add(v.VolumeName + ": " + GdmlText.T("GdmlInheritedMedium","No material specified; using the surrounding medium"));
                }
                else
                {
                    part.MediumOwner = part;
                    var material = v.Material;
                    if (material == null || material.Unknown || material.Partial || !material.HasDensity || !(material.Density > 0) || material.Fractions.Count == 0)
                        throw new InvalidDataException(GdmlText.T("GdmlUnknownMedium","Unknown composition or density") + ": " + v.VolumeName + " / " + v.MaterialName + (material?.Missing.Count > 0 ? " (" + string.Join(", ",material.Missing) + ")" : ""));
                    if (!materials.TryGetValue(material,out part.Material))
                        materials.Add(material,part.Material = GdmlFieldMap.ToField(material));
                }
                placed.Add(v,part);
            }
            foreach (var solid in bodies.Keys.Where(s => s.Kind == GdmlSolidKind.Tessellated && !s.VolumeKnown))
                Warnings.Add(solid.Name + ": " + GdmlText.T("GdmlTransportOpen","Open mesh: tracking uses the original facets; paths at holes are uncertain."));
            Crystal = placed[geo.Volumes[crystal]];
            if (sample >= 0) Sample = placed[geo.Volumes[sample]];
            World = placed[geo.World];
            Parts = geo.Volumes.AsEnumerable().Reverse().Select(v => placed[v]).OrderByDescending(p => p.Depth).Concat(new[] { World }).ToArray();
        }
    }
}
