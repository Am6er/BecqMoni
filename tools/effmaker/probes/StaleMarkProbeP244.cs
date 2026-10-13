// `AMBER218` (П244, 07.10.2026): МЕТКА «!» СТАРОЙ КРИВОЙ ИЛИ СТАРОЙ МАТРИЦЫ
// В СПИСКЕ ГЕОМЕТРИЙ ВКЛАДКИ «Efficiency» КОНФИГУРАЦИИ ПРИБОРА.
//
// Постановка Amber 07.10.2026, консоль со снимком, дословно: «В выпадающем
// списке геометрий пометь те геометрии, в которых либо эффективность старой
// версии либо матрица отклика старой версии - не имеет значения, что именно.
// подсвети их белым восклицательным знаком в красной квадратной подложке
// (красный квадрат, а в нём белый воскл знак) перед текстом имя геометрии.
// Формат, например "! Цилиндр"». Решения вопросником того же дня: кривая —
// «Всё, о чём вкладка говорит «пересчитайте»», матрица — «Всё, что окно
// матрицы зовёт «устарела»», где — «Только список в конфигурации прибора».
//
// Что меряется. Решение «ставить ли метку» живёт в статических методах
// `DeviceConfigForm.CurveStale / MatrixStale / EfficiencyStale /
// StaleEfficiencyGuids` — нарочно без окна; рисунок метки — в статическом
// `DrawStaleMark`. Проба ходит отражением: своей копии правила у неё нет
// НАРОЧНО, копия проверяла бы себя.
//
//     stalemarkprobep244 [--geometry=X.in] [--vessel=Y.in] [--other=Z.in] [--real=<файл.rmx>]
//
// Пять разделов: решение о кривой (8 случаев), решение о матрице на складе
// стенда (8 случаев, матрица собрана в памяти и записана `ResponseMatrix.Save`
// — счёта матриц здесь нет), набор Guid прибора, форма без показа (набор
// после заполнения списка и после записи матрицы, рисование строки списка в
// картинку — красные и белые точки), цена чтения одной настоящей матрицы.
//
// ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ — «У СВЕЖЕЙ КРИВОЙ И ГОДНОЙ МАТРИЦЫ МЕТКИ НЕТ»
// (случаи 1 и б) и «у свежей строки списка НЕТ красных точек» (раздел формы):
// без них проба прошла бы и на правиле «метить всегда».
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;

namespace BecquerelMonitor.Probes
{
    static class StaleMarkProbeP244
    {
        static int failures;

        const BindingFlags NPS = BindingFlags.NonPublic | BindingFlags.Static;
        const BindingFlags NPI = BindingFlags.NonPublic | BindingFlags.Instance;

        static readonly MethodInfo CurveStale = typeof(DeviceConfigForm).GetMethod("CurveStale", NPS);
        static readonly MethodInfo MatrixStale = typeof(DeviceConfigForm).GetMethod("MatrixStale", NPS);
        static readonly MethodInfo EfficiencyStale = typeof(DeviceConfigForm).GetMethod("EfficiencyStale", NPS);
        static readonly MethodInfo StaleGuids = typeof(DeviceConfigForm).GetMethod("StaleEfficiencyGuids", NPS);
        static readonly MethodInfo DrawMark = typeof(DeviceConfigForm).GetMethod("DrawStaleMark", NPS);

        [STAThread]
        static int Main(string[] args)
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentUICulture = new CultureInfo("en-US");

            string geometryPath = null, vesselPath = null, otherPath = null, realPath = null;
            foreach (string a in args)
            {
                if (a.StartsWith("--geometry=", StringComparison.Ordinal)) geometryPath = a.Substring(11);
                else if (a.StartsWith("--vessel=", StringComparison.Ordinal)) vesselPath = a.Substring(9);
                else if (a.StartsWith("--other=", StringComparison.Ordinal)) otherPath = a.Substring(8);
                else if (a.StartsWith("--real=", StringComparison.Ordinal)) realPath = a.Substring(7);
            }

