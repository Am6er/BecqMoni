using BecquerelMonitor;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Serialization;

// Правильно ли читается энергетическая калибровка спектра.
//
// Повод: PolynomialEnergyCalibration.ChannelToEnergy разбирал только степени
// 4, 3 и 2, а всё остальное молча уходило в линейную ветку. Спектр с
// калибровкой 5-й степени (их пишет SpectraLine ЛСРМ) открывался с неверной
// шкалой по всему диапазону и без единого сообщения об ошибке. Обратное
// преобразование в той же ситуации бросало NotImplementedException, а
// DetectPeak вызывается под catch-all в DCPeakDetectionView — то есть поиск
// пиков на таком спектре молча не работал.
//
// Проба печатает энергию по каналам, тем же полиномом, посчитанным вручную, и
// разницу; плюс проверяет обратное преобразование на замыкание. Если ветка
// степени потеряна, столбец «разница» немедленно это покажет.
//
// Сборка (после сборки основного проекта):
//   csc /target:exe /platform:anycpu /langversion:7.3 /out:<wd>\CalibrationProbe.exe ^
//       /r:<wd>\BecquerelMonitor.exe /r:System.dll /r:System.Core.dll ^
//       /r:System.Xml.dll /r:System.Xml.Serialization.dll /r:System.Drawing.dll ^
//       /r:System.Windows.Forms.dll tools\LibraryFitLab\probes\CalibrationProbe.cs
//
// Запуск:  CalibrationProbe.exe <spectrum.xml>

class CalibrationProbe
{
    static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("CalibrationProbe.exe <spectrum.xml>");
            return 2;
        }

        Console.OutputEncoding = Encoding.UTF8;

        ResultDataFile file;
        var serializer = new XmlSerializer(typeof(ResultDataFile));
        using (var stream = File.OpenRead(args[0]))
        {
            file = (ResultDataFile)serializer.Deserialize(stream);
        }

        ResultData rd = file.ResultDataList.First();
        EnergySpectrum es = rd.EnergySpectrum;
        var calibration = es.EnergyCalibration as PolynomialEnergyCalibration;
        if (calibration == null)
        {
            Console.Error.WriteLine("калибровка не полиномиальная");
            return 1;
        }

        double[] c = calibration.Coefficients;
        Console.WriteLine("каналов      {0}", es.NumberOfChannels);
        Console.WriteLine("степень      {0}, коэффициентов {1}", calibration.PolynomialOrder, c.Length);
        for (int i = 0; i < c.Length; i++)
        {
            Console.WriteLine("  a{0} = {1:E10}", i, c[i]);
        }
        Console.WriteLine();
        Console.WriteLine("{0,8} {1,16} {2,16} {3,14} {4,12}",
            "канал", "ChannelToEnergy", "полином вручную", "разница, кэВ", "обратно");

        bool bad = false;
        int[] channels = { 0, 1, es.NumberOfChannels / 8, es.NumberOfChannels / 4,
                           es.NumberOfChannels / 2, es.NumberOfChannels * 3 / 4,
                           es.NumberOfChannels - 1 };
        foreach (int n in channels)
        {
            double fromApp = calibration.ChannelToEnergy(n);

            // тот же полином схемой Горнера, независимо от приложения
            int top = Math.Min(calibration.PolynomialOrder, c.Length - 1);
            double manual = 0.0;
            for (int i = top; i >= 0; i--)
            {
                manual = manual * n + c[i];
            }

            double back;
            try
            {
                back = calibration.EnergyToChannel(manual, es.NumberOfChannels);
            }
            catch (Exception ex)
            {
                back = double.NaN;
                Console.WriteLine("  EnergyToChannel бросил {0}", ex.GetType().Name);
            }

            double delta = fromApp - manual;
            if (Math.Abs(delta) > 0.01)
            {
                bad = true;
            }
            Console.WriteLine("{0,8} {1,16:F3} {2,16:F3} {3,14:F3} {4,12:F1}",
                n, fromApp, manual, delta, back);
        }

        Console.WriteLine();
        Console.WriteLine(bad
            ? "ПЛОХО: приложение читает калибровку не тем полиномом, который записан"
            : "ок: шкала совпадает с записанным полиномом");
        return bad ? 1 : 0;
    }
}
