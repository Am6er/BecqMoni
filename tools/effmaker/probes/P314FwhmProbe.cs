// Проба полосы П314 (15.10.2026): РЕВИЗИЯ КАЛИБРОВКИ ПШПВ И ПРЕДПРОСМОТРА ФОРМЫ ПИКА.
//
// Без окон, отражением. Один exe работает со сборкой ДО правки полосы (обязан показать
// дефекты) и ПОСЛЕ (обязан показать, что они ушли); контроли обязаны сойтись на обеих.
//
//   P314FwhmProbe.exe        (из каталога, где лежит BecquerelMonitor.exe)
//
// Плечи:
//   A  подгонка: синтетические опоры с известной ПШПВ(канал) — подгонка обязана вернуть
//      заложенные параметры (три формы); вырожденные входы обязаны быть ОТВЕРГНУТЫ с
//      названной причиной (одна опора, две на одном канале, убывание, ноль внизу, p ≥ 1).
//   B  XML: кривая трёх форм с формой пика — сохранить → загрузить под культурой ru-RU,
//      сверка ПОБИТОВО; плюс 200 000 случайных double через тот же сериализатор.
//      Положительный контроль: потеря на «G15» обязана быть замечена сверкой.
//   C  вид: смена формы кривой у ПОСЧИТАННОЙ кривой — «Рассчитать» обязана загореться
//      (дефект: гасла); «Рассчитать» после смены кладёт в документ новую форму.
//   D  вид: столбец ПШПВ таблицы опор печатает дробную ширину не полным double.
//   E  вид: «Получить все пики» с документом ДРУГОГО числа каналов — замер для вопроса
//      (не правка): во что превращается кривая.
//   F  предпросмотр формы пика (Фойгт): числа ширин в легенде — те, что у нарисованной кривой.
//   G  вид: «Сохранить в конфиг прибора» после записи доводит документ до того же состояния,
//      что «Рассчитать»: документ «изменён», пики перерисованы (разбором IL — у кнопки
//      окно «Да/Нет», безоконно её не нажать) и общий хвост исполняется отражением;
//      «Обновить опоры» графика ПШПВ помечает документ изменённым.
//   H  конфигурация прибора без элемента кривой в файле: кривая по её трём числам или
//      встроенная прямая конструктора (замер для вопроса, не правка).
//   --png=<каталог>  снимки предпросмотра (Фойгт) 640×480 и 1000×640 без рабочего стола.
//
// Коды: 0 — дефектов нет, контроли сошлись (сборка ПОСЛЕ); 1 — дефект виден (сборка ДО);
// 3 — контроль НЕ сошёлся (замер пуст); 5 — обрыв.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;
using BecquerelMonitor;
using BecquerelMonitor.Utils;
using XPTable.Models;

