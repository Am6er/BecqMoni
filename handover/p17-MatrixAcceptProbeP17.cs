using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Serialization;

// Полоса П17, 06.09.2026 — ПРИЁМКА `A269`, а не диагностика.
//
// Вопрос ровно один: сколько корпусных спектров получают матрицу склада. Судит
// так же, как разбор (`CorpusFsaProbe`, `FsaAnalysisSession.Capture`): грузит
// `.rmx` по Guid кривой и спрашивает `matrix.IsValidFor(rd.Efficiency.Geometry)`
// — то есть сверяет клеймо файла с геометрией, ЛЕЖАЩЕЙ В СПЕКТРЕ.
//
// Склад читается прямо из `corpus/geometries/response/<guid>.rmx`, минуя
// `ResponseMatrixStore`: рабочий каталог с `config\device\response` для этого
// вопроса не нужен, а лишний шаг копирования — лишний источник разницы.
//
//   MatrixAcceptProbeP17.exe --corpus=<...\corpus>
//
// Печатает строку TSV на спектр: ключ, имя кривой, guid, состояние.
static class MatrixAcceptProbeP17
{
    [STAThread]
    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        string corpus = null;
        foreach (string a in args)
        {
            if (a.StartsWith("--corpus=", StringComparison.Ordinal)) corpus = a.Substring(9);
            else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
        }
        if (corpus == null) { Console.Error.WriteLine("нужен --corpus="); return 2; }

        string store = Path.Combine(Path.Combine(corpus, "geometries"), "response");
        var serializer = new XmlSerializer(typeof(ResultDataFile));
        string[] files = Directory.GetFiles(Path.Combine(corpus, "spectra"), "*.xml");
        Array.Sort(files, StringComparer.Ordinal);

        int good = 0, bad = 0, nofile = 0, nogeom = 0;
        Console.WriteLine("спектр\tкривая\tguid\tсостояние");
        foreach (string path in files)
        {
            string key = Path.GetFileNameWithoutExtension(path);
            ResultDataFile file;
            using (var st = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                file = (ResultDataFile)serializer.Deserialize(st);
            ResultData rd = file.ResultDataList[0];
            if (rd.Efficiency == null || !rd.Efficiency.HasGeometry)
            {
                nogeom++;
                Console.WriteLine(key + "\t\t\tБЕЗ ГЕОМЕТРИИ");
                continue;
            }

            string rmx = Path.Combine(store, rd.Efficiency.Guid + ".rmx");
            MatrixRefusal refusal;
            int fileFormat;
            ResponseMatrix m = File.Exists(rmx)
                ? ResponseMatrix.Load(rmx, out refusal, out fileFormat)
                : null;
            if (m == null)
            {
                nofile++;
                Console.WriteLine(key + "\t" + rd.Efficiency.Name + "\t" + rd.Efficiency.Guid
                                  + "\tФАЙЛА НЕТ");
                continue;
            }

            bool ok = m.IsValidFor(rd.Efficiency.Geometry);
            if (ok) good++; else bad++;
            Console.WriteLine(key + "\t" + rd.Efficiency.Name + "\t" + rd.Efficiency.Guid
                              + "\t" + (ok ? "СОШЛОСЬ" : "РАЗОШЛОСЬ"));
        }

        Console.WriteLine();
        Console.WriteLine("сошлось {0}, разошлось {1}, файла нет {2}, без геометрии {3} (спектров {4})",
                          good, bad, nofile, nogeom, files.Length);
        return 0;
    }
}
