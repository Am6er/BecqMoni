using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;
using BecquerelMonitor.Utils;
using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Serialization;

namespace ZoneInterferenceProbeP174
{
    /// <summary>
    /// (`S199`, полоса П174 28.09.2026) ПОМЕХА ПРИРОДНОГО СПУТНИКА У ЗОНЫ ROI —
    /// <see cref="BecquerelCoefficient.InterferenceForZone"/> на спектре корпуса:
    /// зоны с окном ±0.75·ПШПВ (ПШПВ — по кривой эффективности спектра, как у
    /// пробы П167) вокруг линий из списка. Печатает множитель, спутника и предка
    /// либо «помехи нет».
    ///
    ///   ZoneInterferenceProbeP174 --spectrum=X.xml [--zones=Ra-226:186.21,U-235:185.72,...] [--width=0.75]
    /// </summary>
    static class Program
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        [STAThread]
        static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            string spectrum = null;
            string zones = "Ra-226:186.211,U-235:185.715,Pb-214:351.932,Bi-214:609.312,Th-234:92.38,Cs-137:661.657,ROI 1:186.211";
            double width = 0.75;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrum = a.Substring(11);
                else if (a.StartsWith("--zones=", StringComparison.Ordinal)) zones = a.Substring(8);
                else if (a.StartsWith("--width=", StringComparison.Ordinal)) width = double.Parse(a.Substring(8), Inv);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            if (spectrum == null || !File.Exists(spectrum))
            {
                Console.Error.WriteLine("нужен --spectrum=<файл .xml>");
                return 2;
            }

            ResultData rd;
            var serializer = new XmlSerializer(typeof(ResultDataFile));
            using (var stream = new FileStream(spectrum, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                rd = ((ResultDataFile)serializer.Deserialize(stream)).ResultDataList[0];
            }

            var pcal = rd.EnergySpectrum.EnergyCalibration as PolynomialEnergyCalibration;
            if (pcal != null) pcal.CheckCalibration(rd.EnergySpectrum.NumberOfChannels);
            double fw = rd.Efficiency != null && rd.Efficiency.HasGeometry ? rd.Efficiency.Geometry.FwhmAt662Percent : 0.0;
            Console.WriteLine("спектр {0}: кривая {1}, ПШПВ@662 геометрии {2} %, калибровка ПШПВ {3}",
                              Path.GetFileName(spectrum), rd.Efficiency != null ? rd.Efficiency.Name : "нет",
                              fw.ToString("F2", Inv), rd.FwhmCalibration != null ? "есть" : "нет");
            Console.WriteLine("зона\tлиния, кэВ\tокно, кэВ\tпомеха");
            foreach (string item in zones.Split(','))
            {
                int colon = item.LastIndexOf(':');
                string name = item.Substring(0, colon);
                double e = double.Parse(item.Substring(colon + 1), Inv);
                double w = width * (fw > 0.0 ? fw : 7.0) / 100.0 * Math.Sqrt(662.0 * e);
                var roi = new ROIDefinitionData
                {
                    Name = name, Enabled = true, PeakEnergy = e, Intencity = 1.0,
                    LowerLimit = e - w, UpperLimit = e + w, AutoBecquerelCoefficient = true,
                };
                FsaLineInterference hit = BecquerelCoefficient.InterferenceForZone(roi, rd);
                string said = hit == null
                    ? "помехи нет"
                    : string.Format(Inv, "×{0} — {1} (к {2}, отношение активностей {3}), линия зоны {4} кэВ, строка {5}",
                                    hit.Factor.ToString("F3", Inv), hit.Companion, hit.Reference,
                                    hit.ActivityRatio.ToString("F5", Inv), hit.LineKev.ToString("F2", Inv), hit.Component);
                Console.WriteLine("{0}\t{1}\t{2}…{3}\t{4}", name, e.ToString("F2", Inv),
                                  roi.LowerLimit.ToString("F1", Inv), roi.UpperLimit.ToString("F1", Inv), said);
            }

            return 0;
        }
    }
}
