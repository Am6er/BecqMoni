using BecquerelMonitor;
using BecquerelMonitor.Utils;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Xml.Serialization;

namespace S109ActivityProbe
{
    /// <summary>
    /// (`S109`) КОРПУСНАЯ ПОЛОВИНА ЦЕНЫ ПОРОГА 1 %: сколько пиков корпуса
    /// показали бы человеку активность в беккерелях, сколько из них отказывает
    /// по рентгену, сколько по выходу линии и сколько остаётся ГОЛЫМ ЧИСЛОМ.
    ///
    /// Зачем проба. Числа `S96` (678 пиков, 59 отказов, 282 спора, 337 голых)
    /// записаны В КОММЕНТАРИИ оборванным агентом и встречной проверки не
    /// проходили; цитировать их нельзя, пока не перемерено. Цена самого порога
    /// по СПИСКУ линий уже измерена (`S99`: из 73 видимых линий с выходом ниже
    /// 1 % ровно шесть), но список — не корпус: одна линия может стоять в
    /// сотне спектров, а другая ни в одном.
    ///
    /// ⛔ ЧТО ИМЕННО ВОСПРОИЗВОДИТСЯ. Путь беккерелей живёт в
    /// `EnergySpectrumView.EnsureSelectionAnalytics` (ветка «выделили область,
    /// в ней ровно один распознанный пик») и включается ЧЕТЫРЬМЯ условиями:
    /// `PeakMode.Visible`, измеренная ПШПВ выделения, видимый спектр и живая
    /// кривая эффективности У САМОГО СПЕКТРА. Проба считает, что первые три
    /// человеком выполнены, и перебирает пики так, как если бы он выделил
    /// каждый по очереди; четвёртое проверяется по файлу спектра.
    ///
    /// ⚠ ДОПУЩЕНИЕ, И ОНО НАЗВАНО: окно спора соседей в приложении — половина
    /// ПШПВ ВЫДЕЛЕНИЯ (`SelectionFWHMinkev`, измеренная гауссовым фитом внутри
    /// выделения). Здесь вместо неё берётся ПШПВ НАЙДЕННОГО ПИКА (`Peak.FWHM`,
    /// тоже кэВ): выделение вокруг одного пика даёт ровно её. Число «спорных»
    /// поэтому зависит от того, насколько широко человек выделяет, и является
    /// оценкой при выделении «в одну ПШПВ».
    ///
    /// ⛔ ПОРОГ И ПРИЗНАК РЕНТГЕНА ЧИТАЮТСЯ ИЗ СОБРАННОЙ СБОРКИ, а не
    /// переписываются сюда: `MinimumActivityYieldPercent` — константа, и её
    /// копия в пробе вкомпилировалась бы намертво, продолжая мерить старое
    /// число после смены порога. Берётся `GetRawConstantValue()`.
    /// `ScanActivityRivals` — приватный метод вида, он зовётся ОТРАЖЕНИЕМ на
    /// объекте без конструктора: своя копия правила «кто сосед» была бы вторым
    /// ответом на тот же вопрос.
    ///
    /// ⚠ Библиотека здесь ПОСТАВОЧНАЯ, и это не нарушение правила «корпус
    /// гоняется по указанным нуклидам» (`--lib=sample`): то правило про
    /// ПОЛНОСПЕКТРАЛЬНЫЙ РАЗБОР и его recall с фантомами, а подпись пика, по
    /// которой покупается число беккерелей, в приложении приходит от
    /// `PeakDetector.MatchNuclide` из АКТИВНОГО НАБОРА, и другой библиотеки у
    /// человека за экраном нет. Измерять цену порога по списку из манифеста
    /// значило бы мерить не тот путь.
    ///
    ///     S109ActivityProbe --spectra=&lt;…\CORPUS\corpus\spectra&gt;
    ///                       [--csv=s109.csv]
    ///
    /// Строка CSV — на КАЖДЫЙ найденный пик, включая те, что до активности не
    /// доходят: без них не видно, какая доля пиков отсеивается раньше и на
    /// каком условии. Разбор по частям корпуса (`parts.csv`) делается снаружи.
    /// </summary>
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

