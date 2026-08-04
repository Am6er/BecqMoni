using BecquerelMonitor;
using BecquerelMonitor.Utils;
using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace ScoutProbe
{
    /// <summary>
    /// Разведка файла спектра и выбиратель замены.
    ///
    /// `SpectrumScout` отвечает на один вопрос: понадобится ли этому спектру
    /// конфигурация прибора и на какую он ссылается. Ответ нужен ДО долгого
    /// прогона, поэтому файл не десериализуется, а читается потоком с
    /// пропуском рядов отсчётов. Ошибиться тут легко и незаметно: ответить
    /// «своя калибровка есть», приняв за неё чужую вложенную, — и вопрос не
    /// задастся, а спектр молча выпадет уже в прогоне.
    ///
    ///     scoutprobe &lt;каталог со спектрами&gt;
    ///
    /// Проверяется:
    ///
    /// 1. КОРПУС целиком: у всех 69 спектров калибровка ПШПВ своя, и разведка
    ///    обязана сказать это про каждый. Ссылка на прибор при этом читается —
    ///    пустых Guid быть не должно;
    /// 2. СПЕКТР БЕЗ КАЛИБРОВКИ: тот же файл с вырезанным элементом — разведка
    ///    обязана попросить прибор и назвать его Guid;
    /// 3. СПЕКТР БЕЗ ССЫЛКИ на прибор — просит прибор, Guid пустой;
    /// 4. МУСОР вместо файла и отсутствующий файл — не исключение, а «нет».
    /// 5. ВЫБИРАТЕЛЬ: список доезжает до выпадающего, «Выбрать» на пустом
    ///    списке недоступна.
    ///
    /// Ожидание: «ВСЕ СОШЛИСЬ».
    /// </summary>
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            if (args.Length < 1)
            {
                Console.WriteLine("scoutprobe <каталог со спектрами>");
                return 2;
            }

            int bad = 0;
            string[] files = Directory.GetFiles(args[0], "*.xml");
            Console.WriteLine("=== корпус: {0} файлов ===", files.Length);
            int needs = 0;
            int noGuid = 0;
            string sample = null;
            foreach (string file in files)
            {
                string guid;
                if (SpectrumScout.NeedsDeviceConfig(file, out guid))
                {
                    needs++;
                    Console.WriteLine("  !! {0}: калибровки ПШПВ не видно", Path.GetFileName(file));
                }

                if (guid.Length == 0)
                {
                    noGuid++;
                    Console.WriteLine("  !! {0}: ссылки на прибор не видно", Path.GetFileName(file));
                }
                else if (sample == null)
                {
                    sample = file;
                }
            }

            bad += Same("спектров без своей калибровки ПШПВ", 0, needs);
            bad += Same("спектров без ссылки на прибор", 0, noGuid);
            if (sample == null)
            {
                Console.WriteLine("нечем продолжать: в каталоге нет пригодного спектра");
                return 1;
            }

            Console.WriteLine();
            Console.WriteLine("=== подделки на основе {0} ===", Path.GetFileName(sample));
            string text = File.ReadAllText(sample);
            string reference;
            SpectrumScout.NeedsDeviceConfig(sample, out reference);
            Console.WriteLine("  ссылка исходного файла: {0}", reference);

            string dir = Path.Combine(Path.GetTempPath(), "scoutprobe");
            Directory.CreateDirectory(dir);

            // Калибровка вырезана целиком, вместе с обёрткой.
            string cut = Cut(text, "<SqrtFwhmCalibration>", "</SqrtFwhmCalibration>");
            cut = Cut(cut, "<SimpleSqrtFwhmCalibration>", "</SimpleSqrtFwhmCalibration>");
            bad += Case(dir, "без калибровки ПШПВ", cut, true, reference);

            // Ссылка на прибор вырезана: спрашивать прибор надо, а называть
            // нечего.
            string noref = Cut(cut, "<DeviceConfigReference>", "</DeviceConfigReference>");
            bad += Case(dir, "без ссылки на прибор", noref, true, "");

            // Ссылка есть, калибровка есть — прибор не нужен.
            bad += Case(dir, "всё на месте", text, false, reference);

            bad += Case(dir, "мусор вместо XML", "это не файл спектра", false, "");
            string absent = Path.Combine(dir, "нет-такого.xml");
            if (File.Exists(absent))
            {
                File.Delete(absent);
            }

            string guidOfAbsent;
            bad += Same("отсутствующий файл", false,
                        SpectrumScout.NeedsDeviceConfig(absent, out guidOfAbsent));

            Console.WriteLine();
            Console.WriteLine("=== выбиратель ===");
            bad += CheckPicker();

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : string.Format("НЕ СОШЛОСЬ: {0}", bad));
            return bad == 0 ? 0 : 1;
        }

        static int Case(string dir, string what, string body, bool expectedNeeds, string expectedGuid)
        {
            string path = Path.Combine(dir, "проба.xml");
            File.WriteAllText(path, body);
            string guid;
            bool needs = SpectrumScout.NeedsDeviceConfig(path, out guid);
            int bad = Same(what + ": нужен прибор", expectedNeeds, needs);
            bad += Same(what + ": ссылка", expectedGuid, guid);
            return bad;
        }

        static string Cut(string text, string open, string close)
        {
            int a = text.IndexOf(open, StringComparison.Ordinal);
            int b = text.IndexOf(close, StringComparison.Ordinal);
            if (a < 0 || b < 0)
            {
                return text;
            }

            return text.Substring(0, a) + text.Substring(b + close.Length);
        }

        /// <summary>
        /// Форма выбирателя без показа: ShowDialog ждал бы человека. Смотрим
        /// то, что человек увидел бы, — состав списка и доступность «Выбрать».
        /// </summary>
        static int CheckPicker()
        {
            int bad = 0;
            object[] items = { "первый", "второй", "третий" };
            using (Form form = Build("выбор", "из чего", items, items[1]))
            {
                ComboBox box = Combo(form);
                bad += Same("пунктов в списке", 3, box.Items.Count);
                bad += Same("выбран переданный", "второй", box.SelectedItem);
                bad += Same("«Выбрать» доступна", true, Accept(form).Enabled);
            }

            using (Form form = Build("выбор", "из чего", new object[0], null))
            {
                bad += Same("пустой список: пунктов", 0, Combo(form).Items.Count);
                bad += Same("пустой список: «Выбрать» недоступна", false, Accept(form).Enabled);
            }

            return bad;
        }

        static Form Build(string title, string question, object[] items, object selected)
        {
            ConstructorInfo ctor = typeof(PickOneForm).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic, null,
                new Type[] { typeof(string), typeof(string),
                             typeof(System.Collections.Generic.IEnumerable<object>), typeof(object) },
                null);
            if (ctor == null)
            {
                throw new InvalidOperationException("нет конструктора PickOneForm");
            }

            return (Form)ctor.Invoke(new object[] { title, question, items, selected });
        }

        static ComboBox Combo(Form form)
        {
            foreach (Control control in form.Controls)
            {
                ComboBox box = control as ComboBox;
                if (box != null)
                {
                    return box;
                }
            }

            throw new InvalidOperationException("на форме нет списка");
        }

        static Button Accept(Form form)
        {
            foreach (Control control in form.Controls)
            {
                Button button = control as Button;
                if (button != null && button.DialogResult == DialogResult.OK)
                {
                    return button;
                }
            }

            throw new InvalidOperationException("на форме нет кнопки «Выбрать»");
        }

        static int Same(string what, object expected, object got)
        {
            bool ok = Equals(expected, got);
            Console.WriteLine("  {0,-44} {1} {2}{3}", what, ok ? "=" : "!!",
                              Show(got), ok ? "" : string.Format(" вместо {0}", Show(expected)));
            return ok ? 0 : 1;
        }

        static string Show(object value)
        {
            string text = value == null ? "null" : value.ToString();
            return text.Length == 0 ? "«»" : text;
        }
    }
}
