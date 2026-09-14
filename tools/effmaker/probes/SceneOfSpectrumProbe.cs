using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Serialization;

namespace SceneOfSpectrumProbe
{
    /// <summary>
    /// Машинный дамп сцены ТОГО ЖЕ прибора и той же кривой, что у спектра, —
    /// вход для внешнего арбитра `tools/g4cf`.
    ///
    /// ЗАЧЕМ. Вопрос Amber 01.09.2026: «почему вообще появляются волны при
    /// такой-то огромной статистике? возьми моделирование GEANT4, прогони его
    /// по этому же спектру — там тоже такие волны?». Чтобы Geant4 считал ТУ ЖЕ
    /// сцену, её надо взять из кривой эффективности САМОГО спектра, а не
    /// набрать руками: геометрия живёт в файле спектра (`Efficiency.Geometry`),
    /// и второй её копии в дереве быть не должно.
    ///
    ///     sceneofspectrumprobe --spectrum=X.xml [--out=scene.txt]
    ///
    /// Печатает `DumpScene()` — тот же формат SCENE/mat/region/source/END, что
    /// читает `g4cf scene &lt;файл&gt;`.
    /// </summary>
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            string spectrumPath = null, outPath = null;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrumPath = a.Substring(11);
                else if (a.StartsWith("--out=", StringComparison.Ordinal)) outPath = a.Substring(6);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            if (spectrumPath == null)
            {
                Console.Error.WriteLine("нужен --spectrum=<файл>");
                return 2;
            }

            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();

            ResultDataFile file;
            var serializer = new XmlSerializer(typeof(ResultDataFile));
            using (var stream = new FileStream(spectrumPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                file = (ResultDataFile)serializer.Deserialize(stream);
            }

            if (file.ResultDataList == null || file.ResultDataList.Count == 0)
            {
                Console.Error.WriteLine("в файле нет ни одного результата");
                return 2;
            }

            ResultData rd = file.ResultDataList[0];
            if (rd.Efficiency == null || !rd.Efficiency.HasGeometry)
            {
                Console.Error.WriteLine("у спектра нет кривой с геометрией — сцену взять неоткуда");
                return 2;
            }

            Console.WriteLine("кривая: {0}", rd.Efficiency.Name);
            var simulator = new EfficiencySimulator(rd.Efficiency.Geometry);
            Console.WriteLine("сцена (человеку): {0}", simulator.DescribeScene());

            string dump = simulator.DumpScene();
            if (outPath != null)
            {
                File.WriteAllText(outPath, dump);
                Console.WriteLine("записано: {0}", outPath);
            }
            else
            {
                Console.WriteLine(dump);
            }

            return 0;
        }
    }
}
