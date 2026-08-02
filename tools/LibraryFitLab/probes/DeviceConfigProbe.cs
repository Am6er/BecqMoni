using BecquerelMonitor;
using System;
using System.IO;
using System.Text;
using System.Xml.Serialization;

// Переживает ли DeviceConfigInfo непустой Note.
//
// Note объявлен как CDATA (IXmlSerializable), а CDATA.ReadXml зовёт
// ReadElementString(), который читает элемент вместе с закрывающим тегом. Отсюда
// была гипотеза, что XmlSerializer после этого закроет ещё один элемент и всё,
// что идёт за Note — PolynomialEnergyCalibration, StabilizerConfig,
// DoseRateConfig, PeakDetectionMethodConfig, — потеряется.
//
// Проба это опровергает: во всех вариантах Note (пустой, текст, CDATA,
// многострочный, записанный самим приложением) конфигурация читается целиком.
// Так и должно быть — XmlSerializationReader.ReadSerializable для члена типа
// IXmlSerializable вызывает ReadXml и больше ничего не делает, то есть прочитать
// элемент целиком и есть контракт. Оставлено в дереве, чтобы гипотезу не
// выдвигали второй раз.
//
// Сборка (после сборки основного проекта):
//   csc /target:exe /platform:anycpu /langversion:7.3 /out:<wd>\DeviceConfigProbe.exe ^
//       /r:<wd>\BecquerelMonitor.exe /r:System.dll /r:System.Core.dll ^
//       /r:System.Xml.dll /r:System.Xml.Serialization.dll /r:System.Drawing.dll ^
//       /r:System.Windows.Forms.dll tools\LibraryFitLab\probes\DeviceConfigProbe.cs

class DeviceConfigProbe
{
    const string Template = @"<?xml version=""1.0""?>
<DeviceConfigInfo xmlns:xsd=""http://www.w3.org/2001/XMLSchema"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"">
  <FormatVersion>120920</FormatVersion>
  <Guid>probe-guid</Guid>
  <Name>probe</Name>
  <LastUpdated>2026-07-27T00:00:00.0000000+03:00</LastUpdated>
  <DefaultMeasurementTime>360000000</DefaultMeasurementTime>
  <ChannelPitch>1</ChannelPitch>
  <NumberOfChannels>8192</NumberOfChannels>
  <DeviceType>AtomSpectraVCP</DeviceType>
  <ThermometerType>None</ThermometerType>
  <EnergyCalibrationType>Polynomial</EnergyCalibrationType>
  {0}
  <PolynomialEnergyCalibration>
    <PolynomialOrder>2</PolynomialOrder>
    <Coefficients><Coefficient>1</Coefficient><Coefficient>0.4</Coefficient><Coefficient>0</Coefficient></Coefficients>
  </PolynomialEnergyCalibration>
  <StabilizerConfig><TargetPeaks /></StabilizerConfig>
  <DoseRateConfig><DoseRateCalibrationPoints /></DoseRateConfig>
  <PeakDetectionMethodConfig>
    <Min_SNR>4</Min_SNR><FWHM_AT_0>10</FWHM_AT_0><Ch_Fwhm>1877</Ch_Fwhm><Width_Fwhm>101</Width_Fwhm>
    <Max_Items>40</Max_Items><Tolerance>77</Tolerance><Min_Range>11</Min_Range><Max_Range>2222</Max_Range>
    <Min_FWHM_Tol>10</Min_FWHM_Tol><Max_FWHM_Tol>190</Max_FWHM_Tol><Enabled>true</Enabled>
    <Ch_Concat>4096</Ch_Concat><PeakType>0</PeakType>
    <ExpGaussExpLeftTail>1</ExpGaussExpLeftTail><ExpGaussExpRightTail>1</ExpGaussExpRightTail>
  </PeakDetectionMethodConfig>
  <BackgroundSpectrumPathname />
</DeviceConfigInfo>";

    static readonly XmlSerializer Serializer = new XmlSerializer(typeof(DeviceConfigInfo));

    static DeviceConfigInfo Load(string xml)
    {
        using (var reader = new StringReader(xml))
        {
            return (DeviceConfigInfo)Serializer.Deserialize(reader);
        }
    }

    static string Describe(DeviceConfigInfo device)
    {
        var peak = device.PeakDetectionMethodConfig as FWHMPeakDetectionMethodConfig;
        if (peak == null)
        {
            return "PeakDetectionMethodConfig == null";
        }

        var calibration = device.EnergyCalibration as PolynomialEnergyCalibration;
        bool ok = peak.Min_Range == 11 && peak.Max_Range == 2222 &&
                  peak.Ch_Concat == 4096 && peak.Tolerance == 77 &&
                  calibration != null && calibration.PolynomialOrder == 2;
        return string.Format("{0}  Min_Range={1} Max_Range={2} Ch_Concat={3} Tolerance={4} ecalOrder={5}",
            ok ? "ЦЕЛА " : "ПОТЕРЯНА",
            peak.Min_Range, peak.Max_Range, peak.Ch_Concat, peak.Tolerance,
            calibration == null ? -1 : calibration.PolynomialOrder);
    }

    static void Case(string label, string note)
    {
        try
        {
            Console.WriteLine("{0,-30} {1}", label, Describe(Load(string.Format(Template, note))));
        }
        catch (Exception ex)
        {
            Console.WriteLine("{0,-30} ИСКЛЮЧЕНИЕ {1}", label, ex.GetBaseException().Message);
        }
    }

    static void Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("ожидается Min_Range=11 Max_Range=2222 Ch_Concat=4096 Tolerance=77 ecalOrder=2");
        Case("<Note />", "<Note />");
        Case("<Note></Note>", "<Note></Note>");
        Case("<Note>текст</Note>", "<Note>plain text</Note>");
        Case("<Note><![CDATA[...]]></Note>", "<Note><![CDATA[hello note]]></Note>");
        Case("<Note> две строки </Note>", "<Note>line one\nline two</Note>");
        Case("без Note", "");

        // и то же самое через собственный writer приложения
        DeviceConfigInfo device = Load(string.Format(Template, "<Note />"));
        device.Note = "written by the app\nsecond line";
        var text = new StringBuilder();
        using (var writer = new StringWriter(text))
        {
            Serializer.Serialize(writer, device);
        }

        Console.WriteLine("--- запись приложением и чтение обратно ---");
        using (var reader = new StringReader(text.ToString()))
        {
            Console.WriteLine("{0,-30} {1}", "round-trip",
                Describe((DeviceConfigInfo)Serializer.Deserialize(reader)));
        }
    }
}
