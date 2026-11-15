using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using BecquerelMonitor.Properties;
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

// (`AMBER274`, полоса `a274w` 14.11.2026) Раздел собственной активности кристалла в окне «Матрица отклика…»
// (решение Amber 14.11.2026 вопросником, дословно: «Да, отдельной полосой (Рекомендую)») и `Д2` критика `crit274f`.
//
//   CrystalSectionWindowProbeA274w --mode=window --device=<имя> --curve=<имя> [--ui=en|ru] [--snap=<.png>] [--threads=N]
//       окно над кривой портативной конфигурации (копия конфигурации — в каталоге пробы), счёт на ЦП малой сеткой:
//       кристалл с собственной активностью — раздел есть, годен прибору, подпись хода и строка подробностей на
//       языке окна; «Сохранить» → склад копии → общий читатель склада (`ResponseMatrixStore.Resolve`) →
//       `FsaMatrixBinding.BindDevice` раздел берёт; порча — отказ (чужой источник QF, чужая форма альф — `OtherRecipe`;
//       байт раздела — `Damaged`; матрица без раздела — `Missing`, окно говорит «пересчитайте»).
//       Кристалл без собственной активности — раздела нет, строки нет, подписи хода нет.
//       У кривой без точек (шаблон прибора) окно считать не даёт: проба кладёт ей три точки В ПАМЯТИ (файл не пишется).
//   --mode=d2 --device=<имя> --curve=<имя> --matdb=<копия matdb со строкой is_default = 1>
//       `Д2`: прежний вход (`AttachCrystalSection(g, m)`, `CrystalSectionFor(g, …)`) и новый с пустым источником QF
//       ведут себя одинаково И при строке «по веществу»; положительный контроль — прежнее поведение (рецепт
//       умолчания напрямую) при такой строке даёт ДРУГОЕ клеймо.
//   --mode=bind --rmx=<.rmx склада> --spectrum=<спектр .xml> --scene=<.in> --expect=<CrystalSectionRefusal>
//       `З2`: раздел файла склада глазами разбора корпуса — прибор из `<Efficiency>` спектра (`XmlSerializer(ResultDataFile)`,
//       как у `CorpusFsaProbe`), `FsaMatrixBinding.BindDevice`; код 0 — причина привязки равна ожидаемой.
//   --mode=mdb --matdb=<matdb до> --rmx=<matdb после> --spectrum=<копия с тронутой таблицей переноса>
//       задача 1: отпечаток склада `mdb=` до и после записи QF один; контроль — тронутая таблица переноса его меняет;
//       читатель видит новую строку QF.
//
// Окно НЕ показывается на экран: форма строится, кнопки зовутся отражением, `await` прокачивается
// `Application.DoEvents` (приём `ElectronSectionFormProbeA305`); `--snap` — только со скрытого стола (`HiddenRun.exe`).
// Склад — в каталоге пробы; настоящий `%AppData%\BecqMoni` проба трогать отказывается (код 2).
//
// Код возврата: 0 — всё сошлось; 1 — расхождения; 2 — ключи или среда.
static class CrystalSectionWindowProbeA274w
{
    const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;

    static int failures;

    static void Check(bool ok, string what)
    {
        if (!ok) failures++;
        Console.WriteLine((ok ? "   OK  " : "   ОТКАЗ ") + what);
    }

    static string Fmt(string format, params object[] args)
    {
        return string.Format(CultureInfo.InvariantCulture, format, args);
    }

    static object Field(ResponseMatrixForm form, string name)
    {
        FieldInfo f = typeof(ResponseMatrixForm).GetField(name, F);
        if (f == null) throw new MissingFieldException("ResponseMatrixForm", name);
        return f.GetValue(form);
    }

    static void Call(ResponseMatrixForm form, string method)
    {
        typeof(ResponseMatrixForm).GetMethod(method, F).Invoke(form, new object[] { null, EventArgs.Empty });
    }

    static string Text(string key)
    {
        return Resources.ResourceManager.GetString(key, Resources.Culture);
    }

