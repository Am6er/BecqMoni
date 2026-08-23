using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;

// Что означает `Visible` у линии нуклида — S31.
//
// ⚠ Общего правила `ProbeDeviceConfig` (`S82`) здесь НЕТ И НЕ НАДО (`T59`,
// разобрано 23.08.2026): поиск пиков проба не запускает вовсе — упоминание
// `PeakDetector` ниже это ссылка на его правило отбора, а не вызов. Работает
// она с определениями нуклидов и составом образа, где прибора нет.
//
// Поле одно на «рисовать» и «подписывать пик» (решение Amber 08.08.2026), а
// СОСТАВ ОБРАЗА в полноспектральном разборе от него не зависит вовсе. Правило
// записано у самого поля (`NuclideDefinition.Visible`); проба здесь потому,
// что глазами его не проверить: обе половины видны только в числах.
//
// До 08.08.2026 `FsaLibrary` фильтровала определения по `Visible`, и весь
// добор недостающих линий скрытыми записями — на который опирается поставочный
// конфиг — в образ не попадал ни разу: у Bi-214 в файле 21 линия, в образе
// оказывалось 7.
//
//     visibleruleprobe
//
// Ожидание: «ВСЕ СОШЛИСЬ».
class VisibleRuleProbe
{
    static int bad;

    static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

        HiddenLinesEnterTemplate();
        HidingTheOnlyLabelDropsNuclide();
        ShippedConfigLeansOnThis();

