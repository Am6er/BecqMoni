// `AMBER319` (08.11.2026): МЕТКА «!» СТАРОЙ КРИВОЙ ИЛИ СТАРОЙ МАТРИЦЫ В СПИСКЕ
// «ГЕОМЕТРИЯ» ПАНЕЛИ «УПРАВЛЕНИЕ ИЗМЕРЕНИЕМ» — ТЕМ ЖЕ ПРАВИЛОМ, ЧТО ВО ВКЛАДКЕ
// «ЭФФЕКТИВНОСТЬ» КОНФИГУРАЦИИ ПРИБОРА (`AMBER218`).
//
// Постановка Amber 07.11.2026, дословно: «красные восклицательные знаки (картинка 2)
// отображать так же в списке картинки 1. Слово Эффективн. переименовать в Геометрия»;
// пункт «… — из файла спектра» — «Тем же правилом (Рекомендую)».
//
//     stalemarkpanelprobea319 --mode=tab   [--rows=<каталог для строк-картинок>]
//     stalemarkpanelprobea319 --mode=panel [--geometry=X.in] [--other=Z.in] [--shots=<каталог>]
//
// --mode=tab — ВКЛАДКА НА КОНФИГАХ ТОГО КАТАЛОГА, где лежит проба (`config\device`
//   рядом с exe; для сверки «до/после» — копия конфига Amber в двух копиях каталога
//   проб: сборки ДО выноса правила и ПОСЛЕ). На каждый прибор: скрытая
//   `DeviceConfigForm` (без показа), `LoadEfficiencyTab`, набор `efficiencyStaleGuids`,
//   решения `CurveStale` / `MatrixStale` по каждой кривой и sha256 картинки каждой
//   строки списка, нарисованной обработчиком вкладки `efficiencyCombo_DrawItem`.
//   Всё — отражением по ИМЕНАМ, которые есть в обеих сборках, поэтому одна и та же
//   проба печатает сравнимое на обеих; «побитно так же» = вывод двух прогонов
//   совпал (кроме строк времени «мс»).
//
// --mode=panel — ПАНЕЛЬ: решение по строкам (`DCControlPanel.StaleMarks`), настоящая
//   панель на скрытом столе (`UpdateEfficiencyList` отражением, строки и закрытое поле
//   — в картинку, красные точки), снимок панели en/ru, ширина подписи «Геометрия»
//   при 96 и 120 DPI. Матрицы — синтетические, на склад КАТАЛОГА ПРОБЫ, и снимаются.
//
// ⛔ ПОЛОЖИТЕЛЬНЫЕ КОНТРОЛИ: свежая кривая без матрицы и свежая с годной матрицей —
// БЕЗ метки; у строки свежей кривой нет ни одной красной точки; закрытое поле со
// свежей выбранной — без красных точек. Без них проба прошла бы на «метить всегда».
// ⛔ Окна на экран не выводятся: пускать ТОЛЬКО скрытым столом (`HiddenRun.exe`).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;

namespace BecquerelMonitor.Probes
{
    static class StaleMarkPanelProbeA319
    {
        static int failures;

        const BindingFlags NPS = BindingFlags.NonPublic | BindingFlags.Static;
        const BindingFlags NPI = BindingFlags.NonPublic | BindingFlags.Instance;
        const BindingFlags PS = BindingFlags.Public | BindingFlags.Static;

        [STAThread]
        static int Main(string[] args)
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentUICulture = new CultureInfo("en-US");
            // Как Program.Main приложения: без этого снимок панели — классическими рамками.
            Application.EnableVisualStyles();

            string mode = null, rows = null, geometryPath = null, otherPath = null, shots = null;
            foreach (string a in args)
            {
                if (a.StartsWith("--mode=", StringComparison.Ordinal)) mode = a.Substring(7);
                else if (a.StartsWith("--rows=", StringComparison.Ordinal)) rows = a.Substring(7);
                else if (a.StartsWith("--geometry=", StringComparison.Ordinal)) geometryPath = a.Substring(11);
                else if (a.StartsWith("--other=", StringComparison.Ordinal)) otherPath = a.Substring(8);
                else if (a.StartsWith("--shots=", StringComparison.Ordinal)) shots = a.Substring(8);
            }

            Console.WriteLine("== сборка ==");
            Console.WriteLine("  BecquerelMonitor.exe {0}", typeof(DeviceConfigForm).Assembly.Location);
            Console.WriteLine("  ResponseMatrix.PhysicsVersion = {0}",
                              ResponseMatrix.PhysicsVersion.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("  склад: {0}", ResponseMatrixStore.Directory);

            DeviceType.InitializeDeviceTypes();
            ThermometerType.InitializeThermometerTypes();

            if (mode == "tab")
            {
                TabMode(rows);
            }
            else if (mode == "panel")
            {
                string models = FindModels();
                geometryPath = geometryPath ?? (models == null ? null : Path.Combine(models, "Nano16Pro.in"));
                otherPath = otherPath ?? (models == null ? null : Path.Combine(models, "Nano16Pro_point10.in"));
                foreach (string p in new[] { geometryPath, otherPath })
                {
                    if (p == null || !File.Exists(p))
                    {
                        Console.WriteLine("⛔ геометрии нет: {0}; нужны --geometry= --other= (файлы .in)", p);
                        return 2;
                    }
                }

                PanelMode(geometryPath, otherPath, shots);
            }
            else
            {
                Console.WriteLine("⛔ --mode=tab | --mode=panel");
                return 2;
            }

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ВСЕ СОШЛИСЬ" : "РАСХОЖДЕНИЙ: " + failures);
            return failures == 0 ? 0 : 1;
        }

