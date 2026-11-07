using BecquerelMonitor;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace MainFormTimerProbeA293
{
    /// <summary>
    /// ПРИЁМКА `AMBER293` (05.11.2026): `NullReferenceException` в `MainForm.OnTimer`,
    /// `MainForm.cs:740` (`this.globalConfigManager.GlobalConfig.ChartViewConfig.ChartRefreshCycle`).
    ///
    ///     mainformtimerprobea293 --sandbox=&lt;каталог для копий конфига&gt;
    ///
    /// ПРИЧИНА. У `MainForm` два конструктора. `MainForm()` — дизайнера: зовёт только
    /// `InitializeComponent`, поля `globalConfigManager` не ставит. Таймер же заводит не конструктор,
    /// а `MainForm_Load`, подписанный в `InitializeComponent`, поэтому `new MainForm(); Show()` даёт
    /// таймер при `globalConfigManager == null`, и падает первый же такт. Приложение зовёт только
    /// `new MainForm(args)` (`Program.cs:95`), где поле ставится до всякого такта.
    ///
    /// ⛔ ОКОН НЕТ: `MainForm` не показывается, `MainForm_Load` не идёт, такт зовётся отражением.
    /// Сообщения приложения уходят в шов `AppUi.Probe`, поэтому идёт путь ПОЛЬЗОВАТЕЛЯ
    /// (`AppUi.HasWindows` истинно), но модального окна нет. Конфиг человека не читается и не
    /// пишется: сцены — правленые КОПИИ поставочного `config\BecquerelMonitor.xml` каталога проб в
    /// `--sandbox`, по процессу на сцену (менеджер конфига — одиночка), путь задаётся отражением.
    ///
    /// Сцены: `harness` — `new MainForm()`, обязана ВОСПРОИЗВЕСТИ падение (положительный контроль
    /// мерки). Остальные — `new MainForm(args)`, падения быть не должно: `normal`, `empty` (пустой
    /// узел), `absent` (узла нет), `broken` (файл оборван), `nilroot` (`xsi:nil` у корня), `nil-*`
    /// (`xsi:nil` у вложенных настроек).
    ///
    /// Ожидание: «ВСЕ СОШЛИСЬ», код 0.
    /// </summary>
    static class Program
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        const string HarnessScene = "harness";

        /// <summary>Вложенные настройки `GlobalConfigInfo`, у которых есть умолчание-объект.</summary>
        static readonly string[] NestedNodes = { "ColorConfig", "ChartViewConfig", "MeasurementConfig", "SoundConfig", "FsaCalculation" };

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            string sandbox = null, scene = null;
            foreach (string a in args)
            {
                if (a.StartsWith("--sandbox=", StringComparison.Ordinal)) sandbox = a.Substring(10);
                else if (a.StartsWith("--scene=", StringComparison.Ordinal)) scene = a.Substring(8);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }
            if (sandbox == null)
            {
                Console.Error.WriteLine("нужен --sandbox=<каталог>");
                return 2;
            }
            Directory.CreateDirectory(sandbox);
            return scene == null ? Driver(sandbox) : RunScene(scene, sandbox);
        }

        // ------------------------------------------------------------------
        //  водитель: по процессу на сцену
        // ------------------------------------------------------------------

        static int Driver(string sandbox)
        {
            string exeDir = Path.GetDirectoryName(typeof(Program).Assembly.Location);
            string supply = File.ReadAllText(Path.Combine(exeDir, "config", "BecquerelMonitor.xml"));
            Console.WriteLine("сборка приложения: {0}", typeof(GlobalConfigManager).Assembly.Location);

            var scenes = new List<KeyValuePair<string, string>>
            {
                Pair(HarnessScene, supply),
                Pair("normal", supply),
                Pair("empty", ReplaceNode(supply, "ChartViewConfig", "<ChartViewConfig />")),
                Pair("absent", ReplaceNode(supply, "ChartViewConfig", "")),
                Pair("broken", supply.Substring(0, supply.Length / 2)),
                Pair("nilroot", Regex.Replace(Regex.Replace(supply, "<GlobalConfigInfo ([^>]*)>", "<GlobalConfigInfo $1 xsi:nil=\"true\" />"),
                                              "(?s)(xsi:nil=\"true\" />).*$", "$1")),
            };
            foreach (string node in NestedNodes)
            {
                scenes.Add(Pair("nil-" + node, ReplaceNode(supply, node, "<" + node + " xsi:nil=\"true\" />")));
            }

            int bad = 0;
            foreach (KeyValuePair<string, string> s in scenes)
            {
                string file = Path.Combine(sandbox, s.Key + ".xml");
                File.WriteAllText(file, s.Value, new UTF8Encoding(false));
                var start = new ProcessStartInfo(typeof(Program).Assembly.Location,
                                                 "--sandbox=\"" + sandbox + "\" --scene=" + s.Key)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    WorkingDirectory = exeDir,
                };
                using (Process p = Process.Start(start))
                {
                    string err = null;
                    p.ErrorDataReceived += (o, e) => { if (e.Data != null) err += e.Data + "\n"; };
                    p.BeginErrorReadLine();
                    string text = p.StandardOutput.ReadToEnd();
                    p.WaitForExit();
                    Console.WriteLine();
                    Console.WriteLine("--- сцена {0}: код {1} ---", s.Key, p.ExitCode);
                    Console.Write(text);
                    if (!string.IsNullOrEmpty(err)) Console.Write(err);
                    if (p.ExitCode != 0) bad++;
                }
            }

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "НЕ СОШЛОСЬ: сцен " + bad.ToString(CultureInfo.InvariantCulture));
            return bad == 0 ? 0 : 1;
        }

        static KeyValuePair<string, string> Pair(string name, string xml)
        {
            return new KeyValuePair<string, string>(name, xml);
        }

        static string ReplaceNode(string xml, string node, string with)
        {
            var re = new Regex("(?s)<" + node + @"(\s[^>]*)?(/>|>.*?</" + node + ">)");
            // Узла нет в файле (поставочный конфиг старше настройки) — ставится перед концом корня.
            return re.IsMatch(xml) ? re.Replace(xml, with, 1) : xml.Replace("</GlobalConfigInfo>", with + "</GlobalConfigInfo>");
        }

        // ------------------------------------------------------------------
        //  одна сцена
        // ------------------------------------------------------------------

        static int RunScene(string scene, string sandbox)
        {
            var reports = new List<string>();
            AppUi.Probe = (kind, text, title) =>
            {
                reports.Add(kind + ": " + Regex.Replace(text ?? "", @"\s+", " "));
                return false;
            };
            bool harness = scene == HarnessScene;
            FieldInfo managerField = typeof(MainForm).GetField("globalConfigManager", Any);

            MainForm form;
            try
            {
                FieldInfo pathField = typeof(GlobalConfigManager).GetField("becqMoniMainConfig", Any);
                FieldInfo instance = typeof(GlobalConfigManager).GetField("instance", Any);
                pathField.SetValue(instance.GetValue(null), Path.Combine(sandbox, scene + ".xml"));
                if (harness)
                {
                    // Обвязка: конструктор дизайнера и затем то, с чего начинает `MainForm_Load`.
                    form = new MainForm();
                    DeviceConfigManager.GetInstance();
                }
                else
                {
                    // ⛔ Единственный конструктор приложения: `Program.cs:95`, `new MainForm(args)`.
                    form = new MainForm(new string[0]);
                }
                GlobalConfigInfo info = GlobalConfigManager.GetInstance().GlobalConfig;
                Console.WriteLine("  чтение: GlobalConfig {0}, ChartViewConfig {1}, ChartRefreshCycle {2}; сообщений {3}",
                                  info == null ? "null" : "есть",
                                  info == null || info.ChartViewConfig == null ? "null" : "есть",
                                  info == null || info.ChartViewConfig == null ? "—" : info.ChartViewConfig.ChartRefreshCycle.ToString(CultureInfo.InvariantCulture),
                                  reports.Count);
                foreach (string r in reports) Console.WriteLine("    {0}", Short(r));
                Console.WriteLine("  конструктор {0}: дошёл, поле MainForm.globalConfigManager {1}",
                                  harness ? "MainForm()" : "MainForm(args)",
                                  managerField.GetValue(form) == null ? "null" : "есть");
            }
            catch (Exception ex)
            {
                Console.WriteLine("  ⛔ конструктор или чтение: {0}", Describe(Unwrap(ex)));
                return 1;
            }

            MethodInfo tick = typeof(MainForm).GetMethod("OnTimer", Any);
            Exception fault = Tick(tick, form);
            Console.WriteLine("  такт: {0}", fault == null ? "без исключения" : Describe(fault));
            form.Dispose();
            Exception afterDispose = Tick(tick, form);
            Console.WriteLine("  такт после Dispose: {0}", afterDispose == null ? "без исключения" : Describe(afterDispose));

            if (harness)
            {
                // ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ МЕРКИ: падение из лога обязано воспроизводиться здесь, тем
                // же исключением и в том же методе. Не воспроизвелось — мерка больше не про него.
                bool same = fault is NullReferenceException && Describe(fault).Contains("MainForm.OnTimer")
                            && managerField.GetValue(form) == null;
                Console.WriteLine(same ? "  воспроизведено: падение из лога, причина — конструктор дизайнера"
                                       : "  ⛔ падение из лога больше не воспроизводится");
                return same ? 0 : 1;
            }
            return fault == null && afterDispose == null ? 0 : 1;
        }

        static Exception Tick(MethodInfo tick, MainForm form)
        {
            try
            {
                tick.Invoke(form, new object[] { null, EventArgs.Empty });
                return null;
            }
            catch (TargetInvocationException ex)
            {
                return ex.InnerException;
            }
        }

        static Exception Unwrap(Exception ex)
        {
            while (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
            return ex;
        }

        static string Describe(Exception ex)
        {
            string top = (ex.StackTrace ?? "").Split('\n').FirstOrDefault() ?? "";
            top = Regex.Replace(top.Trim(), @"[A-Za-z]:[\\/].*[\\/]([^\\/]+\.cs)", "$1");   // путь сборки в печать не идёт
            return ex.GetType().Name + " — " + top;
        }

        static string Short(string text)
        {
            return text.Length <= 160 ? text : text.Substring(0, 160) + "…";
        }
    }
}
