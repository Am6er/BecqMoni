using BecquerelMonitor;
using System;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

// Диагностика «финдер не нашёл ни одного пика».
//
// Печатает всё, от чего зависит поиск, — какая конфигурация до него доехала,
// во сколько каналов пересыпается спектр, какой ширины получается ядро,
// докуда дотягивается диапазон, — потом отдельно гоняет PeakFinder напрямую и
// PeakDetector.DetectPeak целиком. Именно эта пара и разошлась на германии:
// напрямую находилось 40 пиков, через DetectPeak — ноль, и разница оказалась в
// том, что LoadResultData брал не ту конфигурацию (см. README, раздел про
// [XmlIgnore]).
//
// Сборка (после сборки основного проекта):
//   csc /target:exe /platform:anycpu /langversion:7.3 /out:<wd>\PeakFinderProbe.exe ^
//       /r:<wd>\BecquerelMonitor.exe /r:System.dll /r:System.Core.dll ^
//       /r:System.Xml.dll /r:System.Xml.Serialization.dll /r:System.Drawing.dll ^
//       /r:System.Windows.Forms.dll tools\LibraryFitLab\probes\PeakFinderProbe.cs
//
// Запуск:  PeakFinderProbe.exe <workdir> <spectrum.xml>

class PeakFinderProbe
{
    static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("PeakFinderProbe.exe <workdir> <spectrum.xml>");
            return 2;
        }

        Environment.CurrentDirectory = args[0];
        GlobalConfigManager.GetInstance();
        DeviceConfigManager.GetInstance();
        NuclideDefinitionManager.GetInstance();

        ResultDataFile file;
        var serializer = new XmlSerializer(typeof(ResultDataFile));
        using (var stream = File.OpenRead(args[1]))
        {
            file = (ResultDataFile)serializer.Deserialize(stream);
        }

        ResultData rd = file.ResultDataList.First();
        DeviceConfigInfo device = DeviceConfigManager.GetInstance().DeviceConfigList
            .FirstOrDefault(d => d.Guid == rd.DeviceConfigReference.Guid);
        if (device == null)
        {
            Console.Error.WriteLine("нет конфигурации устройства для GUID " + rd.DeviceConfigReference.Guid);
            return 1;
        }

        var config = (FWHMPeakDetectionMethodConfig)
            ((FWHMPeakDetectionMethodConfig)device.PeakDetectionMethodConfig).Clone();
        EnergySpectrum es = rd.EnergySpectrum;
        rd.DeviceConfig = device;
        rd.PeakDetectionMethodConfig = config;
        rd.ROIConfig = null;

        Console.WriteLine("устройство   {0}", device.Name);
        Console.WriteLine("спектр       {0} каналов, {1} отсчётов, фон {2}",
            es.NumberOfChannels, es.TotalPulseCount,
            rd.BackgroundEnergySpectrum == null ? "нет" : "есть");
        Console.WriteLine("конфиг       Min_SNR={0} Min_Range={1} Max_Range={2} Ch_Concat={3} " +
                          "Tolerance={4} Max_Items={5} FWHM_Tol=[{6},{7}]",
            config.Min_SNR, config.Min_Range, config.Max_Range, config.Ch_Concat,
            config.Tolerance, config.Max_Items, config.Min_FWHM_Tol, config.Max_FWHM_Tol);
        Console.WriteLine("FWHM-калибр  {0}",
            rd.FwhmCalibration == null ? "НЕТ" : rd.FwhmCalibration.GetType().Name);

        var spectrum = new BecquerelMonitor.FWHMPeakDetector.Spectrum(es);
        int mul = es.NumberOfChannels / Math.Max(1, config.Ch_Concat);
        if (mul > 1)
        {
            spectrum.combine_bins(mul);
        }

        var kernel = new BecquerelMonitor.FWHMPeakDetector.PeakFilter(rd.FwhmCalibration);
        var finder = new BecquerelMonitor.FWHMPeakDetector.PeakFinder(
            spectrum, kernel,
            fwhm_tol_min: (double)config.Min_FWHM_Tol / 100.0,
            fwhm_tol_max: (double)config.Max_FWHM_Tol / 100.0);

        int lo = Convert.ToInt32(es.EnergyCalibration.EnergyToChannel(
            config.Min_Range, maxChannels: es.NumberOfChannels));
        int hi = Convert.ToInt32(es.EnergyCalibration.EnergyToChannel(
            config.Max_Range, maxChannels: es.NumberOfChannels));
        int mid = es.NumberOfChannels / 2;
        Console.WriteLine("пересыпка    mul={0}, рабочих каналов {1}", mul, spectrum.counts.Length);
        Console.WriteLine("ядро         FWHM({0}) = {1:F2} канала, макс SNR по спектру {2:F1}",
            mid, kernel.fwhm(mid), finder.snr.Max());
        Console.WriteLine("диапазон     {0}..{1} кэВ -> каналы {2}..{3}",
            config.Min_Range, config.Max_Range, lo, hi);

        // Локальные максимумы SNR: если их ноль, дело в ядре или в пересыпке;
        // если их много, а пиков нет — режет ширинный фильтр в add_peak.
        int maxima = 0;
        for (int i = 1; i < finder.snr.Length - 1; i++)
        {
            if (finder.snr[i - 1] < finder.snr[i] && finder.snr[i] >= finder.snr[i + 1] &&
                finder.snr[i] >= config.Min_SNR)
            {
                maxima++;
            }
        }
        Console.WriteLine("максимумов SNR >= {0}: {1}", config.Min_SNR, maxima);

        finder.find_peaks(lo, hi, config.Min_SNR, config.Max_Items);
        Console.WriteLine("find_peaks   -> {0}", finder.centroids == null ? 0 : finder.centroids.Length);

        var visible = new PeakDetector().DetectPeak(rd, BackgroundMode.Visible, SmoothingMethod.None, null);
        var subtract = new PeakDetector().DetectPeak(rd, BackgroundMode.Substract, SmoothingMethod.None, null);
        Console.WriteLine("DetectPeak   Visible -> {0}, Substract -> {1}", visible.Count, subtract.Count);
        return 0;
    }
}
