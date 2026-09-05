// Полоса F44, 05.09.2026. Проба ЗАМЕРА, а не сторож: доводит безоконный
// прогон ДО тех самых мест `DeviceConfigForm.button1_Click`, где до правки
// стояли голые `MessageBox.Show`, и печатает, что оттуда вышло.
//
// ⛔ Собственного вердикта пробы тут НЕДОСТАТОЧНО. Окон нет — это утверждение
//    о ПРОЦЕССЕ, и мерит его внешний счётчик (`watch_run.ps1`, класс окна
//    `#32770`). Проба отвечает только на второй вопрос: «а дошла ли она туда
//    вообще» — иначе «окон 0» неотличимо от «до места не добрались».
//
// ⚠ Проба живёт в `handover/f44-headless/`, а НЕ в `tools/effmaker/probes/`:
//   сторож `check_headless.py` засевает достижимость именно оттуда, и новая
//   проба рядом с прочими сдвинула бы засев, то есть меняла бы измеряемое.
//
// Плечи:
//   (по умолчанию) — три уложения `button1_Click`, замер;
//   --window-control — ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ СЧЁТЧИКА: поднимает настоящее
//                      модальное окно и выходит кодом 7. Без него «окон 0»
//                      неотличимо от «счётчик слеп».
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using BecquerelMonitor;

static class HeadlessButton1ProbeF44
{
    const BindingFlags NP = BindingFlags.Instance | BindingFlags.NonPublic;

    static readonly StringBuilder Seen = new StringBuilder();
    static int failures;
    // ⚠ РАЗНЫЕ вещи, и складывать их нельзя: `failures` — «дошли и вышло не
    //   то», `notReached` — «до места не добрались вовсе». Второе замер не
    //   ломает, но и подтверждением служить не может: о нём говорится вслух.
    static int notReached;

    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        bool windowControl = false, face = false;
        foreach (string a in args)
        {
            if (a == "--window-control") windowControl = true;
            else if (a == "--face") face = true;
        }

        if (windowControl)
        {
            return WindowControl();
        }
        if (face)
        {
            return Face();
        }

