using BecquerelMonitor;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Xml.Serialization;

// V2: третья кривая разрешения — степенная FWHM = a·ch^p.
//
// Проверяется то, чего не видит компилятор:
//
//   1. ПОДГОНКА возвращает то, из чего сделаны точки. Кривая строится по
//      логарифмам, и ошибка в них не «падает», а тихо даёт другую степень.
//   2. КРУГОВОРОТ ЧЕРЕЗ XML. Кривая хранится в конфиге прибора и в файле
//      спектра как подтип абстрактной `FwhmCalibration`; забытый
//      `[XmlElement(typeof(...))]` — это не ошибка компиляции, а молчаливая
//      потеря калибровки при сохранении.
//   3. ПРИЁМКА формы. Модель обязана расти, а относительная ширина — падать;
//      степень вне (0, 1) принимать нельзя.
//   4. СПИСОК В ФОРМЕ и перечисление кривых совпадают по длине: строка списка,
//      которой нет в перечислении (или наоборот), даёт выбор, молча
//      переключающий не на ту кривую.
//
//   fwhmpowerprobe
class FwhmPowerProbe
{
    static int bad;

    static int Main(string[] args)
    {
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        Console.OutputEncoding = Encoding.UTF8;

        Fit();
        RoundTrip();
        Guard();
        ListLength();

        Console.WriteLine();
        Console.WriteLine(bad == 0 ? "ВСЕ ПРОВЕРКИ ПРОШЛИ" : "ПРОВАЛОВ: " + bad);
        return bad == 0 ? 0 : 1;
    }

    static void Check(string what, bool ok, string detail)
    {
        Console.WriteLine("   {0,-46} {1}{2}", what, ok ? "ок" : "ПЛОХО",
                          detail == null ? "" : "  " + detail);
        if (!ok) bad++;
    }

    /// <summary>Точки делаются ИЗ ИЗВЕСТНОЙ степени — подгонка обязана её вернуть.</summary>
    static void Fit()
    {
        Console.WriteLine("1. Подгонка возвращает заданную степень");
        const double a = 0.42, p = 0.61;
        var cal = new PowerFwhmCalibration();
        foreach (int ch in new[] { 50, 120, 300, 700, 1500, 3000 })
        {
            cal.CalibrationPeaks.Add(new CalibrationPeak
            {
                Channel = ch,
                Energy = ch * 2.9,
                FWHM = a * Math.Pow(ch, p),
            });
        }

        bool done = cal.PerformCalibration(4096);
        Check("подгонка принята", done, null);
        Check("амплитуда", Math.Abs(cal.Coefficients[0] - a) < 1e-6 * a,
              cal.Coefficients[0].ToString("G6"));
        Check("показатель", Math.Abs(cal.Coefficients[1] - p) < 1e-9,
              cal.Coefficients[1].ToString("G6"));
        Check("ширина на 662-м канале", Math.Abs(cal.ChannelToFwhm(662) - a * Math.Pow(662, p)) < 1e-9,
              cal.ChannelToFwhm(662).ToString("G6"));
        Check("обратный ход канал<-ширина",
              Math.Abs(cal.FwhmToChannel(cal.ChannelToFwhm(662)) - 662.0) < 1e-6, null);
    }

    /// <summary>Кривая обязана пережить сохранение и чтение конфига прибора.</summary>
    static void RoundTrip()
    {
        Console.WriteLine("2. Круговорот через XML конфигурации поиска пиков");
        var config = new FWHMPeakDetectionMethodConfig();
        var cal = new PowerFwhmCalibration();
        cal.CalibrationPeaks.Add(new CalibrationPeak { Channel = 100, Energy = 290, FWHM = 6.0 });
        cal.CalibrationPeaks.Add(new CalibrationPeak { Channel = 1000, Energy = 2900, FWHM = 24.0 });
        cal.PerformCalibration(4096);
        config.FwhmCalibration = cal;

        var serializer = new XmlSerializer(typeof(FWHMPeakDetectionMethodConfig));
        string text;
        using (var writer = new StringWriter())
        {
            serializer.Serialize(writer, config);
            text = writer.ToString();
        }

        Check("тип назван в файле", text.Contains("PowerFwhmCalibration"), null);

        FWHMPeakDetectionMethodConfig back;
        using (var reader = new StringReader(text))
        {
            back = (FWHMPeakDetectionMethodConfig)serializer.Deserialize(reader);
        }

        var got = back.FwhmCalibration as PowerFwhmCalibration;
        Check("прочиталась тем же типом", got != null, back.FwhmCalibration == null
              ? "null" : back.FwhmCalibration.GetType().Name);
        if (got == null) return;

        Check("коэффициенты целы",
              Math.Abs(got.Coefficients[0] - cal.Coefficients[0]) < 1e-12 &&
              Math.Abs(got.Coefficients[1] - cal.Coefficients[1]) < 1e-12, null);
        Check("опорные точки целы", got.CalibrationPeaks.Count == 2, null);
    }

    /// <summary>Нефизичную форму принимать нельзя — ни падающую, ни быстрее линейной.</summary>
    static void Guard()
    {
        Console.WriteLine("3. Приёмка формы");
        Check("степень выше единицы отвергнута", !Fits(1.4), null);
        Check("падающая кривая отвергнута", !Fits(-0.3), null);
        Check("физичная степень принята", Fits(0.6), null);
    }

    static bool Fits(double power)
    {
        var cal = new PowerFwhmCalibration();
        foreach (int ch in new[] { 100, 400, 1600 })
        {
            cal.CalibrationPeaks.Add(new CalibrationPeak
            {
                Channel = ch,
                Energy = ch * 2.9,
                FWHM = 0.5 * Math.Pow(ch, power),
            });
        }

        return cal.PerformCalibration(4096);
    }

    /// <summary>Список в форме и перечисление кривых обязаны совпадать по длине.</summary>
    static void ListLength()
    {
        Console.WriteLine("4. Список формы против перечисления кривых");
        int inEnum = Enum.GetValues(typeof(FwhmCalibration.FwhmCalibrationCurve)).Length;
        var view = new System.Resources.ResourceManager(
            "BecquerelMonitor.DCFwhmCalibrationView", typeof(DCFwhmCalibrationView).Assembly);
        int inList = 0;
        while (true)
        {
            string key = inList == 0 ? "selectCurveComboBox.Items"
                                     : "selectCurveComboBox.Items" + inList.ToString(CultureInfo.InvariantCulture);
            if (view.GetString(key) == null) break;
            inList++;
        }

        Check("длины совпали", inEnum == inList,
              string.Format("перечисление {0}, список {1}", inEnum, inList));
    }
}
