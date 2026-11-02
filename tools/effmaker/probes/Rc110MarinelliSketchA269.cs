using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace Rc110MarinelliSketchA269
{
    /// <summary>
    /// (`AMBER269`, полоса rc110mar 02.11.2026) Эскиз геометрии кривой из файла прибора — для проверки
    /// глазом — и её текст `.in`: геометрия берётся из `config\device\&lt;прибор&gt;.xml` по имени кривой
    /// (тем же <see cref="XmlSerializer"/>, что читает приложение), чертёж — <see cref="GeometrySketch"/>
    /// обоих видов (детектор и источник), как <c>SketchShot</c>, текст — <see cref="GeometryWriter.Save"/>.
    /// Печатает объём и массу пробы по сцене (<see cref="GeometryScenes"/> не зовётся — только поля модели).
    ///
    ///   rc110marinellisketcha269 --device=&lt;файл прибора .xml&gt; --curve=&lt;имя кривой&gt; --png=&lt;куда.png&gt; [--in=&lt;куда.in&gt;]
    /// Коды: 0 — нарисовано; 2 — ключи/файлы.
    /// </summary>
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            string device = null, curve = null, png = null, inPath = null;
            foreach (string a in args)
            {
                if (a.StartsWith("--device=", StringComparison.Ordinal)) device = a.Substring(9);
                else if (a.StartsWith("--curve=", StringComparison.Ordinal)) curve = a.Substring(8);
                else if (a.StartsWith("--png=", StringComparison.Ordinal)) png = a.Substring(6);
                else if (a.StartsWith("--in=", StringComparison.Ordinal)) inPath = a.Substring(5);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }
            if (device == null || curve == null || png == null || !File.Exists(device))
            {
                Console.Error.WriteLine("нужны --device=<файл> --curve=<имя> --png=<файл>");
                return 2;
            }

            DeviceConfigInfo info;
            using (var stream = new FileStream(device, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                info = (DeviceConfigInfo)new XmlSerializer(typeof(DeviceConfigInfo)).Deserialize(stream);
            }
            EfficiencyConfigData eff = null;
            foreach (EfficiencyConfigData e in info.EfficiencyConfigs)
            {
                if (string.Equals(e.Name, curve, StringComparison.Ordinal)) eff = e;
            }
            if (eff == null || eff.Geometry == null)
            {
                Console.Error.WriteLine("кривой с геометрией нет: " + curve);
                return 2;
            }
            GeometryModel g = eff.Geometry;
            double rIn = 0.5 * g.MarinelliHoleDiameter + g.MarinelliHoleSideThickness;
            double rOut = 0.5 * g.MarinelliBeakerDiameter - g.MarinelliSideThickness;
            double cap = Math.Max(0.0, g.MarinelliSourceHeight - g.MarinelliHoleHeight);
            double volume = Math.PI * (rOut * rOut - rIn * rIn) * g.MarinelliSourceHeight + Math.PI * rIn * rIn * cap;
            Console.WriteLine("кривая «{0}» {1}: сцена {2}; проба {3} ρ {4} г/см³; объём пробы {5} см³, масса {6} г",
                              eff.Name, eff.Guid, g.SourceType, g.Source.Name,
                              g.Source.Density.ToString("0.#####", CultureInfo.InvariantCulture),
                              (volume / 1000.0).ToString("F2", CultureInfo.InvariantCulture),
                              (volume / 1000.0 * g.Source.Density).ToString("F1", CultureInfo.InvariantCulture));
            if (inPath != null)
            {
                GeometryWriter.Save(g, inPath);
                Console.WriteLine("текст .in: {0}", inPath);
            }

            Application.EnableVisualStyles();
            using (Bitmap bmp = new Bitmap(1200, 640))
            using (Graphics gr = Graphics.FromImage(bmp))
            {
                gr.Clear(Color.White);
                Draw(gr, g, GeometrySketch.SketchMode.Detector, new Rectangle(0, 0, 600, 640));
                Draw(gr, g, GeometrySketch.SketchMode.Source, new Rectangle(600, 0, 600, 640));
                bmp.Save(png, System.Drawing.Imaging.ImageFormat.Png);
            }
            Console.WriteLine("эскиз: {0}", png);
            return 0;
        }

        static void Draw(Graphics g, GeometryModel model, GeometrySketch.SketchMode mode, Rectangle where)
        {
            using (GeometrySketch sketch = new GeometrySketch { Mode = mode })
            {
                sketch.Bounds = new Rectangle(0, 0, where.Width, where.Height);
                sketch.SetModel(model);
                using (Bitmap part = new Bitmap(where.Width, where.Height))
                {
                    MethodInfo paint = typeof(GeometrySketch).GetMethod(
                        "OnPaint", BindingFlags.Instance | BindingFlags.NonPublic);
                    using (Graphics pg = Graphics.FromImage(part))
                    {
                        paint.Invoke(sketch, new object[] { new PaintEventArgs(pg, sketch.ClientRectangle) });
                    }
                    g.DrawImage(part, where.Location);
                }
            }
        }
    }
}