        Say("== F44: три места `DeviceConfigForm.button1_Click` на БЕЗОКОННОМ пути ==");
        Say("");
        Assembly app = typeof(GlobalConfigManager).Assembly;
        Say("сборка приложения: " + app.Location);
        Say("собрана:           " + File.GetLastWriteTime(app.Location)
                                        .ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture));
        Say("AppUi.HasWindows:  " + AppUi.HasWindows
            + (AppUi.HasWindows ? "  ⛔ ЭТО ПРИЛОЖЕНИЕ, А НЕ ПРОБА: замер не о том" : "  (проба — так и надо)"));
        if (AppUi.HasWindows) failures++;

        // ⛔ Обе карты примитивов ROI — ДО менеджеров-одиночек (`T60`).
        ROIPrimitiveDefinition.InitializeROIPrimitiveDefinitions();
        ROIPrimitiveOperation.InitializeROIPrimitiveOperations();
        // ⛔ Реестры приборов — ДО первого `DeviceConfigForm`: в приложении их
        //    заводит `MainForm`, а окна здесь нет (грабля `CultureProbeO14`).
        DeviceType.InitializeDeviceTypes();
        ThermometerType.InitializeThermometerTypes();

        Say("");
        // Место 1: `Solve` отказал → `ERRInvalidChannelOrEnergyValues`.
        // ⚠ Какое именно уложение валит решатель, ЗАРАНЕЕ НЕ ИЗВЕСТНО:
        //   MathNet на вырожденной матрице отдаёт NaN, а не отказ, и часть
        //   уложений вылетает МИМО `try` (пустые коэффициенты роняют
        //   `CheckCalibration`). Поэтому перебор, а не догадка: место
        //   считается достигнутым, если хоть одно уложение туда довело.
        Say("── МЕСТО 1 (:1944, ERRInvalidChannelOrEnergyValues) ──");
        string want1 = Res("ERRInvalidChannelOrEnergyValues");
        var cand = new List<KeyValuePair<string, List<CalibrationPoint>>>
        {
            Cand("две точки одного канала — матрица вырождена", 0, 100m, 0, 200m),
            Cand("две точки канала 0 — столбец канала нулевой", 0, 100m, 0, 100m),
        };
        cand.Add(new KeyValuePair<string, List<CalibrationPoint>>(
            "точек ноль — порядок −1", new List<CalibrationPoint>()));
        cand.Add(new KeyValuePair<string, List<CalibrationPoint>>(
            "пять точек одного канала — матрица 5×5 вырождена",
            new List<CalibrationPoint> {
                new CalibrationPoint(10, 100m, 0), new CalibrationPoint(10, 200m, 0),
                new CalibrationPoint(10, 300m, 0), new CalibrationPoint(10, 400m, 0),
                new CalibrationPoint(10, 500m, 0) }));
        cand.Add(new KeyValuePair<string, List<CalibrationPoint>>(
            "шесть точек — матрица 6×5 не квадратна",
            new List<CalibrationPoint> {
                new CalibrationPoint(1, 10m, 0), new CalibrationPoint(2, 20m, 0),
                new CalibrationPoint(3, 30m, 0), new CalibrationPoint(4, 40m, 0),
                new CalibrationPoint(5, 50m, 0), new CalibrationPoint(6, 60m, 0) }));
        cand.Add(new KeyValuePair<string, List<CalibrationPoint>>(
            "канал 10^160 — степень уходит в бесконечность",
            new List<CalibrationPoint> {
                new CalibrationPoint(int.MaxValue, 1m, 0),
                new CalibrationPoint(int.MaxValue - 1, 2m, 0),
                new CalibrationPoint(int.MaxValue - 2, 3m, 0),
                new CalibrationPoint(int.MaxValue - 3, 4m, 0),
                new CalibrationPoint(int.MaxValue - 4, 5m, 0) }));

        bool hit1 = false;
        foreach (var c in cand)
        {
            if (Shot("  уложение: " + c.Key, c.Value, want1)) { hit1 = true; break; }
        }
        if (hit1)
        {
            Say("  ИТОГ МЕСТА 1: достигнуто, вернулось СТРОКОЙ, а не окном.");
        }
        else
        {
            notReached++;
            Say("  ⚠ ИТОГ МЕСТА 1: НЕ ДОСТИГНУТО ни одним из " + cand.Count + " уложений.");
            Say("    Ветка `catch` включается ровно двумя способами: `CalibrationSolver.Solve`");
            Say("    бросает, либо возвращает `null`. Ниже — прямой замер обоих условий.");
            SolverNever(cand);
        }
        Say("");

        // Место 2: решение есть, но наклон нулевой → `CheckCalibration` = false.
        Say("── МЕСТО 2 (:1954, CalibrationFunctionError) ──");
        bool hit2 = Shot("  уложение: две точки одной энергии — наклон 0",
             new List<CalibrationPoint> {
                 new CalibrationPoint(0, 100m, 0),
                 new CalibrationPoint(100, 100m, 0)
             },
             Res("CalibrationFunctionError"));
        Say(hit2
            ? "  ИТОГ МЕСТА 2: достигнуто, вернулось СТРОКОЙ, а не окном."
            : "  ⛔ ИТОГ МЕСТА 2: до места не дошли — замер НЕ СОСТОЯЛСЯ.");
        if (!hit2) failures++;

        // Место 3: разбор, почему уложения нет, — в отчёте и журнале.
        Say("");
        notReached++;
        Say("МЕСТО 3 (:1978, CalibrationFunctionError) — уложения НЕТ, и это не пропуск:");
        Say("  вторая `CheckCalibration()` зовётся на ТОМ ЖЕ объекте с теми же");
        Say("  коэффициентами и тем же числом каналов (8192 по умолчанию) — между");
        Say("  проверками правятся только `Text` у пяти `numericUpDown`, а обратно");
        Say("  в `energyCalibration` они не читаются. Значит вторая проверка");
        Say("  повторяет ответ первой, и после её `true` до :1978 дойти нельзя.");
        Say("  Правка там сделана всё равно: сторож считает вызовы РАЗБОРОМ ТЕКСТА,");
        Say("  и одно оставленное голое окно держало бы отказ на всём файле.");

        Say("");
        NonFinite();

        Say("");
        Say("── что вышло в поток ошибок за прогон ──");
        Say(Seen.Length == 0 ? "  (ничего)" : Seen.ToString().TrimEnd());

        Say("");
        Say("РАСХОЖДЕНИЙ: " + failures + "   МЕСТ, ДО КОТОРЫХ НЕ ДОБРАЛИСЬ: " + notReached + " из 3");
        Say(failures == 0
            ? "Ни одного окна за прогон изнутри пробы не поднялось; счёт окон ВНЕШНИЙ — у `watch_run.ps1`."
            : "⛔ замер разошёлся, см. выше.");
        Console.Out.Flush();
        return failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// Одно уложение: набить `calibrationPoints`, позвать настоящий
    /// `button1_Click` отражением и посмотреть, что дверь напечатала.
    /// </summary>
    static bool Shot(string name, List<CalibrationPoint> points, string want)
    {
        Say(name);
        DeviceConfigForm form = null;
        string got = null;
        try
        {
            form = new DeviceConfigForm();
            FieldInfo fi = typeof(DeviceConfigForm).GetField("calibrationPoints", NP);
            if (fi == null) throw new InvalidOperationException("поле `calibrationPoints` не найдено: сборка чужая");
            fi.SetValue(form, points);

            MethodInfo mi = typeof(DeviceConfigForm).GetMethod("button1_Click", NP);
            if (mi == null) throw new InvalidOperationException("`button1_Click` не найден: сборка чужая");

            got = Capture(delegate { mi.Invoke(form, new object[] { null, EventArgs.Empty }); });
        }
        catch (Exception ex)
        {
            Say("    вылетело МИМО места: " + ex.GetType().Name + ": " + Innermost(ex));
            return false;
        }
        finally
        {
            if (form != null) { try { form.Dispose(); } catch (Exception) { } }
        }

        Say("    вернулся управлением (окна не было): ДА");
        Say("    напечатано в поток ошибок: " + (got.Length == 0 ? "(пусто)" : "«" + got.Trim() + "»"));
        bool ok = got.IndexOf(want, StringComparison.Ordinal) >= 0;
        Say("    ожидался текст ресурса «" + want + "»: " + (ok ? "СОШЁЛСЯ" : "не он"));
        return ok;
    }

    /// <summary>
    /// Прямой замер обоих условий ветки `catch` на :1944: решатель зовётся с
    /// теми же уложениями, что и через обработчик, и печатается, БРОСИЛ ли он
    /// и вернул ли `null`. Без этого «место не достигнуто» — догадка.
    /// </summary>
    static void SolverNever(List<KeyValuePair<string, List<CalibrationPoint>>> cand)
    {
        int threw = 0, nulls = 0;
        foreach (var c in cand)
        {
            List<CalibrationPoint> pts = c.Value;
            int order = pts.Count >= 5 ? 4 : pts.Count - 1;
            string verdict;
            try
            {
                double[] m = BecquerelMonitor.Utils.CalibrationSolver.Solve(pts, order);
                if (m == null) { nulls++; verdict = "вернул null"; }
                else
                {
                    verdict = "вернул " + m.Length + " коэф.: ["
                        + string.Join(", ", Array.ConvertAll(m, v => v.ToString("R", CultureInfo.InvariantCulture)))
                        + "]";
                }
            }
            catch (Exception ex) { threw++; verdict = "БРОСИЛ " + ex.GetType().Name; }
            Say("      Solve(" + pts.Count + " точек, порядок " + order + "): " + verdict);
        }
        Say("      итого по " + cand.Count + " уложениям: бросил " + threw + ", вернул null " + nulls);
        Say("      значит ветка :1944 этими входами не включается ВООБЩЕ.");
    }

    static List<CalibrationPoint> Cand(int c1, decimal e1, int c2, decimal e2)
    {
        return new List<CalibrationPoint> {
            new CalibrationPoint(c1, e1, 0), new CalibrationPoint(c2, e2, 0) };
    }

    static KeyValuePair<string, List<CalibrationPoint>> Cand(string how, int c1, decimal e1, int c2, decimal e2)
    {
        return new KeyValuePair<string, List<CalibrationPoint>>(how, Cand(c1, e1, c2, e2));
    }

    /// <summary>
    /// Перехват потока ошибок НА ВРЕМЯ вызова, с тройником: настоящий поток
    /// продолжает получать всё, иначе внешний журнал прогона осиротеет.
    /// </summary>
    static string Capture(Action run)
    {
        TextWriter old = Console.Error;
        StringWriter box = new StringWriter();
        Console.SetError(new Tee(old, box));
        try { run(); }
        finally { Console.SetError(old); }
        string s = box.ToString();
        Seen.Append(s);
        return s;
    }

    sealed class Tee : TextWriter
    {
        readonly TextWriter a, b;
        public Tee(TextWriter a, TextWriter b) { this.a = a; this.b = b; }
        public override Encoding Encoding { get { return a.Encoding; } }
        public override void Write(char c) { a.Write(c); b.Write(c); }
        public override void Write(string s) { a.Write(s); b.Write(s); }
        public override void WriteLine(string s) { a.WriteLine(s); b.WriteLine(s); }
        public override void Flush() { a.Flush(); b.Flush(); }
    }

    /// <summary>
    /// ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ ВНЕШНЕГО СЧЁТЧИКА. Без него «окон 0» значит
    /// ровно столько же, сколько молчание слепого. Окно поднимается по-
    /// настоящему, счётчик обязан его назвать; закрывать его некому, поэтому
    /// процесс уходит сам через четыре секунды кодом 7.
    /// </summary>
    static int WindowControl()
    {
        Say("== F44: положительный контроль внешнего счётчика окон ==");
        Say("поднимаю НАСТОЯЩЕЕ модальное окно; счётчик обязан его увидеть.");
        Console.Out.Flush();
        Thread t = new Thread(delegate ()
        {
            MessageBox.Show("Контроль счётчика окон, полоса F44", "F44",
                            MessageBoxButtons.OK, MessageBoxIcon.None);
        });
        t.IsBackground = true;
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        Thread.Sleep(4000);
        Say("четыре секунды прошли, ухожу кодом 7 (окно закрыть некому).");
        Console.Out.Flush();
        Environment.Exit(7);
        return 7;
    }

    /// <summary>
    /// ПОПУТНАЯ НАХОДКА, замеряется прямо: `CheckCalibration` пропускает
    /// коэффициенты, которые не являются числами. Из-за этого уложения
    /// «две точки одного канала» проходят ОБЕ проверки обработчика.
    /// </summary>
    static void NonFinite()
    {
        Say("── попутно: что `CheckCalibration` считает годной шкалой ──");
        Check("порядок 1, [NaN, NaN]", 1, double.NaN, double.NaN);
        Check("порядок 1, [NaN, +∞]", 1, double.NaN, double.PositiveInfinity);
        Check("порядок 1, [0, +∞]", 1, 0.0, double.PositiveInfinity);
        Check("порядок 1, [0, 1] — здоровая, для сравнения", 1, 0.0, 1.0);
        Check("порядок 1, [0, 0] — наклон 0, отвергается", 1, 0.0, 0.0);
    }

    static void Check(string how, int order, params double[] coef)
    {
        var cal = new PolynomialEnergyCalibration();
        cal.Coefficients = coef;
        cal.PolynomialOrder = order;
        string v;
        try { v = cal.CheckCalibration() ? "ГОДНА" : "отвергнута"; }
        catch (Exception ex) { v = "БРОСИЛА " + ex.GetType().Name; }
        Say("    " + how.PadRight(44) + " → " + v);
    }

    /// <summary>
    /// ВИД СООБЩЕНИЯ: два окна разом — «как было» и «как стало» — чтобы их
    /// сличил ВНЕШНИЙ перечислитель (подпись окна, кнопки, знак, текст).
    ///
    /// ⛔ «Как стало» — НЕ подражание, а САМА дверь: у `AppUi` через отражение
    ///    поднимается признак окон, и `Report` идёт своей оконной веткой.
    ///    Живых окон в процессе нет, `UiHost()` вернёт null, и метод покажет
    ///    окно ровно тем вызовом, каким показывает его в приложении, когда
    ///    поток окон ещё не заведён.
    ///
    /// ⚠ Хозяин окна (owner) на ЭТОМ замере одинаков у обоих — null. Он и не
    ///   мерится: хозяин двигает окно и делает его модальным приложению
    ///   (решение `A241`), а подписи, кнопки и знака не касается.
    /// </summary>
    static int Face()
    {
        Say("== F44: сличение ВИДА сообщения — «как было» против «как стало» ==");
        string text = Res("CalibrationFunctionError");
        Say("текст: " + text);

        Thread a = new Thread(delegate ()
        {
            // КАК БЫЛО: ровно тот вызов, что стоял на :1944/:1954/:1978.
            MessageBox.Show(text);
        });
        a.IsBackground = true; a.SetApartmentState(ApartmentState.STA); a.Start();
        Thread.Sleep(700);

        FieldInfo hw = typeof(AppUi).GetField("hasWindows", BindingFlags.Static | BindingFlags.NonPublic);
        if (hw == null) { Say("⛔ поле `hasWindows` не найдено: сборка чужая"); return 3; }
        hw.SetValue(null, true);
        Say("AppUi.HasWindows поднят отражением: " + AppUi.HasWindows);

        Thread b = new Thread(delegate ()
        {
            // КАК СТАЛО: сама дверь, её оконная ветка.
            AppUi.Report(text, "", MessageBoxIcon.None);
        });
        b.IsBackground = true; b.SetApartmentState(ApartmentState.STA); b.Start();

        Thread.Sleep(4000);
        Say("оба окна подняты; ухожу кодом 7 — закрыть их некому.");
        Console.Out.Flush();
        Environment.Exit(7);
        return 7;
    }

    static string Res(string key)
    {
        PropertyInfo pi = typeof(BecquerelMonitor.Properties.Resources)
            .GetProperty(key, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        if (pi == null) throw new InvalidOperationException("ресурс " + key + " не найден");
        return (string)pi.GetValue(null, null);
    }

    static string Innermost(Exception ex)
    {
        while (ex.InnerException != null) ex = ex.InnerException;
        return ex.Message;
    }

    static void Say(string s) { Console.Out.WriteLine(s); }
}