    static EfficiencyConfigData Find(string device, string curve)
    {
        foreach (DeviceConfigInfo info in DeviceConfigManager.GetInstance().DeviceConfigList)
        {
            if (!string.Equals(info.Name, device, StringComparison.Ordinal)) continue;
            foreach (EfficiencyConfigData e in info.EfficiencyConfigs)
            {
                if (string.Equals(e.Name, curve, StringComparison.Ordinal)) return e;
            }
        }

        throw new InvalidOperationException("нет прибора/кривой: " + device + " / " + curve);
    }

    static void StubCurveIfEmpty(EfficiencyConfigData eff)
    {
        if (eff.HasCurve) return;
        eff.Curve = new List<ROIEfficiencyData>
        {
            new ROIEfficiencyData { Energy = 30, Efficiency = 0.5, ErrorPercent = 1 },
            new ROIEfficiencyData { Energy = 662, Efficiency = 0.1, ErrorPercent = 1 },
            new ROIEfficiencyData { Energy = 3000, Efficiency = 0.02, ErrorPercent = 1 },
        };
        Console.WriteLine("   у кривой нет точек (шаблон прибора) — три точки положены в ПАМЯТИ, файл конфигурации не пишется");
    }

    /// <summary>«Посчитать» и прокачка до конца. Возвращает подписи хода, какие были.</summary>
    static List<string> Compute(ResponseMatrixForm form, double limitSeconds, out double seconds)
    {
        if (!(SynchronizationContext.Current is WindowsFormsSynchronizationContext))
        {
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        }

        var labels = new List<string>();
        var clock = Stopwatch.StartNew();
        Call(form, "ComputeClick");
        var progress = (Label)Field(form, "progressLabel");
        while ((bool)Field(form, "busy"))
        {
            if (clock.Elapsed.TotalSeconds > limitSeconds)
            {
                Console.WriteLine(Fmt("   счёт не кончился за {0:F0} с — отмена", limitSeconds));
                Call(form, "CancelClick");
            }

            Application.DoEvents();
            if (labels.Count == 0 || labels[labels.Count - 1] != progress.Text) labels.Add(progress.Text);
            Thread.Sleep(5);
        }

        Application.DoEvents();
        if (labels.Count == 0 || labels[labels.Count - 1] != progress.Text) labels.Add(progress.Text);
        seconds = clock.Elapsed.TotalSeconds;
        return labels;
    }

    static void Quick(ResponseMatrixForm form, int threads)
    {
        ((NumericUpDown)Field(form, "minEnergyBox")).Value = 30;
        ((NumericUpDown)Field(form, "maxEnergyBox")).Value = 3000;
        ((NumericUpDown)Field(form, "nodesBox")).Value = 8;
        ((NumericUpDown)Field(form, "binBox")).Value = 2;
        var histories = (NumericUpDown)Field(form, "historiesBox");
        histories.Value = Math.Max(histories.Minimum, 2000);
        var beta = (NumericUpDown)Field(form, "betaHistoriesBox");
        beta.Value = Math.Max(beta.Minimum, 2000);
        if (threads > 0) ((NumericUpDown)Field(form, "threadsBox")).Value = threads;
    }

    static void Snap(ResponseMatrixForm form, string path)
    {
        if (path == null) return;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(0, 0);
        form.Show();
        Application.DoEvents();
        using (var bmp = new Bitmap(form.Width, form.Height))
        {
            form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }

        form.Hide();
        Console.WriteLine("   снимок: " + path);
    }

    static bool HasCyrillic(string s)
    {
        foreach (char c in s ?? "") if (c >= 'А' && c <= 'я') return true;
        return false;
    }

    static int LastIndexOf(byte[] data, byte[] mark)
    {
        for (int i = data.Length - mark.Length; i >= 0; i--)
        {
            int k = 0;
            while (k < mark.Length && data[i + k] == mark[k]) k++;
            if (k == mark.Length) return i;
        }

        return -1;
    }

    static FsaAnalyzer Bind(EfficiencyConfigData eff, ResponseMatrix m)
    {
        var analyzer = new FsaAnalyzer();
        FsaMatrixBinding.BindDevice(analyzer, eff, m);
        return analyzer;
    }

