using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Media.Media3D;
using System.Xml.Linq;
using System.Xml.Serialization;
using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;

internal static class GdmlTransportProbe
{
    const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static void Require(bool ok, string detail) { if (!ok) throw new Exception(detail); }
    static object Field(object target, string field) { return target.GetType().GetField(field,Members).GetValue(target); }

    static XElement Volume(string name, string solid, string material, params XElement[] children)
    {
        return new XElement("volume",new XAttribute("name",name),
            material == null ? null : new XElement("materialref",new XAttribute("ref",material)),
            new XElement("solidref",new XAttribute("ref",solid)),children);
    }

    static XElement Place(string name, double x=0, double y=0, double z=0)
    {
        return new XElement("physvol",new XAttribute("name","PV_"+name),
            new XElement("volumeref",new XAttribute("ref",name)),new XElement("position",
            new XAttribute("x",x),new XAttribute("y",y),new XAttribute("z",z),new XAttribute("unit","mm")));
    }

    static XElement Box(string name, double width)
    {
        return new XElement("box",new XAttribute("name",name),new XAttribute("lunit","mm"),
            new XAttribute("x",width),new XAttribute("y",width),new XAttribute("z",width));
    }

    static string Scene(string dir, string worldMaterial, string core, string label)
    {
        var world = Volume("World","world",worldMaterial,Place("Al"),Place("Crystal",0,53),Place("Aux",60,0));
        for (int i=0; i<66; i++) world.Add(Place("Dot",i*2-65,-75));
        var xml = new XElement("gdml",new XElement("materials"),
            new XElement("solids",Box("world",200),Box("outer",40),Box("inner",20),Box("core",4),Box("xtal",10),Box("tiny",1)),
            new XElement("structure",Volume("Crystal","xtal","G4_SODIUM_IODIDE"),Volume("Al","outer","G4_Al",Place("Water")),
            Volume("Water","inner","G4_WATER",Place("Core")),Volume("Core","core",core),Volume("Aux","core",null),
            Volume("Dot","tiny","G4_Al"),world),
            new XElement("setup",new XAttribute("name","default"),new XAttribute("version","1"),new XElement("world",new XAttribute("ref","World"))));
        string path = Path.Combine(dir,label+".gdml");
        xml.Save(path);
        return path;
    }

    static GeometryModel Import(string path,string crystal,string sample=null,GeometryMaterial material=null)
    {
        var geo = GdmlGeometry.Read(path);
        Require(geo.FatalError == null,geo.FatalError);
        var map = GdmlFieldMap.Build(geo,GdmlOverview.Of(geo),geo.Volumes.First(v=>v.VolumeName==crystal),
            sample == null ? null : geo.Volumes.First(v=>v.VolumeName==sample),null,material);
        var parts = GdmlParts.Detector | (sample == null ? GdmlParts.None : GdmlParts.Sample);
        Require((map.Found & parts)==parts,"mapping: "+map.Refusal+" "+map.SampleProblem);
        var model = GeometryEditorPanel.Blank();
        map.Apply(model,parts);
        return model;
    }

    static void Ray(GeometryModel model,double worldDensity,double coreDensity,double waterDensity=1)
    {
        var sim = new EfficiencySimulator(model);
        sim.EnsureBuiltForProbe();
        var regions = (Array)Field(sim,"regionArray");
        Require(regions.Length>64,"expected a multiword active set");
        var world = regions.Cast<object>().Select(r=>Field(r,"Gdml")).Single(p=>p!=null && (int)Field(p,"Depth")==0);
        var frame=(Matrix3D)Field(world,"Inverse");
        frame.Invert();
        var origin=frame.Transform(new Point3D(-90,0,0));
        var d=frame.Transform(new Vector3D(10,0,0));
        sim.GetType().GetMethod("CollectCrossings",Members).Invoke(sim,new object[]{origin.X,origin.Y,origin.Z,d.X,d.Y,d.Z});
        var cuts=(double[])Field(sim,"rayCross");
        var owners=(Array)Field(sim,"raySeg");
        var expected = new[] {7.0,8,8.8,9.2,10,11,14.8,15.2,19};
        var rho = new[] {worldDensity,2.699,waterDensity,coreDensity,waterDensity,2.699,worldDensity,worldDensity,worldDensity};
        Require((int)Field(sim,"rayCount")==expected.Length,"wrong number of boundaries");
        for(int i=0;i<expected.Length;i++)
        {
            Require(Math.Abs(cuts[i]-expected[i])<1e-7,"boundary "+i+" actual "+cuts[i]);
            var material=(GeometryMaterial)Field(owners.GetValue(i),"Material");
            Require(Math.Abs(material.Density-rho[i]) < Math.Max(1e-32,rho[i]*1e-9),"medium "+i+" "+material.Name+" "+material.Density);
        }
        Require(owners.GetValue(expected.Length)==null,"world exit");
    }