        // ==================================================================
        // Вкладка на конфигах каталога — сверка «до/после» выноса
        // ==================================================================

        static void TabMode(string rows)
        {
            MethodInfo curveStale = typeof(DeviceConfigForm).GetMethod("CurveStale", NPS);
            MethodInfo matrixStale = typeof(DeviceConfigForm).GetMethod("MatrixStale", NPS);
            MethodInfo load = typeof(DeviceConfigForm).GetMethod("LoadEfficiencyTab", NPI);
            MethodInfo draw = typeof(DeviceConfigForm).GetMethod("efficiencyCombo_DrawItem", NPI);
            FieldInfo setField = typeof(DeviceConfigForm).GetField("efficiencyStaleGuids", NPI);
            FieldInfo comboField = typeof(DeviceConfigForm).GetField("efficiencyCombo", NPI);
            if (curveStale == null || matrixStale == null || load == null || draw == null
                || setField == null || comboField == null)
            {
                Check(false, "члены вкладки (CurveStale / MatrixStale / LoadEfficiencyTab / "
                             + "efficiencyCombo_DrawItem / efficiencyStaleGuids / efficiencyCombo) найдены");
                return;
            }

            if (rows != null)
            {
                Directory.CreateDirectory(rows);
            }

            List<DeviceConfigInfo> devices = DeviceConfigManager.GetInstance().DeviceConfigList;
            Console.WriteLine();
            Console.WriteLine("== вкладка «Эффективность»: приборов {0} ==",
                              devices.Count.ToString(CultureInfo.InvariantCulture));
            int curves = 0, marked = 0, curveOld = 0, matrixOld = 0;
            long matrixMs = 0;
            using (DeviceConfigForm form = new DeviceConfigForm())
            {
                ComboBox combo = (ComboBox)comboField.GetValue(form);
                Console.WriteLine("  список: DrawMode {0}, ItemHeight {1}", combo.DrawMode, combo.ItemHeight);
                for (int d = 0; d < devices.Count; d++)
                {
                    DeviceConfigInfo device = devices[d];
                    load.Invoke(form, new object[] { device });
                    HashSet<string> set = (HashSet<string>)setField.GetValue(form);
                    Console.WriteLine("  прибор {0}: кривых {1}, с меткой {2}",
                                      (d + 1).ToString(CultureInfo.InvariantCulture),
                                      (device.EfficiencyConfigs == null ? 0 : device.EfficiencyConfigs.Count).ToString(CultureInfo.InvariantCulture),
                                      set.Count.ToString(CultureInfo.InvariantCulture));
                    for (int i = 0; i < combo.Items.Count; i++)
                    {
                        EfficiencyConfigData config = combo.Items[i] as EfficiencyConfigData;
                        bool c = false, m = false;
                        if (config != null)
                        {
                            curves++;
                            c = (bool)curveStale.Invoke(null, new object[] { config });
                            Stopwatch watch = Stopwatch.StartNew();
                            m = (bool)matrixStale.Invoke(null, new object[] { config });
                            watch.Stop();
                            matrixMs += watch.ElapsedMilliseconds;
                            if (c) curveOld++;
                            if (m) matrixOld++;
                            bool inSet = set.Contains(config.Guid ?? "");
                            if (inSet) marked++;
                            Check(inSet == (c || m), "прибор " + (d + 1) + " строка " + i + ": в наборе ⇔ кривая или матрица старая");
                        }

                        int red;
                        string sha = RenderRow(form, draw, combo, i, rows == null ? null
                            : Path.Combine(rows, string.Format(CultureInfo.InvariantCulture, "tab_{0:00}_{1:00}.png", d + 1, i)), out red);
                        Console.WriteLine("    строка {0}: {1,-6} кривая {2,-7} матрица {3,-9} красных {4,4}  sha256 {5}",
                                          i.ToString(CultureInfo.InvariantCulture),
                                          config == null ? "(нет)" : (config.Guid ?? "").Substring(0, Math.Min(6, (config.Guid ?? "").Length)),
                                          config == null ? "-" : (c ? "СТАРАЯ" : "свежая"),
                                          config == null ? "-" : (m ? "СТАРАЯ" : "не старая"),
                                          red.ToString(CultureInfo.InvariantCulture), sha);
                        if (config != null)
                        {
                            Check((red > 0) == set.Contains(config.Guid ?? ""), "прибор " + (d + 1) + " строка " + i + ": красные точки ⇔ метка");
                        }
                    }
                }
            }

            Console.WriteLine("  итого: кривых {0}, с меткой {1} (старая кривая {2}, старая матрица {3})",
                              curves.ToString(CultureInfo.InvariantCulture), marked.ToString(CultureInfo.InvariantCulture),
                              curveOld.ToString(CultureInfo.InvariantCulture), matrixOld.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("  время MatrixStale на всех кривых: {0} мс", matrixMs.ToString(CultureInfo.InvariantCulture));
        }

        // ==================================================================
        // Панель
        // ==================================================================

        /// <summary>
        /// Клеймо СВЕЖЕЙ кривой нынешней сборки: поколение, окно пика и ВСЕ ключи физики,
        /// которых вкладка требует у кривой того же поколения (`AMBER274`, Н7:
        /// <c>DeviceConfigForm.MissingPhysicsTokens</c> — берутся у самого приложения,
        /// своей копии списка нет). Без них «свежая» кривая П244 вышла старой (08.11.2026).
        /// </summary>
        static string Fresh()
        {
            string stamp = string.Format(CultureInfo.InvariantCulture, "phys={0}; hist=200000; grid=40-3000 keV/34 std; {1}",
                                         ResponseMatrix.PhysicsVersion, EfficiencyCalculation.PeakWindowStamp);
            MethodInfo missing = typeof(DeviceConfigForm).GetMethod("MissingPhysicsTokens", NPS);
            if (missing != null)
            {
                foreach (string token in (List<string>)missing.Invoke(null, new object[] { "" }))
                {
                    stamp += "; " + token;
                }
            }

            return stamp;
        }

        static string Old()
        {
            int build = ResponseMatrix.PhysicsVersion;
            return Fresh().Replace("phys=" + build.ToString(CultureInfo.InvariantCulture) + ";",
                                   "phys=" + (build - 4).ToString(CultureInfo.InvariantCulture) + ";");
        }

        static EfficiencyConfigData Curve(string name, string geometryPath, string stamp)
        {
            EfficiencyConfigData config = new EfficiencyConfigData(name);
            config.ComputeStamp = stamp;
            config.Geometry = GeometryModel.Load(geometryPath);
            config.Geometry.FwhmAt662Percent = 7.0;
            // Сцена изотропного поля: ей раздел электронов не нужен (`AMBER305`,
            // `ResponseMatrix.NeedsElectronSection`), и синтетическая матрица без раздела —
            // годная. Иначе «годной» синтетической матрицы не бывает вовсе.
            config.Geometry.Scene = GeometrySceneKind.Iso;
            config.GeometryFingerprint = ResponseMatrix.GeometryFingerprint(config.Geometry);
            config.Curve.Add(new ROIEfficiencyData { Energy = 60.0, Efficiency = 0.1 });
            config.Curve.Add(new ROIEfficiencyData { Energy = 662.0, Efficiency = 0.02 });
            config.Curve.Add(new ROIEfficiencyData { Energy = 1460.0, Efficiency = 0.01 });
            return config;
        }

        /// <summary>Копия кривой «как из файла спектра» — с тем же Guid (так её несёт файл).</summary>
        static EfficiencyConfigData FileCopy(EfficiencyConfigData source, string stamp)
        {
            EfficiencyConfigData copy = source.Copy();
            if (copy.Guid != source.Guid)
            {
                FieldInfo guid = typeof(EfficiencyConfigData).GetField("guid", NPI);
                PropertyInfo prop = typeof(EfficiencyConfigData).GetProperty("Guid");
                if (prop != null && prop.CanWrite) prop.SetValue(copy, source.Guid, null);
                else if (guid != null) guid.SetValue(copy, source.Guid);
            }

            if (stamp != null)
            {
                copy.ComputeStamp = stamp;
            }

            return copy;
        }

        static ResponseMatrix Synthetic(GeometryModel geometry, ResponseMatrixOptions options, string stamp)
        {
            double[] grid = options.BuildGrid(geometry);
            var rows = new float[grid.Length][];
            for (int i = 0; i < grid.Length; i++)
            {
                var row = new float[64];
                for (int b = 0; b < row.Length; b++)
                {
                    row[b] = (float)((i + 1) * 1e-4 + b * 1e-6);
                }

                rows[i] = row;
            }

            return new ResponseMatrix
            {
                Stamp = stamp ?? ResponseMatrix.ComputeStamp(geometry, options),
                Options = options,
                Energies = grid,
                ChannelRows = new[] { rows },
                BinKev = options.BinKev,
                Histories = options.Histories,
                CreatedUtc = new DateTime(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc),
                BuildSeconds = 1.0
            };
        }

        static void PanelMode(string geometryPath, string otherPath, string shots)
        {
            MethodInfo staleMarks = typeof(DCControlPanel).GetMethod("StaleMarks", PS);
            Type helper = typeof(DeviceConfigForm).Assembly.GetType("BecquerelMonitor.EfficiencyStaleMark");
            if (staleMarks == null || helper == null)
            {
                Check(false, "DCControlPanel.StaleMarks и EfficiencyStaleMark есть в сборке (сборка ДО AMBER319 их не имеет)");
                return;
            }

            MethodInfo isStale = helper.GetMethod("IsStale", NPS);
            var options = new ResponseMatrixOptions { NodeCount = 6, MinEnergyKev = 40.0, MaxEnergyKev = 3000.0,
                                                      Histories = 1000 };

            // Кривые прибора: свежая без матрицы, старая кривая, свежая со старой матрицей,
            // свежая с годной матрицей.
            EfficiencyConfigData fresh = Curve("свежая", geometryPath, Fresh());
            EfficiencyConfigData oldCurve = Curve("старая кривая", geometryPath, Old());
            EfficiencyConfigData oldMatrix = Curve("старая матрица", geometryPath, Fresh());
            EfficiencyConfigData goodMatrix = Curve("годная матрица", geometryPath, Fresh());
            DeviceConfigInfo device = new DeviceConfigInfo();
            device.EfficiencyConfigs.Add(fresh);
            device.EfficiencyConfigs.Add(oldCurve);
            device.EfficiencyConfigs.Add(oldMatrix);
            device.EfficiencyConfigs.Add(goodMatrix);

            List<string> written = new List<string>();
            Directory.CreateDirectory(ResponseMatrixStore.Directory);
            try
            {
                string oldStamp = ResponseMatrix.ComputeStamp(oldMatrix.Geometry, options);
                oldStamp = "phys=" + (ResponseMatrix.PhysicsVersion - 1).ToString(CultureInfo.InvariantCulture)
                           + oldStamp.Substring(oldStamp.IndexOf(';'));
                Save(Synthetic(oldMatrix.Geometry, options, oldStamp), oldMatrix.Guid, written);
                Save(Synthetic(goodMatrix.Geometry, options, null), goodMatrix.Guid, written);

                Console.WriteLine();
                Console.WriteLine("== решение по строкам панели (DCControlPanel.StaleMarks) ==");

                // 1. Кривые прибора, родной кривой нет; выбрана свежая.
                Case(staleMarks, "1. кривые прибора, выбрана свежая", fresh, null, device,
                     new[] { false, false, true, true, false });

                // 2. Родная копия = свежая кривая без матрицы на складе (чужой файл):
                //    судится только кривая — метки нет.
                EfficiencyConfigData foreignFresh = Curve("из файла, свежая", geometryPath, Fresh());
                Case(staleMarks, "2. из файла: свежая, матрицы на складе нет", foreignFresh, foreignFresh, device,
                     new[] { false, false, false, true, true, false });

                // 3. Родная копия — старая кривая (матрицы нет): метка по кривой копии.
                EfficiencyConfigData foreignOld = Curve("из файла, старая", geometryPath, Old());
                Case(staleMarks, "3. из файла: старая кривая, матрицы нет", foreignOld, foreignOld, device,
                     new[] { false, true, false, true, true, false });

                // 4. Родная копия свежая, её Guid совпадает с кривой прибора, у которой
                //    матрица на складе устарела: метка по матрице копии.
                EfficiencyConfigData copyOldMatrix = FileCopy(oldMatrix, null);
                Check(copyOldMatrix.Guid == oldMatrix.Guid, "4. копия несёт Guid кривой прибора");
                Case(staleMarks, "4. из файла: свежая, матрица копии устарела", copyOldMatrix, copyOldMatrix, device,
                     new[] { false, true, false, true, true, false });

                // 5. Родная копия свежая, её матрица годна — метки нет (положительный контроль).
                EfficiencyConfigData copyGood = FileCopy(goodMatrix, null);
                Case(staleMarks, "5. из файла: свежая, матрица копии годна", copyGood, copyGood, device,
                     new[] { false, false, false, true, true, false });

                // 6. Строка судится СВОЕЙ копией, а не Guid: копия свежей кривой прибора,
                //    но со старым клеймом — помечена строка файла, строка прибора нет.
                EfficiencyConfigData copyFreshOld = FileCopy(fresh, Old());
                Case(staleMarks, "6. из файла: копия свежей кривой прибора, но клеймо старое",
                     copyFreshOld, copyFreshOld, device, new[] { false, true, false, true, true, false });

                // 7. Матрица копии годна для геометрии прибора, а копия — другой геометрии
                //    (геометрию в файле правили): матрица судится по геометрии копии.
                EfficiencyConfigData copyOtherGeometry = FileCopy(goodMatrix, null);
                copyOtherGeometry.Geometry = GeometryModel.Load(otherPath);
                copyOtherGeometry.Geometry.FwhmAt662Percent = 7.0;
                copyOtherGeometry.Geometry.Scene = GeometrySceneKind.Iso;
                copyOtherGeometry.GeometryFingerprint = ResponseMatrix.GeometryFingerprint(copyOtherGeometry.Geometry);
                Case(staleMarks, "7. из файла: матрица на складе от другой геометрии",
                     copyOtherGeometry, copyOtherGeometry, device, new[] { false, true, false, true, true, false });

                // 8. Матрицы у копии нет — снять годную со склада: судится только кривая.
                File.Delete(ResponseMatrixStore.PathOf(goodMatrix.Guid));
                Case(staleMarks, "8. из файла: свежая, матрица снята со склада", copyGood, copyGood, device,
                     new[] { false, false, false, true, true, false });
                Save(Synthetic(goodMatrix.Geometry, options, null), goodMatrix.Guid, written);

                // 9. Кривая, которой нет ни в файле, ни у прибора (последняя строка) — тем же правилом.
                EfficiencyConfigData stray = Curve("ничья, старая", geometryPath, Old());
                Case(staleMarks, "9. ничья кривая (не из файла, не у прибора), старая", stray, null, device,
                     new[] { false, false, true, true, false, true });

                // 10. Решение строки = общий помощник по каждой копии (одна копия правила).
                List<object> items = DCControlPanel.BuildEfficiencyItems(copyFreshOld, copyFreshOld, device, out int sel);
                bool[] marks = (bool[])staleMarks.Invoke(null, new object[] { items });
                bool same = marks.Length == items.Count;
                for (int i = 0; same && i < items.Count; i++)
                {
                    EfficiencyConfigData c = items[i] is DCControlPanel.SpectrumEfficiencyItem own ? own.Config
                                           : items[i] as EfficiencyConfigData;
                    same = marks[i] == (c != null && (bool)isStale.Invoke(null, new object[] { c }));
                }

                Check(same, "10. метка строки = EfficiencyStaleMark.IsStale своей копии (копия без встроенной матрицы)");

                Embedded(staleMarks, helper, geometryPath, otherPath, options, written);
                Check(((bool[])staleMarks.Invoke(null, new object[] { null })).Length == 0, "11. null — пустые метки, без исключения");

                RealPanel(fresh, oldCurve, oldMatrix, goodMatrix, copyFreshOld, device, shots);
            }
            finally
            {
                foreach (string path in written)
                {
                    try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
                }
            }

            LabelFit();
        }

        /// <summary>
        /// (`AMBER319`, второй заход; решение Amber 08.11.2026 «По тому, что возьмёт разбор
        /// (Рекомендую)») Пункт из файла судит матрицу, которую возьмёт разбор
        /// (<c>ResponseMatrixStore.Resolve</c>). Сценарий критика `crit319`: спектр сохранён со
        /// встроенной матрицей геометрии G; у прибора геометрию правят (G') и матрицу
        /// пересчитывают. Склад по тому же Guid годен для G', не для G; разбор берёт годную
        /// встроенную — метки нет. Контроли: без встроенной — по складу, метка; встроенная
        /// прежнего поколения физики (её Resolve отвергает) — по складу, метка.
        /// </summary>
        static void Embedded(MethodInfo staleMarks, Type helper, string geometryPath, string otherPath,
                             ResponseMatrixOptions options, List<string> written)
        {
            MethodInfo isStale = helper.GetMethod("IsStale", NPS);
            MethodInfo asAnalysed = helper.GetMethod("IsStaleAsAnalysed", NPS);
            if (asAnalysed == null)
            {
                Check(false, "EfficiencyStaleMark.IsStaleAsAnalysed есть в сборке");
                return;
            }

            Console.WriteLine();
            Console.WriteLine("== пункт из файла со встроенной матрицей (Resolve) ==");
            EfficiencyConfigData device = Curve("правленая", geometryPath, Fresh());
            string path = ResponseMatrixStore.PathOf(device.Guid);
            ResponseMatrix forG = Synthetic(device.Geometry, options, null);
            Save(forG, device.Guid, written);
            EmbeddedResponseMatrix embedded = EmbeddedResponseMatrix.FromFile(device.Guid, path, forG);

            // Прежнего поколения физики — та же геометрия G, клеймо на поколение ниже.
            string oldPath = Path.Combine(Path.GetDirectoryName(path), "a319_old_" + device.Guid + ".rmx");
            string oldStamp = ResponseMatrix.ComputeStamp(device.Geometry, options);
            oldStamp = "phys=" + (ResponseMatrix.PhysicsVersion - 1).ToString(CultureInfo.InvariantCulture)
                       + oldStamp.Substring(oldStamp.IndexOf(';'));
            ResponseMatrix oldMatrix = Synthetic(device.Geometry, options, oldStamp);
            oldMatrix.Save(oldPath);
            written.Add(oldPath);
            EmbeddedResponseMatrix embeddedOld = EmbeddedResponseMatrix.FromFile(device.Guid, oldPath, oldMatrix);

            // Спектр сохранён: копия кривой геометрии G со встроенной матрицей.
            EfficiencyConfigData withEmbedded = device.Copy();
            withEmbedded.EmbeddedMatrix = embedded;
            EfficiencyConfigData withoutEmbedded = device.Copy();
            EfficiencyConfigData withOldEmbedded = device.Copy();
            withOldEmbedded.EmbeddedMatrix = embeddedOld;
            Check(withEmbedded.Guid == device.Guid && withoutEmbedded.EmbeddedMatrix == null,
                  "копии несут Guid кривой прибора; у копии «без встроенной» встроенной нет");

            // У прибора правят геометрию (G') и пересчитывают матрицу: склад годен для G'.
            device.Geometry = GeometryModel.Load(otherPath);
            device.Geometry.FwhmAt662Percent = 7.0;
            device.Geometry.Scene = GeometrySceneKind.Iso;
            device.GeometryFingerprint = ResponseMatrix.GeometryFingerprint(device.Geometry);
            Save(Synthetic(device.Geometry, options, null), device.Guid, written);
            DeviceConfigInfo cfg = new DeviceConfigInfo();
            cfg.EfficiencyConfigs.Add(device);

            Case(staleMarks, "12. сценарий критика: встроенная годна, склад — для правленой геометрии",
                 withEmbedded, withEmbedded, cfg, new[] { false, false, false });
            bool tabRule = (bool)isStale.Invoke(null, new object[] { withEmbedded });
            Console.WriteLine("      правило вкладки (склад) для той же копии: {0}", tabRule ? "СТАРАЯ" : "не старая");
            Check(tabRule, "12. правило вкладки по складу копию метит (разница — только источник матрицы)");
            Case(staleMarks, "13. контроль: встроенной нет — судится склад (чужая геометрия)",
                 withoutEmbedded, withoutEmbedded, cfg, new[] { false, true, false });
            Case(staleMarks, "14. контроль: встроенная прежнего поколения физики — Resolve отвергает, судится склад",
                 withOldEmbedded, withOldEmbedded, cfg, new[] { false, true, false });

            MatrixRefusal refusal;
            int format;
            ResponseMatrixSource source;
            string why;
            ResponseMatrixStore.Resolve(withEmbedded, out refusal, out format, out source, out why);
            Console.WriteLine("      Resolve(12): источник {0}", source);
            Check(source == ResponseMatrixSource.Spectrum, "12. Resolve отдаёт встроенную");
            ResponseMatrixStore.Resolve(withOldEmbedded, out refusal, out format, out source, out why);
            Console.WriteLine("      Resolve(14): источник {0}, встроенная не взята: {1}", source, why);
            Check(source == ResponseMatrixSource.Store && why.Length > 0, "14. Resolve отвергает встроенную прежней физики");
        }

        static void Save(ResponseMatrix matrix, string guid, List<string> written)
        {
            string path = ResponseMatrixStore.PathOf(guid);
            matrix.Save(path);
            written.Add(path);
        }

        static void Case(MethodInfo staleMarks, string title, EfficiencyConfigData current, EfficiencyConfigData own,
                         DeviceConfigInfo device, bool[] want)
        {
            int selected;
            List<object> items = DCControlPanel.BuildEfficiencyItems(current, own, device, out selected);
            bool[] got = (bool[])staleMarks.Invoke(null, new object[] { items });
            var line = new StringBuilder();
            for (int i = 0; i < items.Count; i++)
            {
                line.Append(i == 0 ? "" : " | ").Append(got[i] ? "! " : "").Append(items[i]);
            }

            Console.WriteLine("  {0}", title);
            Console.WriteLine("      {0}", line);
            bool ok = got.Length == want.Length;
            for (int i = 0; ok && i < got.Length; i++)
            {
                ok = got[i] == want[i];
            }

            Check(ok, title + ": метки " + Flags(got) + ", ждали " + Flags(want));
        }

        static string Flags(bool[] flags)
        {
            var s = new StringBuilder();
            foreach (bool f in flags) s.Append(f ? '!' : '.');
            return s.ToString();
        }

        // ------------------------------------------------------------------
        // Настоящая панель на скрытом столе
        // ------------------------------------------------------------------

        static void RealPanel(EfficiencyConfigData fresh, EfficiencyConfigData oldCurve, EfficiencyConfigData oldMatrix,
                              EfficiencyConfigData goodMatrix, EfficiencyConfigData own, DeviceConfigInfo device, string shots)
        {
            Console.WriteLine();
            Console.WriteLine("== настоящая панель (скрытый стол) ==");
            MethodInfo update = typeof(DCControlPanel).GetMethod("UpdateEfficiencyList", NPI);
            MethodInfo draw = typeof(DCControlPanel).GetMethod("efficiencyComboBox_DrawItem", NPI);
            FieldInfo marksField = typeof(DCControlPanel).GetField("efficiencyStaleMarks", NPI);
            FieldInfo comboField = typeof(DCControlPanel).GetField("efficiencyComboBox", NPI);
            FieldInfo labelField = typeof(DCControlPanel).GetField("efficiencyLbl", NPI);
            FieldInfo tipField = typeof(DCControlPanel).GetField("toolTip1", NPI);
            if (update == null || draw == null || marksField == null || comboField == null || labelField == null || tipField == null)
            {
                Check(false, "члены панели (UpdateEfficiencyList / efficiencyComboBox_DrawItem / efficiencyStaleMarks / "
                             + "efficiencyComboBox / efficiencyLbl / toolTip1) найдены");
                return;
            }

            foreach (string culture in new[] { "en-US", "ru-RU" })
            {
                Thread.CurrentThread.CurrentUICulture = new CultureInfo(culture);
                BecquerelMonitor.Properties.Resources.Culture = Thread.CurrentThread.CurrentUICulture;
                MainForm shell = (MainForm)FormatterServices.GetUninitializedObject(typeof(MainForm));
                using (DCControlPanel panel = new DCControlPanel(shell))
                {
                    ComboBox combo = (ComboBox)comboField.GetValue(panel);
                    Label label = (Label)labelField.GetValue(panel);
                    ToolTip tip = (ToolTip)tipField.GetValue(panel);
                    Console.WriteLine("  [{0}] подпись «{1}», DrawMode {2}", culture, label.Text, combo.DrawMode);
                    Check(combo.DrawMode == DrawMode.OwnerDrawFixed, culture + ": список рисуется своими руками");
                    Check(label.Text == (culture == "en-US" ? "Geometry" : "Геометрия"), culture + ": подпись «Geometry»/«Геометрия»");
                    string tipText = tip.GetToolTip(combo);
                    int longest = 0;
                    foreach (string l in tipText.Replace("\r\n", "\n").Split('\n')) longest = Math.Max(longest, l.Length);
                    Console.WriteLine("  [{0}] подсказка: {1} знаков, строк {2}, длиннейшая {3}", culture,
                                      tipText.Length.ToString(CultureInfo.InvariantCulture),
                                      tipText.Split('\n').Length.ToString(CultureInfo.InvariantCulture),
                                      longest.ToString(CultureInfo.InvariantCulture));
                    Check(tipText.IndexOf(culture == "en-US" ? "geometry" : "Геометрия", StringComparison.Ordinal) >= 0
                          && longest <= AppUi.TipWidth, culture + ": подсказка о геометрии, блоком ≤ " + AppUi.TipWidth);

                    // Выбрана родная копия со старым клеймом: строки «(нет)», «! копия», кривые прибора.
                    ResultData data = new ResultData();
                    data.DeviceConfig = device;
                    data.FileEfficiency = own;
                    data.Efficiency = own;
                    panel.ClientSize = new Size(360, 300);
                    panel.Show();
                    Application.DoEvents();
                    update.Invoke(panel, new object[] { data });
                    bool[] marks = (bool[])marksField.GetValue(panel);
                    Console.WriteLine("  [{0}] строк {1}, метки {2}, выбрана {3}", culture,
                                      combo.Items.Count.ToString(CultureInfo.InvariantCulture), Flags(marks),
                                      combo.SelectedIndex.ToString(CultureInfo.InvariantCulture));
                    Check(Flags(marks) == ".!.!!.", culture + ": метки «(нет), ! копия, свежая, ! старая кривая, ! старая матрица, годная»");

                    // Раскрытый список — строки в картинку.
                    for (int i = 0; i < combo.Items.Count; i++)
                    {
                        int red;
                        RenderRow(panel, draw, combo, i, shots == null ? null
                            : Path.Combine(shots, string.Format(CultureInfo.InvariantCulture, "panel_{0}_row{1}.png", culture.Substring(0, 2), i)), out red);
                        Console.WriteLine("    строка {0}: красных {1,4}  {2}", i.ToString(CultureInfo.InvariantCulture),
                                          red.ToString(CultureInfo.InvariantCulture), combo.Items[i]);
                        Check((red > 0) == marks[i], culture + " строка " + i + ": красные точки ⇔ метка");
                    }

                    // Закрытое поле: выбрана помеченная, затем свежая.
                    int redSelStale = ClosedField(panel, combo, shots, culture, "stale");
                    combo.SelectedIndex = 2;   // свежая кривая прибора
                    int redSelFresh = ClosedField(panel, combo, shots, culture, "fresh");
                    Console.WriteLine("  [{0}] закрытое поле: выбрана старая — красных {1}; выбрана свежая — красных {2}", culture,
                                      redSelStale.ToString(CultureInfo.InvariantCulture), redSelFresh.ToString(CultureInfo.InvariantCulture));
                    Check(redSelStale > 0, culture + ": закрытое поле со старой выбранной — метка есть");
                    Check(redSelFresh == 0, culture + ": закрытое поле со свежей выбранной — красных нет (положительный контроль)");

                    // Подпись влезает: правый край подписи левее поля списка.
                    Console.WriteLine("  [{0}] подпись: x {1}..{2}, поле списка с x {3}", culture,
                                      label.Left.ToString(CultureInfo.InvariantCulture), label.Right.ToString(CultureInfo.InvariantCulture),
                                      combo.Left.ToString(CultureInfo.InvariantCulture));
                    Check(label.Right < combo.Left, culture + ": подпись не заходит на поле списка");

                    // Снимок панели целиком (скрытый стол; на диск, не в git).
                    combo.SelectedIndex = 1;
                    Application.DoEvents();
                    if (shots != null)
                    {
                        using (Bitmap bmp = new Bitmap(panel.Width, panel.Height))
                        {
                            panel.DrawToBitmap(bmp, new Rectangle(0, 0, panel.Width, panel.Height));
                            string file = Path.Combine(shots, "panel_" + culture.Substring(0, 2) + ".png");
                            bmp.Save(file, ImageFormat.Png);
                            Console.WriteLine("  [{0}] снимок панели: {1}", culture, file);
                        }
                    }

                    panel.Hide();
                }
            }

            Thread.CurrentThread.CurrentUICulture = new CultureInfo("en-US");
            BecquerelMonitor.Properties.Resources.Culture = null;
        }

        static int ClosedField(Control owner, ComboBox combo, string shots, string culture, string what)
        {
            Application.DoEvents();
            using (Bitmap bmp = new Bitmap(combo.Width, combo.Height))
            {
                combo.DrawToBitmap(bmp, new Rectangle(0, 0, combo.Width, combo.Height));
                if (shots != null)
                {
                    bmp.Save(Path.Combine(shots, "panel_" + culture.Substring(0, 2) + "_closed_" + what + ".png"), ImageFormat.Png);
                }

                int red, white;
                CountRed(bmp, out red, out white);
                return red;
            }
        }

        // ------------------------------------------------------------------
        // Подпись «Геометрия» / «Geometry»: ширина при 96 и 120 DPI
        // ------------------------------------------------------------------

        static void LabelFit()
        {
            Console.WriteLine();
            Console.WriteLine("== ширина подписей (шрифт панели Microsoft Sans Serif 8 pt) ==");
            // Раскладка панели (DCControlPanel.resx): подписи с x = 13, поля — с x = 95.
            const int labelX = 13, fieldX = 95;
            foreach (double scale in new[] { 1.0, 1.25 })
            {
                using (Font font = new Font("Microsoft Sans Serif", (float)(8.0 * 96.0 / 72.0 * scale), GraphicsUnit.Pixel))
                {
                    foreach (string text in new[] { "Geometry", "Геометрия", "Efficiency", "Эффективн.", "Device Config.", "Устройство" })
                    {
                        int w = TextRenderer.MeasureText(text, font).Width;
                        int roomFixed = fieldX - labelX;                         // раскладка без масштаба
                        int roomScaled = (int)Math.Floor((fieldX - labelX) * scale);   // раскладка с масштабом
                        Console.WriteLine("  {0,3} DPI  {1,-15} {2,3} точек; место {3} (без масштаба раскладки) / {4} (с масштабом)",
                                          ((int)(96 * scale)).ToString(CultureInfo.InvariantCulture), text,
                                          w.ToString(CultureInfo.InvariantCulture), roomFixed.ToString(CultureInfo.InvariantCulture),
                                          roomScaled.ToString(CultureInfo.InvariantCulture));
                        if (text == "Geometry" || text == "Геометрия")
                        {
                            Check(w < roomFixed, text + " при " + (int)(96 * scale) + " DPI влезает и без масштаба раскладки");
                        }
                    }
                }
            }
        }

        // ------------------------------------------------------------------
        // Оснастка
        // ------------------------------------------------------------------

        static string RenderRow(object owner, MethodInfo draw, ComboBox combo, int index, string file, out int red)
        {
            using (Bitmap bmp = new Bitmap(300, combo.ItemHeight))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                Rectangle bounds = new Rectangle(0, 0, bmp.Width, bmp.Height);
                var e = new DrawItemEventArgs(g, combo.Font, bounds, index, DrawItemState.None, Color.Black, Color.White);
                draw.Invoke(owner, new object[] { combo, e });
                int white;
                CountRed(bmp, out red, out white);
                if (file != null)
                {
                    bmp.Save(file, ImageFormat.Png);
                }

                return Sha(bmp);
            }
        }