            if (CurveStale == null || MatrixStale == null || EfficiencyStale == null
                || StaleGuids == null || DrawMark == null)
            {
                Console.WriteLine("НЕТ МЕТОДОВ DeviceConfigForm.CurveStale / MatrixStale / EfficiencyStale / "
                                  + "StaleEfficiencyGuids / DrawStaleMark — мерить нечего");
                return 2;
            }

            string models = FindModels();
            geometryPath = geometryPath ?? (models == null ? null : Path.Combine(models, "Nano16Pro.in"));
            vesselPath = vesselPath ?? (models == null ? null : Path.Combine(models, "Nano16Pro_Marinelli.in"));
            otherPath = otherPath ?? (models == null ? null : Path.Combine(models, "Nano16Pro_point10.in"));
            foreach (string p in new[] { geometryPath, vesselPath, otherPath })
            {
                if (p == null || !File.Exists(p))
                {
                    Console.WriteLine("⛔ геометрии нет: {0}; нужны --geometry= --vessel= --other= (файлы .in)", p);
                    return 2;
                }
            }

            Console.WriteLine("== поколение сборки ==");
            Console.WriteLine("  ResponseMatrix.PhysicsVersion = {0}, FormatVersion = {1}",
                              ResponseMatrix.PhysicsVersion.ToString(CultureInfo.InvariantCulture),
                              ResponseMatrix.FormatVersion.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("  геометрия {0}", geometryPath);
            Console.WriteLine("  сосуд     {0}", vesselPath);
            Console.WriteLine("  другая    {0}", otherPath);


            CurveCases(geometryPath, vesselPath);
            MatrixCases(geometryPath, otherPath);
            GuidSet(geometryPath);
            FormCases(geometryPath);
            Timing(realPath, geometryPath);

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ВСЕ СОШЛИСЬ" : "РАСХОЖДЕНИЙ: " + failures);
            return failures == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------------
        // Решение о кривой
        // ------------------------------------------------------------------

        static string Fresh()
        {
            return string.Format(CultureInfo.InvariantCulture, "phys={0}; hist=200000; grid=40-3000 keV/34 std; {1}",
                                 ResponseMatrix.PhysicsVersion, EfficiencyCalculation.PeakWindowStamp);
        }

        static EfficiencyConfigData Curve(string name, GeometryModel geometry, string stamp, double fwhm)
        {
            EfficiencyConfigData config = new EfficiencyConfigData(name);
            config.ComputeStamp = stamp;
            if (geometry != null)
            {
                config.Geometry = geometry;
                config.Geometry.FwhmAt662Percent = fwhm;
                config.GeometryFingerprint = ResponseMatrix.GeometryFingerprint(config.Geometry);
            }

            config.Curve.Add(new ROIEfficiencyData { Energy = 60.0, Efficiency = 0.1 });
            config.Curve.Add(new ROIEfficiencyData { Energy = 662.0, Efficiency = 0.02 });
            config.Curve.Add(new ROIEfficiencyData { Energy = 1460.0, Efficiency = 0.01 });
            return config;
        }

        static void CurveCases(string geometryPath, string vesselPath)
        {
            Console.WriteLine();
            Console.WriteLine("== решение о кривой (CurveStale) ==");
            int build = ResponseMatrix.PhysicsVersion;
            string old = Fresh().Replace("phys=" + build.ToString(CultureInfo.InvariantCulture) + ";",
                                         "phys=" + (build - 4).ToString(CultureInfo.InvariantCulture) + ";");
            string noPeak = Fresh().Replace("; " + EfficiencyCalculation.PeakWindowStamp, "");

            // 1. ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: свежая кривая, отпечаток сходится — метки нет.
            GeometryModel g1 = GeometryModel.Load(geometryPath);
            CurveCase("1. свежая кривая, геометрия с разрешением", Curve("1", g1, Fresh(), 7.0), false);

            // 2. Поколение ниже сборки.
            GeometryModel g2 = GeometryModel.Load(geometryPath);
            CurveCase("2. поколение " + (build - 4).ToString(CultureInfo.InvariantCulture)
                      + " при сборке " + build.ToString(CultureInfo.InvariantCulture),
                      Curve("2", g2, old, 7.0), true);

            // 3. Нынешнее поколение, прежнее определение пика у геометрии с разрешением.
            GeometryModel g3 = GeometryModel.Load(geometryPath);
            CurveCase("3. без «" + EfficiencyCalculation.PeakWindowStamp + "», с разрешением",
                      Curve("3", g3, noPeak, 7.0), true);

            // 4. То же без разрешения — окна нет, кривая прежняя по праву.
            GeometryModel g4 = GeometryModel.Load(geometryPath);
            CurveCase("4. без «" + EfficiencyCalculation.PeakWindowStamp + "», без разрешения",
                      Curve("4", g4, noPeak, 0.0), false);

            // 5. Посчитана для другой геометрии: отпечаток не сходится.
            GeometryModel g5 = GeometryModel.Load(geometryPath);
            EfficiencyConfigData mismatch = Curve("5", g5, Fresh(), 7.0);
            mismatch.GeometryFingerprint = "не тот отпечаток";
            CurveCase("5. отпечаток при кривой не сходится с геометрией", mismatch, true);

            // 6. Сосуд: прежняя сцена (нет bmscene=1) — только если сцена сосуда правда изменилась.
            GeometryModel v6 = GeometryModel.Load(vesselPath);
            bool expected = EfficiencyCalculation.VesselStampExpected(v6);
            Console.WriteLine("  сосуд {0}: клеймо «{1}» {2}", Path.GetFileName(vesselPath),
                              EfficiencyCalculation.VesselStamp, expected ? "ОЖИДАЕТСЯ" : "не ожидается");
            Check(expected, "6. выбранный сосуд ожидает клеймо сцены (иначе случай 6 ничего не меряет)");
            CurveCase("6. сосуд без «" + EfficiencyCalculation.VesselStamp + "»", Curve("6", v6, Fresh(), 7.0), expected);
            GeometryModel v6b = GeometryModel.Load(vesselPath);
            CurveCase("6б. сосуд с «" + EfficiencyCalculation.VesselStamp + "»",
                      Curve("6б", v6b, Fresh() + "; " + EfficiencyCalculation.VesselStamp, 7.0), false);

            // 7. Клейма нет (ручная, по измерениям) — молчим.
            GeometryModel g7 = GeometryModel.Load(geometryPath);
            CurveCase("7. кривая без клейма", Curve("7", g7, "", 7.0), false);

            // 8. Без геометрии, но с отставшим поколением — подпись говорит, значит и метка.
            EfficiencyConfigData bare = new EfficiencyConfigData("8");
            bare.ComputeStamp = old;
            CurveCase("8. без геометрии, поколение отстало", bare, true);

            Check(!(bool)CurveStale.Invoke(null, new object[] { null }), "9. null — не старая, без исключения");
        }

        static void CurveCase(string title, EfficiencyConfigData config, bool want)
        {
            bool got = (bool)CurveStale.Invoke(null, new object[] { config });
            Console.WriteLine("  {0,-56} -> {1}", title, got ? "СТАРАЯ" : "свежая");
            Check(got == want, title + ": ждали " + (want ? "СТАРАЯ" : "свежая"));
        }

        // ------------------------------------------------------------------
        // Решение о матрице — на складе стенда
        // ------------------------------------------------------------------

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
                CreatedUtc = new DateTime(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc),
                BuildSeconds = 1.0
            };
        }

