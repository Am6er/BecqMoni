using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using BecquerelMonitor.EfficiencyMaker;

namespace BecquerelMonitor
{
    /// <summary>
    /// ⛔ (`AMBER218`, П244 07.10.2026; вынесено `AMBER319` 08.11.2026) МЕТКА «!» СТАРОЙ
    /// КРИВОЙ ИЛИ СТАРОЙ МАТРИЦЫ — ОДНО ПРАВИЛО И ОДИН РИСУНОК НА ВСЁ ПРИЛОЖЕНИЕ.
    ///
    /// Списки, которые им пользуются: вкладка «Эффективность» конфигурации прибора
    /// (<c>DeviceConfigForm.efficiencyCombo</c>) и список «Геометрия» панели
    /// «Управление измерением» (<c>DCControlPanel.efficiencyComboBox</c>). Постановка
    /// Amber 07.11.2026, дословно: «красные восклицательные знаки (картинка 2)
    /// отображать так же в списке картинки 1»; пункт «… — из файла спектра» — «Тем же
    /// правилом (Рекомендую)». До выноса правило жило в
    /// <c>DeviceConfigForm.Efficiency.cs</c>, и вторая копия в панели разошлась бы
    /// с ним при первой же правке — поэтому копий нет, а у формы остались
    /// переходники для прежних читателей (окно нуклидов, пробы П244 и П256).
    ///
    /// Решения Amber 07.10.2026 вопросником, дословно: кривая — **«Всё, о чём вкладка
    /// говорит «пересчитайте»»**, матрица — **«Всё, что окно матрицы зовёт
    /// «устарела»»**. Поэтому решение о КРИВОЙ берёт те же подписи, что вкладка
    /// пишет под списком (<c>DeviceConfigForm.GenerationNotes</c>,
    /// <c>DeviceConfigForm.CurveSceneNotes</c>), а не своё правило.
    ///
    /// ⚠ Решение о матрице читает её файл со склада целиком (<see cref="MatrixStale"/>).
    /// Списки считают метки при ЗАПОЛНЕНИИ, а не в <c>DrawItem</c>: иначе склад
    /// читался бы при каждом движении мыши по раскрытому списку.
    /// </summary>
    internal static class EfficiencyStaleMark
    {
        /// <summary>Текст метки старой кривой или матрицы в списке.</summary>
        internal const string MarkText = "!";

        /// <summary>Просвет между меткой и именем геометрии, точек.</summary>
        const int MarkGap = 4;

        /// <summary>
        /// ⛔ СТАРАЯ ЛИ КРИВАЯ — РОВНО ТО, О ЧЁМ ПОДПИСЬ ПОД СПИСКОМ ВКЛАДКИ ГОВОРИТ
        /// «ПЕРЕСЧИТАЙТЕ»:
        ///
        ///   * поколение переноса кривой ниже поколения сборки (`phys=`);
        ///   * прежнее определение пика — у геометрии с разрешением нет `peps=fwhm`;
        ///   * посчитана для другой геометрии — отпечаток при кривой не сходится с
        ///     геометрией рядом;
        ///   * прежняя сцена сосуда — нет `bmscene=1` у сосуда, чья сцена изменилась.
        ///
        /// Своего правила здесь НЕТ нарочно: метка в списке и подпись под ним обязаны
        /// говорить одно. Расхождение поколений кривой и матрицы между собой сюда не
        /// входит: с матрицей разбирается <see cref="MatrixStale"/>, и у пары «кривая 22,
        /// матрица 26» метку ставит кривая, у «кривая 26, матрица 22» — матрица.
        /// Кривая без клейма (ручная, по измерениям) — не старая: сказать о ней нечего,
        /// как и подписи.
        /// </summary>
        internal static bool CurveStale(EfficiencyConfigData config)
        {
            if (config == null)
            {
                return false;
            }

            return DeviceConfigForm.GenerationNotes(config.ComputeStamp, 0, ResponseMatrix.PhysicsVersion,
                                                    EfficiencyCalculation.PeakWindowExpected(config.Geometry)).Count > 0
                   || DeviceConfigForm.CurveSceneNotes(config).Count > 0;
        }

