using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace BecquerelMonitor.Probes
{
    /// <summary>
    /// ⚠ ПРОТОТИП ПОЛОСЫ П236 (06.10.2026), НЕ КОММИТИТЬ. Считает матрицы отклика
    /// формата приложения для кривых конфигурации прибора — тем же построителем
    /// и с теми же умолчаниями, что кнопка «Response matrix…» формы прибора
    /// (<see cref="ResponseMatrixBuilder.Build"/>, <see cref="ResponseMatrixOptions"/>),
    /// и кладёт их в склад СВОЕГО рабочего каталога (<c>config\device\response</c>
    /// рядом с пробой). Решение Amber 06.10.2026 вопросником: «Считать в копию
    /// полосы» — в её склад не пишет никто.
    ///
    /// Ключи:
    ///   --device=&lt;часть имени прибора&gt;   умолчание «Nano 16 Pro RadiaScan»
    ///   --only=&lt;имя кривой,…&gt;           только эти кривые (по имени, без учёта регистра)
    ///   --threads=N                         умолчание — число ядер
    ///   --histories=N  --nodes=N            вместо умолчаний построителя
    ///   --force                             пересчитать и те, что уже годны в складе
    /// Коды: 0 — посчитано/годно всё; 2 — ключи; 3 — хоть одна кривая не посчиталась.
    /// </summary>
    static class AmberMatrixProbe
    {
        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            string device = "Nano 16 Pro RadiaScan";
            var only = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int threads = Environment.ProcessorCount, histories = 0, nodes = 0;
            bool force = false;
            foreach (string a in args)
            {
                if (a.StartsWith("--device=", StringComparison.Ordinal)) device = a.Substring(9);
                else if (a.StartsWith("--only=", StringComparison.Ordinal))
                {
                    foreach (string part in a.Substring(7).Split(',')) if (part.Trim().Length > 0) only.Add(part.Trim());
                }
                else if (a.StartsWith("--threads=", StringComparison.Ordinal)) threads = int.Parse(a.Substring(10), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--histories=", StringComparison.Ordinal)) histories = int.Parse(a.Substring(12), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--nodes=", StringComparison.Ordinal)) nodes = int.Parse(a.Substring(8), CultureInfo.InvariantCulture);
                else if (a == "--force") force = true;
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            GlobalConfigManager.GetInstance();
            DeviceConfigManager devices = DeviceConfigManager.GetInstance();
            Console.WriteLine("склад: {0}", ResponseMatrixStore.Directory);

            var targets = new List<KeyValuePair<DeviceConfigInfo, EfficiencyConfigData>>();
            foreach (DeviceConfigInfo dev in devices.DeviceConfigList)
            {
                if (dev == null || dev.Name == null || dev.Name.IndexOf(device, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (dev.EfficiencyConfigs == null) continue;
                foreach (EfficiencyConfigData eff in dev.EfficiencyConfigs)
                {
                    if (eff == null || !eff.HasGeometry) continue;
                    if (only.Count > 0 && !only.Contains(eff.Name ?? "")) continue;
                    targets.Add(new KeyValuePair<DeviceConfigInfo, EfficiencyConfigData>(dev, eff));
                }
            }
            if (targets.Count == 0)
            {
                Console.Error.WriteLine("кривых с геометрией у прибора «{0}» не найдено; приборы: {1}", device,
                                        string.Join("; ", devices.DeviceConfigList.Select(d => d.Name)));
                return 2;
            }
            Console.WriteLine("кривых к счёту: {0}; нитей {1}; историй {2}; узлов {3}", targets.Count, threads,
                              histories > 0 ? histories.ToString(CultureInfo.InvariantCulture) : "по умолчанию",
                              nodes > 0 ? nodes.ToString(CultureInfo.InvariantCulture) : "по умолчанию");

            int failed = 0;
            var total = Stopwatch.StartNew();
            foreach (KeyValuePair<DeviceConfigInfo, EfficiencyConfigData> t in targets)
            {
                EfficiencyConfigData eff = t.Value;
                Console.WriteLine();
                Console.WriteLine("=== {0} / «{1}» ({2}) матрица вкл {3}", t.Key.Name, eff.Name, eff.Guid, eff.UseResponseMatrix);
                if (!force)
                {
                    MatrixRefusal refusal;
                    int fileFormat;
                    ResponseMatrix existing = ResponseMatrixStore.Load(eff.Guid, out refusal, out fileFormat);
                    if (existing != null && existing.IsValidFor(eff.Geometry))
                    {
                        Console.WriteLine("  уже есть и годна (формат {0}) — пропуск; --force пересчитает", fileFormat);
                        continue;
                    }
                    Console.WriteLine("  в складе: {0}", existing == null ? "нет или отвергнута (" + refusal + ", формат файла " + fileFormat + ")" : "негодна для геометрии");
                }
                var options = new ResponseMatrixOptions { Threads = threads };
                if (histories > 0) options.Histories = histories;
                if (nodes > 0) options.NodeCount = nodes;
                int lastPercent = -1;
                var progress = new Progress<ResponseMatrixProgress>(p =>
                {
                    if (p == null || p.Total <= 0) return;
                    int percent = (int)(100L * p.Done / p.Total);
                    if (percent / 10 != lastPercent / 10)
                    {
                        lastPercent = percent;
                        Console.WriteLine("  {0}% ({1}/{2}) {3:F0} с", percent, p.Done, p.Total, total.Elapsed.TotalSeconds);
                    }
                });
                var sw = Stopwatch.StartNew();
                try
                {
                    ResponseMatrix matrix = ResponseMatrixBuilder.Build(eff.Geometry, options, progress, CancellationToken.None);
                    sw.Stop();
                    if (matrix == null)
                    {
                        Console.WriteLine("  ⛔ построитель вернул null за {0:F0} с", sw.Elapsed.TotalSeconds);
                        failed++;
                        continue;
                    }
                    ResponseMatrixStore.Save(eff.Guid, matrix);
                    string path = ResponseMatrixStore.PathOf(eff.Guid);
                    Console.WriteLine("  ✅ {0:F0} с → {1} ({2} байт), формат {3}, физика {4}", sw.Elapsed.TotalSeconds, path,
                                      File.Exists(path) ? new FileInfo(path).Length : -1,
                                      ResponseMatrix.FormatVersion, ResponseMatrix.PhysicsVersion);
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    Console.WriteLine("  ⛔ отказ за {0:F0} с: {1}: {2}", sw.Elapsed.TotalSeconds, ex.GetType().Name, ex.Message);
                    failed++;
                }
            }
            Console.WriteLine();
            Console.WriteLine("ИТОГО: кривых {0}, отказов {1}, {2:F0} с", targets.Count, failed, total.Elapsed.TotalSeconds);
            return failed == 0 ? 0 : 3;
        }
    }
}