            string spectraDir = "spectra";
            string csvPath = "s109.csv";
            // ⚠ Во сколько ПШПВ пика человек выделил область. Ключ заведён не
            //    для красоты: окно спора — половина ПШПВ ВЫДЕЛЕНИЯ, и число
            //    «спорных» есть свойство этой ширины, а не спектра. Развёртка
            //    по нему обязана идти рядом с самим числом.
            double selMult = 1.0;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectra=", StringComparison.Ordinal)) spectraDir = a.Substring(10);
                else if (a.StartsWith("--csv=", StringComparison.Ordinal)) csvPath = a.Substring(6);
                else if (a.StartsWith("--sel-mult=", StringComparison.Ordinal))
                    selMult = double.Parse(a.Substring(11), CultureInfo.InvariantCulture);
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: {0}", a);
                    return 2;
                }
            }

            if (!Directory.Exists(spectraDir))
            {
                Console.Error.WriteLine("нет каталога {0}", spectraDir);
                return 2;
            }

            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();
            NuclideSet set = nuclides.ActiveSet;

            // ⛔ Порог — ИЗ СБОРКИ. Константа `public const double`, поэтому
            //    только `GetRawConstantValue`: обычное обращение к ней
            //    вкомпилировало бы значение в пробу.
            Type tView = typeof(EnergySpectrumView);
            FieldInfo fThr = tView.GetField("MinimumActivityYieldPercent",
                                            BindingFlags.Public | BindingFlags.Static);
            if (fThr == null)
            {
                Console.Error.WriteLine("⛔ в сборке нет MinimumActivityYieldPercent — мерить нечем");
                return 3;
            }
            double threshold = Convert.ToDouble(fThr.GetRawConstantValue(), CultureInfo.InvariantCulture);

            Type tAn = tView.GetNestedType("SelectionAnalytics", BindingFlags.NonPublic);
            MethodInfo mScan = tView.GetMethod("ScanActivityRivals",
                                               BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo fMgr = tView.GetField("nuclideManager",
                                            BindingFlags.NonPublic | BindingFlags.Instance);
            if (tAn == null || mScan == null || fMgr == null)
            {
                Console.Error.WriteLine("⛔ путь активности в сборке не найден отражением "
                                        + "(SelectionAnalytics={0}, ScanActivityRivals={1}, nuclideManager={2})",
                                        tAn != null, mScan != null, fMgr != null);
                return 3;
            }

            // Объект вида БЕЗ конструктора: WinForms поднимать незачем, методу
            // нужен один менеджер нуклидов.
            object view = FormatterServices.GetUninitializedObject(tView);
            fMgr.SetValue(view, nuclides);
            PropertyInfo pFwhmKev = tAn.GetProperty("SelectionFWHMinkev");
            PropertyInfo pRivals = tAn.GetProperty("ActivityRivals");
            PropertyInfo pFactor = tAn.GetProperty("ActivityRivalFactor");

            Console.WriteLine("порог выхода ИЗ СБОРКИ: {0} %; библиотека: {1} записей, набор «{2}»;"
                              + " выделение {3}×ПШПВ пика",
                              threshold.ToString("G6", CultureInfo.InvariantCulture),
                              nuclides.NuclideDefinitions != null ? nuclides.NuclideDefinitions.Count : -1,
                              set != null ? set.Name : "(нет)",
                              selMult.ToString("G4", CultureInfo.InvariantCulture));

            var csv = new StringBuilder();
            csv.AppendLine("spectrum,peak_kev,peak_counts,fwhm_kev,has_curve,nuclide,line_kev,"
                           + "intensity_pct,is_xray,low_yield,rivals,rival_factor,coeff_ok,bq_coeff,verdict");

            int spectra = 0, failed = 0, peaksTotal = 0;
            var byVerdict = new Dictionary<string, int>();

            foreach (string file in Directory.GetFiles(spectraDir, "*.xml")
                                             .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                ResultData rd;
                List<Peak> peaks;
                try
                {
                    rd = LoadResult(file);
                    peaks = new PeakDetector().DetectPeak(
                        rd, BackgroundMode.Invisible, SmoothingMethod.None,
                        set, nuclides.NuclideDefinitions);
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine("{0}: {1}", name, e.Message);
                    failed++;
                    continue;
                }

                spectra++;
                if (peaks == null)
                {
                    peaks = new List<Peak>();
                }
                bool hasCurve = rd.Efficiency != null;

                foreach (Peak peak in peaks.OrderBy(p => p.Energy))
                {
                    peaksTotal++;
                    NuclideDefinition nd = peak.Nuclide;
                    string verdict;
                    int rivals = 0;
                    double factor = 1.0;
                    bool xray = false, low = false, coeffOk = false;
                    double coeff = 0.0, coeffErr = 0.0;

                    if (nd == null || !(nd.Intencity > 0.0))
                    {
                        // Так же, как в приложении: без подписи с выходом ветка
                        // активности не начинается вовсе.
                        verdict = nd == null ? "нет подписи" : "у подписи нет выхода";
                    }
                    else if (!hasCurve)
                    {
                        verdict = "кривой нет";
                    }
                    else if (!(peak.FWHM > 0.0))
                    {
                        // `SelectionFWHM > 0` — четвёртое условие ветки.
                        verdict = "ПШПВ пика не измерена";
                    }
                    else
                    {
                        object an = Activator.CreateInstance(tAn, true);
                        pFwhmKev.SetValue(an, peak.FWHM * selMult, null);
                        mScan.Invoke(view, new object[] { peak, an });
                        rivals = (int)pRivals.GetValue(an, null);
                        factor = (double)pFactor.GetValue(an, null);

                        xray = NuclideDefinition.IsElementXrayName(nd.Name);
                        low = !xray && nd.Intencity < threshold;

                        // ⛔ Коэффициент считается ВСЕГДА, в том числе у
                        //    отказных: без него не ответить, стал бы отказ
                        //    ЧИСЛОМ на экране или всё равно отсеялся бы кривой.
                        //    В приложении при отказе этот вызов не делается —
                        //    здесь он ничего не меняет, потому что `verdict`
                        //    решают те же два условия и в том же порядке.
                        coeffOk = BecquerelCoefficient.TryForLine(peak.Energy, nd.Intencity,
                                                                  rd.Efficiency, out coeff, out coeffErr);
                        if (xray)
                        {
                            verdict = "ОТКАЗ рентген";
                        }
                        else if (low)
                        {
                            verdict = "ОТКАЗ выход";
                        }
                        else
                        {
                            verdict = !coeffOk
                                ? "кривая не дала ε"
                                : (rivals > 0 ? "ПОКАЗАНО со спором" : "ПОКАЗАНО голым числом");
                        }
                    }

                    int n;
                    byVerdict.TryGetValue(verdict, out n);
                    byVerdict[verdict] = n + 1;

                    csv.AppendLine(string.Join(",",
                        name,
                        F(peak.Energy, "F3"), F(peak.Count, "F1"), F(peak.FWHM, "F3"),
                        hasCurve ? "1" : "0",
                        nd == null ? "" : nd.Name.Replace(',', ';'),
                        nd == null ? "" : F(nd.Energy, "F3"),
                        nd == null ? "" : F(nd.Intencity, "G6"),
                        xray ? "1" : "0", low ? "1" : "0",
                        rivals.ToString(CultureInfo.InvariantCulture),
                        F(factor, "F3"),
                        coeffOk ? "1" : "0", F(coeff, "G6"),
                        verdict));
                }
            }

            File.WriteAllText(csvPath, csv.ToString(), new UTF8Encoding(false));

            Console.WriteLine();
            Console.WriteLine("спектров разобрано {0}, отказало {1}, пиков всего {2} -> {3}",
                              spectra, failed, peaksTotal, csvPath);
            Console.WriteLine();
            Console.WriteLine("{0,-28} {1,8}", "исход", "пиков");
            foreach (var kv in byVerdict.OrderByDescending(k => k.Value))
            {
                Console.WriteLine("{0,-28} {1,8}", kv.Key, kv.Value);
            }
            return failed > 0 ? 1 : 0;
        }

        static string F(double v, string fmt)
        {
            return double.IsNaN(v) || double.IsInfinity(v)
                ? ""
                : v.ToString(fmt, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Чтение спектра корпуса — тем же порядком, что у `peakoriginprobe`:
        /// прибор и его настройки поиска подставляет `ProbeDeviceConfig`
        /// (`S82`), иначе поиск идёт с умолчаниями библиотеки и подписи
        /// выходят не те.
        /// </summary>
        static ResultData LoadResult(string path)
        {
            var serializer = new XmlSerializer(typeof(ResultDataFile));
            ResultDataFile file;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                file = (ResultDataFile)serializer.Deserialize(stream);
            }
            ResultData rd = file.ResultDataList.First();
            EnergySpectrum s = rd.EnergySpectrum;
            if (s != null && s.Spectrum != null && s.TotalPulseCount == 0)
            {
                long total = 0;
                for (int i = 0; i < s.Spectrum.Length; i++) total += s.Spectrum[i];
                s.TotalPulseCount = total;
                s.ValidPulseCount = total;
            }
            var pcal = s != null ? s.EnergyCalibration as PolynomialEnergyCalibration : null;
            if (pcal != null) pcal.CheckCalibration(s.NumberOfChannels);

            string deviceNote = ProbeDeviceConfig.Attach(rd);
            if (deviceNote.Contains("НЕТ") || deviceNote.Contains("нет"))
            {
                Console.Error.WriteLine("⚠ " + Path.GetFileNameWithoutExtension(path) + ": " + deviceNote);
            }
            if (!(rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig))
            {
                throw new InvalidDataException("нет настроек поиска пиков ни в спектре, ни в приборе");
            }

            if (rd.FwhmCalibration == null)
            {
                var cfg = (FWHMPeakDetectionMethodConfig)rd.PeakDetectionMethodConfig;
                rd.FwhmCalibration = cfg.FwhmCalibration
                    ?? FwhmCalibration.DefaultCalibration(cfg, s.EnergyCalibration);
            }
            return rd;
        }
    }
}
