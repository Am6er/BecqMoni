using BecquerelMonitor;
using BecquerelMonitor.Properties;
using System;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Threading;

namespace UsbExplicitSerialBusyProbeP285
{
    /// <summary>
    /// (П285, 14.10.2026) AmplitudaUSB, явно заданный серийник у двух конфигураций.
    /// Решение Amber 14.10.2026 вопросником, дословно: «Отказывать второму
    /// (Рекомендую)» — второму документу, который пускается на блок с тем же явно
    /// заданным серийником, пока блок набирает в другом документе этого приложения,
    /// — отказ с сообщением (ключ `ERRAmplitudaUsbBlockBusy`).
    ///
    /// Безоконно, отражением на собранном приложении, через НАСТОЯЩИЙ
    /// `AmplitudaUsbDeviceController.StartMeasurement`. Первый документ изображён
    /// арендой в общем реестре `AmplitudaUsbBlockLeases.Measurements` (её берёт
    /// читатель набора, П257); сам набор на подделанном блоке меряют тесты
    /// `tools/AmplitudaUsbTests` (`LeaseTests`), здесь — текст отказа en/ru и путь
    /// приложения от «Пуска» до сообщения.
    ///
    /// ⚠ Серийник — заведомо несуществующий (`P285-NO-SUCH-UNIT`): ни на одном
    /// плече проба не открывает настоящий блок. Перечень HID-устройств
    /// (`FindDevices`, открытие без прав доступа) — тот же, что у «Пуска».
    ///
    /// ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ — прогон против приложения до правки: проба ОБЯЗАНА
    /// отказать (ключа и кода `ErrorBlockBusy` нет, второй документ проходит мимо
    /// аренды к поиску блока — «не найден» вместо «занят»).
    ///
    ///     UsbExplicitSerialBusyProbeP285.exe
    ///     код 0 — всё сошлось, 1 — отказ
    /// </summary>
    static class Program
    {
        const string Serial = "P285-NO-SUCH-UNIT";
        static int failures;

        static int Main()
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

            Console.WriteLine("приложение: " + typeof(AmplitudaUsbReader).Assembly.Location);
            Console.WriteLine("Resources.Culture: " + (Resources.Culture == null ? "<null>" : Resources.Culture.Name));
            FieldInfo busyField = typeof(AmplitudaUsbReader).GetField("ErrorBlockBusy", BindingFlags.Public | BindingFlags.Static);
            int busy = busyField != null ? (int)busyField.GetRawConstantValue() : int.MinValue;
            Console.WriteLine("AmplitudaUsbReader.ErrorBlockBusy: " + (busyField != null ? busy.ToString(CultureInfo.InvariantCulture) : "НЕТ (приложение до правки)"));
            Verdict(busyField != null && busy != AmplitudaUsbReader.ErrorAllInUse && busy != AmplitudaUsbReader.ErrorDeviceNotFound
                    && busy != AmplitudaUsbReader.ErrorUnexpected && busy != AmplitudaUsbReader.ErrorSilence,
                "код отказа ErrorBlockBusy есть и не совпадает с прочими кодами читателя");
            Console.WriteLine();

            string[] cultures = { "en-US", "ru-RU" };

            Console.WriteLine("== 1. Текст ключа ERRAmplitudaUsbBlockBusy, en и ru");
            foreach (string name in cultures)
            {
                CultureInfo ui = CultureInfo.GetCultureInfo(name);
                string raw = Resources.ResourceManager.GetString("ERRAmplitudaUsbBlockBusy", ui);
                Console.WriteLine("   " + name + ": " + (raw ?? "<нет ключа>"));
                Verdict(raw != null && raw.Contains("{0}"), name + ": ключ есть, серийник подставляется местом {0}");
                if (raw == null)
                {
                    continue;
                }
                bool cyrillic = HasCyrillic(raw);
                Verdict(name == "ru-RU" ? cyrillic : !cyrillic, name + ": текст на своём языке (кириллица " + (cyrillic ? "есть" : "нет") + ")");
                if (busyField != null)
                {
                    string described = WithUi(ui, () => AmplitudaUsbDeviceController.DescribeOpenError(busy, Serial));
                    Verdict(described == string.Format(CultureInfo.InvariantCulture, raw, Serial),
                        name + ": DescribeOpenError(ErrorBlockBusy) отдаёт этот текст с серийником как есть");
                }
            }
            Console.WriteLine();

