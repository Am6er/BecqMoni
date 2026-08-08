using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml.Serialization;

// S13: A/B «жёсткая связка против отвязки» и скан порога доверия континууму
// матрицы. ЧИТАТЕЛЬ ручки FsaAnalyzer.ResponseContinuumTrustFloorKev — она
// заведена ради этого замера и в UI не выведена (0 — отвязки нет вовсе, весь
// континуум образа жёстко привязан к пику своей линии).
//
// Проба САМОДОСТАТОЧНА и ничего не пишет: с ключом --geometry она берёт
// геометрию из файла `.in`, считает по ней кривую эффективности и матрицу
// отклика В ПАМЯТИ. Так сделано не для красоты — кривой «Цилиндр», на
// которой снят §11а журнала матрицы, в конфигурациях больше нет НИ В ОДНОЙ
// (проверено 08.08.2026: ни %AppData%\BecqMoni, ни bin\Debug), а подставить
// вместо неё чужую кривую значит подменить УРОВЕНЬ ответа. Цена честности:
// абсолютные числа с §11а не сходятся и сходиться не должны — сравнивать
// внутри одного прогона.
//
//   trustfloorprobe --spectrum=X.xml (--geometry=Y.in | --efficiency=имя)
//                   [--floors=0,80,100,120,150] [--n=300000]
class TrustFloorProbe
{
    static int Main(string[] args)
    {
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        string spectrumPath = null, efficiencyName = null, geometryPath = null;
        double[] floors = { 0.0, 80.0, 100.0, 120.0, 150.0 };
        int histories = 300000;
        foreach (string a in args)
        {
            if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrumPath = a.Substring(11);
            else if (a.StartsWith("--efficiency=", StringComparison.Ordinal)) efficiencyName = a.Substring(13);
            else if (a.StartsWith("--geometry=", StringComparison.Ordinal)) geometryPath = a.Substring(11);
            else if (a.StartsWith("--n=", StringComparison.Ordinal))
                histories = int.Parse(a.Substring(4), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--floors=", StringComparison.Ordinal))
                floors = a.Substring(9).Split(',')
                          .Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();
            else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
        }

        if (spectrumPath == null) { Console.Error.WriteLine("нужен --spectrum=<файл>"); return 2; }

        GlobalConfigManager.GetInstance();
        DeviceConfigManager.GetInstance();
        NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();

        ResultData rd = Load(spectrumPath);
        if (efficiencyName != null && !AttachEfficiency(rd, efficiencyName)) return 2;
        if (geometryPath != null && !AttachFromGeometry(rd, geometryPath)) return 2;
        if (rd.Efficiency == null || !rd.Efficiency.HasGeometry)
        {
            Console.Error.WriteLine("у кривой нет геометрии — матрицу строить не из чего");
            return 1;
        }

        List<Peak> peaks = new PeakDetector().DetectPeak(
            rd, BackgroundMode.Invisible, SmoothingMethod.None,
            nuclides.ActiveSet, nuclides.NuclideDefinitions);
        List<FsaComponent> library = FsaLibrary.BuildFromPeaks(peaks, nuclides.NuclideDefinitions);
        Console.WriteLine("спектр {0}", Path.GetFileName(spectrumPath));
        Console.WriteLine("пиков {0}, компонентов {1}", peaks.Count, library.Count);
        if (library.Count == 0) { Console.Error.WriteLine("библиотека пуста"); return 1; }

        // Матрица — в памяти, БЕЗ записи в конфигурацию пользователя.
        var options = new ResponseMatrixOptions { Histories = histories };
        Console.WriteLine("строю матрицу в памяти: узлов {0}, историй {1}...",
                          options.NodeCount, options.Histories);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        ResponseMatrix matrix = ResponseMatrixBuilder.Build(
            rd.Efficiency.Geometry, options, null, CancellationToken.None);
        Console.WriteLine("  за {0:F0} с, физика {1}, ошибка континуума {2:F2} % (худший узел),"
                          + " {3:F2} % (взвешенная по вкладу узла)",
                          clock.Elapsed.TotalSeconds,
                          ResponseMatrix.PhysicsFromStamp(matrix.Stamp),
                          matrix.ContinuumRelativeError,
                          matrix.ContinuumWeightedError);

        FsaEfficiency efficiency = FsaEfficiency.FromConfig(rd.Efficiency);

        // Опора: без матрицы вовсе — с чем сравнивать выигрыш.
        var plainAnalyzer = new FsaAnalyzer();
        FsaResult plain = plainAnalyzer.Analyze(rd.EnergySpectrum, null, rd.FwhmCalibration,
                                                library, efficiency);
        Console.WriteLine();
        Console.WriteLine("без матрицы: chi2/ndf {0:F3}", plain == null ? Double.NaN : plain.Chi2Ndf);

        var results = new List<Tuple<double, FsaResult>>();
        foreach (double floor in floors)
        {
            var analyzer = new FsaAnalyzer
            {
                ResponseMatrix = matrix,
                ResponseContinuumTrustFloorKev = floor,
            };
            FsaResult r = analyzer.Analyze(rd.EnergySpectrum, null, rd.FwhmCalibration,
                                           library, efficiency);
            results.Add(Tuple.Create(floor, r));
            Console.WriteLine("порог {0,5:F0} кэВ: chi2/ndf {1:F3}{2}",
                              floor, r == null ? Double.NaN : r.Chi2Ndf,
                              floor == 0.0 ? "   <- жёсткая связка (отвязки нет)" : "");
        }

        // Состав: главное в S11 было не chi2, а ЗАНУЛЕНИЕ реальных компонентов.
        Console.WriteLine();
        Console.Write("{0,-14}", "компонент");
        Console.Write("{0,12}", "без матрицы");
        foreach (var t in results) Console.Write("{0,12}", t.Item1.ToString("F0") + " кэВ");
        Console.WriteLine();

        var names = new List<string>();
        foreach (FsaComponent c in library) if (!names.Contains(c.Name)) names.Add(c.Name);
        foreach (string name in names)
        {
            Console.Write("{0,-14}", name);
            Console.Write("{0,12}", Share(plain, name));
            foreach (var t in results) Console.Write("{0,12}", Share(t.Item2, name));
            Console.WriteLine();
        }

        Console.WriteLine();
        Console.WriteLine("(доля %, «-» = компонента нет в ответе, 0.00 = занулён NNLS)");
        Console.Write("{0,-14}{1,12}", "z", Zed(plain));
        foreach (var t in results) Console.Write("{0,12}", Zed(t.Item2));
        Console.WriteLine();
        return 0;
    }

    /// <summary>Диапазон z по НЕслужебным компонентам — признак здоровья состава.</summary>
    static string Zed(FsaResult r)
    {
        if (r == null) return "-";
        var zs = r.Components.Where(c => c.Kind != FsaComponentKind.Nuisance)
                             .Select(c => c.Z).ToList();
        return zs.Count == 0 ? "-"
            : zs.Min().ToString("F0", CultureInfo.InvariantCulture) + ".." +
              zs.Max().ToString("F0", CultureInfo.InvariantCulture);
    }

    static string Share(FsaResult r, string name)
    {
        if (r == null) return "-";
        foreach (FsaComponentResult c in r.Components)
        {
            if (string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                double total = 0.0, mine = 0.0;
                foreach (FsaComponentResult o in r.Components)
                {
                    if (o.Kind == FsaComponentKind.Nuisance) continue;
                    double s = o.Curve != null ? o.Curve.Sum() : 0.0;
                    total += s;
                    if (ReferenceEquals(o, c)) mine = s;
                }

                if (c.Kind == FsaComponentKind.Nuisance)
                {
                    double s = c.Curve != null ? c.Curve.Sum() : 0.0;
                    return (s > 0.0 ? "n:" + (total > 0.0 ? 100.0 * s / total : 0.0).ToString("F2") : "n:0");
                }

                return (total > 0.0 ? 100.0 * mine / total : 0.0).ToString("F2", CultureInfo.InvariantCulture);
            }
        }

        return "-";
    }

    /// <summary>
    /// Кривая и геометрия ИЗ ФАЙЛА `.in`, ничего из конфигурации пользователя.
    /// Нужно потому, что кривой «Цилиндр» прежних сеансов в живом конфиге нет
    /// вовсе (кривых там нет ни одной), а подставлять чужую молча нельзя:
    /// уровень кривой — это уровень ответа. Здесь всё из одной геометрии:
    /// кривая считается тем же переносом, что и матрица, поэтому A/B внутри
    /// прогона сравним, а с числами прежних сеансов — нет.
    /// </summary>
    static bool AttachFromGeometry(ResultData rd, string path)
    {
        GeometryModel geometry;
        try
        {
            geometry = GeometryModel.Load(path);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("геометрия не читается: " + e.Message);
            return false;
        }

        Console.WriteLine("геометрия из файла: {0}", Path.GetFileName(path));
        Console.WriteLine("    {0}", geometry.Describe());
        Console.WriteLine("считаю кривую эффективности той же геометрией...");
        var clock = System.Diagnostics.Stopwatch.StartNew();
        EfficiencyFitResult fit = EfficiencyCalculation.Run(
            geometry, new EfficiencyCalculationOptions { Histories = 200000 },
            null, null);
        if (fit == null || !fit.Ok)
        {
            Console.Error.WriteLine("кривая не посчиталась: " + (fit == null ? "null" : fit.Error));
            return false;
        }

        Console.WriteLine("    точек {0}, {1:F0}..{2:F0} кэВ, за {3:F0} с; клеймо: {4}",
                          fit.Curve.Count, fit.MinEnergy, fit.MaxEnergy,
                          clock.Elapsed.TotalSeconds, fit.ComputeStamp);

        rd.Efficiency = new EfficiencyConfigData("probe: " + Path.GetFileNameWithoutExtension(path))
        {
            Geometry = geometry,
            Curve = fit.Curve,
            Origin = EfficiencyOrigin.Simulation,
            ComputeStamp = fit.ComputeStamp,
        };
        return true;
    }

    static bool AttachEfficiency(ResultData rd, string name)
    {
        foreach (DeviceConfigInfo device in DeviceConfigManager.GetInstance().DeviceConfigList)
        {
            foreach (EfficiencyConfigData curve in device.EfficiencyConfigs)
            {
                if (string.Equals(curve.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    rd.Efficiency = curve.Copy();
                    Console.WriteLine("кривая «{0}» из прибора «{1}», геометрия {2}",
                                      curve.Name, device.Name,
                                      rd.Efficiency.HasGeometry ? "есть" : "НЕТ");
                    return true;
                }
            }
        }

        Console.Error.WriteLine("кривая «{0}» не нашлась ни в одном приборе", name);
        return false;
    }

    static ResultData Load(string path)
    {
        var serializer = new XmlSerializer(typeof(ResultDataFile));
        ResultDataFile file;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            file = (ResultDataFile)serializer.Deserialize(stream);
        }

        ResultData rd = file.ResultDataList[0];
        EnergySpectrum s = rd.EnergySpectrum;
        if (s != null && s.Spectrum != null && s.TotalPulseCount == 0)
        {
            long total = 0;
            for (int i = 0; i < s.Spectrum.Length; i++) total += s.Spectrum[i];
            s.TotalPulseCount = total;
            s.ValidPulseCount = total;
        }

        if (!(rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig)
            && rd.DeviceConfig != null
            && rd.DeviceConfig.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig fromDevice)
        {
            rd.PeakDetectionMethodConfig = (FWHMPeakDetectionMethodConfig)fromDevice.Clone();
        }

        if (rd.FwhmCalibration == null
            && rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig cfg)
        {
            if (cfg.FwhmCalibration == null && rd.EnergySpectrum != null)
            {
                cfg.FwhmCalibration = FwhmCalibration.DefaultCalibration(
                    cfg, rd.EnergySpectrum.EnergyCalibration);
            }

            if (cfg.FwhmCalibration != null) rd.FwhmCalibration = cfg.FwhmCalibration.Clone();
        }

        return rd;
    }
}