        static void MatrixCases(string geometryPath, string otherPath)
        {
            Console.WriteLine();
            Console.WriteLine("== решение о матрице (MatrixStale), склад стенда ==");

            EfficiencyConfigData config = Curve("м", GeometryModel.Load(geometryPath), Fresh(), 7.0);
            string path = ResponseMatrixStore.PathOf(config.Guid);
            string pending = ResponseMatrixStore.PathOf(config.Guid, ResponseMatrixSource.Pending);
            Console.WriteLine("  файл склада стенда: {0}", path);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var options = new ResponseMatrixOptions { NodeCount = 6, MinEnergyKev = 40.0, MaxEnergyKev = 3000.0,
                                                      Histories = 1000 };
            try
            {
                // а. Файла нет — не считали, не старая.
                MatrixCase("а. матрицы нет", config, false);

                // б. ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: нынешний формат, клеймо сходится — годная.
                Synthetic(config.Geometry, options, null).Save(path);
                int fileFormat, physics;
                Check(ResponseMatrix.PeekVersions(path, out fileFormat, out physics)
                      && fileFormat == ResponseMatrix.FormatVersion && physics == ResponseMatrix.PhysicsVersion,
                      "б. записанная матрица читается заголовком как нынешняя");
                MatrixCase("б. годная матрица нынешнего формата", config, false);

                // в. Прежний формат файла.
                WriteFormat(path, ResponseMatrix.FormatVersion - 2);
                MatrixCase("в. формат " + (ResponseMatrix.FormatVersion - 2).ToString(CultureInfo.InvariantCulture),
                           config, true);
                WriteFormat(path, ResponseMatrix.PreviousFormatVersion);
                MatrixCase("в2. предыдущий формат " + ResponseMatrix.PreviousFormatVersion.ToString(CultureInfo.InvariantCulture),
                           config, true);

                // г. Нынешний формат, но поколение переноса в клейме отстало.
                string stale = ResponseMatrix.ComputeStamp(config.Geometry, options);
                stale = "phys=" + (ResponseMatrix.PhysicsVersion - 1).ToString(CultureInfo.InvariantCulture)
                        + stale.Substring(stale.IndexOf(';'));
                Synthetic(config.Geometry, options, stale).Save(path);
                MatrixCase("г. клеймо поколения " + (ResponseMatrix.PhysicsVersion - 1).ToString(CultureInfo.InvariantCulture),
                           config, true);

                // д. Матрица годная, а геометрию правили после счёта.
                Synthetic(config.Geometry, options, null).Save(path);
                MatrixCase("д. (контроль) та же геометрия", config, false);
                GeometryModel was = config.Geometry;
                config.Geometry = GeometryModel.Load(otherPath);
                config.Geometry.FwhmAt662Percent = 7.0;
                MatrixCase("д. геометрия другая после счёта", config, true);
                config.Geometry = was;

                // е. Чужая метка — не матрица, не старая.
                WriteMagic(path, "ZZZZ");
                MatrixCase("е. файл с чужой меткой", config, false);
                WriteMagic(path, "BQRM");

                // ж. Обрубок нынешнего формата — беда, но не старость.
                byte[] whole = File.ReadAllBytes(path);
                File.WriteAllBytes(path, Sub(whole, whole.Length / 2));
                MatrixCase("ж. обрубок", config, false);
                File.WriteAllBytes(path, whole);

                // з. Ждущая сохранения матрица новее склада: судится она.
                MatrixCase("з. (контроль) склад годен", config, false);
                File.Copy(path, pending, true);
                WriteFormat(pending, ResponseMatrix.FormatVersion - 2);
                MatrixCase("з. ждущая — прежнего формата, склад годен", config, true);
                File.Delete(pending);
                MatrixCase("з2. ждущая снята", config, false);

                // и. Без геометрии матрицы не бывает.
                EfficiencyConfigData bare = new EfficiencyConfigData("без геометрии");
                File.Copy(path, ResponseMatrixStore.PathOf(bare.Guid), true);
                WriteFormat(ResponseMatrixStore.PathOf(bare.Guid), ResponseMatrix.FormatVersion - 2);
                try
                {
                    MatrixCase("и. кривая без геометрии при старом файле", bare, false);
                }
                finally
                {
                    File.Delete(ResponseMatrixStore.PathOf(bare.Guid));
                }

                Check(!(bool)MatrixStale.Invoke(null, new object[] { null }), "к. null — не старая, без исключения");
            }
            finally
            {
                try { File.Delete(path); } catch (IOException) { }
                try { if (File.Exists(pending)) File.Delete(pending); } catch (IOException) { }
            }
        }

