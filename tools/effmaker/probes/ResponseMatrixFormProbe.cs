using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace ResponseMatrixFormProbe
{
    /// <summary>
    /// Форма матрицы отклика и кнопка на вкладке «Эффективность».
    ///
    /// Проверяется то, что молчит у компилятора и видно только человеку,
    /// открывшему форму:
    ///
    /// 1. **Три состояния при открытии** — нет матрицы, устарела, годна. Именно
    ///    ради этого форма и заводилась: посчитать спектр по матрице чужой
    ///    геометрии хуже, чем не посчитать вовсе, поэтому годность проверяется
    ///    по отпечатку, а не по наличию файла.
    /// 2. **Подробности у годной** — узлы, диапазон, бин, истории, размер, дата.
    /// 3. **Кнопка «Матрица отклика…» недоступна без геометрии** и на вкладке, и
    ///    в самой форме: у кривой, восстановленной по измерениям, геометрии нет
    ///    и считать не из чего.
    /// 4. **Кнопка есть на вкладке** и подписана из ресурсов.
    /// 5. **Сохранение кладёт файл туда, где его ищут,** и не трогает
    ///    конфигурацию — иначе матрица уехала бы внутрь файлов спектров.
    /// 6. **Матрица ПРЕЖНЕГО ФОРМАТА не отбрасывается молча** (`A50`): отказ
    ///    чтения называет себя, форма говорит «устарела: другое поколение», а
    ///    разбор заготавливает человеку сообщение с обоими номерами формата.
    ///    Положительный контроль — у годного файла разбор молчит.
    ///
    ///     responsematrixformprobe --geometry=X.in [--png=X.png]
    ///         [--nodes=N] [--histories=N] [--threads=N] [--target=%]
    ///         [--bin=кэВ] [--emin=кэВ] [--emax=кэВ]
    ///
    /// Ожидание: «ВСЕ СОШЛИСЬ».
    /// </summary>
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            string geometryPath = null, pngPath = null;
            // Сцена проверки хода счёта (`W27`) — задаваемая: числа с экрана
            // Amber (100 узлов, 300 000 историй, 15 потоков) воспроизводятся
            // ключами, а умолчание оставлено дешёвым, чтобы проба оставалась
            // рядовой.
            int wNodes = 12, wHistories = 20000, wThreads = 0;
            double wTarget = 1.0, wBin = 8.0, wMin = 0.0, wMax = 0.0;
            foreach (string a in args)
            {
                if (a.StartsWith("--geometry=", StringComparison.Ordinal)) geometryPath = a.Substring(11);
                else if (a.StartsWith("--png=", StringComparison.Ordinal)) pngPath = a.Substring(6);
                else if (a.StartsWith("--nodes=", StringComparison.Ordinal)) wNodes = int.Parse(a.Substring(8), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--histories=", StringComparison.Ordinal)) wHistories = int.Parse(a.Substring(12), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--threads=", StringComparison.Ordinal)) wThreads = int.Parse(a.Substring(10), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--target=", StringComparison.Ordinal)) wTarget = double.Parse(a.Substring(9), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--bin=", StringComparison.Ordinal)) wBin = double.Parse(a.Substring(6), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--emin=", StringComparison.Ordinal)) wMin = double.Parse(a.Substring(7), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--emax=", StringComparison.Ordinal)) wMax = double.Parse(a.Substring(7), CultureInfo.InvariantCulture);
            }

            if (geometryPath == null || !File.Exists(geometryPath))
            {
                Console.Error.WriteLine("нужен --geometry=<файл .in>");
                return 2;
            }

            Application.EnableVisualStyles();
            GlobalConfigManager.GetInstance();

            GeometryModel geometry = GeometryModel.Load(geometryPath);
            var config = new EfficiencyConfigData("проба")
            {
                Guid = "probe-" + Guid.NewGuid().ToString("N"),
                Geometry = geometry
            };

            int bad = 0;

            // Файла ещё нет — состояние «не посчитана».
            ResponseMatrixStore.Delete(config.Guid);
            using (var form = new ResponseMatrixForm(config))
            {
                string state = TextOf(form, "stateLabel");
                bool ok = state == BecquerelMonitor.Properties.Resources.ResponseMatrixStateMissing
                          && Enabled(form, "computeButton")
                          && !Enabled(form, "saveButton");
                Report(ok, "нет матрицы: «{0}», «Посчитать» доступна, «Сохранить» нет", Short(state));
                bad += ok ? 0 : 1;
            }

            // Кладём годную матрицу и открываем снова.
            var options = new ResponseMatrixOptions { NodeCount = 10, Histories = 4000, BinKev = 4.0 };
            ResponseMatrix matrix = ResponseMatrixBuilder.Build(geometry, options, null,
                                                               System.Threading.CancellationToken.None);
            ResponseMatrixStore.Save(config.Guid, matrix);

            using (var form = new ResponseMatrixForm(config))
            {
                string state = TextOf(form, "stateLabel");
                string details = StringField(form, "detailsText");

                // ⛔ (`A47`) СВЕРЯТЬСЯ НАДО С `matrix.NodeCount`, А НЕ С
                // `options.NodeCount`. Заказанное число узлов — ПРОСЬБА, а не
                // итог: `BuildGrid(geometry)` доводит сетку узлами вокруг
                // K-краёв веществ пробы (`T42`), и у `ASN16_lu_side.in`
                // заказанные 10 превращаются в 12. Форма печатает 12 — и
                // печатает ПРАВИЛЬНО, а проба искала «10» и роняла пункт с
                // 27.08.2026. Мерено: `BuildGrid(null)` = 10, `BuildGrid(g)` = 12.
                int nodesInMatrix = matrix.NodeCount;
                bool ok = state == BecquerelMonitor.Properties.Resources.ResponseMatrixStateValid
                          && details.Contains(nodesInMatrix.ToString(CultureInfo.CurrentCulture));
                Report(ok, "годная матрица: «{0}», в подробностях {1} узлов (заказано {2}); первая строка: «{3}»",
                       Short(state), nodesInMatrix, options.NodeCount, Short(FirstLine(details)));
                bad += ok ? 0 : 1;
            }

            // Снимок раскладки — чтобы форму можно было посмотреть, не запуская
            // приложение целиком.
            if (pngPath != null)
            {
                using (var form = new ResponseMatrixForm(config))
                {
                    form.Show();
                    Application.DoEvents();
                    using (var bitmap = new System.Drawing.Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
                        bitmap.Save(pngPath, System.Drawing.Imaging.ImageFormat.Png);
                    }

                    form.Hide();
                }

                Console.WriteLine("снимок формы: {0}", pngPath);
            }

            // Правим геометрию — матрица обязана стать устаревшей.
            //
            // ⛔ (`A47`) ДВИГАТЬ НАДО ТУ ДЛИНУ, КОТОРАЯ У ЭТОЙ ФОРМЫ КРИСТАЛЛА
            // РАБОЧАЯ. Прежде проба всегда прибавляла миллиметр к
            // `CrystalHeight` — полю ЦИЛИНДРА. У бруска (`Shape == Box`, а это
            // весь ASN16) оно не участвует ни в чём: `GeometryWriter.Render`
            // пишет в `DS_CrystalHeight` значение `CrystalBoxZ`, а сцену
            // `EfficiencySimulator` строит по `CrystalBoxInScene`. Измерено на
            // `ASN16_lu_side.in`: после `CrystalHeight += 1` текст геометрии
            // совпадает с исходным ПОБАЙТНО (8175 знаков оба), отпечаток тот же
            // (`phys=15;386e80d6…`), сцена та же (ax=9, ay=30, hc=15). То есть
            // «матрица осталась годной» было ВЕРНЫМ ответом на пустой сдвиг, а
            // не дырой в отпечатке: при `CrystalBoxZ += 1` отпечаток меняется
            // (`5ec58627…`), и форма честно говорит «устарела».
            GeometryModel moved = geometry.Clone();
            string movedField;
            if (moved.Shape == CrystalShape.Box)
            {
                moved.CrystalBoxZ += 1.0;
                movedField = "CrystalBoxZ";
            }
            else
            {
                moved.CrystalHeight += 1.0;
                movedField = "CrystalHeight";
            }

            // ⚠ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ НА САМ СДВИГ. Отпечаток считается от
            // ТЕКСТА геометрии; сдвиг, не дошедший до текста, не дойдёт и до
            // отпечатка, и проверка ниже мерила бы пустоту — ровно это и
            // случилось. Здесь она отказывает ЗАМЕТНО, а не молча проходит.
            bool textMoved = GeometryWriter.Render(moved) != GeometryWriter.Render(geometry);
            Report(textMoved, "сдвиг виден в тексте геометрии ({0} +1 мм, форма {1})",
                   movedField, moved.Shape);
            bad += textMoved ? 0 : 1;

            var movedConfig = new EfficiencyConfigData("проба-2")
            {
                Guid = config.Guid,
                Geometry = moved
            };

            using (var form = new ResponseMatrixForm(movedConfig))
            {
                string state = TextOf(form, "stateLabel");
                bool ok = state == BecquerelMonitor.Properties.Resources.ResponseMatrixStateStale;
                Report(ok, "геометрию сдвинули на 1 мм ({0}): «{1}»", movedField, Short(state));
                bad += ok ? 0 : 1;
            }

            // Кривая без геометрии.
            var noGeometry = new EfficiencyConfigData("без геометрии") { Guid = "probe-nogeom" };
            using (var form = new ResponseMatrixForm(noGeometry))
            {
                string state = TextOf(form, "stateLabel");
                bool ok = state == BecquerelMonitor.Properties.Resources.ResponseMatrixNoGeometry
                          && !Enabled(form, "computeButton");
                Report(ok, "без геометрии: «{0}», «Посчитать» недоступна", Short(state));
                bad += ok ? 0 : 1;
            }

            // Файл лежит там, где его ищут, и конфигурация о нём не знает.
            string path = ResponseMatrixStore.PathOf(config.Guid);
            bool stored = File.Exists(path) && new FileInfo(path).Length > 0;
            bool configClean = !Serialized(config).Contains("Rows")
                               && !Serialized(config).Contains("rmx");
            Report(stored && configClean,
                   "файл на месте ({0:F1} КБ), в конфигурации кривой матрицы нет",
                   stored ? new FileInfo(path).Length / 1024.0 : 0.0);
            bad += (stored && configClean) ? 0 : 1;

            // Кнопка на вкладке.
            bool tabButton = HasEfficiencyTabButton();
            Report(tabButton, "на вкладке «Эффективность» есть кнопка «{0}»",
                   BecquerelMonitor.Properties.Resources.EfficiencyTabResponseMatrix);
            bad += tabButton ? 0 : 1;

            // ------------------------------------------------------------------
            // (`A50`) МАТРИЦА ПРЕЖНЕГО ФОРМАТА НЕ ОТБРАСЫВАЕТСЯ МОЛЧА
            // ------------------------------------------------------------------
            // Подъём `FormatVersion` делает нечитаемым весь прежний склад; у
            // Amber 02.09.2026 таких файлов лежало шесть из десяти. `Load`
            // возвращал `null` без единого слова, и в легенде разбора это было
            // видно только пометкой «· без матрицы» — той же самой, что у
            // человека, который матрицу вовсе не считал. Лечится это
            // по-разному (пересчитать против посчитать), поэтому отказ обязан
            // называть СЕБЯ, а разбор — говорить об этом человеку.
            //
            // Сцена делается из ГОДНОГО файла правкой четырёхбайтного поля
            // версии в шапке: так получается ровно то, что лежит у людей после
            // подъёма формата — наш файл, наша матрица, читать нельзя.
            string matrixPath = ResponseMatrixStore.PathOf(config.Guid);
            int oldFormat = ResponseMatrix.FormatVersion - 1;
            SetFileFormat(matrixPath, oldFormat);

            MatrixRefusal refusal;
            int fileFormat;
            ResponseMatrix refused = ResponseMatrix.Load(matrixPath, out refusal, out fileFormat);
            bool named = refused == null && refusal == MatrixRefusal.OldFormat
                         && fileFormat == oldFormat;
            Report(named, "старый формат назван: отказ «{0}», в файле формат {1}, читаем {2}",
                   refusal, fileFormat, ResponseMatrix.FormatVersion);
            bad += named ? 0 : 1;

            using (var form = new ResponseMatrixForm(config))
            {
                string state = TextOf(form, "stateLabel");
                bool ok = state == BecquerelMonitor.Properties.Resources.ResponseMatrixStateStaleVersions;
                Report(ok, "форма про старый формат: «{0}»", Short(state));
                bad += ok ? 0 : 1;
            }

            // Главное: РАЗБОР говорит об этом человеку. Сообщение заготавливает
            // `FsaOverlay` (окно показывает вид — открывать его там, где решение
            // принимается, нельзя: это середина отрисовки), и в нём обязаны
            // стоять ОБА номера формата, иначе оно ничему не учит.
            // ⚠ Свои жалобы разбор пишет в `Trace`, а `Launch` глушит любое
            // исключение и оставляет от него только состояние: без слушателя
            // отказ этой проверки выглядел бы как «не сказал», хотя причина
            // была бы совсем другой.
            System.Diagnostics.Trace.Listeners.Add(
                new System.Diagnostics.TextWriterTraceListener(Console.Error));

            var overlay = new FsaOverlay();
            overlay.EnsureUpToDate(SceneOf(config), false);
            string notice = overlay.TakeResponseMatrixNotice() ?? "";
            bool told = overlay.ResponseMatrixOldFormat
                        && notice.Contains(oldFormat.ToString(CultureInfo.CurrentCulture))
                        && notice.Contains(ResponseMatrix.FormatVersion.ToString(CultureInfo.CurrentCulture));
            Report(told, "разбор назвал причину: пометка «старая матрица» {0}, состояние «{1}», сообщение «{2}»",
                   overlay.ResponseMatrixOldFormat ? "есть" : "НЕТ", Short(overlay.Status), Short(notice));
            bad += told ? 0 : 1;

            // ⚠ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: у ГОДНОГО файла ни пометки, ни
            // сообщения. Без него проверка выше прошла бы и на «говорит всегда».
            SetFileFormat(matrixPath, ResponseMatrix.FormatVersion);
            var quiet = new FsaOverlay();
            quiet.EnsureUpToDate(SceneOf(config), false);
            bool silent = !quiet.ResponseMatrixOldFormat
                          && string.IsNullOrEmpty(quiet.TakeResponseMatrixNotice());
            Report(silent, "у годного файла разбор молчит: пометки {0}",
                   quiet.ResponseMatrixOldFormat ? "ЕСТЬ" : "нет");
            bad += silent ? 0 : 1;

            // 6. ХОД СЧЁТА НЕ ВРЁТ (`W27`). При останове по шуму — а это
            //    умолчание — сетка проходится до трёх раз, и прежде счётчик
            //    считал ПРОГОНЫ против числа УЗЛОВ: на экране появлялось
            //    «Node 121 of 100», полоса замирала полной, а остаток уходил в
            //    минус и рисовался знаком вопроса. Здесь проверяются ровно те
            //    три свойства, которых тогда не было.
            var seen = new System.Collections.Generic.List<ResponseMatrixProgress>();
            var noisy = new ResponseMatrixOptions
            {
                NodeCount = wNodes,
                Histories = wHistories,
                BinKev = wBin,
                Threads = wThreads,
                ContinuumErrorTarget = wTarget
            };
            if (wMax > wMin && wMin > 0.0)
            {
                noisy.MinEnergyKev = wMin;
                noisy.MaxEnergyKev = wMax;
            }
            var sink = new Progress<ResponseMatrixProgress>(p => { lock (seen) { seen.Add(p); } });
            ResponseMatrix noisyMatrix = ResponseMatrixBuilder.Build(geometry, noisy, sink,
                                                                    System.Threading.CancellationToken.None);
            System.Threading.Thread.Sleep(200);
            Application.DoEvents();

            // ⚠ Проверяется КАЖДЫЙ снимок сам по себе, а не их порядок.
            //    Порядок доставки `Progress<T>` не гарантирован ничем: снимок
            //    берётся атомарно в рабочем потоке, а `Post` соседнего потока
            //    может опередить его, — и «план не убывает» ловило бы не
            //    счётчик, а очередь. Сам план по построению только растёт
            //    (`Interlocked.Add` с положительным), а сделанное никогда не
            //    больше плана: истории каждого прогона попадают в план ДО того,
            //    как прогон начнётся.
            int nodes = noisyMatrix.Energies.Length;
            int runs = seen.Count;
            bool overflow = false, beyondPlan = false, nodesBad = false;
            foreach (ResponseMatrixProgress p in seen)
            {
                if (p.Done > p.Total) overflow = true;
                if (p.DoneHistories > p.TotalHistories) beyondPlan = true;

                // ⛔ (`A46`) СЧЁТ УЗЛОВ. Досчитанных не бывает больше взятых в
                // работу, взятых — больше, чем узлов в сетке, и знаменатель
                // ПОСТОЯНЕН: именно его рост (140 → 155 → 156 → 157 на снимках
                // Amber) и заставлял полосу пятиться назад.
                if (p.SettledNodes > p.StartedNodes || p.StartedNodes > p.TotalNodes
                    || p.TotalNodes != nodes)
                {
                    nodesBad = true;
                }
            }

            bool progressOk = runs > 0 && !overflow && !beyondPlan && !nodesBad;
            Report(progressOk, "ход счёта: {0} прогонов на {1} узлов, Done ≤ Total {2}, "
                               + "историй не больше плана {3}, узлы досчитанные ≤ взятых ≤ сетки {4}",
                   runs, nodes, overflow ? "НЕТ" : "да",
                   beyondPlan ? "НЕТ" : "да", nodesBad ? "НЕТ" : "да");
            bad += progressOk ? 0 : 1;

            ResponseMatrixProgress last = seen.Count > 0 ? seen[seen.Count - 1] : null;
            bool finishedOk = last != null && last.Done == last.Total
                              && last.DoneHistories == last.TotalHistories
                              && last.SettledNodes == last.TotalNodes
                              && Math.Abs(last.Percent - 100.0) < 1.0E-9;
            Report(finishedOk, "план закрыт: {0}/{1} прогонов, {2}/{3} историй, узлов {4}/{5}, {6:F1} %",
                   last != null ? last.Done : 0, last != null ? last.Total : 0,
                   last != null ? last.DoneHistories : 0L, last != null ? last.TotalHistories : 0L,
                   last != null ? last.SettledNodes : 0, last != null ? last.TotalNodes : 0,
                   last != null ? last.Percent : 0.0);
            bad += finishedOk ? 0 : 1;

            // ⚠ Проверка обязана попасть в МНОГОПРОХОДНЫЙ режим, иначе она
            // ничего не проверяет: при одном проходе Done и Total сходились и
            // до правки. Цель по шуму здесь нарочно жёсткая (1 %), чтобы узлы
            // просили второй проход.
            // ⛔ (`A46`) ПРОВЕРКИ ПРЕДВАРИТЕЛЬНОЙ ОЦЕНКИ БОЛЬШЕ НЕТ: самой оценки
            // нет в приложении (решение Amber 02.09.2026 «убирай ETA, оно всегда
            // врёт»). Здесь стояла мерка «оценка в пределах ×5 от факта».
            bool multipass = runs > nodes;
            Report(multipass, "уточняющие проходы были: {0} прогонов против {1} узлов", runs, nodes);
            bad += multipass ? 0 : 1;

            ResponseMatrixStore.Delete(config.Guid);
            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "ПРОВАЛОВ: " + bad);
            return bad == 0 ? 0 : 1;
        }

        /// <summary>
        /// Кнопка ищется по обработчику, а не по надписи: надпись переводится, а
        /// поле — нет. Само наличие поля значит, что вкладка её создаёт.
        /// </summary>
        static bool HasEfficiencyTabButton()
        {
            FieldInfo field = typeof(DeviceConfigForm).GetField(
                "efficiencyMatrixButton", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo handler = typeof(DeviceConfigForm).GetMethod(
                "efficiencyMatrixButton_Click", BindingFlags.Instance | BindingFlags.NonPublic);
            return field != null && field.FieldType == typeof(Button) && handler != null;
        }

        /// <summary>
        /// Версия формата в шапке `.rmx`: «BQRM» и следом четыре байта. Правится
        /// на месте, чтобы получить файл ПРЕЖНЕГО поколения, не держа в дереве
        /// двоичный образец, который сам протухнет со следующим подъёмом.
        /// </summary>
        static void SetFileFormat(string path, int format)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite))
            {
                stream.Position = 4;
                stream.Write(BitConverter.GetBytes(format), 0, 4);
            }
        }

        /// <summary>
        /// Наименьший спектр, на котором `FsaOverlay` доходит до решения о
        /// матрице: решение принимается ДО фонового счёта, поэтому содержимое
        /// спектра здесь неважно, а важна кривая с геометрией и включённым
        /// выключателем матрицы.
        /// </summary>
        static ResultData SceneOf(EfficiencyConfigData config)
        {
            // ⚠ КАЛИБРОВКА ОБЯЗАТЕЛЬНА, и это не украшение: `Launch` начинается
            // со снимка спектра, а `EnergySpectrum.Clone` зовёт
            // `energyCalibration.Clone()` без проверки на null. Спектр без
            // калибровки роняет разбор ещё до решения о матрице, и проверка
            // ниже мерила бы пустоту — поймано слушателем `Trace`.
            var spectrum = new EnergySpectrum(1.0, 128);
            spectrum.EnergyCalibration = new PolynomialEnergyCalibration();
            spectrum.MeasurementTime = 100.0;
            return new ResultData
            {
                EnergySpectrum = spectrum,
                Efficiency = config.Copy()
            };
        }

        static string FirstLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            int end = text.IndexOfAny(new[] { '\r', '\n' });
            return end < 0 ? text : text.Substring(0, end);
        }

        static string Serialized(EfficiencyConfigData config)
        {
            var serializer = new System.Xml.Serialization.XmlSerializer(typeof(EfficiencyConfigData));
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, config);
                return writer.ToString();
            }
        }

        static string StringField(Form form, string fieldName)
        {
            return Field(form, fieldName) as string ?? "";
        }

        static string TextOf(Form form, string fieldName)
        {
            var label = Field(form, fieldName) as Label;
            return label != null ? label.Text : "";
        }

        static bool Enabled(Form form, string fieldName)
        {
            var control = Field(form, fieldName) as Control;
            return control != null && control.Enabled;
        }

        static object Field(Form form, string name)
        {
            FieldInfo field = form.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            return field != null ? field.GetValue(form) : null;
        }

        static string Short(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            text = text.Replace(Environment.NewLine, " ");
            return text.Length > 46 ? text.Substring(0, 46) + "…" : text;
        }

        static void Report(bool ok, string format, params object[] args)
        {
            Console.WriteLine("[{0}] {1}", ok ? "СОШЛОСЬ" : "ПРОВАЛ  ",
                              string.Format(CultureInfo.CurrentCulture, format, args));
        }
    }
}
