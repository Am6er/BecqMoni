// `A244`, ДОЛЯ П9 «ГРАФИКИ КАЛИБРОВОК»: ЧИСЛА, КОТОРЫЕ ЧЕЛОВЕК ВИДИТ НА ТРЁХ
// ГРАФИКАХ — калибровки энергии, калибровки ПШПВ и предпросмотра формы пика.
//
//     calibgraphprobef35 [--out=<файл>] [--modal-control]
//
// Что мерится. `CalibrationGraph`, `FWHMCalibrationGraph` и
// `PeakShapePreviewGraph` ничего не печатают в поле ввода — они РИСУЮТ,
// `Graphics.DrawString`. Поэтому «текст на экране» здесь берётся не у свойства
// `Text`, а у самой отрисовки: вид рисует в МЕТАФАЙЛ EMF+, а проба вынимает из
// его записей `DrawString` ровно те строки, которые ушли бы на экран. Своей
// копии форматирования у пробы НЕТ — сравнивается то, что нарисовал живой
// метод приложения. Приём взят у пробы полосы F27 (доля П4), и это сознательно:
// для графика он единственный, который не подменяет измеряемое.
//
// ⛔ ОКНО НЕ ПОДНИМАЕТСЯ. Все три вида — потомки `Form`, но объект формы
//    СОЗДАЁТСЯ и никуда не показывается: ни `Show`, ни `ShowDialog`, ни
//    `Application.Run`. Поля сцены выставляются отражением, приватные методы
//    отрисовки зовутся отражением же. Сторож модальных окон стоит первым делом
//    (разряд `A245`, полоса F20): голое `MessageBox.Show` на безоконном пути
//    вешает прогон насмерть, и без сторожа исход зависел бы от того, закроет ли
//    окно кто-то снаружи.
//
// ⛔ КОСТЫЛЯ `MainForm.cs:158-160` ЗДЕСЬ НЕТ И БЫТЬ НЕ МОЖЕТ. Клон культуры с
//    подменённым разделителем ставит `MainForm` в своём конструкторе, а проба
//    его не зовёт вовсе (у всех трёх видов взяты конструкторы БЕЗ `MainForm`
//    либо с `null`). То есть замер идёт БЕЗ подпорки. Это не заявление: каждое
//    плечо печатает `CurrentCulture.NumberDecimalSeparator` и голое
//    `(1.5).ToString()`, и на `ru-RU`/`de-DE` они обязаны показать ЗАПЯТУЮ.
//    Если показали точку — костыль дотянулся, и плечо не мерит.
//
// ⛔ СУДИТСЯ ВИД СТРОКИ, А НЕ ТОЛЬКО РАВЕНСТВО ПЛЕЧ. Это оплачено долей П7: она
//    прошла приёмку с непереведённой группировкой разрядов именно потому, что
//    приёмка сравнивала плечи между собой, а запятая в группах («1,234.50»)
//    одинакова на всех культурах и плечи не разводит. Поэтому рядом с
//    посимвольным сравнением стоит отдельный разбор ВИДА.
//
// ⚠ Числа сцены взяты БОЛЬШЕ ТЫСЯЧИ и с дробной частью нарочно: только на таком
//   числе видны обе беды разом — разделитель дроби и разделитель разрядов.
//   ⚠ В доле П9 форматов `n…` не было НИ ОДНОГО (все форматы — `f4`, `f2`,
//   `f1`, `0.###`, `0.####`, `0.0`, они группировки не несут), поэтому на
//   числе ≥ 1000 меняется только разделитель дроби; проверка вида всё равно
//   стоит — она сторожит, чтобы группировка не появилась впредь.
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

