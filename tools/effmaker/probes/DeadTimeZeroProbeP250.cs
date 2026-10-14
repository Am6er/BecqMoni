using BecquerelMonitor;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace DeadTimeZeroProbeP250
{
    /// <summary>
    /// (`A319`, полоса П250, 14.10.2026) Прибор, который сам мерит живое время и
    /// отдаёт `InputDeviceConfig.DeadTime() = 0`. Решение Amber 13.10.2026
    /// вопросником, дословно: «Чинить ночью так (Рекомендую)» — меню «Поправка на
    /// мёртвое время» при нуле живое время не трогает и говорит, что поправка не
    /// нужна; панель скорости счёта при нуле считает мёртвое время по живому и
    /// реальному времени спектра, когда живое меньше реального.
    ///
    /// Безоконно, на построенных `ResultData` и конфигурациях приборов, мерит три
    /// вещи:
    ///   ПАНЕЛЬ  — `CountsRateManager.GetDeadTime` (её зовёт `MainForm.ShowCountsRate`);
    ///   МЕНЮ    — `CountsRateManager.ApplyDeadTimeCorrection` (решающая часть
    ///             `MainForm.ApplyDeadTimeCorrectionStripMenuItem_Click`);
    ///   ДИАЛОГ  — `SelectDeviceDialog.PreselectGuid` (что диалог предложит первым).
    ///
    /// ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: исходник собирается и против приложения ДО
    /// правки — два новых метода берутся отражением. На прежнем приложении проба
    /// ОБЯЗАНА отказать: панель даёт 0 % там, где по времени спектра 30 %, а
    /// методов меню и диалога нет вовсе. Плечи приборов с τ ≠ 0 печатают число
    /// формой «R», чтобы прогоны до и после сравнивались построчно (байт в байт).
    ///
    ///     DeadTimeZeroProbeP250.exe        код 0 — всё сошлось, 1 — отказ
    /// </summary>
    static class Program
    {
        static int failures;

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

            MethodInfo apply = typeof(CountsRateManager).GetMethod("ApplyDeadTimeCorrection",
                BindingFlags.Public | BindingFlags.Static);
            MethodInfo preselect = typeof(SelectDeviceDialog).GetMethod("PreselectGuid",
                BindingFlags.Public | BindingFlags.Static);
            Console.WriteLine("приложение: " + typeof(CountsRateManager).Assembly.Location);
            Console.WriteLine("метод меню ApplyDeadTimeCorrection: " + (apply != null ? "есть" : "НЕТ (приложение до правки)"));
            Console.WriteLine("метод диалога PreselectGuid: " + (preselect != null ? "есть" : "НЕТ (приложение до правки)"));
            Console.WriteLine();

            Panel();
            Menu(apply);
            Dialog(preselect);
            Rate();
            StreamingRate();

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ИТОГ: СОШЛОСЬ" : "ИТОГ: ОТКАЗ, расхождений " + failures.ToString(CultureInfo.InvariantCulture));
            return failures == 0 ? 0 : 1;
        }

        // ---------------------------------------------------------------- ПАНЕЛЬ

        static void Panel()
        {
            Console.WriteLine("== ПАНЕЛЬ: CountsRateManager.GetDeadTime, %");
            CountsRateManager mgr = new CountsRateManager();

            // τ = 0: доля по времени СПЕКТРА, история скорости счёта не участвует.
            // Положительный контроль — первое плечо: до правки 0, после 30.
            ZeroArm(mgr, "AmplitudaSerial, живое 70 из 100 (A319: блок ≈30 %)", new AmplitudaSerialDeviceConfig(), 100.0, 70.0, 30.0);
            ZeroArm(mgr, "AmplitudaSerial, живое = реальному", new AmplitudaSerialDeviceConfig(), 100.0, 100.0, 0.0);
            ZeroArm(mgr, "AmplitudaSerial, живого нет (0)", new AmplitudaSerialDeviceConfig(), 100.0, 0.0, 0.0);
            ZeroArm(mgr, "AmplitudaSerial, живое > реального (порча)", new AmplitudaSerialDeviceConfig(), 100.0, 101.0, 0.0);
            ZeroArm(mgr, "AmplitudaSerial, реального нет (0)", new AmplitudaSerialDeviceConfig(), 0.0, 0.0, 0.0);
            ZeroArm(mgr, "SerialInput (τ = 0), живое 40 из 50", new SerialInputDeviceConfig(), 50.0, 40.0, 20.0);
            AtomSpectraDeviceConfig atomZero = new AtomSpectraDeviceConfig();
            atomZero.DeadTimeValue = 0.0;
            ZeroArm(mgr, "AtomSpectra с τ = 0, живое = реальному (так его пишет OnTimer)", atomZero, 100.0, 100.0, 0.0);

            // Конфигурации нет — прежний ноль.
            ResultData noConfig = Build(null, 100.0, 70.0, 0, 0);
            Check("нет конфигурации прибора", mgr.GetDeadTime(noConfig, 3.0), 0.0);

            // τ ≠ 0: прежняя формула на окне, байт в байт. Спектр нарочно с живым
            // 70 из 100 — чтобы видеть, что путь спектра этим приборам не включается.
            AmplitudaUsbDeviceConfig usb = new AmplitudaUsbDeviceConfig();
            ObsidianDeviceConfig obsidian = new ObsidianDeviceConfig();
            AudioInputDeviceConfig audio = new AudioInputDeviceConfig();
            AtomSpectraDeviceConfig atom = new AtomSpectraDeviceConfig();
            atom.DeadTimeValue = 3.0E-06;
            InputDeviceConfig[] nonZero = { usb, obsidian, audio, atom };
            string[] names = { "AmplitudaUsb 14 мкс", "Obsidian 5 мкс", "AudioInput (умолчание)", "AtomSpectra 3 мкс" };
            double[] rates = { 500.0, 20000.0 };
            double[] windows = { 1.0, 3.0, 10.0, 120.0 };
            for (int k = 0; k < nonZero.Length; k++)
            {
                double tau = nonZero[k].DeadTime();
                foreach (double rate in rates)
                {
                    ResultData rd = Build(nonZero[k], 100.0, 70.0, rate, 130);
                    foreach (double window in windows)
                    {
                        double got = mgr.GetDeadTime(rd, window);
                        // Прежний текст GetDeadTime (до A319), переписанный сюда дословно.
                        double live = BecquerelMonitor.Utils.LiveTime.Calculate2(window, mgr.GetCPS(rd, window), tau);
                        double expected = live != 0 ? 100.0 * (window - live) / window : 0;
                        Check(string.Format(CultureInfo.InvariantCulture, "τ≠0 {0}, τ={1:R} с, {2} имп/с, окно {3} с",
                            names[k], tau, rate, window), got, expected);
                    }
                }
            }
            Console.WriteLine();
        }

        static void ZeroArm(CountsRateManager mgr, string name, InputDeviceConfig cfg, double real, double live, double expected)
        {
            // История скорости счёта заполнена, как во время набора: путь τ = 0 обязан её не читать.
            ResultData rd = Build(cfg, real, live, 2000.0, 130);
            Check("τ=0 " + name + ", окно 3 с", mgr.GetDeadTime(rd, 3.0), expected);
            Check("τ=0 " + name + ", окно 120 с", mgr.GetDeadTime(rd, 120.0), expected);
        }

        // ---------------------------------------------------------------- МЕНЮ

        static void Menu(MethodInfo apply)
        {
            Console.WriteLine("== МЕНЮ: CountsRateManager.ApplyDeadTimeCorrection — живое время после");
            // Что писал прежний обработчик меню: Calculate(T, N, DeadTime()). При τ = 0 —
            // ровно реальное время, то есть стирание живого времени блока.
            double oldWrite = BecquerelMonitor.Utils.LiveTime.Calculate(100.0, 123456, 0.0);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "   справка: прежний обработчик при τ = 0 записал бы живое {0:R} с вместо 70 (Calculate(100, 123456, 0))", oldWrite));

            if (apply == null)
            {
                Fail("метода ApplyDeadTimeCorrection нет — меню не мерится (приложение до правки)");
                Console.WriteLine();
                return;
            }

            MenuArm(apply, "AmplitudaSerial (τ = 0): живое НЕ трогается", new AmplitudaSerialDeviceConfig(), false, 70.0);
            MenuArm(apply, "SerialInput (τ = 0): живое НЕ трогается", new SerialInputDeviceConfig(), false, 70.0);
            AtomSpectraDeviceConfig atomZero = new AtomSpectraDeviceConfig();
            MenuArm(apply, "AtomSpectra с τ = 0: живое НЕ трогается", atomZero, false, 70.0);

            InputDeviceConfig[] nonZero = { new AmplitudaUsbDeviceConfig(), new ObsidianDeviceConfig(), new AudioInputDeviceConfig() };
            string[] names = { "AmplitudaUsb 14 мкс", "Obsidian 5 мкс", "AudioInput (умолчание)" };
            for (int k = 0; k < nonZero.Length; k++)
            {
                double expected = BecquerelMonitor.Utils.LiveTime.Calculate(100.0, 123456, nonZero[k].DeadTime());
                MenuArm(apply, names[k] + ": как прежде, Calculate(T, N, τ)", nonZero[k], true, expected);
            }

            // Конфигурации нет — ничего не трогается.
            EnergySpectrum s = new EnergySpectrum();
            s.MeasurementTime = 100.0; s.LiveTime = 70.0; s.TotalPulseCount = 123456;
            bool r = (bool)apply.Invoke(null, new object[] { s, null });
            Check("нет конфигурации: возврат false", r ? 1.0 : 0.0, 0.0);
            Check("нет конфигурации: живое", s.LiveTime, 70.0);
            Console.WriteLine();
        }

        static void MenuArm(MethodInfo apply, string name, InputDeviceConfig cfg, bool expectedApplied, double expectedLive)
        {
            EnergySpectrum s = new EnergySpectrum();
            s.MeasurementTime = 100.0;
            s.LiveTime = 70.0;
            s.TotalPulseCount = 123456;
            bool applied = (bool)apply.Invoke(null, new object[] { s, cfg });
            Check(name + " — возврат (1 = пересчитано)", applied ? 1.0 : 0.0, expectedApplied ? 1.0 : 0.0);
            Check(name + " — живое, с", s.LiveTime, expectedLive);
            Check(name + " — реальное не тронуто, с", s.MeasurementTime, 100.0);
        }

        // ---------------------------------------------------------------- ДИАЛОГ

        static void Dialog(MethodInfo preselect)
        {
            Console.WriteLine("== ДИАЛОГ: SelectDeviceDialog.PreselectGuid — что предложено первым");
            if (preselect == null)
            {
                Fail("метода PreselectGuid нет — до правки диалог предлагал конфигурацию документа только при τ ≠ 0");
                return;
            }
            DeviceConfigInfo serial = new DeviceConfigInfo();
            serial.InputDeviceConfig = new AmplitudaSerialDeviceConfig();
            DeviceConfigInfo obsidian = new DeviceConfigInfo();
            obsidian.InputDeviceConfig = new ObsidianDeviceConfig();
            Dictionary<string, DeviceConfigInfo> map = new Dictionary<string, DeviceConfigInfo>();
            map["guid-serial"] = serial;
            map["guid-obsidian"] = obsidian;

            DialogArm(preselect, "документ AmplitudaSerial (τ = 0) → своя конфигурация", Ref("guid-serial"), map, "guid-serial");
            DialogArm(preselect, "документ Obsidian (τ ≠ 0) → своя, как прежде", Ref("guid-obsidian"), map, "guid-obsidian");
            DialogArm(preselect, "конфигурации документа нет в списке → нет предвыбора", Ref("guid-absent"), map, null);
            DialogArm(preselect, "ссылки нет → нет предвыбора", null, map, null);
            DialogArm(preselect, "у ссылки нет GUID → нет предвыбора", Ref(null), map, null);
        }

        static DeviceConfigReference Ref(string guid)
        {
            DeviceConfigReference r = new DeviceConfigReference();
            r.Guid = guid;
            return r;
        }

        static void DialogArm(MethodInfo preselect, string name, DeviceConfigReference reference,
                              Dictionary<string, DeviceConfigInfo> map, string expected)
        {
            string got = (string)preselect.Invoke(null, new object[] { reference, map });
            bool ok = string.Equals(got, expected, StringComparison.Ordinal);
            if (!ok) failures++;
            Console.WriteLine("   {0}  {1}: {2} (ждали {3})", ok ? "ок   " : "ОТКАЗ", name, got ?? "null", expected ?? "null");
        }

        // ---------------------------------------------------------------- СКОРОСТЬ

        /// <summary>
        /// (П250, находка ревизии 13.10.2026) Скорость счёта панели у прибора со
        /// ступенчатыми данными. Модель `AmplitudaSerial`: блок отдаёт отсчёты раз в
        /// цикл 5.5 ± 0.1 с (опрос 3 с + чтение 2.5 с, `AmplitudaSerialAcquisition`:
        /// следующее чтение — через период опроса после конца прежнего), число
        /// отсчётов в чтении — ровно скорость × момент прихода; история пишется
        /// настоящим <c>AppendResultData</c> раз в 1.00–1.15 с (такт 1000 окна),
        /// реальное время непрерывно. Скорость берётся на каждом отсчёте истории
        /// после 30 с, как её берёт панель, и делится на истинную.
        ///
        /// Два плеча на ОДНИХ данных: с контроллером `AmplitudaSerial`
        /// (<c>IDeviceLiveTimeSource</c>) — то, что увидит человек; без контроллера —
        /// прежний расчёт, он ОБЯЗАН мигать (0 и ≈1.8), иначе модель не воспроизводит
        /// дефект и замер ничего не мерит.
        /// </summary>
        static void Rate()
        {
            Console.WriteLine("== СКОРОСТЬ: GetCPS / GetSpecEffRatio прибора со ступенчатыми данными, отношение к истинной");
            foreach (double window in new double[] { 3.0, 30.0 })
            {
                RateStats with = Simulate(window, true);
                RateStats without = Simulate(window, false);
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "   окно {0} с, БЕЗ контроллера (прежний расчёт): {1}", window, without));
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "   окно {0} с, С контроллером AmplitudaSerial:    {1}", window, with));

                // Контроль модели: прежний расчёт на тех же данных мигает.
                bool flickers = window == 3.0 ? without.Zeros > 0 && without.Max > 1.5 : true;
                Verdict(flickers, string.Format(CultureInfo.InvariantCulture,
                    "контроль модели: прежний расчёт при окне {0} с мигает (нули и > 1.5)", window));

                double lo = window == 3.0 ? 0.75 : 0.95;
                double hi = window == 3.0 ? 1.35 : 1.05;
                Verdict(with.Zeros == 0, string.Format(CultureInfo.InvariantCulture,
                    "окно {0} с: ни одного нуля скорости (было {1} из {2})", window, without.Zeros, without.N));
                Verdict(with.Min >= lo && with.Max <= hi, string.Format(CultureInfo.InvariantCulture,
                    "окно {0} с: отношение в [{1}; {2}] — мин {3:F3}, макс {4:F3}", window, lo, hi, with.Min, with.Max));
                Verdict(Math.Abs(with.Mean - 1.0) <= 0.03, string.Format(CultureInfo.InvariantCulture,
                    "окно {0} с: среднее отношение {1:F4} в пределах 3 %", window, with.Mean));
                Verdict(with.EffZeros == 0 && with.EffMin >= 1 && with.EffMax <= 2, string.Format(CultureInfo.InvariantCulture,
                    "окно {0} с: доля неучтённых (2 %) — {1}…{2} %, нулей {3} (прежний: {4}…{5} %, нулей {6})",
                    window, with.EffMin, with.EffMax, with.EffZeros, without.EffMin, without.EffMax, without.EffZeros));
                Verdict(with.StaleCps == 0.0, string.Format(CultureInfo.InvariantCulture,
                    "окно {0} с: блок замолчал на 60 с — скорость {1:R} (ждали 0, как прежде)", window, with.StaleCps));
            }
            Console.WriteLine();
        }

        sealed class RateStats
        {
            public int N, Zeros, EffZeros, EffMin = int.MaxValue, EffMax = int.MinValue;
            public double Min = double.MaxValue, Max = double.MinValue, Sum;
            public double StaleCps = double.NaN;
            public double Mean { get { return N > 0 ? Sum / N : double.NaN; } }
            public override string ToString()
            {
                return string.Format(CultureInfo.InvariantCulture,
                    "n {0}, нулей {1}, мин {2:F3}, макс {3:F3}, среднее {4:F4}", N, Zeros, Min, Max, Mean);
            }
        }

        static RateStats Simulate(double window, bool stepwiseController)
        {
            const double rate = 1000.0;
            CountsRateManager mgr = new CountsRateManager();
            ResultData rd = new ResultData();
            DeviceConfigInfo info = new DeviceConfigInfo();
            info.InputDeviceConfig = new AmplitudaSerialDeviceConfig();
            rd.DeviceConfig = info;
            if (stepwiseController)
            {
                MeasurementController mc = new MeasurementController(null, rd);
                mc.DeviceController = new AmplitudaSerialDeviceController();
                rd.MeasurementController = mc;
            }
            rd.ResultDataStatus.Recording = true;

            uint seed = 20261014;
            double t = 0;
            double nextArrival = 5500.0 + Jitter(ref seed, 100.0);
            long counts = 0;
            RateStats s = new RateStats();
            while (t < 400000.0)
            {
                t += 1000.0 + 150.0 * Uniform(ref seed);
                while (nextArrival <= t)
                {
                    counts = (long)(rate * nextArrival / 1000.0);
                    nextArrival += 5500.0 + Jitter(ref seed, 100.0);
                }
                Feed(mgr, rd, counts, t);
                if (t < 30000.0) continue;
                double ratio = mgr.GetCPS(rd, window) / rate;
                int eff = mgr.GetSpecEffRatio(rd, window);
                s.N++;
                s.Sum += ratio;
                if (ratio == 0.0) s.Zeros++;
                s.Min = Math.Min(s.Min, ratio);
                s.Max = Math.Max(s.Max, ratio);
                if (eff == 0) s.EffZeros++;
                s.EffMin = Math.Min(s.EffMin, eff);
                s.EffMax = Math.Max(s.EffMax, eff);
            }
            // Блок замолчал: отсчёты не приходят 60 с, история пишется дальше.
            double silentUntil = t + 60000.0;
            while (t < silentUntil)
            {
                t += 1000.0 + 150.0 * Uniform(ref seed);
                Feed(mgr, rd, counts, t);
            }
            s.StaleCps = mgr.GetCPS(rd, window);
            return s;
        }

        static void Feed(CountsRateManager mgr, ResultData rd, long counts, double tMs)
        {
            rd.EnergySpectrum.TotalPulseCount = counts;
            rd.EnergySpectrum.ValidPulseCount = counts - counts / 50;      // 2 % неучтённых
            rd.ResultDataStatus.ElapsedTime = TimeSpan.FromTicks((long)(tMs * 10000.0));
            mgr.AppendResultData(rd);
        }

        static double Uniform(ref uint seed)
        {
            seed = seed * 1664525u + 1013904223u;
            return (seed >> 8) / 16777216.0;
        }

        static double Jitter(ref uint seed, double amplitude)
        {
            return amplitude * (2.0 * Uniform(ref seed) - 1.0);
        }

        /// <summary>
        /// Приборы с ПОТОКОВЫМИ данными — прежний расчёт, байт в байт: без
        /// контроллера и с контроллером `AmplitudaUsb` (не <c>IDeviceLiveTimeSource</c>).
        /// Числа печатаются формой «R» и сличаются построчно с прогоном на
        /// приложении до правки (строки «поток»).
        /// </summary>
        static void StreamingRate()
        {
            Console.WriteLine("== СКОРОСТЬ: приборы с потоковыми данными — прежний расчёт (сличать строки «поток» до/после)");
            foreach (bool usb in new bool[] { false, true })
            {
                CountsRateManager mgr = new CountsRateManager();
                ResultData rd = new ResultData();
                DeviceConfigInfo info = new DeviceConfigInfo();
                info.InputDeviceConfig = new AmplitudaUsbDeviceConfig();
                rd.DeviceConfig = info;
                if (usb)
                {
                    MeasurementController mc = new MeasurementController(null, rd);
                    mc.DeviceController = new AmplitudaUsbDeviceController();
                    rd.MeasurementController = mc;
                }
                rd.ResultDataStatus.Recording = true;
                uint seed = 7;
                double t = 0;
                for (int k = 0; k < 150; k++)
                {
                    t += 1000.0 + 150.0 * Uniform(ref seed);
                    long counts = (long)(1234.5 * t / 1000.0 + 300.0 * Uniform(ref seed));
                    Feed(mgr, rd, counts, t);
                }
                foreach (double window in new double[] { 1.0, 3.0, 10.0, 120.0 })
                {
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "   поток {0}, окно {1} с: cps {2:R}, неучтённых {3} %, мёртвое {4:R} %",
                        usb ? "AmplitudaUsb-контроллер" : "без контроллера", window,
                        mgr.GetCPS(rd, window), mgr.GetSpecEffRatio(rd, window), mgr.GetDeadTime(rd, window)));
                }
            }
            Console.WriteLine();
        }

        static void Verdict(bool ok, string text)
        {
            if (!ok) failures++;
            Console.WriteLine("   {0}  {1}", ok ? "ок   " : "ОТКАЗ", text);
        }

        // ---------------------------------------------------------------- общее

        /// <summary>
        /// Документ: конфигурация прибора, времена спектра и история скорости счёта —
        /// <paramref name="points"/> отсчётов раз в секунду при <paramref name="rate"/> имп/с.
        /// </summary>
        static ResultData Build(InputDeviceConfig cfg, double real, double live, double rate, int points)
        {
            ResultData rd = new ResultData();
            if (cfg != null)
            {
                DeviceConfigInfo info = new DeviceConfigInfo();
                info.InputDeviceConfig = cfg;
                rd.DeviceConfig = info;
            }
            rd.EnergySpectrum.MeasurementTime = real;
            rd.EnergySpectrum.LiveTime = live;
            rd.EnergySpectrum.TotalPulseCount = (long)(rate * real);
            for (int i = 1; i <= points; i++)
            {
                rd.CountRates.Add(new CountRate((long)(rate * i), 0, 1000.0 * i));
            }
            return rd;
        }

        static void Check(string name, double got, double expected)
        {
            // Сравнение БИТОВОЕ: плечи τ ≠ 0 обязаны дать прежнее число, а не близкое.
            bool ok = BitConverter.DoubleToInt64Bits(got) == BitConverter.DoubleToInt64Bits(expected);
            if (!ok) failures++;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "   {0}  {1}: {2:R} (ждали {3:R})",
                ok ? "ок   " : "ОТКАЗ", name, got, expected));
        }

        static void Fail(string text)
        {
            failures++;
            Console.WriteLine("   ОТКАЗ  " + text);
        }
    }
}