        /// <summary>
        /// ⛔ СТАРАЯ ЛИ МАТРИЦА — РОВНО ТО, ЧТО ОКНО «Response matrix» ЗОВЁТ «УСТАРЕЛА»
        /// (<c>ResponseMatrixForm.LoadExisting</c> / <c>SayRefusal</c>):
        ///
        ///   * файл прежнего формата (<see cref="MatrixRefusal.OldFormat"/>);
        ///   * поколение переноса в клейме не равно поколению сборки
        ///     (<see cref="ResponseMatrix.PhysicsVersion"/>);
        ///   * клеймо не сходится с нынешней геометрией и своими же параметрами
        ///     (<see cref="ResponseMatrix.IsValidFor(GeometryModel)"/>) — геометрию
        ///     правили после счёта;
        ///   * нет годного раздела электронов нынешнего рецепта
        ///     (<see cref="ResponseMatrix.NeedsElectronSection"/>, `AMBER305`).
        ///
        /// НЕ старая: матрицы нет вовсе (её не считали), файл обрублен, файл не наш —
        /// окно говорит об этом другими словами. Кривая без геометрии матрицы иметь не
        /// может.
        ///
        /// Матрица ищется по Guid КРИВОЙ, а судится по геометрии ЭТОЙ ЖЕ копии кривой.
        /// Для пункта «… — из файла спектра» панели (`AMBER319`) это и есть «тем же
        /// правилом»: файл чужого человека приносит кривую, а матрицы с её Guid на
        /// нашем складе нет — тогда судится только кривая; есть — судится и она, по
        /// геометрии копии из файла.
        ///
        /// Читается ТО ЖЕ место, что показывает окно матрицы, — ждущая сохранения
        /// матрица новее склада (<see cref="ResponseMatrixStore.EditingSource"/>,
        /// `AMBER186`). Файл читается целиком: годность по геометрии требует
        /// параметров матрицы, а их хвосты лежат ЗА телом файла.
        /// </summary>
        internal static bool MatrixStale(EfficiencyConfigData config)
        {
            if (config == null || !config.HasGeometry || string.IsNullOrEmpty(config.Guid))
            {
                return false;
            }

            MatrixRefusal refusal;
            int fileFormat;
            ResponseMatrix matrix = ResponseMatrixStore.Load(config.Guid,
                                                             ResponseMatrixStore.EditingSource(config.Guid),
                                                             out refusal, out fileFormat);
            return MatrixVerdict(matrix, refusal, config.Geometry);
        }

        /// <summary>
        /// ⛔ (`AMBER319`, решение Amber 08.11.2026 вопросником, дословно: «По тому, что возьмёт
        /// разбор (Рекомендую)») МАТРИЦА ПУНКТА «… — из файла спектра» судится та, которую
        /// реально возьмёт разбор: <see cref="ResponseMatrixStore.Resolve"/> — годная по клейму
        /// матрица склада, а нет её — встроенная в файл спектра (`AMBER202`,
        /// <see cref="ResponseMatrixStore.EmbedFor"/>), если та годна; иначе — склад. Приговор —
        /// тот же <see cref="MatrixVerdict"/>, что у вкладки: второй копии правила нет, разное
        /// только то, ОТКУДА взята матрица. Правило вкладки прибора (<see cref="MatrixStale"/>,
        /// склад или ждущая сохранения) не меняется.
        ///
        /// Сценарий, ради которого это сделано (критик `crit319`): спектр сохранён со встроенной
        /// матрицей геометрии G; у прибора геометрию правят и матрицу пересчитывают — склад по
        /// тому же Guid годен для G', а не для G. Разбор берёт годную встроенную, и «!» у пункта
        /// из файла была бы ложной.
        /// </summary>
        internal static bool MatrixStaleAsAnalysed(EfficiencyConfigData config)
        {
            if (config == null || !config.HasGeometry || string.IsNullOrEmpty(config.Guid))
            {
                return false;
            }

            MatrixRefusal refusal;
            int fileFormat;
            ResponseMatrixSource source;
            string spectrumRefusal;
            ResponseMatrix matrix = ResponseMatrixStore.Resolve(config, out refusal, out fileFormat,
                                                                out source, out spectrumRefusal);
            return MatrixVerdict(matrix, refusal, config.Geometry);
        }

