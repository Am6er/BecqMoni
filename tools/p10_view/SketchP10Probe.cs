// Мерка полосы П10 захода 10.09.2026 по строке `E42` — кадр чертежа против
// того, что рисует `DrawSource` у ЦИЛИНДРА.
//
// ⛔ ЗАЧЕМ СВОЙ ФАЙЛ. Каталог `tools/effmaker/probes/**` в этом заходе занят
// другими полосами: читать и запускать можно, править нельзя. `EditorShot
// --check` при этом меряет ДРУГОЕ — пересечения подписей и выход ПОДПИСИ за
// поле; вылет самого ТЕЛА источника за верх кадра для него зелёный, потому что
// подписи при этом никуда не деваются. Ровно поэтому `E42` и была найдена
// чтением исходника, а не приёмкой.
//
//   sketchp10probe               — все плечи, код 0, если все сошлись
//   sketchp10probe --fingerprint — ещё и отпечаток кадра каждой сцены
//                                  (масштаб, верх мира, отступ, sha кадра):
//                                  им сличаются ДВЕ сборки, до правки и после
//
// Коды возврата: 0 — все плечи сошлись; 1 — хоть одно разошлось; 2 — отказ
// оснастки (не построилась сцена, не нашлось поле).

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using SketchMode = BecquerelMonitor.EfficiencyMaker.GeometrySketch.SketchMode;

namespace BecquerelMonitor.Probes
{
    static class SketchP10Probe
    {
        /// <summary>Колонка редактора, ужатая в свой `MinimumSize` — самое тесное место.</summary>
        static readonly Size Narrow = new Size(232, 440);

        /// <summary>Просторное поле — тот же чертёж при растянутом окне.</summary>
        static readonly Size Roomy = new Size(420, 460);

        const double Energy = 3000.0;

        static bool fingerprint;
        static int bad;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            foreach (string a in args)
            {
                if (a == "--fingerprint") fingerprint = true;
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            try
            {
                Console.WriteLine("=== E42: кадр обязан НАКРЫВАТЬ нарисованное тело цилиндра ===");
                Defect();
                Console.WriteLine();
                Console.WriteLine("=== ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: та же сцена, донышко тоньше сосуда ===");
                Control();
                Console.WriteLine();
                Console.WriteLine("=== ШТАТНЫЕ СЦЕНЫ: кадр не должен сдвинуться ни на пиксель ===");
                Standard();

                Console.WriteLine();
                Console.WriteLine(bad == 0 ? "ИТОГ: все плечи сошлись" : "ИТОГ: РАЗОШЛОСЬ " + bad);
                return bad == 0 ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ОТКАЗ ОСНАСТКИ: " + ex);
                return 2;
            }
        }

        // ------------------------------------------------------------------
        // Сцены
        // ------------------------------------------------------------------

        /// <summary>
        /// Сосуд, у которого ДОНЫШКО ТОЛЩЕ полной высоты, — тот самый случай,
        /// который строка `E42` называет вредом. В дереве такой геометрии нет
        /// ни одной (сплошной перебор 1026 файлов `.in`: 0), она набирается
        /// руками в полях редактора, и запрета на это нигде нет.
        /// </summary>
        static void Defect()
        {
            GeometryModel g = Cylinder();
            g.BeakerEndWallThickness = 40.0;   // донышко 4 см
            g.BeakerHeight = 1.0;              // а «полная высота» 1 мм
            Arm("донышко 40 мм при высоте сосуда 1 мм", g, Narrow, expectOverflow: false);
            Arm("оно же на просторном поле", g, Roomy, expectOverflow: false);
        }

