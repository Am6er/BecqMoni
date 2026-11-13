using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace BecquerelMonitor.EfficiencyMaker
{
    /// <summary>
    /// (`AMBER274`, этап Э2, 04.11.2026) СОБСТВЕННАЯ АКТИВНОСТЬ КРИСТАЛЛА — запись
    /// одного нуклида одного слоя: готовая гистограмма на сетке матрицы, отсчётов
    /// НА ОДИН РАСПАД.
    ///
    /// ⛔ Почему на распад, а не на Бк: амплитуда природных нуклидов считается из
    /// распространённости, периода `nucdb` и массы кристалла (решение Amber
    /// 29.10.2026 вопросником, дословно: «Природные по кристаллу (Рекомендую)»),
    /// а у свободной строки (Ac-227) она подбирается разбором. Общая у записей
    /// только форма — гистограмма распада целиком; множитель у каждой свой.
    ///
    /// ⛔ Почему гистограмма, а не строки: распад в объёме кристалла считается
    /// СОБЫТИЕМ ЦЕЛИКОМ (итог `AMBER273`, 29.10.2026): бета оседает почти вся,
    /// каскад суммируется, линия 788.7 гаснет до 3 %. Строками это не выразить.
    /// </summary>
    public sealed class CrystalOwnEntry
    {
        /// <summary>Имя нуклида как в `nucdb` (`138LA`, `176LU`, `227AC`).</summary>
        public string Nuclide = "";

        /// <summary>Слой: `beta` сейчас, `alpha` — этап Э3.</summary>
        public string Layer = "beta";

        /// <summary>
        /// Амплитуда СВОБОДНАЯ (строка FSA): так задан ряд Ac-227 — загрязнение своё
        /// у каждого кристалла (решение Amber 29.10.2026: «Ac-227 — свободной строкой
        /// FSA»). У природных — false, и тогда <see cref="ActivityBq"/> не 0.
        /// </summary>
        public bool Free;

        /// <summary>Активность нуклида в объёме кристалла, Бк (природные; у свободных 0).</summary>
        public double ActivityBq;

        /// <summary>Сколько историй разыграно — для отчёта и оценки шума гистограммы.</summary>
        public long Histories;

        /// <summary>Отсчётов на один распад в бине сетки матрицы (длина = число бинов).</summary>
        public double[] PerDecay;
    }

    /// <summary>
    /// (`AMBER274`, Э2) РАЗДЕЛ ФАЙЛА МАТРИЦЫ «СОБСТВЕННАЯ АКТИВНОСТЬ КРИСТАЛЛА».
    ///
    /// ⛔ Раздел ДОПИСЫВАЕТСЯ к готовой матрице отдельной хвостовой записью `CWNA`
    /// (payload, SHA-256, длина, `CWNE`) и НЕ меняет ни фотонное тело, ни формат:
    /// старые файлы читаются как прежде, старый код новый файл читает тоже — запись
    /// стоит ПЕРЕД `ELSC`, а `ELSC` ищется от конца файла. Поэтому `FormatVersion`
    /// не поднят: раздел необязательный, его отсутствие — не порча файла.
    ///
    /// Клеймо раздела своё (<see cref="Stamp"/>): вещество и размер кристалла, физика
    /// и сетка. Матрица, собранная для другой геометрии, раздел не отдаёт — как у
    /// раздела электрона (`ResponseElectronSection`), иначе чужая гистограмма молча
    /// ложится в разбор.
    /// </summary>
    public sealed class ResponseCrystalSection
    {
        public const string Magic = "CWNA";
        public const string EndMark = "CWNE";
        public const int Version = 1;

        /// <summary>Клеймо раздела: вещество кристалла, размер, физика, сетка.</summary>
        public string Stamp = "";

        /// <summary>Шаг сетки гистограммы, кэВ (тот же, что у матрицы).</summary>
        public double BinKev;

        /// <summary>Записи по нуклидам и слоям.</summary>
        public List<CrystalOwnEntry> Entries = new List<CrystalOwnEntry>();

        /// <summary>Сколько историй на нуклид задано при сборке (для отчёта).</summary>
        public long Histories;

        /// <summary>Чем раздел собран — строка отчёта, не влияет на числа.</summary>
        public string Note = "";

        public int Count
        {
            get { return this.Entries != null ? this.Entries.Count : 0; }
        }

        public CrystalOwnEntry Find(string nuclide, string layer)
        {
            if (this.Entries == null)
            {
                return null;
            }

            foreach (CrystalOwnEntry entry in this.Entries)
            {
                if (entry != null
                    && string.Equals(entry.Nuclide, nuclide, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(entry.Layer, layer ?? "beta", StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }

            return null;
        }

        /// <summary>
        /// Клеймо раздела для сцены: вещество кристалла, его плотность и размер,
        /// физика матрицы и шаг сетки, рецепт слоя умолчания. Сцена другой геометрии раздел
        /// не получит. Так зовёт разбор (<see cref="UsableFor"/>): ждёт раздел нынешнего рецепта.
        /// </summary>
        public static string ComputeStamp(GeometryModel geometry, double binKev)
        {
            return ComputeStamp(geometry, binKev, null);
        }

        /// <summary>
        /// (`AMBER274`, полоса `a274t` 13.11.2026) То же с РЕЦЕПТОМ, которым раздел собран
        /// (<paramref name="recipe"/>; null — рецепт умолчания). Поле `rcp=` — версия рецепта
        /// слоя и его рычаги (<see cref="FullSpectrumAnalysis.CrystalOwnActivityBuilder.RecipeStamp"/>):
        /// шкала гистограммы, осадок конверсии, L из EADL, релаксация захвата, части, подмены
        /// замера. Раздел прежнего рецепта (или без поля вовсе — до 13.11.2026) разбор не берёт,
        /// как раздел электрона с чужим `cl=`. Пока слой выключен
        /// (<see cref="FullSpectrumAnalysis.FsaAnalyzer.CrystalOwnLayer"/>), раздел никто не пишет
        /// и не читает — числа разбора от поля не зависят.
        /// </summary>
        public static string ComputeStamp(GeometryModel geometry, double binKev, BecquerelMonitor.FullSpectrumAnalysis.CrystalOwnParts recipe)
        {
            var sb = new StringBuilder();
            sb.Append("phys=").Append(ResponseMatrix.PhysicsVersion.ToString(CultureInfo.InvariantCulture));
            sb.Append(";bin=").Append(binKev.ToString("R", CultureInfo.InvariantCulture));
            if (geometry != null)
            {
                GeometryMaterial crystal = geometry.Crystal;
                sb.Append(";mat=").Append(crystal != null ? (crystal.Name ?? "") : "");
                sb.Append(";rho=").Append(crystal != null ? crystal.Density.ToString("R", CultureInfo.InvariantCulture) : "0");
                if (crystal != null && crystal.Fractions != null)
                {
                    var keys = new List<int>(crystal.Fractions.Keys);
                    keys.Sort();
                    foreach (int z in keys)
                    {
                        sb.Append(';').Append(z.ToString(CultureInfo.InvariantCulture))
                          .Append(':').Append(crystal.Fractions[z].ToString("R", CultureInfo.InvariantCulture));
                    }
                }

                sb.Append(";d=").Append(geometry.CrystalDiameter.ToString("R", CultureInfo.InvariantCulture));
                sb.Append(";h=").Append(geometry.CrystalHeight.ToString("R", CultureInfo.InvariantCulture));
                sb.Append(";shape=").Append(geometry.Shape.ToString());
                sb.Append(";bx=").Append(geometry.CrystalBoxX.ToString("R", CultureInfo.InvariantCulture));
                sb.Append(";by=").Append(geometry.CrystalBoxY.ToString("R", CultureInfo.InvariantCulture));
                sb.Append(";bz=").Append(geometry.CrystalBoxZ.ToString("R", CultureInfo.InvariantCulture));
            }

            // (`AMBER274`, `a274s`) Бета слоя с кулоновскими λ (`FsaBetaLibrary.CoulombLambda`):
            // поле только при ВКЛ, при ВЫКЛ клеймо побитно прежнее.
            if (BecquerelMonitor.FullSpectrumAnalysis.FsaBetaLibrary.CoulombLambda)
            {
                // (`a274t`) значение — версия формы: «1» точечное ядро, «2» заряженная сфера
                sb.Append(";cl=").Append(BecquerelMonitor.FullSpectrumAnalysis.FsaBetaLibrary.CoulombLambdaVersion);
            }

            // (`AMBER274`, `a274L`) Бета слоя с F₀L₀ заряженной сферы (`FsaBetaLibrary.FermiFiniteSize`):
            // поле только при ВКЛ, при ВЫКЛ клеймо побитно прежнее.
            if (BecquerelMonitor.FullSpectrumAnalysis.FsaBetaLibrary.FermiFiniteSize)
            {
                sb.Append(";l0=").Append(BecquerelMonitor.FullSpectrumAnalysis.FsaBetaLibrary.FermiFiniteSizeVersion);
            }

            sb.Append(";rcp=").Append(BecquerelMonitor.FullSpectrumAnalysis.CrystalOwnActivityBuilder.RecipeStamp(recipe));
            return sb.ToString();
        }

        /// <summary>(`a274t`) Поле клейма по имени (null — поля нет): разбору и пробе — назвать причину отказа.</summary>
        public static string StampField(string stamp, string name)
        {
            foreach (string part in (stamp ?? "").Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq > 0 && string.Equals(part.Substring(0, eq), name, StringComparison.Ordinal))
                {
                    return part.Substring(eq + 1);
                }
            }

            return null;
        }

        /// <summary>Совпадает ли раздел со сценой (null — раздел чужой).</summary>
        public bool UsableFor(GeometryModel geometry, double binKev)
        {
            return this.Count > 0
                   && string.Equals(this.Stamp, ComputeStamp(geometry, binKev), StringComparison.Ordinal);
        }

        // ------------------------------------------------------------------
        // Запись и чтение
        // ------------------------------------------------------------------

        internal static void WriteTrailer(BinaryWriter writer, ResponseCrystalSection section)
        {
            if (section == null || section.Count == 0)
            {
                return;
            }

            byte[] payload;
            using (var memory = new MemoryStream())
            using (var inner = new BinaryWriter(memory, Encoding.UTF8))
            {
                Write(inner, section);
                inner.Flush();
                payload = memory.ToArray();
            }

            byte[] digest;
            using (SHA256 sha = SHA256.Create())
            {
                digest = sha.ComputeHash(payload);
            }

            writer.Write(Encoding.ASCII.GetBytes(Magic));
            writer.Write(payload);
            writer.Write(digest);
            writer.Write((long)payload.Length);
            writer.Write(Encoding.ASCII.GetBytes(EndMark));
        }

        /// <summary>
        /// Раздел из хвоста файла. null — раздела нет (не ошибка: так у всех матриц
        /// без собственной активности). Повреждённый раздел — null и причина в
        /// <paramref name="damage"/>; тело матрицы при этом цело.
        /// </summary>
        internal static ResponseCrystalSection ReadTrailer(Stream stream, long bodyEnd, out string damage)
        {
            damage = null;
            try
            {
                long end = TrailerEnd(stream);
                const int Tail = 4 + 32 + 8 + 4;
                if (end - bodyEnd < Tail + 4)
                {
                    return null;                        // раздела нет — это норма
                }

                var reader = new BinaryReader(stream, Encoding.UTF8, true);
                stream.Position = end - 12;
                long payloadLength = reader.ReadInt64();
                if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != EndMark)
                {
                    return null;                        // хвост чужой — не наша забота
                }

                long payloadStart = end - 12 - 32 - payloadLength;
                if (payloadLength <= 0 || payloadStart - 4 < bodyEnd)
                {
                    damage = "CWNA: bad length " + payloadLength.ToString(CultureInfo.InvariantCulture);
                    return null;
                }

                stream.Position = payloadStart - 4;
                if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != Magic)
                {
                    damage = "CWNA: no start mark";
                    return null;
                }

                byte[] payload = reader.ReadBytes((int)payloadLength);
                byte[] stored = reader.ReadBytes(32);
                byte[] digest;
                using (SHA256 sha = SHA256.Create())
                {
                    digest = sha.ComputeHash(payload);
                }

                if (payload.Length != payloadLength || stored.Length != 32 || Hex(stored) != Hex(digest))
                {
                    damage = "CWNA: fingerprint mismatch";
                    return null;
                }

                using (var memory = new MemoryStream(payload, false))
                using (var inner = new BinaryReader(memory, Encoding.UTF8))
                {
                    ResponseCrystalSection section = Read(inner);
                    if (section == null)
                    {
                        damage = "CWNA: empty";
                        return null;
                    }

                    if (memory.Position != memory.Length)
                    {
                        damage = "CWNA: trailing bytes";
                        return null;
                    }

                    return section;
                }
            }
            catch (Exception error)
            {
                damage = "CWNA: " + error.Message;
                return null;
            }
        }

        /// <summary>
        /// Где кончается наша запись: конец файла, а если файл кончается разделом
        /// электрона `ELSC` — начало его метки (наш раздел пишется ПЕРЕД ним).
        /// </summary>
        static long TrailerEnd(Stream stream)
        {
            long length = stream.Length;
            if (length < 12)
            {
                return length;
            }

            var reader = new BinaryReader(stream, Encoding.UTF8, true);
            long save = stream.Position;
            try
            {
                stream.Position = length - 12;
                long payloadLength = reader.ReadInt64();
                if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "ELSE")
                {
                    return length;
                }

                long payloadStart = length - 12 - 32 - payloadLength;
                if (payloadLength <= 0 || payloadStart < 4)
                {
                    return length;
                }

                stream.Position = payloadStart - 4;
                return Encoding.ASCII.GetString(reader.ReadBytes(4)) == "ELSC" ? payloadStart - 4 : length;
            }
            catch (Exception)
            {
                return length;
            }
            finally
            {
                stream.Position = save;
            }
        }

        static void Write(BinaryWriter writer, ResponseCrystalSection section)
        {
            writer.Write(Version);
            writer.Write(section.Stamp ?? "");
            writer.Write(section.BinKev);
            writer.Write(section.Histories);
            writer.Write(section.Note ?? "");
            writer.Write(section.Entries.Count);
            foreach (CrystalOwnEntry entry in section.Entries)
            {
                writer.Write(entry.Nuclide ?? "");
                writer.Write(entry.Layer ?? "beta");
                writer.Write(entry.Free);
                writer.Write(entry.ActivityBq);
                writer.Write(entry.Histories);
                writer.Write(entry.PerDecay != null ? entry.PerDecay.Length : 0);
                if (entry.PerDecay != null)
                {
                    foreach (double value in entry.PerDecay)
                    {
                        writer.Write(value);
                    }
                }
            }
        }

        static ResponseCrystalSection Read(BinaryReader reader)
        {
            int version = reader.ReadInt32();
            if (version != Version)
            {
                throw new InvalidDataException("версия раздела " + version.ToString(CultureInfo.InvariantCulture));
            }

            var section = new ResponseCrystalSection
            {
                Stamp = reader.ReadString(),
                BinKev = reader.ReadDouble(),
                Histories = reader.ReadInt64(),
                Note = reader.ReadString(),
            };

            int count = reader.ReadInt32();
            if (count < 0 || count > 1024)
            {
                throw new InvalidDataException("число записей " + count.ToString(CultureInfo.InvariantCulture));
            }

            for (int i = 0; i < count; i++)
            {
                var entry = new CrystalOwnEntry
                {
                    Nuclide = reader.ReadString(),
                    Layer = reader.ReadString(),
                    Free = reader.ReadBoolean(),
                    ActivityBq = reader.ReadDouble(),
                    Histories = reader.ReadInt64(),
                };

                int bins = reader.ReadInt32();
                if (bins < 0 || bins > 1 << 22)
                {
                    throw new InvalidDataException("длина гистограммы " + bins.ToString(CultureInfo.InvariantCulture));
                }

                entry.PerDecay = new double[bins];
                for (int k = 0; k < bins; k++)
                {
                    entry.PerDecay[k] = reader.ReadDouble();
                }

                section.Entries.Add(entry);
            }

            return section;
        }

        static string Hex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
            {
                sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            }

            return sb.ToString();
        }
    }
}
