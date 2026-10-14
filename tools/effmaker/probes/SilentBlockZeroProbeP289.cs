using BecquerelMonitor;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace SilentBlockZeroProbeP289
{
    /// <summary>
    /// (П289, 14.10.2026) Панель скорости счёта у прибора, который сам сообщает
    /// живое время (`IDeviceLiveTimeSource`, `AmplitudaSerial`), когда блок молчит.
    /// Решение Amber 14.10.2026 вопросником, дословно: «Взять, время шагами
    /// (Рекомендую)» — с П280 время данных идёт шагами, раз в чтение, и пока блок
    /// молчит, оно стоит; панель держала последнюю скорость до отказа набора по
    /// связи (30 с), а не падала в 0 через два окна, как сделала П250.
    ///
    /// Безоконно, на построенных `ResultData`, с ПОДМЕННЫМИ часами ПК: менеджер
    /// берётся конструктором `CountsRateManager(Func&lt;double&gt;)` через отражение,
    /// поэтому тот же исходник собирается и против приложения ДО правки (там
    /// конструктора нет — берётся прежний, часы ему не нужны).
    ///
    /// Модель `AmplitudaSerial` после П280: блок отдаёт чтение раз в цикл
    /// 5.5 ± 0.1 с; момент отсчётов — за 1.25 с до прихода на экран (середина
    /// чтения страниц); число отсчётов — ровно скорость × момент; время данных —
    /// этот момент, и между чтениями оно СТОИТ. Такт окна (`MainForm`, 1000 мс) —
    /// 1.00–1.15 с по часам ПК: `AppendResultData`, затем `GetCPS` /
    /// `GetSpecEffRatio`, как в `MainForm.ShowCountsRate`.
    ///
    /// ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ — прогон против приложения до правки: проба
    /// ОБЯЗАНА отказать на плечах «тишина» и «отказ по связи» (панель держит
    /// последнюю скорость), а строки прочих приборов (хэш) — совпасть с прогоном
    /// после правки побайтно.
    ///
    ///     SilentBlockZeroProbeP289.exe [--dump=&lt;файл строк прочих приборов&gt;]
    ///     код 0 — всё сошлось, 1 — отказ
    /// </summary>
    static class Program
    {
        static int failures;
        static double pcMs;                 // подменные часы ПК, мс
        static ConstructorInfo clockCtor;
        const double Rate = 1000.0;         // истинная скорость, имп/с
        const double TickMax = 1150.0;      // самый длинный такт окна в модели, мс

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

            string dump = null;
            foreach (string a in args)
            {
                if (a.StartsWith("--dump=", StringComparison.Ordinal)) dump = a.Substring("--dump=".Length);
            }

            clockCtor = typeof(CountsRateManager).GetConstructor(new Type[] { typeof(Func<double>) });
            Console.WriteLine("приложение: " + typeof(CountsRateManager).Assembly.Location);
            Console.WriteLine("конструктор с часами ПК: " + (clockCtor != null ? "есть" : "НЕТ (приложение до правки)"));
            Console.WriteLine();

            Console.WriteLine("== AmplitudaSerial (IDeviceLiveTimeSource), время данных шагами (П280): тишина 70 с, затем возобновление");
            Silence(3.0);
            Silence(30.0);
            Console.WriteLine();

            Console.WriteLine("== AmplitudaSerial: отказ набора по связи (Recording = false через 33 с тишины), окно 3 с");
            ConnectionLost(3.0);
            Console.WriteLine();

            Console.WriteLine("== AmplitudaSerial: «Стоп» (Recording = false), 120 с часов после — как при П250, последняя скорость");
            Stopped(3.0);
            Console.WriteLine();

            Console.WriteLine("== Справка (без приговора): на каком чтении после «Пуска» панель впервые показывает скорость, окно 3 с");
            FirstSpeed();
            Console.WriteLine();

            Console.WriteLine("== Прочие приборы (не IDeviceLiveTimeSource) — побайтно как до правки (сличать хэш до/после)");
            Others(dump);

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ИТОГ: СОШЛОСЬ" : "ИТОГ: ОТКАЗ, расхождений " + failures.ToString(CultureInfo.InvariantCulture));
            return failures == 0 ? 0 : 1;
        }

        static CountsRateManager NewManager()
        {
            if (clockCtor != null)
            {
                Func<double> clock = () => pcMs;
                return (CountsRateManager)clockCtor.Invoke(new object[] { clock });
            }
            return new CountsRateManager();
        }

        static ResultData NewSerialDoc()
        {
            ResultData rd = new ResultData();
            DeviceConfigInfo info = new DeviceConfigInfo();
            info.InputDeviceConfig = new AmplitudaSerialDeviceConfig();
            rd.DeviceConfig = info;
            MeasurementController mc = new MeasurementController(null, rd);
            mc.DeviceController = new AmplitudaSerialDeviceController();
            rd.MeasurementController = mc;
            rd.ResultDataStatus.Recording = true;
            return rd;
        }

        /// <summary>Блок по модели П280: приходы раз в цикл, момент и отсчёты на приходе.</summary>
        sealed class Block
        {
            public uint Seed = 20261014;
            public double NextArrival;       // мс по часам ПК
            public long Counts;
            public double Moment;            // время данных, мс
            public bool Pending;             // пришло чтение, ещё не отданное такту
            public double ArrivalTickPc = double.NaN;   // такт, на котором лёг последний приход

            public Block() { NextArrival = 5500.0 + Jitter(ref Seed, 100.0); }

            public void Tick(CountsRateManager mgr, ResultData rd, bool silent)
            {
                pcMs += 1000.0 + 150.0 * Uniform(ref Seed);
                if (silent)
                {
                    NextArrival = pcMs + 5500.0 + Jitter(ref Seed, 100.0);
                }
                while (NextArrival <= pcMs)
                {
                    Moment = NextArrival - 1250.0;
                    Counts = (long)(Rate * Moment / 1000.0);
                    NextArrival += 5500.0 + Jitter(ref Seed, 100.0);
                    Pending = true;
                }
                Feed(mgr, rd, Counts, Moment);
                if (Pending && rd.ResultDataStatus.Recording)
                {
                    ArrivalTickPc = pcMs;
                    Pending = false;
                }
            }
        }

        static void Normal(CountsRateManager mgr, ResultData rd, Block b, double window, double untilPc, Stats st)
        {
            while (pcMs < untilPc)
            {
                b.Tick(mgr, rd, false);
                if (pcMs < 30000.0) continue;
                st.Add(mgr.GetCPS(rd, window) / Rate, mgr.GetSpecEffRatio(rd, window));
            }
        }

        static void Silence(double window)
        {
            CountsRateManager mgr = NewManager();
            ResultData rd = NewSerialDoc();
            Block b = new Block();
            pcMs = 0;

            Stats before = new Stats();
            Normal(mgr, rd, b, window, 200000.0, before);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "   окно {0} с, набор 30…200 с: {1}", window, before));
            Verdict(before.Zeros == 0 && before.EffZeros == 0, string.Format(CultureInfo.InvariantCulture,
                "окно {0} с: во время набора ни одного нуля скорости и доли неучтённых", window));
            double lo = window == 3.0 ? 0.75 : 0.95, hi = window == 3.0 ? 1.35 : 1.05;
            Verdict(before.Min >= lo && before.Max <= hi, string.Format(CultureInfo.InvariantCulture,
                "окно {0} с: отношение к истинной в [{1}; {2}] — мин {3:F4}, макс {4:F4}", window, lo, hi, before.Min, before.Max));
            double lastShown = mgr.GetCPS(rd, window);

            // Тишина: блок не отвечает 70 с, такты окна идут, время данных стоит.
            double threshold = Threshold(rd.CountRates, window);
            double silenceEnd = pcMs + 70000.0;
            double firstZeroAge = double.NaN;
            int earlyZeros = 0, lateNonZeros = 0, ticks = 0, effLateNonZeros = 0;
            double heldMin = double.MaxValue, heldMax = double.MinValue;
            while (pcMs < silenceEnd)
            {
                b.Tick(mgr, rd, true);
                double age = pcMs - b.ArrivalTickPc;
                double cps = mgr.GetCPS(rd, window);
                int eff = mgr.GetSpecEffRatio(rd, window);
                ticks++;
                if (age <= threshold)
                {
                    if (cps == 0.0) earlyZeros++;
                    heldMin = Math.Min(heldMin, cps / Rate);
                    heldMax = Math.Max(heldMax, cps / Rate);
                }
                else
                {
                    if (cps != 0.0) lateNonZeros++;
                    if (eff != 0) effLateNonZeros++;
                    if (cps == 0.0 && double.IsNaN(firstZeroAge)) firstZeroAge = age;
                }
            }
            double endCps = mgr.GetCPS(rd, window);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "   окно {0} с, тишина 70 с: порог по истории (2·max(окно, длиннейший промежуток)) {1:F3} с; " +
                "до порога отношение {2:F4}…{3:F4}, нулей {4}; после порога не-нулей {5} из {6} тактов; " +
                "первый ноль на возрасте {7} с; в конце тишины скорость {8:R} имп/с (до тишины {9:R})",
                window, threshold / 1000.0, heldMin, heldMax, earlyZeros, lateNonZeros, ticks,
                double.IsNaN(firstZeroAge) ? "—" : (firstZeroAge / 1000.0).ToString("F3", CultureInfo.InvariantCulture),
                endCps, lastShown));
            Verdict(earlyZeros == 0, string.Format(CultureInfo.InvariantCulture,
                "окно {0} с: до порога панель держит последнюю скорость (ни одного нуля)", window));
            Verdict(!double.IsNaN(firstZeroAge) && firstZeroAge > threshold && firstZeroAge <= threshold + TickMax,
                string.Format(CultureInfo.InvariantCulture,
                "окно {0} с: блок молчит — 0 на первом такте после порога {1:F3} с (первый ноль: {2})", window, threshold / 1000.0,
                double.IsNaN(firstZeroAge) ? "НЕТ — держит последнюю скорость" : (firstZeroAge / 1000.0).ToString("F3", CultureInfo.InvariantCulture) + " с"));
            Verdict(lateNonZeros == 0 && effLateNonZeros == 0, string.Format(CultureInfo.InvariantCulture,
                "окно {0} с: после порога и до конца тишины — 0 и у скорости, и у доли неучтённых (не-нулей {1} и {2})",
                window, lateNonZeros, effLateNonZeros));

            // Возобновление: блок считал и в тишине — отсчёты = скорость × момент.
            Stats after = new Stats();
            bool firstArrivalSeen = false;
            double firstAfter = double.NaN;
            double resumeEnd = pcMs + 60000.0;
            double arrivalBefore = b.ArrivalTickPc;
            int zerosAfterArrival = 0;
            while (pcMs < resumeEnd)
            {
                b.Tick(mgr, rd, false);
                if (b.ArrivalTickPc != arrivalBefore) firstArrivalSeen = true;
                if (!firstArrivalSeen) continue;
                double cps = mgr.GetCPS(rd, window);
                if (double.IsNaN(firstAfter)) firstAfter = cps / Rate;
                if (cps == 0.0) zerosAfterArrival++;
                after.Add(cps / Rate, mgr.GetSpecEffRatio(rd, window));
            }
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "   окно {0} с, 60 с после возобновления: первый такт с данными — отношение {1:F4}; {2}", window, firstAfter, after));
            Verdict(firstAfter >= lo && firstAfter <= hi, string.Format(CultureInfo.InvariantCulture,
                "окно {0} с: первый же такт с новыми данными — скорость снова верная ({1:F4} в [{2}; {3}])", window, firstAfter, lo, hi));
            Verdict(zerosAfterArrival == 0 && after.EffZeros == 0 && after.Min >= lo && after.Max <= hi, string.Format(CultureInfo.InvariantCulture,
                "окно {0} с: после возобновления ни одного нуля, отношение {1:F4}…{2:F4}", window, after.Min, after.Max));
        }

        static void ConnectionLost(double window)
        {
            CountsRateManager mgr = NewManager();
            ResultData rd = NewSerialDoc();
            Block b = new Block();
            pcMs = 0;
            Normal(mgr, rd, b, window, 200000.0, new Stats());
            double lastShown = mgr.GetCPS(rd, window);
            while (pcMs - b.ArrivalTickPc < 33000.0) b.Tick(mgr, rd, true);
            rd.ResultDataStatus.Recording = false;          // ConnectionLost: набор окончен прибором
            double frozenAge = pcMs - b.ArrivalTickPc;
            int nonZeros = 0, ticks = 0;
            double end = pcMs + 60000.0;
            while (pcMs < end)
            {
                b.Tick(mgr, rd, true);
                ticks++;
                if (mgr.GetCPS(rd, window) != 0.0) nonZeros++;
            }
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "   до тишины {0:R} имп/с; отказ на возрасте {1:F3} с; 60 с после отказа — не-нулей {2} из {3}; в конце {4:R} имп/с",
                lastShown, frozenAge / 1000.0, nonZeros, ticks, mgr.GetCPS(rd, window)));
            Verdict(nonZeros == 0, "после отказа по связи панель показывает 0 (возраст замер на 33 с — больше порога, как при П250)");
        }

        static void Stopped(double window)
        {
            CountsRateManager mgr = NewManager();
            ResultData rd = NewSerialDoc();
            Block b = new Block();
            pcMs = 0;
            Normal(mgr, rd, b, window, 200000.0, new Stats());
            double lastShown = mgr.GetCPS(rd, window);
            rd.ResultDataStatus.Recording = false;          // «Стоп»
            int changed = 0, ticks = 0;
            double end = pcMs + 120000.0;
            while (pcMs < end)
            {
                b.Tick(mgr, rd, true);
                ticks++;
                if (BitConverter.DoubleToInt64Bits(mgr.GetCPS(rd, window)) != BitConverter.DoubleToInt64Bits(lastShown)) changed++;
            }
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "   последняя скорость {0:R} имп/с (отношение {1:F4}); 120 с после «Стопа» — отличий {2} из {3} тактов",
                lastShown, lastShown / Rate, changed, ticks));
            Verdict(changed == 0 && lastShown > 0, "после «Стопа» панель держит последнюю скорость побитово (как при П250)");
        }

        /// <summary>
        /// Справка: первая ненулевая скорость после «Пуска» — номер чтения и время
        /// по часам ПК. Два рода данных: время шагами (П280 — до первого чтения время
        /// данных 0, снимок истории не ложится) и непрерывное (как до П280).
        /// </summary>
        static void FirstSpeed()
        {
            foreach (bool stepped in new bool[] { true, false })
            {
                CountsRateManager mgr = NewManager();
                ResultData rd = NewSerialDoc();
                uint seed = 20261014;
                pcMs = 0;
                double nextArrival = 5500.0 + Jitter(ref seed, 100.0);
                long counts = 0;
                double moment = 0;
                int reads = 0;
                string result = "не показана за 60 с";
                while (pcMs < 60000.0)
                {
                    pcMs += 1000.0 + 150.0 * Uniform(ref seed);
                    while (nextArrival <= pcMs)
                    {
                        moment = nextArrival - 1250.0;
                        counts = (long)(Rate * moment / 1000.0);
                        nextArrival += 5500.0 + Jitter(ref seed, 100.0);
                        reads++;
                    }
                    Feed(mgr, rd, counts, stepped ? moment : pcMs);
                    double cps = mgr.GetCPS(rd, 3.0);
                    if (cps != 0.0)
                    {
                        result = string.Format(CultureInfo.InvariantCulture, "на чтении {0}, {1:F3} с по часам ПК, отношение {2:F4}",
                            reads, pcMs / 1000.0, cps / Rate);
                        break;
                    }
                }
                Console.WriteLine("   " + (stepped ? "время шагами (П280): " : "время непрерывное (до П280): ") + result);
            }
        }

        /// <summary>
        /// Порог «блок молчит» по истории — тот же обход, что `StepwiseEnds`
        /// (П250): конец — последняя смена отсчётов, начало — последняя смена не
        /// позже «конец − окно»; порог — 2·max(окно, длиннейший промежуток между
        /// сменами на охвате). Переписан сюда, чтобы проба знала, ЧЕГО ждать.
        /// </summary>
        static double Threshold(List<CountRate> rates, double window)
        {
            int lastIdx = -1;
            for (int i = rates.Count - 1; i >= 1; i--)
            {
                if (rates[i].Counts != rates[i - 1].Counts) { lastIdx = i; break; }
            }
            if (lastIdx < 0) return double.NaN;
            double limit = rates[lastIdx].ElapsedTimeInMs - window * 1000.0;
            int previous = lastIdx;
            double maxGap = 0;
            for (int i = lastIdx - 1; i >= 1; i--)
            {
                if (rates[i].Counts == rates[i - 1].Counts) continue;
                maxGap = Math.Max(maxGap, rates[previous].ElapsedTimeInMs - rates[i].ElapsedTimeInMs);
                previous = i;
                if (rates[i].ElapsedTimeInMs <= limit) break;
            }
            return 2.0 * Math.Max(window * 1000.0, maxGap);
        }

        // ---------------------------------------------------------------- прочие приборы

        /// <summary>
        /// Приборы без `IDeviceLiveTimeSource` — без контроллера и с контроллером
        /// `AmplitudaUsb`; конфигурации с τ &gt; 0 (`AtomSpectra` 3 мкс, `AmplitudaUsb`
        /// 14 мкс), чтобы мёртвое время шло через скорость на окне. Данные трёх родов:
        /// шагами раз в 5.5 с (как `AmplitudaSerial` после П280), шагами раз в 1 с
        /// (пакеты, как `AtomSpectra`), непрерывные (поток); в каждом — тишина 40 с,
        /// возобновление, затем «Стоп» и ещё 30 с тактов. На каждом такте — скорость,
        /// доля неучтённых и мёртвое время на пяти окнах формой «R». Строки
        /// сводятся в sha256 и сличаются с прогоном до правки.
        /// </summary>
        static void Others(string dump)
        {
            List<string> lines = new List<string>();
            InputDeviceConfig[] configs = { AtomConfig(), new AmplitudaUsbDeviceConfig() };
            string[] configNames = { "AtomSpectra 3 мкс", "AmplitudaUsb 14 мкс" };
            double[] cycles = { 5500.0, 1000.0, 0.0 };     // 0 — поток
            double[] windows = { 1.0, 3.0, 10.0, 30.0, 120.0 };
            foreach (bool usbController in new bool[] { false, true })
            {
                for (int c = 0; c < configs.Length; c++)
                {
                    foreach (double cycle in cycles)
                    {
                        foreach (bool clocked in new bool[] { true, false })
                        {
                            CountsRateManager mgr = clocked ? NewManager() : new CountsRateManager();
                            ResultData rd = new ResultData();
                            DeviceConfigInfo info = new DeviceConfigInfo();
                            info.InputDeviceConfig = configs[c];
                            rd.DeviceConfig = info;
                            if (usbController)
                            {
                                MeasurementController mc = new MeasurementController(null, rd);
                                mc.DeviceController = new AmplitudaUsbDeviceController();
                                rd.MeasurementController = mc;
                            }
                            rd.ResultDataStatus.Recording = true;
                            string head = string.Format(CultureInfo.InvariantCulture, "{0}|{1}|{2}|{3}",
                                usbController ? "AmplitudaUsb-контроллер" : "без контроллера", configNames[c],
                                cycle == 0.0 ? "поток" : "шаг " + (cycle / 1000.0).ToString("R", CultureInfo.InvariantCulture) + " с",
                                clocked ? "часы" : "умолч");
                            RunOther(mgr, rd, cycle, windows, head, lines);
                        }
                    }
                }
            }

            // Строки с «часы» и «умолч» на прочих приборах обязаны совпасть: часы им не читаются.
            int mismatched = 0, clockedCount = 0, defaultCount = 0;
            HashSet<string> clockedKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (string l in lines)
            {
                if (!l.Contains("|часы|")) continue;
                clockedCount++;
                clockedKeys.Add(l.Replace("|часы|", "|•|"));
            }
            foreach (string l in lines)
            {
                if (!l.Contains("|умолч|")) continue;
                defaultCount++;
                if (!clockedKeys.Contains(l.Replace("|умолч|", "|•|"))) mismatched++;
            }
            if (clockedCount != defaultCount) mismatched += Math.Abs(clockedCount - defaultCount);

            string hash;
            using (SHA256 sha = SHA256.Create())
            {
                byte[] all = Encoding.UTF8.GetBytes(string.Join("\n", lines));
                hash = BitConverter.ToString(sha.ComputeHash(all)).Replace("-", "");
            }
            int nonZeroCps = 0;
            foreach (string l in lines) if (!l.Contains("cps 0 ")) nonZeroCps++;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "   строк {0} (с ненулевой скоростью {1}); sha256 {2}", lines.Count, nonZeroCps, hash));
            Verdict(mismatched == 0, string.Format(CultureInfo.InvariantCulture,
                "менеджер с подменными часами и прежний дают те же строки (расхождений {0})", mismatched));
            if (dump != null)
            {
                File.WriteAllLines(dump, lines.ToArray(), new UTF8Encoding(false));
                Console.WriteLine("   строки записаны: " + dump);
            }
        }

        static AtomSpectraDeviceConfig AtomConfig()
        {
            AtomSpectraDeviceConfig atom = new AtomSpectraDeviceConfig();
            atom.DeadTimeValue = 3.0E-06;
            return atom;
        }

        static void RunOther(CountsRateManager mgr, ResultData rd, double cycle, double[] windows, string head, List<string> lines)
        {
            uint seed = 7;
            pcMs = 0;
            double nextArrival = cycle > 0 ? cycle + Jitter(ref seed, 100.0) : 0.0;
            long counts = 0;
            double moment = 0;
            // 0…150 с набор, 150…190 с тишина, 190…260 с набор, затем «Стоп» и 30 с тактов.
            while (pcMs < 290000.0)
            {
                pcMs += 1000.0 + 150.0 * Uniform(ref seed);
                bool silent = pcMs >= 150000.0 && pcMs < 190000.0;
                if (pcMs >= 260000.0) rd.ResultDataStatus.Recording = false;
                if (cycle > 0)
                {
                    if (silent) nextArrival = pcMs + cycle;
                    while (nextArrival <= pcMs)
                    {
                        moment = nextArrival - (cycle > 1000.0 ? 1250.0 : 0.0);
                        counts = (long)(1234.5 * moment / 1000.0);
                        nextArrival += cycle + Jitter(ref seed, 100.0);
                    }
                }
                else
                {
                    moment = pcMs;
                    if (!silent) counts = (long)(1234.5 * pcMs / 1000.0 + 300.0 * Uniform(ref seed));
                }
                Feed(mgr, rd, counts, moment);
                foreach (double w in windows)
                {
                    lines.Add(string.Format(CultureInfo.InvariantCulture, "{0}|t {1:R}|окно {2}|cps {3:R} |неучт {4}|мёртвое {5:R}",
                        head, pcMs, w, mgr.GetCPS(rd, w), mgr.GetSpecEffRatio(rd, w), mgr.GetDeadTime(rd, w)));
                }
            }
        }

        // ---------------------------------------------------------------- общее

        sealed class Stats
        {
            public int N, Zeros, EffZeros;
            public double Min = double.MaxValue, Max = double.MinValue, Sum;
            public void Add(double ratio, int eff)
            {
                N++;
                Sum += ratio;
                if (ratio == 0.0) Zeros++;
                if (eff == 0) EffZeros++;
                Min = Math.Min(Min, ratio);
                Max = Math.Max(Max, ratio);
            }
            public override string ToString()
            {
                return string.Format(CultureInfo.InvariantCulture, "n {0}, нулей {1}, нулей доли {2}, мин {3:F4}, макс {4:F4}, среднее {5:F4}",
                    N, Zeros, EffZeros, Min, Max, N > 0 ? Sum / N : double.NaN);
            }
        }

        static void Feed(CountsRateManager mgr, ResultData rd, long counts, double tMs)
        {
            rd.EnergySpectrum.TotalPulseCount = counts;
            rd.EnergySpectrum.ValidPulseCount = counts - counts / 50;      // 2 % неучтённых — чтобы мерить долю
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

        static void Verdict(bool ok, string text)
        {
            if (!ok) failures++;
            Console.WriteLine("   {0}  {1}", ok ? "ок   " : "ОТКАЗ", text);
        }
    }
}