            Console.WriteLine("== 2. Блок " + Serial + " набирается в другом документе: «Пуск» второй конфигурации с тем же явным серийником");
            object firstDocument = new object();
            string key = "sn:" + Serial;
            Verdict(AmplitudaUsbBlockLeases.Measurements.TryTake(key, firstDocument, true), "первый документ держит аренду " + key);
            foreach (string name in cultures)
            {
                CultureInfo ui = CultureInfo.GetCultureInfo(name);
                ResultData second = NewDocument(Serial);
                string message = WithUi(ui, () => TryStart(second));
                bool started = lastStarted;
                Console.WriteLine("   " + name + ": " + (started ? "ПУЩЕН" : "отказ: " + message));
                string raw = Resources.ResourceManager.GetString("ERRAmplitudaUsbBlockBusy", ui);
                string expected = raw == null ? null : string.Format(CultureInfo.InvariantCulture, raw, Serial);
                Verdict(!started && expected != null && message != null && message.Contains(expected),
                    name + ": второй документ получил отказ с текстом ERRAmplitudaUsbBlockBusy");
                Verdict(!second.ResultDataStatus.Recording, name + ": набор второго документа не идёт");
                Verdict(AmplitudaUsbBlockLeases.Measurements.Count == 1 && AmplitudaUsbBlockLeases.Measurements.KeyOf(firstDocument) == key,
                    name + ": отказ не оставил аренды, первый документ держит блок (аренд "
                    + AmplitudaUsbBlockLeases.Measurements.Count.ToString(CultureInfo.InvariantCulture) + ")");
            }
            Console.WriteLine();

            Console.WriteLine("== 3. «Стоп» первого (аренда снята): второй проходит аренду к поиску блока");
            AmplitudaUsbBlockLeases.Measurements.Release(firstDocument);
            {
                CultureInfo ui = CultureInfo.GetCultureInfo("en-US");
                ResultData second = NewDocument(Serial);
                string message = WithUi(ui, () => TryStart(second));
                bool started = lastStarted;
                Console.WriteLine("   " + (started ? "ПУЩЕН" : "отказ: " + message));
                string notFound = string.Format(CultureInfo.InvariantCulture,
                    Resources.ResourceManager.GetString("ERRAmplitudaUsbNotFound", ui), " (s/n " + Serial + ")");
                string busyText = Resources.ResourceManager.GetString("ERRAmplitudaUsbBlockBusy", ui);
                Verdict(!started && message != null && message.Contains(notFound),
                    "без аренды «Пуск» дошёл до поиска блока: блока с таким серийником нет — «не найден», а не «занят»");
                Verdict(busyText == null || message == null || !message.Contains(string.Format(CultureInfo.InvariantCulture, busyText, Serial)),
                    "текста «занят» в этом отказе нет");
                Verdict(AmplitudaUsbBlockLeases.Measurements.Count == 0, "реестр пуст (аренд "
                    + AmplitudaUsbBlockLeases.Measurements.Count.ToString(CultureInfo.InvariantCulture) + ")");
            }
            Console.WriteLine();

            Console.WriteLine("== 4. «Первый найденный» — текст отказа прежний (П257)");
            foreach (string name in cultures)
            {
                CultureInfo ui = CultureInfo.GetCultureInfo(name);
                string allInUse = WithUi(ui, () => AmplitudaUsbDeviceController.DescribeOpenError(AmplitudaUsbReader.ErrorAllInUse, ""));
                Verdict(allInUse == Resources.ResourceManager.GetString("ERRAmplitudaUsbAllInUse", ui),
                    name + ": DescribeOpenError(ErrorAllInUse) = ERRAmplitudaUsbAllInUse");
            }

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ИТОГ: СОШЛОСЬ" : "ИТОГ: ОТКАЗ, расхождений " + failures.ToString(CultureInfo.InvariantCulture));
            return failures == 0 ? 0 : 1;
        }

        static bool lastStarted;

        // Headless: a refused Start throws (RefuseStart, A316); its message carries the refusal text.
        // lastStarted: whether the measurement started.
        static string TryStart(ResultData resultData)
        {
            AmplitudaUsbDeviceController controller = new AmplitudaUsbDeviceController();
            try
            {
                lastStarted = controller.StartMeasurement(resultData);
                return null;
            }
            catch (InvalidOperationException ex)
            {
                lastStarted = false;
                return ex.Message;
            }
            finally
            {
                controller.Dispose();
            }
        }

        static ResultData NewDocument(string serial)
        {
            AmplitudaUsbDeviceConfig config = new AmplitudaUsbDeviceConfig();
            config.SerialNumber = serial;
            DeviceConfigInfo info = new DeviceConfigInfo();
            info.InputDeviceConfig = config;        // Guid stays null: the controller does not consult DeviceConfigManager
            ResultData resultData = new ResultData();
            resultData.DeviceConfig = info;
            resultData.EnergySpectrum = new EnergySpectrum(1.0, 1024);
            return resultData;
        }

        static T WithUi<T>(CultureInfo ui, Func<T> action)
        {
            CultureInfo saved = Thread.CurrentThread.CurrentUICulture;
            Thread.CurrentThread.CurrentUICulture = ui;
            try
            {
                return action();
            }
            finally
            {
                Thread.CurrentThread.CurrentUICulture = saved;
            }
        }

        static bool HasCyrillic(string text)
        {
            foreach (char c in text)
            {
                if (c >= 'Ѐ' && c <= 'ӿ')
                {
                    return true;
                }
            }
            return false;
        }

        static void Verdict(bool ok, string what)
        {
            Console.WriteLine((ok ? "   [ok]    " : "   [ОТКАЗ] ") + what);
            if (!ok)
            {
                failures++;
            }
        }
    }
}