        static void MatrixCase(string title, EfficiencyConfigData config, bool want)
        {
            bool got = (bool)MatrixStale.Invoke(null, new object[] { config });
            Console.WriteLine("  {0,-56} -> {1}", title, got ? "СТАРАЯ" : "не старая");
            Check(got == want, title + ": ждали " + (want ? "СТАРАЯ" : "не старая"));
        }

        // ------------------------------------------------------------------
        // Набор Guid прибора
        // ------------------------------------------------------------------

        static void GuidSet(string geometryPath)
        {
            Console.WriteLine();
            Console.WriteLine("== набор Guid (StaleEfficiencyGuids) ==");
            int build = ResponseMatrix.PhysicsVersion;
            string old = Fresh().Replace("phys=" + build.ToString(CultureInfo.InvariantCulture) + ";",
                                         "phys=" + (build - 4).ToString(CultureInfo.InvariantCulture) + ";");
            EfficiencyConfigData fresh = Curve("свежая", GeometryModel.Load(geometryPath), Fresh(), 7.0);
            EfficiencyConfigData stale = Curve("старая", GeometryModel.Load(geometryPath), old, 7.0);
            EfficiencyConfigData manual = new EfficiencyConfigData("ручная");
            DeviceConfigInfo device = new DeviceConfigInfo();
            device.EfficiencyConfigs.Add(fresh);
            device.EfficiencyConfigs.Add(stale);
            device.EfficiencyConfigs.Add(manual);

            HashSet<string> got = (HashSet<string>)StaleGuids.Invoke(null, new object[] { device });
            Console.WriteLine("  три кривые (свежая, старая, ручная) -> с меткой {0}",
                              got.Count.ToString(CultureInfo.InvariantCulture));
            Check(got.Count == 1 && got.Contains(stale.Guid), "в наборе ровно Guid старой кривой");
            HashSet<string> empty = (HashSet<string>)StaleGuids.Invoke(null, new object[] { null });
            Check(empty != null && empty.Count == 0, "null-конфигурация — пустой набор");
            Check(((bool)EfficiencyStale.Invoke(null, new object[] { stale }))
                  && !((bool)EfficiencyStale.Invoke(null, new object[] { fresh })),
                  "EfficiencyStale: старая — да, свежая — нет");
        }

