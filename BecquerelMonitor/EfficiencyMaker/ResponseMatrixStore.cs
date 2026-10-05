using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace BecquerelMonitor.EfficiencyMaker
{
    /// <summary>
    /// Где лежат матрицы отклика: `config\device\response\<guid>.rmx`, по файлу
    /// на геометрию, ключ — Guid кривой эффективности (у каждой своя геометрия).
    ///
    /// Отдельно от конфигурации, и это не вкусовщина. `ResultData.DeviceConfig`
    /// сериализуется в файл спектра целиком; матрица, положенная в
    /// конфигурацию, уезжала бы в каждый сохранённый спектр — сотни килобайт,
    /// бесполезных получателю. Здесь же файл остаётся на машине, где посчитан,
    /// а спектр уносит в лучшем случае корешок в несколько десятков байт.
    /// (`AMBER202`, 05.10.2026) Получателю матрица оказалась НЕ бесполезна —
    /// теперь она едет в ФАЙЛЕ СПЕКТРА отдельным блоком рядом с кривой
    /// (<see cref="EmbeddedResponseMatrix"/>, выключатель в общих настройках),
    /// но по-прежнему НЕ в конфигурации прибора: блок принадлежит
    /// `ResultData`, а не `EfficiencyConfigData`, и в `config\device\*.xml` не
    /// попадает.
    ///
    /// ⛔ (`AMBER186`, решение Amber 05.10.2026 «Временный файл + перенос») У
    /// матрицы ДВА места, и читатели разбора видят только одно.
    ///
    ///   * <see cref="ResponseMatrixSource.Store"/> — склад, `{guid}.rmx`. Его
    ///     и только его читают разбор, доза и коэффициенты
    ///     (<see cref="Load(string)"/>, <see cref="PathOf"/>).
    ///   * <see cref="ResponseMatrixSource.Pending"/> — `{guid}.rmx.pending`
    ///     рядом со складом. Сюда пишет кнопка «Сохранить» окна матрицы
    ///     (<see cref="SavePending"/>): геометрия, для которой матрица
    ///     посчитана, живёт в КЛОНЕ конфигурации прибора, и пока клон не
    ///     сохранён, класть матрицу на склад нельзя — ответ «Нет» на вопрос
    ///     формы прибора оставлял прежнюю геометрию при затёртой матрице.
    ///     Сохранение конфигурации переносит временные на склад заменой
    ///     (<see cref="CommitPending"/>), отказ — снимает
    ///     (<see cref="DiscardPending"/>). Расширение `.pending` нарочно не
    ///     `.rmx`: маска `*.rmx` его не берёт.
    ///
    /// Третий источник (`AMBER202`, решения Amber 05.10.2026 «Внутри файла
    /// спектра (Рекомендую)» и «Настройка, умолчание ВКЛ (Рекомендую)») —
    /// <see cref="ResponseMatrixSource.Spectrum"/>: матрица, приехавшая В ФАЙЛЕ
    /// СПЕКТРА блоком <see cref="EmbeddedResponseMatrix"/>. Живёт только в
    /// памяти (на склад получателя не кладётся — такого решения не было), и
    /// читатели разбора, дозы и коэффициентов берут её ОДНИМ путём —
    /// <see cref="ResponseMatrixStore.Resolve"/>: склад, а если на складе нет
    /// годной по клейму для этого Guid кривой — встроенная.
    /// </summary>
    public enum ResponseMatrixSource
    {
        /// <summary>Склад `config\device\response\{guid}.rmx` — читают разбор и доза.</summary>
        Store,

        /// <summary>Временный файл окна матрицы, ждущий сохранения конфигурации прибора.</summary>
        Pending,

        /// <summary>
        /// (`AMBER202`) Блок файла спектра, в памяти. Пути на диске у него нет:
        /// <see cref="ResponseMatrixStore.PathOf(string, ResponseMatrixSource)"/>
        /// отвечает на него путём склада.
        /// </summary>
        Spectrum,
    }

    /// <summary>
    /// (`AMBER202`) Почему матрица из файла спектра не взята. Причина словами —
    /// <see cref="EmbeddedResponseMatrix.RefusalText"/>.
    /// </summary>
    public enum EmbeddedMatrixRefusal
    {
        /// <summary>Взята.</summary>
        None,

        /// <summary>Блок от другой кривой (Guid не тот).</summary>
        OtherCurve,

        /// <summary>Блок повреждён: base64, сжатие, длина, контрольная сумма, обрубок, клеймо блока против клейма матрицы.</summary>
        Damaged,

        /// <summary>Матрица прежнего формата файла.</summary>
        OldFormat,

        /// <summary>Клеймо матрицы не сходится с геометрией кривой.</summary>
        Stale,
    }

    /// <summary>
    /// (`AMBER202`) Матрица отклика ВНУТРИ ФАЙЛА СПЕКТРА — необязательный блок
    /// рядом с записью кривой (`ResultData.EmbeddedResponseMatrix`, элемент
    /// XML сразу за `&lt;Efficiency&gt;`).
    ///
    /// Несёт БАЙТЫ файла `.rmx` склада как есть — сжатые GZip и в base64, — а
    /// не разобранную матрицу: читается она тем же
    /// <see cref="ResponseMatrix.Load(Stream, out MatrixRefusal, out int, int)"/>,
    /// что и склад, и у получателя разбор побитово тот же, что у отправителя.
    /// Рядом — Guid кривой, клеймо матрицы, формат, длина и SHA-256 исходных
    /// байтов: по ним блок судится до разбора, а испорченный называет причину
    /// словами (<see cref="EmbeddedMatrixRefusal"/>), а не роняет открытие.
    ///
    /// ⚠ `Data` — СТРОКА, а не `byte[]`: у `byte[]` base64 разбирает сам
    /// `XmlSerializer`, и обрезанный блок валил бы чтение ВСЕГО файла спектра.
    /// Старые версии приложения элемент не знают и пропускают (проверено
    /// опытом сборкой `f4abe6c8`, журнал `handover/app-fix-2026-10-05-fix202.md`).
    /// </summary>
    public class EmbeddedResponseMatrix
    {
        /// <summary>Guid кривой эффективности, для которой матрица посчитана.</summary>
        public string EfficiencyGuid { get; set; }

        /// <summary>Клеймо матрицы (<see cref="ResponseMatrix.Stamp"/>), для глаз и сверки.</summary>
        public string Stamp { get; set; }

        /// <summary>Версия формата файла `.rmx`.</summary>
        public int Format { get; set; }

        /// <summary>Длина исходных (несжатых) байтов `.rmx`.</summary>
        public long Length { get; set; }

        /// <summary>SHA-256 исходных байтов `.rmx`, шестнадцатеричной строкой.</summary>
        public string Sha256 { get; set; }

        /// <summary>Байты `.rmx`, сжатые GZip, в base64 (строки по 76 знаков).</summary>
        public string Data { get; set; }

        /// <summary>Собрать блок из файла склада.</summary>
        public static EmbeddedResponseMatrix FromFile(string efficiencyGuid, string path, ResponseMatrix matrix)
        {
            byte[] raw = File.ReadAllBytes(path);
            byte[] packed;
            using (var buffer = new MemoryStream())
            {
                using (var gzip = new GZipStream(buffer, CompressionLevel.Optimal, true))
                {
                    gzip.Write(raw, 0, raw.Length);
                }

                packed = buffer.ToArray();
            }

            return new EmbeddedResponseMatrix
            {
                EfficiencyGuid = efficiencyGuid,
                Stamp = matrix != null ? matrix.Stamp : "",
                Format = ResponseMatrix.FormatVersion,
                Length = raw.LongLength,
                Sha256 = Hash(raw),
                Data = Convert.ToBase64String(packed, Base64FormattingOptions.InsertLineBreaks),
            };
        }

        /// <summary>
        /// Распаковать и прочитать матрицу для этой кривой. null — отказ, причина
        /// в <paramref name="refusal"/> (и версия файла в <paramref name="fileFormat"/>
        /// при <see cref="EmbeddedMatrixRefusal.OldFormat"/>). Годность по
        /// геометрии кривой проверяется здесь же: негодная матрица не взята.
        /// </summary>
        public ResponseMatrix Decode(EfficiencyConfigData efficiency, out EmbeddedMatrixRefusal refusal,
                                     out int fileFormat)
        {
            fileFormat = 0;
            refusal = EmbeddedMatrixRefusal.OtherCurve;
            if (efficiency == null
                || !string.Equals(this.EfficiencyGuid, efficiency.Guid, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            refusal = EmbeddedMatrixRefusal.Damaged;
            byte[] raw;
            try
            {
                byte[] packed = Convert.FromBase64String(this.Data ?? "");
                using (var input = new MemoryStream(packed))
                using (var gzip = new GZipStream(input, CompressionMode.Decompress))
                using (var output = new MemoryStream())
                {
                    gzip.CopyTo(output);
                    raw = output.ToArray();
                }
            }
            catch (Exception)
            {
                return null;
            }

            if (raw.LongLength != this.Length
                || !string.Equals(Hash(raw), this.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            MatrixRefusal read;
            ResponseMatrix matrix;
            using (var stream = new MemoryStream(raw, false))
            {
                matrix = ResponseMatrix.Load(stream, out read, out fileFormat);
            }

            if (matrix == null)
            {
                refusal = read == MatrixRefusal.OldFormat ? EmbeddedMatrixRefusal.OldFormat : EmbeddedMatrixRefusal.Damaged;
                return null;
            }

            if (!string.Equals(matrix.Stamp ?? "", this.Stamp ?? "", StringComparison.Ordinal))
            {
                return null;
            }

            if (efficiency.HasGeometry && !matrix.IsValidFor(efficiency.Geometry))
            {
                refusal = EmbeddedMatrixRefusal.Stale;
                return null;
            }

            refusal = EmbeddedMatrixRefusal.None;
            return matrix;
        }

        /// <summary>Причина отказа словами (обе культуры, `Properties.Resources`).</summary>
        public static string RefusalText(EmbeddedMatrixRefusal refusal, int fileFormat)
        {
            switch (refusal)
            {
                case EmbeddedMatrixRefusal.None:
                    return "";
                case EmbeddedMatrixRefusal.OtherCurve:
                    return Properties.Resources.EmbeddedMatrixOtherCurve;
                case EmbeddedMatrixRefusal.OldFormat:
                    return string.Format(CultureInfo.InvariantCulture, Properties.Resources.EmbeddedMatrixOldFormat,
                                         fileFormat, ResponseMatrix.FormatVersion);
                case EmbeddedMatrixRefusal.Stale:
                    return Properties.Resources.EmbeddedMatrixStale;
                default:
                    return Properties.Resources.EmbeddedMatrixDamaged;
            }
        }

        static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(bytes);
                var hex = new StringBuilder(digest.Length * 2);
                foreach (byte b in digest)
                {
                    hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                }

                return hex.ToString();
            }
        }
    }

    /// <summary>Склад матриц отклика — см. шапку <see cref="ResponseMatrixSource"/>.</summary>
    public static class ResponseMatrixStore
    {
        /// <summary>Хвост имени временного файла (`AMBER186`).</summary>
        public const string PendingExtension = ".rmx.pending";

        public static string Directory
        {
            get
            {
                return Path.Combine(Package.GetInstance().DeviceDir, "response");
            }
        }

        public static string PathOf(string efficiencyGuid)
        {
            return PathOf(efficiencyGuid, ResponseMatrixSource.Store);
        }

        /// <summary>Путь матрицы кривой в данном месте (`AMBER186`).</summary>
        public static string PathOf(string efficiencyGuid, ResponseMatrixSource source)
        {
            string name = Sanitize(efficiencyGuid);
            switch (source)
            {
                case ResponseMatrixSource.Pending:
                    return Path.Combine(Directory, name + PendingExtension);
                default:
                    return Path.Combine(Directory, name + ".rmx");
            }
        }

        /// <summary>Есть ли у кривой матрица, ждущая сохранения конфигурации.</summary>
        public static bool HasPending(string efficiencyGuid)
        {
            return !string.IsNullOrEmpty(efficiencyGuid)
                   && File.Exists(PathOf(efficiencyGuid, ResponseMatrixSource.Pending));
        }

        /// <summary>
        /// Откуда окну матрицы и вкладке прибора брать матрицу кривой: ждущая
        /// сохранения конфигурации — новее склада и показывается она.
        /// Разбор этим не пользуется.
        /// </summary>
        public static ResponseMatrixSource EditingSource(string efficiencyGuid)
        {
            return HasPending(efficiencyGuid) ? ResponseMatrixSource.Pending : ResponseMatrixSource.Store;
        }

        public static bool Exists(string efficiencyGuid)
        {
            return !string.IsNullOrEmpty(efficiencyGuid) && File.Exists(PathOf(efficiencyGuid));
        }

        public static ResponseMatrix Load(string efficiencyGuid)
        {
            MatrixRefusal refusal;
            int fileFormat;
            return Load(efficiencyGuid, out refusal, out fileFormat);
        }

        /// <summary>
        /// То же, но отказ называет себя (`A50`) — см.
        /// <see cref="ResponseMatrix.Load(string, out MatrixRefusal, out int)"/>.
        /// </summary>
        public static ResponseMatrix Load(string efficiencyGuid, out MatrixRefusal refusal,
                                          out int fileFormat)
        {
            return Load(efficiencyGuid, ResponseMatrixSource.Store, out refusal, out fileFormat);
        }

        /// <summary>То же из данного места (`AMBER186`).</summary>
        public static ResponseMatrix Load(string efficiencyGuid, ResponseMatrixSource source,
                                          out MatrixRefusal refusal, out int fileFormat)
        {
            refusal = MatrixRefusal.NoFile;
            fileFormat = 0;
            if (string.IsNullOrEmpty(efficiencyGuid))
            {
                return null;
            }

            return ResponseMatrix.Load(PathOf(efficiencyGuid, source), out refusal, out fileFormat);
        }

        /// <summary>
        /// ⛔ (`AMBER202`) ЕДИНЫЙ ПУТЬ ЧИТАТЕЛЕЙ К МАТРИЦЕ КРИВОЙ — разбор FSA,
        /// доза, коэффициенты Бк. Порядок — решение постановки: СКЛАД, а если на
        /// складе нет годной по клейму матрицы для этого Guid кривой — матрица
        /// из файла спектра, приехавшая с кривой
        /// (<see cref="EfficiencyConfigData.EmbeddedMatrix"/>), в память.
        ///
        /// Возвращает склад и тогда, когда тот негоден, а встроенной нет или
        /// она отвергнута: годность по геометрии читатели судят сами, как и
        /// прежде, и их слова об этом не меняются. <paramref name="refusal"/> и
        /// <paramref name="fileFormat"/> — склада (`A50`); при взятой встроенной
        /// отказ <see cref="MatrixRefusal.None"/>. <paramref name="spectrumRefusal"/>
        /// — причина словами, почему встроенная НЕ взята (пусто — её не было
        /// или она взята).
        /// </summary>
        public static ResponseMatrix Resolve(EfficiencyConfigData efficiency, out MatrixRefusal refusal,
                                             out int fileFormat, out ResponseMatrixSource source,
                                             out string spectrumRefusal)
        {
            refusal = MatrixRefusal.NoFile;
            fileFormat = 0;
            source = ResponseMatrixSource.Store;
            spectrumRefusal = "";
            if (efficiency == null || string.IsNullOrEmpty(efficiency.Guid))
            {
                return null;
            }

            ResponseMatrix stored = Load(efficiency.Guid, out refusal, out fileFormat);
            if (stored != null && (!efficiency.HasGeometry || stored.IsValidFor(efficiency.Geometry)))
            {
                return stored;
            }

            EmbeddedResponseMatrix embedded = efficiency.EmbeddedMatrix;
            if (embedded == null)
            {
                return stored;
            }

            EmbeddedMatrixRefusal why;
            int embeddedFormat;
            ResponseMatrix carried = embedded.Decode(efficiency, out why, out embeddedFormat);
            if (carried == null)
            {
                spectrumRefusal = EmbeddedResponseMatrix.RefusalText(why, embeddedFormat);
                return stored;
            }

            refusal = MatrixRefusal.None;
            fileFormat = 0;
            source = ResponseMatrixSource.Spectrum;
            return carried;
        }

        /// <summary>
        /// (`AMBER202`) Отметка источников матрицы кривой для кэшей и отпечатков
        /// читателей: файл склада (длина и время записи) и блок из файла
        /// спектра (его SHA-256). Сам файл не читается. Одна на всех читателей —
        /// прежде каждый складывал её сам.
        /// </summary>
        public static string SourceStamp(EfficiencyConfigData efficiency)
        {
            if (efficiency == null)
            {
                return "-";
            }

            string file;
            try
            {
                var info = new FileInfo(PathOf(efficiency.Guid));
                file = info.Exists
                    ? info.Length.ToString(CultureInfo.InvariantCulture) + ":"
                      + info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture)
                    : "-";
            }
            catch (Exception)
            {
                // недоступный файл — то же, что отсутствующий: его не прочтут
                file = "?";
            }

            EmbeddedResponseMatrix embedded = efficiency.EmbeddedMatrix;
            return file + "|emb:" + (embedded != null ? embedded.Sha256 ?? "?" : "-");
        }

        static readonly object embedSync = new object();

        static string embedKey;

        static EmbeddedResponseMatrix embedBlock;

        static EmbeddedResponseMatrix carriedChecked;

        static string carriedKey;

        static bool carriedValid;

        /// <summary>
        /// (`AMBER202`) Блок матрицы для записи в файл спектра этой кривой — или
        /// null, писать нечего. Зовётся из сериализации файла спектра
        /// (`ResultData.EmbeddedResponseMatrix`), выключатель — общая настройка
        /// «Сохранять матрицу отклика в файл спектра» (проверяет вызывающий).
        ///
        /// Берётся то же, что взял бы разбор: годная по клейму матрица СКЛАДА;
        /// нет её — приехавшая с кривой (получатель пересохраняет файл, и
        /// матрица не теряется), если она годна. У кривой без геометрии или с
        /// выключенной галкой «пускать матрицу» (`W11`) — ничего: матрицей
        /// такой кривой разбор не пользуется.
        ///
        /// Кэш — по файлу склада (путь, длина, время) и отпечатку геометрии:
        /// автосохранение при наборе зовёт это каждые несколько секунд, а
        /// проверка годности — полный разбор полумегабайтного файла.
        /// </summary>
        public static EmbeddedResponseMatrix EmbedFor(EfficiencyConfigData efficiency)
        {
            if (efficiency == null || string.IsNullOrEmpty(efficiency.Guid)
                || !efficiency.HasGeometry || !efficiency.UseResponseMatrix)
            {
                return null;
            }

            string geometry = ResponseMatrix.GeometryFingerprint(efficiency.Geometry);
            string path = PathOf(efficiency.Guid);
            try
            {
                var info = new FileInfo(path);
                if (info.Exists)
                {
                    string key = string.Concat(path, "|", info.Length.ToString(CultureInfo.InvariantCulture),
                                               "|", info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture),
                                               "|", geometry);
                    EmbeddedResponseMatrix block;
                    bool known;
                    lock (embedSync)
                    {
                        known = key == embedKey;
                        block = embedBlock;
                    }

                    if (!known)
                    {
                        // Негодная матрица склада тоже запоминается (null):
                        // её файл не меняется, и разбирать его на каждое
                        // автосохранение незачем.
                        ResponseMatrix matrix = ResponseMatrix.Load(path);
                        block = matrix != null && matrix.IsValidFor(efficiency.Geometry)
                            ? EmbeddedResponseMatrix.FromFile(efficiency.Guid, path, matrix)
                            : null;
                        lock (embedSync)
                        {
                            embedKey = key;
                            embedBlock = block;
                        }
                    }

                    if (block != null)
                    {
                        return block;
                    }
                }
            }
            catch (Exception)
            {
                // Склад недоступен — пишем то, что приехало с кривой, если оно есть.
            }

            EmbeddedResponseMatrix carried = efficiency.EmbeddedMatrix;
            if (carried == null)
            {
                return null;
            }

            string carriedFor = efficiency.Guid + "|" + geometry;
            lock (embedSync)
            {
                if (object.ReferenceEquals(carried, carriedChecked) && carriedFor == carriedKey)
                {
                    return carriedValid ? carried : null;
                }
            }

            EmbeddedMatrixRefusal why;
            int format;
            bool valid = carried.Decode(efficiency, out why, out format) != null;
            lock (embedSync)
            {
                carriedChecked = carried;
                carriedKey = carriedFor;
                carriedValid = valid;
            }

            return valid ? carried : null;
        }

        /// <summary>
        /// Поколения матрицы БЕЗ чтения её целиком: версия формата файла и
        /// версия физики из клейма (`A119`). Обёртка над
        /// <see cref="ResponseMatrix.PeekVersions"/> ровно затем, чтобы
        /// потребителю не приходилось складывать путь склада руками: тот, кто
        /// складывает путь сам, однажды сложит его иначе.
        ///
        /// ⚠ Физика нулём значит «в клейме её нет» (файл старше клейм с
        /// `phys=`), а не «поколение 0»; отличать это обязан потребитель.
        /// false — файла нет или он не наш.
        /// </summary>
        public static bool PeekVersions(string efficiencyGuid, out int format, out int physics)
        {
            format = 0;
            physics = 0;
            if (string.IsNullOrEmpty(efficiencyGuid))
            {
                return false;
            }

            return ResponseMatrix.PeekVersions(PathOf(efficiencyGuid), out format, out physics);
        }

        /// <summary>То же из данного места (`AMBER186`).</summary>
        public static bool PeekVersions(string efficiencyGuid, ResponseMatrixSource source,
                                        out int format, out int physics)
        {
            format = 0;
            physics = 0;
            if (string.IsNullOrEmpty(efficiencyGuid))
            {
                return false;
            }

            return ResponseMatrix.PeekVersions(PathOf(efficiencyGuid, source), out format, out physics);
        }

        /// <summary>
        /// Прямо на склад. Приложение так больше не пишет (`AMBER186`): окно
        /// матрицы кладёт её во временный файл (<see cref="SavePending"/>).
        /// Оставлено оснастке, у которой нет формы прибора.
        /// </summary>
        public static void Save(string efficiencyGuid, ResponseMatrix matrix)
        {
            if (string.IsNullOrEmpty(efficiencyGuid) || matrix == null)
            {
                throw new ArgumentNullException("matrix");
            }

            matrix.Save(PathOf(efficiencyGuid));
        }

        /// <summary>
        /// (`AMBER186`) Записать матрицу во ВРЕМЕННЫЙ файл кривой: на склад она
        /// уйдёт сохранением конфигурации прибора (<see cref="CommitPending"/>).
        /// Повторная запись заменяет прежний временный.
        /// </summary>
        public static void SavePending(string efficiencyGuid, ResponseMatrix matrix)
        {
            if (string.IsNullOrEmpty(efficiencyGuid) || matrix == null)
            {
                throw new ArgumentNullException("matrix");
            }

            matrix.Save(PathOf(efficiencyGuid, ResponseMatrixSource.Pending));
        }

        /// <summary>
        /// (`AMBER186`) Перенести временные матрицы этих кривых на склад —
        /// ЗАМЕНОЙ (`File.Replace`, один каталог, один том): читатель видит либо
        /// прежний файл склада, либо новый, обрубка между ними нет. Кривые без
        /// временного файла пропускаются. Возвращает отказы словами (пусто — всё
        /// перенесено); временный, который перенести не удалось, остаётся.
        /// </summary>
        public static List<string> CommitPending(IEnumerable<string> efficiencyGuids)
        {
            List<string> failures = new List<string>();
            if (efficiencyGuids == null)
            {
                return failures;
            }

            foreach (string guid in efficiencyGuids)
            {
                if (!HasPending(guid))
                {
                    continue;
                }

                string pending = PathOf(guid, ResponseMatrixSource.Pending);
                string target = PathOf(guid);
                try
                {
                    if (File.Exists(target))
                    {
                        File.Replace(pending, target, null);
                    }
                    else
                    {
                        File.Move(pending, target);
                    }
                }
                catch (Exception ex)
                {
                    failures.Add(string.Format(CultureInfo.InvariantCulture, "{0}: {1}", target, ex.Message));
                }
            }

            return failures;
        }

        /// <summary>(`AMBER186`) Снять временные матрицы этих кривых — отказ от сохранения.</summary>
        public static void DiscardPending(IEnumerable<string> efficiencyGuids)
        {
            if (efficiencyGuids == null)
            {
                return;
            }

            foreach (string guid in efficiencyGuids)
            {
                if (string.IsNullOrEmpty(guid))
                {
                    continue;
                }

                TryDelete(PathOf(guid, ResponseMatrixSource.Pending));
            }
        }

        /// <summary>
        /// (`AMBER186`) Снять ВСЕ временные матрицы склада — при закрытии формы
        /// прибора, после её вопроса о сохранении: ничьими они к этому моменту
        /// быть уже не могут, а оставленные (обрыв, падение) копились бы.
        /// </summary>
        public static void DiscardAllPending()
        {
            try
            {
                if (!System.IO.Directory.Exists(Directory))
                {
                    return;
                }

                foreach (string path in System.IO.Directory.GetFiles(Directory, "*" + PendingExtension))
                {
                    TryDelete(path);
                }
            }
            catch (Exception)
            {
                // Каталог недоступен — снять нечего; читатели склада временных
                // всё равно не видят.
            }
        }

        /// <summary>
        /// (`AMBER186`) Снять со склада матрицы кривых, удалённых из
        /// конфигурации: <paramref name="removedGuids"/> — были, а теперь нет;
        /// <paramref name="stillReferenced"/> — Guid кривых, которые остаются у
        /// ДРУГИХ конфигураций прибора (копия конфигурации наследует Guid кривых
        /// и делит с прежней файл склада — такой не трогается).
        /// </summary>
        public static void DeleteRemoved(IEnumerable<string> removedGuids, ICollection<string> stillReferenced)
        {
            if (removedGuids == null)
            {
                return;
            }

            foreach (string guid in removedGuids)
            {
                if (string.IsNullOrEmpty(guid)
                    || (stillReferenced != null && stillReferenced.Contains(guid)))
                {
                    continue;
                }

                TryDelete(PathOf(guid));
                TryDelete(PathOf(guid, ResponseMatrixSource.Pending));
            }
        }

        static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception)
            {
                // Занят читателем — останется до следующего раза; склад от
                // лишнего файла не портится, разбор ищет матрицу по Guid кривой.
            }
        }

        public static void Delete(string efficiencyGuid)
        {
            if (string.IsNullOrEmpty(efficiencyGuid))
            {
                return;
            }

            string path = PathOf(efficiencyGuid);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        public static long FileSize(string efficiencyGuid)
        {
            return FileSize(efficiencyGuid, ResponseMatrixSource.Store);
        }

        /// <summary>Размер файла матрицы в данном месте; ноль — файла нет.</summary>
        public static long FileSize(string efficiencyGuid, ResponseMatrixSource source)
        {
            if (string.IsNullOrEmpty(efficiencyGuid))
            {
                return 0;
            }

            string path = PathOf(efficiencyGuid, source);
            return File.Exists(path) ? new FileInfo(path).Length : 0;
        }

        /// <summary>
        /// Guid приходит из конфигурации, а она правится руками. Всё, что не
        /// буква, цифра, дефис или подчёркивание, заменяется: имя файла не место
        /// для доверия чужой строке.
        /// </summary>
        static string Sanitize(string guid)
        {
            if (string.IsNullOrEmpty(guid))
            {
                return "none";
            }

            var chars = guid.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                bool ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'z')
                          || (c >= 'A' && c <= 'Z') || c == '-' || c == '_';
                if (!ok)
                {
                    chars[i] = '_';
                }
            }

            return new string(chars);
        }
    }
}
