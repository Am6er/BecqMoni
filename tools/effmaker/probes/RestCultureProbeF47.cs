// `A244`, ХВОСТ доли П8 «остальное». Полоса F47 захода 06.09.2026.
//
//     restcultureprobef47 [--os=<культура>] [--out=<файл>]
//     restcultureprobef47 --modal-control        # контроль сторожа окон, ждём код 1
//     restcultureprobef47 --sweep --out=<файл>   # сплошной разбор ВСЕЙ сборки (§7)
//
// ЧТО МЕРЯЕТСЯ. Правило Amber 05.09.2026: разделитель дробной части у чисел
// ВСЕГДА ТОЧКА, печать — строго инвариантом, группировки разрядов нет вовсе.
// Доля — одиннадцать файлов, названных поимённо полосами F34 и F35:
// `Utils/SpectrumAriphmetics.cs`, `PeakDetector.cs`,
// `FWHMPeakDetector/Spectrum.cs`, `EfficiencyMaker/EfficiencyCurveGraph.cs`,
// `AboutForm.cs`, `AudioVolumeController.cs`, `DocEnergySpectrum.cs`,
// `MeasurementController.cs`, `NucBase/DataBase.cs`, `AppLog.cs`,
// `EfficiencyConfigData.cs`.
//
// ⛔ КОСТЫЛЬ `MainForm.cs:158-160` (клон культуры с подменённым разделителем)
//    ЗДЕСЬ НЕ ДЕЙСТВУЕТ: окно не поднимается, `MainForm` не строится, культура
//    потока выставляется присваиванием. Проверяется ЯВНО — у каждого плеча
//    печатается настоящий разделитель культуры и `(1.5).ToString()`.
//
// ⛔ СУДИТСЯ ВИД СТРОКИ, А НЕ РАВЕНСТВО ПЛЕЧ (цена оговорки — доля П7, где
//    группировка пережила приёмку: запятая в группах одинакова на всех
//    культурах и плечи не разводит). Все числа слотов взяты ≥ 1000 нарочно.
//
// ⛔ СЧЁТ СКАНЕРА КРИТЕРИЕМ ГОТОВНОСТИ НЕ ЯВЛЯЕТСЯ (вывод полосы F35). В этой
//    доле сканер печатает 46 мест, из которых 45 ЛОЖНЫХ, а 5 настоящих он не
//    видит вовсе. Поэтому §4 доказывает ложность остатка ЗАМЕРОМ, а не доводом.
//
// ⚠ РАЗБОРА ЧИСЕЛ В ДОЛЕ НЕТ ВОВСЕ (`Parse`/`TryParse` — 0 по одиннадцати
//   файлам сплошным поиском), поэтому обратного плеча по разбору нет; вместо
//   него §5 доказывает, что правка не сдвинула ВЫЧИСЛЕНИЯ `SpectrumAriphmetics`
//   (вычитание фона и сглаживание — числа, на которых стоит весь разбор).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using BecquerelMonitor;

// Целевая платформа процесса (.NETFramework 4.8) объявлена ОБЩИМ довеском
// `_TargetFramework.cs` — он компилируется в каждую пробу (`T237`, 06.09.2026);
// свой атрибут здесь дал бы CS0579. Значение печатается в шапке.

static class RestCultureProbeF47
{
    static readonly StringBuilder Log = new StringBuilder();
    static int failures;

    static readonly string[] Foreign = { "ru-RU", "de-DE", "en-US" };

    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        string outPath = null;
        string osCulture = null;
        bool modalControl = false;
        bool sweep = false;
        foreach (string a in args)
        {
            if (a.StartsWith("--out=", StringComparison.Ordinal)) outPath = a.Substring(6);
            else if (a.StartsWith("--os=", StringComparison.Ordinal)) osCulture = a.Substring(5);
            else if (a == "--modal-control") modalControl = true;
            else if (a == "--sweep") sweep = true;
        }

        // ⛔ Плечи выставляют культуру ПОТОКА сами, но «код 0 на трёх культурах»
        //    значит ещё и три РАЗНЫХ ПРОЦЕССА: ключ ставит культуру всему
        //    процессу ДО первого обращения к приложению, как её ставила бы
        //    система. Костыль `MainForm` при этом не поднимается — окна нет.
        if (!string.IsNullOrEmpty(osCulture))
        {
            CultureInfo ci = CultureInfo.GetCultureInfo(osCulture);
            CultureInfo.DefaultThreadCurrentCulture = ci;
            Thread.CurrentThread.CurrentCulture = ci;
        }

        if (sweep)
        {
            Sweep(outPath);
            return failures == 0 ? 0 : 1;
        }

        ModalWatchStart();

        if (modalControl)
        {
            ModalControl();
            Say("");
            Say(failures == 0
                ? "⛔ КОНТРОЛЬ НЕ СРАБОТАЛ: сторож не засчитал ни одного окна"
                : "РАСХОЖДЕНИЙ: " + failures + " (так и надо: это контроль сторожа)");
            ModalWatchStop();
            Finish(outPath);
            return failures == 0 ? 3 : 1;
        }

        // ⛔ Обе карты примитивов ROI — ДО любого менеджера-одиночки (`T60`).
        ROIPrimitiveDefinition.InitializeROIPrimitiveDefinitions();
        ROIPrimitiveOperation.InitializeROIPrimitiveOperations();

        Header();

        try
        {
            Run();
        }
        catch (Exception ex)
        {
            failures++;
            Say("⛔ ПРОГОН ОБОРВАН: " + ex.GetType().Name + ": " + ex.Message);
            Say(ex.StackTrace ?? "");
        }