    static EfficiencyConfigData With(EfficiencyConfigData eff, string source, AlphaGroupShape shape)
    {
        var copy = new EfficiencyConfigData(eff.Name)
        {
            Guid = eff.Guid,
            Geometry = eff.Geometry,
            Curve = eff.Curve,
            AlphaQuenchingSource = source,
            AlphaGroupShape = shape,
        };
        return copy;
    }

    static void ModeWindow(string device, string curve, string ui, string snap, int threads)
    {
        EfficiencyConfigData eff = Find(device, curve);
        GeometryModel g = eff.Geometry;
        bool own = CrystalOwnActivityBuilder.HasOwnActivity(g);
        Console.WriteLine(Fmt("== окно: {0} / {1}; кристалл «{2}», собственная активность: {3}; язык {4}",
                              device, curve, g.Crystal != null ? g.Crystal.Name : "", own ? "есть" : "нет", ui));
        Console.WriteLine("   источник QF прибора: «" + (eff.AlphaQuenchingSource ?? "") + "»; форма альф: "
                          + (eff.AlphaGroupShape != null
                              ? Fmt("{0} / {1} / {2}", eff.AlphaGroupShape.ExtraFwhmShare, eff.AlphaGroupShape.ShelfShare, eff.AlphaGroupShape.ShelfLength)
                              : "нет"));
        StubCurveIfEmpty(eff);
        GlobalConfigManager.GetInstance().GlobalConfig.UseGpuForResponseMatrix = false;   // в памяти: GPU не трогаем

        string progressText = Text("ResponseMatrixCrystalSectionProgress");
        string rowsHead = Text("ResponseMatrixCrystalSectionRows");
        rowsHead = rowsHead.Substring(0, rowsHead.IndexOf("{0}", StringComparison.Ordinal));
        Check(!string.IsNullOrEmpty(progressText) && HasCyrillic(progressText + rowsHead) == (ui == "ru"),
              "строки раздела на языке окна: «" + progressText + "»");

        using (var form = new ResponseMatrixForm(eff))
        {
            bool useGpu = (bool)typeof(ResponseMatrixForm).GetProperty("UseGpu", F).GetValue(form, null);
            Check(!useGpu, "счёт на ЦП (галка GPU снята в памяти)");
            Quick(form, threads);
            double seconds;
            List<string> labels = Compute(form, 1800, out seconds);
            var m = (ResponseMatrix)Field(form, "computed");
            Check(m != null, Fmt("счёт дошёл до конца за {0:F1} с; «{1}»", seconds, labels[labels.Count - 1]));
            if (m == null) return;
            string details = (string)Field(form, "detailsText");
            Console.WriteLine("   подробности:");
            foreach (string line in details.Split(new[] { Environment.NewLine }, StringSplitOptions.None)) Console.WriteLine("     | " + line);
            bool sawLabel = labels.Contains(progressText);
            CrystalSectionRefusal why;
            string detail;
            if (!own)
            {
                Check(m.CrystalSection == null && m.CrystalSectionDamage == null, "раздела нет и отказа нет");
                Check(!sawLabel, "подписи хода раздела не было");
                Check(details.IndexOf(rowsHead.Trim(), StringComparison.Ordinal) < 0
                      && details.IndexOf(Text("ResponseMatrixCrystalSectionMissing"), StringComparison.Ordinal) < 0,
                      "строки о разделе в подробностях нет");
                Snap(form, snap);
                return;
            }

            ResponseCrystalSection section = m.CrystalSectionFor(g, eff, out why, out detail);
            Check(section != null, "раздел посчитан окном и годен прибору (" + why + ")");
            if (section == null) return;
            Check(sawLabel, "подпись хода раздела была: «" + progressText + "»");
            Check(details.IndexOf(rowsHead, StringComparison.Ordinal) >= 0, "строка раздела в подробностях");
            string ignored;
            CrystalOwnParts recipe = ResponseCrystalSection.RecipeFor(g, eff, out ignored);
            Console.WriteLine("   клеймо раздела: " + section.Stamp);
            if (!string.IsNullOrEmpty(eff.AlphaQuenchingSource))
            {
                ScintAlphaQuenching.Law law = ScintAlphaQuenching.Find(EfficiencySimulator.ScintillatorNameOf(g), eff.AlphaQuenchingSource);
                Check(law != null && recipe.HasAlpha && section.HasAlpha, "рецепт прибора с альфами, в разделе альфы есть");
                if (law != null)
                {
                    Console.WriteLine(Fmt("   закон matdb «{0}»: QF(6 МэВ) {1}, наклон {2} /МэВ → рецепт aq {3}, as {4}",
                                          law.Source, law.QfAtReference, law.SlopePerMev, recipe.AlphaQuenching, recipe.AlphaQuenchingSlopePerMev));
                    string rcp = ResponseCrystalSection.StampField(section.Stamp, "rcp") ?? "";
                    Check(rcp.Contains(":aq" + recipe.AlphaQuenching.ToString("R", CultureInfo.InvariantCulture) + ":")
                          && rcp.Contains(":as" + recipe.AlphaQuenchingSlopePerMev.ToString("R", CultureInfo.InvariantCulture) + ":"),
                          "клеймо раздела несёт QF и наклон строки matdb");
                    Check(details.IndexOf("«" + law.Source + "»", StringComparison.Ordinal) >= 0, "подробности называют источник QF");
                }
            }

            Snap(form, snap);

            // Сохранить → склад копии → общий читатель склада, как у приложения (`FsaAnalysisSession`)
            Call(form, "SaveClick");
            ResponseMatrixStore.CommitPending(new[] { eff.Guid });
            MatrixRefusal refusal;
            int format;
            ResponseMatrixSource source;
            string spectrumRefusal;
            ResponseMatrix back = ResponseMatrixStore.Resolve(eff, out refusal, out format, out source, out spectrumRefusal);
            Check(back != null && back.IsValidFor(eff.Geometry), Fmt("на складе копии матрица годна сцене (формат {0}, {1})", format, source));
            if (back == null) return;
            FsaAnalyzer a = Bind(eff, back);
            Check(a.CrystalOwnSection != null && a.CrystalOwnRefusal == CrystalSectionRefusal.None,
                  "разбор (BindDevice) раздел склада берёт: " + a.CrystalOwnRefusal);

            // порча — отказ
            FsaAnalyzer b = Bind(With(eff, "нет_такого_источника", eff.AlphaGroupShape), back);
            Check(b.CrystalOwnSection == null && b.CrystalOwnRefusal == CrystalSectionRefusal.OtherRecipe,
                  "чужой источник QF у прибора — отказ " + b.CrystalOwnRefusal);
            if (eff.AlphaGroupShape != null)
            {
                var other = new AlphaGroupShape { ExtraFwhmShare = eff.AlphaGroupShape.ExtraFwhmShare + 0.01,
                                                  ShelfShare = eff.AlphaGroupShape.ShelfShare, ShelfLength = eff.AlphaGroupShape.ShelfLength };
                FsaAnalyzer c = Bind(With(eff, eff.AlphaQuenchingSource, other), back);
                Check(c.CrystalOwnSection == null && c.CrystalOwnRefusal == CrystalSectionRefusal.OtherRecipe,
                      "чужая форма альф у прибора — отказ " + c.CrystalOwnRefusal);
            }

            string path = ResponseMatrixStore.PathOf(eff.Guid, ResponseMatrixSource.Store);
            byte[] bytes = File.ReadAllBytes(path);
            int at = LastIndexOf(bytes, Encoding.ASCII.GetBytes(ResponseCrystalSection.EndMark));
            Check(at > 64, "метка конца раздела в файле склада найдена");
            if (at > 64)
            {
                string damagedPath = Path.Combine(Path.GetTempPath(), "a274w_damaged_" + Guid.NewGuid().ToString("N") + ".rmx");
                bytes[at - 200] ^= 0x5A;
                File.WriteAllBytes(damagedPath, bytes);
                ResponseMatrix damaged = ResponseMatrix.Load(damagedPath);
                File.Delete(damagedPath);
                FsaAnalyzer d = damaged != null ? Bind(eff, damaged) : null;
                Check(damaged != null && damaged.IsValidFor(g) && d.CrystalOwnSection == null && d.CrystalOwnRefusal == CrystalSectionRefusal.Damaged,
                      "испорченный байт раздела — матрица годна, раздел снят: " + (d != null ? d.CrystalOwnRefusal.ToString() : "не прочитан"));
            }

            // матрица без раздела (как до полосы): разбор — Missing, окно — «пересчитайте»
            back.CrystalSection = null;
            FsaAnalyzer e = Bind(eff, back);
            Check(e.CrystalOwnSection == null && e.CrystalOwnRefusal == CrystalSectionRefusal.Missing, "матрица без раздела — отказ " + e.CrystalOwnRefusal);
            back.Save(path);
            // Решение Amber 14.11.2026: подсказка «пересчитайте» — «Только при включённом слое (Рекомендую)».
            FieldInfo layer = typeof(ResponseMatrixForm).GetField("crystalLayerForProbe", BindingFlags.Static | BindingFlags.NonPublic);
            // (`a274v` 15.11.2026) слой ВКЛ умолчанием — окно над матрицей без раздела говорит «пересчитайте»; контроль
            // подменой пробы: слой ВЫКЛ — подсказки нет (решение Amber 14.11.2026 «Только при включённом слое»)
            Check(layer != null && new FsaAnalyzer().CrystalOwnLayer, "слой в разборе ВКЛ умолчанием; подмена для пробы есть");
            string missing = Text("ResponseMatrixCrystalSectionMissing"), stale = Text("ResponseMatrixCrystalSectionStale");
            using (var reopened = new ResponseMatrixForm(eff))
            {
                string text = (string)Field(reopened, "detailsText");
                Check(text.IndexOf(missing, StringComparison.Ordinal) >= 0,
                      "слой ВКЛ (умолчание): окно над матрицей без раздела говорит «пересчитайте»");
            }

            if (layer != null)
            {
                layer.SetValue(null, false);
                try
                {
                    using (var reopened = new ResponseMatrixForm(eff))
                    {
                        string text = (string)Field(reopened, "detailsText");
                        Check(text.IndexOf(missing, StringComparison.Ordinal) < 0 && text.IndexOf(stale, StringComparison.Ordinal) < 0
                              && text.IndexOf(rowsHead, StringComparison.Ordinal) < 0,
                              "контроль: слой ВЫКЛ (подмена пробы) — окно над матрицей без раздела подсказки «пересчитайте» не показывает");
                    }
                }
                finally
                {
                    layer.SetValue(null, null);
                }
            }
        }
    }

