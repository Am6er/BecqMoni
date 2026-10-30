using BecquerelMonitor;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace BecquerelMonitor.Probes
{
    /// <summary>
    /// ВКЛАДКА «КАЛИБРОВКА ЭНЕРГИИ» НАСТРОЕК ПРИБОРА И СНЯТЫЙ АВТОКАЛИБРАТОР (`AMBER280`,
    /// 29.10.2026). Amber, консоль, дословно: «1. Автокалибратор - полностью удалить из
    /// бекмони. 2. Во вкладке убрать функциональность калибровки и оставить только данные.
    /// Кнопки чтения калибровки и записи калибровки в устройство - оставить, это важно. 3.
    /// Оставить калибровку 2 порядка для устройств RadiaCode.» Вопросником: коэффициенты —
    /// «Только показ»; Obsidian — «Да, как RadiaCode (Рекомендую)».
    ///
    /// Проба собирается против НОВОЙ сборки и идёт из каталога и новой, и ПРЕЖНЕЙ (всё,
    /// чего у одной из них нет, — отражением): прежняя — положительный контроль.
    ///
    ///   devicecalibrationtabprobea280 --mode=read --dirs=&lt;каталог&gt;[;&lt;каталог&gt;…] --tmp=&lt;каталог&gt;
    ///       каждый `*.xml` конфигурации прибора — тем же чтением, что
    ///       `DeviceConfigManager.LoadAllConfigFiles` (XmlSerializer, только чтение, ветка
    ///       `FormatVersion` ≠ 120920 → `DeviceConfigInfo_097b`), копия `new DeviceConfigInfo(info)`,
    ///       запись и повторное чтение. Печатает, сколько в файле `&lt;StabilizerConfig&gt;` и
    ///       `&lt;TargetPeak&gt;`, что прочла сборка (у прежней — число опор отражением) и есть
    ///       ли элемент в пересохранённом файле. ⛔ Положительный контроль чтения — копия
    ///       первого файла с НЕПУСТЫМИ опорами (Cs-137 661.657, 5 %): прежняя сборка обязана
    ///       прочесть опору, новая — открыть файл без ошибки.
    ///   ... --mode=form --tmp=&lt;каталог&gt;
    ///       настоящее окно настроек (`DeviceConfigForm`, без показа): для RadiaCode, Obsidian,
    ///       AtomSpectra (VCP и TCP/REST), звукового входа — видимость и доступность «Читать с
    ///       устройства» / «Записать в устройство», поле калибровки 2-го порядка, поля
    ///       коэффициентов только на чтение, панелей многоточечной калибровки и
    ///       автокалибратора нет. Запись (`SaveFormContents`) шкалы, которую поле печатает
    ///       неточно (0.00091681334111664073), — побитово та же (прежняя сборка: соседний
    ///       double — контроль). AtomSpectra TCP: «прочитанная» калибровка
    ///       (`ApplyAtomSpectraCalibration`) после записи — побитово та же.
    ///
    /// Коды: 0 — сошлось; 1 — не сошлось; 2 — ключи; 3 — сбой.
    /// </summary>
    static class DeviceCalibrationTabProbeA280
    {
        static int bad;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = Inv;
            var opt = new Dictionary<string, string>();
            foreach (string a in args)
            {
                int eq = a.IndexOf('=');
                if (!a.StartsWith("--", StringComparison.Ordinal) || eq < 0)
                {
                    Console.Error.WriteLine("неизвестный ключ: " + a);
                    return 2;
                }
                opt[a.Substring(2, eq - 2)] = a.Substring(eq + 1);
            }
            string mode;
            if (!opt.TryGetValue("mode", out mode))
            {
                Console.Error.WriteLine("нужен --mode=read|form");
                return 2;
            }
            try
            {
                Console.WriteLine("сборка: " + typeof(DeviceConfigInfo).Assembly.Location + " ("
                                  + File.GetLastWriteTime(typeof(DeviceConfigInfo).Assembly.Location).ToString("yyyy-MM-dd HH:mm:ss", Inv) + ")");
                Console.WriteLine("свойство DeviceConfigInfo.StabilizerConfig: "
                                  + (typeof(DeviceConfigInfo).GetProperty("StabilizerConfig") != null ? "ЕСТЬ (прежняя сборка)" : "нет"));
                switch (mode)
                {
                    case "read": return Read(Need(opt, "dirs").Split(';'), Need(opt, "tmp"));
                    case "form": return Form(Need(opt, "tmp"));
                    default:
                        Console.Error.WriteLine("неизвестный режим: " + mode);
                        return 2;
                }
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 2;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("СБОЙ: " + Inner(ex));
                return 3;
            }
        }

        static string Need(Dictionary<string, string> opt, string key)
        {
            string v;
            if (!opt.TryGetValue(key, out v) || string.IsNullOrEmpty(v))
            {
                throw new ArgumentException("нужен --" + key + "=");
            }
            return v;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  read
        // ══════════════════════════════════════════════════════════════════════

        static int Read(string[] dirs, string tmp)
        {
            Directory.CreateDirectory(tmp);
            XmlSerializer old = new XmlSerializer(typeof(DeviceConfigInfo_097b));
            XmlSerializer cur = new XmlSerializer(typeof(DeviceConfigInfo));
            int files = 0, withElement = 0, withPeaks = 0, failed = 0, savedWithElement = 0;
            string first = null;
            foreach (string dir in dirs.Where(d => d.Length > 0))
            {
                Console.WriteLine();
                Console.WriteLine("=== каталог: " + Path.GetFileName(dir.TrimEnd('\\', '/')) + " ===");
                foreach (string path in Directory.GetFiles(dir, "*.xml").OrderBy(p => p, StringComparer.Ordinal))
                {
                    files++;
                    if (first == null) first = path;
                    string text = File.ReadAllText(path);
                    int el = Count(text, "<StabilizerConfig");
                    int tp = Count(text, "<TargetPeak>");
                    if (el > 0) withElement++;
                    if (tp > 0) withPeaks++;
                    string outcome;
                    bool ok = Roundtrip(path, cur, old, tmp, out outcome, ref savedWithElement);
                    if (!ok) failed++;
                    Console.WriteLine("  {0} {1}: <StabilizerConfig> {2}, <TargetPeak> {3}; {4}", ok ? "да " : "НЕТ", Path.GetFileName(path), el, tp, outcome);
                }
            }
            Console.WriteLine();
            Console.WriteLine("=== ⛔ положительный контроль: копия первого файла с непустыми опорами ===");
            string seeded = Path.Combine(tmp, "seeded_targetpeaks.xml");
            string src = File.ReadAllText(first);
            string withPeak = Regex.Replace(src, @"<TargetPeaks\s*/>|<TargetPeaks>\s*</TargetPeaks>",
                "<TargetPeaks><TargetPeak><Nuclide>Cs-137</Nuclide><Energy>661.657</Energy><Error>5</Error></TargetPeak></TargetPeaks>");
            Check("опора вписана в копию " + Path.GetFileName(first), withPeak != src && Count(withPeak, "<TargetPeak>") == 1);
            File.WriteAllText(seeded, withPeak);
            string seededOutcome;
            int dummy = 0;
            bool seededOk = Roundtrip(seeded, cur, old, tmp, out seededOutcome, ref dummy);
            Console.WriteLine("  копия: " + seededOutcome);
            Check("файл с непустыми опорами открыт без ошибки", seededOk);
            bool oldBuild = typeof(DeviceConfigInfo).GetProperty("StabilizerConfig") != null;
            if (oldBuild)
            {
                Check("контроль: прежняя сборка прочла опору (элемент — данные, а не пустая оболочка)",
                      seededOutcome.Contains("опор прочитано 1"));
            }
            Console.WriteLine();
            Console.WriteLine("файлов {0}: с <StabilizerConfig> {1}, с опорами {2}; не открылось {3}; пересохранено с элементом {4}",
                              files, withElement, withPeaks, failed, savedWithElement);
            Check("все файлы открыты без ошибки", failed == 0);
            if (!oldBuild)
            {
                Check("в пересохранённых файлах элемента <StabilizerConfig> нет", savedWithElement == 0);
            }
            Console.WriteLine(bad == 0 ? "СОШЛОСЬ" : "НЕ СОШЛОСЬ: " + bad);
            return bad == 0 ? 0 : 1;
        }

        static bool Roundtrip(string path, XmlSerializer cur, XmlSerializer old, string tmp, out string outcome, ref int savedWithElement)
        {
            try
            {
                DeviceConfigInfo info;
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    info = (DeviceConfigInfo)cur.Deserialize(fs);
                }
                string branch = "120920";
                if (!(info.FormatVersion == "120920"))
                {
                    branch = "097b";
                    using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        info = new DeviceConfigInfo((DeviceConfigInfo_097b)old.Deserialize(fs));
                    }
                }
                DeviceConfigInfo copy = new DeviceConfigInfo(info);
                string peaks = "";
                PropertyInfo sp = typeof(DeviceConfigInfo).GetProperty("StabilizerConfig");
                if (sp != null)
                {
                    object sc = sp.GetValue(info, null);
                    System.Collections.ICollection list = sc == null ? null
                        : (System.Collections.ICollection)sc.GetType().GetProperty("TargetPeaks").GetValue(sc, null);
                    peaks = ", опор прочитано " + (list == null ? "(нет)" : list.Count.ToString(Inv));
                }
                string saved = Path.Combine(tmp, "resaved_" + Path.GetFileName(path));
                using (StreamWriter sw = new StreamWriter(saved, false, new UTF8Encoding(true)))
                {
                    cur.Serialize(sw, copy);
                }
                string back = File.ReadAllText(saved);
                bool hasElement = back.Contains("<StabilizerConfig");
                if (hasElement) savedWithElement++;
                DeviceConfigInfo again;
                using (FileStream fs = new FileStream(saved, FileMode.Open, FileAccess.Read))
                {
                    again = (DeviceConfigInfo)cur.Deserialize(fs);
                }
                bool sameCal = SameBits(info.EnergyCalibration, again.EnergyCalibration);
                outcome = string.Format(Inv, "прочтён ({0}, «{1}», {2}){3}; пересохранён: <StabilizerConfig> {4}, шкала после круга {5}",
                                        branch, info.Name, info.DeviceType, peaks, hasElement ? "ЕСТЬ" : "нет", sameCal ? "та же" : "ДРУГАЯ");
                return sameCal;
            }
            catch (Exception ex)
            {
                outcome = "ОШИБКА: " + Inner(ex);
                return false;
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  form
        // ══════════════════════════════════════════════════════════════════════

        static int Form(string tmp)
        {
            Directory.CreateDirectory(tmp);
            DeviceType.InitializeDeviceTypes();
            ThermometerType.InitializeThermometerTypes();
            GlobalConfigManager.GetInstance();
            MethodInfo load = typeof(DeviceConfigForm).GetMethod("LoadFormContents", F);
            MethodInfo save = typeof(DeviceConfigForm).GetMethod("SaveFormContents", F);
            bool newBuild = typeof(DeviceConfigInfo).GetProperty("StabilizerConfig") == null;

            Console.WriteLine();
            Console.WriteLine("=== 1. состав вкладки ===");
            DeviceConfigForm probeForm = new DeviceConfigForm();
            foreach (string gone in new[] { "panel1", "panel2", "table2", "table3", "button1", "button8", "button9", "button10", "button11", "label9", "label19", "label36" })
            {
                bool present = FieldOf(probeForm, gone) != null;
                if (newBuild) Check("снят элемент вкладки " + gone, !present);
                else Console.WriteLine("  (прежняя) элемент {0}: {1}", gone, present ? "есть" : "нет");
            }
            foreach (string box in new[] { "numericUpDown7", "numericUpDown2", "numericUpDown1", "numericUpDown9", "numericUpDown8" })
            {
                TextBox t = (TextBox)FieldOf(probeForm, box);
                if (newBuild) Check("поле коэффициента " + box + " — только на чтение", t != null && t.ReadOnly);
                else Console.WriteLine("  (прежняя) поле {0}: ReadOnly {1}", box, t != null && t.ReadOnly);
            }
            probeForm.Dispose();

            Console.WriteLine();
            Console.WriteLine("=== 2. «Читать с устройства» / «Записать в устройство» по типам приборов ===");
            var rcCal = new PolynomialEnergyCalibration { PolynomialOrder = 2, Coefficients = new[] { -7.5, 2.7594512663313253, 0.00091681334111664073 } };
            // Прибор с серийником и адресом, как настоящая конфигурация: без них форма RadiaCode
            // при записи снимает и калибровку 2-го порядка (`RadiaCodeDeviceForm.SaveFormContents`).
            Case("RadiaCode, с калибровкой 2-го порядка", "RadiaCode", c => { Rc(c); ((RadiaCodeDeviceConfig)c.InputDeviceConfig).RC_EnergyCalibration = (PolynomialEnergyCalibration)rcCal.Clone(); },
                 rcCal, true, true, true, true, load, save, newBuild);
            Case("RadiaCode, без калибровки 2-го порядка", "RadiaCode", c => { Rc(c); ((RadiaCodeDeviceConfig)c.InputDeviceConfig).RC_EnergyCalibration = null; },
                 rcCal, true, true, true, false, load, save, newBuild);
            Case("Obsidian, с калибровкой 2-го порядка", "Obsidian", c => { Obs(c); ((ObsidianDeviceConfig)c.InputDeviceConfig).OBS_EnergyCalibration = (PolynomialEnergyCalibration)rcCal.Clone(); },
                 rcCal, true, true, true, true, load, save, newBuild);
            Case("AtomSpectra VCP", "AtomSpectraVCP", c => { }, Cal3(), true, true, true, true, load, save, newBuild);
            Case("AtomSpectra TCP/REST", AtomSpectraTcpDeviceConfig.DeviceTypeId, c => { }, Cal3(), true, true, false, false, load, save, newBuild);
            Case("звуковой вход", "AudioInputDevice", c => { }, Cal3(), false, false, false, false, load, save, newBuild);

            Console.WriteLine();
            Console.WriteLine("=== 3. AtomSpectra TCP: калибровка «с устройства» → запись ===");
            {
                DeviceConfigForm f = new DeviceConfigForm();
                DeviceConfigInfo cfg = Config(AtomSpectraTcpDeviceConfig.DeviceTypeId, Cal3());
                SetField(f, "activeDeviceConfig", cfg);
                load.Invoke(f, new object[] { cfg });
                double[] board = { -3.25, 0.36096159703669445, 1.0163882916029785E-05, -1.4991396231931037E-09, 1.0081114780778761E-13 };
                f.ApplyAtomSpectraCalibration(board);
                string shown = ((TextBox)FieldOf(f, "numericUpDown2")).Text;
                bool okSave = (bool)save.Invoke(f, new object[] { cfg });
                PolynomialEnergyCalibration got = (PolynomialEnergyCalibration)cfg.EnergyCalibration;
                bool same = got.PolynomialOrder == 4 && got.Coefficients.Take(5).Select(Bits).SequenceEqual(board.Select(Bits));
                Console.WriteLine("  поле «1 коэф.» показывает «{0}»; после записи: {1}", shown, string.Join("; ", got.Coefficients.Select(R)));
                if (newBuild) Check("запись принята, шкала платы побитово та же", okSave && same);
                else Console.WriteLine("  (прежняя) запись {0}, шкала {1}", okSave, same ? "та же" : "ДРУГАЯ (поля разобраны обратно)");
                f.Dispose();
            }
            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "СОШЛОСЬ" : "НЕ СОШЛОСЬ: " + bad);
            return bad == 0 ? 0 : 1;
        }

        static void Rc(DeviceConfigInfo c)
        {
            RadiaCodeDeviceConfig rc = (RadiaCodeDeviceConfig)c.InputDeviceConfig;
            rc.DeviceSerial = "RC-A280";
            rc.AddressBLE = "000000000000";
        }

        static void Obs(DeviceConfigInfo c)
        {
            ObsidianDeviceConfig obs = (ObsidianDeviceConfig)c.InputDeviceConfig;
            obs.DeviceSerial = "OBS-A280";
            obs.AddressBLE = "000000000000";
        }

        static bool EnabledFlag(Control c)
        {
            // Доступность, заданная коду (свойство Enabled ложно, пока погашена вкладка-родитель).
            MethodInfo gs = typeof(Control).GetMethod("GetState", BindingFlags.Instance | BindingFlags.NonPublic);
            return (bool)gs.Invoke(c, new object[] { 4 });
        }

        static PolynomialEnergyCalibration Cal3()
        {
            return new PolynomialEnergyCalibration { PolynomialOrder = 3, Coefficients = new[] { -7.5, 2.7594512663313253, 0.00091681334111664073, -1.0121718693992851E-06, 0.0 } };
        }

        static DeviceConfigInfo Config(string typeId, PolynomialEnergyCalibration cal)
        {
            DeviceType type = DeviceType.DeviceTypeMap[typeId];
            DeviceConfigInfo cfg = new DeviceConfigInfo();
            cfg.InitFormatVersion();
            cfg.ThermometerType = ThermometerType.ThermometerTypeList[0].Id;
            cfg.Filename = "a280_" + typeId + ".xml";
            cfg.Guid = Guid.NewGuid().ToString();
            cfg.Name = "A280 " + typeId;
            cfg.DeviceType = typeId;
            cfg.InputDeviceConfig = (InputDeviceConfig)Activator.CreateInstance(type.DeviceConfigType);
            cfg.NumberOfChannels = 1024;
            cfg.ChannelPitch = 1;
            cfg.DefaultMeasurementTime = 3600;
            cfg.EnergyCalibration = cal.Clone();
            return cfg;
        }

        static void Case(string label, string typeId, Action<DeviceConfigInfo> setup, PolynomialEnergyCalibration cal,
                         bool readVisible, bool readEnabled, bool writeVisible, bool writeEnabled,
                         MethodInfo load, MethodInfo save, bool newBuild)
        {
            Console.WriteLine("  -- " + label);
            DeviceConfigForm f = new DeviceConfigForm();
            DeviceConfigInfo cfg = Config(typeId, cal);
            setup(cfg);
            // как в приложении: выбор строки списка ставит активную конфигурацию ДО чтения полей
            SetField(f, "activeDeviceConfig", cfg);
            load.Invoke(f, new object[] { cfg });
            Button read = (Button)FieldOf(f, "button13");
            Button write = (Button)FieldOf(f, "button14");
            string second = ((TextBox)FieldOf(f, "textBox16")).Text;
            Console.WriteLine("     «Читать с устройства»: видна {0}, доступна {1}; «Записать в устройство»: видна {2}, доступна {3}; 2-й порядок: «{4}»",
                              VisibleFlag(read), EnabledFlag(read), VisibleFlag(write), EnabledFlag(write), second);
            Check("кнопки как прежде (читать " + readVisible + "/" + readEnabled + ", записать " + writeVisible + "/" + writeEnabled + ")",
                  VisibleFlag(read) == readVisible && EnabledFlag(read) == readEnabled && VisibleFlag(write) == writeVisible && EnabledFlag(write) == writeEnabled);
            // запись формы: шкала, которую поля печатают неточно, — побитово та же
            PolynomialEnergyCalibration before = (PolynomialEnergyCalibration)cfg.EnergyCalibration.Clone();
            bool ok = (bool)save.Invoke(f, new object[] { cfg });
            PolynomialEnergyCalibration after = (PolynomialEnergyCalibration)cfg.EnergyCalibration;
            bool same = after.PolynomialOrder == before.PolynomialOrder
                        && after.Coefficients.Take(before.PolynomialOrder + 1).Select(Bits).SequenceEqual(before.Coefficients.Take(before.PolynomialOrder + 1).Select(Bits));
            string poly2 = "";
            if (cfg.InputDeviceConfig is RadiaCodeDeviceConfig rc)
            {
                poly2 = rc.RC_EnergyCalibration == null ? "нет" : string.Join("; ", rc.RC_EnergyCalibration.Coefficients.Select(R));
            }
            else if (cfg.InputDeviceConfig is ObsidianDeviceConfig obs)
            {
                poly2 = obs.OBS_EnergyCalibration == null ? "нет" : string.Join("; ", obs.OBS_EnergyCalibration.Coefficients.Select(R));
            }
            Console.WriteLine("     запись {0}: c2 {1} → {2}{3}", ok ? "принята" : "ОТКАЗ", R(before.Coefficients[2]), R(after.Coefficients[2]),
                              poly2.Length > 0 ? "; калибровка 2-го порядка после записи: " + poly2 : "");
            if (newBuild)
            {
                Check("запись принята, шкала побитово та же", ok && same);
                if (cfg.InputDeviceConfig is RadiaCodeDeviceConfig || cfg.InputDeviceConfig is ObsidianDeviceConfig)
                {
                    Check("калибровка 2-го порядка = шкала степени 2 (копией)", poly2 == string.Join("; ", before.Coefficients.Take(3).Select(R)));
                }
            }
            else
            {
                Console.WriteLine("     (прежняя) шкала после записи " + (same ? "та же" : "ДРУГАЯ — поля разобраны обратно"));
            }
            f.Dispose();
        }

        static bool VisibleFlag(Control c)
        {
            // Видимость, заданная коду (без показа окна свойство Visible всегда ложно).
            MethodInfo gs = typeof(Control).GetMethod("GetState", BindingFlags.Instance | BindingFlags.NonPublic);
            return (bool)gs.Invoke(c, new object[] { 2 });
        }

        static bool SameBits(EnergyCalibration a, EnergyCalibration b)
        {
            PolynomialEnergyCalibration pa = a as PolynomialEnergyCalibration, pb = b as PolynomialEnergyCalibration;
            if (pa == null || pb == null) return a == null && b == null;
            return pa.PolynomialOrder == pb.PolynomialOrder && pa.Coefficients.Select(Bits).SequenceEqual(pb.Coefficients.Select(Bits));
        }

        static object FieldOf(object o, string name)
        {
            for (Type t = o.GetType(); t != null; t = t.BaseType)
            {
                FieldInfo fi = t.GetField(name, F | BindingFlags.DeclaredOnly);
                if (fi != null) return fi.GetValue(o);
            }
            return null;
        }

        static void SetField(object o, string name, object value)
        {
            for (Type t = o.GetType(); t != null; t = t.BaseType)
            {
                FieldInfo fi = t.GetField(name, F | BindingFlags.DeclaredOnly);
                if (fi != null) { fi.SetValue(o, value); return; }
            }
            throw new MissingFieldException(o.GetType().Name, name);
        }

        static int Count(string text, string what)
        {
            int n = 0, at = 0;
            while ((at = text.IndexOf(what, at, StringComparison.Ordinal)) >= 0) { n++; at += what.Length; }
            return n;
        }

        static void Check(string what, bool ok)
        {
            Console.WriteLine("  {0} {1}", ok ? "да " : "НЕТ", what);
            if (!ok) bad++;
        }

        static long Bits(double x)
        {
            return BitConverter.DoubleToInt64Bits(x);
        }

        static string R(double x)
        {
            return x.ToString("R", Inv);
        }

        static string Inner(Exception ex)
        {
            while (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
            return ex.GetType().Name + ": " + ex.Message + " @ " + (ex.StackTrace ?? "").Split('\n')[0].Trim();
        }
    }
}
