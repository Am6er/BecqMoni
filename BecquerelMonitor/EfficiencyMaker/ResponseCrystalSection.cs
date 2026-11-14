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

        /// <summary>Слой: `beta` (Э2); у ряда с альфами (Э3) — тоже `beta`: альфы лежат в той же записи, их часть — <see cref="AlphaPerDecay"/>.</summary>
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

        /// <summary>
        /// (`AMBER274` Э3, полоса `a274a` 14.11.2026) ЧАСТЬ <see cref="PerDecay"/> ОТ АЛЬФ — события альфа-распада
        /// (свет альфы с гашением плюс её каскад) на тот же один распад головы, на той же сетке. Уже входит в
        /// <see cref="PerDecay"/>, отдельно нужна счёту ряда по альфам
        /// (<see cref="FullSpectrumAnalysis.CrystalOwnActivityBuilder.CountByAlphas(CrystalOwnEntry, double, double[], double, System.Func{double, double}, System.Func{double, double}, double)"/>):
        /// окно счёта — там, где лежат альфы. null — альф в записи нет (запись этапа Э2).
        /// </summary>
        public double[] AlphaPerDecay;
    }

    /// <summary>
    /// (`AMBER274`, полоса `a274m` 14.11.2026) Почему разбор не получил раздел собственной активности кристалла —
    /// как <see cref="ElectronSectionRefusal"/> у раздела электрона. Отказ — только пять последних
    /// (<see cref="ResponseCrystalSection.IsRefusal"/>): кристалл слой иметь должен, а годного раздела нет.
    /// </summary>
    public enum CrystalSectionRefusal
    {
        /// <summary>Раздел годен и отдан разбору.</summary>
        None = 0,

        /// <summary>Матрица к разбору не привязана (умолчание анализатора) — судить нечего.</summary>
        NoMatrix,

        /// <summary>Геометрии у разбора нет — клеймо раздела сверить не с чем.</summary>
        NoScene,

        /// <summary>У кристалла сцены собственной активности нет (NaI, CsI, Ge) — разделу не место.</summary>
        NoOwnActivity,

        /// <summary>Раздела в матрице нет: посчитана без него.</summary>
        Missing,

        /// <summary>Раздел в файле был, но не прочитан (обрубок, отпечаток) или не собран.</summary>
        Damaged,

        /// <summary>Раздел другого кристалла (вещество, плотность, состав, размер) или другой сетки.</summary>
        OtherScene,

        /// <summary>Раздел другой физики: версия переноса или форма беты слоя (`cl`, `l0`, `scr`).</summary>
        OtherPhysics,

        /// <summary>Раздел другого рецепта слоя (`rcp`).</summary>
        OtherRecipe,

        /// <summary>Раздел посчитан по другим данным распада: отпечаток строк `nucdb` нуклидов слоя (`ndb`) не тот.</summary>
        OtherData,
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

        /// <summary>
        /// (`AMBER274` Э3, `a274a`) Версия записи с частью альф (<see cref="CrystalOwnEntry.AlphaPerDecay"/>). Пишется
        /// ТОЛЬКО когда у раздела есть альфы: раздел без альф пишется версией 1 побайтно, как прежде; чтение
        /// понимает обе.
        /// </summary>
        public const int VersionAlpha = 2;

        /// <summary>(`a274a`) Есть ли у раздела записи с частью альф.</summary>
        public bool HasAlpha
        {
            get
            {
                if (this.Entries != null)
                {
                    foreach (CrystalOwnEntry entry in this.Entries)
                    {
                        if (entry != null && entry.AlphaPerDecay != null)
                        {
                            return true;
                        }
                    }
                }

                return false;
            }
        }

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
                // ⛔ (`a274m` 14.11.2026) ТОЧНОСТЬ ЧИСЕЛ КЛЕЙМА — ТА ЖЕ, ЧТО У ТЕКСТА ГЕОМЕТРИИ (`GeometryWriter`: доли
                // состава `G6`, плотность и размеры `G8`), а не «R». Клеймо матрицы стоит на тексте геометрии, и сцена,
                // прошедшая через файл `.in`, матрице годна; раздел слоя с долями «R» той же сцене был НЕ годен:
                // 0.633124 из файла против 0.63312445313046661 из библиотеки веществ. Поймано стендом ключа
                // `CorpusFsaProbe --crystal-own=1` (раздел посчитан пробой склада из `.in`, спектр несёт геометрию
                // из библиотеки) — до причины отказа раздел в таком случае снимался молча. Разделов прежнего вида
                // клейма не писалось (слой выключен, склад раздела не имел).
                sb.Append(";rho=").Append(crystal != null ? crystal.Density.ToString("G8", CultureInfo.InvariantCulture) : "0");
                if (crystal != null && crystal.Fractions != null)
                {
                    var keys = new List<int>(crystal.Fractions.Keys);
                    keys.Sort();
                    foreach (int z in keys)
                    {
                        sb.Append(';').Append(z.ToString(CultureInfo.InvariantCulture))
                          .Append(':').Append(crystal.Fractions[z].ToString("G6", CultureInfo.InvariantCulture));
                    }
                }

                sb.Append(";d=").Append(geometry.CrystalDiameter.ToString("G8", CultureInfo.InvariantCulture));
                sb.Append(";h=").Append(geometry.CrystalHeight.ToString("G8", CultureInfo.InvariantCulture));
                sb.Append(";shape=").Append(geometry.Shape.ToString());
                sb.Append(";bx=").Append(geometry.CrystalBoxX.ToString("G8", CultureInfo.InvariantCulture));
                sb.Append(";by=").Append(geometry.CrystalBoxY.ToString("G8", CultureInfo.InvariantCulture));
                sb.Append(";bz=").Append(geometry.CrystalBoxZ.ToString("G8", CultureInfo.InvariantCulture));
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

            // (`AMBER274`, `a274s`) Бета слоя с экранированием в функции Ферми (`FsaBetaLibrary.FermiScreening`):
            // поле только при ВКЛ, при ВЫКЛ клеймо побитно прежнее.
            if (BecquerelMonitor.FullSpectrumAnalysis.FsaBetaLibrary.FermiScreening)
            {
                sb.Append(";scr=").Append(BecquerelMonitor.FullSpectrumAnalysis.FsaBetaLibrary.FermiScreeningVersion);
            }

            // (`AMBER274`, предложение `a274n` (в), полоса `a274m` 14.11.2026) ДАННЫЕ РАСПАДА слоя — отпечатком строк
            // `nucdb` нуклидов, которые слой сцены читает: правка базы (выход ветви, период, распространённость) раздел
            // отвергает, как правка формы беты. Считается только там, где клеймо раздела вообще нужно (сборка раздела и
            // сверка матрицы, несущей раздел): у матриц без раздела — ни одного чтения базы.
            if (geometry != null)
            {
                sb.Append(";ndb=").Append(BecquerelMonitor.FullSpectrumAnalysis.CrystalOwnActivityBuilder.DecayDataFingerprint(geometry));
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

        /// <summary>
        /// (`AMBER274`, полоса `a274m` 14.11.2026) ПОЧЕМУ раздел не годен сцене — причиной, а не молчаливым null:
        /// клеймо раздела сверяется с ожидаемым по полям. Чужой кристалл (вещество, плотность, состав, размер,
        /// шаг сетки) — <see cref="CrystalSectionRefusal.OtherScene"/>; физика матрицы или форма беты слоя
        /// (`phys`, `cl`, `l0`, `scr`) — <see cref="CrystalSectionRefusal.OtherPhysics"/>; данные распада (`ndb`) —
        /// <see cref="CrystalSectionRefusal.OtherData"/>; рецепт слоя (`rcp`) —
        /// <see cref="CrystalSectionRefusal.OtherRecipe"/>. Разошлось несколько — называется старшая причина
        /// (сцена, физика, данные, рецепт), а <paramref name="detail"/> перечисляет все поля: «поле: в разделе «…», нужно «…»».
        /// Годен — <see cref="CrystalSectionRefusal.None"/> и null. Решение то же, что у <see cref="UsableFor"/>.
        /// </summary>
        public CrystalSectionRefusal Judge(GeometryModel geometry, double binKev, out string detail)
        {
            detail = null;
            if (this.Count == 0)
            {
                return CrystalSectionRefusal.Missing;
            }

            string expected = ComputeStamp(geometry, binKev);
            if (string.Equals(this.Stamp, expected, StringComparison.Ordinal))
            {
                return CrystalSectionRefusal.None;
            }

            SortedDictionary<string, string> have = StampFields(this.Stamp);
            SortedDictionary<string, string> need = StampFields(expected);
            var names = new SortedSet<string>(have.Keys, StringComparer.Ordinal);
            names.UnionWith(need.Keys);
            var differ = new List<string>();
            bool scene = false, physics = false, recipe = false, data = false;
            foreach (string name in names)
            {
                string a, b;
                have.TryGetValue(name, out a);
                need.TryGetValue(name, out b);
                if (string.Equals(a, b, StringComparison.Ordinal))
                {
                    continue;
                }

                differ.Add(name + ": в разделе " + (a == null ? "нет" : "«" + a + "»") + ", нужно " + (b == null ? "нет" : "«" + b + "»"));
                if (name == "rcp")
                {
                    recipe = true;
                }
                else if (name == "phys" || name == "cl" || name == "l0" || name == "scr")
                {
                    physics = true;
                }
                else if (name == "ndb")
                {
                    data = true;
                }
                else
                {
                    scene = true;
                }
            }

            // клейма разные, а поля одинаковые (порядок, повтор поля) — раздел всё равно не годен
            detail = differ.Count > 0 ? string.Join("; ", differ.ToArray()) : "клеймо раздела не совпало с клеймом сцены";
            return scene || differ.Count == 0 ? CrystalSectionRefusal.OtherScene
                 : physics ? CrystalSectionRefusal.OtherPhysics
                 : data ? CrystalSectionRefusal.OtherData
                 : recipe ? CrystalSectionRefusal.OtherRecipe
                 : CrystalSectionRefusal.OtherScene;
        }

        /// <summary>(`a274m`) Поля клейма по именам; доля элемента состава (`57:0.3485`) — полем `Z57`.</summary>
        static SortedDictionary<string, string> StampFields(string stamp)
        {
            var fields = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (string part in (stamp ?? "").Split(';'))
            {
                if (part.Length == 0)
                {
                    continue;
                }

                int eq = part.IndexOf('=');
                int colon = part.IndexOf(':');
                string name, value;
                if (eq > 0)
                {
                    name = part.Substring(0, eq);
                    value = part.Substring(eq + 1);
                }
                else if (colon > 0)
                {
                    name = "Z" + part.Substring(0, colon);
                    value = part.Substring(colon + 1);
                }
                else
                {
                    name = part;
                    value = "";
                }

                // повтор имени — признак порчи: значение склеивается, и сверка его увидит
                fields[name] = fields.ContainsKey(name) ? fields[name] + "|" + value : value;
            }

            return fields;
        }

        /// <summary>(`a274m`) Отказ это или нет: «у кристалла слоя нет», «сцены нет», «матрицы нет» — не отказ, разделу там не место.</summary>
        public static bool IsRefusal(CrystalSectionRefusal refusal)
        {
            return refusal == CrystalSectionRefusal.Missing || refusal == CrystalSectionRefusal.Damaged
                   || refusal == CrystalSectionRefusal.OtherScene || refusal == CrystalSectionRefusal.OtherPhysics
                   || refusal == CrystalSectionRefusal.OtherRecipe || refusal == CrystalSectionRefusal.OtherData;
        }

        /// <summary>(`a274m`) Причина словами — в примечание слоя разбора и в отчёт проб.</summary>
        public static string RefusalText(CrystalSectionRefusal refusal, string detail)
        {
            string head;
            switch (refusal)
            {
                case CrystalSectionRefusal.None: return "раздел годен";
                case CrystalSectionRefusal.NoMatrix: return "матрица не привязана";
                case CrystalSectionRefusal.NoScene: return "у разбора нет геометрии — раздел сверить не с чем";
                case CrystalSectionRefusal.NoOwnActivity: return "у кристалла сцены собственной активности нет";
                case CrystalSectionRefusal.Missing: head = "в матрице нет раздела слоя (матрица посчитана без него)"; break;
                case CrystalSectionRefusal.Damaged: head = "раздел слоя в файле матрицы испорчен или не собран"; break;
                case CrystalSectionRefusal.OtherScene: head = "раздел слоя посчитан для другого кристалла или сетки"; break;
                case CrystalSectionRefusal.OtherPhysics: head = "раздел слоя посчитан другой физикой (версия переноса или форма беты)"; break;
                case CrystalSectionRefusal.OtherRecipe: head = "раздел слоя посчитан другим рецептом слоя"; break;
                case CrystalSectionRefusal.OtherData: head = "раздел слоя посчитан по другим данным распада (nucdb изменилась)"; break;
                default: head = refusal.ToString(); break;
            }

            return string.IsNullOrEmpty(detail) ? head : head + " — " + detail;
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
            bool alpha = section.HasAlpha;
            writer.Write(alpha ? VersionAlpha : Version);
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

                if (alpha)
                {
                    // (`a274a`) часть альф: длина (0 — альф в записи нет) и значения
                    writer.Write(entry.AlphaPerDecay != null ? entry.AlphaPerDecay.Length : 0);
                    if (entry.AlphaPerDecay != null)
                    {
                        foreach (double value in entry.AlphaPerDecay)
                        {
                            writer.Write(value);
                        }
                    }
                }
            }
        }

        static ResponseCrystalSection Read(BinaryReader reader)
        {
            int version = reader.ReadInt32();
            if (version != Version && version != VersionAlpha)
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

                if (version == VersionAlpha)
                {
                    int alphaBins = reader.ReadInt32();
                    if (alphaBins < 0 || alphaBins > 1 << 22)
                    {
                        throw new InvalidDataException("длина части альф " + alphaBins.ToString(CultureInfo.InvariantCulture));
                    }

                    if (alphaBins > 0)
                    {
                        entry.AlphaPerDecay = new double[alphaBins];
                        for (int k = 0; k < alphaBins; k++)
                        {
                            entry.AlphaPerDecay[k] = reader.ReadDouble();
                        }
                    }
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