    static bool SameSection(ResponseCrystalSection a, ResponseCrystalSection b)
    {
        if (a == null || b == null || a.Stamp != b.Stamp || a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            CrystalOwnEntry x = a.Entries[i], y = b.Entries[i];
            if (x.Nuclide != y.Nuclide || x.Layer != y.Layer || x.Free != y.Free || x.ActivityBq != y.ActivityBq
                || x.PerDecay.Length != y.PerDecay.Length) return false;
            for (int k = 0; k < x.PerDecay.Length; k++) if (x.PerDecay[k] != y.PerDecay[k]) return false;
        }

        return true;
    }

    static void ModeD2(string device, string curve, string matdb, int threads)
    {
        EfficiencyConfigData eff = Find(device, curve);
        GeometryModel g = eff.Geometry;
        Console.WriteLine("== Д2: прежний вход и новый с пустым источником QF — " + device + " / " + curve);
        var options = new ResponseMatrixOptions { MinEnergyKev = 30, MaxEnergyKev = 3000, NodeCount = 4, Histories = 1000, ContinuumErrorTarget = 0, JointNodes = 0 };
        if (threads > 0) options.Threads = threads;
        ResponseMatrix photon = ResponseMatrixBuilder.Build(g, options, null, CancellationToken.None);
        // прибор с пустым источником QF и без формы альф — так у всех прежних конфигураций (`Д2` — про них);
        // форма — поле прибора и действует при любых альфах, её сравнение — отдельной строкой ниже
        EfficiencyConfigData empty = With(eff, "", null);
        EfficiencyConfigData emptyShaped = With(eff, "", eff.AlphaGroupShape);
        string ignored;
        foreach (string arm in new[] { "без строки is_default=1 (база полосы)", "со строкой is_default=1 (" + matdb + ")" })
        {
            bool withDefault = arm.StartsWith("со", StringComparison.Ordinal);
            ScintAlphaQuenching.Reload(withDefault ? matdb : null);
            Console.WriteLine("-- " + arm + ": строк закона " + ScintAlphaQuenching.All().Count);
            ScintAlphaQuenching.Law byMaterial = ScintAlphaQuenching.Find(EfficiencySimulator.ScintillatorNameOf(g), null);
            Check(withDefault == (byMaterial != null), "строка «по веществу»: " + (byMaterial != null ? byMaterial.Source : "нет"));

            photon.CrystalSection = null;
            ResponseMatrixBuilder.AttachCrystalSection(g, photon);
            ResponseCrystalSection oldEntry = photon.CrystalSection;
            photon.CrystalSection = null;
            ResponseMatrixBuilder.AttachCrystalSection(g, photon, empty);
            ResponseCrystalSection newEntry = photon.CrystalSection;
            photon.CrystalSection = null;
            ResponseMatrixBuilder.AttachCrystalSection(g, photon, null);
            ResponseCrystalSection nullEntry = photon.CrystalSection;
            Console.WriteLine("   клеймо прежнего входа: " + (oldEntry != null ? ResponseCrystalSection.StampField(oldEntry.Stamp, "rcp") : "нет"));
            Console.WriteLine("   клеймо пустого источника: " + (newEntry != null ? ResponseCrystalSection.StampField(newEntry.Stamp, "rcp") : "нет"));
            Check(SameSection(oldEntry, newEntry) && SameSection(oldEntry, nullEntry), "прежний вход = пустой источник = прибор null: раздел побитно");
            photon.CrystalSection = null;
            ResponseMatrixBuilder.AttachCrystalSection(g, photon, emptyShaped);
            ResponseCrystalSection shaped = photon.CrystalSection;
            string shapedRcp = shaped != null ? ResponseCrystalSection.StampField(shaped.Stamp, "rcp") : "";
            string oldRcp = oldEntry != null ? ResponseCrystalSection.StampField(oldEntry.Stamp, "rcp") : "";
            Console.WriteLine("   клеймо пустого источника с формой прибора: " + shapedRcp);
            Check(withDefault ? shapedRcp.StartsWith(oldRcp + ":aw", StringComparison.Ordinal) : SameSection(oldEntry, shaped),
                  withDefault ? "форма прибора ложится на альфы строки «по веществу» (поле прибора, не Д2)"
                              : "без альф форма прибора не действует: раздел тот же");
            Check(withDefault == (oldEntry != null && oldEntry.HasAlpha), withDefault ? "при строке «по веществу» альфы есть" : "без строки альф нет");

            CrystalSectionRefusal why;
            string detail;
            photon.CrystalSection = newEntry;
            bool oldReader = photon.CrystalSectionFor(g, out why, out detail) != null;
            bool newReader = photon.CrystalSectionFor(g, empty, out why, out detail) != null;
            Check(oldReader && newReader, "раздел пустого источника берут оба читателя (прежний и с прибором)");

            // положительный контроль: прежнее поведение — рецепт умолчания НАПРЯМУЮ, мимо таблицы
            ResponseCrystalSection direct = CrystalOwnActivityBuilder.Build(g, photon.BinKev, 100000, 0x274, new CrystalOwnParts(), out ignored);
            bool differs = direct == null || newEntry == null || direct.Stamp != newEntry.Stamp;
            Check(withDefault == differs, withDefault
                ? "контроль: рецепт умолчания напрямую (как вёл прежний вход) даёт ДРУГОЕ клеймо — расхождение было бы"
                : "контроль: без строки рецепт умолчания напрямую = тот же раздел");
            photon.CrystalSection = direct;
            Check(withDefault == (photon.CrystalSectionFor(g, out why, out detail) == null),
                  withDefault ? "контроль: раздел без альф при строке «по веществу» читатель отвергает (" + why + ")" : "контроль: без строки раздел умолчания годен");
        }

        ScintAlphaQuenching.Reload(null);
    }