        Console.WriteLine();
        Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "РАСХОЖДЕНИЙ: " + bad);
        return bad == 0 ? 0 : 1;
    }

    /// <summary>
    /// Скрытая линия ВХОДИТ в образ: она из спектра не исчезла, и образ без
    /// неё занижен.
    /// </summary>
    static void HiddenLinesEnterTemplate()
    {
        Console.WriteLine();
        Console.WriteLine("=== Скрытая линия входит в образ");

        List<NuclideDefinition> definitions = new List<NuclideDefinition>
        {
            Line("Co-60", 1173.23, 99.85, true),
            Line("Co-60", 1332.49, 99.98, false),   // спрятана
            Line("Cs-137", 661.66, 85.1, true),
        };

        List<FsaComponent> library = FsaLibrary.BuildFromPeaks(
            new List<Peak> { Peak(definitions[0]), Peak(definitions[2]) }, definitions);

        FsaComponent cobalt = library.FirstOrDefault(c => c.Name == "Co-60");
        if (cobalt == null)
        {
            Fail("кобальта нет в библиотеке вовсе: " + Names(library));
            return;
        }

        Same("линий кобальта в образе", cobalt.Lines.Count, 2);
        if (!cobalt.Lines.Any(l => Math.Abs(l.Energy - 1332.49) < 0.01))
        {
            Fail("спрятанная 1332.49 не вошла в образ — это и был дефект S31");
        }
        else
        {
            Console.WriteLine("    ок: спрятанная 1332.49 в образе есть");
        }
    }

    /// <summary>
    /// Обратная сторона того же правила и настоящая ловушка: спрятав
    /// ЕДИНСТВЕННУЮ линию, которой финдер подписывал нуклид, его убирают из
    /// разложения ЦЕЛИКОМ — вместе со всеми скрытыми линиями.
    ///
    /// Скрытая линия сама привести нуклид в разбор не может: подписывает пики
    /// поиск, а он скрытые пропускает (`PeakDetector`, отбор по `Visible`).
    /// Здесь это выражено прямо — пика с таким нуклидом просто нет на входе,
    /// потому что финдер его не породил бы.
    /// </summary>
    static void HidingTheOnlyLabelDropsNuclide()
    {
        Console.WriteLine();
        Console.WriteLine("=== Спрятана единственная подпись — нуклид уходит целиком");

        List<NuclideDefinition> definitions = new List<NuclideDefinition>
        {
            Line("Co-60", 1173.23, 99.85, false),   // спрятана — финдер её не даст
            Line("Co-60", 1332.49, 99.98, false),
            Line("Cs-137", 661.66, 85.1, true),
        };

        // Финдер подписал только цезий: кобальтовых видимых линий не осталось.
        List<FsaComponent> library = FsaLibrary.BuildFromPeaks(
            new List<Peak> { Peak(definitions[2]) }, definitions);

        if (library.Any(c => c.Name == "Co-60"))
        {
            Fail("кобальт попал в разбор без единого подписанного пика — "
                 + "скрытая линия привела компонент сама, чего быть не должно");
        }
        else
        {
            Console.WriteLine("    ок: без подписанного пика кобальта в разборе нет");
        }

        if (!library.Any(c => c.Name == "Cs-137"))
        {
            Fail("цезий рядом тоже пропал: " + Names(library));
        }
        else
        {
            Console.WriteLine("    ок: подписанный сосед на месте");
        }
    }

    /// <summary>
    /// На это правило опирается ПОСТАВОЧНЫЙ конфиг: недостающие линии дописаны
    /// туда скрытыми записями нарочно (`tools/nucdb/fill_intensity.py`, второй
    /// проход). Если фильтр вернуть, образы молча обеднеют — проверяется на
    /// самом файле поставки, а не на выдуманном наборе.
    /// </summary>
    static void ShippedConfigLeansOnThis()
    {
        Console.WriteLine();
        Console.WriteLine("=== Поставочный конфиг: образ полнее видимой части");

        string path = System.IO.Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "config", "NuclideDefinition.xml");
        if (!System.IO.File.Exists(path))
        {
            Console.WriteLine("    пропущено: рядом с exe нет config\\NuclideDefinition.xml");
            return;
        }

        NuclideDefinitionFile file;
        using (System.IO.FileStream stream = System.IO.File.OpenRead(path))
        {
            file = (NuclideDefinitionFile)new System.Xml.Serialization.XmlSerializer(
                typeof(NuclideDefinitionFile)).Deserialize(stream);
        }

        foreach (string nuclide in new[] { "Bi-214", "Ac-228", "Eu-152" })
        {
            NuclideDefinition label = file.NuclideDefinitions
                .FirstOrDefault(d => d.Name == nuclide && d.Visible && d.Intencity > 0.0);
            if (label == null)
            {
                Console.WriteLine("    пропущено: в конфиге нет видимой линии " + nuclide);
                continue;
            }

            List<FsaComponent> library = FsaLibrary.BuildFromPeaks(
                new List<Peak> { Peak(label) }, file.NuclideDefinitions);
            FsaComponent component = library.FirstOrDefault(c => c.Name == nuclide);
            int visible = file.NuclideDefinitions.Count(
                d => d.Name == nuclide && d.Visible && d.Intencity > 0.0);
            int all = component == null ? 0 : component.Lines.Count;
            if (all <= visible)
            {
                Fail(string.Format("{0}: в образе {1} линий при {2} видимых — "
                                   + "добор не доехал", nuclide, all, visible));
            }
            else
            {
                Console.WriteLine(string.Format(
                    "    ок: {0} — в образе {1} линий, из них видимых {2}",
                    nuclide, all, visible));
            }
        }
    }

    // --------------------------------------------------------------

    static NuclideDefinition Line(string name, double energy, double intensity, bool visible)
    {
        return new NuclideDefinition
        {
            Name = name,
            Energy = energy,
            Intencity = intensity,
            Visible = visible,
        };
    }

    static Peak Peak(NuclideDefinition definition)
    {
        return new Peak { Nuclide = definition };
    }

    static string Names(IEnumerable<FsaComponent> library)
    {
        return string.Join(", ", library.Select(c => c.Name));
    }

    static void Same(string what, int got, int expected)
    {
        if (got == expected)
        {
            Console.WriteLine("    ок: {0} = {1}", what, got);
        }
        else
        {
            Fail(string.Format("{0} = {1} вместо {2}", what, got, expected));
        }
    }

    static void Fail(string message)
    {
        bad++;
        Console.WriteLine("    !! " + message);
    }
}