static class CalibGraphProbeF35
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
        string showCulture = null;
        foreach (string a in args)
        {
            if (a.StartsWith("--out=", StringComparison.Ordinal)) outPath = a.Substring(6);
            else if (a.StartsWith("--show=", StringComparison.Ordinal)) showCulture = a.Substring(7);
            else if (a == "--modal-control") modalControl = true;
            // `A263`: неизвестное ИМЯ ключа — отказ, а не молчание.
            else
            {
                Console.WriteLine("не знаю ключа: " + a);
                return 2;
            }
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

        Say("== `A244` П9 «ГРАФИКИ КАЛИБРОВОК»: ЧИСЛА, КОТОРЫЕ РИСУЮТ ТРИ ВИДА ==");
        Say("");

        Assembly app = typeof(CalibrationGraph).Assembly;
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

        try
        {
            MetafileSelfTest();
            // ⚠ `--show=<культура>` НИЧЕГО НЕ СУДИТ: он снимает и печатает все
            //   группы под названной культурой. Нужен, чтобы получить «ДО»
            //   ЗАМЕРОМ, а не пересказом: с прежним исходником прямое плечо
            //   называет одно место и умолкает, а строку целиком показать
            //   больше нечем.
            if (showCulture != null) ShowOnly(showCulture);
            else A244P9();
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
    //  `Graphics` запечатан, подменить `DrawString` нечем. Зато
    //  `Graphics.FromImage(Metafile)` ЗАПИСЫВАЕТ вызовы, а `EnumerateMetafile`
    //  их проигрывает по одной записи, отдавая сырые данные. У записи
    //  EmfPlusDrawString данные лежат так:
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
        public string Bare;              // (1.5).ToString() без культуры
        public string Sep;               // разделитель дроби культуры плеча

        public string[] CalibAxis;       // подписи осей графика калибровки энергии
        public string[] CalibLabel;      // всплывающая подпись точки, без весов
        public string[] CalibLabelW;     // она же с весами (лишняя строка «вес»)
        public string[] CalibWrite;      // формула + СКО, ветка без весов
        public string[] CalibWriteW;     // формула + СКО, ветка с весами (WMSE)

        public string[] FwhmAxis;        // подписи осей графика ПШПВ
        public string[] FwhmLabel;       // всплывающая подпись точки
        public string[] FwhmInfo;        // панель под курсором
        public string[] FwhmWrite;       // формула + СКО

        public string[] ShapeGrid;       // сетка предпросмотра: ось σ и шкала 0.0–1.0
        public string[] ShapeGauss;      // легенда, гауссиана
        public string[] ShapeEge;        // легенда, экспоненциально-гауссова
        public string[] ShapeVoigt;      // легенда, псевдо-Фойгт

        public string Control;           // НАРОЧНО культурная строка: обязана разойтись
        public string Err;
    }

    // Числа сцены. Больше тысячи и с дробной частью нарочно: на таком числе
    // разом видны и разделитель дроби, и разделитель разрядов, появись он.
    const int SceneChannels = 4096;
    const double SceneBigA = 1234.5678;
    const double SceneBigB = 2614.5432;
    const double SceneSmall = 0.5;

    static Shot Run()
    {
        Shot s = new Shot();
        try
        {
            s.Bare = (1.5).ToString();
            s.Sep = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
            // ⚠ Слот нарочно печатается культурой потока: он положительный
            //   контроль плеча и обязан РАЗОЙТИСЬ с эталоном. Без него «всё
            //   совпало» неотличимо от «плечо не мерит».
            s.Control = string.Format(CultureInfo.CurrentCulture, "(контроль плеча) {0:f4}", SceneBigA);

            using (Font font = new Font("Segoe UI", 9f))
            {
                RunCalibrationGraph(s, font);
                RunFwhmGraph(s, font);
                RunPeakShape(s, font);
            }
        }
        catch (Exception ex)
        {
            s.Err = ex.GetType().Name + ": " + ex.Message + "\n" + ex.StackTrace;
        }

        return s;
    }

    // ── График калибровки энергии ─────────────────────────────────────────
    static void RunCalibrationGraph(Shot s, Font font)
    {
        MethodInfo paintAxis = Method(typeof(CalibrationGraph), "PaintAxis");
        MethodInfo paintPoints = Method(typeof(CalibrationGraph), "PaintPoints");
        MethodInfo writeCalib = Method(typeof(CalibrationGraph), "WriteCalibration");

        using (CalibrationGraph view = new CalibrationGraph())
        {
            view.Font = font;

            List<CalibrationPoint> points = new List<CalibrationPoint>();
            points.Add(new CalibrationPoint(1234, 1234.5m, 100));
            points.Add(new CalibrationPoint(2048, 2614.55m, 200));

            PolynomialEnergyCalibration cal = new PolynomialEnergyCalibration();
            cal.PolynomialOrder = 1;
            cal.Coefficients = new double[] { 35.1234, 1.0 };

            Set(view, "maxChannels", SceneChannels);
            Set(view, "maxEnergy", 3500.0);
            Set(view, "width", 800);
            Set(view, "height", 600);
            Set(view, "startwidth", 10);
            Set(view, "startheight", 10);
            Set(view, "calibration", cal);
            Set(view, "originalcalibration", cal);
            Set(view, "points", points);
            Set(view, "originalpoints", points);
            Set(view, "polyorder", 1);
            Set(view, "polycorrect", true);
            Set(view, "recalcPoly", false);
            Set(view, "formloading", false);
            Set(view, "glowPoint", points[0]);
            Set(view, "mouseX", 400);
            Set(view, "mouseY", 300);

            Set(view, "weights", false);
            s.CalibAxis = Capture(delegate(Graphics g) { paintAxis.Invoke(view, new object[] { g }); }).ToArray();
            s.CalibLabel = Capture(delegate(Graphics g) { paintPoints.Invoke(view, new object[] { g }); }).ToArray();
            s.CalibWrite = Capture(delegate(Graphics g) { writeCalib.Invoke(view, new object[] { g }); }).ToArray();

            // ⚠ Вторая ветка — НЕ избыточность: у подписи точки и у СКО по два
            //   разных куска кода, «с весами» и «без», и одна сцена оставила бы
            //   половину мест неснятой.
            Set(view, "weights", true);
            s.CalibLabelW = Capture(delegate(Graphics g) { paintPoints.Invoke(view, new object[] { g }); }).ToArray();
            s.CalibWriteW = Capture(delegate(Graphics g) { writeCalib.Invoke(view, new object[] { g }); }).ToArray();
        }
    }

    // ── График калибровки ПШПВ ────────────────────────────────────────────
    static void RunFwhmGraph(Shot s, Font font)
    {
        MethodInfo paintAxis = Method(typeof(FWHMCalibrationGraph), "PaintAxis");
        MethodInfo paintPoints = Method(typeof(FWHMCalibrationGraph), "PaintPoints");
        MethodInfo drawInfo = Method(typeof(FWHMCalibrationGraph), "DrawInfoPanel");
        MethodInfo writeCalib = Method(typeof(FWHMCalibrationGraph), "WriteCalibration");

        using (FWHMCalibrationGraph view = new FWHMCalibrationGraph())
        {
            view.Font = font;

            List<CalibrationPeak> peaks = new List<CalibrationPeak>();
            CalibrationPeak p0 = new CalibrationPeak();
            p0.Channel = 1234;
            p0.Energy = SceneBigA;
            p0.FWHM = SceneBigB;
            peaks.Add(p0);
            CalibrationPeak p1 = new CalibrationPeak();
            p1.Channel = 2048;
            p1.Energy = SceneBigB;
            p1.FWHM = SceneSmall;
            peaks.Add(p1);

            SqrtFwhmCalibration fw = new SqrtFwhmCalibration();
            fw.Coefficients = new double[] { 12.3456, 0.5, 0.0 };
            fw.CalibrationPeaks = peaks;

            PolynomialEnergyCalibration en = new PolynomialEnergyCalibration();
            en.PolynomialOrder = 1;
            en.Coefficients = new double[] { 35.1234, 1.0 };

            Set(view, "maxChannels", SceneChannels);
            Set(view, "maxFWHM", 64.0);
            Set(view, "width", 800);
            Set(view, "height", 600);
            Set(view, "startwidth", 10);
            Set(view, "startheight", 10);
            Set(view, "fwhmCalibration", fw);
            Set(view, "originalfwhmCalibration", fw);
            Set(view, "energyCalibration", en);
            Set(view, "points", peaks);
            Set(view, "originalpoints", peaks);
            Set(view, "polycorrect", true);
            Set(view, "recalcCurve", false);
            Set(view, "glowPoint", peaks[0]);
            Set(view, "drawInfoPanel", true);
            Set(view, "mouseX", 400);
            Set(view, "mouseY", 300);

            s.FwhmAxis = Capture(delegate(Graphics g) { paintAxis.Invoke(view, new object[] { g }); }).ToArray();
            s.FwhmLabel = Capture(delegate(Graphics g) { paintPoints.Invoke(view, new object[] { g }); }).ToArray();
            s.FwhmInfo = Capture(delegate(Graphics g) { drawInfo.Invoke(view, new object[] { g }); }).ToArray();
            s.FwhmWrite = Capture(delegate(Graphics g) { writeCalib.Invoke(view, new object[] { g }); }).ToArray();
        }
    }

    // ── Предпросмотр формы пика ───────────────────────────────────────────
    static void RunPeakShape(Shot s, Font font)
    {
        MethodInfo paintGrid = Method(typeof(PeakShapePreviewGraph), "PaintGrid");
        MethodInfo paintLegend = Method(typeof(PeakShapePreviewGraph), "PaintLegend");
        Rectangle bounds = new Rectangle(52, 16, 700, 400);

        // ── 1. гауссиана
        using (PeakShapePreviewGraph view = new PeakShapePreviewGraph(null))
        {
            view.Font = font;
            SqrtFwhmCalibration fw = new SqrtFwhmCalibration();
            fw.Coefficients = new double[] { 12.3456, 0.5, 0.0 };
            fw.PeakType = FwhmCalibration.GaussianPeakType;
            view.Init(fw, "форма пика");
            // Числа ставятся ПОСЛЕ Init: PrepareProfile их пересчитывает, а
            // рисование читает поля в момент отрисовки.
            Set(view, "gaussianReferenceSigma", SceneBigA);
            Set(view, "currentCurveSigma", SceneSmall);
            s.ShapeGrid = Capture(delegate(Graphics g)
            {
                paintGrid.Invoke(view, new object[] { g, bounds });
            }).ToArray();
            s.ShapeGauss = Capture(delegate(Graphics g)
            {
                paintLegend.Invoke(view, new object[] { g, bounds });
            }).ToArray();
        }

        // ── 2. экспоненциально-гауссова: свой заголовок «{1:0.0}, {2:0.0}»
        //      и своя строка «t = x/σ, σ = …, L = …, R = …»
        using (PeakShapePreviewGraph view = new PeakShapePreviewGraph(null))
        {
            view.Font = font;
            SqrtFwhmCalibration fw = new SqrtFwhmCalibration();
            fw.Coefficients = new double[] { 12.3456, 0.5, 0.0 };
            fw.PeakType = FwhmCalibration.ExpGaussExpPeakType;
            fw.ExpGaussExpLeftTail = SceneBigA;
            fw.ExpGaussExpRightTail = SceneSmall;
            view.Init(fw, "форма пика");
            Set(view, "gaussianReferenceSigma", SceneBigA);
            Set(view, "currentCurveSigma", SceneBigB);
            s.ShapeEge = Capture(delegate(Graphics g)
            {
                paintLegend.Invoke(view, new object[] { g, bounds });
            }).ToArray();
        }

        // ── 3. псевдо-Фойгт: семь чисел в легенде, из них четыре — поля,
        //      посчитанные PrepareProfile.
        using (PeakShapePreviewGraph view = new PeakShapePreviewGraph(null))
        {
            view.Font = font;
            SqrtFwhmCalibration fw = new SqrtFwhmCalibration();
            fw.Coefficients = new double[] { 12.3456, 0.5, 0.0 };
            fw.PeakType = FwhmCalibration.VoigtPeakType;
            fw.VoigtSigma = SceneBigA;
            fw.VoigtGamma = SceneSmall;
            view.Init(fw, "форма пика");
            Set(view, "gaussianReferenceSigma", SceneBigA);
            Set(view, "currentCurveSigma", SceneBigB);
            Set(view, "voigtScaleFactor", SceneBigB);
            PseudoVoigtParameters vp = new PseudoVoigtParameters();
            vp.GaussianSigma = SceneBigA;
            vp.LorentzGamma = SceneBigB;
            vp.Eta = SceneSmall;
            Set(view, "voigtParameters", vp);
            s.ShapeVoigt = Capture(delegate(Graphics g)
            {
                paintLegend.Invoke(view, new object[] { g, bounds });
            }).ToArray();
        }
    }

    // ── отражение ─────────────────────────────────────────────────────────
    static MethodInfo Method(Type t, string name)
    {
        MethodInfo m = t.GetMethod(name, Any);
        if (m == null) throw new InvalidOperationException("в сборке нет " + t.Name + "." + name);
        return m;
    }

    static void Set(object view, string name, object value)
    {
        Type t = view.GetType();
        FieldInfo f = null;
        while (t != null && f == null)
        {
            f = t.GetField(name, Any);
            t = t.BaseType;
        }

        if (f == null)
        {
            throw new InvalidOperationException("нет поля " + view.GetType().Name + "." + name);
        }

        f.SetValue(view, value);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  ЗАМЕР
    // ══════════════════════════════════════════════════════════════════════

    // ⚠ Ищется ВИД, а не совпадение с эталоном. Запятая между цифрами — это
    //   либо разделитель дроби («1234,50»), либо разделитель разрядов
    //   («1,234.50»), и запрещены оба. Пробельная группировка — любой из
    //   пробелов, которыми её ставят культуры: обычный, неразрывный, узкий
    //   неразрывный, тонкий, цифровой.
    static readonly Regex Grouped = new Regex(
        "[0-9][     ][0-9]{3}(?![0-9])", RegexOptions.None);
    static readonly Regex CommaInNumber = new Regex("[0-9],[0-9]", RegexOptions.None);

    static void ShowOnly(string cultureName)
    {
        Say("");
        Say("── ПОКАЗ БЕЗ СУДА, культура " + cultureName + " ──");
        Thread.CurrentThread.CurrentCulture = cultureName == "invariant"
            ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(cultureName);
        Shot s = Run();
        if (s.Err != null) { Say("  ⛔ не снят: " + s.Err); failures++; return; }
        Say("  [полож. контроль] `(1.5).ToString()` = «" + s.Bare
            + "», разделитель культуры = «" + s.Sep + "»");
        Dump(s);
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
    }

    static void Dump(Shot s)
    {
        Say("  калибровка энергии, оси:        " + Join(s.CalibAxis));
        Say("  калибровка энергии, подпись:    " + Join(s.CalibLabel));
        Say("  калибровка энергии, подпись+вес:" + Join(s.CalibLabelW));
        Say("  калибровка энергии, СКО:        " + Join(s.CalibWrite));
        Say("  калибровка энергии, ВСКО:       " + Join(s.CalibWriteW));
        Say("  ПШПВ, оси:                      " + Join(s.FwhmAxis));
        Say("  ПШПВ, подпись точки:            " + Join(s.FwhmLabel));
        Say("  ПШПВ, панель под курсором:      " + Join(s.FwhmInfo));
        Say("  ПШПВ, СКО:                      " + Join(s.FwhmWrite));
        Say("  форма пика, сетка:              " + Join(s.ShapeGrid));
        Say("  форма пика, легенда гауссианы:  " + Join(s.ShapeGauss));
        Say("  форма пика, легенда ЭГЭ:        " + Join(s.ShapeEge));
        Say("  форма пика, легенда Фойгта:     " + Join(s.ShapeVoigt));
    }

    static void A244P9()
    {
        Say("");
        Say("══════════════════════════════════════════════════════════════");
        Say("`A244` П9: ЧИСЛА ТРЁХ ГРАФИКОВ (калибровка энергии, ПШПВ, форма пика)");
        Say("══════════════════════════════════════════════════════════════");

        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        Shot want = Run();
        if (want.Err != null)
        {
            Say("⛔ ЭТАЛОН НЕ СНЯТ: " + want.Err);
            failures++;
            return;
        }

        Say("");
        Say("── ЭТАЛОН (инвариантная культура) ──");
        Dump(want);

        // ── ЦЕНА ПРАВКИ, показанная числом. Группировки в доле не было ни в
        //    одном формате, поэтому «половина правки» здесь одна — культура.
        CultureInfo ru = CultureInfo.GetCultureInfo("ru-RU");
        Say("");
        Say("  [полож. контроль ПРЕЖНЕГО кода] что печатали форматы доли:");
        Say("      «f4» на ru-RU:          «" + SceneBigA.ToString("f4", ru) + "»"
            + "   после правки: «" + SceneBigA.ToString("f4", CultureInfo.InvariantCulture) + "»");
        Say("      «0.###» на ru-RU:       «" + SceneBigA.ToString("0.###", ru) + "»"
            + "   после правки: «" + SceneBigA.ToString("0.###", CultureInfo.InvariantCulture) + "»");
        Say("      дробное 0.5 «f2» ru-RU: «" + SceneSmall.ToString("f2", ru) + "»"
            + "   после правки: «" + SceneSmall.ToString("f2", CultureInfo.InvariantCulture) + "»");
        Say("      группировки в доле НЕТ: «n4» дала бы «"
            + SceneBigA.ToString("n4", CultureInfo.InvariantCulture)
            + "», а «f4» даёт «" + SceneBigA.ToString("f4", CultureInfo.InvariantCulture) + "»");

        // ── ЧТО ВИДИТ ЧЕЛОВЕК НА ЖИВОЙ СИСТЕМЕ. Культура здесь та самая, что
        //    ставит костыль `MainForm.cs:158-160`: клон системной с ТОЧКОЙ в
        //    дробной части. Не судится — показывается.
        // T245: НАРОЧНО — подмена разделителя ЗДЕСЬ и есть предмет замера;
        //    инвариант вместо неё стёр бы то самое, что плечо показывает.
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
            Say("  ПШПВ, подпись точки:       " + Join(live.FwhmLabel));
            Say("  форма пика, легенда ЭГЭ:   " + Join(live.ShapeEge));
        }

        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

        // ── ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ САМОЙ ПРОВЕРКИ ВИДА. Без него «ни запятой,
        //    ни разделителя разрядов» неотличимо от «проверка ничего не ищет».
        Say("");
        string[] bad = { "1,234.50", "1 234,50", "1 234.50", "1 234.50", "1234.50" };
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

        Shot cross = null;

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

            bool ctlDiffers = comma ? got.Control != want.Control : got.Control == want.Control;
            Say("  [полож. контроль] нарочно культурная строка = «" + got.Control + "»"
                + (ctlDiffers ? " — плечо разводит культуры" : " ⛔ ПЛЕЧО НЕ РАЗВОДИТ КУЛЬТУРЫ"));
            if (!ctlDiffers) failures++;

            // ── ПРЯМОЕ: посимвольное сравнение с эталоном.
            Same("  ПЕЧАТЬ  калибровка энергии: оси", got.CalibAxis, want.CalibAxis);
            Same("  ПЕЧАТЬ  калибровка энергии: подпись точки", got.CalibLabel, want.CalibLabel);
            Same("  ПЕЧАТЬ  калибровка энергии: подпись точки с весом", got.CalibLabelW, want.CalibLabelW);
            Same("  ПЕЧАТЬ  калибровка энергии: формула и СКО", got.CalibWrite, want.CalibWrite);
            Same("  ПЕЧАТЬ  калибровка энергии: формула и ВСКО", got.CalibWriteW, want.CalibWriteW);
            Same("  ПЕЧАТЬ  ПШПВ: оси", got.FwhmAxis, want.FwhmAxis);
            Same("  ПЕЧАТЬ  ПШПВ: подпись точки", got.FwhmLabel, want.FwhmLabel);
            Same("  ПЕЧАТЬ  ПШПВ: панель под курсором", got.FwhmInfo, want.FwhmInfo);
            Same("  ПЕЧАТЬ  ПШПВ: формула и СКО", got.FwhmWrite, want.FwhmWrite);
            Same("  ПЕЧАТЬ  форма пика: сетка", got.ShapeGrid, want.ShapeGrid);
            Same("  ПЕЧАТЬ  форма пика: легенда гауссианы", got.ShapeGauss, want.ShapeGauss);
            Same("  ПЕЧАТЬ  форма пика: легенда ЭГЭ", got.ShapeEge, want.ShapeEge);
            Same("  ПЕЧАТЬ  форма пика: легенда Фойгта", got.ShapeVoigt, want.ShapeVoigt);

            // ── ВИД: ни запятой между цифрами, ни разделителя разрядов.
            //    ⛔ ОТДЕЛЬНАЯ проверка, а не следствие сравнения плеч.
            ViewCheck("  ВИД  калибровка энергии: оси", got.CalibAxis);
            ViewCheck("  ВИД  калибровка энергии: подпись точки", got.CalibLabel);
            ViewCheck("  ВИД  калибровка энергии: подпись точки с весом", got.CalibLabelW);
            ViewCheck("  ВИД  калибровка энергии: формула и СКО", got.CalibWrite);
            ViewCheck("  ВИД  калибровка энергии: формула и ВСКО", got.CalibWriteW);
            ViewCheck("  ВИД  ПШПВ: оси", got.FwhmAxis);
            ViewCheck("  ВИД  ПШПВ: подпись точки", got.FwhmLabel);
            ViewCheck("  ВИД  ПШПВ: панель под курсором", got.FwhmInfo);
            ViewCheck("  ВИД  ПШПВ: формула и СКО", got.FwhmWrite);
            ViewCheck("  ВИД  форма пика: сетка", got.ShapeGrid);
            ViewCheck("  ВИД  форма пика: легенда гауссианы", got.ShapeGauss);
            ViewCheck("  ВИД  форма пика: легенда ЭГЭ", got.ShapeEge);
            ViewCheck("  ВИД  форма пика: легенда Фойгта", got.ShapeVoigt);

            // ── ПЕРЕКРЁСТНОЕ: снятое на чужой культуре читается тут.
            if (cross == null)
            {
                cross = got;
            }
            else
            {
                Same("  ТЕКСТ перекрёстный  подпись точки ПШПВ чужого плеча", cross.FwhmLabel, got.FwhmLabel);
                Same("  ТЕКСТ перекрёстный  легенда Фойгта чужого плеча", cross.ShapeVoigt, got.ShapeVoigt);
            }
        }

        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
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
        if (slots == null)
        {
            Say(title + ": ⛔ НЕ СНЯТО");
            failures++;
            return;
        }

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
            if (c == ' ') sb.Append("<NBSP>");
            else if (c == ' ') sb.Append("<NNBSP>");
            else if (c == ' ') sb.Append("<THSP>");
            else if (c == ' ') sb.Append("<FGSP>");
            else if (c == '\n') sb.Append("\\n");
            else if (c == '\t') sb.Append("\\t");
            else if (c == '\r') sb.Append("\\r");
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
        Say("ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ СТОРОЖА ОКОН (`--modal-control`)");
        int before = modalSeen;
        DateTime t0 = DateTime.UtcNow;
        Thread th = new Thread(delegate()
        {
            System.Windows.Forms.MessageBox.Show("контрольное окно полосы F35",
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
