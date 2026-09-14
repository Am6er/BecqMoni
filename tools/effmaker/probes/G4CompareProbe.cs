using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.Utils;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Serialization;

namespace G4CompareProbe
{
    /// <summary>
    /// Спектр, посчитанный ВНЕШНИМ арбитром (Geant4, `tools/g4cf hist`), рядом
    /// с измерением — тем же уширением и той же шкалой, что у приложения.
    ///
    /// ЗАЧЕМ. Вопрос Amber 01.09.2026 по волнистости модели: «почему вообще
    /// появляются волны при такой-то огромной статистике? возьми моделирование
    /// GEANT4, прогони его по этому же спектру — там тоже такие волны?».
    /// Наш ответ на форму спектра считает своя матрица отклика; независимая
    /// проверка нужна ровно затем, чтобы отделить «так устроен прибор» от «так
    /// устроена наша модель».
    ///
    ///     g4compareprobe --spectrum=X.xml --hist=g4.txt [--out=cmp.csv]
    ///                    [--from=300] [--to=620]
    ///
    /// `--hist=` — вывод `g4cf … hist E N шаг` как есть: строки
    /// `HISTBEGIN bins=… bin_kev=…`, `HIST &lt;бин&gt; &lt;счёт&gt;`, `HISTEND`.
    ///
    /// Что делается: гистограмма депозитов уширяется профилем `PeakShapeModel`
    /// по ПШПВ-калибровке САМОГО спектра (та же форма, которой пользуется
    /// разбор), кладётся на шкалу спектра, нормируется по площади фотопика — и
    /// печатается рядом с измерением за вычетом фона. Разбор остатка — за
    /// питоном; здесь только честное приведение к одной шкале.
    /// </summary>
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            string spectrumPath = null, histPath = null, outPath = "g4cmp.csv";
            double fromKev = 300.0, toKev = 620.0, fwhmScale = 1.0;
            string lightMaterial = null;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrumPath = a.Substring(11);
                else if (a.StartsWith("--hist=", StringComparison.Ordinal)) histPath = a.Substring(7);
                else if (a.StartsWith("--out=", StringComparison.Ordinal)) outPath = a.Substring(6);
                else if (a.StartsWith("--from=", StringComparison.Ordinal)) fromKev = double.Parse(a.Substring(7), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--to=", StringComparison.Ordinal)) toKev = double.Parse(a.Substring(5), CultureInfo.InvariantCulture);
                // (`A37`) Во сколько раз уширять СИЛЬНЕЕ паспортной ПШПВ: проверка
                // догадки «континуум размыт не так, как пик».
                else if (a.StartsWith("--fwhm-scale=", StringComparison.Ordinal)) fwhmScale = double.Parse(a.Substring(13), CultureInfo.InvariantCulture);
                // ⛔ (`A37`) ШКАЛА СВЕТА. Прибор меряет НЕ энергию, а свет, и у
                // сцинтиллятора выход на килоэлектронвольт зависит от энергии
                // электрона (непропорциональность, `F11`). Geant4 отдаёт
                // энерговыделение и о свете не знает — значит сравнивать его
                // с измерением в шкале энергии значит сравнивать разные
                // величины. Ключ переводит депозит в шкалу света по нашей же
                // кривой (`scint_electron_light_yield`), с якорем на линии.
                else if (a.StartsWith("--light=", StringComparison.Ordinal)) lightMaterial = a.Substring(8);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            if (spectrumPath == null || histPath == null)
            {
                Console.Error.WriteLine("нужны --spectrum=<файл> и --hist=<вывод g4cf>");
                return 2;
            }

            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();

            ResultData rd = Load(spectrumPath);
            if (rd == null)
            {
                return 2;
            }

            EnergySpectrum spectrum = rd.EnergySpectrum;
            EnergyCalibration calibration = spectrum.EnergyCalibration;
            FwhmCalibration fwhm = rd.FwhmCalibration;
            if (fwhm == null)
            {
                Console.Error.WriteLine("у спектра нет ПШПВ-калибровки — уширять нечем");
                return 2;
            }

            int channels = spectrum.NumberOfChannels;

            // Измерение за вычетом фона — тем же правилом, что у разбора:
            // фон приводится по времени.
            double[] net = new double[channels];
            EnergySpectrum background = rd.BackgroundEnergySpectrum;
            double k = background != null && background.MeasurementTime > 0.0
                ? spectrum.MeasurementTime / background.MeasurementTime
                : 0.0;
            for (int i = 0; i < channels; i++)
            {
                double value = spectrum.Spectrum[i];
                if (background != null && i < background.Spectrum.Length)
                {
                    value -= k * background.Spectrum[i];
                }

                net[i] = value;
            }

            double binKev;
            double[] deposit = ReadHist(histPath, out binKev);
            if (deposit == null)
            {
                return 2;
            }

            Console.WriteLine("уширение: паспортная ПШПВ × {0:F2}", fwhmScale);
            Console.WriteLine("гистограмма арбитра: {0} бинов по {1} кэВ, событий с откликом {2:F0}",
                              deposit.Length, binKev.ToString("F3", CultureInfo.InvariantCulture),
                              Sum(deposit));

            // ⛔ ШКАЛУ АРБИТРА НАДО ВЫРОВНЯТЬ ПО ИЗМЕРЕНИЮ, иначе сравнение
            // мерит не форму, а ошибку энергетической калибровки. У этого
            // спектра она −0.9 % (пик 661.657 стоит на 655.6), и без поправки
            // весь расчёт оказывается правее измерения на шесть килоэлектрон-
            // вольт — на комптоновском крае это десятки процентов «расхождения»,
            // которого на деле нет.
            //
            // Множитель берётся по ЦЕНТРУ ТЯЖЕСТИ фотопика измерения: там же,
            // где нормировка площади, и по тем же каналам.
            double alignLo = 662.0 - 60.0, alignHi = 662.0 + 60.0;
            double weight = 0.0, moment = 0.0;
            for (int i = 0; i < channels; i++)
            {
                double e = calibration.ChannelToEnergy(i);
                if (e < alignLo || e > alignHi || !(net[i] > 0.0))
                {
                    continue;
                }

                weight += net[i];
                moment += net[i] * e;
            }

            double gain = weight > 0.0 ? (moment / weight) / 661.657 : 1.0;
            Console.WriteLine("выравнивание по фотопику: центр измерения {0:F2} кэВ, множитель шкалы {1:F5}",
                              weight > 0.0 ? moment / weight : 0.0, gain);

            MaterialDatabase.LightYieldCurve light = null;
            double anchorYield = 1.0;
            if (lightMaterial != null)
            {
                light = MaterialDatabase.LightYieldOf(lightMaterial);
                if (light == null)
                {
                    Console.Error.WriteLine("нет кривой световыхода для «{0}»", lightMaterial);
                    return 2;
                }

                anchorYield = light.Of(661.657);
                Console.WriteLine("шкала света: {0}, выход на линии {1:F4}, на 300 кэВ {2:F4}, на 100 кэВ {3:F4}",
                                  lightMaterial, anchorYield, light.Of(300.0), light.Of(100.0));
            }

            // Уширение: каждый бин депозита раскладывается профилем той же
            // формы, какой разбор описывает линии.
            double[] model = new double[channels];
            for (int b = 0; b < deposit.Length; b++)
            {
                double counts = deposit[b];
                if (!(counts > 0.0))
                {
                    continue;
                }

                double energy = b * binKev * gain;
                if (light != null && energy > 0.0)
                {
                    // Событие принимается за ОДИН электрон своей энергии: у нас
                    // свет копится по каждому электрону в отдельности, здесь такой
                    // росписи нет — гистограмма приходит суммарным депозитом.
                    // Приближение завышает свет у многоэлектронных событий и потому
                    // даёт НИЖНЮЮ оценку сдвига, а не верхнюю.
                    energy *= light.Of(energy) / anchorYield;
                }

                double center = calibration.EnergyToChannel(energy, maxChannels: channels);
                if (Double.IsNaN(center) || center < 0.0 || center >= channels)
                {
                    continue;
                }

                double width = fwhmScale * fwhm.ChannelToFwhm(center);
                if (!(width > 0.0) || Double.IsNaN(width))
                {
                    continue;
                }

                int left = (int)Math.Floor(center - PeakShapeModel.GetLeftSupport(fwhm, width));
                int right = (int)Math.Ceiling(center + PeakShapeModel.GetRightSupport(fwhm, width));
                if (left < 0) left = 0;
                if (right >= channels) right = channels - 1;

                double norm = 0.0;
                for (int i = left; i <= right; i++)
                {
                    norm += PeakShapeModel.RelativeValue(i - center, width, fwhm);
                }

                if (!(norm > 0.0))
                {
                    continue;
                }

                double scale = counts / norm;
                for (int i = left; i <= right; i++)
                {
                    model[i] += scale * PeakShapeModel.RelativeValue(i - center, width, fwhm);
                }
            }

            // Нормировка по ФОТОПИКУ: полная площадь несравнима — у измерения в
            // ней сидят и рентген свинца, и обратное рассеяние, и наложения,
            // которых у арбитра нет по построению (он считает одну линию).
            double peakLo = 662.0 - 60.0, peakHi = 662.0 + 60.0;
            double sumNet = 0.0, sumModel = 0.0;
            for (int i = 0; i < channels; i++)
            {
                double e = calibration.ChannelToEnergy(i);
                if (e >= peakLo && e <= peakHi)
                {
                    sumNet += net[i];
                    sumModel += model[i];
                }
            }

            if (!(sumModel > 0.0) || !(sumNet > 0.0))
            {
                Console.Error.WriteLine("нормировать нечем: пустой фотопик");
                return 1;
            }

            double factor = sumNet / sumModel;
            Console.WriteLine("нормировка по фотопику 602…722 кэВ: множитель {0:E3}", factor);

            using (var writer = new StreamWriter(outPath, false, new UTF8Encoding(true)))
            {
                writer.WriteLine("ch,keV,net,g4");
                for (int i = 0; i < channels; i++)
                {
                    double e = calibration.ChannelToEnergy(i);
                    if (e < fromKev || e > toKev)
                    {
                        continue;
                    }

                    writer.WriteLine("{0},{1},{2},{3}",
                                     i.ToString(CultureInfo.InvariantCulture),
                                     e.ToString("F3", CultureInfo.InvariantCulture),
                                     net[i].ToString("F3", CultureInfo.InvariantCulture),
                                     (factor * model[i]).ToString("F3", CultureInfo.InvariantCulture));
                }
            }

            Console.WriteLine("{0}: полоса {1}…{2} кэВ", outPath, fromKev, toKev);
            return 0;
        }