    static void CheckLegacyImport(string dir)
    {
        string path = Scene(dir,"G4_AIR","G4_Galactic","legacy");
        var oldGeometry = Import(path,"Crystal","Water");
        oldGeometry.TransportScene = null;
        var info = new GdmlImportInfo { FileName=Path.GetFileName(path), Sha256=GdmlCopyStore.Put(File.ReadAllBytes(path)),
            CrystalPart="Crystal", SamplePart="Water", Sample=true };
        var config = new EfficiencyConfigData("legacy") { Geometry=oldGeometry, GdmlImport=info };
        Ray(config.Geometry,.00120479,1e-25);
        var oldWithoutCopy = oldGeometry.Clone();
        oldWithoutCopy.TransportScene = null;
        var missing = new EfficiencyConfigData("missing") { Geometry=oldWithoutCopy,
            GdmlImport=new GdmlImportInfo { FileName=info.FileName, Sha256=new string('0',64),
                CrystalPart=info.CrystalPart,SamplePart=info.SamplePart,Sample=true } };
        Require(!string.IsNullOrEmpty(missing.Geometry.MissingMaterialProblem()), "legacy import with missing file");
    }

    static void CheckSolidBoundaries(string dir)
    {
        var xml = XDocument.Load(Scene(dir,"G4_AIR","G4_WATER","solids"));
        var solids = xml.Root.Element("solids");
        solids.Add(new XElement("subtraction",new XAttribute("name","offsetHole"),
            new XElement("first",new XAttribute("ref","outer")),new XElement("second",new XAttribute("ref","inner")),
            new XElement("position",new XAttribute("x",4),new XAttribute("unit","mm"))));
        solids.Add(new XElement("tube",new XAttribute("name","halfTube"),new XAttribute("rmin",5),new XAttribute("rmax",10),
            new XAttribute("z",20),new XAttribute("startphi",0),new XAttribute("deltaphi",180),new XAttribute("aunit","deg"),new XAttribute("lunit","mm")));
        var structure = xml.Root.Element("structure");
        structure.Add(Volume("Cut","offsetHole","G4_Al"),Volume("Tube","halfTube","G4_Al"));
        var world = structure.Elements("volume").Single(v=>(string)v.Attribute("name")=="World");
        var rotated = Place("Cut",0,0,45);
        rotated.Add(new XElement("rotation",new XAttribute("z",90),new XAttribute("unit","deg")));
        world.Add(rotated,Place("Tube",0,0,-45));
        string path = Path.Combine(dir,"placed-solids.gdml");
        xml.Save(path);
        var sim = new EfficiencySimulator(Import(path,"Crystal"));
        sim.EnsureBuiltForProbe();
        var parts = ((Array)Field(sim,"regionArray")).Cast<object>().Select(r=>Field(r,"Gdml")).Where(p=>p!=null).ToList();
        var cut = parts.Single(p=>(string)Field(p,"Name")=="PV_Cut");
        ExpectSpans(cut,0,new[] {0d,1.4,3.4,4});
        var tube = parts.Single(p=>(string)Field(p,"Name")=="PV_Tube");
        double extent = Math.Sqrt(91)/10;
        ExpectSpans(tube,3,new[] {2-extent,1.6,2.4,2+extent});
        ExpectSpans(tube,-3,new double[0]);
    }

    static void ExpectSpans(object part, double y, double[] expected)
    {
        var frame=(Matrix3D)Field(part,"Inverse");
        frame.Invert();
        var hit = part.GetType().GetMethod("Spans",Members);
        Type ray = hit.GetParameters()[0].ParameterType;
        object[] args = { Activator.CreateInstance(ray,Members,null,new object[] {
            frame.Transform(new Point3D(-20,y,0)),frame.Transform(new Vector3D(10,0,0))},null),new double[16] };
        int count=(int)hit.Invoke(part,args);
        Require(2*count==expected.Length,"number of solid boundaries: "+Field(part,"Name"));
        var actual=(double[])args[1];
        for(int i=0;i<expected.Length;i++) Require(Math.Abs(actual[i]-expected[i])<1e-8,"solid boundary "+i+" actual "+actual[i]);
    }