        static string Sha(Bitmap bmp)
        {
            var bytes = new List<byte>(bmp.Width * bmp.Height * 4);
            for (int y = 0; y < bmp.Height; y++)
            {
                for (int x = 0; x < bmp.Width; x++)
                {
                    int argb = bmp.GetPixel(x, y).ToArgb();
                    bytes.AddRange(BitConverter.GetBytes(argb));
                }
            }

            using (SHA256 sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(bytes.ToArray())).Replace("-", "").Substring(0, 16).ToLowerInvariant();
            }
        }

        static void CountRed(Bitmap bmp, out int red, out int white)
        {
            red = 0;
            white = 0;
            for (int y = 0; y < bmp.Height; y++)
            {
                for (int x = 0; x < bmp.Width; x++)
                {
                    Color c = bmp.GetPixel(x, y);
                    if (c.R > 200 && c.G < 60 && c.B < 60)
                    {
                        red++;
                    }
                    else if (c.R > 200 && c.G > 200 && c.B > 200)
                    {
                        white++;
                    }
                }
            }
        }

        static string FindModels()
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int up = 0; up < 8 && dir != null; up++)
            {
                string models = Path.Combine(dir, "tools", "effmaker", "models");
                if (File.Exists(Path.Combine(models, "Nano16Pro.in")))
                {
                    return models;
                }

                DirectoryInfo parent = Directory.GetParent(dir.TrimEnd(Path.DirectorySeparatorChar));
                dir = parent == null ? null : parent.FullName;
            }

            return null;
        }

        static void Check(bool ok, string what)
        {
            if (!ok)
            {
                failures++;
                Console.WriteLine("    ⛔ НЕ СОШЛОСЬ: {0}", what);
            }
        }
    }
}
