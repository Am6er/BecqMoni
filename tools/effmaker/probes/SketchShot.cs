using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace SketchShot
{
    /// <summary>
    /// Снимок чертежей редактора геометрии прямо в PNG, без захвата рабочего
    /// стола: экран перекрывают чужие окна, а проверять надо отрисовку.
    ///
    ///   sketchshot &lt;файл .in&gt; &lt;куда.png&gt; [ключ подсветки]
    /// </summary>
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("sketchshot <файл .in> <куда.png> [ключ]");
                return 1;
            }

            Application.EnableVisualStyles();
            GeometryModel model = GeometryModel.Load(args[0]);
            string key = args.Length > 2 ? args[2] : null;

            using (Bitmap bmp = new Bitmap(840, 460))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                Draw(g, model, GeometrySketch.SketchMode.Detector, new Rectangle(0, 0, 420, 460), key);
                Draw(g, model, GeometrySketch.SketchMode.Source, new Rectangle(420, 0, 420, 460), key);
                bmp.Save(args[1], System.Drawing.Imaging.ImageFormat.Png);
            }

            Console.WriteLine("записано: {0}", args[1]);
            return 0;
        }

        static void Draw(Graphics g, GeometryModel model, GeometrySketch.SketchMode mode,
                         Rectangle where, string key)
        {
            using (GeometrySketch sketch = new GeometrySketch { Mode = mode })
            {
                sketch.Bounds = new Rectangle(0, 0, where.Width, where.Height);
                sketch.SetModel(model);
                sketch.HighlightKey = key;
                using (Bitmap part = new Bitmap(where.Width, where.Height))
                {
                    // OnPaint защищён — зовём его тем же способом, что и WinForms.
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
