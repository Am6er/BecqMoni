// Проба полосы П313 (15.10.2026): ПОРОГ КОРРЕЛЯЦИИ ЗВУКОВОГО ВХОДА ПЕРЕЖИВАЕТ «ОТКРЫТЬ → СОХРАНИТЬ».
//
// Окно звукового прибора (`AudioInputDeviceForm`) хранит порог формы импульса
// (`PRAHomageMethodConfig.PulseThreshold`) как n/100 (`SaveFormContents`: целое поле / 100.0),
// а показывал его `(int)(PulseThreshold * 100.0)` — ОТСЕЧЕНИЕМ. Для n = 29, 57, 58 произведение
// выходит чуть меньше n (0.57·100 = 56.99999999999999): поле показывало 56, и первое же
// «Сохранить» писало порог 0.56. Каждое открытие окна настроек — минус процент, молча.
//
// НАСТОЯЩАЯ форма без окна и без устройства: `new AudioInputDeviceForm()` (конструктор без
// параметров — только InitializeComponent), список устройств — пустой (поле отражением),
// `LoadFormContents(конфиг с порогом n/100)` → `SaveFormContents(новый конфиг)` → порог обязан
// вернуться ровно n/100, а поле показать n. Перебор n = 0…100.
//
// Положительный контроль: проба сама считает, какие n отсечение портит (тем же выражением
// `(int)(n/100.0*100.0)`), — сборка ДО обязана испортить РОВНО их, иначе замер слеп.
//
// Коды: 0 — все 101 порог пережили круг (сборка ПОСЛЕ); 1 — испорчены ровно предсказанные
// (сборка ДО); 3 — испорчено не то, что предсказано (замер слеп) или форма не сохранила;
// 5 — обрыв.
//
//   P313PulseThresholdFormProbe.exe    (из каталога, где лежит BecquerelMonitor.exe)
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using BecquerelMonitor;
using WinMM;

static class P313PulseThresholdFormProbe
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [STAThread]
    static int Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            Console.WriteLine("приложение: " + typeof(AudioInputDeviceForm).Assembly.Location);
            List<int> predicted = Enumerable.Range(0, 101).Where(n => (int)(n / 100.0 * 100.0) != n).ToList();
            Console.WriteLine("отсечение портит (предсказание пробы): " + string.Join(", ", predicted));

            AudioInputDeviceForm form = new AudioInputDeviceForm();
            typeof(AudioInputDeviceForm).GetField("waveDeviceList", F)
                .SetValue(form, new ReadOnlyCollection<WaveInDeviceCaps>(new List<WaveInDeviceCaps>()));
            MaskedTextBox box = (MaskedTextBox)typeof(AudioInputDeviceForm).GetField("maskedTextBox2", F).GetValue(form);

            List<int> spoiled = new List<int>();
            int saveFailed = 0;
            foreach (int n in Enumerable.Range(0, 101))
            {
                AudioInputDeviceConfig src = new AudioInputDeviceConfig();
                src.AudioInputDevice = new WaveInDeviceCaps { DeviceId = 0, Name = "p313" };
                ((PRAHomageMethodConfig)src.PulseDetectionMethodConfig).PulseThreshold = n / 100.0;
                form.LoadFormContents(src);
                string shown = box.Text;
                AudioInputDeviceConfig dst = new AudioInputDeviceConfig();
                if (!form.SaveFormContents(dst))
                {
                    saveFailed++;
                    continue;
                }
                double back = ((PRAHomageMethodConfig)dst.PulseDetectionMethodConfig).PulseThreshold;
                if (back != n / 100.0 || shown != n.ToString(Inv))
                {
                    spoiled.Add(n);
                    Console.WriteLine(string.Format(Inv, "  порог {0} %: поле показало «{1}», сохранено {2}", n, shown, back.ToString("R", Inv)));
                }
            }
            Console.WriteLine(string.Format(Inv, "испорчено {0} из 101; SaveFormContents отказал {1} раз", spoiled.Count, saveFailed));
            if (saveFailed > 0)
            {
                Console.WriteLine("⛔ ИТОГ: форма не сохранила — замер негоден (код 3)");
                return 3;
            }
            if (spoiled.Count == 0)
            {
                Console.WriteLine("ИТОГ: все пороги пережили «открыть → сохранить» (код 0)");
                return 0;
            }
            if (spoiled.SequenceEqual(predicted))
            {
                Console.WriteLine("⛔ ИТОГ: ДЕФЕКТ — испорчены ровно предсказанные отсечением пороги (код 1; так обязана отвечать сборка ДО П313)");
                return 1;
            }
            Console.WriteLine("⛔ ИТОГ: испорчено не то, что предсказано — замер слеп (код 3)");
            return 3;
        }
        catch (Exception ex)
        {
            Console.WriteLine("⛔ ОБРЫВ: " + ex);
            return 5;
        }
    }
}