        /// <summary>
        /// ⛔ Тот же вход, отличающийся РОВНО ОДНИМ: донышко тоньше сосуда.
        /// Без него нули выше не значат ничего — проба, у которой мерка не
        /// работает вовсе, дала бы «накрыто» и на неправленом дереве. Здесь
        /// кадр обязан накрывать тело И ДО правки, и после.
        /// </summary>
        static void Control()
        {
            GeometryModel g = Cylinder();
            g.BeakerEndWallThickness = 1.0;
            g.BeakerHeight = 40.0;
            Arm("КОНТРОЛЬ: донышко 1 мм при высоте сосуда 40 мм", g, Narrow, expectOverflow: false);
            Arm("КОНТРОЛЬ: оно же на просторном поле", g, Roomy, expectOverflow: false);

            // ⛔ Второй контроль, с другой стороны: мерка обязана УВИДЕТЬ вылет
            // там, где он заведомо есть. Кадр берётся у сцены-двойника, у
            // которой донышка нет вовсе, а рисуется тело сцены с толстым
            // донышком — то есть ровно то состояние, в котором дерево жило до
            // правки. Не сработал — значит счётчик вылета мёртв, и все нули
            // выше пусты.
            GeometryModel thin = Cylinder();
            thin.BeakerEndWallThickness = 0.0;
            thin.BeakerHeight = 1.0;
            GeometryModel thick = Cylinder();
            thick.BeakerEndWallThickness = 40.0;
            thick.BeakerHeight = 1.0;
            Cross("КОНТРОЛЬ: кадр от сцены без донышка, тело от сцены с донышком",
                  thin, thick, Narrow);
        }

        /// <summary>
        /// Штатные сцены: две заготовки редактора и все геометрии дерева.
        /// Кадр обязан остаться прежним до пикселя — сличается отпечатком
        /// между двумя сборками.
        /// </summary>
        static void Standard()
        {
            Frame("сцена «на земле», узкое поле", Scene("ground"), SketchMode.Source, Narrow);
            Frame("сцена «на земле», просторное", Scene("ground"), SketchMode.Source, Roomy);
            Frame("сцена «в лунке», узкое поле", Scene("borehole"), SketchMode.Source, Narrow);
            Frame("сцена «в лунке», просторное", Scene("borehole"), SketchMode.Source, Roomy);

            string dir = ModelsDir();
            if (dir == null)
            {
                Console.WriteLine("  ⚠ каталога моделей не нашлось — геометрии дерева не мерены");
                bad++;
                return;
            }

            string[] files = Directory.GetFiles(dir, "*.in");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            var hash = new StringBuilder();
            int drawn = 0;
            foreach (string file in files)
            {
                GeometryModel g;
                try { g = GeometryModel.Load(file); }
                catch { continue; }

                double scale, worldTop;
                int padTop;
                string sha = Paint(g, SketchMode.Source, Roomy, out scale, out worldTop, out padTop);
                if (sha == null) continue;

                drawn++;
                hash.Append(Path.GetFileName(file)).Append('|')
                    .Append(Num(scale)).Append('|').Append(Num(worldTop)).Append('|')
                    .Append(padTop.ToString(CultureInfo.InvariantCulture)).Append('|')
                    .Append(sha).Append('\n');
            }

            Console.WriteLine("  геометрий дерева нарисовано {0} из {1}; отпечаток кадров {2}",
                              drawn, files.Length, Sha(hash.ToString()));
            if (fingerprint)
            {
                Console.WriteLine(hash.ToString());
            }

            if (drawn == 0)
            {
                Console.WriteLine("  ⛔ не нарисовано НИ ОДНОЙ — мерить нечем");
                bad++;
            }
        }

        static GeometryModel Cylinder()
        {
            GeometryModel g = GeometryEditorPanel.Blank();
            GeometryPresets.Items[0].Apply(g);
            GeometryScenes.Ground(g, Energy);

            // Сцена «на земле» даёт грунт шириной в полметра: масштаб там
            // задаёт ширина, и вертикаль ни на что не влияет (это и есть
            // причина, по которой `E42` не закрылась на месте). Сосуд сужается
            // до настоящего стакана — тогда кадр меряет вертикаль.
            g.SourceType = GeometrySourceType.Cylinder;
            g.BeakerDiameter = 70.0;
            g.SourceHeight = 20.0;
            g.BeakerToDetectorDistance = 2.0;
            g.BeakerSideWallThickness = 1.0;
            return g;
        }