        static double Sum(double[] a)
        {
            double s = 0.0;
            for (int i = 0; i < a.Length; i++)
            {
                s += a[i];
            }

            return s;
        }

        static double[] ReadHist(string path, out double binKev)
        {
            binKev = 0.0;
            var values = new List<double>();
            int bins = 0;
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.StartsWith("HISTBEGIN", StringComparison.Ordinal))
                {
                    foreach (string part in line.Split(' '))
                    {
                        if (part.StartsWith("bins=", StringComparison.Ordinal))
                        {
                            bins = int.Parse(part.Substring(5), CultureInfo.InvariantCulture);
                        }
                        else if (part.StartsWith("bin_kev=", StringComparison.Ordinal))
                        {
                            binKev = double.Parse(part.Substring(8), CultureInfo.InvariantCulture);
                        }
                    }

                    values = new List<double>(new double[Math.Max(0, bins)]);
                }
                else if (line.StartsWith("HIST ", StringComparison.Ordinal))
                {
                    string[] parts = line.Split(' ');
                    if (parts.Length >= 3)
                    {
                        int bin = int.Parse(parts[1], CultureInfo.InvariantCulture);
                        double counts = double.Parse(parts[2], CultureInfo.InvariantCulture);
                        while (values.Count <= bin)
                        {
                            values.Add(0.0);
                        }

                        values[bin] = counts;
                    }
                }
            }

            if (values.Count == 0 || !(binKev > 0.0))
            {
                Console.Error.WriteLine("в файле нет гистограммы (HISTBEGIN/HIST): " + path);
                return null;
            }

            return values.ToArray();
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
            if (rd.FwhmCalibration == null
                && rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig cfg)
            {
                if (cfg.FwhmCalibration == null && rd.EnergySpectrum != null)
                {
                    cfg.FwhmCalibration = FwhmCalibration.DefaultCalibration(
                        cfg, rd.EnergySpectrum.EnergyCalibration);
                }

                if (cfg.FwhmCalibration != null)
                {
                    rd.FwhmCalibration = cfg.FwhmCalibration.Clone();
                }
            }

            return rd;
        }
    }
}