    /// <summary>
    /// (`З2`) Раздел файла склада <paramref name="rmx"/> глазами разбора корпуса: прибор — узел `<Efficiency>` спектра
    /// (тем же `XmlSerializer(ResultDataFile)`, что у `CorpusFsaProbe`), привязка — `FsaMatrixBinding.BindDevice`; сцена —
    /// файл `.in`, с которого склад считал (годность самой матрицы сверяется по нему). Ждётся причина <paramref name="expect"/>.
    /// </summary>
    static void ModeBind(string rmx, string spectrum, string scene, string expect)
    {
        GeometryModel g = GeometryModel.Load(scene);
        ResponseMatrix m = ResponseMatrix.Load(rmx);
        Check(m != null && m.IsValidFor(g), "матрица склада читается и годна сцене " + Path.GetFileName(scene));
        if (m == null) return;
        ResultDataFile file;
        using (FileStream stream = File.OpenRead(spectrum))
        {
            file = (ResultDataFile)new System.Xml.Serialization.XmlSerializer(typeof(ResultDataFile)).Deserialize(stream);
        }

        EfficiencyConfigData eff = file.ResultDataList[0].Efficiency;
        Console.WriteLine("   прибор спектра: «" + eff.Name + "», источник QF «" + (eff.AlphaQuenchingSource ?? "") + "»");
        // геометрия разбора — сцена склада (стенд), поля слоя — прибора спектра
        var device = With(eff, eff.AlphaQuenchingSource, eff.AlphaGroupShape);
        device.Geometry = g;
        FsaAnalyzer a = Bind(device, m);
        Console.WriteLine("   раздел в файле: " + (m.CrystalSection != null ? ResponseCrystalSection.StampField(m.CrystalSection.Stamp, "rcp") : "нет"));
        Console.WriteLine("   привязка: " + a.CrystalOwnRefusal + (a.CrystalOwnRefusalDetail != null ? " — " + a.CrystalOwnRefusalDetail : ""));
        Check(string.Equals(a.CrystalOwnRefusal.ToString(), expect, StringComparison.Ordinal)
              && (a.CrystalOwnSection != null) == (expect == "None"), "причина привязки = ожидаемой «" + expect + "»");
    }