        static GeometryModel Scene(string kind)
        {
            GeometryModel g = GeometryEditorPanel.Blank();
            GeometryPresets.Items[0].Apply(g);
            if (kind == "ground") GeometryScenes.Ground(g, Energy);
            else GeometryScenes.Borehole(g, Energy);
            return g;
        }

        static string ModelsDir()
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 8 && dir != null; i++)
            {
                string candidate = Path.Combine(dir, "tools", "effmaker", "models");
                if (Directory.Exists(candidate)) return candidate;
                DirectoryInfo up = Directory.GetParent(dir.TrimEnd(Path.DirectorySeparatorChar));
                dir = up != null ? up.FullName : null;
            }

            return null;
        }

        // ------------------------------------------------------------------
        // Плечи
        // ------------------------------------------------------------------

        /// <summary>
        /// Плечо `E42`: НАКРЫВАЕТ ЛИ КАДР нарисованное тело. Число печатается
        /// в пикселях — на сколько верх тела выше верха кадра.
        /// </summary>
        static void Arm(string name, GeometryModel m, Size size, bool expectOverflow)
        {
            double scale, worldTop;
            int padTop;
            string sha = Paint(m, SketchMode.Source, size, out scale, out worldTop, out padTop);
            if (sha == null)
            {
                Console.WriteLine("  {0,-56} ⛔ чертёж не нарисовался", name);
                bad++;
                return;
            }

            double zBody = CylinderBodyTop(m);
            double yBody = padTop + (zBody - worldTop) * scale;
            double over = padTop - yBody;         // > 0 — тело выше кадра
            bool overflow = over > 0.5;
            bool ok = overflow == expectOverflow;
            if (!ok) bad++;

            Console.WriteLine("  {0,-56} {1}x{2}: верх тела y = {3}, верх кадра y = {4}, "
                              + "вылет {5} px — {6}",
                              name, size.Width, size.Height,
                              Num(yBody), padTop.ToString(CultureInfo.InvariantCulture), Num(over),
                              ok ? "ok" : "РАЗОШЛОСЬ");
            if (fingerprint)
            {
                Console.WriteLine("      масштаб {0}, верх мира {1}, sha кадра {2}",
                                  Num(scale), Num(worldTop), sha);
            }
        }

        /// <summary>
        /// Положительный контроль мерки: кадр берётся у ОДНОЙ сцены, тело — у
        /// ДРУГОЙ. Вылет обязан найтись, иначе счётчик не считает.
        /// </summary>
        static void Cross(string name, GeometryModel frameFrom, GeometryModel bodyFrom, Size size)
        {
            double scale, worldTop;
            int padTop;
            string sha = Paint(frameFrom, SketchMode.Source, size, out scale, out worldTop, out padTop);
            if (sha == null)
            {
                Console.WriteLine("  {0,-56} ⛔ чертёж не нарисовался", name);
                bad++;
                return;
            }

            double zBody = CylinderBodyTop(bodyFrom);
            double yBody = padTop + (zBody - worldTop) * scale;
            double over = padTop - yBody;
            bool ok = over > 0.5;
            if (!ok) bad++;
            Console.WriteLine("  {0,-56} вылет {1} px, ждали положительный — {2}",
                              name, Num(over), ok ? "ok" : "РАЗОШЛОСЬ");
        }

        static void Frame(string name, GeometryModel m, SketchMode mode, Size size)
        {
            double scale, worldTop;
            int padTop;
            string sha = Paint(m, mode, size, out scale, out worldTop, out padTop);
            if (sha == null)
            {
                Console.WriteLine("  {0,-40} ⛔ чертёж не нарисовался", name);
                bad++;
                return;
            }

            Console.WriteLine("  {0,-40} масштаб {1}, верх мира {2}, отступ {3}, sha {4}",
                              name, Num(scale), Num(worldTop),
                              padTop.ToString(CultureInfo.InvariantCulture), sha);
        }

        // ------------------------------------------------------------------
        // Мерка
        // ------------------------------------------------------------------

        /// <summary>
        /// ⛔ ВЕРХ ТЕЛА ЦИЛИНДРА СЧИТАЕТСЯ ЗДЕСЬ ЗАНОВО, а не берётся у
        /// чертежа: проба обязана держать СВОЁ представление о том, что
        /// рисуется, иначе она соглашается с правкой по построению. Формула
        /// списана с `GeometrySketch.DrawSource`, ветка `Cylinder`:
        /// стенка рисуется от `zSrcTop - hs`, где `zSrcTop = zWallTop - end`.
        /// </summary>
        static double CylinderBodyTop(GeometryModel m)
        {
            double tfr = Math.Max(m.FrontReflectorThickness, 0.0);
            double tsr = Math.Max(m.SideReflectorThickness, 0.0);
            double tfc = Math.Max(m.FrontCladdingThickness, 0.0);
            double tsc = Math.Max(m.SideCladdingThickness, 0.0);
            double tfg = Math.Max(m.FrontGapThickness, 0.0);
            double tsg = Math.Max(m.SideGapThickness, 0.0);
            if (m.Facing == GeometryDetectorFacing.Side)
            {
                double t = tfr; tfr = tsr; tsr = t;
                t = tfc; tfc = tsc; tsc = t;
                t = tfg; tfg = tsg; tsg = t;
            }

            double zFace = -(tfr + tfg + tfc);
            double zWallTop = zFace - Math.Max(m.BeakerToDetectorDistance, 0.0);
            double zSrcTop = zWallTop - Math.Max(m.BeakerEndWallThickness, 0.0);
            return zSrcTop - Math.Max(m.SourceHeight, 0.0);
        }

        /// <summary>
        /// Нарисовать чертёж в растр и вынуть из вида его раскладку. null —
        /// чертёж не нарисовался (масштаб не сложился, ранний выход `OnPaint`).
        /// </summary>
        static string Paint(GeometryModel model, SketchMode mode, Size size,
                            out double scale, out double worldTop, out int padTop)
        {
            scale = 0.0; worldTop = 0.0; padTop = 0;
            using (GeometrySketch sketch = new GeometrySketch { Mode = mode })
            using (Bitmap bmp = new Bitmap(size.Width, size.Height))
            {
                sketch.Bounds = new Rectangle(Point.Empty, size);
                sketch.SetModel(model);
                MethodInfo paint = typeof(GeometrySketch).GetMethod(
                    "OnPaint", BindingFlags.Instance | BindingFlags.NonPublic);
                if (paint == null)
                {
                    throw new InvalidOperationException("не нашёлся GeometrySketch.OnPaint");
                }

                using (Graphics g = Graphics.FromImage(bmp))
                using (PaintEventArgs e = new PaintEventArgs(g, new Rectangle(Point.Empty, size)))
                {
                    paint.Invoke(sketch, new object[] { e });
                }

                scale = (double)Field("scale").GetValue(sketch);
                worldTop = (double)Field("worldTop").GetValue(sketch);
                padTop = (int)Field("padTop").GetValue(sketch);
                if (!(scale > 0.0))
                {
                    return null;
                }

                return ShaBitmap(bmp);
            }
        }

        static FieldInfo Field(string name)
        {
            FieldInfo f = typeof(GeometrySketch).GetField(
                name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (f == null)
            {
                throw new InvalidOperationException("не нашлось поле GeometrySketch." + name);
            }

            return f;
        }

        static string ShaBitmap(Bitmap bmp)
        {
            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png);
                using (SHA256 sha = SHA256.Create())
                {
                    return BitConverter.ToString(sha.ComputeHash(ms.ToArray()))
                                       .Replace("-", "").Substring(0, 16).ToLowerInvariant();
                }
            }
        }

        static string Sha(string text)
        {
            using (SHA256 sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text)))
                                   .Replace("-", "").Substring(0, 16).ToLowerInvariant();
            }
        }

        /// <summary>Число печатается точкой и без группировки разрядов — правило Amber 05.09.2026.</summary>
        static string Num(double value)
        {
            return value.ToString("F3", CultureInfo.InvariantCulture);
        }
    }
}
