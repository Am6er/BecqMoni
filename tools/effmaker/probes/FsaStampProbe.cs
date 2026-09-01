using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Xml.Serialization;

namespace FsaStampProbe
{
    /// <summary>
    /// Читатель отпечатка разложения (`A31`).
    ///
    /// ЗАЧЕМ ПРОБА. Разложение пересчитывается, только когда сменился ОТПЕЧАТОК
    /// входных данных (<c>FsaOverlay.BuildStamp</c>). Набора нуклидов в нём не
    /// было, и правка набора на экране не меняла ничего: состав библиотеки идёт
    /// от подписей пиков, подписи ставит набор, а пересчитывать их после правки
    /// было некому. Признак починен — здесь у него появляется читатель, иначе
    /// он снова станет неотличим от «оно и так работало».
    ///
    ///     fsastampprobe --spectrum=X.xml
    ///
    /// Проверяется ТРИ вещи:
    ///
    ///   1. смена ВЫБОРА набора (все нуклиды → набор) меняет отпечаток;
    ///   2. правка СОСТАВА набора (нуклид добавлен в него) меняет отпечаток;
    ///   3. возврат состава назад возвращает и отпечаток — то есть отпечаток
    ///      следит за содержанием, а не просто «что-то трогали».
    ///
    /// Ожидание: «ВСЕ СОШЛИСЬ».
    /// </summary>
    static class Program
    {
        static int bad;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            string spectrumPath = null;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal))
                {
                    spectrumPath = a.Substring(11);
                }
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: " + a);
                    return 2;
                }
            }

            if (spectrumPath == null)
            {
                Console.Error.WriteLine("нужен --spectrum=<файл>");
                return 2;
            }

            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();

            ResultData rd = Load(spectrumPath);
            if (rd == null)
            {
                return 2;
            }

            if (nuclides.NuclideSets == null || nuclides.NuclideSets.Count == 0)
            {
                Console.Error.WriteLine("в конфигурации нет ни одного набора — проверять нечего");
                return 2;
            }

            MethodInfo build = typeof(FsaOverlay).GetMethod(
                "BuildStamp", BindingFlags.Static | BindingFlags.NonPublic);
            if (build == null)
            {
                Console.Error.WriteLine("нет FsaOverlay.BuildStamp");
                return 2;
            }

            Func<string> stamp = () => (string)build.Invoke(null, new object[] { rd, true });

            nuclides.ActiveSet = null;
            string all = stamp();
            Console.WriteLine("все нуклиды      : {0}", Tail(all));

            NuclideSet set = nuclides.NuclideSets[0];
            nuclides.ActiveSet = set;
            string chosen = stamp();
            Console.WriteLine("набор «{0}»: {1}", set.Name, Tail(chosen));
            Check("выбор набора меняет отпечаток", all != chosen);

            // Правка СОСТАВА: берётся нуклид, которого в наборе ещё нет.
            NuclideDefinition victim = null;
            foreach (NuclideDefinition definition in nuclides.NuclideDefinitions)
            {
                if (definition != null && definition.Sets != null && !definition.Sets.Contains(set.Id))
                {
                    victim = definition;
                    break;
                }
            }

            if (victim == null)
            {
                Console.Error.WriteLine("в наборе уже все нуклиды — правку состава проверить нечем");
                return 2;
            }

            victim.Sets.Add(set.Id);
            string edited = stamp();
            Console.WriteLine("+ «{0}»       : {1}", victim.Name, Tail(edited));
            Check("правка состава набора меняет отпечаток", chosen != edited);

            victim.Sets.Remove(set.Id);
            string restored = stamp();
            Check("возврат состава возвращает отпечаток", chosen == restored);

            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "РАСХОЖДЕНИЙ: " + bad.ToString(CultureInfo.InvariantCulture));
            return bad == 0 ? 0 : 1;
        }

        /// <summary>Хвост отпечатка — набор и пики; начало у всех одинаково.</summary>
        static string Tail(string stamp)
        {
            if (string.IsNullOrEmpty(stamp))
            {
                return "(пусто)";
            }

            string[] parts = stamp.Split('|');
            return parts.Length >= 2
                ? parts[parts.Length - 2]
                : stamp;
        }

        static void Check(string what, bool ok)
        {
            Console.WriteLine("{0}: {1}", ok ? "СОШЛОСЬ " : "РАЗОШЛОСЬ", what);
            if (!ok)
            {
                bad++;
            }
        }

        static ResultData Load(string path)
        {
            var serializer = new XmlSerializer(typeof(ResultDataFile));
            ResultDataFile file;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                file = (ResultDataFile)serializer.Deserialize(stream);
            }

            if (file.ResultDataList == null || file.ResultDataList.Count == 0)
            {
                Console.Error.WriteLine("в файле нет ни одного результата");
                return null;
            }

            ResultData rd = file.ResultDataList[0];
            ProbeDeviceConfig.Attach(rd);
            return rd;
        }
    }
}