    [STAThread]
    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string mode = null, device = null, curve = null, ui = "en", snap = null, matdb = null, rmx = null, spectrum = null, scene = null, expect = null;
        int threads = 0;
        foreach (string a in args)
        {
            if (a.StartsWith("--mode=", StringComparison.Ordinal)) mode = a.Substring(7);
            else if (a.StartsWith("--device=", StringComparison.Ordinal)) device = a.Substring(9);
            else if (a.StartsWith("--curve=", StringComparison.Ordinal)) curve = a.Substring(8);
            else if (a.StartsWith("--ui=", StringComparison.Ordinal)) ui = a.Substring(5);
            else if (a.StartsWith("--snap=", StringComparison.Ordinal)) snap = a.Substring(7);
            else if (a.StartsWith("--matdb=", StringComparison.Ordinal)) matdb = a.Substring(8);
            else if (a.StartsWith("--rmx=", StringComparison.Ordinal)) rmx = a.Substring(6);
            else if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrum = a.Substring(11);
            else if (a.StartsWith("--scene=", StringComparison.Ordinal)) scene = a.Substring(8);
            else if (a.StartsWith("--expect=", StringComparison.Ordinal)) expect = a.Substring(9);
            else if (a.StartsWith("--threads=", StringComparison.Ordinal)) threads = int.Parse(a.Substring(10), CultureInfo.InvariantCulture);
            else { Console.WriteLine("не знаю ключа: " + a); return 2; }
        }

