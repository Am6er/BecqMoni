// `A244`, ДОЛЯ П4 «ГРАФИК»: ЧИСЛА БОЛЬШОГО ГРАФИКА СПЕКТРА.
//
//     graphcultureprobef27 [--out=<файл>] [--modal-control]
//
// Что мерится. `EnergySpectrumView` ничего не печатает в поле ввода — он
// РИСУЕТ, `Graphics.DrawString`. Поэтому «текст на экране» здесь берётся не у
// свойства `Text` какого-нибудь поля, а у самой отрисовки: вид рисует в
// МЕТАФАЙЛ EMF+, а проба вынимает из его записей `DrawString` ровно те строки,
// которые ушли бы на экран. Ни одной своей копии форматирования у пробы нет —
// сравнивается то, что нарисовал живой метод приложения.
//
// ⛔ ОКНО НЕ ПОДНИМАЕТСЯ. `EnergySpectrumView` — это `UserControl`, он
//    создаётся обычным конструктором и никуда не показывается; поля вьюпорта
//    выставляются отражением тем же приёмом, что в соседних пробах
//    (`FsaSelectionProbeF16.Setup`). Сторож модальных окон стоит первым делом
//    (разряд `A245`, полоса F20): голое `MessageBox.Show` на безоконном пути
//    вешает прогон насмерть, и без сторожа исход зависел бы от того, закроет
//    ли окно кто-то снаружи.
//
// ⛔ КОСТЫЛЯ `MainForm.cs:158-160` ЗДЕСЬ НЕТ И БЫТЬ НЕ МОЖЕТ. Клон культуры с
//    подменённым разделителем ставит `MainForm` в своём конструкторе, а проба
//    его не зовёт вовсе — то есть замер идёт БЕЗ подпорки, и точка на экране,
//    если она есть, поставлена правкой доли П4, а не костылём. Это не
//    заявление: каждое плечо печатает `CurrentCulture.NumberDecimalSeparator`
//    и голое `(1.5).ToString()`, и на `ru-RU`/`de-DE` они обязаны показать
//    ЗАПЯТУЮ. Если показали точку — костыль дотянулся, и плечо не мерит.
//
// ⛔ СУДИТСЯ ВИД СТРОКИ, А НЕ РАВЕНСТВО ПЛЕЧ. Это оплачено долей П7: она
//    прошла приёмку с непереведённой группировкой разрядов именно потому, что
//    приёмка сравнивала плечи между собой, а запятая в группах («1,234.50»)
//    одинакова на всех культурах и плечи не разводит. Поэтому рядом с
//    посимвольным сравнением стоит отдельный разбор ВИДА: в нарисованном
//    числе не бывает ни запятой между цифрами, ни разделителя разрядов.
//
// ⚠ Числа сцены взяты БОЛЬШЕ ТЫСЯЧИ и с дробной частью нарочно: только на
//   таком числе видны обе беды разом — «1 234,50» (культура) и «1,234.50»
//   (формат `n…`, который несёт разделитель разрядов и на инварианте).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using BecquerelMonitor;
using BecquerelMonitor.Utils;

// Целевая платформа процесса (.NETFramework 4.8) объявлена ОБЩИМ довеском
// `_TargetFramework.cs` — он компилируется в каждую пробу (`T237`, 06.09.2026);
// свой атрибут здесь дал бы CS0579. Значение печатается в шапке.

static class GraphCultureProbeF27
{
    static readonly StringBuilder Log = new StringBuilder();
    static int failures;

    const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static
                             | BindingFlags.Public | BindingFlags.NonPublic;

    static readonly string[] Foreign = { "ru-RU", "de-DE", "en-US" };

    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        string outPath = null;
        bool modalControl = false;
        foreach (string a in args)
        {
            if (a.StartsWith("--out=", StringComparison.Ordinal)) outPath = a.Substring(6);
            else if (a == "--modal-control") modalControl = true;
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

        Say("== `A244` П4 «ГРАФИК»: ЧИСЛА, КОТОРЫЕ РИСУЕТ EnergySpectrumView ==");
        Say("");

        Assembly app = typeof(EnergySpectrumView).Assembly;
        Say("сборка приложения: " + app.Location);
        try
        {
            Say("собрана:           " + File.GetLastWriteTime(app.Location)
                                          .ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture));
        }
        catch (Exception ex) { Say("собрана:           не прочитана: " + ex.Message); }
        Say("процесс пробы:     целевая платформа входной сборки="
            + (AppDomain.CurrentDomain.SetupInformation.TargetFrameworkName ?? "НЕ ОБЪЯВЛЕНА"));
        Say("культура ОС:       " + CultureInfo.InstalledUICulture.Name);

