using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace FsaFlagsProbe
{
    /// <summary>
    /// Две галки разбора на панели поиска пиков и связь между ними (`S77`).
    ///
    /// ЗАЧЕМ ПРОБА. До 23.08.2026 в поставке обе галки стояли ровно в тех
    /// положениях, при которых видимая не делала НИЧЕГО: «Равновесие» включено
    /// умолчанием, «Enable DB lookups for FSA» — выключено, а связывать ряд
    /// можно только там, где ряд ЕСТЬ. Состав из баз собирает его обходом
    /// `nucdb.decay_chain`; прежний путь строит компоненты по подписям найденных
    /// пиков, и структуры ряда там нет вовсе.
    ///
    /// Решением Amber зависимость сделана ВИДИМОЙ: при выключенном выводе из
    /// баз «Равновесие» гаснет. Здесь у этого правила появляется читатель —
    /// иначе оно снова станет неотличимо от «галка просто не работает».
    ///
    /// Проверяется ЧЕТЫРЕ вещи, и третья не менее важна первых двух:
    ///
    ///   1. вывод из баз выключен -> «Равновесие» недоступно;
    ///   2. вывод из баз включён   -> «Равновесие» доступно;
    ///   3. ЗНАЧЕНИЕ «Равновесия» при гашении НЕ ТРОГАЕТСЯ — погашенная галка
    ///      помнит выбор человека и оживает вместе с соседней. Гасить и
    ///      обнулять — разные вещи, и второе потеряло бы настройку молча;
    ///   4. у погашенной галки есть подсказка, у доступной её нет.
    ///
    ///     fsaflagsprobe
    ///
    /// ВТОРОЙ РАЗДЕЛ (`A145`, этап 1: `A170`, критерий 11) — без окон, до них:
    /// фасад <c>FsaCalculationOptions</c> и ЧЕТЫРЕ внутренних ключа анализатора
    /// (`CascadeSumming`, `CascadeSumPeaks`, `Backscatter`,
    /// `BackscatterWithMatrix`) для всех четырёх положений двух пользовательских
    /// флажков; защита — поднятый до применения `BackscatterWithMatrix` фасад
    /// ОПУСКАЕТ; через фасад его поднять нельзя (члена с таким именем у типа
    /// нет — проверяется отражением); пять новых полей конфигурации переживают
    /// клон, XML и `AdoptFrom`.
    ///
    /// Ожидание: «ВСЕ СОШЛИСЬ».
    /// </summary>
    static class Program
    {
        static int bad;

        [STAThread]
        static int Main()
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo culture = (CultureInfo)Thread.CurrentThread.CurrentCulture.Clone();
            culture.NumberFormat.NumberDecimalSeparator = ".";
            Thread.CurrentThread.CurrentCulture = culture;

            FacadeSection();
            ConfigSection();

            MainForm mainForm = new MainForm();
            DCPeakDetectionView panel = new DCPeakDetectionView(mainForm);
            CheckBox lookups = Field(panel, "checkBoxDbLookups");
            CheckBox equilibrium = Field(panel, "checkBoxEquilibrium");

            Console.WriteLine("=== связь галок разбора (S77) ===");

            // Человек включил «Равновесие» при работающем выводе из баз.
            lookups.Checked = true;
            equilibrium.Checked = true;
            Same("вывод из баз включён: «Равновесие» доступно", true, equilibrium.Enabled);
            Same("подсказки у доступной галки нет", "", HintOf(panel, equilibrium));

            // Выключил вывод из баз — галка обязана погаснуть, но НЕ сброситься.
            lookups.Checked = false;
            Same("вывод из баз выключен: «Равновесие» недоступно", false, equilibrium.Enabled);
            Same("значение «Равновесия» не тронуто", true, equilibrium.Checked);
            bool hasHint = !string.IsNullOrEmpty(HintOf(panel, equilibrium));
            Same("у погашенной галки есть подсказка", true, hasHint);

            // Вернул — галка оживает с прежним значением.
            lookups.Checked = true;
            Same("вернули вывод из баз: «Равновесие» снова доступно", true, equilibrium.Enabled);
            Same("и помнит прежнее значение", true, equilibrium.Checked);

            // И обратный случай: выключенное «Равновесие» тоже переживает
            // гашение — правило про доступность, а не про значение.
            equilibrium.Checked = false;
            lookups.Checked = false;
            Same("выключенное «Равновесие» переживает гашение", false, equilibrium.Checked);

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "НЕ СОШЛОСЬ: " + bad);

            // ⛔ ФОРМЫ УБИРАЮТСЯ ЯВНО, и это не косметика. Без этого процесс
            // печатал «ВСЕ СОШЛИСЬ» и падал уже ПОСЛЕ, на разборе окон:
            // 0xC000041D (STATUS_FATAL_USER_CALLBACK_EXCEPTION), код возврата
            // -1073740771 вместо нуля. Проба, чей текст говорит «сошлось», а
            // код — «упало», хуже молчащей: `build_all.ps1` этого не видит, а
            // тот, кто смотрит на код, читает отказ.
            panel.Dispose();
            mainForm.Dispose();
            return bad == 0 ? 0 : 1;
        }

        /// <summary>
        /// (`A170`) Таблица «фасад → четыре внутренних ключа» для всех четырёх
        /// положений двух пользовательских флажков. Перед каждым применением
        /// анализатору ПОДБРАСЫВАЕТСЯ опасное состояние (`BackscatterWithMatrix
        /// = true`, половины суммирования вразнобой): фасад обязан привести
        /// ключи к своему положению, а не «дописать сверху».
        /// </summary>
        static void FacadeSection()
        {
            Console.WriteLine("=== фасад FsaCalculationOptions → внутренние ключи (A170) ===");
            Console.WriteLine("  {0,-10} {1,-10} | {2,-14} {3,-15} {4,-11} {5}",
                              "суммир.", "рассеяние", "CascadeSumming", "CascadeSumPeaks",
                              "Backscatter", "BackscatterWithMatrix");
            foreach (bool summing in new[] { true, false })
            {
                foreach (bool backscatter in new[] { true, false })
                {
                    var analyzer = new FsaAnalyzer
                    {
                        BackscatterWithMatrix = true,
                        CascadeSumming = !summing,
                        CascadeSumPeaks = summing,
                        Backscatter = !backscatter
                    };
                    new FsaCalculationOptions { CascadeSumming = summing, Backscatter = backscatter }
                        .ApplyTo(analyzer);
                    Console.WriteLine("  {0,-10} {1,-10} | {2,-14} {3,-15} {4,-11} {5}",
                                      summing ? "вкл" : "выкл", backscatter ? "вкл" : "выкл",
                                      analyzer.CascadeSumming, analyzer.CascadeSumPeaks,
                                      analyzer.Backscatter, analyzer.BackscatterWithMatrix);
                    string cell = "суммирование " + (summing ? "вкл" : "выкл")
                                  + ", рассеяние " + (backscatter ? "вкл" : "выкл");
                    Same(cell + ": CascadeSumming", summing, analyzer.CascadeSumming);
                    Same(cell + ": CascadeSumPeaks", summing, analyzer.CascadeSumPeaks);
                    Same(cell + ": Backscatter", backscatter, analyzer.Backscatter);
                    Same(cell + ": BackscatterWithMatrix опущен", false, analyzer.BackscatterWithMatrix);
                    Same(cell + ": EscapeGate не тронут", true, analyzer.EscapeGate);
                }
            }

            // ⛔ Положительный контроль: поднять `BackscatterWithMatrix` через
            // фасад НЕВОЗМОЖНО — у типа нет такого члена. Проверяется
            // отражением, потому что «не компилируется» пробой не покажешь.
            MemberInfo[] hole = typeof(FsaCalculationOptions).GetMember(
                "BackscatterWithMatrix", BindingFlags.Instance | BindingFlags.Static
                                         | BindingFlags.Public | BindingFlags.NonPublic);
            Same("у фасада нет члена BackscatterWithMatrix", 0, hole.Length);
            MemberInfo[] gate = typeof(FsaCalculationOptions).GetMember(
                "EscapeGate", BindingFlags.Instance | BindingFlags.Static
                              | BindingFlags.Public | BindingFlags.NonPublic);
            Same("у фасада нет члена EscapeGate", 0, gate.Length);
            MemberInfo[] hatch = typeof(FsaCalculationOptions).GetMember(
                "SumLayerIncludesContinuum", BindingFlags.Instance | BindingFlags.Static
                                             | BindingFlags.Public | BindingFlags.NonPublic);
            Same("у фасада нет члена SumLayerIncludesContinuum", 0, hatch.Length);

            // Вылеты (`A168`): фасад пишет положительный ключ, гейт остаётся.
            var escapes = new FsaAnalyzer();
            new FsaCalculationOptions { EscapeAndAnnihilation = false }.ApplyTo(escapes);
            Same("вылеты выкл: EscapeAndAnnihilation", false, escapes.EscapeAndAnnihilation);
            Same("вылеты выкл: EscapeGate по-прежнему true", true, escapes.EscapeGate);
            Console.WriteLine();
        }

        /// <summary>
        /// (`A145`, критерий 11) Пять новых флажков конфигурации переживают
        /// копирующий конструктор, XML и `AdoptFrom`; умолчания у всех —
        /// включено; выключенное положение НЕ теряется ни на одном шаге.
        /// </summary>
        static void ConfigSection()
        {
            Console.WriteLine("=== флажки конфигурации: клон, XML, AdoptFrom (A145, критерий 11) ===");
            var fresh = new FWHMPeakDetectionMethodConfig();
            Same("умолчание AtomicXrayForFsa", true, fresh.AtomicXrayForFsa);
            Same("умолчание CascadeSummingForFsa", true, fresh.CascadeSummingForFsa);
            Same("умолчание BackscatterForFsa", true, fresh.BackscatterForFsa);
            Same("умолчание EscapeAndAnnihilationForFsa", true, fresh.EscapeAndAnnihilationForFsa);
            Same("умолчание PileUpForFsa", true, fresh.PileUpForFsa);
            Same("умолчание — отпечаток настроек", "peaks|eq|xray|sum|bs|esc|pu",
                 FsaCalculationOptions.FromConfig(fresh).Stamp);

            var device = new FWHMPeakDetectionMethodConfig
            {
                AtomicXrayForFsa = false,
                CascadeSummingForFsa = false,
                BackscatterForFsa = false,
                EscapeAndAnnihilationForFsa = false,
                PileUpForFsa = false,
                DbLookupsForFsa = true,
                ChainEquilibrium = false
            };
            string expected = "db|free|-xray|-sum|-bs|-esc|-pu";
            Same("выключенные — отпечаток настроек", expected, FsaCalculationOptions.FromConfig(device).Stamp);

            var clone = (FWHMPeakDetectionMethodConfig)device.Clone();
            Same("клон несёт те же флажки", expected, FsaCalculationOptions.FromConfig(clone).Stamp);

            var serializer = new XmlSerializer(typeof(FWHMPeakDetectionMethodConfig));
            string xml;
            using (var writer = new StringWriter(CultureInfo.InvariantCulture))
            {
                serializer.Serialize(writer, device);
                xml = writer.ToString();
            }

            Same("XML содержит EscapeAndAnnihilationForFsa", true, xml.Contains("<EscapeAndAnnihilationForFsa>false</EscapeAndAnnihilationForFsa>"));
            FWHMPeakDetectionMethodConfig back;
            using (var reader = new StringReader(xml))
            {
                back = (FWHMPeakDetectionMethodConfig)serializer.Deserialize(reader);
            }

            Same("XML туда и обратно несёт те же флажки", expected, FsaCalculationOptions.FromConfig(back).Stamp);

            // Старый файл конфигурации — без новых элементов: значения умолчания.
            string old = xml;
            foreach (string name in new[] { "AtomicXrayForFsa", "CascadeSummingForFsa", "BackscatterForFsa",
                                            "EscapeAndAnnihilationForFsa", "PileUpForFsa" })
            {
                old = old.Replace("<" + name + ">false</" + name + ">", "");
            }

            FWHMPeakDetectionMethodConfig legacy;
            using (var reader = new StringReader(old))
            {
                legacy = (FWHMPeakDetectionMethodConfig)serializer.Deserialize(reader);
            }

            Same("старый XML без элементов — все пять включены", "db|free|xray|sum|bs|esc|pu",
                 FsaCalculationOptions.FromConfig(legacy).Stamp);

            // AdoptFrom: флажки идут от ПРИБОРА, у спектра остаются только его
            // калибровка ПШПВ и Enabled — как у прочих настроек поиска.
            var spectrum = new FWHMPeakDetectionMethodConfig { Enabled = false };
            FWHMPeakDetectionMethodConfig adopted = FWHMPeakDetectionMethodConfig.AdoptFrom(device, spectrum);
            Same("AdoptFrom берёт флажки у прибора", expected, FsaCalculationOptions.FromConfig(adopted).Stamp);
            Same("AdoptFrom оставляет спектру Enabled", false, adopted.Enabled);
            Same("AdoptFrom не трогает прибор", expected, FsaCalculationOptions.FromConfig(device).Stamp);
            Console.WriteLine();
        }

        static CheckBox Field(DCPeakDetectionView panel, string name)
        {
            FieldInfo f = typeof(DCPeakDetectionView).GetField(
                name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (f == null)
            {
                Console.WriteLine("  ⛔ поля «{0}» на панели НЕТ — проба смотрит не туда", name);
                bad++;
                return new CheckBox();
            }

            return (CheckBox)f.GetValue(panel);
        }

        /// <summary>
        /// Текст подсказки СЕЙЧАС. ⚠ Читать поле надо каждый раз: подсказка
        /// заводится лениво, при первом же вызове правила, и снимок, взятый до
        /// него, остался бы null навсегда — на этом проба уже оступилась.
        /// </summary>
        static string HintOf(DCPeakDetectionView panel, Control control)
        {
            FieldInfo f = typeof(DCPeakDetectionView).GetField(
                "fsaToolTip", BindingFlags.Instance | BindingFlags.NonPublic);
            ToolTip tip = f == null ? null : (ToolTip)f.GetValue(panel);
            return tip == null ? "" : tip.GetToolTip(control);
        }

        static void Same(string what, object expected, object got)
        {
            bool ok = Equals(expected, got);
            Console.WriteLine("  {0,-52} {1} {2}{3}", what, ok ? "=" : "!!", got,
                              ok ? "" : string.Format(" вместо {0}", expected));
            if (!ok)
            {
                bad++;
            }
        }
    }
}
