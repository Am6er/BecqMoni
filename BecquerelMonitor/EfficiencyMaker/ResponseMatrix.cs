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
        public const int FormatVersion = 2;

        /// <summary>
        /// Версия ФИЗИКИ. Поднимать при любой правке переноса, меняющей числа:
        /// иначе в кэше молча останется матрица, посчитанная старой моделью, и
        /// это ровно тот сорт ошибки, который не проявляется, а тихо смещает
        /// результат.
        /// </summary>
        public const int PhysicsVersion = 1;

        /// <summary>Узлы сетки входных энергий, кэВ, по возрастанию.</summary>
        public double[] Energies { get; set; }

        /// <summary>Шаг бина поглощённой энергии, кэВ.</summary>
        public double BinKev { get; set; }

        /// <summary>Строки: на узел — доли на бин. Длина строки своя у каждого узла.</summary>
        public float[][] Rows { get; set; }

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

        public int NodeCount
        {
            get { return this.Energies != null ? this.Energies.Length : 0; }
        }

        /// <summary>Размер в памяти/на диске, байт (только числа откликов).</summary>
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

                return hex.ToString();
            }
        }

        /// <summary>
        /// Текст геометрии для отпечатка. Пишется тем же `GeometryWriter`, что
        /// сохраняет модель на диск, — во временный файл, потому что другого
        /// входа у него нет. Если запись почему-либо не удалась, откатываемся на
        /// <see cref="GeometryModel.Describe"/>: хуже, но лучше, чем отпечаток,
        /// не зависящий от геометрии вовсе.
        /// </summary>
        static string GeometryText(GeometryModel geometry)
        {
            string path = null;
            try
            {
                path = Path.Combine(Path.GetTempPath(), "bqm_stamp_" + Guid.NewGuid().ToString("N") + ".in");
                GeometryWriter.Save(geometry, path);
                return File.ReadAllText(path);
            }
            catch (Exception)
            {
                return geometry.Describe();
            }
            finally
            {
                try
                {
                    if (path != null && File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }
                catch (Exception)
                {
                    // временный файл — не повод падать
                }
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
            if (target == null || !(weight > 0.0) || !(energyKev > 0.0)
                || this.Rows == null || this.Energies == null || this.Energies.Length == 0)
            {
                return;
            }

            int hi = Array.BinarySearch(this.Energies, energyKev);
            if (hi >= 0)
            {
                this.Stretch(this.Rows[hi], this.Energies[hi], energyKev, weight, target);
                return;
            }

            hi = ~hi;
            if (hi <= 0)
            {
                this.Stretch(this.Rows[0], this.Energies[0], energyKev, weight, target);
                return;
            }

            if (hi >= this.Energies.Length)
            {
                int last = this.Energies.Length - 1;
                this.Stretch(this.Rows[last], this.Energies[last], energyKev, weight, target);
                return;
            }

            int lo = hi - 1;
            double span = this.Energies[hi] - this.Energies[lo];
            double t = span > 0.0 ? (energyKev - this.Energies[lo]) / span : 0.0;
            this.Stretch(this.Rows[lo], this.Energies[lo], energyKev, weight * (1.0 - t), target);
            this.Stretch(this.Rows[hi], this.Energies[hi], energyKev, weight * t, target);
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

                foreach (float[] row in this.Rows)
                {
                    writer.Write(row.Length);
                    foreach (float v in row)
                    {
                        writer.Write(v);
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
                SingleScatter = reader.ReadBoolean()
            };
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

                    matrix.Rows = new float[nodes][];
                    for (int i = 0; i < nodes; i++)
                    {
                        int length = reader.ReadInt32();
                        float[] row = new float[length];
                        for (int b = 0; b < length; b++)
                        {
                            row[b] = reader.ReadSingle();
                        }

                        matrix.Rows[i] = row;
                    }

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