        // Сцена, «плоскость» и «объём» — см. комментарий у Shot.
        try
        {
            MetafileSelfTest();
            A244P4();
        }
        catch (Exception ex)
        {
            Say("");
            Say("⛔ ЗАМЕР ОБОРВАЛСЯ: " + ex.GetType().Name + ": " + ex.Message);
            Say(ex.StackTrace);
            failures++;
        }

        Say("");
        Say("модальных окон за прогон: " + modalSeen);
        Say(failures == 0 ? "СОШЛОСЬ, расхождений нет" : "НЕ СОШЛОСЬ: " + failures);
        ModalWatchStop();
        Finish(outPath);
        return failures == 0 ? 0 : 1;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  СНЯТИЕ ТЕКСТА С ОТРИСОВКИ
    //
    //  `Graphics` запечатать нельзя (класс запечатан), подменить `DrawString`
    //  нечем. Зато `Graphics.FromImage(Metafile)` ЗАПИСЫВАЕТ вызовы, а
    //  `EnumerateMetafile` их проигрывает по одной записи, отдавая сырые
    //  данные. У записи EmfPlusDrawString данные лежат так:
    //  BrushId(4) FormatID(4) Length(4) LayoutRect(16) String(Length×2, UTF-16).
    //  Отсюда смещение 28.
    //
    //  ⚠ Свой положительный контроль у снятия обязателен: «строк не найдено»
    //    неотличимо от «снятие не работает».
    // ══════════════════════════════════════════════════════════════════════

    static List<string> drawn;

    static void MetafileSelfTest()
    {
        Say("");
        Say("[полож. контроль СНЯТИЯ] метафайл отдаёт обратно то, что нарисовано:");
        List<string> got = Capture(delegate(Graphics g)
        {
            using (Font f = new Font("Segoe UI", 9f))
            {
                g.DrawString("1,234.50", f, Brushes.Black, new RectangleF(0, 0, 200, 20));
                StringFormat sf = new StringFormat();
                sf.Alignment = StringAlignment.Far;
                g.DrawString("хвост 1234.50", f, Brushes.Red, new RectangleF(0, 20, 200, 20), sf);
                g.DrawString("простая", f, Brushes.Blue, 3f, 40f);
            }
        });
        bool ok = got.Count == 3 && got[0] == "1,234.50" && got[1] == "хвост 1234.50" && got[2] == "простая";
        Say("  снято " + got.Count + " строк: «" + string.Join("» | «", got.ToArray()) + "»"
            + (ok ? " — снятие работает" : "  ⛔ СНЯТИЕ НЕ РАБОТАЕТ, весь замер недействителен"));
        if (!ok) failures++;
    }

    static List<string> Capture(Action<Graphics> draw)
    {
        List<string> texts = new List<string>();
        using (Bitmap host = new Bitmap(8, 8))
        using (Graphics rg = Graphics.FromImage(host))
        {
            IntPtr hdc = rg.GetHdc();
            Metafile mf;
            try
            {
                mf = new Metafile(hdc, new RectangleF(0, 0, 1200, 900),
                                  MetafileFrameUnit.Pixel, EmfType.EmfPlusOnly);
            }
            finally { rg.ReleaseHdc(hdc); }

            try
            {
                using (Graphics g = Graphics.FromImage(mf))
                {
                    draw(g);
                }

                drawn = texts;
                using (Bitmap play = new Bitmap(4, 4))
                using (Graphics pg = Graphics.FromImage(play))
                {
                    pg.EnumerateMetafile(mf, new Point(0, 0), OnRecord);
                }
            }
            finally
            {
                drawn = null;
                mf.Dispose();
            }
        }

        return texts;
    }

