using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml.Serialization;

namespace BecquerelMonitor.Probes
{
    /// <summary>
    /// КРИВИЗНА ТРАКТА В ФАЙЛЕ СПЕКТРА (`AMBER271`, 29.10.2026; решение Amber вопросником,
    /// дословно: «Спектра, прибор — умолчание (Рекомендую)»).
    ///
    ///     tractcurvaturefileprobea271 --spectrum=&lt;спектр корпуса на приборе с κ ≠ 0&gt;
    ///                                 --device=&lt;XML конфигурации его прибора&gt;
    ///                                 --matrix=&lt;.rmx его сцены&gt; --tmp=&lt;каталог для файлов&gt;
    ///
    /// Четыре раздела.
    ///
    ///   1. ФАЙЛ (в). Своя κ переживает круг «записать — прочитать» побитово при культуре
    ///      потока ru-RU и en-US крест-накрест; в файле число записано точкой. Когда поле
    ///      пишется: своя κ — да; без своей на приборе с Guid — да, κ прибора; без прибора
    ///      — нет. Старый файл без поля — своей κ нет, κ = прибора. Нечисло в поле — как
    ///      поля нет. Запись в свой файл делает κ своей; назначение прибора её снимает;
    ///      копия спектра её несёт.
    ///   2. РАЗБОР (а, б) — сеансом приложения (`FsaAnalysisSession`, матрица — блоком в
    ///      спектре, как у файла с другой машины). (а) файл без поля — анализатор взял κ
    ///      прибора, тем же числом, что `AdoptDevice`, и разбор побитово тот же, что при
    ///      своей κ, равной прибору. (б) файл с полем κ ≠ κ прибора — анализатор взял κ
    ///      файла, служебная строка привязки печатает её, разбор другой.
    ///      ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: тот же файл с ПРОИГНОРИРОВАННЫМ полем (так читала
    ///      прежняя сборка) судится тем же судьёй (б) и ОБЯЗАН им быть отвергнут, а его
    ///      разбор — совпасть с (а) побитово.
    ///   3. ОТПЕЧАТОК (г). Смена κ спектра меняет отпечаток сеанса; запись в файл (κ та же)
    ///      и снятие своей κ, равной прибору, — нет.
    ///   4. ВОПРОС КНОПКИ «Сохранить в конфигурацию прибора»: строки о κ нет, пока κ
    ///      прибора не меняется; есть, с обоими числами точкой, — когда меняется; в обеих
    ///      культурах интерфейса и разная.
    ///
    /// Окон проба не открывает. Сборка — `tools/effmaker/probes/build_all.ps1`; запускать
    /// из каталога сборки проб (там поставочный `config\`).
    /// Ожидание: «СОШЛОСЬ». Коды: 0 — сошлось; 1 — не сошлось; 2 — ключи/файлы; 3 — разбор
    /// не завершился.
    /// </summary>
    static class TractCurvatureFileProbeA271
    {
        static int bad;
        static FsaAnalyzer lastAnalyzer;

        static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");
        static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>κ спектра в плече (б): заметно другая, чем у прибора, и не нуль (у нуля строки привязки о κ нет).</summary>
        const double SpectrumKappa = 0.0060;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = Inv;

            string spectrum = null, devicePath = null, matrixPath = null, tmp = null;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrum = a.Substring(11);
                else if (a.StartsWith("--device=", StringComparison.Ordinal)) devicePath = a.Substring(9);
                else if (a.StartsWith("--matrix=", StringComparison.Ordinal)) matrixPath = a.Substring(9);
                else if (a.StartsWith("--tmp=", StringComparison.Ordinal)) tmp = a.Substring(6);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            if (spectrum == null || devicePath == null || matrixPath == null || tmp == null)
            {
                Console.Error.WriteLine("нужны --spectrum=, --device=, --matrix=, --tmp=");
                return 2;
            }

            foreach (string f in new[] { spectrum, devicePath, matrixPath })
            {
                if (!File.Exists(f)) { Console.Error.WriteLine("нет файла: " + f); return 2; }
            }

            Directory.CreateDirectory(tmp);

            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();