        // ------------------------------------------------------------------
        // Форма без показа: набор после заполнения и после записи матрицы;
        // строка списка — в картинку
        // ------------------------------------------------------------------

        static void FormCases(string geometryPath)
        {
            Console.WriteLine();
            Console.WriteLine("== форма без показа ==");

            MethodInfo load = typeof(DeviceConfigForm).GetMethod("LoadEfficiencyTab", NPI);
            MethodInfo saved = typeof(DeviceConfigForm).GetMethod("responseMatrixForm_MatrixSaved", NPI);
            MethodInfo draw = typeof(DeviceConfigForm).GetMethod("efficiencyCombo_DrawItem", NPI);
            FieldInfo setField = typeof(DeviceConfigForm).GetField("efficiencyStaleGuids", NPI);
            FieldInfo comboField = typeof(DeviceConfigForm).GetField("efficiencyCombo", NPI);
            EventInfo evt = typeof(ResponseMatrixForm).GetEvent("MatrixSaved");
            MethodInfo raise = typeof(ResponseMatrixForm).GetMethod("OnMatrixSaved", NPI);
            if (load == null || saved == null || draw == null || setField == null || comboField == null
                || evt == null || raise == null)
            {
                Check(false, "члены формы (LoadEfficiencyTab / responseMatrixForm_MatrixSaved / "
                             + "efficiencyCombo_DrawItem / efficiencyStaleGuids / efficiencyCombo / "
                             + "ResponseMatrixForm.MatrixSaved / OnMatrixSaved) найдены");
                return;
            }

            DeviceType.InitializeDeviceTypes();
            ThermometerType.InitializeThermometerTypes();

            EfficiencyConfigData fresh = Curve("свежая", GeometryModel.Load(geometryPath), Fresh(), 7.0);
            EfficiencyConfigData eff = Curve("с матрицей", GeometryModel.Load(geometryPath), Fresh(), 7.0);
            DeviceConfigInfo device = new DeviceConfigInfo();
            device.EfficiencyConfigs.Add(fresh);
            device.EfficiencyConfigs.Add(eff);
            device.ActiveEfficiencyGuid = eff.Guid;

            string path = ResponseMatrixStore.PathOf(eff.Guid);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var options = new ResponseMatrixOptions { NodeCount = 6, MinEnergyKev = 40.0, MaxEnergyKev = 3000.0,
                                                      Histories = 1000 };
            try
            {
                Synthetic(eff.Geometry, options, null).Save(path);
                WriteFormat(path, ResponseMatrix.FormatVersion - 2);
                using (DeviceConfigForm form = new DeviceConfigForm())
                {
                    ComboBox combo = (ComboBox)comboField.GetValue(form);
                    Check(combo.DrawMode == DrawMode.OwnerDrawFixed, "список рисуется своими руками (OwnerDrawFixed)");

                    load.Invoke(form, new object[] { device });
                    HashSet<string> set = (HashSet<string>)setField.GetValue(form);
                    Console.WriteLine("  после заполнения списка: с меткой {0} (старая матрица у «{1}»)",
                                      set.Count.ToString(CultureInfo.InvariantCulture), eff.Name);
                    Check(set.Count == 1 && set.Contains(eff.Guid), "после заполнения метка у кривой со старой матрицей");
                    Check(combo.Items.Count == 3, "в списке «(none)» и две кривые");

                    // Строка списка — в картинку. Старая: есть красные и белые точки; свежая: красных нет.
                    int staleIndex = combo.Items.IndexOf(eff);
                    int freshIndex = combo.Items.IndexOf(fresh);
                    int redStale, whiteStale, redFresh, whiteFresh;
                    Render(form, draw, combo, staleIndex, out redStale, out whiteStale);
                    Render(form, draw, combo, freshIndex, out redFresh, out whiteFresh);
                    Console.WriteLine("  строка старой: красных {0}, белых внутри квадрата {1}; строка свежей: красных {2}",
                                      redStale.ToString(CultureInfo.InvariantCulture),
                                      whiteStale.ToString(CultureInfo.InvariantCulture),
                                      redFresh.ToString(CultureInfo.InvariantCulture));
                    Check(redStale > 0 && whiteStale > 0, "у старой строки красный квадрат с белым внутри");
                    Check(redFresh == 0, "у свежей строки красных точек нет (положительный контроль)");

                    // Матрица пересчитана и записана окном — метка уходит без перезаполнения списка.
                    Synthetic(eff.Geometry, options, null).Save(path);
                    Check(((HashSet<string>)setField.GetValue(form)).Contains(eff.Guid),
                          "до события набор прежний (положительный контроль)");
                    using (ResponseMatrixForm matrixForm = new ResponseMatrixForm(eff))
                    {
                        evt.AddEventHandler(matrixForm,
                                            Delegate.CreateDelegate(evt.EventHandlerType, form, saved));
                        raise.Invoke(matrixForm, null);
                    }

                    set = (HashSet<string>)setField.GetValue(form);
                    Console.WriteLine("  после MatrixSaved с годной матрицей: с меткой {0}",
                                      set.Count.ToString(CultureInfo.InvariantCulture));
                    Check(set.Count == 0, "после записи годной матрицы метка ушла");
                }
            }
            finally
            {
                try { File.Delete(path); } catch (IOException) { }
            }

            // Сам рисунок метки: ширина больше стороны квадрата, точки красные и белые.
            using (Bitmap bmp = new Bitmap(120, 15))
            using (Graphics g = Graphics.FromImage(bmp))
            using (Font font = new Font("Microsoft Sans Serif", 8.25f))
            {
                g.Clear(Color.White);
                int width = (int)DrawMark.Invoke(null, new object[] { g, new Rectangle(0, 0, 120, 15), font });
                int red, white;
                CountSquare(bmp, new Rectangle(1, 1, 13, 13), out red, out white);
                Console.WriteLine("  DrawStaleMark 120x15: ширина {0}, красных {1}, белых в квадрате {2}",
                                  width.ToString(CultureInfo.InvariantCulture),
                                  red.ToString(CultureInfo.InvariantCulture),
                                  white.ToString(CultureInfo.InvariantCulture));
                Check(width >= 14 && width <= 20, "ширина метки — квадрат в строку плюс просвет");
                Check(red > 60 && white > 4, "квадрат красный, знак белый");
            }
        }