    static bool OnRecord(EmfPlusRecordType recordType, int flags, int dataSize,
                         IntPtr data, PlayRecordCallback cb)
    {
        if (recordType == EmfPlusRecordType.DrawString && data != IntPtr.Zero && dataSize >= 28
            && drawn != null)
        {
            byte[] buf = new byte[dataSize];
            Marshal.Copy(data, buf, 0, dataSize);
            int len = BitConverter.ToInt32(buf, 8);
            if (len >= 0 && 28 + len * 2 <= dataSize)
            {
                drawn.Add(Encoding.Unicode.GetString(buf, 28, len * 2));
            }
            else
            {
                drawn.Add("(ЗАПИСЬ НЕ РАЗОБРАНА len=" + len + " size=" + dataSize + ")");
            }
        }

        return true;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  СЦЕНА
    // ══════════════════════════════════════════════════════════════════════

    sealed class Shot
    {
        public string Bare;          // (1.5).ToString() без культуры
        public string Sep;           // разделитель дроби культуры плеча
        public string[] Panel;       // панель под курсором, ветка «обнаружено»
        public string[] PanelLow;    // она же, ветка «ниже порога» (верхние пределы)
        public string[] PanelNoErr;  // она же, когда ошибок нет (ветки без «±»)
        public string[] PanelNoBg;   // она же без фона (короткая строка имп/с)
        public string[] Axis;        // подписи горизонтальной шкалы
        public string[] Pow10;       // подписи вертикальной шкалы (степень десяти)
        public string[] Refusals;    // отказы, которые панель показывает вместо числа
        public string Stopwatch;     // секундомер отрисовки
        public string Err;
    }

    // Числа сцены. Каждое ловит свою беду: больше тысячи — разделитель
    // разрядов, с дробной частью — разделитель дроби, отрицательное — знак,
    // мельче тысячной — короткая форма.
    const int SceneChannels = 4096;
    const int SceneCursor = 1234;

    static Shot Run()
    {
        Shot s = new Shot();
        try
        {
            s.Bare = (1.5).ToString();
            s.Sep = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;

            // ── Отказы считаются ПЕРВЫМИ: строка отказа не только сравнивается
            //    сама по себе, она ещё и УХОДИТ В ПАНЕЛЬ — там её рисуют вместо
            //    числа активности, и мерить её надо ровно так, как её увидят.
            MethodInfo refusal = typeof(EnergySpectrumView).GetMethod("ActivityCurveRefusal", Any);
            if (refusal == null)
            {
                s.Err = "в сборке нет EnergySpectrumView.ActivityCurveRefusal";
                return s;
            }

            BecquerelCoefficient.LineResult outOfRange = new BecquerelCoefficient.LineResult();
            outOfRange.Problem = BecquerelCoefficient.LineProblem.OutOfRange;
            outOfRange.CurveMin = 1234.5;
            outOfRange.CurveMax = 2614.55;
            BecquerelCoefficient.LineResult noEps = new BecquerelCoefficient.LineResult();
            noEps.Problem = BecquerelCoefficient.LineProblem.NoEpsilon;

            s.Refusals = new string[]
            {
                (string)refusal.Invoke(null, new object[] { 1234.5678, outOfRange }),
                (string)refusal.Invoke(null, new object[] { 1234.5678, noEps }),
                // ⚠ Третий слот НАРОЧНО печатается культурой потока: он —
                //   положительный контроль плеча и обязан РАЗОЙТИСЬ. Без него
                //   «все отказы совпали» неотличимо от «плечо не мерит».
                string.Format(CultureInfo.CurrentCulture, "(контроль плеча) {0:0.##}", 1234.5678),
            };

            using (Font font = new Font("Segoe UI", 9f))
            using (EnergySpectrumView view = new EnergySpectrumView())
            {
                view.Font = font;
                Setup(view, font);

                MethodInfo showCursor = typeof(EnergySpectrumView).GetMethod("ShowCursorValues", Any);
                MethodInfo showHoriz = typeof(EnergySpectrumView).GetMethod("ShowHorizontalAxis", Any);
                MethodInfo showWatch = typeof(EnergySpectrumView).GetMethod("ShowStopwatch", Any);
                if (showCursor == null || showHoriz == null || showWatch == null)
                {
                    s.Err = "в сборке нет ShowCursorValues/ShowHorizontalAxis/ShowStopwatch";
                    return s;
                }

                // ⚠ Панель снимается ЧЕТЫРЬМЯ сценами, и это не избыточность:
                //   у неё четыре ветки, и в каждой свои места печати. Одна
                //   сцена оставила бы шесть мест из тридцати девяти неснятыми
                //   — то есть «совпало» стояло бы там, где не мерялось ничего.
                //
                // ── 1. «обнаружено»: активность числом ± ошибка
                Set(view, "selectionAnalytics", Analytics(true, null, false, false));
                Set(view, "selectionAnalyticsDirty", false);
                s.Panel = Capture(delegate(Graphics g) { showCursor.Invoke(view, new object[] { g }); }).ToArray();

                // ── 2. «ниже порога»: верхние пределы, строка Lu и ОТКАЗ
                //      вместо числа — тот самый, что посчитан выше.
                Set(view, "selectionAnalytics", Analytics(false, s.Refusals[0], false, false));
                Set(view, "selectionAnalyticsDirty", false);
                s.PanelLow = Capture(delegate(Graphics g) { showCursor.Invoke(view, new object[] { g }); }).ToArray();

                // ── 3. ошибок нет: имп/с и импульсы печатаются БЕЗ «±», и это
                //      другие две строки кода, чем с «±».
                Set(view, "selectionAnalytics", Analytics(true, null, true, false));
                Set(view, "selectionAnalyticsDirty", false);
                s.PanelNoErr = Capture(delegate(Graphics g) { showCursor.Invoke(view, new object[] { g }); }).ToArray();

                // ── 4. фона нет: вся ветка «соотн. фона / имп/с над фоном»
                //      заменяется одной короткой строкой «имп/с».
                Set(view, "selectionAnalytics", Analytics(true, null, true, true));
                Set(view, "selectionAnalyticsDirty", false);
                s.PanelNoBg = Capture(delegate(Graphics g) { showCursor.Invoke(view, new object[] { g }); }).ToArray();

                // ── подписи горизонтальной шкалы: и энергия («f0»), и каналы
                List<string> axis = new List<string>();
                Set(view, "horizontalUnit", HorizontalUnit.Energy);
                axis.AddRange(Capture(delegate(Graphics g) { showHoriz.Invoke(view, new object[] { g }); }));
                Set(view, "horizontalUnit", HorizontalUnit.Channel);
                axis.AddRange(Capture(delegate(Graphics g) { showHoriz.Invoke(view, new object[] { g }); }));
                Set(view, "horizontalUnit", HorizontalUnit.Energy);
                s.Axis = axis.ToArray();

                // ── секундомер отрисовки
                Set(view, "measureDrawingTime", true);
                object watch = Field("stopwatch").GetValue(view);
                watch.GetType().GetMethod("Reset").Invoke(watch, null);
                s.Stopwatch = string.Join(" | ",
                    Capture(delegate(Graphics g) { showWatch.Invoke(view, new object[] { g }); }).ToArray());
                Set(view, "measureDrawingTime", false);

                // ── подписи вертикальной шкалы: степень десяти. Метод шкалы
                //    зовёт ровно эти два и ничего своего не печатает.
                MethodInfo powD = typeof(EnergySpectrumView).GetMethod(
                    "FormatAs10Power", Any, null, new Type[] { typeof(double) }, null);
                MethodInfo powM = typeof(EnergySpectrumView).GetMethod(
                    "FormatAs10Power", Any, null, new Type[] { typeof(decimal) }, null);
                if (powD == null || powM == null)
                {
                    s.Err = "в сборке нет EnergySpectrumView.FormatAs10Power";
                    return s;
                }

                List<string> pow = new List<string>();
                foreach (double v in new double[] { 0.5, 12.75, 998.5, 1234.5, 123456.75, 0.001234 })
                {
                    pow.Add((string)powD.Invoke(view, new object[] { v }));
                }
                foreach (decimal v in new decimal[] { 12.75m, 998.5m, 1234.5m, 123456.75m })
                {
                    pow.Add((string)powM.Invoke(view, new object[] { v }));
                }
                s.Pow10 = pow.ToArray();
            }
        }
        catch (Exception ex)
        {
            s.Err = ex.GetType().Name + ": " + (ex.InnerException != null ? ex.InnerException.Message : ex.Message);
        }

        return s;
    }

    /// <summary>
    /// Поля вьюпорта. Вид собран без формы, поэтому шкалу, размеры и курсор
    /// надо выставить руками — тем же приёмом, что в `FsaSelectionProbeF16`.
    /// </summary>
    static void Setup(EnergySpectrumView view, Font font)
    {
        PolynomialEnergyCalibration cal = new PolynomialEnergyCalibration();
        cal.PolynomialOrder = 1;
        cal.Coefficients = new double[] { 0.5, 1.25 };

        EnergySpectrum spectrum = new EnergySpectrum(1.0, SceneChannels);
        spectrum.EnergyCalibration = cal;
        spectrum.MeasurementTime = 3600.5;
        spectrum.Spectrum[SceneCursor] = 123456;

        EnergySpectrum background = new EnergySpectrum(1.0, SceneChannels);
        background.EnergyCalibration = cal;
        background.MeasurementTime = 1800.25;
        background.Spectrum[SceneCursor] = 4321;

        ResultData rd = new ResultData();
        rd.EnergySpectrum = spectrum;
        rd.BackgroundEnergySpectrum = background;

        Set(view, "activeResultData", rd);
        Set(view, "energySpectrum", spectrum);
        Set(view, "backgroundEnergySpectrum", background);
        Set(view, "energyCalibration", cal);
        Set(view, "baseEnergyCalibration", cal);
        Set(view, "backgroundEnergyCalibration", cal);
        Set(view, "backgroundNumberOfChannels", SceneChannels);
        Set(view, "numberOfChannels", SceneChannels);
        Set(view, "totalMaxChannel", SceneChannels);
        Set(view, "backgroundMode", BackgroundMode.Invisible);
        Set(view, "horizontalUnit", HorizontalUnit.Energy);

        Set(view, "left", 1);
        Set(view, "width", 900);
        Set(view, "height", 600);
        Set(view, "bottom", 16);
        Set(view, "scrollX", 0);
        Set(view, "scrollY", 0);
        Set(view, "horizontalScale", 1.0);
        Set(view, "verticalScale", 1.0);
        Set(view, "pixelPerEnergy", 0.4);
        Set(view, "energyViewOffset", 0.0);

        // Курсор: панель канала рисуется только при живом курсоре.
        Set(view, "validCursor", true);
        Set(view, "cursorChannel", SceneCursor);
        Set(view, "cursorEnergy", cal.ChannelToEnergy(SceneCursor));
        Set(view, "cursorX", 400);
        // Ближайшая линия не ищется: она к числам панели отношения не имеет,
        // а поиск потянул бы за собой библиотеку нуклидов.
        Set(view, "nuclideCursorPeakDirty", false);
        Set(view, "nuclideCursorPeak", null);

        // Выделение: вторая панель.
        Set(view, "selectionStart", 1000);
        Set(view, "selectionEnd", 2000);
        Set(view, "selectionFWHM", 0.1234567);
        Set(view, "selectionFullWidth", 1234);
        Set(view, "selectionCentroidCh", 4321);
        Set(view, "selectionCentroidkeV", 1234.5678);
        Set(view, "selectionFWHMinkev", 1234.5678);
    }

    /// <summary>
    /// Числа выделения. <paramref name="detected"/> = true — ветка «обнаружено»
    /// (активность числом и ошибкой), false — «ниже порога» (верхние пределы и
    /// строка Lu). Обе ветки рисуют РАЗНЫЕ места печати, и обе нужны.
    /// </summary>
    static object Analytics(bool detected, string refusal, bool zeroErr, bool zeroBg)
    {
        Type t = typeof(EnergySpectrumView).GetNestedType("SelectionAnalytics", BindingFlags.NonPublic);
        if (t == null) throw new InvalidOperationException("нет EnergySpectrumView.SelectionAnalytics");
        object a = Activator.CreateInstance(t, true);

        P(t, a, "HasSelection", true);
        P(t, a, "StartChannel", 1000);
        P(t, a, "EndChannel", 2000);
        P(t, a, "StartEnergy", 1250.5);
        P(t, a, "EndEnergy", 2500.75);
        P(t, a, "GrossCounts", 123456.75);
        P(t, a, "AdjBgCounts", zeroBg ? 0.0 : 4321.25);
        P(t, a, "PeakCounts", 98765.5);
        P(t, a, "NetCounts", detected ? 119135.5 : 1234.5);
        P(t, a, "NetCountsErr", zeroErr ? 0.0 : 1234.5);
        P(t, a, "NetCps", 1234.56789);
        P(t, a, "NetCpsErr", zeroErr ? 0.0 : 12.345678);
        P(t, a, "Lc", 2345.75);
        P(t, a, "Lu", 3456.25);
        P(t, a, "Ld", 4567.125);
        P(t, a, "Lq", 5678.0625);
        P(t, a, "Activity", 12345.75);
        P(t, a, "ActivityError", 1234.25);
        P(t, a, "ActivityUpperLimit", 23456.5);
        P(t, a, "ActivityByMass", 34567.25);
        P(t, a, "ActivityByMassError", 3456.125);
        P(t, a, "ActivityByMassUpperLimit", 45678.75);
        P(t, a, "ActivityByVolume", 56789.5);
        P(t, a, "ActivityByVolumeError", 5678.25);
        P(t, a, "ActivityByVolumeUpperLimit", 67890.125);
        P(t, a, "ActivityLabel", "Cs-137");
        P(t, a, "ActivityLineKev", 1234.5678);
        // Мельче тысячной нарочно: ради такого выхода строка и заведена (S96),
        // и печатается он не «f2», а «g4».
        P(t, a, "ActivityIntensity", 0.0009);
        P(t, a, "ActivityRivals", 3);
        P(t, a, "ActivityRivalFactor", 1234.5);
        P(t, a, "ActivityRefusal", refusal == null ? "" : refusal);
        return a;
    }

    static void P(Type t, object o, string name, object value)
    {
        PropertyInfo p = t.GetProperty(name, Any);
        if (p == null) throw new InvalidOperationException("нет SelectionAnalytics." + name);
        p.SetValue(o, value, null);
    }

    static FieldInfo Field(string name)
    {
        FieldInfo f = typeof(EnergySpectrumView).GetField(name, Any);
        if (f == null) throw new InvalidOperationException("нет поля EnergySpectrumView." + name);
        return f;
    }

    static void Set(object view, string name, object value)
    {
        Field(name).SetValue(view, value);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  ЗАМЕР
    // ══════════════════════════════════════════════════════════════════════

    // ⚠ Ищется ВИД, а не совпадение с эталоном. Запятая между цифрами — это
    //   либо разделитель дроби («1234,50»), либо разделитель разрядов
    //   («1,234.50»), и запрещены оба. Пробельная группировка — любой из
    //   пробелов, которыми её ставят культуры: обычный, неразрывный,
    //   узкий неразрывный, тонкий, цифровой.
    static readonly Regex Grouped = new Regex(
        "[0-9][ \u00A0\u202F\u2009\u2007][0-9]{3}(?![0-9])", RegexOptions.None);
    static readonly Regex CommaInNumber = new Regex("[0-9],[0-9]", RegexOptions.None);

    static void A244P4()
    {
        Say("");
        Say("══════════════════════════════════════════════════════════════");
        Say("`A244` П4: ЧИСЛА БОЛЬШОГО ГРАФИКА (панель курсора, шкалы, отказы)");
        Say("══════════════════════════════════════════════════════════════");

        CultureInfo.DefaultThreadCurrentCulture = null;
        CultureInfo.DefaultThreadCurrentUICulture = null;
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

        Shot want = Run();
        if (want.Err != null)
        {
            Say("  ⛔ ЭТАЛОН НЕ СНЯТ, замер невозможен: " + want.Err);
            failures++;
            return;
        }

        Say("  слотов печати: панель «обнаружено» " + want.Panel.Length
            + ", «ниже порога» " + want.PanelLow.Length
            + ", «без ошибок» " + want.PanelNoErr.Length
            + ", «без фона» " + want.PanelNoBg.Length
            + ", шкала " + want.Axis.Length + ", степень десяти " + want.Pow10.Length
            + ", отказы " + want.Refusals.Length);
        Say("");
        Say("  ЭТАЛОН (инвариант), панель «обнаружено»:");
        foreach (string t in want.Panel) Say("      " + Show(t));
        Say("  ЭТАЛОН (инвариант), панель «ниже порога»:");
        foreach (string t in want.PanelLow) Say("      " + Show(t));
        Say("  ЭТАЛОН (инвариант), панель «без ошибок»: " + Join(want.PanelNoErr));
        Say("  ЭТАЛОН (инвариант), панель «без фона»: " + Join(want.PanelNoBg));
        Say("  ЭТАЛОН (инвариант), шкала: " + Join(want.Axis));
        Say("  ЭТАЛОН (инвариант), степень десяти: " + Join(want.Pow10));
        Say("  ЭТАЛОН (инвариант), отказы: " + Join(want.Refusals));
        Say("  ЭТАЛОН (инвариант), секундомер: " + Show(want.Stopwatch));

        // ── ЦЕНА ПОЛОВИНЫ ПРАВКИ. Обе половины мерятся ОТДЕЛЬНО: перевод на
        //    инвариант без смены `n…`→`f…` оставляет запятую в разрядах, а
        //    смена формата без культуры оставляет запятую в дроби. Ни одна
        //    из двух проверок не поймала бы другую.
        Say("");
        Say("  [полож. контроль ПРЕЖНЕГО кода] что было бы без каждой половины правки:");
        Say("      только инвариант, формат остался «n2»:  «"
            + (1234.5).ToString("n2", CultureInfo.InvariantCulture) + "»  ⛔ разделитель разрядов");
        CultureInfo ru = CultureInfo.GetCultureInfo("ru-RU");
        Say("      только «f2», культура осталась ru-RU:    «"
            + (1234.5).ToString("f2", ru) + "»  ⛔ разделитель дроби");
        Say("      прежний код целиком («n2» на ru-RU):     «"
            + (1234.5).ToString("n2", ru) + "»");
        Say("      после правки («f2» инвариантом):         «"
            + (1234.5).ToString("f2", CultureInfo.InvariantCulture) + "»");

        // ── ЧТО ВИДИТ ЧЕЛОВЕК НА ЖИВОЙ СИСТЕМЕ. Плечо не судит — оно
        //    ПОКАЗЫВАЕТ. Культура здесь та самая, что ставит костыль
        //    `MainForm.cs:158-160`: клон системной с ТОЧКОЙ в дробной части
        //    и НЕТРОНУТЫМ разделителем разрядов. То есть эта строка — экран
        //    поставляемого приложения на русской системе, и на нём видно, что
        //    костыль лечит только половину: дробь он чинит, разряды нет.
        CultureInfo crutch = (CultureInfo)CultureInfo.GetCultureInfo("ru-RU").Clone();
        crutch.NumberFormat.NumberDecimalSeparator = ".";
        Thread.CurrentThread.CurrentCulture = crutch;
        Shot live = Run();
        Say("");
        Say("── ЭКРАН ЖИВОЙ СИСТЕМЫ (ru-RU + костыль MainForm), НЕ СУДИТСЯ ──");
        if (live.Err != null)
        {
            Say("  ⛔ не снят: " + live.Err);
            failures++;
        }
        else
        {
            Say("  панель «обнаружено»: " + Join(live.Panel));
        }

        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

        // ── ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ САМОЙ ПРОВЕРКИ ВИДА. Без него «ни запятой,
        //    ни разделителя разрядов» неотличимо от «проверка ничего не
        //    ищет»: разбор вида — это разбор регулярками, и регулярка,
        //    написанная мимо, молчит ровно так же, как чистая строка.
        Say("");
        string[] bad = { "1,234.50", "1 234,50", "1\u00A0234.50", "1\u202F234.50", "1234.50" };
        int caught = 0;
        for (int i = 0; i < bad.Length; i++)
        {
            bool hit = CommaInNumber.IsMatch(bad[i]) || Grouped.IsMatch(bad[i]);
            if (hit) caught++;
            Say("  [полож. контроль ПРОВЕРКИ ВИДА] «" + Show(bad[i]) + "» → "
                + (hit ? "поймано" : "пропущено"));
        }

        bool viewArmOk = caught == 4 && !CommaInNumber.IsMatch("1234.50") && !Grouped.IsMatch("1234.50");
        Say("  поймано " + caught + " из 4 порченых, чистая строка не тронута: "
            + (viewArmOk ? "проверка вида работает"
                         : "⛔ ПРОВЕРКА ВИДА НЕ РАБОТАЕТ, её вердикты ничего не значат"));
        if (!viewArmOk) failures++;

        string[] crossPanel = null;

        foreach (string name in Foreign)
        {
            CultureInfo os = CultureInfo.GetCultureInfo(name);
            Say("");
            Say("── ПЛЕЧО " + name + " (костыля MainForm нет, подмены разделителя нет) ──");
            Thread.CurrentThread.CurrentCulture = os;
            bool comma = os.NumberFormat.NumberDecimalSeparator == ",";

            Shot got = Run();
            if (got.Err != null)
            {
                Say("  ⛔ плечо не отработало: " + got.Err);
                failures++;
                continue;
            }

            // ── Положительный контроль плеча: культура обязана быть видна, и
            //    костыль обязан НЕ дотягиваться.
            string wantBare = comma ? "1,5" : "1.5";
            bool armOk = got.Bare == wantBare && got.Sep == (comma ? "," : ".");
            Say("  [полож. контроль] `(1.5).ToString()` = «" + got.Bare
                + "», разделитель культуры = «" + got.Sep + "»"
                + (armOk ? " — плечо воспроизводит культуру, костыля нет"
                         : " ⛔ ОЖИДАЛОСЬ «" + wantBare + "»: ПЛЕЧО НЕ МЕРИТ"));
            if (!armOk) failures++;

            // ── ПРЯМОЕ: посимвольное сравнение с эталоном.
            Same("  ПЕЧАТЬ  панель под курсором, ветка «обнаружено»", got.Panel, want.Panel);
            Same("  ПЕЧАТЬ  панель под курсором, ветка «ниже порога»", got.PanelLow, want.PanelLow);
            Same("  ПЕЧАТЬ  панель под курсором, ветка «без ошибок»", got.PanelNoErr, want.PanelNoErr);
            Same("  ПЕЧАТЬ  панель под курсором, ветка «без фона»", got.PanelNoBg, want.PanelNoBg);
            Same("  ПЕЧАТЬ  подписи шкалы (энергия «f0» и каналы)", got.Axis, want.Axis);
            Same("  ПЕЧАТЬ  подписи шкалы: степень десяти", got.Pow10, want.Pow10);
            Same("  ПЕЧАТЬ  отказы вместо числа", Head(got.Refusals), Head(want.Refusals));
            Same("  ПЕЧАТЬ  секундомер отрисовки", new string[] { got.Stopwatch },
                 new string[] { want.Stopwatch });

            // ── Контроль ТРЕТЬЕГО слота отказов: он нарочно печатается
            //    культурой потока и обязан РАЗОЙТИСЬ, иначе плечо не мерит.
            string tail = got.Refusals[got.Refusals.Length - 1];
            bool tailDiffers = comma ? tail != want.Refusals[want.Refusals.Length - 1]
                                     : tail == want.Refusals[want.Refusals.Length - 1];
            Say("    [полож. контроль] нарочно культурная строка = «" + tail + "»"
                + (tailDiffers ? " — плечо разводит культуры"
                               : " ⛔ ПЛЕЧО НЕ РАЗВОДИТ КУЛЬТУРЫ"));
            if (!tailDiffers) failures++;

            // ── ВИД: ни запятой между цифрами, ни разделителя разрядов.
            //    ⛔ Это ОТДЕЛЬНАЯ проверка, а не следствие сравнения плеч:
            //    запятая в группах одинакова на всех культурах, и сравнение
            //    плеч её пропускает (цена — доля П7).
            ViewCheck("  ВИД  панель «обнаружено»", got.Panel);
            ViewCheck("  ВИД  панель «ниже порога»", got.PanelLow);
            ViewCheck("  ВИД  панель «без ошибок»", got.PanelNoErr);
            ViewCheck("  ВИД  панель «без фона»", got.PanelNoBg);
            ViewCheck("  ВИД  подписи шкалы", got.Axis);
            ViewCheck("  ВИД  степень десяти", got.Pow10);
            ViewCheck("  ВИД  отказы", Head(got.Refusals));

            // ── ПЕРЕКРЁСТНОЕ: панель, снятая на чужой культуре, читается тут.
            if (crossPanel == null)
            {
                crossPanel = got.Panel;
            }
            else
            {
                Same("  ТЕКСТ перекрёстный  панель чужого плеча та же", crossPanel, got.Panel);
            }
        }

        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
    }

    static string[] Head(string[] a)
    {
        string[] r = new string[a.Length - 1];
        Array.Copy(a, r, a.Length - 1);
        return r;
    }

    static void Same(string title, string[] got, string[] want)
    {
        if (got == null || want == null)
        {
            Say(title + "  ⛔ НЕ СНЯТО");
            failures++;
            return;
        }

        if (got.Length != want.Length)
        {
            Say(title + "  ⛔ СЛОТОВ " + got.Length + ", ЭТАЛОН " + want.Length);
            failures++;
            return;
        }

        for (int i = 0; i < got.Length; i++)
        {
            if (!string.Equals(got[i], want[i], StringComparison.Ordinal))
            {
                Say(title);
                Say("    ⛔ слот " + i + " ПОЛУЧЕНО «" + Show(got[i]) + "»");
                Say("                ЭТАЛОН   «" + Show(want[i]) + "»");
                failures++;
                return;
            }
        }

        Say(title + "  совпало с эталоном (" + got.Length + " слотов)");
    }

    static void ViewCheck(string title, string[] slots)
    {
        string dirty = null;
        for (int i = 0; i < slots.Length && dirty == null; i++)
        {
            string v = slots[i];
            if (v == null) continue;
            if (CommaInNumber.IsMatch(v)) dirty = "слот " + i + " = «" + Show(v) + "» (запятая между цифрами)";
            else if (Grouped.IsMatch(v)) dirty = "слот " + i + " = «" + Show(v) + "» (разделитель разрядов)";
        }

        Say(title + ": " + (dirty == null ? "ни запятой, ни разделителя разрядов"
                                          : "⛔ " + dirty));
        if (dirty != null) failures++;
    }

    static string Join(string[] a)
    {
        if (a == null) return "НЕ СНЯТО";
        string[] shown = new string[a.Length];
        for (int i = 0; i < a.Length; i++) shown[i] = Show(a[i]);
        return "«" + string.Join("» | «", shown) + "»";
    }

    static string Show(string s)
    {
        if (s == null) return "(null)";
        StringBuilder sb = new StringBuilder();
        foreach (char c in s)
        {
            if (c == '\u00A0') sb.Append("<NBSP>");
            else if (c == '\u202F') sb.Append("<NNBSP>");
            else if (c == '\u2009') sb.Append("<THSP>");
            else if (c == '\u2007') sb.Append("<FGSP>");
            else sb.Append(c);
        }

        return sb.ToString();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  СТОРОЖ МОДАЛЬНЫХ ОКОН (образец — `CultureProbeO14`, полоса F20, `A245`)
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
                        + "» — сторож закрывает его сам, разряд `A245`");
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
                    string s = txt.ToString().Trim();
                    if (s.Length > 0)
                    {
                        if (acc.Length > 0) acc.Append(" / ");
                        acc.Append(s);
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
        Say("ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ СТОРОЖА ОКОН (`--modal-control`)");
        int before = modalSeen;
        DateTime t0 = DateTime.UtcNow;
        Thread th = new Thread(delegate()
        {
            System.Windows.Forms.MessageBox.Show("контрольное окно полосы F27",
                                                 "контроль", System.Windows.Forms.MessageBoxButtons.OK);
        });
        th.IsBackground = true;
        th.SetApartmentState(ApartmentState.STA);
        th.Start();
        bool closed = th.Join(20000);
        double sec = (DateTime.UtcNow - t0).TotalSeconds;
        Say("  окно поднято нарочно, закрыто сторожем: " + (closed ? "ДА" : "НЕТ")
            + ", секунд " + sec.ToString("F1", CultureInfo.InvariantCulture)
            + ", окон назвал сторож: " + (modalSeen - before));
        if (!closed)
        {
            Say("  ⛔ СТОРОЖ НЕ РАБОТАЕТ: окно не закрыто за 20 с");
            failures++;
        }
    }

    static void Say(string line)
    {
        Console.WriteLine(line);
        Log.AppendLine(line);
    }

    static void Finish(string outPath)
    {
        if (string.IsNullOrEmpty(outPath)) return;
        try
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(outPath));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(outPath, Log.ToString(), new UTF8Encoding(false));
            Console.WriteLine("вывод: " + Path.GetFullPath(outPath));
        }
        catch (Exception ex)
        {
            Console.WriteLine("вывод НЕ записан: " + ex.Message);
        }
    }
}