static class P314FwhmProbe
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static int defect, control;

    [STAThread]
    static int Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }
        CultureInfo.CurrentCulture = Inv;
        CultureInfo.DefaultThreadCurrentCulture = Inv;
        try
        {
            Console.WriteLine(ProbeTargetFramework.Describe());
            Console.WriteLine("приложение: " + typeof(ResultData).Assembly.Location);
            Console.WriteLine("собрано: " + File.GetLastWriteTime(typeof(ResultData).Assembly.Location).ToString("yyyy-MM-dd HH:mm:ss", Inv));
            Console.WriteLine();
            ArmFit();
            ArmXml();
            ArmTypeSwitch();
            ArmFwhmColumn();
            ArmGetAll();
            ArmPreview();
            ArmSaveTail();
            ArmDeviceNumbers();
            foreach (string a in args)
            {
                if (a.StartsWith("--png=")) Pictures(a.Substring(6));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("ОБРЫВ: " + ex);
            return 5;
        }
        Console.WriteLine();
        Console.WriteLine("ИТОГ: дефектов {0}, несработавших контролей {1}", defect, control);
        if (control > 0) return 3;
        return defect > 0 ? 1 : 0;
    }

    // ================================================================== A — подгонка

    static void ArmFit()
    {
        Console.WriteLine("== A. Подгонка: заложенные параметры возвращаются, вырожденное отвергается");
        const int N = 4096;

        // A1 простой корень: F² = 4 + 0.05·ch
        {
            var c = new SimpleSqrtFwhmCalibration();
            foreach (int ch in new[] { 100, 400, 900, 1600 })
                c.CalibrationPeaks.Add(new CalibrationPeak { Channel = ch, FWHM = Math.Sqrt(4.0 + 0.05 * ch) });
            bool ok = c.PerformCalibration(N);
            double e = Math.Max(Rel(c.Coefficients[0], 4.0), Rel(c.Coefficients[1], 0.05));
            Line("A1 простой корень F²=4+0.05ch, 4 опоры", ok, "c = [" + G(c.Coefficients[0]) + ", " + G(c.Coefficients[1]) + "], отн. ошибка " + E(e));
            Control(ok && e < 1e-9, "A1: заложенные параметры простого корня не вернулись");
        }
        // A2 корень полинома: F² = 4 + 0.05·ch + 1e-5·ch²
        {
            var c = new SqrtFwhmCalibration();
            foreach (int ch in new[] { 50, 300, 800, 1500, 3000 })
                c.CalibrationPeaks.Add(new CalibrationPeak { Channel = ch, FWHM = Math.Sqrt(4.0 + 0.05 * ch + 1e-5 * ch * ch) });
            bool ok = c.PerformCalibration(N);
            double e = Math.Max(Rel(c.Coefficients[0], 4.0), Math.Max(Rel(c.Coefficients[1], 0.05), Rel(c.Coefficients[2], 1e-5)));
            Line("A2 корень полинома F²=4+0.05ch+1e-5ch², 5 опор", ok, "c = [" + G(c.Coefficients[0]) + ", " + G(c.Coefficients[1]) + ", " + G(c.Coefficients[2]) + "], отн. ошибка " + E(e));
            Control(ok && e < 1e-7, "A2: заложенные параметры корня полинома не вернулись");
        }
        // A3 степенная: F = 0.45·ch^0.62, плюс опора на канале 0 (как FWHM_AT_0) — её логарифма нет
        {
            var c = new PowerFwhmCalibration();
            c.CalibrationPeaks.Add(new CalibrationPeak { Channel = 0, FWHM = 3.0 });
            foreach (int ch in new[] { 40, 250, 900, 2600 })
                c.CalibrationPeaks.Add(new CalibrationPeak { Channel = ch, FWHM = 0.45 * Math.Pow(ch, 0.62) });
            bool ok = c.PerformCalibration(N);
            double e = Math.Max(Rel(c.Coefficients[0], 0.45), Rel(c.Coefficients[1], 0.62));
            Line("A3 степенная F=0.45·ch^0.62, 4 опоры + опора на канале 0", ok, "a = " + G(c.Coefficients[0]) + ", p = " + G(c.Coefficients[1]) + ", отн. ошибка " + E(e));
            Control(ok && e < 1e-9, "A3: заложенные параметры степенной не вернулись");
        }

        // Положительный контроль: заведомо плохие входы ОБЯЗАНЫ быть отвергнуты.
        Refuse("A4 простой корень, одна опора", new SimpleSqrtFwhmCalibration(), N, FwhmCalibration.FwhmCheckResult.Rejected,
               P(500, 10));
        Refuse("A5 простой корень, две опоры на одном канале", new SimpleSqrtFwhmCalibration(), N, FwhmCalibration.FwhmCheckResult.NotFinite,
               P(500, 10), P(500, 12));
        Refuse("A6 простой корень, ширина убывает (300;30),(600;28)", new SimpleSqrtFwhmCalibration(), N, FwhmCalibration.FwhmCheckResult.Rejected,
               P(300, 30), P(600, 28));
        Refuse("A7 простой корень, ноль внизу (100;2),(1000;20)", new SimpleSqrtFwhmCalibration(), N, FwhmCalibration.FwhmCheckResult.NonPositiveWidth,
               P(100, 2), P(1000, 20));
        Refuse("A8 корень полинома, две из трёх на одном канале", new SqrtFwhmCalibration(), N, FwhmCalibration.FwhmCheckResult.NotFinite,
               P(100, 5), P(100, 6), P(900, 20));
        Refuse("A9 степенная, p = 1.30 ≥ 1", new PowerFwhmCalibration(), N, FwhmCalibration.FwhmCheckResult.Rejected,
               P(100, 10), P(1000, 200));
        Refuse("A10 степенная, две опоры на одном канале", new PowerFwhmCalibration(), N, FwhmCalibration.FwhmCheckResult.NotFinite,
               P(500, 10), P(500, 12));
        Refuse("A11 степенная, единственная опора не на нуле", new PowerFwhmCalibration(), N, FwhmCalibration.FwhmCheckResult.Rejected,
               P(0, 3), P(800, 20));

        // Экстраполяция ниже опор: у корневых форм с положительным c0 и у степенной ширина
        // ниже первой опоры конечна и положительна, на нулевом канале — c0 и 0 соответственно.
        {
            var s = new SimpleSqrtFwhmCalibration();
            s.CalibrationPeaks.Add(P(300, Math.Sqrt(4 + 0.05 * 300)));
            s.CalibrationPeaks.Add(P(2000, Math.Sqrt(4 + 0.05 * 2000)));
            s.PerformCalibration(N);
            var p = new PowerFwhmCalibration();
            p.CalibrationPeaks.Add(P(300, 0.45 * Math.Pow(300, 0.62)));
            p.CalibrationPeaks.Add(P(2000, 0.45 * Math.Pow(2000, 0.62)));
            p.PerformCalibration(N);
            Console.WriteLine("  ниже опор (канал 10 / 1 / 0): простой корень " + G(s.ChannelToFwhm(10)) + " / " + G(s.ChannelToFwhm(1)) + " / " + G(s.ChannelToFwhm(0))
                              + "; степенная " + G(p.ChannelToFwhm(10)) + " / " + G(p.ChannelToFwhm(1)) + " / " + G(p.ChannelToFwhm(0)));
            Control(Math.Abs(s.ChannelToFwhm(10) - Math.Sqrt(4.5)) < 1e-9 && Math.Abs(p.ChannelToFwhm(10) - 0.45 * Math.Pow(10, 0.62)) < 1e-9,
                    "экстраполяция ниже опор не та, что у заложенной формы");
        }
        Console.WriteLine();
    }

    static CalibrationPeak P(int ch, double fwhm) { return new CalibrationPeak { Channel = ch, FWHM = fwhm }; }

    static void Refuse(string label, FwhmCalibration c, int n, FwhmCalibration.FwhmCheckResult want, params CalibrationPeak[] pts)
    {
        foreach (var p in pts) c.CalibrationPeaks.Add(p);
        bool ok; string why;
        try { ok = c.PerformCalibration(n); why = c.LastCheck.ToString(); }
        catch (Exception ex) { ok = false; why = "исключение " + ex.GetType().Name; }
        Console.WriteLine("  {0}: принята={1}, причина {2} (ждём {3})", label, ok, why, want);
        Control(!ok && why == want.ToString(), label + ": плохой вход НЕ отвергнут с ожидаемой причиной");
    }

    // ================================================================== B — XML

    static void ArmXml()
    {
        Console.WriteLine("== B. Сохранить → загрузить под ru-RU: побитово");
        CultureInfo saved = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new CultureInfo("ru-RU");
        try
        {
            var rnd = new Random(314);
            foreach (FwhmCalibration c in new FwhmCalibration[] { new SimpleSqrtFwhmCalibration(), new SqrtFwhmCalibration(), new PowerFwhmCalibration() })
            {
                for (int i = 0; i < 4; i++)
                {
                    int ch = 200 + 700 * i;
                    double f = c is PowerFwhmCalibration ? 0.45 * Math.Pow(ch, 0.62)
                             : c is SqrtFwhmCalibration ? Math.Sqrt(4.0 + 0.05 * ch + 1e-5 * ch * ch)
                             : Math.Sqrt(4.0 + 0.05 * ch);
                    c.CalibrationPeaks.Add(new CalibrationPeak { Channel = ch, Energy = 661.657 * (i + 1) / 3.0 + 1e-9 * rnd.NextDouble(), FWHM = f * (1.0 + 1e-3 * (rnd.NextDouble() - 0.5)) });
                }
                if (!c.PerformCalibration(4096)) { Control(false, "B: подгонка " + c.GetType().Name + " не прошла"); continue; }
                c.PeakType = FwhmCalibration.VoigtPeakType;
                c.VoigtSigma = 0.7; c.VoigtGamma = 0.3;
                c.ExpGaussExpLeftTail = 1.3; c.ExpGaussExpRightTail = 2.9;
                c.GaussianChi2Total = 123.456789; c.ExpGaussExpChi2Total = 98.7654321; c.VoigtChi2Total = 87.1; c.Chi2pNdp = 1.0123456789;

                var cfg = new FWHMPeakDetectionMethodConfig();
                cfg.FwhmCalibration = c;
                var xs = new XmlSerializer(typeof(FWHMPeakDetectionMethodConfig));
                var sw = new StringWriter(CultureInfo.InvariantCulture);
                xs.Serialize(sw, cfg);
                string xml = sw.ToString();
                var back = (FWHMPeakDetectionMethodConfig)xs.Deserialize(new StringReader(xml));
                List<string> diff = DiffCurve(c, back.FwhmCalibration);
                bool commas = xml.Contains("<Coefficient>") && System.Text.RegularExpressions.Regex.IsMatch(xml, "<(Coefficient|FWHM|Energy)>-?[0-9]+,[0-9]");
                Console.WriteLine("  {0}: расхождений {1}{2}; запятая в числах файла: {3}", c.GetType().Name, diff.Count,
                                  diff.Count == 0 ? "" : " (" + string.Join("; ", diff.Take(4)) + ")", commas);
                Control(diff.Count == 0 && !commas, "B: круг XML " + c.GetType().Name + " не побитов или пишет запятую");
            }

            // Случайные double тем же сериализатором (массивом коэффициентов).
            const int M = 200000;
            var vals = new double[M];
            for (int i = 0; i < M; i++)
            {
                double mant = rnd.NextDouble() * 2.0 - 1.0;
                int exp = rnd.Next(-12, 12);
                vals[i] = mant * Math.Pow(10, exp);
            }
            var xa = new XmlSerializer(typeof(double[]));
            var swa = new StringWriter(CultureInfo.InvariantCulture);
            xa.Serialize(swa, vals);
            var backa = (double[])xa.Deserialize(new StringReader(swa.ToString()));
            int bad = 0; string firstBad = null;
            for (int i = 0; i < M; i++)
            {
                if (BitConverter.DoubleToInt64Bits(vals[i]) != BitConverter.DoubleToInt64Bits(backa[i]))
                {
                    bad++;
                    if (firstBad == null) firstBad = vals[i].ToString("G17", Inv) + " → " + backa[i].ToString("G17", Inv);
                }
            }
            // Положительный контроль сверки: тот же массив через «G15» обязан потерять биты.
            int lossy = 0;
            for (int i = 0; i < M; i++)
            {
                double r = double.Parse(vals[i].ToString("G15", Inv), Inv);
                if (BitConverter.DoubleToInt64Bits(vals[i]) != BitConverter.DoubleToInt64Bits(r)) lossy++;
            }
            Console.WriteLine("  {0} случайных double: XmlSerializer расходится в {1}{2}; контроль «G15» расходится в {3}",
                              M, bad, firstBad == null ? "" : " (первое: " + firstBad + ")", lossy);
            Control(lossy > 0, "B: сверка не видит даже потерь «G15» — сверка слепа");
            if (bad > 0)
            {
                Console.WriteLine("  ⚠ НАХОДКА: XmlSerializer .NET Framework теряет биты double (формат «R» на 64 битах) — касается ВСЕХ чисел XML, не только ПШПВ");
            }
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = saved;
        }
        Console.WriteLine();
    }

    static List<string> DiffCurve(FwhmCalibration a, FwhmCalibration b)
    {
        var d = new List<string>();
        if (b == null) { d.Add("кривой нет"); return d; }
        if (a.GetType() != b.GetType()) d.Add("тип " + a.GetType().Name + " → " + b.GetType().Name);
        if (a.Coefficients.Length != b.Coefficients.Length) d.Add("число коэффициентов");
        else for (int i = 0; i < a.Coefficients.Length; i++) Bits(d, "c" + i, a.Coefficients[i], b.Coefficients[i]);
        if (a.CalibrationPeaks.Count != b.CalibrationPeaks.Count) d.Add("число опор " + a.CalibrationPeaks.Count + " → " + b.CalibrationPeaks.Count);
        else for (int i = 0; i < a.CalibrationPeaks.Count; i++)
            {
                if (a.CalibrationPeaks[i].Channel != b.CalibrationPeaks[i].Channel) d.Add("канал опоры " + i);
                Bits(d, "E" + i, a.CalibrationPeaks[i].Energy, b.CalibrationPeaks[i].Energy);
                Bits(d, "F" + i, a.CalibrationPeaks[i].FWHM, b.CalibrationPeaks[i].FWHM);
            }
        if (a.PeakType != b.PeakType) d.Add("форма пика");
        Bits(d, "σ", a.VoigtSigma, b.VoigtSigma); Bits(d, "γ", a.VoigtGamma, b.VoigtGamma);
        Bits(d, "L", a.ExpGaussExpLeftTail, b.ExpGaussExpLeftTail); Bits(d, "R", a.ExpGaussExpRightTail, b.ExpGaussExpRightTail);
        Bits(d, "χ²G", a.GaussianChi2Total, b.GaussianChi2Total); Bits(d, "χ²E", a.ExpGaussExpChi2Total, b.ExpGaussExpChi2Total);
        Bits(d, "χ²V", a.VoigtChi2Total, b.VoigtChi2Total); Bits(d, "χ²/ndp", a.Chi2pNdp, b.Chi2pNdp);
        return d;
    }

    static void Bits(List<string> d, string name, double x, double y)
    {
        if (BitConverter.DoubleToInt64Bits(x) != BitConverter.DoubleToInt64Bits(y))
            d.Add(name + " " + x.ToString("G17", Inv) + " → " + y.ToString("G17", Inv));
    }

    // ================================================================== C — смена формы кривой

    static void ArmTypeSwitch()
    {
        Console.WriteLine("== C. Смена формы кривой у посчитанной кривой: «Рассчитать» обязана загореться");
        Stand s = Stand.Make("p314-type", 4096, 0.75);
        var curve = new SimpleSqrtFwhmCalibration();
        foreach (int ch in new[] { 100, 400, 900, 1600 })
            curve.CalibrationPeaks.Add(P(ch, Math.Sqrt(4.0 + 0.05 * ch)));
        Control(curve.PerformCalibration(4096), "C: исходная кривая не подогналась");
        s.Rd.FwhmCalibration = curve;
        s.Doc.Dirty = false;

        s.Invoke("UpdateFwhmCalibration", true);
        ComboBox combo = (ComboBox)s.Get("selectCurveComboBox");
        Button calc = (Button)s.Get("executeCalibrationButton");
        Console.WriteLine("  до смены: форма в списке {0}, «Рассчитать» {1}, calibrationDone {2}", combo.SelectedIndex, calc.Enabled, s.Get("calibrationDone"));
        Control(combo.SelectedIndex == (int)FwhmCalibration.FwhmCalibrationCurve.SimpleSqrtFwhmCalibration && !calc.Enabled,
                "C: исходное состояние не то (форма не «простой корень» или кнопка уже горит)");

        combo.SelectedIndex = (int)FwhmCalibration.FwhmCalibrationCurve.PowerFwhmCalibration;
        object field = s.Get("fwhmCalibration");
        if (!(field is PowerFwhmCalibration))
        {
            // Без дескриптора окна событие может не прийти — зовём обработчик сами.
            s.Invoke("SelectCurveComboBox_SelectedIndexChanged", null, EventArgs.Empty);
            field = s.Get("fwhmCalibration");
        }
        Console.WriteLine("  после выбора «Степенная»: поле вида {0}, «Рассчитать» {1}", field == null ? "ПУСТО" : field.GetType().Name, calc.Enabled);
        Control(field is PowerFwhmCalibration, "C: смена формы не дошла до поля вида — замер пуст");
        if (!calc.Enabled)
        {
            Defect("C: после смены формы кривой «Рассчитать» ПОГАШЕНА — новую форму не посчитать, пока не тронешь опоры");
        }

        Exception tail = null;
        try { s.Invoke("ExecuteCalibrationButton_Click", null, EventArgs.Empty); }
        catch (Exception ex) { tail = ex; }
        FwhmCalibration doc = s.Rd.FwhmCalibration;
        Console.WriteLine("  «Рассчитать»: документ {0}{1}{2}", doc == null ? "без кривой" : doc.GetType().Name,
                          doc is PowerFwhmCalibration ? ", a = " + G(doc.Coefficients[0]) + ", p = " + G(doc.Coefficients[1]) : "",
                          tail == null ? "" : " (хвост обновления окон: " + Inner(tail) + ")");
        Control(doc is PowerFwhmCalibration && doc.Coefficients[1] > 0.3 && doc.Coefficients[1] < 0.6,
                "C: «Рассчитать» не положила степенную в документ");
        Console.WriteLine("  после «Рассчитать»: «Рассчитать» {0}, документ изменён {1}", calc.Enabled, s.Doc.Dirty);
        Control(!calc.Enabled && s.Doc.Dirty, "C: после расчёта кнопка не погасла или документ не помечен");
        s.Drop();
        Console.WriteLine();
    }

    // ================================================================== D — столбец ПШПВ

    static void ArmFwhmColumn()
    {
        Console.WriteLine("== D. Таблица опор: как печатается дробная ПШПВ");
        Stand s = Stand.Make("p314-col", 4096, 0.75);
        var curve = new SimpleSqrtFwhmCalibration();
        curve.CalibrationPeaks.Add(new CalibrationPeak { Channel = 882, Energy = 661.6574123456789, FWHM = Math.Sqrt(4.0 + 0.62 * 882) });
        curve.CalibrationPeaks.Add(new CalibrationPeak { Channel = 1776, Energy = 1332.4921234567, FWHM = Math.Sqrt(4.0 + 0.62 * 1776) });
        Control(curve.PerformCalibration(4096), "D: кривая не подогналась");
        s.Rd.FwhmCalibration = curve;
        s.Invoke("UpdateFwhmCalibration", true);
        TableModel model = (TableModel)s.Get("tableModel1");
        NumberColumn fwhmCol = (NumberColumn)s.Get("fwhmColumn");
        NumberColumn energyCol = (NumberColumn)s.Get("energyColumn");
        Control(model.Rows.Count == 2, "D: в таблице не две строки — замер пуст");
        if (model.Rows.Count != 2) { s.Drop(); return; }
        string f0 = XPTable.Renderers.NumberCellRenderer.RenderText(model.Rows[0].Cells[3].Data, fwhmCol.Format, Inv);
        string e0 = XPTable.Renderers.NumberCellRenderer.RenderText(model.Rows[0].Cells[2].Data, energyCol.Format, Inv);
        Console.WriteLine("  строка 1: энергия «{0}» (формат {1}), ПШПВ «{2}» (формат {3})", e0, energyCol.Format, f0, fwhmCol.Format);
        Control(e0 == "661.657", "D: столбец энергии печатает не «661.657» — путь печати не тот, замер пуст");
        int decimals = f0.Contains(".") ? f0.Length - f0.IndexOf('.') - 1 : 0;
        if (decimals > 3)
        {
            Defect("D: столбец ПШПВ печатает полный double («" + f0 + "», " + decimals + " знаков после точки)");
        }
        Control(f0.StartsWith("23.4"), "D: число ПШПВ в таблице не то");
        Console.WriteLine("  ПШПВ опоры полностью: " + curve.CalibrationPeaks[0].FWHM.ToString("R", Inv));

        // Подпись опоры под курсором на графике калибровки ПШПВ.
        MethodInfo label = typeof(FWHMCalibrationGraph).GetMethod("PointLabelText", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        if (label == null)
        {
            string old = curve.CalibrationPeaks[0].FWHM.ToString(Inv);
            Defect("D: подпись опоры на графике ПШПВ печатает ширину ToString() — «" + old + "» (метода PointLabelText нет, сборка ДО)");
        }
        else
        {
            string text = (string)label.Invoke(null, new object[] { curve.CalibrationPeaks[0], curve.ChannelToFwhm(882) });
            string one = text.Replace("\n", " / ");
            Console.WriteLine("  подпись опоры на графике: " + one);
            var m = System.Text.RegularExpressions.Regex.Match(text, @"([0-9]+\.[0-9]+) ch");
            int dec = m.Success ? m.Groups[1].Value.Length - m.Groups[1].Value.IndexOf('.') - 1 : -1;
            Control(m.Success, "D: в подписи графика не найдена ширина «… ch»");
            if (dec > 3) Defect("D: подпись опоры на графике печатает " + dec + " знаков ширины");
        }
        s.Drop();
        Console.WriteLine();
    }

    // ================================================================== E — «Получить все пики»

    static void ArmGetAll()
    {
        Console.WriteLine("== E. «Получить все пики» с документом другого числа каналов (замер для вопроса)");
        // Один и тот же детектор: F(E) = √(4 + 3.232·E) кэВ (7 % на 662 кэВ); шкала 3 и 0.75 кэВ/канал.
        GetAllCase("активный 1024 канала (3 кэВ/кан), чужой 4096 (0.75 кэВ/кан)", 1024, 3.0, new[] { 40, 220, 444 }, 4096, 0.75, new[] { 880, 1564 });
        GetAllCase("активный 4096 каналов (0.75 кэВ/кан), чужой 1024 (3 кэВ/кан)", 4096, 0.75, new[] { 160, 880, 1776 }, 1024, 3.0, new[] { 40, 391 });
        Console.WriteLine();
    }

    static double Fkev(double en) { return Math.Sqrt(4.0 + 3.2320 * en); }

    static void GetAllCase(string label, int na, double ga, int[] cha, int nb, double gb, int[] chb)
    {
        Console.WriteLine("  " + label);
        DocumentManager.GetInstance().DocumentList.Clear();
        Stand a = Stand.Make("p314-getall-a", na, ga);
        Stand b = Stand.Make("p314-getall-b", nb, gb);
        var ca = new SimpleSqrtFwhmCalibration();
        foreach (int ch in cha) ca.CalibrationPeaks.Add(new CalibrationPeak { Channel = ch, Energy = ch * ga, FWHM = Fkev(ch * ga) / ga });
        Control(ca.PerformCalibration(na), "E: кривая активного документа не подогналась");
        var cb = new SimpleSqrtFwhmCalibration();
        foreach (int ch in chb) cb.CalibrationPeaks.Add(new CalibrationPeak { Channel = ch, Energy = ch * gb, FWHM = Fkev(ch * gb) / gb });
        Control(cb.PerformCalibration(nb), "E: кривая чужого документа не подогналась");
        a.Rd.FwhmCalibration = ca;
        b.Rd.FwhmCalibration = cb;
        a.Invoke("UpdateFwhmCalibration", true);
        a.Invoke("GetAllPeaksButton_Click", null, EventArgs.Empty);
        List<CalibrationPeak> got = a.Rd.FwhmCalibration.CalibrationPeaks;
        Console.WriteLine("    опоры активного после сбора (канал; ПШПВ, кан.): " + string.Join(", ", got.Select(p => "(" + p.Channel.ToString(Inv) + "; " + p.FWHM.ToString("0.0#", Inv) + ")")));
        FwhmCalibration refit = a.Rd.FwhmCalibration.Clone();
        bool ok = refit.PerformCalibration(na);
        foreach (double en in new[] { 120.0, 660.0, 1332.0 })
        {
            double truth = Fkev(en);
            double was = ca.ChannelToFwhm(en / ga) * ga;
            double now = refit.ChannelToFwhm(en / ga) * ga;
            Console.WriteLine("    ПШПВ на {0} кэВ: истинная {1} кэВ, до сбора {2}, после сбора и «Рассчитать» {3} ({4})",
                              en.ToString("0", Inv), truth.ToString("0.0", Inv), was.ToString("0.0", Inv), now.ToString("0.0", Inv),
                              ((now / truth - 1) * 100).ToString("+0.0;-0.0", Inv) + " %");
        }
        Console.WriteLine("    «Рассчитать» после сбора: принята {0}, причина {1}; опор за пределом шкалы {2}: {3}",
                          ok, refit.LastCheck, na, got.Count(p => p.Channel >= na));
        // Отказ «ширина ≤ 0 внизу» подсказывает степенную — что даёт она на тех же опорах.
        var power = new PowerFwhmCalibration { CalibrationPeaks = CalibrationPeak.ClonePeaks(got) };
        bool okp = power.PerformCalibration(na);
        var sb = new StringBuilder();
        foreach (double en in new[] { 120.0, 660.0, 1332.0 })
        {
            double now = power.ChannelToFwhm(en / ga) * ga;
            sb.Append(en.ToString("0", Inv) + " кэВ " + now.ToString("0.0", Inv) + " (" + ((now / Fkev(en) - 1) * 100).ToString("+0.0;-0.0", Inv) + " %)  ");
        }
        Console.WriteLine("    степенная по подсказке на тех же опорах: принята {0} (p = {1}); {2}", okp, G(power.Coefficients[1]), sb.ToString().TrimEnd());
        Control(got.Count == cha.Length + chb.Length, "E: сбор не притянул чужие опоры — замер пуст");
        a.Drop(); b.Drop();
    }

    // ================================================================== F — предпросмотр (Фойгт)

    static void ArmPreview()
    {
        Console.WriteLine("== F. Предпросмотр формы пика, Фойгт σ_rel = γ_rel = 1: числа легенды против кривой");
        var c = new SimpleSqrtFwhmCalibration { PeakType = FwhmCalibration.VoigtPeakType, VoigtSigma = 1.0, VoigtGamma = 1.0 };
        var g = new PeakShapePreviewGraph(null);
        g.Init(c, "форма");
        var vp = (PseudoVoigtParameters)GetField(g, "voigtParameters");
        double unit = (double)GetField(g, "currentCurveSigma");
        // Нарисованная кривая в физическом x (ПШПВ = 1): x = nx·unit.
        MethodInfo sel = typeof(PeakShapePreviewGraph).GetMethod("EvaluateSelectedRelative", F);
        double atHalf = (double)sel.Invoke(g, new object[] { 0.5 / unit });
        double model = PeakShapeModel.RelativeValue(0.5, 1.0, c);
        Console.WriteLine("  кривая на x = ПШПВ/2: {0} (модель приложения {1}); профиль σ_V = {2}, γ_V = {3}, η = {4}; компоненты σ = {5}, γ = {6}",
                          atHalf.ToString("0.0000", Inv), model.ToString("0.0000", Inv), vp.ProfileSigma.ToString("0.###", Inv), vp.ProfileGamma.ToString("0.###", Inv),
                          vp.Eta.ToString("0.###", Inv), vp.GaussianSigma.ToString("0.###", Inv), vp.LorentzGamma.ToString("0.###", Inv));
        Control(Math.Abs(atHalf - 0.5) < 0.01 && Math.Abs(atHalf - model) < 1e-12, "F: нарисованная кривая не та, что у приложения");

        // Что дала бы формула легенды с ЧИСЛАМИ компонент (σ, γ) на том же x.
        double legend = LegendVoigt(0.5, vp.Eta, vp.GaussianSigma, vp.LorentzGamma);
        Console.WriteLine("  формула легенды с σ = {0}, γ = {1} на x = ПШПВ/2 даёт {2} вместо 0.5",
                          vp.GaussianSigma.ToString("0.###", Inv), vp.LorentzGamma.ToString("0.###", Inv), legend.ToString("0.0000", Inv));

        MethodInfo build = typeof(PeakShapePreviewGraph).GetMethod("BuildSelectedFormulaPieces", F);
        Array rows = (Array)build.Invoke(g, null);
        var texts = new List<string>();
        foreach (object row in rows) texts.Add(Flatten(row));
        foreach (string t in texts) Console.WriteLine("    легенда: " + t);
        string all = string.Join(" | ", texts);
        string sp = vp.ProfileSigma.ToString("0.###", Inv), gp = vp.ProfileGamma.ToString("0.###", Inv);
        bool hasProfile = Regex(all, "σ_V[^|]*= " + System.Text.RegularExpressions.Regex.Escape(sp) + "\\b")
                          && Regex(all, "γ_V[^|]*= " + System.Text.RegularExpressions.Regex.Escape(gp) + "\\b")
                          && all.Contains("G_V(x,σ_V)") && all.Contains("L(x,γ_V)");
        if (!hasProfile)
        {
            Defect("F: в легенде формула V(x) с σ = " + vp.GaussianSigma.ToString("0.###", Inv) + ", γ = " + vp.LorentzGamma.ToString("0.###", Inv)
                   + ", а кривая нарисована с σ_V = " + sp + ", γ_V = " + gp);
        }
        Control(all.Contains("η = " + vp.Eta.ToString("0.###", Inv)), "F: легенда не прочитана (нет строки η)");

        // Гаусс и EGE: оси и кривая те же, что у модели (контроль, правкой не трогаются).
        foreach (int type in new[] { FwhmCalibration.GaussianPeakType, FwhmCalibration.ExpGaussExpPeakType })
        {
            var cc = new SimpleSqrtFwhmCalibration { PeakType = type, ExpGaussExpLeftTail = 0.7, ExpGaussExpRightTail = 1.6 };
            var gg = new PeakShapePreviewGraph(null);
            gg.Init(cc, "форма");
            double u = (double)GetField(gg, "currentCurveSigma");
            double worst = 0;
            foreach (double x in new[] { -1.7, -0.6, 0.0, 0.3, 0.9, 2.2 })
            {
                double pv = (double)sel.Invoke(gg, new object[] { x / u });
                double mv = PeakShapeModel.RelativeValue(x, 1.0, cc);
                worst = Math.Max(worst, Math.Abs(pv - mv));
            }
            Console.WriteLine("  {0}: предпросмотр против модели приложения, наибольшее расхождение {1}", type == 0 ? "гаусс" : "EGE", E(worst));
            Control(worst < 1e-12, "F: предпросмотр формы " + type + " расходится с моделью приложения");
        }
        Console.WriteLine();
    }

    static double LegendVoigt(double x, double eta, double sigma, double gamma)
    {
        Func<double, double> v = t => eta * gamma / (Math.PI * (t * t + gamma * gamma))
                                      + (1 - eta) * Math.Exp(-0.5 * t * t / (sigma * sigma)) / (sigma * Math.Sqrt(2 * Math.PI));
        return v(x) / v(0);
    }

    static bool Regex(string s, string pattern) { return System.Text.RegularExpressions.Regex.IsMatch(s, pattern); }

    // Текст строки легенды: подписи — через «_» (σ_V), степени — через «^».
    static string Flatten(object piece)
    {
        if (piece == null) return "";
        Type t = piece.GetType();
        switch (t.Name)
        {
            case "TextFormulaPiece": return (string)GetField(piece, "text");
            case "RowFormulaPiece":
                var sb = new StringBuilder();
                foreach (object p in (Array)GetField(piece, "pieces")) sb.Append(Flatten(p));
                return sb.ToString();
            case "ScriptFormulaPiece":
                string sub = Flatten(GetField(piece, "subscriptPiece")), sup = Flatten(GetField(piece, "superscriptPiece"));
                return Flatten(GetField(piece, "basePiece")) + (sub.Length > 0 ? "_" + sub : "") + (sup.Length > 0 ? "^" + sup : "");
            case "FractionFormulaPiece":
                return "(" + Flatten(GetField(piece, "numerator")) + ")/(" + Flatten(GetField(piece, "denominator")) + ")";
        }
        return "?" + t.Name;
    }

    // ================================================================== G — «Сохранить в прибор»

    static void ArmSaveTail()
    {
        Console.WriteLine("== G. «Сохранить в конфиг прибора»: документ «изменён», пики перерисованы — как у «Рассчитать»");
        Type vt = typeof(DCFwhmCalibrationView);
        // Обработчик «Сохранить в прибор» ищется по СМЫСЛУ — метод вида, который пишет
        // конфигурацию прибора (`DeviceConfigManager.SaveConfig`), — а не по имени: у него
        // окно «Да/Нет», и имя в литерале пробы сторож `check_headless` законно считал бы
        // зовом отражением (проба его НЕ зовёт, только читает IL).
        var writers = vt.GetMethods(F | BindingFlags.DeclaredOnly)
                        .Where(m => Reach(m, 0).Contains("DeviceConfigManager.SaveConfig")).ToList();
        Console.WriteLine("  пишет конфигурацию прибора: " + string.Join(", ", writers.Select(m => m.Name)));
        Control(writers.Count == 1, "G: метод вида, пишущий конфигурацию прибора, не найден однозначно — разбор слеп");
        HashSet<string> save = Reach(writers.Count == 1 ? writers[0] : null, 2);
        HashSet<string> calc = Reach(vt.GetMethod("ExecuteCalibrationButton_Click", F), 2);
        string[] need = { "DocEnergySpectrum.set_Dirty", "MainForm.UpdateDetectedPeakView", "DocEnergySpectrum.UpdateEnergySpectrum" };
        foreach (string n in need)
        {
            Console.WriteLine("  {0}: «Рассчитать» {1}, «Сохранить в прибор» {2}", n, calc.Contains(n) ? "зовёт" : "НЕТ", save.Contains(n) ? "зовёт" : "НЕТ");
            Control(calc.Contains(n), "G: разбор IL не видит " + n + " даже у «Рассчитать» — разбор слеп");
            if (!save.Contains(n)) Defect("G: «Сохранить в прибор» меняет кривую документа, но не зовёт " + n);
        }
        Console.WriteLine("  calibrationDone: «Рассчитать» {0}, «Сохранить в прибор» {1}",
                          calc.Contains("DCFwhmCalibrationView.calibrationDone=") ? "ставит" : "НЕТ", save.Contains("DCFwhmCalibrationView.calibrationDone=") ? "ставит" : "НЕТ");
        Control(calc.Contains("DCFwhmCalibrationView.calibrationDone="), "G: разбор IL не видит запись calibrationDone у «Рассчитать»");
        if (!save.Contains("DCFwhmCalibrationView.calibrationDone=")) Defect("G: «Сохранить в прибор» не отмечает кривую посчитанной — «Рассчитать» остаётся гореть");

        // «Обновить опоры» графика переписывает опоры документа — документ обязан стать изменённым.
        HashSet<string> view = Reach(vt.GetMethod("ViewCalibrationButton_Click", F), 0);
        HashSet<string> remove = Reach(vt.GetMethod("RemovePeakButton_Click", F), 0);
        Console.WriteLine("  set_Dirty: «Удалить опору» {0}, «Показать график» после «Обновить опоры» {1}",
                          remove.Contains("DocEnergySpectrum.set_Dirty") ? "зовёт" : "НЕТ", view.Contains("DocEnergySpectrum.set_Dirty") ? "зовёт" : "НЕТ");
        Control(remove.Contains("DocEnergySpectrum.set_Dirty"), "G: разбор IL не видит set_Dirty у «Удалить опору» — разбор слеп");
        if (!view.Contains("DocEnergySpectrum.set_Dirty")) Defect("G: опоры документа переписаны графиком («Обновить опоры»), документ не помечен изменённым");

        // Общий хвост (после правки) — исполнением: изменённая кривая помечает документ,
        // та же самая — нет.
        MethodInfo commit = vt.GetMethod("CommitFittedCurve", F);
        if (commit == null)
        {
            Console.WriteLine("  общего хвоста CommitFittedCurve нет (сборка ДО правки)");
            Console.WriteLine();
            return;
        }
        Stand s = Stand.Make("p314-save", 4096, 0.75);
        var curve = new SimpleSqrtFwhmCalibration();
        foreach (int ch in new[] { 300, 1200 }) curve.CalibrationPeaks.Add(P(ch, Math.Sqrt(4 + 0.05 * ch)));
        Control(curve.PerformCalibration(4096), "G: кривая не подогналась");
        s.Rd.FwhmCalibration = curve;
        s.Invoke("UpdateFwhmCalibration", true);
        s.Doc.Dirty = false;
        FwhmCalibration same = curve.Clone();
        Exception ex1 = null;
        try { commit.Invoke(s.View, new object[] { s.Doc, same }); } catch (Exception ex) { ex1 = ex; }
        bool dirtySame = s.Doc.Dirty;
        Console.WriteLine("  та же кривая: документ изменён {0}{1}", dirtySame, ex1 == null ? "" : " (" + Inner(ex1) + ")");
        Control(!dirtySame && ex1 == null, "G: запись ТОЙ ЖЕ кривой пометила документ изменённым (или упала)");

        var other = new SimpleSqrtFwhmCalibration();
        foreach (int ch in new[] { 300, 1200 }) other.CalibrationPeaks.Add(P(ch, Math.Sqrt(9 + 0.06 * ch)));
        Control(other.PerformCalibration(4096), "G: вторая кривая не подогналась");
        s.Set("calibrationDone", false);
        Exception ex2 = null;
        try { commit.Invoke(s.View, new object[] { s.Doc, other }); } catch (Exception ex) { ex2 = ex; }
        Console.WriteLine("  другая кривая: документ изменён {0}, calibrationDone {1}, кривая документа c0 = {2}{3}",
                          s.Doc.Dirty, s.Get("calibrationDone"), G(s.Rd.FwhmCalibration.Coefficients[0]), ex2 == null ? "" : " (" + Inner(ex2) + ")");
        Control(s.Doc.Dirty && (bool)s.Get("calibrationDone") && Math.Abs(s.Rd.FwhmCalibration.Coefficients[0] - 9.0) < 1e-9,
                "G: запись другой кривой не довела документ до состояния «Рассчитать»");
        s.Drop();
        Console.WriteLine();
    }

    // ================================================================== снимки без экрана

    /// <summary>
    /// `--png=&lt;каталог&gt;`: предпросмотр формы пика (Фойгт) в наименьшем окне 640×480 и в
    /// 1000×640 — отрисовкой в картинку (`DrawToBitmap`), без рабочего стола: помещается ли
    /// легенда. Картинки — свидетельство на диске, в git не идут.
    /// </summary>
    static void Pictures(string dir)
    {
        Directory.CreateDirectory(dir);
        var c = new SimpleSqrtFwhmCalibration { PeakType = FwhmCalibration.VoigtPeakType, VoigtSigma = 1.0, VoigtGamma = 1.0 };
        foreach (var size in new[] { new System.Drawing.Size(640, 480), new System.Drawing.Size(1000, 640) })
        {
            var g = new PeakShapePreviewGraph(null);
            g.Init(c, "Peak shape");
            g.Size = size;
            g.CreateControl();
            using (var bmp = new System.Drawing.Bitmap(g.Width, g.Height))
            {
                g.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, g.Width, g.Height));
                string path = Path.Combine(dir, "preview_voigt_" + size.Width + "x" + size.Height + ".png");
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                Console.WriteLine("  снимок: " + path);
            }
            g.Dispose();
        }
        // ⚠ Вид ПШПВ так не снять: неприкреплённая панель DockContent рисует пустое поле
        //    (проверено 15.10.2026) — таблицу мерит плечо D рендерером столбца.
    }

    // ================================================================== H — три числа прибора

    /// <summary>
    /// Кривая ПШПВ конфигурации прибора, у которой в файле НЕТ элемента кривой: строится
    /// ли она по ЕЁ ТРЁМ ЧИСЛАМ (FWHM_AT_0, Ch_Fwhm, Width_Fwhm) или остаётся встроенной
    /// прямой конструктора [0:15, 3756:103]. Замер для вопроса (не правка): конструктор
    /// строит кривую ДО того, как сериализатор кладёт числа файла, а все заслоны «кривой
    /// нет — построить по числам» (`DeviceConfigInfo`, `CreateResultData`,
    /// `ApplyDeviceConfigChange`) срабатывают только на null.
    /// </summary>
    static void ArmDeviceNumbers()
    {
        Console.WriteLine("== H. Конфигурация прибора без элемента кривой: кривая по её числам или встроенная?");
        var xs = new XmlSerializer(typeof(FWHMPeakDetectionMethodConfig));
        Func<FWHMPeakDetectionMethodConfig, string> show = c =>
        {
            FwhmCalibration f = c.FwhmCalibration;
            if (f == null) return "кривой НЕТ";
            return f.GetType().Name + " [" + string.Join(" ", f.CalibrationPeaks.Select(p => p.Channel.ToString(Inv) + ":" + p.FWHM.ToString("0.###", Inv))) + "]"
                   + ", ПШПВ(0) = " + f.ChannelToFwhm(0).ToString("0.##", Inv) + ", ПШПВ(Ch_Fwhm) = " + f.ChannelToFwhm(c.Ch_Fwhm).ToString("0.##", Inv);
        };
        // Файл как у поставочных RC-101/ASN: три числа, элемента кривой нет.
        var src = new FWHMPeakDetectionMethodConfig { FWHM_AT_0 = 6, Ch_Fwhm = 234, Width_Fwhm = 31 };
        var sw = new StringWriter(CultureInfo.InvariantCulture);
        xs.Serialize(sw, src);
        string xml = System.Text.RegularExpressions.Regex.Replace(sw.ToString(),
            @"<(SimpleSqrt|Sqrt|Power)FwhmCalibration>.*?</\1FwhmCalibration>", "", System.Text.RegularExpressions.RegexOptions.Singleline);
        Control(!xml.Contains("FwhmCalibration>") && xml.Contains("<Width_Fwhm>31</Width_Fwhm>"), "H: файл без кривой не собран — замер пуст");
        var noCurve = (FWHMPeakDetectionMethodConfig)xs.Deserialize(new StringReader(xml));
        Console.WriteLine("  файл 6 / 234 / 31 без кривой → " + show(noCurve));
        // Положительный контроль: файл С элементом кривой читается ею (приметные опоры).
        var marked = new FWHMPeakDetectionMethodConfig { FWHM_AT_0 = 6, Ch_Fwhm = 234, Width_Fwhm = 31 };
        var mc = new SimpleSqrtFwhmCalibration();
        mc.CalibrationPeaks.Add(P(111, 5)); mc.CalibrationPeaks.Add(P(222, 9));
        mc.PerformCalibration(1024);
        marked.FwhmCalibration = mc;
        var sw2 = new StringWriter(CultureInfo.InvariantCulture);
        xs.Serialize(sw2, marked);
        var withCurve = (FWHMPeakDetectionMethodConfig)xs.Deserialize(new StringReader(sw2.ToString()));
        Console.WriteLine("  тот же файл с кривой [111:5 222:9] → " + show(withCurve));
        Control(withCurve.FwhmCalibration != null && withCurve.FwhmCalibration.CalibrationPeaks.Count == 2
                && withCurve.FwhmCalibration.CalibrationPeaks[0].Channel == 111, "H: файл с кривой прочитан не ею — замер слеп");
        double f234 = noCurve.FwhmCalibration == null ? double.NaN : noCurve.FwhmCalibration.ChannelToFwhm(234);
        if (noCurve.FwhmCalibration != null && Math.Abs(f234 - 31.0) > 0.01)
        {
            Console.WriteLine("  ⚠ НАХОДКА (вопрос, не правка): кривая конфигурации без элемента — встроенная, ПШПВ(234) = "
                              + f234.ToString("0.##", Inv) + " вместо 31 из файла, ПШПВ(0) = "
                              + noCurve.FwhmCalibration.ChannelToFwhm(0).ToString("0.##", Inv) + " вместо 6");
        }

        // Пресет RadiaCode: числа ставятся ПОСЛЕ конструктора.
        FWHMPeakDetectionMethodConfig rc103 = RadiacodePresets.RC103_FWHM_Preset();
        Console.WriteLine("  пресет RC-103 (4 / 266 / 26) → " + show(rc103));

        // Поставочные конфигурации каталога проб — как их читает приложение.
        foreach (DeviceConfigInfo d in DeviceConfigManager.GetInstance().DeviceConfigList)
        {
            var c = d.PeakDetectionMethodConfig as FWHMPeakDetectionMethodConfig;
            if (c == null) continue;
            Console.WriteLine("  прибор «{0}» ({1} кан.) числа {2} / {3} / {4} → {5}", d.Name, d.NumberOfChannels,
                              c.FWHM_AT_0.ToString(Inv), c.Ch_Fwhm.ToString(Inv), c.Width_Fwhm.ToString(Inv), show(c));
        }
        Console.WriteLine();
    }

    /// <summary>
    /// Имена вызываемых методов и записываемых полей, достижимые из метода по IL на
    /// <paramref name="depth"/> уровней вглубь (только методы своего типа).
    /// </summary>
    static HashSet<string> Reach(MethodInfo m, int depth)
    {
        var set = new HashSet<string>();
        if (m == null) return set;
        Walk(m, depth, set, new HashSet<MethodBase>());
        return set;
    }

    static void Walk(MethodBase m, int depth, HashSet<string> set, HashSet<MethodBase> seen)
    {
        if (!seen.Add(m)) return;
        MethodBody body = m.GetMethodBody();
        if (body == null) return;
        byte[] il = body.GetILAsByteArray();
        Module mod = m.Module;
        for (int i = 0; i + 4 < il.Length; i++)
        {
            byte op = il[i];
            if (op == 0x28 || op == 0x6F || op == 0x7D) // call, callvirt, stfld
            {
                int tok = BitConverter.ToInt32(il, i + 1);
                try
                {
                    if (op == 0x7D)
                    {
                        FieldInfo f = mod.ResolveField(tok);
                        set.Add(f.DeclaringType.Name + "." + f.Name + "=");
                    }
                    else
                    {
                        MethodBase callee = mod.ResolveMethod(tok);
                        set.Add(callee.DeclaringType.Name + "." + callee.Name);
                        if (depth > 0 && callee.DeclaringType == m.DeclaringType) Walk(callee, depth - 1, set, seen);
                    }
                }
                catch (Exception) { }
            }
        }
    }

    // ================================================================== стенд

    sealed class Stand
    {
        public DocEnergySpectrum Doc;
        public ResultData Rd;
        public MainForm Form;
        public DCFwhmCalibrationView View;

        public static Stand Make(string name, int channels, double kevPerChannel)
        {
            DocEnergySpectrum doc = DocumentManager.GetInstance().CreateDocument(name + ".xml");
            if (doc == null) throw new InvalidOperationException("документ " + name + " не создан");
            ResultData rd = doc.ActiveResultData;
            var es = new EnergySpectrum(1.0, channels);
            es.EnergyCalibration = new PolynomialEnergyCalibration { PolynomialOrder = 1, Coefficients = new[] { 0.0, kevPerChannel } };
            for (int i = 0; i < channels; i++)
            {
                double d = (i - channels / 4.0) / 8.0;
                es.Spectrum[i] = 20 + (int)(4000.0 * Math.Exp(-0.5 * d * d));
            }
            es.MeasurementTime = 300.0;
            es.TotalPulseCount = es.Spectrum.Sum(x => (long)x);
            rd.EnergySpectrum = es;

            MainForm form = (MainForm)FormatterServices.GetUninitializedObject(typeof(MainForm));
            SetField(form, "activeDocument", doc);
            SetField(form, "documentManager", DocumentManager.GetInstance());
            try { SetField(form, "dcPeakDetectionView", new DCPeakDetectionView(form)); } catch (Exception) { }
            return new Stand { Doc = doc, Rd = rd, Form = form, View = new DCFwhmCalibrationView(form) };
        }

        public object Get(string field) { return GetField(View, field); }
        public void Set(string field, object value) { SetField(View, field, value); }

        public object Invoke(string method, params object[] args)
        {
            MethodInfo mi = typeof(DCFwhmCalibrationView).GetMethod(method, F);
            if (mi == null) throw new MissingMethodException(method);
            try { return mi.Invoke(View, args); }
            catch (TargetInvocationException ex) { throw ex.InnerException; }
        }

        public void Drop()
        {
            DocumentManager.GetInstance().DocumentList.Remove(Doc);
        }
    }

    // ================================================================== мелочь

    static object GetField(object o, string name)
    {
        for (Type t = o.GetType(); t != null; t = t.BaseType)
        {
            FieldInfo f = t.GetField(name, F | BindingFlags.DeclaredOnly);
            if (f != null) return f.GetValue(o);
        }
        throw new MissingFieldException(o.GetType().Name, name);
    }

    static void SetField(object o, string name, object value)
    {
        for (Type t = o.GetType(); t != null; t = t.BaseType)
        {
            FieldInfo f = t.GetField(name, F | BindingFlags.DeclaredOnly);
            if (f != null) { f.SetValue(o, value); return; }
        }
        throw new MissingFieldException(o.GetType().Name, name);
    }

    static string Inner(Exception ex)
    {
        while (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
        string at = ex.StackTrace == null ? "" : ex.StackTrace.Split('\n')[0].Trim();
        return ex.GetType().Name + " " + at;
    }

    static double Rel(double x, double want) { return Math.Abs(x - want) / Math.Abs(want); }
    static string G(double x) { return x.ToString("G10", Inv); }
    static string E(double x) { return x.ToString("0.0E+0", Inv); }

    static void Line(string label, bool ok, string text)
    {
        Console.WriteLine("  {0}: принята={1}, {2}", label, ok, text);
    }

    static void Defect(string text)
    {
        defect++;
        Console.WriteLine("  ДЕФЕКТ: " + text);
    }

    static void Control(bool ok, string text)
    {
        if (ok) return;
        control++;
        Console.WriteLine("  КОНТРОЛЬ НЕ СОШЁЛСЯ: " + text);
    }
}