        ModalWatchStop();
        Say("");
        Say("модальных окон за прогон: " + modalSeen
            + (modalSeen == 0 ? "" : "  ⛔ (каждое засчитано расхождением)"));
        Say(failures == 0 ? "СОШЛОСЬ, расхождений 0" : "⛔ НЕ СОШЛОСЬ: " + failures);
        Finish(outPath);
        return failures == 0 ? 0 : 1;
    }

    static void Header()
    {
        Assembly app = typeof(GlobalConfigManager).Assembly;
        Say("== `A244` ХВОСТ доли П8: число печатается ТОЧКОЙ, группировки нет ==");
        Say("");
        Say("сборка приложения: " + app.Location);
        try
        {
            Say("собрана:           " + File.GetLastWriteTime(app.Location)
                                          .ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture));
        }
        catch (Exception ex) { Say("собрана:           не прочитана: " + ex.Message); }
        Say("проба собрана:     " + File.GetLastWriteTime(Assembly.GetExecutingAssembly().Location)
                                       .ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture));
        Say("целевая платформа входной сборки: "
            + (AppDomain.CurrentDomain.SetupInformation.TargetFrameworkName ?? "⛔ НЕ ОБЪЯВЛЕНА"));
        Say("культура ОС:       " + CultureInfo.InstalledUICulture.Name
            + "; культура ПРОЦЕССА задана ключом: "
            + (CultureInfo.DefaultThreadCurrentCulture == null
               ? "нет (как у системы)" : CultureInfo.DefaultThreadCurrentCulture.Name)
            + "; культура потока при старте: " + CultureInfo.CurrentCulture.Name
            + ", её разделитель дробной части «"
            + CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator + "»");
        Say("окна приложения:   AppUi.HasWindows = " + AppUi.HasWindows
            + (AppUi.HasWindows ? "  ⛔ путь НЕ безоконный" : "  — безоконный путь, отказы бросаются"));
        Say("⛔ костыль `MainForm` (клон культуры с подменённым разделителем) НЕ ПРИМЕНЁН:"
            + " окно не поднимается, `MainForm` не строится.");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  §1. СЛОТЫ ПЕЧАТИ — настоящие места доли.
    // ══════════════════════════════════════════════════════════════════════

    sealed class Slot
    {
        public string Name;
        public string Text;
        public Slot(string name, string text) { Name = name; Text = text; }
    }

    static string Safe(Func<string> f)
    {
        try { return f(); }
        catch (Exception ex) { return "⛔ЗАПУСК:" + ex.GetType().Name + ":" + Innermost(ex); }
    }

    static Assembly App { get { return typeof(GlobalConfigManager).Assembly; } }

    static Type AppType(string name)
    {
        return App.GetType(name, true);
    }

    /// <summary>Спектр с заданным числом каналов; калибровка по усмотрению.</summary>
    static EnergySpectrum MakeSpectrum(int channels, EnergyCalibration calibration)
    {
        EnergySpectrum sp = new EnergySpectrum(1.0, channels);
        sp.EnergyCalibration = calibration;
        return sp;
    }

    /// <summary>
    /// `DocEnergySpectrum` БЕЗ конструктора: это `DockContent`, то есть окно, а
    /// поднимать окна на безоконном пути нельзя. Нужны ровно два поля, через
    /// которые `CombineWith` добирается до спектра.
    /// </summary>
    static object MakeDoc(EnergySpectrum spectrum)
    {
        Type tDoc = AppType("BecquerelMonitor.DocEnergySpectrum");
        object doc = FormatterServices.GetUninitializedObject(tDoc);
        ResultData rd = new ResultData();
        rd.EnergySpectrum = spectrum;
        ResultDataFile file = new ResultDataFile();
        file.ResultDataList = new List<ResultData>();
        file.ResultDataList.Add(rd);
        SetField(doc, "resultDataFile", file);
        SetField(doc, "activeResultDataIndex", 0);
        return doc;
    }

    static List<Slot> PrintSlots()
    {
        var s = new List<Slot>();

        // 1. `SpectrumAriphmetics.CombineWith` — отказ «разное число каналов».
        //    ДВА числа в одной строке; сканер их не видит (получатель — не имя,
        //    а выражение `mainSpectrum.NumberOfChannels`).
        s.Add(new Slot("Combine разные каналы", Safe(delegate
        {
            var ar = new BecquerelMonitor.Utils.SpectrumAriphmetics();
            SetField(ar, "MainSpectrum", MakeDoc(MakeSpectrum(4096, new PolynomialEnergyCalibration())));
            object added = MakeDoc(MakeSpectrum(1024, new PolynomialEnergyCalibration()));
            try
            {
                ar.GetType().GetMethod("CombineWith").Invoke(ar, new object[] { added });
                return "⛔ ОТКАЗА НЕ БЫЛО";
            }
            catch (TargetInvocationException ex) { return Innermost(ex); }
        })));

        // 2. `SpectrumAriphmetics.PolynomialOf` — отказ «нет калибровки».
        s.Add(new Slot("PolynomialOf без калибровки", Safe(delegate
        {
            return InvokePolynomialOf(MakeSpectrum(8192, null));
        })));

        // 3. Тот же метод, отказ «калибровка не полиномиальная».
        s.Add(new Slot("PolynomialOf чужой вид", Safe(delegate
        {
            return InvokePolynomialOf(MakeSpectrum(4096, new NonlinearEnergyCalibration()));
        })));

        // 4. `MeasurementController.CreateDeviceController` — отказ «конфигурации
        //    прибора нет среди загруженных»; в тексте ЧИСЛО загруженных.
        //    Карта нарочно из 1234 записей: ниже тысячи `n` и `f` неотличимы.
        s.Add(new Slot("прибор не среди загруженных", Safe(delegate
        {
            Type tMc = AppType("BecquerelMonitor.MeasurementController");
            object mc = FormatterServices.GetUninitializedObject(tMc);
            SetField(mc, "resultData", new ResultData());   // DeviceConfig == null
            Type tDcm = AppType("BecquerelMonitor.DeviceConfigManager");
            object dcm = FormatterServices.GetUninitializedObject(tDcm);
            var map = new Dictionary<string, DeviceConfigInfo>();
            for (int i = 0; i < 1234; i++) map.Add("g" + i.ToString(CultureInfo.InvariantCulture), null);
            SetField(dcm, "deviceConfigMap", map);
            SetField(mc, "deviceConfigManager", dcm);
            MethodInfo m = tMc.GetMethod("CreateDeviceController",
                                         BindingFlags.Instance | BindingFlags.NonPublic);
            try
            {
                m.Invoke(mc, null);
                return "⛔ ОТКАЗА НЕ БЫЛО";
            }
            catch (TargetInvocationException ex) { return Innermost(ex); }
        })));

        return s;
    }

    static string InvokePolynomialOf(EnergySpectrum spectrum)
    {
        Type t = AppType("BecquerelMonitor.Utils.SpectrumAriphmetics");
        MethodInfo m = t.GetMethod("PolynomialOf", BindingFlags.Static | BindingFlags.NonPublic);
        if (m == null) throw new MissingMethodException("SpectrumAriphmetics.PolynomialOf");
        try
        {
            m.Invoke(null, new object[] { spectrum, "Cutoff" });
            return "⛔ ОТКАЗА НЕ БЫЛО";
        }
        catch (TargetInvocationException ex) { return Innermost(ex); }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  §4. ЛОЖНОСТЬ ОСТАТКА СКАНЕРА — ЗАМЕРОМ, а не доводом.
    //  45 мест из 46 объявлены ложными; здесь каждое семейство проверяется
    //  на трёх культурах настоящим кодом приложения.
    // ══════════════════════════════════════════════════════════════════════

    static List<Slot> FalsePositiveSlots()
    {
        var s = new List<Slot>();

        // 32 места: `Convert.ToInt32(double)` / `Convert.ToDouble(int)` —
        // ЧИСЛОВЫЕ преобразования, культуры в них нет вовсе. Меряется настоящим
        // кодом: конструктор `FWHMPeakDetector.Spectrum` (3 таких места).
        s.Add(new Slot("FWHM Spectrum bin_edges", Safe(delegate
        {
            EnergySpectrum sp = MakeSpectrum(2048, new PolynomialEnergyCalibration());
            for (int i = 0; i < 2048; i++) sp.Spectrum[i] = i;
            var fs = new BecquerelMonitor.FWHMPeakDetector.Spectrum(sp);
            return fs.counts[1234].ToString("R", CultureInfo.InvariantCulture) + "|"
                 + fs.bin_edges_raw[1234].ToString("R", CultureInfo.InvariantCulture) + "|"
                 + fs.bin_edges_raw[2048].ToString("R", CultureInfo.InvariantCulture);
        })));

        // Голое семейство `Convert.To*` — то же, но без обвязки.
        s.Add(new Slot("Convert.To* над числом", Safe(delegate
        {
            return Convert.ToInt32(1234.5).ToString(CultureInfo.InvariantCulture) + "|"
                 + Convert.ToInt32(1235.5).ToString(CultureInfo.InvariantCulture) + "|"
                 + Convert.ToDouble(1234).ToString("R", CultureInfo.InvariantCulture);
        })));

        // `AppLog.cs:351` — `Version.ToString()`. Довод «инвариантен по
        // построению» проверяется, а не принимается на веру.
        s.Add(new Slot("Version.ToString", Safe(delegate
        {
            return new Version(1234, 5, 6, 7).ToString();
        })));

        // `EfficiencyConfigData.cs:139,152` — `Guid.NewGuid().ToString()`.
        s.Add(new Slot("Guid.ToString", Safe(delegate
        {
            return new Guid("b3f8fa53-0004-438e-9003-51a46e139bfc").ToString();
        })));

        // `PeakDetector.cs:1079` и `AudioVolumeController.cs:41` —
        // `StringBuilder.ToString()`: перегрузки с провайдером нет и быть не
        // может, внутри строки.
        s.Add(new Slot("StringBuilder.ToString", Safe(delegate
        {
            var sb = new StringBuilder("Cs-137");
            sb.Append(" / ").Append("Ba-137m");
            return sb.ToString();
        })));

        // `DocEnergySpectrum.cs:567` — `Type.ToString()`.
        s.Add(new Slot("Type.ToString", Safe(delegate
        {
            return typeof(BecquerelMonitor.Utils.SpectrumAriphmetics).ToString();
        })));

        // `EfficiencyMaker/EfficiencyCurveGraph.cs:150` — сканер счёл склейкой
        // числа со строкой, а место УЖЕ инвариантно (доля П7). Меряется тем же
        // выражением, каким рисуется подпись шкалы.
        s.Add(new Slot("EffCurveGraph подпись шкалы", Safe(delegate
        {
            int d = -1234;
            return "1e" + d.ToString(CultureInfo.InvariantCulture);
        })));

        // Пять `string.Format(Resources.…, строка)` — `AboutForm` ×2,
        // `MeasurementController` ×2, `NucBase/DataBase`, `DocEnergySpectrum`,
        // `SpectrumAriphmetics`: подставляются ТОЛЬКО строки.
        s.Add(new Slot("string.Format над строками", Safe(delegate
        {
            return string.Format("{0} / {1}", "nucdb.sqlite", "IOException: нет файла");
        })));

        return s;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  §5. ЧИСЛА `SpectrumAriphmetics` НЕ СДВИНУЛИСЬ.
    //  Вычитание фона и сглаживание — то, на чём стоит весь разбор. Правка
    //  доли трогает ТОЛЬКО текст двух отказов, и это проверяется, а не
    //  заявляется: контрольные суммы печатаются и сверяются между культурами
    //  (а полосой — со сборкой из `HEAD`).
    // ══════════════════════════════════════════════════════════════════════

    static List<Slot> MathSlots()
    {
        var s = new List<Slot>();

        s.Add(new Slot("Substract фона", Safe(delegate
        {
            EnergySpectrum main = Synthetic(2048, 1);
            EnergySpectrum bg = Synthetic(2048, 2);
            main.MeasurementTime = 1000;
            bg.MeasurementTime = 2000;
            var ar = new BecquerelMonitor.Utils.SpectrumAriphmetics(main);
            EnergySpectrum res = ar.Substract(bg);
            return Digest(res.Spectrum);
        })));

        s.Add(new Slot("Continuum SNIP", Safe(delegate
        {
            EnergySpectrum sp = Synthetic(2048, 1);
            var ar = new BecquerelMonitor.Utils.SpectrumAriphmetics(sp);
            // Радиус окна SNIP берётся из модели ПШПВ; без неё `BuildSnipRadius`
            // падает на `null`. Конструктор с моделью тянет `GlobalConfigManager`,
            // то есть чтение настроек, — поле ставится напрямую.
            var fw = new SqrtFwhmCalibration();
            fw.Coefficients = new double[] { 0.0, 2.5, 0.0 };
            SetField(ar, "FwhmCalibration", fw);
            EnergySpectrum res = ar.Continuum();
            return Digest(res.Spectrum);
        })));

        s.Add(new Slot("WMA сглаживание", Safe(delegate
        {
            EnergySpectrum sp = Synthetic(2048, 1);
            var ar = new BecquerelMonitor.Utils.SpectrumAriphmetics(sp);
            return Digest(ar.WMA(sp.Spectrum, 7));
        })));

        s.Add(new Slot("SMA сглаживание", Safe(delegate
        {
            EnergySpectrum sp = Synthetic(2048, 1);
            var ar = new BecquerelMonitor.Utils.SpectrumAriphmetics(sp);
            return Digest(ar.SMA(sp.Spectrum, 5, 100, true));
        })));

        s.Add(new Slot("Cutoff по каналу", Safe(delegate
        {
            EnergySpectrum sp = Synthetic(2048, 1);
            sp.EnergyCalibration = Poly();
            EnergySpectrum res = BecquerelMonitor.Utils.SpectrumAriphmetics
                                 .CutoffSpectrumChannels(sp, 1024);
            return Digest(res.Spectrum) + "|каналов " + res.NumberOfChannels
                   .ToString(CultureInfo.InvariantCulture);
        })));

        return s;
    }

    static PolynomialEnergyCalibration Poly()
    {
        var c = new PolynomialEnergyCalibration();
        c.PolynomialOrder = 1;
        c.Coefficients = new double[] { 0.0, 1.5 };
        return c;
    }

    /// <summary>Спектр без случайности: два «пика» на подложке.</summary>
    static EnergySpectrum Synthetic(int channels, int seed)
    {
        EnergySpectrum sp = new EnergySpectrum(1.0, channels);
        sp.EnergyCalibration = Poly();
        long total = 0;
        for (int i = 0; i < channels; i++)
        {
            double baseline = 100.0 / (1.0 + i / 256.0) * seed;
            double peak1 = 5000.0 * Math.Exp(-Math.Pow((i - 600) / 12.0, 2.0));
            double peak2 = 2500.0 * Math.Exp(-Math.Pow((i - 1400) / 18.0, 2.0));
            int v = (int)(baseline + peak1 + peak2);
            sp.Spectrum[i] = v;
            total += v;
        }
        sp.TotalPulseCount = total;
        sp.ValidPulseCount = total;
        sp.MeasurementTime = 1000;
        sp.LiveTime = 1000;
        return sp;
    }

    /// <summary>Сумма и FNV-1a по массиву — печатается инвариантом.</summary>
    static string Digest(int[] a)
    {
        long sum = 0;
        ulong h = 14695981039346656037UL;
        for (int i = 0; i < a.Length; i++)
        {
            sum += a[i];
            uint u = unchecked((uint)a[i]);
            for (int b = 0; b < 4; b++)
            {
                h ^= (byte)(u >> (b * 8));
                h *= 1099511628211UL;
            }
        }
        return "n=" + a.Length.ToString(CultureInfo.InvariantCulture)
             + " Σ=" + sum.ToString(CultureInfo.InvariantCulture)
             + " fnv=" + h.ToString("x16", CultureInfo.InvariantCulture);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  §6. `AudioVolumeController.GetCompleteDeviceName` — место есть, живой
    //  замер невозможен: метод требует COM-устройства (`MMDevice`), а
    //  `IPropertyStore` внутренний, подделать его из пробы нечем. Судится
    //  СОБРАННЫЙ КОД: тело метода разбирается по IL и называет, кого зовёт.
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>Имена методов, вызываемых телом данного метода.</summary>
    static List<string> CalleesOf(MethodInfo mi)
    {
        var names = new List<string>();
        MethodBody body = mi.GetMethodBody();
        if (body == null) return names;
        byte[] il = body.GetILAsByteArray();
        Module mod = mi.Module;
        Type[] gt = mi.DeclaringType.IsGenericType
                    ? mi.DeclaringType.GetGenericArguments() : null;
        for (int i = 0; i + 4 < il.Length; i++)
        {
            byte op = il[i];
            // call (0x28), callvirt (0x6F) — оба с четырёхбайтовым токеном.
            if (op != 0x28 && op != 0x6F) continue;
            int token = BitConverter.ToInt32(il, i + 1);
            try
            {
                MethodBase mb = mod.ResolveMethod(token, gt, null);
                var ps = new List<string>();
                foreach (ParameterInfo p in mb.GetParameters()) ps.Add(p.ParameterType.Name);
                names.Add(mb.DeclaringType.FullName + "." + mb.Name
                          + "(" + string.Join(", ", ps.ToArray()) + ")");
            }
            catch (Exception) { }
        }
        return names;
    }

    /// <summary>Печать без культуры: вызов, который в теле стоять НЕ ДОЛЖЕН.</summary>
    static bool IsBarePrint(string callee)
    {
        // `T.ToString()` любого типа-значения и `Object.ToString()`: печать по
        // культуре ПОТОКА. Перегрузка с провайдером в имени несёт «IFormatProvider».
        if (callee.EndsWith(".ToString()", StringComparison.Ordinal)) return true;
        // Склейка через `object[]` печатает сама — та же дыра, что у
        // `ROIConfigForm.GetPrimitiveRegionString` (доли П5/П6) и у трёх мест П9.
        if (callee.StartsWith("System.String.Concat(Object", StringComparison.Ordinal)) return true;
        if (callee.StartsWith("System.String.Concat(Object[]", StringComparison.Ordinal)) return true;
        return false;
    }

    static bool IsCulturedPrint(string callee)
    {
        return callee.IndexOf("IFormatProvider", StringComparison.Ordinal) >= 0;
    }

    /// <summary>
    /// ⛔ ЕДИНСТВЕННАЯ МЕРКА, КОТОРАЯ ЛОВИТ ОТКАТ ЦЕЛОГО. Измерено этой полосой:
    /// откат `NumberOfChannels.ToString(инвариант)` → `NumberOfChannels` дал
    /// код 0 и на `ru-RU`, и на `de-DE`, и на `en-US` — у ПОЛОЖИТЕЛЬНОГО целого
    /// вид не зависит от культуры вовсе (ни группировки, ни иных цифр в «G»).
    /// Значит «плечи сошлись» о таком месте не говорит НИЧЕГО, и судить надо
    /// собранный код: какая перегрузка позвана.
    /// </summary>
    static void IlSection()
    {
        Say("");
        Say("──────────────────────────────────────────────────────────────");
        Say("§6. РАЗБОР СОБРАННОГО КОДА: какая перегрузка печати позвана");
        Say("──────────────────────────────────────────────────────────────");
        Say("  ⛔ у ПОЛОЖИТЕЛЬНОГО целого вид не зависит от культуры — плечи");
        Say("     такого отката не разводят; судит этот раздел.");

        string[][] targets = new string[][] {
            new string[] { "BecquerelMonitor.Utils.SpectrumAriphmetics", "CombineWith" },
            new string[] { "BecquerelMonitor.Utils.SpectrumAriphmetics", "PolynomialOf" },
            new string[] { "BecquerelMonitor.MeasurementController", "CreateDeviceController" },
            new string[] { "BecquerelMonitor.AudioVolumeController", "GetCompleteDeviceName" },
        };

        foreach (string[] t in targets)
        {
            try
            {
                MethodInfo mi = AppType(t[0]).GetMethod(t[1],
                    BindingFlags.Instance | BindingFlags.Static
                    | BindingFlags.Public | BindingFlags.NonPublic);
                if (mi == null) throw new MissingMethodException(t[0] + "." + t[1]);
                List<string> callees = CalleesOf(mi);
                var bare = new List<string>();
                int cultured = 0;
                foreach (string n in callees)
                {
                    if (IsBarePrint(n)) bare.Add(n);
                    else if (IsCulturedPrint(n)) cultured++;
                }
                Say("");
                Say("  " + t[0].Substring(t[0].LastIndexOf('.') + 1) + "." + t[1]
                    + ": печатей с провайдером " + cultured.ToString(CultureInfo.InvariantCulture)
                    + ", БЕЗ культуры " + bare.Count.ToString(CultureInfo.InvariantCulture));
                foreach (string n in bare)
                {
                    failures++;
                    Say("    ⛔ ПЕЧАТЬ БЕЗ КУЛЬТУРЫ: " + n);
                }
                if (cultured == 0)
                {
                    failures++;
                    Say("    ⛔ ни одной печати с провайдером — место потеряно?");
                }
            }
            catch (Exception ex)
            {
                failures++;
                Say("  ⛔ разбор " + t[0] + "." + t[1] + " не вышел: "
                    + ex.GetType().Name + ": " + Innermost(ex));
            }
        }

        Say("");
        Say("  `AudioVolumeController.GetCompleteDeviceName` живым замером НЕ БЕРЁТСЯ:");
        Say("  нужен COM-`MMDevice`, а `IPropertyStore` внутренний — подделать нечем;");
        Say("  поэтому единственная его мерка — разбор выше.");

        // Положительный контроль разбора: то же на заведомо «плохих» телах —
        // иначе «печати без культуры нет» значило бы лишь «разбор молчит».
        int caught = 0;
        foreach (string nm in new string[] { "BadOnPurposeObject", "BadOnPurposeInt", "BadOnPurposeConcat" })
        {
            MethodInfo bad = typeof(RestCultureProbeF47)
                             .GetMethod(nm, BindingFlags.Static | BindingFlags.NonPublic);
            bool hit = false;
            foreach (string n in CalleesOf(bad)) if (IsBarePrint(n)) hit = true;
            Say("  [положительный контроль] " + nm + ": печать без культуры "
                + (hit ? "НАЙДЕНА — разбор видит" : "⛔ НЕ НАЙДЕНА: разбор слеп"));
            if (hit) caught++; else failures++;
        }
        Say("  контролей поймано: " + caught.ToString(CultureInfo.InvariantCulture) + " из 3");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  §7. СПЛОШНОЙ РАЗБОР ВСЕЙ СБОРКИ (`--sweep`).
    //  Ответ на вопрос финала `A244`: остался ли в приложении хоть один
    //  непереведённый случай. Судится СОБРАННЫЙ КОД, а не исходник: сканер
    //  `scan_culture.py` слеп к склейке через выражение, к `StringBuilder`,
    //  к `string.Join` и к интерполяции, а IL несёт настоящий вызов.
    // ══════════════════════════════════════════════════════════════════════

    static readonly string[] NumericTypes = {
        "System.Int32", "System.Int64", "System.Int16", "System.Byte",
        "System.SByte", "System.UInt32", "System.UInt64", "System.UInt16",
        "System.Double", "System.Single", "System.Decimal",
        "System.DateTime", "System.TimeSpan", "System.DateTimeOffset",
    };

    static bool IsNumericType(string full)
    {
        foreach (string t in NumericTypes) if (t == full) return true;
        return false;
    }

    /// <summary>
    /// Вызов, который печатает переданный `object` САМ и без провайдера:
    /// `string.Format`, `string.Concat`, `StringBuilder.Append/AppendFormat`,
    /// `string.Join`, `Convert.ToString`. Число попадает туда через `box`,
    /// и вызова `ToString()` в теле не видно вовсе.
    /// </summary>
    static bool IsObjectPrinter(MethodBase callee, string owner)
    {
        if (owner != "System.String" && owner != "System.Text.StringBuilder"
            && owner != "System.Convert") return false;
        string n = callee.Name;
        if (n != "Format" && n != "Concat" && n != "Join" && n != "Append"
            && n != "AppendFormat" && n != "AppendLine" && n != "ToString") return false;
        bool anyObject = false;
        foreach (ParameterInfo p in callee.GetParameters())
        {
            string pn = p.ParameterType.Name;
            if (pn == "IFormatProvider" || pn == "CultureInfo") return false;
            if (pn == "Object" || pn == "Object[]") anyObject = true;
        }
        return anyObject;
    }

    sealed class Hit
    {
        public string Where;
        public string What;
    }

    static void Sweep(string outPath)
    {
        Say("== `A244`: СПЛОШНОЙ РАЗБОР СОБРАННОГО ПРИЛОЖЕНИЯ (`--sweep`) ==");
        Say("");
        Say("сборка: " + App.Location);
        Say("Ищутся вызовы, печатающие/разбирающие ЧИСЛО без явной культуры:");
        Say("  `T.ToString()` и `T.ToString(String)` у числовых типов,");
        Say("  `T.Parse(String)` и `T.TryParse(String, out T)` у них же.");
        Say("⚠ ДАТА И ВРЕМЯ остаются в раскладке человека (решение Amber) —");
        Say("  `DateTime`/`TimeSpan` считаются отдельно и в итог НЕ входят.");
        Say("");

        Type[] types;
        try { types = App.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = ex.Types; }

        var printHits = new List<Hit>();
        var parseHits = new List<Hit>();
        var dateHits = new List<Hit>();
        var boxHits = new List<Hit>();
        int methods = 0;

        foreach (Type t in types)
        {
            if (t == null) continue;
            MethodBase[] all;
            try
            {
                var acc = new List<MethodBase>();
                BindingFlags bf = BindingFlags.Instance | BindingFlags.Static
                                  | BindingFlags.Public | BindingFlags.NonPublic
                                  | BindingFlags.DeclaredOnly;
                acc.AddRange(t.GetMethods(bf));
                acc.AddRange(t.GetConstructors(bf));
                all = acc.ToArray();
            }
            catch (Exception) { continue; }

            foreach (MethodBase mb in all)
            {
                MethodBody body;
                try { body = mb.GetMethodBody(); }
                catch (Exception) { continue; }
                if (body == null) continue;
                methods++;
                byte[] il;
                try { il = body.GetILAsByteArray(); }
                catch (Exception) { continue; }
                if (il == null) continue;
                Module mod = mb.Module;
                Type[] gt = t.IsGenericType ? t.GetGenericArguments() : null;
                Type[] gm = null;
                try { if (mb.IsGenericMethodDefinition) gm = mb.GetGenericArguments(); }
                catch (Exception) { }

                // ⚠ СЛЕПОЕ ПЯТНО САМОГО ЭТОГО ОБХОДА: `string.Format(шаблон,
                //   число)` и `string.Concat(object…)` печатают ВНУТРИ себя,
                //   и вызова `Double.ToString()` в теле нет вовсе — число туда
                //   попадает инструкцией `box`. Поэтому боксы числовых типов
                //   собираются отдельно и вместе с вызовами «печать object без
                //   провайдера» дают ЛОКАТОР (не меру): такое тело надо
                //   смотреть глазами.
                var boxedNums = new List<string>();
                for (int i = 0; i + 4 < il.Length; i++)
                {
                    if (il[i] != 0x8C) continue;   // box
                    try
                    {
                        Type bt = mod.ResolveType(BitConverter.ToInt32(il, i + 1), gt, gm);
                        if (bt != null && IsNumericType(bt.FullName)) boxedNums.Add(bt.Name);
                    }
                    catch (Exception) { }
                }
                bool objectPrinter = false;

                for (int i = 0; i + 4 < il.Length; i++)
                {
                    byte op = il[i];
                    if (op != 0x28 && op != 0x6F) continue;
                    int token = BitConverter.ToInt32(il, i + 1);
                    MethodBase callee;
                    try { callee = mod.ResolveMethod(token, gt, gm); }
                    catch (Exception) { continue; }
                    if (callee == null || callee.DeclaringType == null) continue;
                    string owner = callee.DeclaringType.FullName;

                    if (boxedNums.Count > 0 && !objectPrinter && IsObjectPrinter(callee, owner))
                        objectPrinter = true;

                    // ⚠ ДВЕ ДЫРЫ САМОГО ОБХОДА, найдены им же и закрыты здесь:
                    //   `StringBuilder.Append(double)` — печать числа, а
                    //   получатель НЕ числовой тип (та же слепота, что у
                    //   сканера, стоившая доле П7 шестнадцати мест);
                    //   `Convert.ToDouble(string)` — разбор, получатель
                    //   `System.Convert`.
                    if (owner == "System.Text.StringBuilder"
                        && (callee.Name == "Append" || callee.Name == "AppendLine"))
                    {
                        ParameterInfo[] ap = callee.GetParameters();
                        if (ap.Length == 1 && IsNumericType(ap[0].ParameterType.FullName))
                        {
                            string s2 = "System.Text.StringBuilder." + callee.Name
                                        + "(" + ap[0].ParameterType.Name + ")";
                            var h2 = new Hit { Where = t.FullName + "." + mb.Name, What = s2 };
                            if (ap[0].ParameterType.FullName == "System.DateTime"
                                || ap[0].ParameterType.FullName == "System.TimeSpan") dateHits.Add(h2);
                            else printHits.Add(h2);
                        }
                        continue;
                    }
                    if (owner == "System.Convert" && callee.Name.StartsWith("To", StringComparison.Ordinal)
                        && IsNumericType("System." + callee.Name.Substring(2)))
                    {
                        ParameterInfo[] cp = callee.GetParameters();
                        bool cultured2 = false, fromString = false;
                        foreach (ParameterInfo p2 in cp)
                        {
                            if (p2.ParameterType.Name == "IFormatProvider") cultured2 = true;
                            if (p2.ParameterType.Name == "String") fromString = true;
                        }
                        if (fromString && !cultured2)
                        {
                            var h3 = new Hit { Where = t.FullName + "." + mb.Name,
                                               What = "System.Convert." + callee.Name + "(String)" };
                            parseHits.Add(h3);
                        }
                        continue;
                    }

                    if (!IsNumericType(owner)) continue;

                    ParameterInfo[] ps = callee.GetParameters();
                    bool cultured = false;
                    var names = new List<string>();
                    foreach (ParameterInfo p in ps)
                    {
                        names.Add(p.ParameterType.Name);
                        if (p.ParameterType.Name == "IFormatProvider"
                            || p.ParameterType.Name == "NumberFormatInfo"
                            || p.ParameterType.Name == "DateTimeFormatInfo"
                            || p.ParameterType.Name == "CultureInfo") cultured = true;
                    }
                    if (cultured) continue;

                    string sig = owner + "." + callee.Name + "(" + string.Join(", ", names.ToArray()) + ")";
                    string where = t.FullName + "." + mb.Name;
                    bool isDate = owner == "System.DateTime" || owner == "System.TimeSpan"
                                  || owner == "System.DateTimeOffset";

                    if (callee.Name == "ToString")
                    {
                        var h = new Hit { Where = where, What = sig };
                        if (isDate) dateHits.Add(h); else printHits.Add(h);
                    }
                    else if (callee.Name == "Parse" || callee.Name == "TryParse")
                    {
                        // `Parse(String)` без культуры; `Parse(Char)` и прочее — не разбор текста.
                        if (ps.Length > 0 && ps[0].ParameterType.Name == "String")
                        {
                            var h = new Hit { Where = where, What = sig };
                            if (isDate) dateHits.Add(h); else parseHits.Add(h);
                        }
                    }
                }

                if (objectPrinter)
                {
                    var kinds = new Dictionary<string, int>();
                    foreach (string k in boxedNums)
                        kinds[k] = kinds.ContainsKey(k) ? kinds[k] + 1 : 1;
                    var parts = new List<string>();
                    foreach (KeyValuePair<string, int> kv in kinds)
                        parts.Add(kv.Key + "×" + kv.Value.ToString(CultureInfo.InvariantCulture));
                    boxHits.Add(new Hit {
                        Where = t.FullName + "." + mb.Name,
                        What = "box " + string.Join(", ", parts.ToArray())
                               + " + печать object без провайдера" });
                }
            }
        }

        Say("тел методов разобрано: " + methods.ToString(CultureInfo.InvariantCulture));
        Say("");
        Report("ПЕЧАТЬ ЧИСЛА БЕЗ КУЛЬТУРЫ", printHits);
        Report("РАЗБОР ЧИСЛА БЕЗ КУЛЬТУРЫ", parseHits);
        Report("ДАТА/ВРЕМЯ без культуры (в итог НЕ входит: решение Amber)", dateHits);
        Report("⚠ ЛОКАТОР (не мера): в теле есть box числа И печать object без"
               + " провайдера — смотреть глазами", boxHits);

        // ── Чем это грозит финалу: КОСТЫЛЬ ЛЕЧИТ ТОЛЬКО ДРОБНУЮ ЧАСТЬ.
        Say("── целое культуре не подчиняется: замер, а не довод ──");
        foreach (string nm in Foreign)
        {
            CultureInfo os2 = CultureInfo.GetCultureInfo(nm);
            Thread.CurrentThread.CurrentCulture = os2;
            Say("  " + nm + ": (1234567).ToString() = «" + (1234567).ToString()
                + "», (-1234).ToString() = «" + (-1234).ToString()
                + "», (1.5).ToString() = «" + (1.5).ToString() + "»");
        }
        // Тот самый клон, что стоит в `MainForm.cs:158-160`: разделитель
        // ДРОБНОЙ части подменён на точку. Целому это не меняет ничего.
        CultureInfo crutch = (CultureInfo)CultureInfo.GetCultureInfo("ru-RU").Clone();
        crutch.NumberFormat.NumberDecimalSeparator = ".";
        Thread.CurrentThread.CurrentCulture = crutch;
        Say("  клон `MainForm` (ru-RU, разделитель дроби = точка):"
            + " (1234567).ToString() = «" + (1234567).ToString()
            + "», (1.5).ToString() = «" + (1.5).ToString() + "»");
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        Say("  ⇒ костыль правит ТОЛЬКО дробную часть; на целые он не влияет вовсе,");
        Say("    значит его снятие ни одного из найденных мест не сдвинет.");
        Say("");

        int total = printHits.Count + parseHits.Count;
        Say("");
        Say("══════════════════════════════════════════════════════════════");
        Say("ИТОГ: непереведённых случаев ЧИСЛА в приложении: "
            + total.ToString(CultureInfo.InvariantCulture));
        Say("══════════════════════════════════════════════════════════════");

        // ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ КАЖДОЙ ИЗ ЧЕТЫРЁХ ПРОВЕРОК. Без него «ноль
        //    разборов» и «ноль `Append(число)`» значили бы лишь «проверка слепа»
        //    — а именно так уже терялись места (`checked-what-will-be-used`).
        var controls = new string[][] {
            new string[] { "BadOnPurposeInt",     "System.Int32.ToString(" },
            new string[] { "BadOnPurposeDouble",  "System.Double.ToString(" },
            new string[] { "BadOnPurposeAppend",  "System.Text.StringBuilder.Append(Double" },
            new string[] { "BadOnPurposeConvert", "System.Convert.ToDouble(String" },
        };
        int caught = 0;
        foreach (string[] c in controls)
        {
            MethodInfo bad = typeof(RestCultureProbeF47)
                             .GetMethod(c[0], BindingFlags.Static | BindingFlags.NonPublic);
            bool hit = false;
            foreach (string n in CalleesOf(bad))
                if (n.StartsWith(c[1], StringComparison.Ordinal)) hit = true;
            Say("[положительный контроль] " + c[0] + " → «" + c[1] + "…»: "
                + (hit ? "НАЙДЕНО — проверка видит" : "⛔ НЕ НАЙДЕНО: ПРОВЕРКА СЛЕПА"));
            if (hit) caught++; else failures++;
        }
        Say("[положительный контроль обхода] поймано " + caught.ToString(CultureInfo.InvariantCulture)
            + " из 4");

        Finish(outPath);
    }

    static void Report(string title, List<Hit> hits)
    {
        Say("── " + title + ": " + hits.Count.ToString(CultureInfo.InvariantCulture) + " ──");
        var byOwner = new Dictionary<string, List<Hit>>();
        foreach (Hit h in hits)
        {
            string key = h.Where.Substring(0, h.Where.LastIndexOf('.'));
            if (!byOwner.ContainsKey(key)) byOwner[key] = new List<Hit>();
            byOwner[key].Add(h);
        }
        var keys = new List<string>(byOwner.Keys);
        keys.Sort();
        foreach (string k in keys)
        {
            Say("  " + k + "  ×" + byOwner[k].Count.ToString(CultureInfo.InvariantCulture));
            foreach (Hit h in byOwner[k])
                Say("      " + h.Where.Substring(h.Where.LastIndexOf('.') + 1) + " → " + h.What);
        }
        Say("");
    }

    static string BadOnPurposeDouble(double value)
    {
        return "x" + value.ToString("F2");
    }

    static string BadOnPurposeAppend(double value)
    {
        var sb = new StringBuilder();
        sb.Append("x").Append(value);
        return sb.ToString();
    }

    static double BadOnPurposeConvert(string text)
    {
        return Convert.ToDouble(text);
    }

    /// <summary>Нарочно испорченные тела — мерка для разбора IL из §6.</summary>
    static string BadOnPurposeObject(object value)
    {
        return "x" + value.ToString();
    }

    static string BadOnPurposeInt(int value)
    {
        return "x" + value.ToString();
    }

    static string BadOnPurposeConcat(int value)
    {
        return string.Concat(new object[] { "x", value, "y" });
    }

    // ══════════════════════════════════════════════════════════════════════
    //  ПРОГОН
    // ══════════════════════════════════════════════════════════════════════

    static void Run()
    {
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        List<Slot> refPrint = PrintSlots();
        List<Slot> refFalse = FalsePositiveSlots();
        List<Slot> refMath = MathSlots();

        Say("");
        Say("══════════════════════════════════════════════════════════════");
        Say("ЭТАЛОН (культура потока = инвариант)");
        Say("  §1 слотов печати: " + refPrint.Count
            + ";  §4 слотов «ложный остаток»: " + refFalse.Count
            + ";  §5 слотов вычислений: " + refMath.Count);
        Say("══════════════════════════════════════════════════════════════");
        Dump("§1", refPrint, true);
        Dump("§4", refFalse, true);
        Dump("§5", refMath, true);

        // ── §2. ВИД: разделителя групп нет ни в одном слоте.
        Say("");
        Say("── §2 ВИД: группировка разрядов в эталоне (решение Amber: её нет вовсе) ──");
        int grouped = 0;
        foreach (Slot sl in refPrint)
        {
            if (!HasGrouping(sl.Text)) continue;
            grouped++; failures++;
            Say("  ⛔ РАЗДЕЛИТЕЛЬ ГРУПП: " + sl.Name + " = «" + sl.Text + "»");
        }
        Say("  слотов с разделителем групп: " + grouped + (grouped == 0 ? " — так и надо" : ""));
        Say("  (все числа слотов ≥ 1000 нарочно: ниже тысячи `n` и `f` неотличимы)");

        string[] crossPrint = null;

        foreach (string name in Foreign)
        {
            Say("");
            Say("──────────────────────────────────────────────────────────────");
            Say("ПЛЕЧО: системная культура потока " + name + " (подмены разделителя НЕТ)");
            Say("──────────────────────────────────────────────────────────────");

            CultureInfo os = CultureInfo.GetCultureInfo(name);
            Thread.CurrentThread.CurrentCulture = os;

            string bare = (1.5).ToString();
            string want = os.NumberFormat.NumberDecimalSeparator == "," ? "1,5" : "1.5";
            Say("  [положительный контроль] `(1.5).ToString()` без культуры = «" + bare + "»  "
                + (bare == want ? "— плечо воспроизводит культуру"
                                : "⛔ ОЖИДАЛОСЬ «" + want + "»: ПЛЕЧО НЕ МЕРИТ"));
            if (bare != want) failures++;
            Say("  [положительный контроль] `(1234.5).ToString(\"n2\")` без культуры = «"
                + (1234.5).ToString("n2") + "» — так выглядела бы группировка, если бы её оставили");

            int bad = Compare("§1 ПЕЧАТЬ", refPrint, PrintSlots());
            Compare("§4 ложный остаток", refFalse, FalsePositiveSlots());
            Compare("§5 вычисления", refMath, MathSlots());

            List<Slot> here = PrintSlots();
            if (crossPrint == null)
            {
                crossPrint = new string[here.Count];
                for (int i = 0; i < here.Count; i++) crossPrint[i] = here[i].Text;
                Say("  ПЕРЕКРЁСТНОЕ: снимок этого плеча взят меркой следующим");
            }
            else
            {
                int badCross = 0;
                for (int i = 0; i < here.Count && i < crossPrint.Length; i++)
                {
                    if (here[i].Text == crossPrint[i]) continue;
                    badCross++; failures++;
                    Say("  ⛔ ПЕРЕКРЁСТНОЕ РАЗОШЛОСЬ: " + here[i].Name
                        + " — на " + Foreign[0] + " «" + crossPrint[i]
                        + "», здесь «" + here[i].Text + "»");
                }
                Say("  ПЕРЕКРЁСТНОЕ: расхождений " + badCross
                    + (badCross == 0 ? " — строка " + Foreign[0] + " читается здесь так же" : ""));
            }

            int gr = 0;
            foreach (Slot sl in here) if (HasGrouping(sl.Text)) gr++;
            if (gr > 0) { failures++; Say("  ⛔ §2 ВИД: разделитель групп на плече " + name + ": слотов " + gr); }
            else Say("  §2 ВИД: разделителя групп на плече нет");
            if (bad == 0) { }
        }

        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        IlSection();
    }

    static void Dump(string tag, List<Slot> slots, bool judgeLaunch)
    {
        foreach (Slot sl in slots)
        {
            Say("  " + tag + " " + Pad(sl.Name) + " = «" + sl.Text + "»");
            if (judgeLaunch && sl.Text.StartsWith("⛔", StringComparison.Ordinal))
            {
                failures++;
                Say("     ⛔ слот не снят — мерить нечем");
            }
        }
    }

    static int Compare(string tag, List<Slot> reference, List<Slot> here)
    {
        int bad = 0;
        if (here.Count != reference.Count)
        {
            failures++;
            Say("  ⛔ " + tag + ": число слотов разошлось: " + here.Count + " против " + reference.Count);
        }
        for (int i = 0; i < here.Count && i < reference.Count; i++)
        {
            if (here[i].Text == reference[i].Text) continue;
            bad++; failures++;
            Say("  ⛔ " + tag + " РАЗОШЛОСЬ: " + here[i].Name);
            Say("       эталон: «" + reference[i].Text + "»");
            Say("       здесь:  «" + here[i].Text + "»");
        }
        Say("  " + tag + ": слотов " + here.Count + ", разошлось " + bad
            + (bad == 0 ? " — вид строки тот же, что у инварианта" : ""));
        return bad;
    }

    /// <summary>
    /// Есть ли в строке РАЗДЕЛИТЕЛЬ ГРУПП: цифра, потом запятая/пробел
    /// (обычный, неразрывный, узкий) или апостроф, потом РОВНО три цифры.
    /// </summary>
    static bool HasGrouping(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        for (int i = 1; i + 3 < s.Length; i++)
        {
            char c = s[i];
            if (c != ',' && c != ' ' && c != ' ' && c != ' ' && c != '\'') continue;
            if (!char.IsDigit(s[i - 1])) continue;
            if (!char.IsDigit(s[i + 1]) || !char.IsDigit(s[i + 2]) || !char.IsDigit(s[i + 3])) continue;
            if (i + 4 < s.Length && char.IsDigit(s[i + 4])) continue;
            return true;
        }
        return false;
    }

    static string Pad(string s)
    {
        return s.Length >= 30 ? s : s + new string(' ', 30 - s.Length);
    }

    static string Innermost(Exception ex)
    {
        while (ex.InnerException != null) ex = ex.InnerException;
        return ex.Message;
    }

    static void SetField(object target, string name, object value)
    {
        Type t = target.GetType();
        while (t != null)
        {
            FieldInfo fi = t.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic
                                            | BindingFlags.Public);
            if (fi != null) { fi.SetValue(target, value); return; }
            t = t.BaseType;
        }
        throw new MissingFieldException(target.GetType().Name + "." + name);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  СТОРОЖ МОДАЛЬНЫХ ОКОН (образец — `CultureProbeO14`, `A245`)
    // ══════════════════════════════════════════════════════════════════════

    const string DialogClass = "#32770";
    const uint WM_CLOSE = 0x0010;

    delegate bool EnumWindowProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern bool EnumWindows(EnumWindowProc lpfn, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern bool EnumChildWindows(IntPtr hWnd, EnumWindowProc lpfn, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetClassNameW(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetWindowTextW(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    static volatile bool modalWatchStop;
    static Thread modalWatchThread;
    static int modalSeen;

    static void ModalWatchStart()
    {
        modalWatchThread = new Thread(delegate()
        {
            uint self = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
            Dictionary<long, bool> known = new Dictionary<long, bool>();
            while (!modalWatchStop)
            {
                List<IntPtr> found = new List<IntPtr>();
                try
                {
                    EnumWindows(delegate(IntPtr h, IntPtr l)
                    {
                        uint pid;
                        GetWindowThreadProcessId(h, out pid);
                        if (pid != self) return true;
                        StringBuilder cls = new StringBuilder(64);
                        GetClassNameW(h, cls, cls.Capacity);
                        if (cls.ToString() == DialogClass) found.Add(h);
                        return true;
                    }, IntPtr.Zero);
                }
                catch (Exception) { }

                foreach (IntPtr h in found)
                {
                    long key = h.ToInt64();
                    if (known.ContainsKey(key)) continue;
                    known[key] = true;
                    modalSeen++;
                    failures++;
                    Say("⛔ БЕЗОКОННЫЙ ПУТЬ УПЁРСЯ В МОДАЛЬНОЕ ОКНО: «" + ModalText(h)
                        + "» — сторож закрывает его сам; нажать «ОК» здесь некому,"
                        + " разряд `A245`");
                    try { PostMessage(h, WM_CLOSE, IntPtr.Zero, IntPtr.Zero); }
                    catch (Exception) { }
                }
                Thread.Sleep(200);
            }
        });
        modalWatchThread.IsBackground = true;
        modalWatchThread.Start();
    }

    static string ModalText(IntPtr dialog)
    {
        StringBuilder acc = new StringBuilder();
        try
        {
            EnumChildWindows(dialog, delegate(IntPtr ch, IntPtr l)
            {
                StringBuilder cls = new StringBuilder(64);
                GetClassNameW(ch, cls, cls.Capacity);
                if (cls.ToString() == "Static")
                {
                    StringBuilder txt = new StringBuilder(512);
                    GetWindowTextW(ch, txt, txt.Capacity);
                    string t = txt.ToString().Trim();
                    if (t.Length > 0)
                    {
                        if (acc.Length > 0) acc.Append(" / ");
                        acc.Append(t);
                    }
                }
                return true;
            }, IntPtr.Zero);
        }
        catch (Exception) { }
        return acc.Length == 0 ? "(текст не прочитан)" : acc.ToString();
    }

    static void ModalWatchStop()
    {
        modalWatchStop = true;
        if (modalWatchThread != null) modalWatchThread.Join(2000);
    }

    static void ModalControl()
    {
        Say("");
        Say("══════════════════════════════════════════════════════════════");
        Say("ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ СТОРОЖА ОКОН (`--modal-control`)");
        Say("══════════════════════════════════════════════════════════════");
        int before = modalSeen;
        DateTime t0 = DateTime.UtcNow;
        Thread th = new Thread(delegate()
        {
            System.Windows.Forms.MessageBox.Show("контрольное окно полосы F47",
                                                 "контроль", System.Windows.Forms.MessageBoxButtons.OK);
        });
        th.IsBackground = true;
        th.SetApartmentState(ApartmentState.STA);
        th.Start();
        bool closed = th.Join(20000);
        double sec = (DateTime.UtcNow - t0).TotalSeconds;
        Say("  окно поднято нарочно, закрыто сторожем: " + (closed ? "да" : "⛔ НЕТ")
            + ", секунд " + sec.ToString("F1", CultureInfo.InvariantCulture)
            + ", окон назвал сторож: " + (modalSeen - before));
        if (!closed) failures++;
    }

    // ══════════════════════════════════════════════════════════════════════

    static void Say(string line)
    {
        lock (Log)
        {
            Log.AppendLine(line);
            Console.WriteLine(line);
        }
    }

    static void Finish(string outPath)
    {
        if (string.IsNullOrEmpty(outPath)) return;
        try
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(outPath));
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(outPath, Log.ToString(), new UTF8Encoding(false));
            Console.WriteLine("отчёт: " + Path.GetFullPath(outPath));
        }
        catch (Exception ex) { Console.WriteLine("отчёт не записан: " + ex.Message); }
    }
}