        /// <summary>
        /// Приговор матрице, уже взятой из своего места: «устарела» — ровно три состояния окна
        /// матрицы и отсутствие раздела электронов (см. <see cref="MatrixStale"/>). Матрицы нет —
        /// старая, только если файл прежнего формата.
        /// </summary>
        static bool MatrixVerdict(ResponseMatrix matrix, MatrixRefusal refusal, GeometryModel geometry)
        {
            if (matrix == null)
            {
                return refusal == MatrixRefusal.OldFormat;
            }

            return ResponseMatrix.PhysicsFromStamp(matrix.Stamp) != ResponseMatrix.PhysicsVersion
                   || !matrix.IsValidFor(geometry)
                   || matrix.NeedsElectronSection(geometry);
        }

        /// <summary>
        /// Метка пункта «… — из файла спектра» (`AMBER319`): старая кривая копии ИЛИ старая
        /// матрица, которую возьмёт разбор (<see cref="MatrixStaleAsAnalysed"/>).
        /// </summary>
        internal static bool IsStaleAsAnalysed(EfficiencyConfigData config)
        {
            return CurveStale(config) || MatrixStaleAsAnalysed(config);
        }

        /// <summary>
        /// Ставить ли метку «!»: старая кривая ИЛИ старая матрица — «не имеет
        /// значения, что именно» (постановка Amber 07.10.2026).
        /// </summary>
        internal static bool IsStale(EfficiencyConfigData config)
        {
            return CurveStale(config) || MatrixStale(config);
        }

        /// <summary>
        /// Guid кривых прибора, которым положена метка (вкладка «Эффективность»:
        /// там в списке только кривые прибора, и Guid строки различает). Пустая
        /// конфигурация — пустой набор.
        /// </summary>
        internal static HashSet<string> StaleGuids(DeviceConfigInfo config)
        {
            HashSet<string> stale = new HashSet<string>(StringComparer.Ordinal);
            if (config == null || config.EfficiencyConfigs == null)
            {
                return stale;
            }

            foreach (EfficiencyConfigData item in config.EfficiencyConfigs)
            {
                if (item != null && !string.IsNullOrEmpty(item.Guid) && IsStale(item))
                {
                    stale.Add(item.Guid);
                }
            }

            return stale;
        }

        /// <summary>
        /// Нарисовать метку — белый «!» на красном квадрате — у левого края
        /// <paramref name="bounds"/>, высотой в строку. Возвращает ширину, занятую
        /// меткой вместе с просветом: с неё начинается имя геометрии. Так постановка
        /// Amber 07.10.2026, дословно: «белым восклицательным знаком в красной
        /// квадратной подложке (красный квадрат, а в нём белый воскл знак) перед
        /// текстом имя геометрии. Формат, например "! Цилиндр"».
        ///
        /// Статический и без формы нарочно: проба рисует его в картинку и считает
        /// красные и белые точки, а не снимает список с экрана.
        /// </summary>
        internal static int DrawMark(Graphics graphics, Rectangle bounds, Font font)
        {
            int side = Math.Max(4, bounds.Height - 2);
            Rectangle square = new Rectangle(bounds.X + 1, bounds.Y + (bounds.Height - side) / 2, side, side);
            using (SolidBrush red = new SolidBrush(Color.Red))
            {
                graphics.FillRectangle(red, square);
            }

            using (Font bold = new Font(font, FontStyle.Bold))
            {
                TextRenderer.DrawText(graphics, MarkText, bold, square, Color.White,
                                      TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                                      | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            }

            return 1 + side + MarkGap;
        }

        /// <summary>
        /// Строка списка геометрий: метка (если <paramref name="stale"/>) и подпись
        /// пункта. Один рисунок на оба списка — и в раскрытом списке, и в закрытом
        /// поле (<see cref="DrawItemState.ComboBoxEdit"/>): выбранная старая
        /// геометрия помечена и со свёрнутым списком.
        /// </summary>
        internal static void DrawComboItem(ComboBox combo, DrawItemEventArgs e, bool stale)
        {
            e.DrawBackground();
            if (e.Index >= 0 && e.Index < combo.Items.Count)
            {
                object item = combo.Items[e.Index];
                int left = e.Bounds.X;
                if (stale)
                {
                    left += DrawMark(e.Graphics, e.Bounds, e.Font);
                }
                else
                {
                    left += 1;
                }

                Rectangle textBounds = new Rectangle(left, e.Bounds.Y, Math.Max(0, e.Bounds.Right - left), e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, item == null ? "" : item.ToString(), e.Font, textBounds,
                                      e.ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                                                   | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }

            e.DrawFocusRectangle();
        }
    }
}
