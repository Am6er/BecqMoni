using System;
using System.Collections.Generic;
using System.Text;

namespace BecquerelMonitor.EfficiencyMaker
{
    internal static class GdmlPartDetails
    {
        static string T(string key, string english) { return GdmlText.T("GdmlInfo" + key, english); }
        static string N(double x) { return GdmlText.N(x); }
        static string Value(double x, string unit)
        {
            return x >= 0 && !double.IsInfinity(x) ? N(x) + " " + unit : T("Unknown", "Unknown");
        }

        static string Triple(double x, double y, double z, string separator)
        {
            return N(x) + separator + N(y) + separator + N(z);
        }

        static void Row(StringBuilder text, string label, string value)
        {
            if (!string.IsNullOrEmpty(value))
                text.Append(label).Append("   ").AppendLine(value);
        }

        internal static string Describe(GdmlVolumeInstance part)
        {
            var text = new StringBuilder();
            string mm = GdmlText.T("GdmlUnitMm", "mm"), cm3 = GdmlText.T("GdmlUnitCm3", "cm³");
            var solid = part.Solid;
            var material = part.Material;

            Row(text, T("Material", "Material"), part.MaterialName);
            Row(text, T("Shape", "Shape"), solid.Tag);
            if (solid.BoundsKnown)
                Row(text, T("LocalSize", "Local size (X × Y × Z)"),
                    Triple(solid.Max[0]-solid.Min[0], solid.Max[1]-solid.Min[1], solid.Max[2]-solid.Min[2], " × ") + " " + mm);
            if (solid.Tag == "tube" && solid.Planes.Count == 2)
            {
                var end = solid.Planes[0];
                Row(text, T("OuterDiameter", "Outer diameter"), Value(2 * end[2], mm));
                if (end[1] > 0) Row(text, T("InnerDiameter", "Bore diameter"), Value(2 * end[1], mm));
                Row(text, T("Height", "Height"), Value(Math.Abs(solid.Planes[1][0] - end[0]), mm));
            }
            Row(text, T("Mass", "Mass"), Value(part.MassG, GdmlText.T("GdmlUnitG", "g")));
            Row(text, T("Volume", "Solid volume"), Value(part.VolumeCm3, cm3));
            if (part.Children != 0 || part.OwnVolumeCm3 != part.VolumeCm3)
                Row(text, T("OwnVolume", "Material volume"), Value(part.OwnVolumeCm3, cm3));
            text.AppendLine();
            if (material != null)
            {
                Row(text, T("Density", "Density"), material.HasDensity
                    ? Value(material.Density, T("DensityUnit", "g/cm³")) : T("Unknown", "Unknown"));
                var declared = material.FileDefinition;
                if (declared != null)
                {
                    Row(text, T("State", "Material state"), declared.State);
                    var fractions = new List<string>();
                    var atomCounts = new List<string>();
                    foreach (var entry in declared.Refs)
                    {
                        if (entry.IsAtoms) atomCounts.Add(entry.Reference + " " + N(entry.Fraction));
                        else fractions.Add(entry.Reference + " " + N(100 * entry.Fraction) + "%");
                    }
                    Row(text, T("DeclaredFractions", "Mass fractions in file"), string.Join(", ", fractions));
                    Row(text, T("DeclaredAtoms", "Atom counts in file"), string.Join(", ", atomCounts));
                }
                Row(text, T("Fractions", "Element mass fractions"), material.Fractions.Count > 0
                    ? material.CompositionText() : T("Unknown", "Unknown"));
                if (material.Missing.Count != 0)
                    Row(text, T("Missing", "Unresolved components"), string.Join(", ", material.Missing));
                if (material.AtomicMass > 0)
                    Row(text, T("AtomicMass", "Atomic mass"), Value(material.AtomicMass, T("AtomicUnit", "g/mol")));
                string source = material.Origin == "file" ? "GDML" : material.Origin == "G4" ? T("G4", "Built-in G4 table") :
                    material.Origin == "file + G4" ? "GDML + " + T("G4", "Built-in G4 table") : T("Unknown", "Unknown");
                Row(text, T("MaterialSource", "Material data source"), source);
                text.AppendLine();
            }

            Row(text, T("Logical", "Logical volume"), part.VolumeName);
            Row(text, T("Placement", "Physical placement"), part.InstanceName);
            Row(text, T("Solid", "Solid"), part.SolidName);
            var chain = new Stack<string>();
            for (var item = part; item != null; item = item.Parent)
                chain.Push(string.IsNullOrEmpty(item.InstanceName) ? item.VolumeName : item.InstanceName);
            Row(text, T("Path", "Placement path"), string.Join(" / ", chain));
            Row(text, T("Children", "Child placements"), N(part.Children));
            if (part.PlacementKnown && part.Transform != null)
            {
                GdmlPoint origin = part.Transform.Apply(new GdmlPoint());
                Row(text, T("WorldOrigin", "Origin in world (X, Y, Z)"), Triple(origin.X, origin.Y, origin.Z, ", ") + " " + mm);
                text.AppendLine(T("Axes", "Local axes in world coordinates"));
                for (int axis = 0; axis < 3; axis++)
                {
                    GdmlPoint direction = part.Transform.ImageOfAxis(axis);
                    Row(text, "  " + "XYZ"[axis], Triple(direction.X, direction.Y, direction.Z, ", "));
                }
            }
            if (part.BoundsKnown)
            {
                Row(text, T("WorldMin", "World minimum"), Triple(part.Min[0], part.Min[1], part.Min[2], ", ") + " " + mm);
                Row(text, T("WorldMax", "World maximum"), Triple(part.Max[0], part.Max[1], part.Max[2], ", ") + " " + mm);
            }
            if (solid.Kind == GdmlSolidKind.Tessellated)
            {
                Row(text, T("Vertices", "Vertices"), N(solid.Vertices.Count));
                Row(text, T("Triangles", "Triangles"), N(solid.Triangles.Count / 3));
            }
            if (solid.Kind == GdmlSolidKind.Polycone || solid.Kind == GdmlSolidKind.GenericPolycone)
            {
                Row(text, T("Angles", "Start angle / sweep"), N(solid.StartPhi) + "° / " + N(solid.DeltaPhi) + "°");
                bool planes = solid.Kind == GdmlSolidKind.Polycone;
                var profile = planes ? solid.Planes : solid.Profile;
                string columns = planes ? T("Planes", "Planes (Z, inner R, outer R)") : T("Profile", "Profile points (R, Z)");
                text.AppendLine(columns + ", " + mm);
                for (int i = 0; i < Math.Min(profile.Count, 12); i++)
                {
                    var p = profile[i];
                    text.Append("  ").AppendLine(planes ? Triple(p[0], p[1], p[2], ", ") : N(p[0]) + ", " + N(p[1]));
                }
                if (profile.Count > 12) text.AppendLine(T("MoreProfile", "The GDML tab contains the full profile."));
            }
            if (GdmlModelMesh.IsBoolean(solid))
            {
                Row(text, T("Operand1", "First operand"), solid.FirstRef);
                Row(text, T("Operands", "Other operands"), string.Join(", ", solid.SecondRefs));
            }
            foreach (string note in new[] { solid.Note, part.MassNote, part.UnknownContent })
                if (!string.IsNullOrEmpty(note)) text.AppendLine(note);
            return text.ToString().TrimEnd();
        }
    }
}