        if (mode == "mdb")
        {
            // (`a274w`, задача 1) отпечаток склада `mdb=` до и после записи QF — обязан совпасть; положительный
            // контроль — копия с тронутой таблицей переноса (`--spoiled=`): отпечаток обязан смениться
            if (matdb == null || rmx == null || spectrum == null)
            {
                Console.WriteLine("нужны --matdb=<до> --rmx=<после> --spectrum=<копия с тронутой таблицей переноса>");
                return 2;
            }

            string before = MaterialDatabase.SimulatorDataFingerprint(matdb);
            string after = MaterialDatabase.SimulatorDataFingerprint(rmx);
            string spoiled = MaterialDatabase.SimulatorDataFingerprint(spectrum);
            Console.WriteLine("mdb до:    " + before + "  " + matdb);
            Console.WriteLine("mdb после: " + after + "  " + rmx);
            Console.WriteLine("mdb порча: " + spoiled + "  " + spectrum);
            Check(before == after && before != "none", "отпечаток склада до и после записи QF один");
            Check(spoiled != before, "контроль: тронутая таблица переноса меняет отпечаток");
            ScintAlphaQuenching.Reload(rmx);
            ScintAlphaQuenching.Law law = ScintAlphaQuenching.Find("LaBr3:Ce", "a274a_bg_vertices");
            Check(law != null && law.QfAtReference == 0.3787 && law.SlopePerMev == 0.0195 && law.ReferenceKev == 6000.0
                  && !law.IsDefault && ScintAlphaQuenching.Find("LaBr3:Ce", null) == null,
                  "читатель видит строку после записи: QF(6 МэВ) 0.3787, наклон 0.0195, is_default 0, строки «по веществу» нет");
            if (law != null)
            {
                foreach (double e in new[] { 4950.0, 5716.0, 6623.0, 6819.0, 7386.0 })
                {
                    Console.WriteLine(Fmt("   QF({0} кэВ) = {1:F4}{2}", e, law.At(e), law.Measured(e) ? "" : " (вне охвата замера)"));
                }
            }

            ScintAlphaQuenching.Reload(null);
            Console.WriteLine(failures == 0 ? "ВСЕ СОШЛИСЬ" : "⛔ РАСХОЖДЕНИЙ: " + failures.ToString(CultureInfo.InvariantCulture));
            return failures == 0 ? 0 : 1;
        }

