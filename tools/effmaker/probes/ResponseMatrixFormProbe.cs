using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace ResponseMatrixFormProbe
{
    /// <summary>
    /// Форма матрицы отклика и кнопка на вкладке «Эффективность».
    ///
    /// Проверяется то, что молчит у компилятора и видно только человеку,
    /// открывшему форму:
    ///
    /// 1. **Три состояния при открытии** — нет матрицы, устарела, годна. Именно
    ///    ради этого форма и заводилась: посчитать спектр по матрице чужой
    ///    геометрии хуже, чем не посчитать вовсе, поэтому годность проверяется
    ///    по отпечатку, а не по наличию файла.
    /// 2. **Подробности у годной** — узлы, диапазон, бин, истории, размер, дата.
    /// 3. **Кнопка «Матрица отклика…» недоступна без геометрии** и на вкладке, и
    ///    в самой форме: у кривой, восстановленной по измерениям, геометрии нет
    ///    и считать не из чего.
    /// 4. **Кнопка есть на вкладке** и подписана из ресурсов.
    /// 5. **Сохранение кладёт файл туда, где его ищут,** и не трогает
    ///    конфигурацию — иначе матрица уехала бы внутрь файлов спектров.
    ///
    ///     responsematrixformprobe --geometry=X.in
    ///
    /// Ожидание: «ВСЕ СОШЛИСЬ».
    /// </summary>
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            string geometryPath = null, pngPath = null;
            foreach (string a in args)
            {
                if (a.StartsWith("--geometry=", StringComparison.Ordinal)) geometryPath = a.Substring(11);
                else if (a.StartsWith("--png=", StringComparison.Ordinal)) pngPath = a.Substring(6);
            }

            if (geometryPath == null || !File.Exists(geometryPath))
            {
                Console.Error.WriteLine("нужен --geometry=<файл .in>");
                return 2;
            }

            Application.EnableVisualStyles();
            GlobalConfigManager.GetInstance();

            GeometryModel geometry = GeometryModel.Load(geometryPath);
            var config = new EfficiencyConfigData("проба")
            {
                Guid = "probe-" + Guid.NewGuid().ToString("N"),
                Geometry = geometry
            };

            int bad = 0;

            // Файла ещё нет — состояние «не посчитана».
            ResponseMatrixStore.Delete(config.Guid);
            using (var form = new ResponseMatrixForm(config))
            {
                string state = TextOf(form, "stateLabel");
                bool ok = state == BecquerelMonitor.Properties.Resources.ResponseMatrixStateMissing
                          && Enabled(form, "computeButton")
                          && !Enabled(form, "saveButton");
                Report(ok, "нет матрицы: «{0}», «Посчитать» доступна, «Сохранить» нет", Short(state));
                bad += ok ? 0 : 1;
            }

            // Кладём годную матрицу и открываем снова.
            var options = new ResponseMatrixOptions { NodeCount = 10, Histories = 4000, BinKev = 4.0 };
            ResponseMatrix matrix = ResponseMatrixBuilder.Build(geometry, options, null,
                                                               System.Threading.CancellationToken.None);
            ResponseMatrixStore.Save(config.Guid, matrix);

            using (var form = new ResponseMatrixForm(config))
            {
                string state = TextOf(form, "stateLabel");
                string details = StringField(form, "detailsText");
                bool ok = state == BecquerelMonitor.Properties.Resources.ResponseMatrixStateValid
                          && details.Contains(options.NodeCount.ToString(CultureInfo.CurrentCulture));
                Report(ok, "годная матрица: «{0}», в подробностях {1} узлов", Short(state), options.NodeCount);
                bad += ok ? 0 : 1;
            }

            // Снимок раскладки — чтобы форму можно было посмотреть, не запуская
            // приложение целиком.
            if (pngPath != null)
            {
                using (var form = new ResponseMatrixForm(config))
                {
                    form.Show();
                    Application.DoEvents();
                    using (var bitmap = new System.Drawing.Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
                        bitmap.Save(pngPath, System.Drawing.Imaging.ImageFormat.Png);
                    }

                    form.Hide();
                }

                Console.WriteLine("снимок формы: {0}", pngPath);
            }

            // Правим геометрию — матрица обязана стать устаревшей.
            GeometryModel moved = geometry.Clone();
            moved.CrystalHeight += 1.0;
            var movedConfig = new EfficiencyConfigData("проба-2")
            {
                Guid = config.Guid,
                Geometry = moved
            };

            using (var form = new ResponseMatrixForm(movedConfig))
            {
                string state = TextOf(form, "stateLabel");
                bool ok = state == BecquerelMonitor.Properties.Resources.ResponseMatrixStateStale;
                Report(ok, "геометрию сдвинули на 1 мм: «{0}»", Short(state));
                bad += ok ? 0 : 1;
            }

            // Кривая без геометрии.
            var noGeometry = new EfficiencyConfigData("без геометрии") { Guid = "probe-nogeom" };
            using (var form = new ResponseMatrixForm(noGeometry))
            {
                string state = TextOf(form, "stateLabel");
                bool ok = state == BecquerelMonitor.Properties.Resources.ResponseMatrixNoGeometry
                          && !Enabled(form, "computeButton");
                Report(ok, "без геометрии: «{0}», «Посчитать» недоступна", Short(state));
                bad += ok ? 0 : 1;
            }

            // Файл лежит там, где его ищут, и конфигурация о нём не знает.
            string path = ResponseMatrixStore.PathOf(config.Guid);
            bool stored = File.Exists(path) && new FileInfo(path).Length > 0;
            bool configClean = !Serialized(config).Contains("Rows")
                               && !Serialized(config).Contains("rmx");
            Report(stored && configClean,
                   "файл на месте ({0:F1} КБ), в конфигурации кривой матрицы нет",
                   stored ? new FileInfo(path).Length / 1024.0 : 0.0);
            bad += (stored && configClean) ? 0 : 1;

            // Кнопка на вкладке.
            bool tabButton = HasEfficiencyTabButton();
            Report(tabButton, "на вкладке «Эффективность» есть кнопка «{0}»",
                   BecquerelMonitor.Properties.Resources.EfficiencyTabResponseMatrix);
            bad += tabButton ? 0 : 1;

            ResponseMatrixStore.Delete(config.Guid);
            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "ПРОВАЛОВ: " + bad);
            return bad == 0 ? 0 : 1;
        }

        /// <summary>
        /// Кнопка ищется по обработчику, а не по надписи: надпись переводится, а
        /// поле — нет. Само наличие поля значит, что вкладка её создаёт.
        /// </summary>
        static bool HasEfficiencyTabButton()
        {
            FieldInfo field = typeof(DeviceConfigForm).GetField(
                "efficiencyMatrixButton", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo handler = typeof(DeviceConfigForm).GetMethod(
                "efficiencyMatrixButton_Click", BindingFlags.Instance | BindingFlags.NonPublic);
            return field != null && field.FieldType == typeof(Button) && handler != null;
        }

        static string Serialized(EfficiencyConfigData config)
        {
            var serializer = new System.Xml.Serialization.XmlSerializer(typeof(EfficiencyConfigData));
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, config);
                return writer.ToString();
            }
        }

        static string StringField(Form form, string fieldName)
        {
            return Field(form, fieldName) as string ?? "";
        }

        static string TextOf(Form form, string fieldName)
        {
            var label = Field(form, fieldName) as Label;
            return label != null ? label.Text : "";
        }

        static bool Enabled(Form form, string fieldName)
        {
            var control = Field(form, fieldName) as Control;
            return control != null && control.Enabled;
        }

        static object Field(Form form, string name)
        {
            FieldInfo field = form.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            return field != null ? field.GetValue(form) : null;
        }

        static string Short(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            text = text.Replace(Environment.NewLine, " ");
            return text.Length > 46 ? text.Substring(0, 46) + "…" : text;
        }

        static void Report(bool ok, string format, params object[] args)
        {
            Console.WriteLine("[{0}] {1}", ok ? "СОШЛОСЬ" : "ПРОВАЛ  ",
                              string.Format(CultureInfo.CurrentCulture, format, args));
        }
    }
}