            DeviceConfigInfo device;
            using (var stream = new FileStream(devicePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                device = (DeviceConfigInfo)new XmlSerializer(typeof(DeviceConfigInfo)).Deserialize(stream);
            }

            Console.WriteLine("прибор: {0}, κ = {1} 1/МэВ, Guid {2}", device.Name, R(device.TractCurvature), device.Guid);
            if (!(device.TractCurvature != 0.0) || string.IsNullOrEmpty(device.Guid))
            {
                Console.Error.WriteLine("нужен прибор с κ ≠ 0 и Guid — иначе плечи (а) и (б) не различить");
                return 2;
            }

            FileSection(spectrum, device);
            int code = AnalysisSection(spectrum, device, matrixPath, tmp, nuclides);
            if (code != 0)
            {
                return code;
            }

            NoteSection(spectrum, device);

            Console.WriteLine();
            Console.WriteLine(bad == 0
                ? "СОШЛОСЬ: κ спектра пишется в файл, читается разбором FSA и его отпечатком; без поля — κ прибора"
                : "НЕ СОШЛОСЬ: расхождений " + bad.ToString(CultureInfo.InvariantCulture));
            return bad == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------------
        // 1. ФАЙЛ
        // ------------------------------------------------------------------

        static void FileSection(string spectrum, DeviceConfigInfo device)
        {
            Console.WriteLine();
            Console.WriteLine("=== 1. файл: круг записи-чтения, когда поле пишется, старый файл ===");

            ResultData old = ReadFile(File.ReadAllText(spectrum), Inv);
            Check("в файле корпуса поля нет", !File.ReadAllText(spectrum).Contains("<TractCurvature>"));
            Check("старый файл: своей κ нет", !old.HasOwnTractCurvature);
            old.DeviceConfig = device;
            Same("старый файл на приборе: κ = κ прибора", device.TractCurvature, old.TractCurvature);

            // (в) круг: значения — с коротким и с длинным представлением, отрицательное,
            // и известное `R`-трудное число .NET Framework (0.84551240822557006: "R" даёт
            // 15 знаков, которые читаются в соседний double) — его исход печатается
            // отдельно, это свойство XmlConvert для ВСЕХ чисел файла, не только κ
            double[] values = { 0.0026, -0.0012345678901234567, 1.0 / 3.0, 2.5e-7, 0.0 };
            var cultures = new[] { Ru, En };
            foreach (double v in values)
            {
                foreach (CultureInfo write in cultures)
                {
                    foreach (CultureInfo read in cultures)
                    {
                        ResultData rd = Bare(old, device);
                        rd.TractCurvature = v;
                        string xml = WriteFile(rd, write);
                        string field = FieldOf(xml);
                        ResultData back = ReadFile(xml, read);
                        back.DeviceConfig = device;
                        string where = string.Format(CultureInfo.InvariantCulture, "κ = {0}, запись {1}, чтение {2}", R(v), write.Name, read.Name);
                        Check(where + ": поле записано, без запятой", field != null && field.IndexOf(',') < 0);
                        Check(where + ": своя κ после чтения", back.HasOwnTractCurvature);
                        SameBits(where + ": значение побитово", v, back.TractCurvature);
                    }
                }
            }

            {
                double hard = 0.84551240822557006;
                ResultData rd = Bare(old, device);
                rd.TractCurvature = hard;
                ResultData back = ReadFile(WriteFile(rd, Ru), En);
                Console.WriteLine("  справка: R-трудное {0} → в файле «{1}» → прочитано {2} ({3})",
                                  hard.ToString("G17", CultureInfo.InvariantCulture), FieldOf(WriteFile(rd, Inv)), back.TractCurvature.ToString("G17", CultureInfo.InvariantCulture),
                                  BitConverter.DoubleToInt64Bits(hard) == BitConverter.DoubleToInt64Bits(back.TractCurvature)
                                      ? "побитово" : "НЕ побитово: свойство XmlConvert .NET Framework");
            }

            // ⛔ контроль судьи «побитово»: соседний double обязан различаться
            Check("контроль: судья побитовости различает соседний double",
                  BitConverter.DoubleToInt64Bits(0.0026) != BitConverter.DoubleToInt64Bits(NextUp(0.0026)));

            // когда поле пишется
            {
                ResultData rd = Bare(old, device);
                string field = FieldOf(WriteFile(rd, Ru));
                Check("без своей κ на приборе с Guid: поле пишется", field != null);
                Same("  …и это κ прибора", device.TractCurvature, field != null ? double.Parse(field, CultureInfo.InvariantCulture) : double.NaN);

                rd.DeviceConfig = new DeviceConfigInfo();
                Check("без своей κ без прибора: поля нет", FieldOf(WriteFile(rd, Ru)) == null);
                Same("  …и κ разбора 0", 0.0, rd.TractCurvature);

                rd.TractCurvature = SpectrumKappa;
                Check("своя κ без прибора: поле пишется", FieldOf(WriteFile(rd, Ru)) != null);
                rd.ForgetTractCurvature();
                Check("снятая своя κ без прибора: поля нет", FieldOf(WriteFile(rd, Ru)) == null);
            }

            // нечисло в поле — как поля нет
            foreach (string garbage in new[] { "NaN", "INF", "-INF" })
            {
                ResultData rd = Bare(old, device);
                rd.TractCurvature = SpectrumKappa;
                string xml = Regex.Replace(WriteFile(rd, Inv), "<TractCurvature>[^<]*</TractCurvature>",
                                           "<TractCurvature>" + garbage + "</TractCurvature>");
                ResultData back = ReadFile(xml, Inv);
                back.DeviceConfig = device;
                Check("поле «" + garbage + "»: своей κ нет", !back.HasOwnTractCurvature);
                Same("  …κ = прибора", device.TractCurvature, back.TractCurvature);
            }

            // запись в свой файл, назначение прибора, копия, сосед документа
            {
                ResultData rd = Bare(old, device);
                rd.KeepWrittenTractCurvature();
                Check("запись в файл: κ стала своей", rd.HasOwnTractCurvature);
                Same("  …тем же числом", device.TractCurvature, rd.TractCurvature);

                ResultData none = Bare(old, device);
                none.DeviceConfig = new DeviceConfigInfo();
                none.KeepWrittenTractCurvature();
                Check("запись в файл без прибора: своей κ не завелось", !none.HasOwnTractCurvature);

                rd.TractCurvature = SpectrumKappa;
                ResultData copy = rd.Clone();
                Check("копия: своя κ", copy.HasOwnTractCurvature);
                Same("  …то же число", SpectrumKappa, copy.TractCurvature);

                MethodInfo like = typeof(DocumentManager).GetMethod("NewResultDataLike", BindingFlags.Static | BindingFlags.NonPublic);
                ResultData sibling = like != null ? (ResultData)like.Invoke(null, new object[] { rd }) : null;
                Check("сосед документа (NewResultDataLike): своя κ шаблона", sibling != null && sibling.HasOwnTractCurvature
                                                                             && sibling.TractCurvature == SpectrumKappa);

                rd.ForgetTractCurvature();
                Check("назначение прибора (ForgetTractCurvature): своей κ нет", !rd.HasOwnTractCurvature);
                Same("  …κ = прибора", device.TractCurvature, rd.TractCurvature);
            }
        }

        // ------------------------------------------------------------------
        // 2. РАЗБОР и 3. ОТПЕЧАТОК
        // ------------------------------------------------------------------

        static int AnalysisSection(string spectrum, DeviceConfigInfo device, string matrixPath, string tmp,
                                   NuclideDefinitionManager nuclides)
        {
            Console.WriteLine();
            Console.WriteLine("=== 2. разбор сеансом приложения: (а) без поля, (б) с полем, контроль ===");

            ResponseMatrix matrix = ResponseMatrix.Load(matrixPath);
            if (matrix == null)
            {
                Console.Error.WriteLine("матрица не читается: " + matrixPath);
                return 2;
            }

            // (а) файл как есть — без поля
            ResultData a = Load(spectrum, device, matrixPath, matrix, nuclides);
            Check("(а) своей κ нет", !a.HasOwnTractCurvature);
            FsaResult ra = Run(a);
            if (ra == null) return 3;
            FsaAnalyzer anA = lastAnalyzer;
            Check("матрица в разборе (без неё карты «adc» нет, и плечи ничего не мерят)", ra.ResponseMatrixUsed);
            var old = new FsaAnalyzer();
            old.AdoptDevice(device);
            SameBits("(а) анализатор взял κ прибора — то же число, что AdoptDevice", old.AdcTractCurvature, anA.AdcTractCurvature);
            string noteA = TractNoteOf(ra);
            Console.WriteLine("  (а) привязка: …{0}", noteA ?? "<строки о κ нет>");
            Check("(а) строка привязки печатает κ прибора", noteA == Note(device.TractCurvature));

            // (а) своя κ, равная прибору, — тот же разбор побитово
            ResultData a2 = Load(spectrum, device, matrixPath, matrix, nuclides);
            a2.TractCurvature = device.TractCurvature;
            FsaResult ra2 = Run(a2);
            if (ra2 == null) return 3;
            Check("(а) своя κ = прибора: разбор побитово тот же", Print(ra) == Print(ra2));

            // (б) файл с полем κ ≠ κ прибора — через запись и чтение файла
            ResultData withField = Load(spectrum, device, matrixPath, matrix, nuclides);
            withField.TractCurvature = SpectrumKappa;
            string path = Path.Combine(tmp, "a271_with_field.xml");
            WriteFileTo(withField, path, Ru);
            Console.WriteLine("  файл с полем: {0}, поле «{1}»", path, FieldOf(File.ReadAllText(path)));

            ResultData b = Load(path, device, matrixPath, matrix, nuclides);
            FsaResult rb = Run(b);
            if (rb == null) return 3;
            FsaAnalyzer anB = lastAnalyzer;
            int judgedB = JudgeField(rb, anB, ra, "(б)");
            bad += judgedB;

            // ⛔ контроль: тот же файл, поле проигнорировано (так читала прежняя сборка)
            ResultData c = Load(path, device, matrixPath, matrix, nuclides);
            c.ForgetTractCurvature();
            FsaResult rc = Run(c);
            if (rc == null) return 3;
            FsaAnalyzer anC = lastAnalyzer;
            int saved = bad;
            int judgedC = JudgeField(rc, anC, ra, "контроль");
            bad = saved;
            Check("⛔ контроль: судья (б) ОТВЕРГ файл с проигнорированным полем (расхождений " + judgedC.ToString(CultureInfo.InvariantCulture) + ")", judgedC > 0);
            Check("контроль: разбор с проигнорированным полем побитово = (а)", Print(rc) == Print(ra));

            Console.WriteLine();
            Console.WriteLine("=== 3. отпечаток сеанса — от κ, которую берёт разбор ===");
            ResultData s = Load(spectrum, device, matrixPath, matrix, nuclides);
            string s0 = FsaAnalysisSession.BuildStamp(s, false);
            s.TractCurvature = device.TractCurvature;
            Check("своя κ = прибора: отпечаток прежний", FsaAnalysisSession.BuildStamp(s, false) == s0);
            s.TractCurvature = SpectrumKappa;
            string s1 = FsaAnalysisSession.BuildStamp(s, false);
            Check("смена κ спектра: отпечаток другой", s1 != s0);
            s.KeepWrittenTractCurvature();
            Check("запись в файл (κ та же): отпечаток прежний", FsaAnalysisSession.BuildStamp(s, false) == s1);
            s.ForgetTractCurvature();
            Check("снятие своей κ: отпечаток прибора", FsaAnalysisSession.BuildStamp(s, false) == s0);
            return 0;
        }

        /// <summary>Судья плеча (б): κ файла в анализаторе, в строке привязки, и разбор не тот, что у (а).</summary>
        static int JudgeField(FsaResult r, FsaAnalyzer an, FsaResult ra, string arm)
        {
            int before = bad;
            SameBits(arm + " анализатор взял κ файла", SpectrumKappa, an.AdcTractCurvature);
            string note = TractNoteOf(r);
            Console.WriteLine("  {0} привязка: …{1}", arm, note ?? "<строки о κ нет>");
            Check(arm + " строка привязки печатает κ файла", note == Note(SpectrumKappa));
            Check(arm + " разбор другой, чем без поля", Print(r) != Print(ra));
            Console.WriteLine("  {0} χ²/ndf {1} против {2} у (а)", arm, r.Chi2Ndf.ToString("F4", CultureInfo.InvariantCulture), ra.Chi2Ndf.ToString("F4", CultureInfo.InvariantCulture));
            return bad - before;
        }

        // ------------------------------------------------------------------
        // 4. ВОПРОС КНОПКИ
        // ------------------------------------------------------------------

        static void NoteSection(string spectrum, DeviceConfigInfo device)
        {
            Console.WriteLine();
            Console.WriteLine("=== 4. вопрос кнопки «Сохранить в конфигурацию прибора» ===");
            MethodInfo note = typeof(DCEnergyCalibrationView).GetMethod("TractCurvatureChangeNote",
                                                                     BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (note == null)
            {
                Check("метод TractCurvatureChangeNote найден", false);
                return;
            }

            ResultData rd = Bare(ReadFile(File.ReadAllText(spectrum), Inv), device);
            Check("своей κ нет: строки о κ нет", note.Invoke(null, new object[] { rd }) == null);
            rd.TractCurvature = device.TractCurvature;
            Check("своя κ = прибора: строки о κ нет", note.Invoke(null, new object[] { rd }) == null);
            rd.TractCurvature = SpectrumKappa;

            CultureInfo ui = Thread.CurrentThread.CurrentUICulture;
            var texts = new Dictionary<string, string>();
            try
            {
                foreach (CultureInfo culture in new[] { En, Ru })
                {
                    Thread.CurrentThread.CurrentUICulture = culture;
                    Thread.CurrentThread.CurrentCulture = culture;
                    string text = (string)note.Invoke(null, new object[] { rd });
                    texts[culture.Name] = text;
                    Console.WriteLine("  {0}: {1}", culture.Name, text);
                    Check(culture.Name + ": строка о κ есть, с обоими числами точкой",
                          text != null && text.Contains(R(device.TractCurvature)) && text.Contains(R(SpectrumKappa)));
                }
            }
            finally
            {
                Thread.CurrentThread.CurrentUICulture = ui;
                Thread.CurrentThread.CurrentCulture = Inv;
            }

            Check("en и ru — разные строки (перевод доехал)", texts["en-US"] != texts["ru-RU"]);
        }

        // ------------------------------------------------------------------
        // помощники
        // ------------------------------------------------------------------

        static FsaResult Run(ResultData rd)
        {
            lastAnalyzer = null;
            FsaAnalysisSession.ProbeAnalyzerHook = analyzer => lastAnalyzer = analyzer;
            var session = new FsaAnalysisSession();
            var done = new ManualResetEvent(false);
            session.Completed += (s, e) => done.Set();
            session.EnsureUpToDate(rd, false, FsaCalculationOptions.Of(rd));
            if (!done.WaitOne(TimeSpan.FromSeconds(600)) || session.Result == null || lastAnalyzer == null)
            {
                Console.WriteLine("разбор не завершился");
                return null;
            }

            return session.Result;
        }

        /// <summary>
        /// Спектр, каким его открывает приложение: читатель файла приложения
        /// (<c>DocumentManager.ReadResultDataFile</c>), прибор, его настройки поиска, кривая
        /// FWHM, пики; матрица — блоком в спектре.
        /// </summary>
        static ResultData Load(string path, DeviceConfigInfo device, string matrixPath, ResponseMatrix matrix,
                               NuclideDefinitionManager nuclides)
        {
            MethodInfo reader = typeof(DocumentManager).GetMethod("ReadResultDataFile", BindingFlags.Static | BindingFlags.NonPublic);
            ResultData rd = ((ResultDataFile)reader.Invoke(null, new object[] { path })).ResultDataList[0];
            EnergySpectrum s = rd.EnergySpectrum;
            if (s != null && s.Spectrum != null && s.TotalPulseCount == 0)
            {
                long total = 0;
                for (int i = 0; i < s.Spectrum.Length; i++) total += s.Spectrum[i];
                s.TotalPulseCount = total;
                s.ValidPulseCount = total;
            }

            rd.DeviceConfig = device;
            rd.PeakDetectionMethodConfig = FWHMPeakDetectionMethodConfig.AdoptFrom(
                device.PeakDetectionMethodConfig as FWHMPeakDetectionMethodConfig,
                rd.PeakDetectionMethodConfig as FWHMPeakDetectionMethodConfig);
            if (rd.FwhmCalibration == null && rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig cfg)
            {
                rd.FwhmCalibration = FwhmCalibration.DefaultCalibration(cfg, rd.EnergySpectrum.EnergyCalibration);
            }

            if (rd.Efficiency != null)
            {
                rd.Efficiency.EmbeddedMatrix = EmbeddedResponseMatrix.FromFile(rd.Efficiency.Guid, matrixPath, matrix);
            }

            rd.DetectedPeaks = new PeakDetector().DetectPeak(rd, BackgroundMode.Invisible, SmoothingMethod.None,
                                                             nuclides.ActiveSet, nuclides.NuclideDefinitions);
            return rd;
        }

        /// <summary>Лёгкая копия для разделов файла: без кривой эффективности (блок матрицы не нужен), прибор — данный.</summary>
        static ResultData Bare(ResultData source, DeviceConfigInfo device)
        {
            ResultData rd = source.Clone();
            rd.Efficiency = null;
            rd.FileEfficiency = null;
            rd.DeviceConfig = device;
            rd.ForgetTractCurvature();
            return rd;
        }

        static string WriteFile(ResultData rd, CultureInfo culture)
        {
            CultureInfo before = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = culture;
            try
            {
                var file = new ResultDataFile();
                file.ResultDataList.Add(rd);
                var serializer = new XmlSerializer(typeof(ResultDataFile));
                using (var writer = new StringWriter(CultureInfo.InvariantCulture))
                {
                    serializer.Serialize(writer, file);
                    return writer.ToString();
                }
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = before;
            }
        }

        /// <summary>Запись в файл — теми же настройками писателя, что <c>DocumentManager.SaveDocument</c> (UTF-8, отступы).</summary>
        static void WriteFileTo(ResultData rd, string path, CultureInfo culture)
        {
            CultureInfo before = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = culture;
            try
            {
                var file = new ResultDataFile();
                file.ResultDataList.Add(rd);
                var serializer = new XmlSerializer(typeof(ResultDataFile));
                using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var writer = System.Xml.XmlWriter.Create(stream, new System.Xml.XmlWriterSettings { Encoding = Encoding.UTF8, Indent = true }))
                {
                    serializer.Serialize(writer, file);
                }
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = before;
            }
        }

        static ResultData ReadFile(string xml, CultureInfo culture)
        {
            CultureInfo before = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = culture;
            try
            {
                var serializer = new XmlSerializer(typeof(ResultDataFile));
                using (var reader = new StringReader(xml))
                {
                    return ((ResultDataFile)serializer.Deserialize(reader)).ResultDataList[0];
                }
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = before;
            }
        }

        static string FieldOf(string xml)
        {
            Match m = Regex.Match(xml, "<TractCurvature>([^<]*)</TractCurvature>");
            return m.Success ? m.Groups[1].Value : null;
        }

        /// <summary>Хвост служебной строки привязки о κ — ровно тем форматом, каким его печатает разбор.</summary>
        static string TractNoteOf(FsaResult r)
        {
            string note = r.AnchorNote ?? "";
            int at = note.IndexOf("кривизна тракта ", StringComparison.Ordinal);
            if (at < 0) return null;
            int end = note.IndexOf(" 1/МэВ", at, StringComparison.Ordinal);
            return end < 0 ? null : note.Substring(at, end - at + 6);
        }

        static string Note(double kappa)
        {
            return string.Format(CultureInfo.InvariantCulture, "кривизна тракта {0:F5} 1/МэВ", kappa);
        }

        /// <summary>Разбор одной строкой, побитово: шкала, χ², строки состава.</summary>
        static string Print(FsaResult r)
        {
            var sb = new StringBuilder();
            sb.Append(R(r.Gain)).Append('|').Append(R(r.OffsetChannels)).Append('|').Append(R(r.Chi2Ndf));
            if (r.Components != null)
            {
                foreach (FsaComponentResult row in r.Components)
                {
                    sb.Append('|').Append(row.Name).Append(':').Append(R(row.CountRate));
                }
            }

            return sb.ToString();
        }

        static double NextUp(double v)
        {
            return BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(v) + 1);
        }

        static string R(double v)
        {
            return v.ToString("R", CultureInfo.InvariantCulture);
        }

        static void Check(string what, bool ok)
        {
            Console.WriteLine("  {0} {1}", ok ? "ok  " : "НЕТ ", what);
            if (!ok) bad++;
        }

        static void Same(string what, double expected, double actual)
        {
            Check(string.Format(CultureInfo.InvariantCulture, "{0}: ждали {1}, есть {2}", what, R(expected), R(actual)),
                  expected.Equals(actual));
        }

        static void SameBits(string what, double expected, double actual)
        {
            Check(string.Format(CultureInfo.InvariantCulture, "{0}: ждали {1}, есть {2}", what, R(expected), R(actual)),
                  BitConverter.DoubleToInt64Bits(expected) == BitConverter.DoubleToInt64Bits(actual));
        }
    }
}