        if (mode == "bind")
        {
            if (rmx == null || spectrum == null || scene == null || expect == null)
            {
                Console.WriteLine("нужны --rmx= --spectrum= --scene= --expect=<CrystalSectionRefusal>");
                return 2;
            }

            try { ModeBind(rmx, spectrum, scene, expect); }
            catch (Exception ex) { failures++; Console.WriteLine("⛔ проба оборвалась: " + ex); }
            Console.WriteLine(failures == 0 ? "ВСЕ СОШЛИСЬ" : "⛔ РАСХОЖДЕНИЙ: " + failures.ToString(CultureInfo.InvariantCulture));
            return failures == 0 ? 0 : 1;
        }

        if (mode == null || device == null || curve == null || (mode == "d2" && matdb == null))
        {
            Console.WriteLine("нужны --mode=window|d2|bind --device= --curve= (у d2 ещё --matdb=)");
            return 2;
        }

        var culture = new CultureInfo(ui == "ru" ? "ru-RU" : "en-US");
        Thread.CurrentThread.CurrentUICulture = culture;
        Resources.Culture = culture;
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);   // как у приложения (`Program.cs`): снимок рисует GDI, как экран
        GlobalConfigManager.GetInstance();
        string dir = ResponseMatrixStore.Directory;
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        Console.WriteLine("exe:   " + typeof(ResponseMatrixForm).Assembly.Location);
        Console.WriteLine("склад: " + dir);
        if (dir.StartsWith(appData, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("⛔ склад пробы — настоящий %AppData%: проба нужна в портативном каталоге");
            return 2;
        }

        Directory.CreateDirectory(dir);
        try
        {
            if (mode == "window") ModeWindow(device, curve, ui, snap, threads);
            else if (mode == "d2") ModeD2(device, curve, matdb, threads);
            else { Console.WriteLine("не знаю режима: " + mode); return 2; }
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine("⛔ проба оборвалась: " + ex);
        }

        Console.WriteLine(failures == 0 ? "ВСЕ СОШЛИСЬ" : "⛔ РАСХОЖДЕНИЙ: " + failures.ToString(CultureInfo.InvariantCulture));
        return failures == 0 ? 0 : 1;
    }
}
