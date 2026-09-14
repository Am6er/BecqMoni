using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Serialization;

// П74 (T260): одноразовый экспортёр — геометрия из узла <Efficiency> спектра в .in
// писателем ПРИЛОЖЕНИЯ (GeometryWriter.Save) с проверкой круга: GeometryModel.Load(.in)
// -> Render равен Render(геометрия файла), клеймо ComputeStamp то же.
//   ExportScene.exe <спектр.xml> <куда.in>
// Собирается csc против каталога проб и запускается ИЗ КОПИИ каталога проб (matdb рядом).
static class ExportScene
{
    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        if (args.Length < 2) { Console.Error.WriteLine("ExportScene <спектр.xml> <куда.in>"); return 2; }
        var serializer = new XmlSerializer(typeof(ResultDataFile));
        ResultDataFile file;
        using (var s = new FileStream(args[0], FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            file = (ResultDataFile)serializer.Deserialize(s);
        }
        ResultData rd = file.ResultDataList[0];
        if (rd.Efficiency == null) { Console.Error.WriteLine("у спектра нет узла Efficiency"); return 1; }
        Console.WriteLine("guid кривой: {0}  имя: {1}  origin: {2}  ComputeStamp кривой: {3}",
                          rd.Efficiency.Guid, rd.Efficiency.Name, rd.Efficiency.Origin, rd.Efficiency.ComputeStamp);
        GeometryModel g = rd.Efficiency.Geometry;
        if (g == null) { Console.Error.WriteLine("у кривой нет геометрии"); return 1; }
        string render0 = GeometryWriter.Render(g);
        GeometryWriter.Save(g, args[1]);
        GeometryModel back = GeometryModel.Load(args[1]);
        string render1 = GeometryWriter.Render(back);
        var opt = new ResponseMatrixOptions();
        string stamp0 = ResponseMatrix.ComputeStamp(g, opt);
        string stamp1 = ResponseMatrix.ComputeStamp(back, opt);
        Console.WriteLine("круг Render: {0}", render0 == render1 ? "СОШЁЛСЯ побайтно" : "РАЗОШЁЛСЯ");
        Console.WriteLine("клеймо геометрии файла (умолчания класса): {0}", stamp0);
        Console.WriteLine("клеймо геометрии из .in                 : {0}", stamp1);
        Console.WriteLine("InShield={0} Shape={1} SourceType={2} Scene={3} Name={4}", g.InShield, g.Shape, g.SourceType, g.Scene, g.Name);
        if (render0 != render1)
        {
            File.WriteAllText(args[1] + ".render0.txt", render0);
            File.WriteAllText(args[1] + ".render1.txt", render1);
            return 1;
        }
        if (args.Length >= 3)
        {
            GeometryModel other = GeometryModel.Load(args[2]);
            string renderO = GeometryWriter.Render(other);
            string stampO = ResponseMatrix.ComputeStamp(other, opt);
            Console.WriteLine("сцена {0}: Render {1}, клеймо {2}", args[2],
                              renderO == render0 ? "РАВЕН файлу" : "ДРУГОЙ", stampO);
            if (renderO != render0)
            {
                File.WriteAllText(args[1] + ".file.txt", render0);
                File.WriteAllText(args[1] + ".scene.txt", renderO);
            }
        }
        return stamp0 == stamp1 ? 0 : 1;
    }
}