    static void CheckSampleOwnership(string dir)
    {
        var replacement = new GeometryMaterial { Name = "Test carbon", Density = 2 };
        replacement.Fractions.Add(6,1);
        string path = Scene(dir,"G4_AIR",null,"override");
        Ray(Import(path,"Crystal","Water",replacement),.00120479,2,2);
        var crystal = Import(path,"Core","Water",replacement).Crystal;
        Require(crystal.Density == 2 && crystal.Fractions.ContainsKey(6), "inherited crystal sample override");
        path = Scene(dir,"G4_AIR","G4_Galactic","override-explicit");
        Ray(Import(path,"Crystal","Water",replacement),.00120479,1e-25,2);

        var xml = XDocument.Load(path);
        var structure = xml.Root.Element("structure");
        structure.Add(Volume("Blocker","inner","G4_Al"));
        structure.Elements("volume").Single(v=>(string)v.Attribute("name")=="Al").Add(Place("Blocker",10));
        path = Path.Combine(dir,"overlap.gdml");
        xml.Save(path);
        var sim = new EfficiencySimulator(Import(path,"Crystal","Water"));
        sim.EnsureBuiltForProbe();
        object source = Field(sim,"source"), part = Field(source,"part");
        var local = (Matrix3D)Field(part,"Inverse");
        var next = source.GetType().GetMethod("Next",Members);
        for (int i=0; i<256; i++)
        {
            object[] args = {sim,0d,0d,0d};
            next.Invoke(source,args);
            var p = local.Transform(new Point3D((double)args[1],(double)args[2],(double)args[3]));
            Require(p.X < 0 && (Math.Abs(p.X)>2 || Math.Abs(p.Y)>2 || Math.Abs(p.Z)>2),
                "source emitted in a sibling or a daughter");
        }
    }

    [STAThread] static int Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string dir=args[0];
        Directory.CreateDirectory(dir);
        foreach (bool vacuumWorld in new[] {false,true})
        {
            var g=Import(Scene(dir,vacuumWorld?"G4_Galactic":null,"G4_Galactic",vacuumWorld?"vacuum":"air"),"Crystal");
            Ray(g,vacuumWorld?1e-25:.00120479,1e-25);
            string saved=Path.Combine(dir,"scene.in");
            GeometryWriter.Save(g,saved);
            var loaded=GeometryModel.Load(saved);
            Require(loaded.TransportScene.Packed==g.TransportScene.Packed,"scene .in round trip");
            Ray(loaded,vacuumWorld?1e-25:.00120479,1e-25);
            var ser=new XmlSerializer(typeof(GeometryModel));
            using(var buffer=new MemoryStream())
            {
                ser.Serialize(buffer,g); buffer.Position=0;
                Ray((GeometryModel)ser.Deserialize(buffer),vacuumWorld?1e-25:.00120479,1e-25);
            }
        }
        var missing=Import(Scene(dir,"G4_AIR","UnknownNamedMedium","unknown"),"Crystal");
        Require(missing.MissingMaterialProblem()?.Contains("UnknownNamedMedium")==true,"unknown named medium was accepted");
        var inherited=Import(Scene(dir,"G4_AIR",null,"inherited"),"Crystal","Core");
        Require(inherited.Source.Density==1 && inherited.Source.Fractions.Count==2,"inherited sample composition");
        Ray(inherited,.00120479,1);
        var inheritedCrystal=Import(Scene(dir,"G4_AIR",null,"inherited-crystal"),"Core");
        Require(inheritedCrystal.Crystal.Density==1 && inheritedCrystal.Crystal.Fractions.Count==2,"inherited crystal");
        CheckLegacyImport(dir);
        CheckSolidBoundaries(dir);
        CheckSampleOwnership(dir);
        Console.WriteLine("PASS: legacy import and missing copy, rotated subtraction, partial tube, sample override, daughter and sibling ownership, >64 placements, original boundaries, parent medium, default air, explicit vacuum, named unknown, source, crystal, .in and XML.");
        if(args.Length>1)
        {
            var g=Import(args[1],"LV_NaI_crystal","LV_Statuette");
            g.FwhmAt662Percent=7.1;
            GeometryWriter.Save(g,Path.Combine(dir,"GS2020-original.in"));
            foreach (var warning in g.Warnings) Console.WriteLine(warning);
            var options=new ResponseMatrixOptions {MinEnergyKev=40, MaxEnergyKev=662, NodeCount=4, Histories=2000, Threads=2, JointNodes=0, ResolveEdges=false};
            var matrix=ResponseMatrixBuilder.Build(g,options,null,CancellationToken.None);
            matrix.Save(Path.Combine(dir,"control.rmat"));
            Require(matrix.Energies.Length>=4 && matrix.PeakEfficiencyResolution.All(p=>p>0 && p<1),"matrix peak rows");
            Console.WriteLine("Matrix rows "+string.Join(", ",matrix.Energies.Zip(matrix.PeakEfficiencyResolution,(e,v)=>e.ToString("F2")+" keV "+v.ToString("G6"))));
        }
        return 0;
    }
}