        static void Render(DeviceConfigForm form, MethodInfo draw, ComboBox combo, int index,
                           out int red, out int white)
        {
            using (Bitmap bmp = new Bitmap(200, combo.ItemHeight))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                Rectangle bounds = new Rectangle(0, 0, bmp.Width, bmp.Height);
                var e = new DrawItemEventArgs(g, combo.Font, bounds, index, DrawItemState.None,
                                              Color.Black, Color.White);
                draw.Invoke(form, new object[] { combo, e });
                CountSquare(bmp, new Rectangle(1, 1, bmp.Height - 2, bmp.Height - 2), out red, out white);
            }
        }

        static void CountSquare(Bitmap bmp, Rectangle square, out int red, out int white)
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
                    else if (square.Contains(x, y) && c.R > 200 && c.G > 200 && c.B > 200)
                    {
                        white++;
                    }
                }
            }
        }

        // ------------------------------------------------------------------
        // Цена чтения одной настоящей матрицы
        // ------------------------------------------------------------------

        static void Timing(string realPath, string geometryPath)
        {
            Console.WriteLine();
            Console.WriteLine("== цена MatrixStale на настоящей матрице ==");
            if (realPath == null)
            {
                string store = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                            "BecqMoni", "config", "device", "response");
                long best = -1;
                if (Directory.Exists(store))
                {
                    foreach (string f in Directory.GetFiles(store, "*.rmx"))
                    {
                        long length = new FileInfo(f).Length;
                        if (length > best)
                        {
                            best = length;
                            realPath = f;
                        }
                    }
                }
            }

            if (realPath == null || !File.Exists(realPath))
            {
                Console.WriteLine("  настоящей матрицы нет (--real=<файл.rmx>) — цена не мерена, отказа нет");
                return;
            }

            EfficiencyConfigData config = Curve("настоящая", GeometryModel.Load(geometryPath), Fresh(), 7.0);
            string path = ResponseMatrixStore.PathOf(config.Guid);
            try
            {
                File.Copy(realPath, path, true);
                Console.WriteLine("  файл {0}, {1} байт", Path.GetFileName(realPath),
                                  new FileInfo(path).Length.ToString(CultureInfo.InvariantCulture));
                var watch = new Stopwatch();
                for (int i = 0; i < 5; i++)
                {
                    watch.Restart();
                    bool stale = (bool)MatrixStale.Invoke(null, new object[] { config });
                    watch.Stop();
                    Console.WriteLine("  чтение {0}: {1} мс -> {2}", (i + 1).ToString(CultureInfo.InvariantCulture),
                                      watch.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture),
                                      stale ? "СТАРАЯ" : "не старая");
                }
            }
            finally
            {
                try { File.Delete(path); } catch (IOException) { }
            }
        }

        // ------------------------------------------------------------------
        // Оснастка
        // ------------------------------------------------------------------

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

        static byte[] Sub(byte[] data, int length)
        {
            byte[] part = new byte[length];
            Array.Copy(data, part, length);
            return part;
        }

        static void WriteFormat(string path, int format)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream))
            {
                stream.Position = 4;
                writer.Write(format);
            }
        }

        static void WriteMagic(string path, string magic)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                stream.Position = 0;
                byte[] bytes = Encoding.ASCII.GetBytes(magic);
                stream.Write(bytes, 0, bytes.Length);
            }
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
