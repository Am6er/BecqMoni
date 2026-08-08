using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace BecquerelMonitor.EfficiencyMaker
{
    /// <summary>
    /// Отклик детектора для одной геометрии: как распределяется ПОГЛОЩЁННАЯ
    /// энергия, если в детектор летит квант заданной энергии.
    ///
    /// Зачем. Образ нуклида в полноспектральном разложении — это сегодня сумма
    /// пиковых профилей, и комптоновского континуума в нём нет вовсе: всё плато
    /// отдано свободной подложке. С матрицей образ становится суммой
    /// `выход линии × отклик(E_линии)`, то есть комптон каждого нуклида
    /// перестаёт быть догадкой и становится посчитанной величиной.
    ///
    /// Строка матрицы — один узел сетки входных энергий; в ней доли на бин
    /// ПОГЛОЩЁННОЙ энергии, нормированные на квант, испущенный источником в 4π
    /// (та же нормировка, что у <see cref="EfficiencySimulator.Efficiency"/>).
    /// Последний значащий бин строки — пик полного поглощения.
    ///
    /// ГДЕ ЛЕЖИТ. Отдельным файлом рядом с конфигурацией устройства, а не внутри
    /// неё. Причина жёсткая: `ResultData.DeviceConfig` сериализуется в файл
    /// спектра целиком, и матрица, положенная в конфигурацию, уезжала бы внутрь
    /// каждого сохранённого спектра — сотни килобайт данных, которые получателю
    /// бесполезны (пересчитать он их не сможет, а его собственная геометрия
    /// другая). В конфигурации остаётся только корешок
    /// (<see cref="ResponseMatrixInfo"/>) в несколько десятков байт.
    ///
    /// УШИРЕНИЯ ЗДЕСЬ НЕТ. Матрица живёт в энергии поглощения, без ПШПВ:
    /// разрешение принадлежит спектру, а не геометрии, и вшив его сюда, пришлось
    /// бы пересчитывать матрицу при каждой перекалибровке.
    /// </summary>
    public sealed class ResponseMatrix
    {
        /// <summary>Версия формата файла. Растёт при несовместимом изменении.</summary>
        // 3 — строки узлов разложены по каналам исхода. Версия 2 не читается
        // намеренно: в ней каналов нет, а достроить их из суммы нельзя, и
        // матрица, молча притворившаяся годной, рисовала бы весь непиковый
        // отклик одним слоем. Форма увидит «устарела» и пересчитает.
        // 4 — в блоке параметров появился ключ LightNonproportionality
        //     (07.08.2026): старый файл короче на байт, и читать его новым
        //     кодом значило бы съехать всем полем данных.
        // 5 — в блоке параметров появился ключ AnalogContinuum (08.08.2026,
        //     физика 6): та же арифметика — файл длиннее на байт, версия
        //     ОБЯЗАНА смениться, иначе формат-4 файл съедет полем данных.
        //     Найдено сверкой с параллельной сессией S11: ключ был добавлен
        //     в Options и отпечаток, но не в Write/ReadOptions.
        // 6 — в блоке параметров появились ключи BoundScattering и
        //     BremFromData (08.08.2026, физика 7 и 8): та же арифметика,
        //     файл длиннее ещё на два байта.
        public const int FormatVersion = 6;

        /// <summary>
        /// Версия ФИЗИКИ. Поднимать при любой правке переноса, меняющей числа:
        /// иначе в кэше молча останется матрица, посчитанная старой моделью, и
        /// это ровно тот сорт ошибки, который не проявляется, а тихо смещает
        /// результат.
        /// </summary>
        // 2 — доля K-оболочки по энергии (EPICS2017) и однократное рассеяние
        //     с лучей, прошедших мимо кристалла (06.08.2026). Первая правка
        //     чуть меняет пик выше K-края, вторая добавляет континуум.
        // 3 — занос электронов из окна/оправы в кристалл (07.08.2026): пока
        //     влияет только на полную эффективность (TotalEfficiency, F1) —
        //     матрицу отклика не меняет, но правило одно: правка переноса —
        //     новая версия.
        // 4 — отклик в шкале света (F11, 07.08.2026): электронные вклады
        //     взвешены кривой L(E) из nucdb, бины пересчитаны с якорем по
        //     пику. Меняет форму отклика CsI/NaI ниже ~200 кэВ.
        // 5 — вылет электронов из кристалла ВКЛЮЧЁН (F12, 07.08.2026):
        //     порогово-линейная эффективная глубина, калибровка по
        //     Geant4-развёртке. Снижает пик выше ~700 кэВ (на 2614 — на 13 %)
        //     и добавляет континуум.
        // 6 — континуум отклика аналоговой веткой (F14, 08.08.2026): бины ниже
        //     пика считаются переносом ε_полной (полная сфера, многократное
        //     рассеяние во всех областях, пролёт сквозь кристалл с возвратом,
        //     занос электронов), пик остаётся взвешенным. Плюс F13: брусовая
        //     кювета больше не затеняет пробу стенкой. Взвешенный континуум
        //     недобирал 0.57–0.92 от Geant4 (журнал tccfcalc2, §11).
        // 7 — рассеяние на СВЯЗАННОМ электроне (N11, 08.08.2026): угол
        //     комптона с множителем отбора S(x,Z), доплеровское размытие
        //     рассеянной энергии по профилям Комптона, когерентное — своим
        //     каналом с углом по F²(x,Z) вместо «проходит насквозь».
        //     Данные лежали в базе с 06.08.2026 без потребителя.
        // 8 — спектр тормозного из сечений Зельцера — Бергера вместо
        //     приближения Крамерса dN/dk = C/k (M3, 08.08.2026): сечения
        //     интегрируются по пути торможения, пробег — ESTAR. Меняет форму
        //     спектра вылетающих квантов, а значит и континуум наверху шкалы.
        public const int PhysicsVersion = 8;

        /// <summary>Узлы сетки входных энергий, кэВ, по возрастанию.</summary>
        public double[] Energies { get; set; }

        /// <summary>Шаг бина поглощённой энергии, кэВ.</summary>
        public double BinKev { get; set; }

        /// <summary>Строки: на узел — доли на бин. Длина строки своя у каждого узла.</summary>
        /// <summary>
        /// Отклик узлов, суммарный по каналам. В файле НЕ хранится — считается
        /// из каналов при загрузке (<see cref="RebuildTotals"/>): держать в
        /// файле и части, и их сумму значило бы однажды получить файл, где они
        /// друг другу не отвечают.
        /// </summary>
        public float[][] Rows { get; set; }

        /// <summary>
        /// Отклик, разложенный по каналам исхода: `[канал][узел][бин]`, канал —
        /// <see cref="EfficiencySimulator.ResponseChannel"/>. Пустой канал
        /// (скажем, вылет 511 у матрицы, не достающей до порога пар) хранится
        /// как строки нулевой длины и места не занимает.
        /// </summary>
        public float[][][] ChannelRows { get; set; }

        /// <summary>Историй на узел — по нему судят о статистике бина.</summary>
        public int Histories { get; set; }

        /// <summary>Отпечаток геометрии и физики, с которыми считалось.</summary>
        public string Stamp { get; set; }

        /// <summary>
        /// Параметры, с которыми матрица посчитана. Хранятся, а не
        /// восстанавливаются из краёв сетки: `exp(log(30))` даёт
        /// 30.000000000000004, отпечаток от такой строки другой, и годная
        /// матрица объявлялась бы устаревшей при каждом открытии формы.
        /// </summary>
        public ResponseMatrixOptions Options { get; set; }

        public DateTime CreatedUtc { get; set; }

        /// <summary>Сколько всего заняло построение — для показа в форме.</summary>
        public double BuildSeconds { get; set; }

        /// <summary>
        /// Худшая по узлам относительная ошибка КОНТИНУУМА строки, % (F23);
        /// 0 — не считалось. В ФАЙЛ НЕ ПИШЕТСЯ и у прочитанной матрицы равна
        /// нулю: это свойство прогона, а не матрицы, и ради него не стоит
        /// поднимать версию формата — форма показывает его сразу после счёта,
        /// то есть тогда, когда на него можно ответить числом историй.
        /// См. <see cref="EfficiencySimulator.LastContinuumRelativeError"/>.
        /// </summary>
        public double ContinuumRelativeError { get; set; }

        public int NodeCount
        {
            get { return this.Energies != null ? this.Energies.Length : 0; }
        }

        /// <summary>
        /// Размер в памяти, байт (только числа откликов). Считаются И каналы,
        /// И суммарные строки: в памяти живут оба набора (суммы
        /// восстанавливаются из каналов при загрузке), и счёт по одним лишь
        /// суммам занижал показанное «в памяти, КБ» примерно вдвое.
        /// </summary>
        public long DataBytes
        {
            get
            {
                long cells = 0;
                if (this.Rows != null)
                {
                    foreach (float[] row in this.Rows)
                    {
                        cells += row != null ? row.Length : 0;
                    }
                }

                if (this.ChannelRows != null)
                {
                    foreach (float[][] channel in this.ChannelRows)
                    {
                        if (channel == null)
                        {
                            continue;
                        }

                        foreach (float[] row in channel)
                        {
                            cells += row != null ? row.Length : 0;
                        }
                    }
                }

                return cells * sizeof(float);
            }
        }

        // ------------------------------------------------------------------
        // Отпечаток
        // ------------------------------------------------------------------

        /// <summary>
        /// Отпечаток геометрии и условий счёта. Считается от ТЕКСТА геометрии в
        /// том же формате, в каком она пишется на диск: так в него попадают все
        /// поля модели разом, и добавленное завтра поле не окажется молча вне
        /// проверки. Плюс ключи физики, сетка и версия переноса.
        ///
        /// Версия физики стоит ПЕРЕД хешем открытым текстом (`phys=N;хеш`):
        /// хеш необратим, и спрятанную в него версию <see cref="PhysicsFromStamp"/>
        /// уже никак не достанет — форма показывала «физика 0» про только что
        /// посчитанную матрицу. В сравнение отпечатков префикс входит наравне
        /// с хешем.
        /// </summary>
        public static string ComputeStamp(GeometryModel geometry, ResponseMatrixOptions options)
        {
            if (geometry == null)
            {
                return "";
            }

            var sb = new StringBuilder();
            sb.Append("phys=").Append(PhysicsVersion).Append(';');
            if (options != null)
            {
                sb.Append("emin=").Append(options.MinEnergyKev.ToString("R", CultureInfo.InvariantCulture)).Append(';');
                sb.Append("emax=").Append(options.MaxEnergyKev.ToString("R", CultureInfo.InvariantCulture)).Append(';');
                sb.Append("nodes=").Append(options.NodeCount).Append(';');
                sb.Append("bin=").Append(options.BinKev.ToString("R", CultureInfo.InvariantCulture)).Append(';');
                sb.Append("hist=").Append(options.Histories).Append(';');
                sb.Append("xray=").Append(options.XrayEscape ? 1 : 0).Append(';');
                sb.Append("coh=").Append(options.CoherentPassesThrough ? 1 : 0).Append(';');
                sb.Append("brem=").Append(options.Bremsstrahlung ? 1 : 0).Append(';');
                sb.Append("scat=").Append(options.SingleScatter ? 1 : 0).Append(';');
                sb.Append("npl=").Append(options.LightNonproportionality ? 1 : 0).Append(';');
                sb.Append("acont=").Append(options.AnalogContinuum ? 1 : 0).Append(';');
                sb.Append("bound=").Append(options.BoundScattering ? 1 : 0).Append(';');
                sb.Append("bremsb=").Append(options.BremFromData ? 1 : 0).Append(';');
            }

            sb.Append("geom=").Append(GeometryText(geometry));

            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                var hex = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash)
                {
                    hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                }

                return "phys=" + PhysicsVersion.ToString(CultureInfo.InvariantCulture)
                       + ";" + hex;
            }
        }

        /// <summary>
        /// Текст геометрии для отпечатка. Собирается тем же `GeometryWriter`,
        /// что сохраняет модель на диск, — но в памяти (`Render`), без
        /// временного файла: проверка годности зовётся с UI-потока на каждый
        /// тик живого набора, и файловая пара запись/чтение/удаление там —
        /// лишний ввод-вывод на ровном месте. Если сборка почему-либо не
        /// удалась, откатываемся на <see cref="GeometryModel.Describe"/>:
        /// хуже, но лучше, чем отпечаток, не зависящий от геометрии вовсе.
        /// </summary>
        static string GeometryText(GeometryModel geometry)
        {
            try
            {
                return GeometryWriter.Render(geometry);
            }
            catch (Exception)
            {
                return geometry.Describe();
            }
        }

        public bool IsValidFor(GeometryModel geometry, ResponseMatrixOptions options)
        {
            return !string.IsNullOrEmpty(this.Stamp)
                && string.Equals(this.Stamp, ComputeStamp(geometry, options), StringComparison.Ordinal);
        }

        /// <summary>Годна ли для этой геометрии — по СВОИМ параметрам.</summary>
        public bool IsValidFor(GeometryModel geometry)
        {
            return this.Options != null && this.IsValidFor(geometry, this.Options);
        }

        // ------------------------------------------------------------------
        // Чтение отклика
        // ------------------------------------------------------------------

        /// <summary>
        /// Отклик на линию энергии <paramref name="energyKev"/>: доли на бин.
        ///
        /// Между узлами сетки интерполируется НЕ поканально. Прямое усреднение
        /// двух соседних строк размазало бы комптоновский край в ступеньку
        /// шириной с шаг сетки: у соседних узлов край стоит в разных местах.
        /// Поэтому строки сначала растягиваются на общую шкалу «доля от энергии
        /// линии», и лишь потом смешиваются — при таком переносе и край, и пики
        /// вылета едут туда, где им положено.
        /// </summary>
        public double[] Evaluate(double energyKev, int bins)
        {
            double[] result = new double[bins];
            if (this.Rows == null || this.Energies == null || this.Energies.Length == 0
                || !(energyKev > 0.0) || bins <= 0)
            {
                return result;
            }

            int hi = Array.BinarySearch(this.Energies, energyKev);
            if (hi >= 0)
            {
                Stretch(this.Rows[hi], this.Energies[hi], energyKev, 1.0, result);
                return result;
            }

            hi = ~hi;
            if (hi <= 0)
            {
                Stretch(this.Rows[0], this.Energies[0], energyKev, 1.0, result);
                return result;
            }

            if (hi >= this.Energies.Length)
            {
                int last = this.Energies.Length - 1;
                Stretch(this.Rows[last], this.Energies[last], energyKev, 1.0, result);
                return result;
            }

            int lo = hi - 1;
            double span = this.Energies[hi] - this.Energies[lo];
            double t = span > 0.0 ? (energyKev - this.Energies[lo]) / span : 0.0;
            Stretch(this.Rows[lo], this.Energies[lo], energyKev, 1.0 - t, result);
            Stretch(this.Rows[hi], this.Energies[hi], energyKev, t, result);
            return result;
        }

        /// <summary>
        /// Перенести строку узла на шкалу линии: бин с долей `p` от энергии узла
        /// становится бином с той же долей от энергии линии. Площадь при этом
        /// сохраняется — вес делится между двумя соседними бинами приёмника.
        /// </summary>
        void Stretch(float[] row, double nodeEnergy, double lineEnergy, double weight, double[] target)
        {
            if (row == null || !(nodeEnergy > 0.0) || !(weight > 0.0))
            {
                return;
            }

            double scale = lineEnergy / nodeEnergy;
            for (int b = 0; b < row.Length; b++)
            {
                double value = row[b];
                if (!(value > 0.0))
                {
                    continue;
                }

                double position = b * scale;
                int at = (int)position;
                double frac = position - at;

                // Границы ЗАЖИМАЮТСЯ, а не отбрасываются. Номер бина пика узла —
                // уже округлённая величина, и умножение на масштаб способно
                // вынести его на долю бина за конец приёмника. Молчаливое
                // отбрасывание съедало при этом четверть пика: перенос обязан
                // сохранять площадь, а не терять её на краю.
                Add(target, at, weight * value * (1.0 - frac));
                Add(target, at + 1, weight * value * frac);
            }
        }

        /// <summary>
        /// Добавить отклик линии в уже готовый массив, без выделения памяти.
        /// Нужно образу компонента: линий у нуклида десятки, узлов сетки дрейфа
        /// 81, компонентов до десятка — выделять на каждую линию по массиву в
        /// полторы тысячи чисел значило бы мусорить десятками тысяч буферов на
        /// одно разложение.
        /// </summary>
        public void Accumulate(double[] target, double energyKev, double weight)
        {
            this.Accumulate(target, energyKev, weight, -1);
        }

        /// <summary>
        /// Отклик ОДНОГО канала исхода: пик, комптон, вылет 511 или вылет
        /// K-рентгена. <paramref name="channel"/> = −1 — весь отклик разом.
        /// </summary>
        public void AccumulateChannel(double[] target, double energyKev, double weight, int channel)
        {
            this.Accumulate(target, energyKev, weight, channel);
        }

        /// <summary>Есть ли у матрицы раскладка по каналам.</summary>
        public bool HasChannels
        {
            get { return this.ChannelRows != null && this.ChannelRows.Length > 0; }
        }

        void Accumulate(double[] target, double energyKev, double weight, int channel)
        {
            // Просили КОНКРЕТНЫЙ канал, а такого нет (матрица без каналов или
            // чужой номер) — вклад пустой. Молчаливый откат на суммарные
            // строки давал бы вызывающему, перебирающему каналы, двойной счёт
            // всего отклика вместо пустого канала.
            float[][] rows;
            if (channel < 0)
            {
                rows = this.Rows;
            }
            else if (this.HasChannels && channel < this.ChannelRows.Length)
            {
                rows = this.ChannelRows[channel];
            }
            else
            {
                return;
            }

            if (target == null || !(weight > 0.0) || !(energyKev > 0.0)
                || rows == null || this.Energies == null || this.Energies.Length == 0)
            {
                return;
            }

            int hi = Array.BinarySearch(this.Energies, energyKev);
            if (hi >= 0)
            {
                this.Stretch(rows[hi], this.Energies[hi], energyKev, weight, target);
                return;
            }

            hi = ~hi;
            if (hi <= 0)
            {
                this.Stretch(rows[0], this.Energies[0], energyKev, weight, target);
                return;
            }

            if (hi >= this.Energies.Length)
            {
                int last = this.Energies.Length - 1;
                this.Stretch(rows[last], this.Energies[last], energyKev, weight, target);
                return;
            }

            int lo = hi - 1;
            double span = this.Energies[hi] - this.Energies[lo];
            double t = span > 0.0 ? (energyKev - this.Energies[lo]) / span : 0.0;
            this.Stretch(rows[lo], this.Energies[lo], energyKev, weight * (1.0 - t), target);
            this.Stretch(rows[hi], this.Energies[hi], energyKev, weight * t, target);
        }

        /// <summary>
        /// Пересчитать суммарные строки из каналов. Зовётся после построения и
        /// после чтения файла — <see cref="Rows"/> в файле не лежит.
        ///
        /// Перенос строки канала на шкалу линии обязан идти по ЕЁ длине, а не
        /// по длине суммы: длины у всех каналов узла одинаковы (все они —
        /// раскладка одной гистограммы), и это здесь же проверяется зажимом.
        /// </summary>
        public void RebuildTotals()
        {
            if (!this.HasChannels || this.Energies == null)
            {
                return;
            }

            int nodes = this.Energies.Length;
            float[][] totals = new float[nodes][];
            for (int i = 0; i < nodes; i++)
            {
                int length = 0;
                foreach (float[][] channel in this.ChannelRows)
                {
                    if (channel != null && i < channel.Length && channel[i] != null && channel[i].Length > length)
                    {
                        length = channel[i].Length;
                    }
                }

                float[] total = new float[length];
                foreach (float[][] channel in this.ChannelRows)
                {
                    if (channel == null || i >= channel.Length || channel[i] == null)
                    {
                        continue;
                    }

                    float[] row = channel[i];
                    for (int b = 0; b < row.Length && b < length; b++)
                    {
                        total[b] += row[b];
                    }
                }

                totals[i] = total;
            }

            this.Rows = totals;
        }

        /// <summary>Вклад в бин с зажатием по краям — площадь не теряется.</summary>
        static void Add(double[] target, int index, double value)
        {
            if (!(value > 0.0) || target.Length == 0)
            {
                return;
            }

            if (index < 0)
            {
                index = 0;
            }
            else if (index >= target.Length)
            {
                index = target.Length - 1;
            }

            target[index] += value;
        }

        // ------------------------------------------------------------------
        // Файл
        // ------------------------------------------------------------------

        public void Save(string path)
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Пишем через временный файл: прерванная запись не должна оставить
            // обрубок, который при следующем открытии сойдёт за годную матрицу.
            string temp = path + ".tmp";
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write(Encoding.ASCII.GetBytes("BQRM"));
                writer.Write(FormatVersion);
                writer.Write(this.Stamp ?? "");
                writer.Write(this.BinKev);
                writer.Write(this.Histories);
                writer.Write(this.CreatedUtc.Ticks);
                writer.Write(this.BuildSeconds);
                WriteOptions(writer, this.Options);
                writer.Write(this.Energies.Length);
                foreach (double e in this.Energies)
                {
                    writer.Write(e);
                }

                // Каналы, а не сумма: сумма восстанавливается из них точно, а
                // обратно — нет. Пустой канал занимает по четыре байта на узел.
                writer.Write(this.ChannelRows.Length);
                foreach (float[][] channel in this.ChannelRows)
                {
                    foreach (float[] row in channel)
                    {
                        writer.Write(row.Length);
                        foreach (float v in row)
                        {
                            writer.Write(v);
                        }
                    }
                }
            }

            if (File.Exists(path))
            {
                File.Delete(path);
            }

            File.Move(temp, path);
        }

        static void WriteOptions(BinaryWriter writer, ResponseMatrixOptions o)
        {
            if (o == null)
            {
                o = new ResponseMatrixOptions();
            }

            writer.Write(o.MinEnergyKev);
            writer.Write(o.MaxEnergyKev);
            writer.Write(o.NodeCount);
            writer.Write(o.BinKev);
            writer.Write(o.Histories);
            writer.Write(o.XrayEscape);
            writer.Write(o.CoherentPassesThrough);
            writer.Write(o.Bremsstrahlung);
            writer.Write(o.SingleScatter);
            writer.Write(o.LightNonproportionality);
            writer.Write(o.AnalogContinuum);
            writer.Write(o.BoundScattering);
            writer.Write(o.BremFromData);
        }

        static ResponseMatrixOptions ReadOptions(BinaryReader reader)
        {
            return new ResponseMatrixOptions
            {
                MinEnergyKev = reader.ReadDouble(),
                MaxEnergyKev = reader.ReadDouble(),
                NodeCount = reader.ReadInt32(),
                BinKev = reader.ReadDouble(),
                Histories = reader.ReadInt32(),
                XrayEscape = reader.ReadBoolean(),
                CoherentPassesThrough = reader.ReadBoolean(),
                Bremsstrahlung = reader.ReadBoolean(),
                SingleScatter = reader.ReadBoolean(),
                LightNonproportionality = reader.ReadBoolean(),
                AnalogContinuum = reader.ReadBoolean(),
                BoundScattering = reader.ReadBoolean(),
                BremFromData = reader.ReadBoolean()
            };
        }

        /// <summary>
        /// Версии генерации из файла БЕЗ чтения матрицы целиком: версия формата
        /// и версия физики из отпечатка (`phys=N;` — первый ключ). Заголовок
        /// «магия, формат, отпечаток» одинаков у всех форматов, поэтому
        /// работает и для файла, который <see cref="Load"/> читать отказался, —
        /// ради формы, которая обязана сказать «устарела: формат N», а не
        /// «матрицы нет». false — файла нет или он не наш; неразобранная
        /// физика возвращается нулём (файл старше отпечатков с `phys=`).
        /// </summary>
        public static bool PeekVersions(string path, out int format, out int physics)
        {
            format = 0;
            physics = 0;
            if (!File.Exists(path))
            {
                return false;
            }

            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var reader = new BinaryReader(stream, Encoding.UTF8))
                {
                    if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "BQRM")
                    {
                        return false;
                    }

                    format = reader.ReadInt32();
                    physics = PhysicsFromStamp(reader.ReadString());
                    return true;
                }
            }
            catch (Exception)
            {
                return format > 0;      // формат прочли — уже есть что сказать
            }
        }

        /// <summary>Версия физики из отпечатка; 0 — в отпечатке её нет.</summary>
        public static int PhysicsFromStamp(string stamp)
        {
            if (string.IsNullOrEmpty(stamp) || !stamp.StartsWith("phys=", StringComparison.Ordinal))
            {
                return 0;
            }

            int end = stamp.IndexOf(';');
            int value;
            return end > 5 && int.TryParse(stamp.Substring(5, end - 5), NumberStyles.None,
                                           CultureInfo.InvariantCulture, out value)
                ? value : 0;
        }

        /// <summary>Читает матрицу; null, если файла нет или он не наш.</summary>
        public static ResponseMatrix Load(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var reader = new BinaryReader(stream, Encoding.UTF8))
                {
                    if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "BQRM")
                    {
                        return null;
                    }

                    if (reader.ReadInt32() != FormatVersion)
                    {
                        return null;
                    }

                    var matrix = new ResponseMatrix
                    {
                        Stamp = reader.ReadString(),
                        BinKev = reader.ReadDouble(),
                        Histories = reader.ReadInt32(),
                        CreatedUtc = new DateTime(reader.ReadInt64(), DateTimeKind.Utc),
                        BuildSeconds = reader.ReadDouble()
                    };

                    matrix.Options = ReadOptions(reader);
                    int nodes = reader.ReadInt32();
                    matrix.Energies = new double[nodes];
                    for (int i = 0; i < nodes; i++)
                    {
                        matrix.Energies[i] = reader.ReadDouble();
                    }

                    int channels = reader.ReadInt32();
                    matrix.ChannelRows = new float[channels][][];
                    for (int c = 0; c < channels; c++)
                    {
                        float[][] rows = new float[nodes][];
                        for (int i = 0; i < nodes; i++)
                        {
                            int length = reader.ReadInt32();
                            float[] row = new float[length];
                            for (int b = 0; b < length; b++)
                            {
                                row[b] = reader.ReadSingle();
                            }

                            rows[i] = row;
                        }

                        matrix.ChannelRows[c] = rows;
                    }

                    matrix.RebuildTotals();
                    return matrix;
                }
            }
            catch (Exception)
            {
                // Обрубок или чужой файл — это «матрицы нет», а не отказ работы.
                return null;
            }
        }
    }

    /// <summary>
    /// Корешок матрицы — то немногое, что можно держать в конфигурации: он
    /// маленький, и его отъезд в файл спектра безвреден.
    /// </summary>
    public sealed class ResponseMatrixInfo
    {
        public string Stamp { get; set; }

        public DateTime CreatedUtc { get; set; }

        public int NodeCount { get; set; }

        public int Histories { get; set; }

        public double BinKev { get; set; }

        public long DataBytes { get; set; }
    }

    /// <summary>Параметры построения. Все входят в отпечаток.</summary>
    public sealed class ResponseMatrixOptions
    {
        public double MinEnergyKev = 30.0;

        public double MaxEnergyKev = 3000.0;

        /// <summary>Узлов сетки. Сетка логарифмическая: внизу отклик меняется быстрее.</summary>
        public int NodeCount = 100;

        public double BinKev = 2.0;

        public int Histories = 300000;

        public bool XrayEscape = true;

        public bool CoherentPassesThrough = true;

        public bool Bremsstrahlung = true;

        public bool SingleScatter = true;

        /// <summary>
        /// Отклик в шкале света (непропорциональность светового выхода,
        /// TODO F11): каждый электронный вклад взвешивается кривой L(E) из
        /// nucdb, бины пересчитываются с якорем по пику. Без кривой для
        /// вещества кристалла (германий, CZT) ключ ничего не меняет.
        /// </summary>
        public bool LightNonproportionality = true;

        /// <summary>
        /// Континуум строк — аналоговой веткой переноса (физика 6, F14);
        /// выключенный ключ возвращает прежний взвешенный континуум с его
        /// измеренным недобором 0.57–0.92 от Geant4 — только для сравнения.
        /// </summary>
        public bool AnalogContinuum = true;

        /// <summary>
        /// Рассеяние на СВЯЗАННОМ электроне (N11, физика 7): угол комптона с
        /// множителем отбора S(x,Z), доплеровское размытие по профилям
        /// Комптона и когерентное отдельным каналом с углом по F²(x,Z).
        /// Один ключ на три части сознательно: в матрице они не разделяются,
        /// а по отдельности их отпирают поля симулятора — для абляций
        /// (<see cref="EfficiencySimulator.BoundCompton"/>,
        /// <see cref="EfficiencySimulator.DopplerBroadening"/>,
        /// <see cref="EfficiencySimulator.RayleighScatter"/>).
        /// </summary>
        public bool BoundScattering = true;

        /// <summary>
        /// Спектр тормозного из сечений Зельцера — Бергера (M3, физика 8)
        /// вместо приближения Крамерса dN/dk = C/k. Выключенный ключ
        /// возвращает приближение — для абляции.
        /// </summary>
        public bool BremFromData = true;

        /// <summary>Потоков; 0 — по числу ядер минус один.</summary>
        public int Threads;

        public ResponseMatrixOptions Clone()
        {
            return (ResponseMatrixOptions)this.MemberwiseClone();
        }

        /// <summary>Узлы сетки, кэВ.</summary>
        public double[] BuildGrid()
        {
            int n = Math.Max(2, this.NodeCount);
            double lo = Math.Max(1.0, this.MinEnergyKev);
            double hi = Math.Max(lo * 1.01, this.MaxEnergyKev);
            double[] grid = new double[n];
            double logLo = Math.Log(lo), logHi = Math.Log(hi);
            for (int i = 0; i < n; i++)
            {
                grid[i] = Math.Exp(logLo + (logHi - logLo) * i / (n - 1));
            }

            return grid;
        }
    }
}
